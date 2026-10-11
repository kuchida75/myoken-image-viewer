using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;

namespace ZonerInspiredViewer
{
    internal static class ExifOrientationBatch
    {
        private const long PixelLimit = 128L * 1024 * 1024;
        private static readonly HashSet<string> ExcludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".viewer-rotation-backups", ".viewer-enhance-backups", ".viewer-orientation-backups", ".zen-batch-backups" };

        internal static List<BatchItem> Plan(string[] selected, CancellationToken token)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folders = new Stack<string>();
            foreach (string selectedPath in FileTransferService.TopLevelSources(selected))
            {
                token.ThrowIfCancellationRequested();
                string path = Path.GetFullPath(selectedPath);
                if (Directory.Exists(path)) folders.Push(path);
                else if (File.Exists(path) && ImageExtensions.IsBrowsableImage(path)) paths.Add(path);
            }
            while (folders.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string folder = folders.Pop();
                FileTransferService.CheckAncestors(folder);
                foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    token.ThrowIfCancellationRequested();
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!ExcludedFolders.Contains(Path.GetFileName(entry))) folders.Push(entry);
                    }
                    else if (ImageExtensions.IsBrowsableImage(entry)) paths.Add(entry);
                }
            }
            string[] ordered = paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            var rows = new BatchItem[ordered.Length];
            ModernImageDecoder.Initialize();
            Parallel.For(0, ordered.Length, new ParallelOptions
            { MaxDegreeOfParallelism = Math.Max(1, Math.Min(12, Environment.ProcessorCount - 2)), CancellationToken = token }, index =>
            {
                var item = new BatchItem { Source = ordered[index], Destination = ordered[index], Status = "Checking EXIF", Ready = false };
                rows[index] = item;
                try { Inspect(item, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { item.Status = "Skipped: " + error.Message; }
            });
            return rows.ToList();
        }

        private static void Inspect(BatchItem item, CancellationToken token)
        {
            FileTransferService.CheckAncestors(item.Source);
            if ((File.GetAttributes(item.Source) & FileAttributes.ReadOnly) != 0)
            { item.Status = "Skipped: read-only"; return; }
            MagickFormat format = EnhancedImageStore.Format(item.Source);
            using (var stream = File.OpenRead(item.Source))
            using (var images = new MagickImageCollection())
            {
                images.Ping(stream, new MagickReadSettings { Format = format, FrameCount = 2 });
                token.ThrowIfCancellationRequested();
                if (images.Count != 1) { item.Status = "Skipped: animation or multiple frames"; return; }
                if (format == MagickFormat.Png)
                {
                    stream.Position = 0;
                    if (ImageRotationService.IsAnimatedPng(stream)) { item.Status = "Skipped: animated PNG"; return; }
                }
                var image = images[0];
                if (image.Format != format) { item.Status = "Skipped: extension does not match image format"; return; }
                var exif = image.GetExifProfile();
                var orientation = exif == null ? null : exif.GetValue(ExifTag.Orientation);
                int value = orientation == null ? 0 : orientation.Value;
                if (value < 2 || value > 8) { item.Status = "Unchanged: no EXIF rotation needed"; return; }
                if (!MagickNET.SupportedFormats.Any(supported => supported.Format == format && supported.SupportsWriting))
                { item.Status = "Skipped: source format is decode-only"; return; }
                if (image.Width < 1 || image.Height < 1 || (long)image.Width * image.Height > PixelLimit)
                { item.Status = "Skipped: input exceeds 128 megapixels"; return; }
                item.Width = (int)image.Width; item.Height = (int)image.Height;
                item.OutputWidth = value >= 5 ? item.Height : item.Width;
                item.OutputHeight = value >= 5 ? item.Width : item.Height;
                item.ExifOrientation = value;
                item.Revision = FileRevision.Read(item.Source);
                item.Dimensions = item.Width + " x " + item.Height + " -> " + item.OutputWidth + " x " + item.OutputHeight;
                item.Status = "Ready: EXIF orientation " + value;
                item.Ready = true;
            }
        }

        internal static string Save(BatchItem item, CancellationToken token)
        {
            string path = item.Source;
            MagickFormat format = EnhancedImageStore.Format(path);
            string folder = Path.GetDirectoryName(path);
            string temporary = Path.Combine(folder, ".viewer-orientation-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (new SharedFileGate(path))
            {
                token.ThrowIfCancellationRequested();
                FileTransferService.CheckAncestors(path);
                if (!item.Revision.Matches(path)) throw new IOException("Source changed since preview; preview again.");
                if ((File.GetAttributes(path) & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0)
                    throw new IOException("Read-only files and file links cannot be overwritten.");
                byte[] digest;
                DateTime modified = File.GetLastWriteTimeUtc(path);
                try
                {
                    using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var image = new MagickImage())
                    {
                        using (var sha = SHA256.Create()) digest = sha.ComputeHash(input);
                        input.Position = 0;
                        var check = new BatchItem { Source = path };
                        Inspect(check, token);
                        if (!check.Ready || check.ExifOrientation != item.ExifOrientation || check.Width != item.Width || check.Height != item.Height)
                            throw new IOException("Image orientation or dimensions changed since preview.");
                        input.Position = 0;
                        image.Read(input, new MagickReadSettings { Format = format, FrameCount = 1 });
                        if (image.Format != format) throw new IOException("Image format changed since preview.");
                        image.AutoOrient();
                        if (image.Width != item.OutputWidth || image.Height != item.OutputHeight)
                            throw new IOException("Auto-orient produced unexpected dimensions.");
                        var exif = image.GetExifProfile();
                        if (exif == null) throw new IOException("EXIF metadata was lost during decoding.");
                        exif.RemoveThumbnail(); exif.SetValue(ExifTag.Orientation, (ushort)1);
                        exif.SetValue(ExifTag.PixelXDimension, new Number(image.Width));
                        exif.SetValue(ExifTag.PixelYDimension, new Number(image.Height));
                        exif.SetValue(ExifTag.ImageWidth, new Number(image.Width));
                        exif.SetValue(ExifTag.ImageLength, new Number(image.Height));
                        image.SetProfile(exif); image.Orientation = OrientationType.TopLeft;
                        if (format == MagickFormat.Jpeg) image.Quality = 95;
                        token.ThrowIfCancellationRequested();
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { image.Write(output, format); output.Flush(true); }
                    }
                    token.ThrowIfCancellationRequested();
                    using (var output = File.OpenRead(temporary))
                    using (var verify = new MagickImage())
                    {
                        verify.Ping(output, new MagickReadSettings { Format = format, FrameCount = 1 });
                        var exif = verify.GetExifProfile();
                        if (verify.Width != item.OutputWidth || verify.Height != item.OutputHeight || verify.Orientation != OrientationType.TopLeft
                            || exif == null || exif.GetValue(ExifTag.Orientation) == null || exif.GetValue(ExifTag.Orientation).Value != 1)
                            throw new IOException("Encoded orientation could not be verified; original was not replaced.");
                    }
                    if (!item.Revision.Matches(path)) throw new IOException("Source changed during processing.");
                    using (var current = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    using (var sha = SHA256.Create())
                        if (!digest.SequenceEqual(sha.ComputeHash(current))) throw new IOException("Source content changed during processing.");
                    File.SetLastWriteTimeUtc(temporary, modified);
                    string backups = Path.Combine(folder, ".viewer-orientation-backups");
                    if (Directory.Exists(backups)) FileTransferService.CheckAncestors(backups);
                    else { Directory.CreateDirectory(backups); FileTransferService.CheckAncestors(backups); }
                    string backup = Path.Combine(backups, Guid.NewGuid().ToString("N") + "." + Path.GetFileName(path) + ".bak");
                    token.ThrowIfCancellationRequested();
                    File.Replace(temporary, path, backup, false);
                    return backup;
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
