using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ImageMagick;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static MagickImage CodecFixture(IMagickColor<byte> color, int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = color.A; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
        using (var stream = new MemoryStream())
        {
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(stream); stream.Position = 0;
            return new MagickImage(stream);
        }
    }

    private static void RunUiFormatChecks()
    {
        string root = Path.Combine(Root, "formats-" + Guid.NewGuid().ToString("N").Substring(0, 10));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        ModernImageDecoder.Initialize();
        var formats = new Dictionary<string, MagickFormat> { { ".jpg", MagickFormat.Jpeg }, { ".jpeg", MagickFormat.Jpeg },
            { ".png", MagickFormat.Png }, { ".webp", MagickFormat.WebP }, { ".avif", MagickFormat.Avif },
            { ".jxl", MagickFormat.Jxl }, { ".gif", MagickFormat.Gif } };
        foreach (var format in formats)
        {
            string path = Path.Combine(photos, "Image" + format.Key);
            using (var image = CodecFixture(MagickColors.Crimson, 91, 63))
            {
                image.Quality = 90;
                var exif = new ExifProfile(); exif.SetValue(ExifTag.Make, "Codec fixture camera"); image.SetProfile(exif);
                image.Write(path, format.Value);
            }
        }
        string fixture = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "tools", "Fixtures", "libheif-example.heic"));
        File.Copy(fixture, Path.Combine(photos, "Image.heic")); File.Copy(fixture, Path.Combine(photos, "Image.heif"));
        var registry = new DecoderRegistry(delegate { return false; });
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        foreach (string path in Directory.GetFiles(photos))
        {
            Assert(ImageExtensions.IsBrowsableImage(path.ToUpperInvariant()), "browsable extension " + path);
            BitmapSource decoded = registry.Decode(path, 0, CancellationToken.None);
            Assert(decoded.IsFrozen && decoded.PixelWidth > 0 && decoded.PixelHeight > 0, "detached format decode " + path);
            Assert(ToolPixels(decoded).Distinct().Count() > 2, "nonblank real image pixels " + path);
            Assert(registry.ReadMetadata(path).PixelWidth == decoded.PixelWidth, "metadata dimensions " + path);
            var thumbnail = services.Thumbnails.GetThumbnailAsync(ImageFileItem.FromPath(path), 128, CancellationToken.None);
            WaitBackup(thumbnail); Assert(thumbnail.Result != null && Math.Max(thumbnail.Result.PixelWidth, thumbnail.Result.PixelHeight) <= 128, "thumbnail " + path);
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        Assert(registry.ReadMetadata(Path.Combine(photos, "Image.webp")).Rows.Any(row => row.Value == "Codec fixture camera"), "modern EXIF camera metadata");
        string alphaWebp = Path.Combine(root, "transparent.webp");
        using (var transparent = CodecFixture(MagickColors.Transparent, 40, 30))
        {
            transparent.SetProfile(ColorProfiles.SRGB); transparent.Write(alphaWebp, MagickFormat.WebP);
        }
        var iccPixels = ToolPixels(new ModernImageDecoder(delegate { return true; }).Decode(alphaWebp, 0, CancellationToken.None));
        Assert(Enumerable.Range(0, iccPixels.Length / 4).All(i => iccPixels[i * 4 + 3] == 0), "bundled WebP alpha and optional ICC conversion");
        bool canceled = false;
        try { registry.Decode(Path.Combine(photos, "Image.webp"), 64, new CancellationToken(true)); }
        catch (OperationCanceledException) { canceled = true; }
        Assert(canceled, "modern codec honors pre-cancellation");
        string disguised = Path.Combine(root, "not-an-image.webp"); File.WriteAllText(disguised, "<svg xmlns='http://www.w3.org/2000/svg' width='90' height='60'/>");
        bool rejected = false; try { registry.Decode(disguised, 0, CancellationToken.None); } catch (Exception) { rejected = true; }
        Assert(rejected, "forced raster decoder rejects disguised vector input");
        using (new FileStream(disguised, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }

        string gif = Path.Combine(photos, "Animated.gif"), finiteGif = Path.Combine(root, "finite.gif");
        using (var animation = new MagickImageCollection())
        {
            foreach (var color in new[] { MagickColors.Red, MagickColors.Lime, MagickColors.Blue })
            {
                var frame = CodecFixture(color, 120, 80); frame.AnimationDelay = 10; frame.AnimationIterations = 0;
                animation.Add(frame);
            }
            animation.Write(gif, MagickFormat.Gif);
            animation[0].AnimationIterations = 2; animation.Write(finiteGif, MagickFormat.Gif);
        }
        var decoder = new ModernImageDecoder(delegate { return false; });
        var frames = decoder.ReadAnimation(gif, 1024 * 1024, CancellationToken.None);
        Assert(frames != null && frames.Frames.Count == 3 && frames.Delays.All(delay => delay == 100) && frames.Iterations == 0, "GIF frame timing and loop count");
        Assert(ToolPixels(frames.Frames[0])[2] > 240 && ToolPixels(frames.Frames[1])[1] > 240, "coalesced GIF frame pixels");
        bool limited = false; try { decoder.ReadAnimation(gif, 100, CancellationToken.None); } catch (NotSupportedException) { limited = true; }
        Assert(limited && registry.Decode(gif, 0, CancellationToken.None) != null, "oversized animation has usable first frame");
        string partialGif = Path.Combine(root, "partial.gif");
        using (var partial = CodecFixture(MagickColors.Red, 20, 20))
        {
            partial.Page = new MagickGeometry { Width = 120, Height = 80, X = 30, Y = 20 };
            partial.BackgroundColor = MagickColors.Transparent; partial.Write(partialGif, MagickFormat.Gif);
        }
        var canvas = registry.Decode(partialGif, 0, CancellationToken.None);
        Assert(canvas.PixelWidth == 120 && canvas.PixelHeight == 80 && registry.ReadMetadata(partialGif).PixelWidth == 120, "offset GIF frame uses logical canvas for display and metadata");
        string disposalGif = Path.Combine(root, "disposal.gif");
        using (var animation = new MagickImageCollection())
        {
            animation.Add(CodecFixture(MagickColors.Red, 120, 80));
            var overlay = CodecFixture(MagickColors.Lime, 20, 20);
            overlay.Page = new MagickGeometry { Width = 120, Height = 80, X = 40, Y = 30 };
            overlay.GifDisposeMethod = GifDisposeMethod.Previous; animation.Add(overlay);
            var last = CodecFixture(MagickColors.Blue, 20, 20);
            last.Page = new MagickGeometry { Width = 120, Height = 80 }; animation.Add(last);
            animation.Write(disposalGif, MagickFormat.Gif);
        }
        var disposed = decoder.ReadAnimation(disposalGif, 1024 * 1024, CancellationToken.None);
        int overlayPixel = (35 * 120 + 45) * 4;
        Assert(ToolPixels(disposed.Frames[1])[overlayPixel + 1] > 240 && ToolPixels(disposed.Frames[2])[overlayPixel + 2] > 240
            && ToolPixels(disposed.Frames[2])[overlayPixel + 1] < 10, "GIF Previous disposal restores underlying image between offset frames");

        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 780 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        foreach (string path in Directory.GetFiles(photos))
        {
            Invoke(window, "OpenImageTab", path, true); Wait(delegate { return Ready(window, path); }, "viewer opens " + path);
        }
        Invoke(window, "OpenImageTab", gif, true);
        Wait(delegate { return Field<ImageAnimation>(window, "_animation") != null; }, "GIF playback loaded");
        var before = Field<Image>(window, "_mainImage").Source;
        Wait(delegate { return !Object.ReferenceEquals(before, Field<Image>(window, "_mainImage").Source); }, "GIF playback visibly advances");
        Invoke(window, "RotateImage", 1);
        Assert(Capture(window).Tabs.Any(tab => tab.Path == gif && tab.RotationQuarterTurns == 1), "GIF rotation remains view-only");
        Invoke(window, "OpenImageTab", finiteGif, true);
        Wait(delegate { return Ready(window, finiteGif) && Field<ImageAnimation>(window, "_animation") != null; }, "finite GIF loaded");
        Wait(delegate { return !Field<System.Windows.Threading.DispatcherTimer>(window, "_animationTimer").IsEnabled; }, "finite GIF stops at loop count");
        Assert(Field<int>(window, "_animationFrame") == 2, "finite GIF leaves final frame visible");
        Invoke(window, "OpenImageTab", gif, true); Wait(delegate { return Ready(window, gif); }, "return to animated folder");
        PressKey(window, Key.Enter); Pump();
        Assert(Field<ImageAnimation>(window, "_animation") == null && !Field<System.Windows.Threading.DispatcherTimer>(window, "_animationTimer").IsEnabled, "browser return releases GIF frames and timer");
        foreach (string name in new[] { "_helpButton", "_configureButton", "_renameButton", "_deleteButton", "_sessionsButton" })
        {
            var button = Field<Button>(window, name);
            Assert(button.ToolTip is ToolTip && !String.IsNullOrEmpty(AutomationProperties.GetName(button)), "discoverable command " + name);
            Assert(ToolTipService.GetShowOnDisabled(button), "disabled commands retain explanations " + name);
        }
        Assert(AutomationProperties.GetAcceleratorKey(Field<Button>(window, "_configureButton")) == "Ctrl+,", "Configure shortcut exposed");
        Render((FrameworkElement)window.Content, "formats-toolbar-dark.png");
        window.Width = 980; window.Height = 640; Pump();
        Render((FrameworkElement)window.Content, "formats-toolbar-narrow.png");
        var tooltip = (ToolTip)Field<Button>(window, "_configureButton").ToolTip;
        tooltip.PlacementTarget = Field<Button>(window, "_configureButton"); tooltip.IsOpen = true; Pump();
        Render(tooltip, "formats-command-tooltip.png"); tooltip.IsOpen = false;
        Invoke(window, "ShowHelp"); var help = Field<HelpWindow>(window, "_helpWindow"); Pump();
        var pages = Field<List<FrameworkElement>>(help, "_pages");
        Assert(((RichTextBox)pages[0]).Document.Blocks.OfType<Table>().Count() >= 5, "Help has semantic shortcut tables");
        Assert(HelpDocument.Text((RichTextBox)pages[3]).Contains("HEIC / HEIF"), "Help lists actual format support");
        Render((FrameworkElement)help.Content, "formats-help-shortcuts-dark.png");
        CheckShortcutLayout((RichTextBox)pages[0]);
        help.SelectSection(2); Pump(); Render((FrameworkElement)help.Content, "formats-help-notes-dark.png");
        help.SelectSection(3); Pump(); Render((FrameworkElement)help.Content, "formats-help-formats-dark.png");
        ThemeManager.SetDarkTheme(false); help.Width = 580; help.Height = 540; help.SelectSection(0); Pump();
        Render((FrameworkElement)help.Content, "formats-help-light-narrow.png");
        CheckShortcutLayout((RichTextBox)pages[0]);
        help.Close(); ThemeManager.SetDarkTheme(true); window.Close(); Pump();
        services = AppServices.Create(Path.Combine(root, "Profile")); window = new MainWindow(services); window.Show(); WaitScan(window);
        Assert(Capture(window).Tabs.Count >= 10, "all modern format tabs restore"); window.Close(); Pump();
        Console.WriteLine("PASS: real JPEG/PNG/WebP/AVIF/JXL/HEIC/HEIF/GIF decode, thumbnails, metadata, GIF playback, icon descriptions and structured Help.");
    }

    private static void CheckShortcutLayout(RichTextBox view)
    {
        var table = view.Document.Blocks.OfType<Table>().First();
        var text = table.RowGroups[0].Rows[0].Cells[1].Blocks.FirstBlock;
        Rect start = text.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        Rect end = text.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        Assert(end.Y - start.Y < 50 && start.X < view.ActualWidth - 200, "shortcut descriptions have a readable column in wide/narrow Help");
    }
}
