using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using ImageMagick;

namespace ZonerInspiredViewer
{
    internal static class BatchImages
    {
        internal static readonly string[] Extensions = { ".jpg", ".png", ".webp", ".avif", ".jxl", ".gif" };
        private const long PixelLimit = 128L * 1024 * 1024;
        internal static void CheckOutputFolder(string path)
        {
            string existing = Path.GetFullPath(path);
            while (!Directory.Exists(existing))
            {
                if (File.Exists(existing)) throw new IOException("An output folder component is a file.");
                existing = Path.GetDirectoryName(existing);
                if (existing == null) throw new IOException("The output drive is unavailable.");
            }
            FileTransferService.CheckAncestors(existing);
        }

        internal static string Token(string path, string name, string format)
        {
            ModernImageDecoder.Initialize();
            using (var stream = File.OpenRead(path))
            using (var image = new MagickImage())
            {
                image.Ping(stream, new MagickReadSettings { Format = EnhancedImageStore.Format(path), FrameCount = 1 });
                bool swap = (int)image.Orientation >= 5 && (int)image.Orientation <= 8;
                if (name == "width") return (swap ? image.Height : image.Width).ToString(CultureInfo.InvariantCulture);
                if (name == "height") return (swap ? image.Width : image.Height).ToString(CultureInfo.InvariantCulture);
                var profile = image.GetExifProfile();
                var date = profile == null ? null : profile.GetValue(ExifTag.DateTimeOriginal);
                DateTime taken;
                if (date == null || !DateTime.TryParseExact(date.Value, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out taken))
                    throw new IOException("Date taken is not available in this image header.");
                return taken.ToString(format, CultureInfo.InvariantCulture);
            }
        }

        internal static void Inspect(BatchItem item, BatchOptions options, CancellationToken token)
        {
            ModernImageDecoder.Initialize();
            MagickFormat sourceFormat = EnhancedImageStore.Format(item.Source);
            MagickFormat output = options.Kind == BatchKind.Resize && options.KeepFormat ? sourceFormat : EnhancedImageStore.Format("image" + options.Format);
            if (!MagickNET.SupportedFormats.Any(f => f.Format == output && f.SupportsWriting)) throw new NotSupportedException("Source format is decode-only; choose another output format.");
            using (var stream = File.OpenRead(item.Source))
            using (var images = new MagickImageCollection())
            {
                images.Ping(stream, new MagickReadSettings { Format = sourceFormat, FrameCount = 2 });
                token.ThrowIfCancellationRequested();
                if (images.Count != 1) throw new NotSupportedException("Animation or multi-image input is not supported by batch export.");
                if (sourceFormat == MagickFormat.Png) { stream.Position = 0; if (ImageRotationService.IsAnimatedPng(stream)) throw new NotSupportedException("Animated PNG is not supported by batch export."); }
                var image = images[0]; bool swap = (int)image.Orientation >= 5 && (int)image.Orientation <= 8;
                item.Width = (int)(swap ? image.Height : image.Width); item.Height = (int)(swap ? image.Width : image.Height);
                Size(item.Width, item.Height, options, out item.OutputWidth, out item.OutputHeight);
                item.Dimensions = item.Width + " x " + item.Height + " -> " + item.OutputWidth + " x " + item.OutputHeight;
            }
        }

        internal static void Size(int width, int height, BatchOptions options, out int outputWidth, out int outputHeight)
        {
            if (width < 1 || height < 1 || (long)width * height > PixelLimit) throw new IOException("Input exceeds the 128-megapixel limit.");
            double w = width, h = height;
            if (options.Kind == BatchKind.Resize)
            {
                double targetW = options.Width, targetH = options.Height, scale = 1;
                if (options.ResizeMode == BatchResizeMode.Percentage) scale = options.Percent / 100;
                else if (options.ResizeMode == BatchResizeMode.LongEdge) scale = options.Width / Math.Max(width, height);
                else if (options.ResizeMode == BatchResizeMode.ShortEdge) scale = options.Width / Math.Min(width, height);
                else
                {
                    if (options.ResizeMode == BatchResizeMode.PrintSize)
                    { double units = options.PrintCentimeters ? 2.54 : 1; targetW = options.PrintWidth / units * options.Dpi; targetH = options.PrintHeight / units * options.Dpi; }
                    scale = options.Fit == BatchFit.Width ? targetW / width : options.Fit == BatchFit.Height ? targetH / height : Math.Min(targetW / width, targetH / height);
                    if (!options.PreserveAspect) { w = targetW; h = targetH; }
                }
                if (options.PreserveAspect || options.ResizeMode == BatchResizeMode.Percentage || options.ResizeMode == BatchResizeMode.LongEdge || options.ResizeMode == BatchResizeMode.ShortEdge)
                { w *= scale; h *= scale; }
                if (options.Direction == BatchDirection.ReduceOnly) { w = Math.Min(width, w); h = Math.Min(height, h); }
                if (options.Direction == BatchDirection.EnlargeOnly) { w = Math.Max(width, w); h = Math.Max(height, h); }
            }
            if (w > 65536 || h > 65536 || w * h > PixelLimit) throw new IOException("Output exceeds 65536 pixels per side or 128 megapixels.");
            outputWidth = Math.Max(1, (int)Math.Round(w, MidpointRounding.AwayFromZero)); outputHeight = Math.Max(1, (int)Math.Round(h, MidpointRounding.AwayFromZero));
        }

