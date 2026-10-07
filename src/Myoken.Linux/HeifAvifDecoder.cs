using System.Drawing;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Myoken.Core;
using PhotoSauce.MagicScaler;
using PhotoSauce.MagicScaler.Transforms;
using PhotoSauce.NativeCodecs.Libheif;

namespace Myoken.Linux;

// HEIC/HEIF/AVIF decoder using PhotoSauce's libheif plugin. The NuGet package
// carries Linux x64/arm64 native decoders, so the application does not depend on
// the host's libheif packages. libheif normalizes item orientation; MagicScaler
// normalizes any exposed ICC profile into the sRGB working space.
internal static class HeifAvifDecoder
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".heic", ".heif", ".avif" };
    private static readonly Lazy<bool> Registered = new(Register, LazyThreadSafetyMode.ExecutionAndPublication);

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path));

    public static OrientedDecodeResult Decode(string path, int edge, bool full, CancellationToken token)
    {
        if (!IsSupported(path)) throw new NotSupportedException("Not a HEIC/HEIF/AVIF path.");
        if (!full && edge < 1) throw new ArgumentOutOfRangeException(nameof(edge));
        token.ThrowIfCancellationRequested();
        _ = Registered.Value;

        var info = ImageFileInfo.Load(path);
        if (info.Frames.Count == 0) throw new InvalidDataException("HEIF container has no image frame.");
        var frame = info.Frames[0];
        var width = frame.Width;
        var height = frame.Height;
        ViewerDecodePolicy.Validate(width, height, full);

        var settings = new ProcessImageSettings
        {
            Sharpen = false,
            ResizeMode = CropScaleMode.Max,
            OrientationMode = OrientationMode.Normalize,
            ColorProfileMode = ColorProfileMode.ConvertToSrgb,
            HybridMode = HybridScaleMode.FavorSpeed
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
            throw new InvalidDataException("HEIF decoder did not produce the expected BGRA32 working format.");
        if (source.Width <= 0 || source.Height <= 0)
            throw new InvalidDataException("HEIF decoder returned invalid dimensions.");
        if (full && (source.Width != width || source.Height != height))
            throw new InvalidDataException("Full-resolution HEIF decode dimensions differ from the primary image.");

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
        var format = info.MimeType == ImageMimeTypes.Avif ? "AVIF" : "HEIC/HEIF";
        return new OrientedDecodeResult(bitmap, width, height, width, height,
            ImageOrientation.TopLeft,
            format + " container profile (libheif/MagicScaler)",
            true, true);
    }

    private static bool Register()
    {
        CodecManager.Configure(codecs => codecs.UseLibheif());
        return true;
    }
}
