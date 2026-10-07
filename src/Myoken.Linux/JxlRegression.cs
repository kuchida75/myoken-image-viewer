using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Myoken.Linux;

internal sealed partial class MainWindow
{
    private async Task RunJxlChecksAsync()
    {
        var testRoot = Environment.GetEnvironmentVariable("MYOKEN_TEST_ROOT")
            ?? throw new InvalidOperationException("MYOKEN_TEST_ROOT is required for codec tests.");
        var root = Path.Combine(testRoot, "codecs");
        var jxl = Path.Combine(root, "sample.jxl");
        var heic = Path.Combine(root, "sample.heic");
        CheckUi(File.Exists(jxl), "generated JPEG XL fixture exists");
        CheckUi(ImageDecoder.IsSupported(jxl), "browser support policy includes JXL extension");

        var full = JxlDecoder.Decode(jxl, 4096, true, CancellationToken.None);
        try
        {
            CheckUi(full.Width == 64 && full.Height == 48
                && full.Bitmap.PixelSize == new PixelSize(64, 48),
                "JPEG XL full decode preserves first-frame visual dimensions");
            CheckUi(full.ColorConvertedToSrgb
                && full.SourceColorSpace.Contains("libjxl/MagicScaler", StringComparison.Ordinal),
                "JPEG XL decode normalizes through the sRGB libjxl pipeline");
            var red = ReadJxlPixel(full.Bitmap, 8, 8);
            CheckUi(red.r > 120 && red.g < 130 && red.b < 130 && red.a > 240,
                "JPEG XL decoded pixels contain expected opaque red quadrant");
            var alpha = ReadJxlPixel(full.Bitmap, 56, 40);
            CheckUi(alpha.a is >= 40 and <= 100,
                $"JPEG XL preserves first-frame alpha ({alpha.a})");
        }
        finally { full.Bitmap.Dispose(); }

        using (var thumb = await ImageDecoder.LoadAsync(jxl, 32))
            CheckUi(Math.Max(thumb.PixelSize.Width, thumb.PixelSize.Height) == 32,
                "JPEG XL thumbnail decode respects requested long edge");

        _previewCache.Clear();
        var tab = AddImageTab(jxl);
        await SelectForTestAsync(tab);
        var viewer = (ImageViewer)tab.Content;
        await WaitUiAsync(() => viewer.HasImage, "JPEG XL viewer preview failed");
        CheckUi(viewer.View.SourceWidth == 64 && viewer.View.SourceHeight == 48,
            "JPEG XL viewer uses first-frame visual dimensions");
        viewer.InvokeInfoButton();
        await viewer.MetadataTask.WaitAsync(TimeSpan.FromSeconds(10));
        CheckUi(viewer.MetadataText.Contains("JPEG XL container profile", StringComparison.Ordinal)
            && viewer.MetadataText.Contains("Working: sRGB", StringComparison.Ordinal)
            && viewer.MetadataText.Contains("JPEG XL EXIF/XMP detail extraction is not exposed", StringComparison.Ordinal)
            && !viewer.MetadataText.Contains("Metadata:", StringComparison.Ordinal),
            "JPEG XL Info panel reports decoder-managed colour and explicit metadata-detail limit");

        await SelectForTestAsync(_browser);
        var before = _previewCache.Snapshot;
        await SelectForTestAsync(tab);
        var after = _previewCache.Snapshot;
        CheckUi(viewer.PreviewCacheHit && after.DecodeAttempts == before.DecodeAttempts,
            "JPEG XL viewer preview is reusable through the existing preview cache");

        // The global PhotoSauce registry must continue to serve HEIC after libjxl
        // has been exercised, proving that one advanced codec never replaces another.
        using (var heicAgain = await ImageDecoder.LoadAsync(heic, 16))
            CheckUi(Math.Max(heicAgain.PixelSize.Width, heicAgain.PixelSize.Height) == 16,
                "shared PhotoSauce registry keeps HEIC available after JPEG XL use");

        CaptureForTest("jpeg-xl-viewer");
        await SelectForTestAsync(_browser);
        CloseTab(tab);
        _previewCache.Clear();
        Console.WriteLine("PASS: L003d JPEG XL decoder, alpha, thumbnail, viewer, cache and codec-registry regressions");
    }

    private static (byte r, byte g, byte b, byte a) ReadJxlPixel(Bitmap bitmap, int x, int y)
    {
        var ptr = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), ptr, 4, 4);
            return (Marshal.ReadByte(ptr, 2), Marshal.ReadByte(ptr, 1),
                Marshal.ReadByte(ptr, 0), Marshal.ReadByte(ptr, 3));
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
