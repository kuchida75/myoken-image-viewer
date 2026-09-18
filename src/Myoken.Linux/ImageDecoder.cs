using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Myoken.Linux;

internal static class ImageDecoder
{
    private static readonly SemaphoreSlim Gate = new(2);
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" };

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path));

    // SKCodec is used only to inspect dimensions. Bitmap allocation is requested
    // at the appropriate long edge, not by blindly decoding a full-resolution image.
    public static async Task<Bitmap> LoadAsync(string path, int edge, CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                int width, height;
                using (var probe = File.OpenRead(path))
                using (var codec = SKCodec.Create(probe))
                {
                    if (codec == null) throw new InvalidDataException("Unsupported or damaged image.");
                    width = codec.Info.Width;
                    height = codec.Info.Height;
                }
                if (width <= 0 || height <= 0 || (long)width * height > 120_000_000)
                    throw new InvalidDataException("Image dimensions exceed this prototype's safety limit.");
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var bitmap = width >= height
                    ? Bitmap.DecodeToWidth(stream, Math.Min(edge, width))
                    : Bitmap.DecodeToHeight(stream, Math.Min(edge, height));
                if (token.IsCancellationRequested)
                {
                    bitmap.Dispose();
                    token.ThrowIfCancellationRequested();
                }
                return bitmap;
            }, token);
        }
        finally { Gate.Release(); }
    }
}
