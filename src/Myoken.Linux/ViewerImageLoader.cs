using Avalonia.Media.Imaging;
using Myoken.Core;

namespace Myoken.Linux;

internal sealed record ViewerImage(
    Bitmap Bitmap,
    int Width,
    int Height,
    int EncodedWidth,
    int EncodedHeight,
    ImageOrientation Orientation)
{
    public bool IsFullResolution => Bitmap.PixelSize.Width == Width && Bitmap.PixelSize.Height == Height;
}

internal static class ViewerImageLoader
{
    private static readonly PriorityAsyncGate Gate = new();

    public static async Task<ViewerImage> LoadAsync(string path, bool full, CancellationToken token,
        ViewerDecodePriority priority = ViewerDecodePriority.Foreground)
    {
        using var gate = await Gate.EnterAsync(priority, token);
        return await Task.Run(() =>
        {
            var decoded = OrientedImageDecoder.Decode(path, ViewerDecodePolicy.PreviewEdge, full, token);
            return new ViewerImage(decoded.Bitmap, decoded.Width, decoded.Height,
                decoded.EncodedWidth, decoded.EncodedHeight, decoded.Orientation);
        }, token);
    }
}
