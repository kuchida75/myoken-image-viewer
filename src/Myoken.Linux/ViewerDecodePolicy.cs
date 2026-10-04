namespace Myoken.Linux;

internal static class ViewerDecodePolicy
{
    public const int PreviewEdge = 4096;
    public const long MaximumInputPixels = 120_000_000;
    // 64 million RGBA8 pixels are about 244 MiB. This is an output-size guard,
    // NOT a bound on decoder scratch space, GPU copies or total process memory.
    public const long MaximumFullPixels = 64_000_000;
    public static bool CanDecodeFull(int width, int height) => width > 0 && height > 0
        && (long)width * height <= MaximumFullPixels;
    public static void Validate(int width, int height, bool full)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaximumInputPixels)
            throw new InvalidDataException("Image exceeds the 120-million-pixel input limit.");
        if (full && !CanDecodeFull(width, height))
            throw new InvalidDataException("Full resolution exceeds the 64-million-pixel limit; preview remains available.");
    }
}
