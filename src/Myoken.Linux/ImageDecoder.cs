using Avalonia.Media.Imaging;

namespace Myoken.Linux;

internal static class ImageDecoder
{
    private static readonly SemaphoreSlim Gate = new(2);
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" };

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path));

    public static async Task<Bitmap> LoadAsync(string path, int edge, CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
                OrientedImageDecoder.Decode(path, edge, full: false, token).Bitmap, token);
        }
        finally { Gate.Release(); }
    }
}
