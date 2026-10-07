using System.Drawing;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Myoken.Core;
using PhotoSauce.MagicScaler;
using PhotoSauce.MagicScaler.Transforms;

namespace Myoken.Linux;

// JPEG XL decoder backed by PhotoSauce/libjxl. Myoken intentionally exposes the
// first frame only for L003d, while preserving alpha and normalising orientation
// plus source profiles into the same SDR sRGB working space as other formats.
internal static class JxlDecoder
{
    public static bool IsSupported(string path) =>
        string.Equals(Path.GetExtension(path), ".jxl", StringComparison.OrdinalIgnoreCase);

    public static OrientedDecodeResult Decode(string path, int edge, bool full, CancellationToken token)
    {
        if (!IsSupported(path)) throw new NotSupportedException("Not a JPEG XL path.");
        if (!full && edge < 1) throw new ArgumentOutOfRangeException(nameof(edge));
        token.ThrowIfCancellationRequested();
        AdvancedCodecRegistry.EnsureRegistered();

        var info = ImageFileInfo.Load(path);
        if (info.Frames.Count == 0) throw new InvalidDataException("JPEG XL container has no image frame.");
        var frame = info.Frames[0];

        // ImageFileInfo reports corrected visual dimensions. Its orientation enum
        // deliberately matches EXIF values, so derive stored dimensions for Info.
        var encodedOrientation = ImageOrientationInfo.Normalize((int)frame.ExifOrientation);
        var width = frame.Width;
        var height = frame.Height;
        var encodedWidth = ImageOrientationInfo.SwapsAxes(encodedOrientation) ? height : width;
        var encodedHeight = ImageOrientationInfo.SwapsAxes(encodedOrientation) ? width : height;
        ViewerDecodePolicy.Validate(width, height, full);

        var settings = new ProcessImageSettings
        {
            Sharpen = false,
            ResizeMode = CropScaleMode.Max,
            OrientationMode = OrientationMode.Normalize,
            ColorProfileMode = ColorProfileMode.ConvertToSrgb,
            HybridMode = HybridScaleMode.FavorSpeed,
            DecoderOptions = new PhotoSauce.NativeCodecs.Libjxl.JxlDecoderOptions(0..1)
        };
        if (!full)
        {
            settings.Width = edge;
            settings.Height = edge;
        }

        using var pipeline = MagicImageProcessor.BuildPipeline(path, settings);
        pipeline.AddTransform(new FormatConversionTransform(PhotoSauce.MagicScaler.PixelFormats.Bgra32bpp));
        var source = pipeline.PixelSource;
        token.ThrowIfCancellationRequested();

        if (source.Format != PhotoSauce.MagicScaler.PixelFormats.Bgra32bpp)
            throw new InvalidDataException("JPEG XL decoder did not produce the expected BGRA32 working format.");
        if (source.Width <= 0 || source.Height <= 0)
            throw new InvalidDataException("JPEG XL decoder returned invalid dimensions.");
        if (full && (source.Width != width || source.Height != height))
            throw new InvalidDataException("Full-resolution JPEG XL decode dimensions differ from the first image frame.");

        var stride = checked(source.Width * 4);
        var bytes = new byte[checked(stride * source.Height)];
        source.CopyPixels(new Rectangle(0, 0, source.Width, source.Height), stride, bytes);
        token.ThrowIfCancellationRequested();

        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        Bitmap? bitmap = null;
        try
        {
            bitmap = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul,
                handle.AddrOfPinnedObject(), new PixelSize(source.Width, source.Height),
                new Vector(96, 96), stride);
        }
        finally { handle.Free(); }

        token.ThrowIfCancellationRequested();
        return new OrientedDecodeResult(bitmap, width, height, encodedWidth, encodedHeight,
            ImageOrientation.TopLeft,
            "JPEG XL container profile (libjxl/MagicScaler)",
            true, true);
    }
}