        internal static void Export(BatchItem item, BatchOptions options, CancellationToken token)
        {
            string folder = Path.GetDirectoryName(item.Destination); CheckOutputFolder(folder); Directory.CreateDirectory(folder); FileTransferService.CheckAncestors(folder);
            string temporary = Path.Combine(folder, ".zen-batch-" + Guid.NewGuid().ToString("N") + ".tmp");
            var gates = new System.Collections.Generic.List<SharedFileGate>();
            try
            {
                foreach (string path in new[] { item.Source, item.Destination }.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)) gates.Add(new SharedFileGate(path));
                FileTransferService.CheckAncestors(item.Source);
                if (!item.Revision.Matches(item.Source)) throw new IOException("Source changed since preview; preview again.");
                byte[] destinationDigest = null;
                if (item.Overwrite)
                {
                    CheckDestination(item);
                    using (var stream = new FileStream(item.Destination, FileMode.Open, FileAccess.Read, FileShare.Read)) destinationDigest = Digest(stream);
                }
                else if (File.Exists(item.Destination) || Directory.Exists(item.Destination)) throw new IOException("Destination appeared after preview; no file overwritten.");
                DateTime modified = File.GetLastWriteTimeUtc(item.Source);
                byte[] sourceDigest;
                using (var source = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var image = new MagickImage())
                {
                    sourceDigest = Digest(source); source.Position = 0;
                    var check = new BatchItem { Source = item.Source }; Inspect(check, options, token);
                    if (check.OutputWidth != item.OutputWidth || check.OutputHeight != item.OutputHeight) throw new IOException("Source dimensions changed since preview.");
                    image.Read(source, new MagickReadSettings { Format = EnhancedImageStore.Format(item.Source), FrameCount = 1 }); image.AutoOrient();
                    if (image.Width != item.OutputWidth || image.Height != item.OutputHeight)
                    { image.FilterType = FilterType.Lanczos; image.Resize(new MagickGeometry((uint)item.OutputWidth, (uint)item.OutputHeight) { IgnoreAspectRatio = true }); }
                    if (options.Kind == BatchKind.Resize && options.ResizeMode == BatchResizeMode.PrintSize) image.Density = new Density(options.Dpi, options.Dpi, DensityUnit.PixelsPerInch);
                    if (!options.PreserveMetadata)
                    {
                        var color = image.GetColorProfile(); var density = image.Density;
                        image.Strip(); if (color != null) image.SetProfile(color); image.Density = density;
                    }
                    else
                    {
                        var exif = image.GetExifProfile();
                        if (exif != null)
                        {
                            exif.RemoveThumbnail(); exif.SetValue(ExifTag.Orientation, (ushort)1);
                            exif.SetValue(ExifTag.PixelXDimension, new Number((uint)item.OutputWidth)); exif.SetValue(ExifTag.PixelYDimension, new Number((uint)item.OutputHeight));
                            exif.SetValue(ExifTag.ImageWidth, new Number((uint)item.OutputWidth)); exif.SetValue(ExifTag.ImageLength, new Number((uint)item.OutputHeight)); image.SetProfile(exif);
                        }
                    }
                    image.Orientation = OrientationType.TopLeft; image.Depth = 8; image.Quality = (uint)options.Quality;
                    MagickFormat format = EnhancedImageStore.Format(item.Destination);
                    if (format == MagickFormat.Jpeg) { image.BackgroundColor = MagickColors.White; image.Alpha(AlphaOption.Remove); }
                    if (format == MagickFormat.WebP) image.Settings.SetDefine(MagickFormat.WebP, "lossless", options.Lossless);
                    if (format == MagickFormat.Jxl && options.Lossless) image.Quality = 100;
                    token.ThrowIfCancellationRequested();
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { image.Write(output, format); output.Flush(true); }
                    token.ThrowIfCancellationRequested();
                    using (var verify = new MagickImage())
                    using (var output = File.OpenRead(temporary))
                    { verify.Read(output, new MagickReadSettings { Format = format }); if (verify.Width != item.OutputWidth || verify.Height != item.OutputHeight) throw new IOException("Encoded output failed verification."); }
                }
                if (!item.Revision.Matches(item.Source)) throw new IOException("Source changed during processing.");
                using (var source = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    if (!Digest(source).SequenceEqual(sourceDigest)) throw new IOException("Source content changed during processing.");
                    if (options.PreserveDates) File.SetLastWriteTimeUtc(temporary, modified);
                    token.ThrowIfCancellationRequested();
                    if (item.Overwrite)
                    {
                        CheckDestination(item);
                        string backupFolder = Path.Combine(folder, ".zen-batch-backups"); CheckOutputFolder(backupFolder); Directory.CreateDirectory(backupFolder); FileTransferService.CheckAncestors(backupFolder);
                        string backup = Path.Combine(backupFolder, Guid.NewGuid().ToString("N") + "." + Path.GetFileName(item.Destination));
                        using (var existing = new FileStream(item.Destination, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                        { if (!Digest(existing).SequenceEqual(destinationDigest)) throw new IOException("Destination changed during processing."); token.ThrowIfCancellationRequested(); File.Replace(temporary, item.Destination, backup, false); }
                    }
                    else File.Move(temporary, item.Destination);
                }
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                for (int i = gates.Count - 1; i >= 0; i--) gates[i].Dispose();
            }
        }
        private static void CheckDestination(BatchItem item)
        {
            FileTransferService.CheckAncestors(item.Destination);
            if (!item.DestinationRevision.Matches(item.Destination)) throw new IOException("Destination changed since preview.");
            if ((File.GetAttributes(item.Destination) & FileAttributes.ReadOnly) != 0) throw new IOException("Destination is read-only.");
        }
        private static byte[] Digest(Stream stream) { using (var sha = SHA256.Create()) return sha.ComputeHash(stream); }
    }
}
