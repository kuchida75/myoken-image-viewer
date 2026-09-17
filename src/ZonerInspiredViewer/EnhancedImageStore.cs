using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMagick;

namespace ZonerInspiredViewer
{
    internal sealed class EnhancedSaveRequest
    {
        internal string SourcePath, Destination;
        internal FileRevision SourceRevision, DestinationRevision;
        internal BitmapSource Pixels;
        internal Effect QuickEffect;
        internal ManualAdjustments Manual;
        internal int Rotation;
        internal bool AllowOverwrite;
    }

    internal sealed class EnhancedSaveResult
    {
        internal string Path, Backup;
        internal FileRevision Revision;
    }

    internal static class EnhancedImageStore
    {
        internal const string Filter = "PNG (lossless)|*.png|JPEG|*.jpg;*.jpeg|WebP (lossless)|*.webp|AVIF|*.avif|JPEG XL (lossless)|*.jxl";
        internal static string BackupFolder(string path) { return Path.Combine(Path.GetDirectoryName(path), ".viewer-enhance-backups"); }

        internal static MagickFormat Format(string path)
        {
            switch (System.IO.Path.GetExtension(path ?? "").ToLowerInvariant())
            {
                case ".jpg": case ".jpeg": return MagickFormat.Jpeg;
                case ".png": return MagickFormat.Png;
                case ".webp": return MagickFormat.WebP;
                case ".avif": return MagickFormat.Avif;
                case ".jxl": return MagickFormat.Jxl;
                case ".heic": case ".heif": return MagickFormat.Heic;
                case ".gif": return MagickFormat.Gif;
                default: throw new NotSupportedException("Choose PNG, JPEG, WebP, AVIF or JPEG XL for Save As.");
            }
        }

