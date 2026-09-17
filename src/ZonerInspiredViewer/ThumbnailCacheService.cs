using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed class ThumbnailCacheService
    {
        private readonly DecoderRegistry _decoders;
        private readonly GpuThumbnailProcessor _gpu;
        private readonly string _cacheRoot;
        private readonly SemaphoreSlim _parallelism;
        private readonly SemaphoreSlim _folderParallelism = new SemaphoreSlim(4, 4);
        private readonly SemaphoreSlim _fallbackParallelism = new SemaphoreSlim(2, 2);
        private readonly ReaderWriterLockSlim _maintenance = new ReaderWriterLockSlim();

        public ThumbnailCacheService(DecoderRegistry decoders, string cacheRoot, int workerCount, GpuThumbnailProcessor gpu = null)
        {
            _decoders = decoders;
            _gpu = gpu;
            _cacheRoot = cacheRoot;
            _parallelism = new SemaphoreSlim(Math.Max(2, workerCount), Math.Max(2, workerCount));
            Directory.CreateDirectory(_cacheRoot);
        }

        public Task<BitmapSource> GetThumbnailAsync(
            ImageFileItem item,
            int thumbnailPixelSize,
            CancellationToken cancellationToken)
        {
            thumbnailPixelSize = Math.Max(64, Math.Min(2048, thumbnailPixelSize));
            return Task.Factory.StartNew(
                delegate
                {
                    _parallelism.Wait(cancellationToken);
                    _maintenance.EnterReadLock();
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string cachePath = GetCachePath(item, thumbnailPixelSize);
                        if (File.Exists(cachePath))
                        {
                            try { return LoadBitmap(cachePath, 0, cancellationToken); }
                            catch (IOException) { }
                            catch (NotSupportedException) { }
                            catch (FileFormatException) { }
                            catch (UnauthorizedAccessException) { }
                        }

                        BitmapSource source = DecodeThumbnail(item.Path, thumbnailPixelSize, cancellationToken);
                        SaveJpeg(cachePath, source);
                        return source;
                    }
                    finally
                    {
                        _maintenance.ExitReadLock();
                        _parallelism.Release();
                    }
                },
                cancellationToken,
                TaskCreationOptions.None,
                TaskScheduler.Default);
        }

        private BitmapSource DecodeThumbnail(string path, int size, CancellationToken token)
        {
            return _gpu == null ? DecodeThumbnailCpu(path, size, token)
                : _gpu.Process(path, size, pixels => DecodeThumbnailCpu(path, pixels, token), token);
        }

        private BitmapSource DecodeThumbnailCpu(string path, int size, CancellationToken token)
        {
            try { return Resize(_decoders.Decode(path, size, token), size); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                if (!(error is NotSupportedException || error is System.IO.FileFormatException
                    || error is ArgumentException || error is System.Runtime.InteropServices.COMException)) throw;
            }
            // A few codecs can decode the full image but reject scaled decoding. Bound that fallback's memory load.
            _fallbackParallelism.Wait(token);
            try { return Resize(_decoders.Decode(path, 0, token), size); }
            finally { _fallbackParallelism.Release(); }
        }

        private static BitmapSource Resize(BitmapSource source, int size)
        {
            double scale = Math.Min(1, (double)size / Math.Max(source.PixelWidth, source.PixelHeight));
            if (scale >= 1) return source;
            var bitmap = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            int stride = (bitmap.PixelWidth * bitmap.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
            var detached = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, bitmap.DpiX, bitmap.DpiY,
                bitmap.Format, bitmap.Palette, pixels, stride);
            detached.Freeze(); return detached;
        }

        public Task<ThumbnailRebuildProgress> RebuildFolderAsync(string folder, int size,
            Action<ThumbnailRebuildProgress> progress, CancellationToken token)
        {
            size = Math.Max(64, Math.Min(2048, size));
            return Task.Run(delegate
            {
                int completed = 0, failed = 0;
                string lastError = null;
                object gate = new object(); var watch = Stopwatch.StartNew();
                Action<string, int[]> rebuild = delegate(string path, int[] sizes)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        _parallelism.Wait(token);
                        _maintenance.EnterReadLock();
                        try
                        {
                            ImageFileItem item = ImageFileItem.FromPath(path);
                            BitmapSource source = DecodeThumbnail(path, sizes.Max(), token);
                            foreach (int pixels in sizes.Distinct())
                            {
                                token.ThrowIfCancellationRequested();
                                SaveJpeg(GetCachePath(item, pixels), Resize(source, pixels), true);
                            }
                        }
                        finally { _maintenance.ExitReadLock(); _parallelism.Release(); }
                        Interlocked.Increment(ref completed);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) { Interlocked.Increment(ref failed); lock (gate) lastError = Path.GetFileName(path) + ": " + error.Message; }
                    lock (gate)
                    {
                        if (progress != null && watch.ElapsedMilliseconds >= 150)
                        {
                            progress(new ThumbnailRebuildProgress { Completed = completed, Failed = failed, LastError = lastError }); watch.Restart();
                        }
                    }
                };
                Parallel.ForEach(Directory.EnumerateFileSystemEntries(folder),
                    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token }, delegate(string path)
                    {
                        if (Directory.Exists(path))
                        {
                            // Refresh the bounded folder mosaic candidates, not the entire descendant tree.
                            try
                            {
                                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
                                foreach (string child in Directory.EnumerateFiles(path).Where(ImageExtensions.IsBrowsableImage).Take(32))
                                    rebuild(child, new[] { Math.Max(64, (size + 1) / 2), 128 });
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception error) { Interlocked.Increment(ref failed); lock (gate) lastError = error.Message; }
                        }
                        else if (ImageExtensions.IsBrowsableImage(path)) rebuild(path, new[] { size, 64, 256 });
                    });
                return new ThumbnailRebuildProgress { Completed = completed, Failed = failed, LastError = lastError };
            }, token);
        }

        public Task<BitmapSource[]> GetFolderThumbnailsAsync(string folder, int thumbnailPixelSize,
            CancellationToken cancellationToken)
        {
            return Task.Run(async delegate
            {
                await _folderParallelism.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var images = new List<BitmapSource>(4);
                    int attempted = 0;
                    foreach (string path in Directory.EnumerateFiles(folder))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!ImageExtensions.IsBrowsableImage(path)) continue;
                        try
                        {
                            BitmapSource bitmap = await GetThumbnailAsync(ImageFileItem.FromPath(path),
                                thumbnailPixelSize, cancellationToken).ConfigureAwait(false);
                            images.Add(bitmap);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception) { }
                        // Bound decoder attempts when a folder contains corrupt files or unsupported codecs.
                        if (images.Count == 4 || ++attempted == 32) break;
                    }
                    return images.ToArray();
                }
                finally { _folderParallelism.Release(); }
            }, cancellationToken);
        }

        private static BitmapSource LoadBitmap(string path, int decodePixelWidth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = new BitmapImage();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            cancellationToken.ThrowIfCancellationRequested();
            return image;
        }

        private static void SaveJpeg(string path, BitmapSource source, bool required = false)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var encoder = new JpegBitmapEncoder();
                encoder.QualityLevel = 82;
                encoder.Frames.Add(BitmapFrame.Create(source));
                using (var stream = File.Create(temporary))
                {
                    encoder.Save(stream);
                }
                using (new SharedFileGate(path))
                {
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
            }
            catch
            {
                if (required) throw;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private string GetCachePath(ImageFileItem item, int thumbnailPixelSize)
        {
            string key = item.Path.ToUpperInvariant()
                + "|" + item.LastWriteUtc.Ticks.ToString()
                + "|" + item.Length.ToString();
            string hash;
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(key));
                var builder = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }

                hash = builder.ToString();
            }

            return Path.Combine(
                _cacheRoot,
                "s" + thumbnailPixelSize.ToString(),
                hash.Substring(0, 2),
                hash + ".jpg");
        }

        internal void Clear()
        {
            _maintenance.EnterWriteLock();
            try { ProfileMaintenance.ClearDirectory(_cacheRoot); }
            finally { _maintenance.ExitWriteLock(); }
        }

        internal void Invalidate(IEnumerable<string> paths)
        {
            _maintenance.EnterWriteLock();
            try
            {
                int[] sizes = Directory.EnumerateDirectories(_cacheRoot, "s*").Select(path =>
                { int size; return Int32.TryParse(Path.GetFileName(path).Substring(1), out size) ? size : 0; }).Where(size => size >= 64 && size <= 2048).ToArray();
                foreach (string path in InvalidationFiles(paths))
                {
                    if (!File.Exists(path)) continue;
                    ImageFileItem item = ImageFileItem.FromPath(path);
                    foreach (int size in sizes)
                    {
                        string cached = GetCachePath(item, size); ProfileMaintenance.CheckPath(_cacheRoot, cached);
                        if (File.Exists(cached)) File.Delete(cached);
                    }
                }
            }
            finally { _maintenance.ExitWriteLock(); }
        }

        private static IEnumerable<string> InvalidationFiles(IEnumerable<string> paths)
        {
            var pending = new Stack<string>(FileTransferService.TopLevelSources(paths));
            while (pending.Count > 0)
            {
                string path = pending.Pop();
                if (!File.Exists(path) && !Directory.Exists(path)) continue;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                if (Directory.Exists(path)) foreach (string child in Directory.EnumerateFileSystemEntries(path)) pending.Push(child);
                else if (ImageExtensions.IsBrowsableImage(path)) yield return path;
            }
        }
    }

    internal sealed class ThumbnailRebuildProgress
    {
        public int Completed, Failed;
        public string LastError;
        public override string ToString() { return Completed + (Completed == 1 ? " image rebuilt" : " images rebuilt") + (Failed > 0 ? ", " + Failed + " failed" : ""); }
    }
}
