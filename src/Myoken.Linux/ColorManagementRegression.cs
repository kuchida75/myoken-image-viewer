using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Myoken.Linux;

internal sealed partial class MainWindow
{
    private async Task RunColorManagementChecksAsync(string folder)
    {
        var root = Path.GetFullPath(Path.Combine(folder, "..", "color-fixtures"));
        Directory.CreateDirectory(root);
        var p3Path = Path.Combine(root, "display-p3.png");
        WriteTaggedP3Png(p3Path, 220, 90, 40);

        using (var stream = File.OpenRead(p3Path))
        using (var codec = SKCodec.Create(stream))
        {
            CheckUi(codec != null && codec.Info.ColorSpace != null
                && !codec.Info.ColorSpace.IsSrgb
                && SourceColorInfo.Inspect(codec.Info.ColorSpace).Name == "Display P3",
                "generated wide-gamut fixture carries a recognised Display P3 source space");
        }

        var decoded = OrientedImageDecoder.Decode(p3Path, 4096, full: true, CancellationToken.None);
        try
        {
            CheckUi(decoded.ColorConvertedToSrgb && decoded.SourceColorSpace == "Display P3",
                "profile-aware decode marks Display P3 to sRGB conversion");
            var actual = ReadAvaloniaPixel(decoded.Bitmap, 0, 0);
            var expected = ReadReferenceSrgbPixel(p3Path);
            CheckUi(ChannelNear(actual, expected, 1),
                $"profile-aware decode matches Skia sRGB reference ({actual.r},{actual.g},{actual.b})");
            CheckUi(Math.Abs(actual.r - 220) > 2 || Math.Abs(actual.g - 90) > 2 || Math.Abs(actual.b - 40) > 2,
                "Display P3 fixture changes numeric RGB values when normalised to sRGB");
        }
        finally { decoded.Bitmap.Dispose(); }

        using (var thumb = await ImageDecoder.LoadAsync(p3Path, 32))
        {
            var thumbPixel = ReadAvaloniaPixel(thumb, 0, 0);
            var expected = ReadReferenceSrgbPixel(p3Path);
            CheckUi(ChannelNear(thumbPixel, expected, 2),
                "thumbnail path publishes sRGB-normalised pixels before caching");
        }

        _previewCache.Clear();
        var tab = AddImageTab(p3Path);
        await SelectForTestAsync(tab);
        var viewer = (ImageViewer)tab.Content;
        await WaitUiAsync(() => viewer.HasImage, "Display P3 viewer fixture failed to load");
        CheckUi(viewer.ColorConvertedToSrgb && viewer.SourceColorSpace == "Display P3",
            "viewer exposes source-profile conversion state");
        viewer.InvokeInfoButton();
        await viewer.MetadataTask.WaitAsync(TimeSpan.FromSeconds(10));
        CheckUi(viewer.MetadataText.Contains("Source: Display P3", StringComparison.Ordinal)
            && viewer.MetadataText.Contains("Working: sRGB", StringComparison.Ordinal)
            && viewer.MetadataText.Contains("Source → sRGB: applied", StringComparison.Ordinal)
            && viewer.MetadataText.Contains("Display profile: not applied by Myoken yet.", StringComparison.Ordinal),
            "Info panel distinguishes source-profile conversion from display-profile output");
        CaptureForTest("color-managed-p3");
        await SelectForTestAsync(_browser);
        CloseTab(tab);
        _previewCache.Clear();

        Console.WriteLine("PASS: L003b source-profile to sRGB normalization regressions");
    }

    private static void WriteTaggedP3Png(string path, byte r, byte g, byte b)
    {
        using var p3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3)
            ?? throw new InvalidOperationException("Display P3 color space unavailable.");
        var info = new SKImageInfo(48, 24, SKColorType.Rgba8888, SKAlphaType.Opaque, p3);
        using var bitmap = new SKBitmap(info);
        var bytes = new byte[checked(bitmap.RowBytes * bitmap.Height)];
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var offset = y * bitmap.RowBytes + x * 4;
            bytes[offset] = r; bytes[offset + 1] = g; bytes[offset + 2] = b; bytes[offset + 3] = 255;
        }
        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Unable to encode Display P3 test image.");
        using var output = File.Create(path);
        data.SaveTo(output);
    }

    private static (byte r, byte g, byte b) ReadReferenceSrgbPixel(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream)
            ?? throw new InvalidDataException("Cannot decode color fixture.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height,
            SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var bitmap = SKBitmap.Decode(codec, info)
            ?? throw new InvalidDataException("Cannot convert color fixture to sRGB.");
        var color = bitmap.GetPixel(0, 0);
        return (color.Red, color.Green, color.Blue);
    }

    private static (byte r, byte g, byte b) ReadAvaloniaPixel(Bitmap bitmap, int x, int y)
    {
        var ptr = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), ptr, 4, 4);
            return (Marshal.ReadByte(ptr, 2), Marshal.ReadByte(ptr, 1), Marshal.ReadByte(ptr, 0));
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static bool ChannelNear((byte r, byte g, byte b) a, (byte r, byte g, byte b) b, int tolerance) =>
        Math.Abs(a.r - b.r) <= tolerance && Math.Abs(a.g - b.g) <= tolerance && Math.Abs(a.b - b.b) <= tolerance;
}