        internal static Task<EnhancedSaveResult> SaveAsync(EnhancedSaveRequest request, CancellationToken token, Action<double> progress)
        {
            var completion = new TaskCompletionSource<EnhancedSaveResult>();
            var worker = new Thread(delegate()
            {
                try { completion.SetResult(Save(request, token, progress)); }
                catch (OperationCanceledException) { completion.SetCanceled(); }
                catch (Exception error) { completion.SetException(error); }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            worker.IsBackground = true; worker.SetApartmentState(ApartmentState.STA); worker.Start();
            return completion.Task;
        }

        private static byte[] Digest(Stream stream)
        { stream.Position = 0; using (var sha = SHA256.Create()) return sha.ComputeHash(stream); }

        private static void VerifyWritable(string path)
        {
            if (File.Exists(path) && (File.GetAttributes(path) & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("Read-only files and file links cannot be overwritten. Use Save As in a writable folder.");
            if (Directory.Exists(path)) throw new IOException("The destination is a folder, not an image file.");
        }

        private static EnhancedSaveResult Save(EnhancedSaveRequest request, CancellationToken token, Action<double> progress)
        {
            string source = Path.GetFullPath(request.SourcePath), destination = Path.GetFullPath(request.Destination);
            bool same = String.Equals(source, destination, StringComparison.OrdinalIgnoreCase);
            MagickFormat format = Format(destination);
            ModernImageDecoder.Initialize(); token.ThrowIfCancellationRequested();
            if (!MagickNET.SupportedFormats.Any(f => f.Format == format && f.SupportsWriting))
                throw new NotSupportedException("This encoder is unavailable. Use Save As with PNG, JPEG or WebP.");
            if (!ManualAdjustments.IsValid(request.Manual)) throw new InvalidDataException("Invalid Enhance values.");
            var gates = new List<SharedFileGate>();
            string temporary = Path.Combine(Path.GetDirectoryName(destination), ".viewer-enhance-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                foreach (string path in new[] { source, destination }.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    gates.Add(new SharedFileGate(path));
                if (!request.SourceRevision.Matches(source)) throw new IOException("The source changed since it was displayed. Reopen it before saving.");
                VerifyWritable(destination);
                bool exists = File.Exists(destination);
                if (exists && (!request.AllowOverwrite || !request.DestinationRevision.Matches(destination)))
                    throw new IOException("The destination changed or overwrite was not confirmed. Choose Save As again.");
                if (!exists && request.DestinationRevision.Length >= 0) throw new IOException("The destination changed. Choose Save As again.");
                byte[] targetDigest = null, sourceDigest;
                if (exists && !same) using (var target = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read)) targetDigest = Digest(target);
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    sourceDigest = Digest(input); input.Position = 0;
                    using (var headers = new MagickImageCollection())
                    {
                        headers.Ping(input, new MagickReadSettings { Format = Format(source), FrameCount = 2 });
                        if (headers.Count == 0) throw new InvalidDataException("The original image header is unavailable.");
                        if (same && (headers.Count != 1 || headers[0].Format != Format(source)))
                            throw new NotSupportedException("Animations, multi-image files and mismatched file extensions cannot be overwritten. Save As exports the displayed frame.");
                        if (same && Format(source) == MagickFormat.Png)
                        {
                            input.Position = 0;
                            if (ImageRotationService.IsAnimatedPng(input)) throw new NotSupportedException("Animated PNG cannot be overwritten. Use Save As for a still image.");
                        }
                        BitmapSource rendered = EnhancedImageRenderer.Render(request.Pixels, request.QuickEffect, request.Manual,
                            request.Rotation, token, p => { if (progress != null) progress(p * 0.8); });
                        var straight = new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0);
                        int stride = checked(rendered.PixelWidth * 4); byte[] bytes = new byte[checked(stride * rendered.PixelHeight)];
                        straight.CopyPixels(bytes, stride, 0);
                        using (var output = new MagickImage())
                        {
                            output.ReadPixels(bytes, new PixelReadSettings((uint)rendered.PixelWidth, (uint)rendered.PixelHeight, StorageType.Char, PixelMapping.BGRA));
                            output.ColorSpace = ColorSpace.sRGB; output.SetProfile(ColorProfiles.SRGB); output.Depth = 8;
                            output.Density = headers[0].Density;
                            var exif = headers[0].GetExifProfile();
                            if (exif != null)
                            {
                                exif.RemoveThumbnail(); exif.SetValue(ExifTag.Orientation, (ushort)1);
                                exif.SetValue(ExifTag.PixelXDimension, new Number((uint)rendered.PixelWidth)); exif.SetValue(ExifTag.PixelYDimension, new Number((uint)rendered.PixelHeight));
                                exif.SetValue(ExifTag.ImageWidth, new Number((uint)rendered.PixelWidth)); exif.SetValue(ExifTag.ImageLength, new Number((uint)rendered.PixelHeight));
                                exif.SetValue(ExifTag.ColorSpace, (ushort)1); output.SetProfile(exif);
                            }
                            var iptc = headers[0].GetIptcProfile(); if (iptc != null) output.SetProfile(iptc);
                            output.Orientation = OrientationType.TopLeft;
                            output.Quality = format == MagickFormat.WebP || format == MagickFormat.Jxl ? 100u : 95u;
                            if (format == MagickFormat.WebP) output.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
                            if (format == MagickFormat.Jpeg) { output.BackgroundColor = MagickColors.White; output.Alpha(AlphaOption.Remove); }
                            token.ThrowIfCancellationRequested();
                            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            { output.Write(stream, format); stream.Flush(true); }
                        }
                        token.ThrowIfCancellationRequested();
                        using (var verify = new MagickImage())
                        using (var stream = File.OpenRead(temporary))
                        {
                            verify.Read(stream, new MagickReadSettings { Format = format });
                            if (verify.Width != rendered.PixelWidth || verify.Height != rendered.PixelHeight)
                                throw new IOException("The saved image could not be verified.");
                        }
                    }
                }
                token.ThrowIfCancellationRequested();
                if (!request.SourceRevision.Matches(source)) throw new IOException("The source changed during saving. No file was overwritten.");
                using (var check = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    if (!sourceDigest.SequenceEqual(Digest(check))) throw new IOException("The source content changed during saving.");
                string backup = null;
                if (exists)
                {
                    VerifyWritable(destination);
                    if (!request.DestinationRevision.Matches(destination)) throw new IOException("The destination changed during saving. It was not overwritten.");
                    string folder = BackupFolder(destination); Directory.CreateDirectory(folder);
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("The backup directory cannot be a link.");
                    string name = Path.GetFileName(destination);
                    backup = Path.Combine(folder, name.Substring(0, Math.Min(80, name.Length)) + "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "." + Guid.NewGuid().ToString("N") + ".bak");
                    using (var current = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    {
                        if (!(same ? sourceDigest : targetDigest).SequenceEqual(Digest(current))) throw new IOException("The destination content changed during saving.");
                        token.ThrowIfCancellationRequested(); File.Replace(temporary, destination, backup, false);
                    }
                }
                else { token.ThrowIfCancellationRequested(); File.Move(temporary, destination); }
                if (progress != null) progress(1);
                return new EnhancedSaveResult { Path = destination, Backup = backup, Revision = FileRevision.Read(destination) };
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                for (int i = gates.Count - 1; i >= 0; i--) gates[i].Dispose();
            }
        }
    }
}
