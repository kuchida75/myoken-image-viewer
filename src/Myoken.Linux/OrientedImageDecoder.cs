using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Myoken.Core;
using SkiaSharp;

namespace Myoken.Linux;

internal sealed record OrientedDecodeResult(
    Bitmap Bitmap,
    int Width,
    int Height,
    int EncodedWidth,
    int EncodedHeight,
    ImageOrientation Orientation);

// Explicitly honours EXIF orientation. Normal TopLeft images keep Avalonia's
// existing decoder; only mirrored/rotated inputs take the Skia transform path.
internal static class OrientedImageDecoder
{
    public static OrientedDecodeResult Decode(string path, int edge, bool full, CancellationToken token)
    {
        if (!full && edge < 1) throw new ArgumentOutOfRangeException(nameof(edge));
        token.ThrowIfCancellationRequested();

        int encodedWidth, encodedHeight;
        ImageOrientation orientation;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var managed = new SKManagedStream(stream, false))
        using (var codec = SKCodec.Create(managed))
        {
            if (codec == null) throw new InvalidDataException("Unsupported or damaged image.");
            encodedWidth = codec.Info.Width;
            encodedHeight = codec.Info.Height;
            orientation = ImageOrientationInfo.Normalize((int)codec.EncodedOrigin);
        }

        ViewerDecodePolicy.Validate(encodedWidth, encodedHeight, full);
        var swap = ImageOrientationInfo.SwapsAxes(orientation);
        var displayWidth = swap ? encodedHeight : encodedWidth;
        var displayHeight = swap ? encodedWidth : encodedHeight;

        // Preserve the established, well-tested Avalonia path for normal images.
        if (orientation == ImageOrientation.TopLeft)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Bitmap bitmap = full
                ? new Bitmap(stream)
                : encodedWidth >= encodedHeight
                    ? Bitmap.DecodeToWidth(stream, Math.Min(edge, encodedWidth))
                    : Bitmap.DecodeToHeight(stream, Math.Min(edge, encodedHeight));
            if (token.IsCancellationRequested)
            {
                bitmap.Dispose();
                token.ThrowIfCancellationRequested();
            }
            return new OrientedDecodeResult(bitmap, displayWidth, displayHeight,
                encodedWidth, encodedHeight, orientation);
        }

        var targetDisplayWidth = displayWidth;
        var targetDisplayHeight = displayHeight;
        if (!full && Math.Max(displayWidth, displayHeight) > edge)
        {
            if (displayWidth >= displayHeight)
            {
                targetDisplayWidth = edge;
                targetDisplayHeight = Math.Max(1, (int)Math.Round(displayHeight * (edge / (double)displayWidth)));
            }
            else
            {
                targetDisplayHeight = edge;
                targetDisplayWidth = Math.Max(1, (int)Math.Round(displayWidth * (edge / (double)displayHeight)));
            }
        }

        var targetRawWidth = swap ? targetDisplayHeight : targetDisplayWidth;
        var targetRawHeight = swap ? targetDisplayWidth : targetDisplayHeight;
        var scale = Math.Min(1f, Math.Min(targetRawWidth / (float)encodedWidth, targetRawHeight / (float)encodedHeight));

        using var sourceStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var skStream = new SKManagedStream(sourceStream, false);
        using var sourceCodec = SKCodec.Create(skStream);
        if (sourceCodec == null) throw new InvalidDataException("Unsupported or damaged image.");
        var supported = sourceCodec.GetScaledDimensions(scale);
        if (supported.Width <= 0 || supported.Height <= 0)
            throw new InvalidDataException("Decoder cannot determine a safe scaled image size.");

        var nearestInfo = new SKImageInfo(supported.Width, supported.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var nearest = SKBitmap.Decode(sourceCodec, nearestInfo)
            ?? throw new InvalidDataException("Unable to decode image pixels.");
        token.ThrowIfCancellationRequested();

        SKBitmap raw = nearest;
        SKBitmap? resized = null;
        SKBitmap? oriented = null;
        try
        {
            if (nearest.Width != targetRawWidth || nearest.Height != targetRawHeight)
            {
                resized = nearest.Resize(
                    new SKImageInfo(targetRawWidth, targetRawHeight, SKColorType.Bgra8888, SKAlphaType.Premul),
                    new SKSamplingOptions(SKFilterMode.Linear));
                if (resized == null) throw new InvalidDataException("Unable to resize decoded image.");
                raw = resized;
            }

            token.ThrowIfCancellationRequested();
            oriented = ApplyOrientation(raw, orientation);
            var final = oriented ?? raw;
            var bitmap = new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, final.GetPixels(),
                new PixelSize(final.Width, final.Height), new Vector(96, 96), final.RowBytes);
            if (token.IsCancellationRequested)
            {
                bitmap.Dispose();
                token.ThrowIfCancellationRequested();
            }
            return new OrientedDecodeResult(bitmap, displayWidth, displayHeight,
                encodedWidth, encodedHeight, orientation);
        }
        finally
        {
            oriented?.Dispose();
            resized?.Dispose();
        }
    }

    private static SKBitmap? ApplyOrientation(SKBitmap source, ImageOrientation orientation)
    {
        if (orientation == ImageOrientation.TopLeft) return null;
        var swap = ImageOrientationInfo.SwapsAxes(orientation);
        var width = source.Width;
        var height = source.Height;
        var target = new SKBitmap(new SKImageInfo(
            swap ? height : width, swap ? width : height,
            SKColorType.Bgra8888, SKAlphaType.Premul));

        var matrix = orientation switch
        {
            ImageOrientation.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
            ImageOrientation.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
            ImageOrientation.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
            ImageOrientation.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            ImageOrientation.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
            ImageOrientation.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
            ImageOrientation.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
            _ => SKMatrix.Identity
        };

        using var canvas = new SKCanvas(target);
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return target;
    }
}
