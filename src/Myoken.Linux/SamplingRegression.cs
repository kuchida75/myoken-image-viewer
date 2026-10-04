using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Myoken.Linux;

internal sealed partial class MainWindow
{
    // Run with the existing guarded X11 fixture suite. Inspect rendered pixels,
    // not just the selected enum, to detect ineffective interpolation settings.
    private static void RunSamplingRenderChecks()
    {
        using var original = new SKBitmap(16, 16);
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
            original.SetPixel(x, y, (x + y) % 2 == 0 ? SKColors.Black : SKColors.White);
        using var image = SKImage.FromBitmap(original);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(encoded.ToArray());
        using var bitmap = new Bitmap(stream);
        var surface = new ImageSurface { Bitmap = bitmap };
        surface.Measure(new Size(80, 80)); surface.Arrange(new Rect(0, 0, 80, 80));
        surface.View.SetSource(16, 16);
        try
        {
            foreach (var zoom in new[] { 1.768, 2.0, 8.0 })
            {
                surface.SetPixelMode(false); surface.ZoomAt(zoom, new Point(40, 40));
                CheckUi(surface.SamplingMode == BitmapInterpolationMode.HighQuality, $"Smooth selects high-quality sampling at {zoom * 100:0.#}%");
                using var smooth = RenderSamplingFixture(surface, zoom == 1.768 ? "sampling-smooth-1768" : null);
                surface.SetPixelMode(true);
                using var sharp = RenderSamplingFixture(surface, zoom == 1.768 ? "sampling-pixels-1768" : null);
                var blended = 0; var sharpBlended = 0;
                // Interior samples exclude transparent image edges and clipping.
                for (var y = 34; y < 46; y++)
                for (var x = 34; x < 46; x++)
                {
                    var s = smooth.GetPixel(x, y); var p = sharp.GetPixel(x, y);
                    CheckUiPixel(s.Alpha == 255 && p.Alpha == 255, "sampling fixture did not render opaque pixels");
                    if (s.Red > 8 && s.Red < 247) blended++;
                    if (p.Red > 8 && p.Red < 247) sharpBlended++;
                }
                CheckUi(blended > 0 && sharpBlended == 0,
                    $"rendered {zoom * 100:0.#}% Smooth blends edges while Pixels retains original blocks ({blended} vs {sharpBlended} blended samples)");
            }
            surface.SetPixelMode(false); surface.ZoomAt(1, new Point(40, 40));
            using (var actual = RenderSamplingFixture(surface, null))
            {
                for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                    CheckUiPixel(actual.GetPixel(32 + x, 32 + y) == original.GetPixel(x, y), "100% rendered pixels differ from original");
            }
            CheckUi(true, "100% keeps every generated source pixel unchanged in the render target");
            using var reduced = bitmap.CreateScaledBitmap(new PixelSize(8, 8), BitmapInterpolationMode.HighQuality);
            surface.Bitmap = reduced;
            CheckUi(surface.SamplingMode == BitmapInterpolationMode.HighQuality, "source zoom 100% does not mistake a reduced preview for 1:1");
            surface.Bitmap = bitmap;
            CheckUi(surface.SamplingMode == BitmapInterpolationMode.None, "full-resolution replacement immediately enables exact 1:1 sampling");
            surface.SetPixelMode(true); surface.ZoomAt(0.75, new Point(40, 40));
            CheckUi(surface.SamplingMode == BitmapInterpolationMode.HighQuality, "minification remains smooth even in pixel-inspection mode");
        }
        finally { surface.Bitmap = null; }
    }

    private static void CheckUiPixel(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
    }
    private static SKBitmap RenderSamplingFixture(ImageSurface surface, string? name)
    {
        using var target = new RenderTargetBitmap(new PixelSize(80, 80));
        target.Render(surface);
        var output = Environment.GetEnvironmentVariable("MYOKEN_TEST_OUTPUT");
        if (name != null && !string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            target.Save(Path.Combine(output, name + ".png"), new PngBitmapEncoderOptions());
        }
        using var encoded = new MemoryStream();
        target.Save(encoded, new PngBitmapEncoderOptions());
        return SKBitmap.Decode(encoded.ToArray()) ?? throw new InvalidDataException("Rendered sampling fixture did not decode.");
    }
}
