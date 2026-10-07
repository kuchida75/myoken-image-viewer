using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Myoken.Core;
using SkiaSharp;

namespace Myoken.Linux;

internal sealed partial class MainWindow
{
    private async Task RunOrientationAndMetadataChecksAsync(string folder)
    {
        var root = Path.GetFullPath(Path.Combine(folder, "..", "orientation-fixtures"));
        Directory.CreateDirectory(root);
        var expected = new[] { "RGBY", "GRYB", "YBGR", "BYRG", "RBGY", "BRYG", "YGBR", "GYRB" };

        for (var value = 1; value <= 8; value++)
        {
            var path = Path.Combine(root, $"orientation-{value}.jpg");
            WriteExifOrientationJpeg(path, value);
            var decoded = OrientedImageDecoder.Decode(path, 256, true, CancellationToken.None);
            try
            {
                var swaps = ImageOrientationInfo.SwapsAxes((ImageOrientation)value);
                CheckUi(decoded.Width == (swaps ? 30 : 40) && decoded.Height == (swaps ? 40 : 30)
                    && decoded.Bitmap.PixelSize == new PixelSize(decoded.Width, decoded.Height),
                    $"EXIF orientation {value} produces oriented dimensions");
                CheckUi(CornerSignature(decoded.Bitmap) == expected[value - 1],
                    $"EXIF orientation {value} maps all four corners correctly");
            }
            finally { decoded.Bitmap.Dispose(); }
        }

        var rightTop = Path.Combine(root, "orientation-6.jpg");
        using (var thumb = await ImageDecoder.LoadAsync(rightTop, 24))
            CheckUi(thumb.PixelSize.Height == 24 && thumb.PixelSize.Width == 18,
                "thumbnail decoder applies orientation before publication");

        var metadata = PortableImageMetadataReader.Read(rightTop);
        CheckUi(metadata.Orientation == ImageOrientation.RightTop
            && metadata.Make == "MYOKEN" && metadata.Model == "L003A"
            && metadata.Software == "Myoken Test",
            "portable metadata reader extracts EXIF orientation and camera fields");

        _previewCache.Clear();
        var tab = AddImageTab(rightTop);
        await SelectForTestAsync(tab);
        var viewer = (ImageViewer)tab.Content;
        await WaitUiAsync(() => viewer.HasImage, "oriented viewer fixture failed to load");
        CheckUi(viewer.View.SourceWidth == 30 && viewer.View.SourceHeight == 40
            && viewer.Orientation == ImageOrientation.RightTop,
            "viewer geometry uses oriented source dimensions");
        viewer.InvokeInfoButton();
        await viewer.MetadataTask.WaitAsync(TimeSpan.FromSeconds(10));
        CheckUi(viewer.MetadataVisible && viewer.MetadataText.Contains("MYOKEN L003A", StringComparison.Ordinal)
            && viewer.MetadataText.Contains("Rotate 90° CW (6)", StringComparison.Ordinal),
            "Info panel exposes basic portable metadata and orientation");
        CaptureForTest("orientation-metadata");
        await SelectForTestAsync(_browser);
        CloseTab(tab);
        _previewCache.Clear();

        Console.WriteLine("PASS: L003a EXIF orientation, thumbnail/viewer geometry and metadata-panel regressions");
    }

    private static string CornerSignature(Bitmap bitmap)
    {
        var x = Math.Min(4, Math.Max(0, bitmap.PixelSize.Width / 4));
        var y = Math.Min(4, Math.Max(0, bitmap.PixelSize.Height / 4));
        return Classify(bitmap, x, y)
            + Classify(bitmap, bitmap.PixelSize.Width - 1 - x, y)
            + Classify(bitmap, x, bitmap.PixelSize.Height - 1 - y)
            + Classify(bitmap, bitmap.PixelSize.Width - 1 - x, bitmap.PixelSize.Height - 1 - y);
    }

    private static string Classify(Bitmap bitmap, int x, int y)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), buffer, 4, 4);
            var b = Marshal.ReadByte(buffer, 0);
            var g = Marshal.ReadByte(buffer, 1);
            var r = Marshal.ReadByte(buffer, 2);
            if (r > 140 && g > 140 && b < 130) return "Y";
            if (r >= g && r >= b) return "R";
            if (g >= r && g >= b) return "G";
            return "B";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void WriteExifOrientationJpeg(string path, int orientation)
    {
        using var bitmap = new SKBitmap(40, 30, SKColorType.Bgra8888, SKAlphaType.Opaque);
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = x < 20
                ? (y < 15 ? SKColors.Red : SKColors.Blue)
                : (y < 15 ? SKColors.Lime : SKColors.Yellow);
            bitmap.SetPixel(x, y, color);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        var jpeg = data.ToArray();
        var app1 = BuildExifApp1(orientation);
        using var output = File.Create(path);
        output.Write(jpeg, 0, 2);
        output.Write(app1, 0, app1.Length);
        output.Write(jpeg, 2, jpeg.Length - 2);
    }

    private static byte[] BuildExifApp1(int orientation)
    {
        var make = Encoding.ASCII.GetBytes("MYOKEN\0");
        var model = Encoding.ASCII.GetBytes("L003A\0");
        var software = Encoding.ASCII.GetBytes("Myoken Test\0");
        const int entries = 4;
        var dataOffset = 8 + 2 + entries * 12 + 4;
        var makeOffset = dataOffset;
        var modelOffset = makeOffset + make.Length;
        var softwareOffset = modelOffset + model.Length;

        using var tiff = new MemoryStream();
        using (var writer = new BinaryWriter(tiff, Encoding.ASCII, true))
        {
            writer.Write((byte)'I'); writer.Write((byte)'I');
            writer.Write((ushort)42); writer.Write((uint)8);
            writer.Write((ushort)entries);
            WriteAsciiEntry(writer, 0x010F, make.Length, makeOffset);
            WriteAsciiEntry(writer, 0x0110, model.Length, modelOffset);
            writer.Write((ushort)0x0112); writer.Write((ushort)3); writer.Write((uint)1);
            writer.Write((ushort)orientation); writer.Write((ushort)0);
            WriteAsciiEntry(writer, 0x0131, software.Length, softwareOffset);
            writer.Write((uint)0);
            writer.Write(make); writer.Write(model); writer.Write(software);
        }

        var tiffBytes = tiff.ToArray();
        var payload = new byte[6 + tiffBytes.Length];
        Encoding.ASCII.GetBytes("Exif\0\0").CopyTo(payload, 0);
        tiffBytes.CopyTo(payload, 6);
        var length = payload.Length + 2;
        var segment = new byte[payload.Length + 4];
        segment[0] = 0xFF; segment[1] = 0xE1;
        segment[2] = (byte)(length >> 8); segment[3] = (byte)length;
        payload.CopyTo(segment, 4);
        return segment;
    }

    private static void WriteAsciiEntry(BinaryWriter writer, ushort tag, int count, int offset)
    {
        writer.Write(tag); writer.Write((ushort)2); writer.Write((uint)count); writer.Write((uint)offset);
    }
}
