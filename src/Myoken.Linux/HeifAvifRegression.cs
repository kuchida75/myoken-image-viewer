using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Myoken.Linux;

internal sealed partial class MainWindow
{
    private async Task RunHeifAvifChecksAsync()
    {
        var testRoot = Environment.GetEnvironmentVariable("MYOKEN_TEST_ROOT")
            ?? throw new InvalidOperationException("MYOKEN_TEST_ROOT is required for codec tests.");
        var root = Path.Combine(testRoot, "codecs");
        var heic = Path.Combine(root, "sample.heic");
        var avif = Path.Combine(root, "sample.avif");
        CheckUi(File.Exists(heic) && File.Exists(avif), "generated HEIC and AVIF fixtures exist");
        CheckUi(ImageDecoder.IsSupported(heic) && ImageDecoder.IsSupported(avif)
            && ImageDecoder.IsSupported(Path.ChangeExtension(heic, ".heif")),
            "browser support policy includes HEIC, HEIF and AVIF extensions");

        foreach (var (path, label) in new[] { (heic, "HEIC"), (avif, "AVIF") })
        {
            var full = HeifAvifDecoder.Decode(path, 4096, true, CancellationToken.None);
            try
            {
                CheckUi(full.Width == 64 && full.Height == 48
                    && full.Bitmap.PixelSize == new PixelSize(64, 48),
                    $"{label} full decode preserves primary dimensions");
                CheckUi(full.ColorConvertedToSrgb && full.SourceColorSpace.Contains("libheif/MagicScaler", StringComparison.Ordinal),
                    $"{label} decode normalizes through the sRGB HEIF pipeline");
                var pixel = ReadCodecPixel(full.Bitmap, 8, 8);
                CheckUi(pixel.r > 100 && pixel.g < 140 && pixel.b < 140,
                    $"{label} decoded pixels contain expected red quadrant");
            }
            finally { full.Bitmap.Dispose(); }

            using var thumb = await ImageDecoder.LoadAsync(path, 32);
            CheckUi(Math.Max(thumb.PixelSize.Width, thumb.PixelSize.Height) == 32,
                $"{label} thumbnail decode respects requested long edge");
        }

        _previewCache.Clear();
        var heicTab = AddImageTab(heic);
        var avifTab = AddImageTab(avif);
        await SelectForTestAsync(heicTab);
        var heicViewer = (ImageViewer)heicTab.Content;
        await WaitUiAsync(() => heicViewer.HasImage, "HEIC viewer preview failed");
        CheckUi(heicViewer.View.SourceWidth == 64 && heicViewer.View.SourceHeight == 48,
            "HEIC viewer uses decoded primary dimensions");
        heicViewer.InvokeInfoButton();
        await heicViewer.MetadataTask.WaitAsync(TimeSpan.FromSeconds(10));
        CheckUi(heicViewer.MetadataText.Contains("HEIC/HEIF container profile", StringComparison.Ordinal)
            && heicViewer.MetadataText.Contains("Working: sRGB", StringComparison.Ordinal),
            "HEIC Info panel reports decoder-managed sRGB working output");

        await SelectForTestAsync(avifTab);
        var avifViewer = (ImageViewer)avifTab.Content;
        await WaitUiAsync(() => avifViewer.HasImage, "AVIF viewer preview failed");
        var before = _previewCache.Snapshot;
        await SelectForTestAsync(heicTab);
        var after = _previewCache.Snapshot;
        CheckUi(heicViewer.PreviewCacheHit && after.DecodeAttempts == before.DecodeAttempts,
            "HEIC viewer preview is reusable through the existing preview cache");
        CaptureForTest("heic-avif-viewer");

        await SelectForTestAsync(_browser);
        CloseTab(heicTab); CloseTab(avifTab);
        _previewCache.Clear();
        Console.WriteLine("PASS: L003c HEIC/AVIF native decoder, thumbnail, viewer and cache regressions");
    }

    private static (byte r, byte g, byte b) ReadCodecPixel(Bitmap bitmap, int x, int y)
    {
        var ptr = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), ptr, 4, 4);
            return (Marshal.ReadByte(ptr, 2), Marshal.ReadByte(ptr, 1), Marshal.ReadByte(ptr, 0));
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
