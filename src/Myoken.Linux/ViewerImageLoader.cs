using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Myoken.Linux;

internal sealed record ViewerImage(Bitmap Bitmap, int Width, int Height)
{
    public bool IsFullResolution => Bitmap.PixelSize.Width == Width && Bitmap.PixelSize.Height == Height;
}

internal static class ViewerImageLoader
{
    // Serialise viewer decodes across tabs, including cancelled native operations
    // that cannot be interrupted mid-call. Thumbnail decoding has its own existing gate.
    private static readonly SemaphoreSlim Gate = new(1);

    public static async Task<ViewerImage> LoadAsync(string path, bool full, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                int width, height;
                using (var managed = new SKManagedStream(stream, false))
                using (var codec = SKCodec.Create(managed))
                {
                    if (codec == null) throw new InvalidDataException("Unsupported or damaged image.");
                    width = codec.Info.Width; height = codec.Info.Height;
                }
                ViewerDecodePolicy.Validate(width, height, full);
                token.ThrowIfCancellationRequested();
                stream.Position = 0;
                Bitmap? bitmap = null;
                try
                {
                    bitmap = full ? new Bitmap(stream)
                        : width >= height
                            ? Bitmap.DecodeToWidth(stream, Math.Min(ViewerDecodePolicy.PreviewEdge, width))
                            : Bitmap.DecodeToHeight(stream, Math.Min(ViewerDecodePolicy.PreviewEdge, height));
                    token.ThrowIfCancellationRequested();
                    if (full && (bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height))
                        throw new InvalidDataException("Decoded dimensions differ from the source header; actual-size viewing is unavailable.");
                    var result = new ViewerImage(bitmap, width, height);
                    bitmap = null; // Ownership passes to the UI caller.
                    return result;
                }
                finally { bitmap?.Dispose(); }
            }, token);
        }
        finally { Gate.Release(); }
    }
}
