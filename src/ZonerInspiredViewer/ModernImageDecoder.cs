using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace ZonerInspiredViewer
{
    internal sealed class ImageAnimation
    {
        public readonly List<BitmapSource> Frames = new List<BitmapSource>();
        public readonly List<int> Delays = new List<int>();
        public uint Iterations;
    }

    internal sealed class ModernImageDecoder : IImageDecoder
    {
        private static readonly SemaphoreSlim Workers = new SemaphoreSlim(Math.Max(2, Math.Min(8, Environment.ProcessorCount / 4)));
        private static readonly Lazy<bool> Initialized = new Lazy<bool>(delegate
        {
            MagickNET.SetNativeLibraryDirectory(AppDomain.CurrentDomain.BaseDirectory);
            MagickNET.Initialize(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Codecs"));
            ResourceLimits.Memory = 512UL * 1024 * 1024;
            ResourceLimits.Disk = 2UL * 1024 * 1024 * 1024;
            ResourceLimits.Thread = 2;
            ResourceLimits.Width = ResourceLimits.Height = 65536;
            return true;
        });
        private readonly Func<bool> _icc;
        public ModernImageDecoder(Func<bool> icc) { _icc = icc; }
        public string Name { get { return "Bundled Magick.NET 14.16 (8-bit display)"; } }
        internal static void Initialize() { bool initialized = Initialized.Value; }

        private static MagickFormat Format(string extension)
        {
            switch (extension.ToLowerInvariant())
            {
                case ".avif": return MagickFormat.Avif;
                case ".webp": return MagickFormat.WebP;
                case ".jxl": return MagickFormat.Jxl;
                case ".heic": case ".heif": return MagickFormat.Heic;
                case ".gif": return MagickFormat.Gif;
                default: throw new NotSupportedException("Unsupported image format: " + extension);
            }
        }

        public bool CanDecode(string extension)
        {
            return extension.Equals(".avif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jxl", StringComparison.OrdinalIgnoreCase) || extension.Equals(".heic", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".heif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase);
        }

        private static MagickReadSettings Settings(string path, uint? count)
        {
            // Force an allowlisted raster decoder, never interpret a filename as a delegate command.
            return new MagickReadSettings { Format = Format(Path.GetExtension(path)), FrameIndex = count.HasValue ? (uint?)0 : null, FrameCount = count };
        }

        private static FileStream Open(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        private static void CheckDimensions(IMagickImage image)
        {
            if (image.Width == 0 || image.Height == 0 || (ulong)image.Width * image.Height > 128UL * 1024 * 1024)
                throw new NotSupportedException("Image exceeds the 128-megapixel display limit.");
        }

        private static BitmapSource Pixels(IMagickImage<byte> image, int width, bool icc)
        {
            CheckDimensions(image);
            image.AutoOrient();
            if (width > 0 && image.Width > width) image.Resize((uint)width, 0);
            if (icc && image.GetColorProfile() != null) image.TransformColorSpace(ColorProfiles.SRGB);
            else image.ColorSpace = ColorSpace.sRGB;
            using (var pixels = image.GetPixels())
            {
                byte[] data = pixels.ToByteArray(PixelMapping.BGRA);
                var bitmap = BitmapSource.Create((int)image.Width, (int)image.Height, 96, 96, PixelFormats.Bgra32, null, data, checked((int)image.Width * 4));
                bitmap.Freeze(); return bitmap;
            }
        }

        public BitmapSource Decode(string path, int decodePixelWidth, CancellationToken token)
        {
            Workers.Wait(token);
            try
            {
                Initialize(); token.ThrowIfCancellationRequested();
                using (var stream = Open(path))
                using (var image = new MagickImage())
                {
                    var settings = Settings(path, 1);
                    image.Ping(stream, settings); CheckDimensions(image); stream.Position = 0;
                    if (settings.Format == MagickFormat.Gif)
                    {
                        if ((ulong)image.Page.Width * image.Page.Height > 128UL * 1024 * 1024)
                            throw new NotSupportedException("GIF canvas exceeds the 128-megapixel display limit.");
                        using (var first = new MagickImageCollection())
                        {
                            first.Read(stream, settings); first.Coalesce(); token.ThrowIfCancellationRequested();
                            return Pixels(first[0], decodePixelWidth, _icc());
                        }
                    }
                    image.Read(stream, settings); token.ThrowIfCancellationRequested();
                    BitmapSource result = Pixels(image, decodePixelWidth, _icc());
                    token.ThrowIfCancellationRequested(); return result;
                }
            }
            finally { Workers.Release(); }
        }

        public ImageMetadata ReadMetadata(string path)
        {
            var file = new FileInfo(path);
            var metadata = new ImageMetadata { Path = path, Decoder = Name, FileSize = file.Length, ModifiedUtc = file.LastWriteTimeUtc };
            Workers.Wait();
            try
            {
                Initialize();
                using (var stream = Open(path))
                using (var image = new MagickImage())
                {
                    image.Ping(stream, Settings(path, 1));
                    CheckDimensions(image);
                    metadata.FileType = image.Format == MagickFormat.Jxl ? "JPEG XL" : image.Format.ToString().ToUpperInvariant();
                    // Keep header depth before a full read converts pixels to the Q8 display pipeline.
                    metadata.BitDepth = image.Depth > 0 ? image.Depth + " bits/channel"
                        + (image.Format == MagickFormat.Gif ? " (palette)" : "") : null;
                    metadata.Add("Bit depth", metadata.BitDepth);
                    // Some codecs omit profiles from header-only reads (notably WebP).
                    if (image.GetExifProfile() == null)
                    {
                        stream.Position = 0; image.Read(stream, Settings(path, 1));
                    }
                    metadata.PixelWidth = (int)image.Width; metadata.PixelHeight = (int)image.Height;
                    if (image.Format == MagickFormat.Gif)
                    {
                        metadata.PixelWidth = (int)Math.Max(image.Width, image.Page.Width);
                        metadata.PixelHeight = (int)Math.Max(image.Height, image.Page.Height);
                    }
                    metadata.Add("Dimensions", metadata.PixelWidth + " x " + metadata.PixelHeight);
                    metadata.Add("Format", image.Format); metadata.Add("Color space", image.ColorSpace);
                    var exif = image.GetExifProfile();
                    if (exif != null) foreach (var value in exif.Values)
                    {
                        object data = value.GetValue();
                        if (data is Array) continue;
                        metadata.Add(value.Tag.ToString(), data);
                    }
                }
            }
            catch (Exception error) { metadata.Add("Metadata warning", error.Message); }
            finally { Workers.Release(); }
            metadata.Add("Decoder", Name); metadata.Add("Modified", metadata.ModifiedUtc.ToLocalTime());
            metadata.Add("File size", ImageExtensions.FormatBytes(metadata.FileSize)); return metadata;
        }

        internal ImageAnimation ReadAnimation(string path, long budget, CancellationToken token)
        {
            Workers.Wait(token);
            try
            {
                Initialize(); token.ThrowIfCancellationRequested();
                using (var stream = Open(path))
                using (var frames = new MagickImageCollection())
                {
                    var settings = Settings(path, null);
                    frames.Ping(stream, settings);
                    if (frames.Count < 2) return null;
                    ulong width = 0, height = 0;
                    foreach (var frame in frames)
                    {
                        width = Math.Max(width, Math.Max(frame.Width, frame.Page.Width));
                        height = Math.Max(height, Math.Max(frame.Height, frame.Page.Height));
                    }
                    if (frames.Count > 512 || width * height * 4UL * (ulong)frames.Count > (ulong)budget)
                        throw new NotSupportedException("Animation exceeds the playback budget; showing its first frame.");
                    token.ThrowIfCancellationRequested(); frames.Clear(); stream.Position = 0;
                    frames.Read(stream, settings); frames.Coalesce(); token.ThrowIfCancellationRequested();
                    var result = new ImageAnimation { Iterations = frames[0].AnimationIterations };
                    long used = 0;
                    foreach (var frame in frames)
                    {
                        token.ThrowIfCancellationRequested();
                        used = checked(used + (long)frame.Width * frame.Height * 4);
                        if (used > budget) throw new NotSupportedException("Animation exceeds the playback budget; showing its first frame.");
                        result.Frames.Add(Pixels(frame, 0, _icc()));
                        result.Delays.Add((int)Math.Max(20, Math.Min(60000, 1000L * frame.AnimationDelay / Math.Max(1, frame.AnimationTicksPerSecond))));
                    }
                    return result;
                }
            }
            finally { Workers.Release(); }
        }
    }
}
