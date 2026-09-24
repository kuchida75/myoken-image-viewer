using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
using ZonerInspiredViewer;
using ImageMetadata = ZonerInspiredViewer.ImageMetadata;

internal static partial class ViewerRegressionTests
{
    private static void CheckMetadataOverlayBounds(MainWindow window)
    {
        window.UpdateLayout();
        var overlay = Field<Border>(window, "_imageMetadataOverlay");
        var view = Field<Grid>(window, "_imageView");
        Assert(overlay.Visibility == Visibility.Visible, "metadata overlay is visible");
        AssertInside(overlay, view);
        Point origin = overlay.TranslatePoint(new Point(), view);
        AssertNear(origin.X, 16, "metadata left inset");
        AssertNear(origin.Y + overlay.ActualHeight, view.ActualHeight - overlay.Margin.Bottom, "metadata bottom inset");
        var navigator = Field<Border>(window, "_imageNavigatorOverlay");
        if (navigator.IsVisible) Assert(!new Rect(origin, overlay.RenderSize).IntersectsWith(
            new Rect(navigator.TranslatePoint(new Point(), view), navigator.RenderSize)), "metadata clears image navigator");
        foreach (TextBlock value in Field<TextBlock[]>(window, "_imageMetadataValues"))
        {
            AssertInside(value, overlay);
            Point at = value.TranslatePoint(new Point(2, 2), view);
            Assert(view.InputHitTest(at) != value && view.InputHitTest(at) != overlay, "metadata passes mouse input through");
        }
    }

    private static void WaitOverlayMetadata(MainWindow window, string path)
    {
        Wait(delegate
        {
            var metadata = Field<ImageMetadata>(window, "_imageOverlayMetadata");
            return Ready(window, path) && metadata != null && metadata.Path == path
                && Field<FileRevision>(window, "_imageOverlayMetadataRevision").Equals(Field<FileRevision>(window, "_displayedImageRevision"));
        }, "metadata matches displayed image " + Path.GetFileName(path));
    }

    private static void CheckMetadataRows(MainWindow window, string path, int width, int height)
    {
        WaitOverlayMetadata(window, path);
        TextBlock[] rows = Field<TextBlock[]>(window, "_imageMetadataValues");
        var file = new FileInfo(path);
        Assert(rows.Length == 6 && rows.All(row => !String.IsNullOrWhiteSpace(row.Text)), "six populated metadata rows");
        Assert(rows[0].Text == ImageExtensions.FormatBytes(file.Length), "dynamic file size uses actual bytes and scaled units");
        Assert(rows[1].Text.EndsWith("(" + file.Extension.ToLowerInvariant() + ")"), "file type includes extension");
        Assert(rows[2].Text == file.Name, "current filename shown");
        Assert(rows[3].Text == file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"), "local modified date/time shown");
        Assert(rows[4].Text == width + " x " + height + " px", "original pixel resolution shown");
        Assert(rows[5].Text.Contains("bits/"), "bit depth has explicit units");
        CheckImageStatusBar(window, path);
    }

    private static void CheckImageStatusBar(MainWindow window, string path)
    {
        var statusPath = Field<TextBlock>(window, "_imageStatusPath");
        var details = Field<TextBlock[]>(window, "_imageStatusValues");
        var values = (string[])Invoke(window, "DisplayedImageDetails");
        string[] labels = { "Size: ", "Type: ", "Name: ", "Modified: ", "Resolution: ", "Depth: " };
        Assert(statusPath.IsVisible && statusPath.Text == path && (string)statusPath.ToolTip == path, "status bar shows complete current path with full tooltip");
        window.UpdateLayout();
        var canvas = Field<Canvas>(window, "_imageCanvas");
        var root = (FrameworkElement)window.Content;
        double bottom = canvas.TranslatePoint(new Point(0, canvas.ActualHeight), root).Y;
        Assert(statusPath.TranslatePoint(new Point(), root).Y >= bottom, "status bar is below image, not an overlay");
        AssertInside(statusPath, root);
        for (int i = 0; i < details.Length; i++)
        {
            Assert(details[i].IsVisible && details[i].Text == labels[i] + values[i], "status field uses current revision: " + labels[i]);
            AssertInside(details[i], root);
        }
    }

    private static void CheckOverlayBitDepth(string root)
    {
        var registry = new DecoderRegistry(delegate { return false; });
        string gray = Path.Combine(root, "Gray16.png");
        byte[] pixels = new byte[80 * 60 * 2]; new Random(312).NextBytes(pixels);
        var bitmap = BitmapSource.Create(80, 60, 96, 96, PixelFormats.Gray16, null, pixels, 80 * 2);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(gray)) png.Save(stream);
        Assert(registry.ReadMetadata(gray).BitDepth == "16 bits/pixel (Gray16)", "16-bit grayscale PNG metadata retains native precision");
        ModernImageDecoder.Initialize();
        foreach (var item in new Dictionary<string, MagickFormat> { { ".jpg", MagickFormat.Jpeg }, { ".webp", MagickFormat.WebP },
            { ".avif", MagickFormat.Avif }, { ".jxl", MagickFormat.Jxl }, { ".gif", MagickFormat.Gif } })
        {
            string path = Path.Combine(root, "Depth" + item.Key);
            using (var image = CodecFixture(MagickColors.Crimson, 80, 60)) image.Write(path, item.Value);
            ImageMetadata metadata = registry.ReadMetadata(path);
            Assert(!String.IsNullOrEmpty(metadata.BitDepth) && !String.IsNullOrEmpty(metadata.FileType), "format depth/type " + item.Key);
            if (item.Key == ".jpg") Assert(metadata.BitDepth == "24 bits/pixel (Bgr24)", "JPEG native depth");
            else Assert(metadata.BitDepth == "8 bits/channel" + (item.Key == ".gif" ? " (palette)" : ""), "modern header depth " + item.Key);
        }
        string highDepth = Path.Combine(root, "Depth16.jxl");
        using (var image = CodecFixture(MagickColors.Crimson, 80, 60)) { image.Depth = 16; image.Write(highDepth, MagickFormat.Jxl); }
        Assert(registry.ReadMetadata(highDepth).BitDepth == "16 bits/channel", "16-bit JXL header is not reduced to display depth");
        string heic = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "tools", "Fixtures", "libheif-example.heic"));
        Assert(registry.ReadMetadata(heic).BitDepth == "8 bits/channel", "HEIC header depth");
        Assert(ImageExtensions.FormatBytes(1024).EndsWith("KB") && ImageExtensions.FormatBytes(1024 * 1024).EndsWith("MB")
            && ImageExtensions.FormatBytes(1024L * 1024 * 1024).EndsWith("GB"), "file size scales by magnitude");
    }

    private sealed class OverlayGatedDecoder : IImageDecoder
    {
        private readonly WicImageDecoder _inner = new WicImageDecoder(delegate { return false; });
        public readonly ManualResetEventSlim Captured = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
        private int _held;
        public string PathToHold;
        public string Name { get { return "Gated metadata test decoder"; } }
        public bool CanDecode(string extension) { return _inner.CanDecode(extension); }
        public BitmapSource Decode(string path, int width, CancellationToken token) { return _inner.Decode(path, width, token); }
        public ImageMetadata ReadMetadata(string path)
        {
            ImageMetadata value = _inner.ReadMetadata(path);
            if (path == PathToHold && Interlocked.Exchange(ref _held, 1) == 0)
            {
                Captured.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Metadata test gate timed out.");
            }
            return value;
        }
    }

    private static void RunMetadataOverlayChecks()
    {
        string root = Path.Combine(Root, "metadata-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        CheckOverlayBitDepth(root);
        string first = Path.Combine(photos, "A.png"), second = Path.Combine(photos, "B.png");
        string longName = Path.Combine(photos, new string('W', 80) + ".png");
        MakeImage(first, 2200, 1500, 96); MakeImage(second, 300, 200, 96); MakeImage(longName, 900, 600, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 780 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        var overlay = Field<Border>(window, "_imageMetadataOverlay");
        var values = Field<TextBlock[]>(window, "_imageMetadataValues");
        Assert(!Field<bool>(window, "_imageMetadataOverlayEnabled") && overlay.Visibility == Visibility.Collapsed, "overlay defaults off");
        PressKey(window, Key.I);
        Assert(!Field<bool>(window, "_imageMetadataOverlayEnabled"), "I does not toggle in browser mode");
        Invoke(window, "OpenImageTab", first, true); WaitOverlayMetadata(window, first); WaitScan(window);
        CheckImageStatusBar(window, first);
        Assert(overlay.Visibility == Visibility.Collapsed, "status details do not enable Info overlay");
        Render((FrameworkElement)window.Content, "image-status-dark.png");
        var canvas = Field<Canvas>(window, "_imageCanvas"); canvas.Focus();
        Size frame = canvas.RenderSize; IInputElement focus = Keyboard.FocusedElement;
        PressKey(window, Key.I); Pump(); CheckMetadataRows(window, first, 2200, 1500); CheckMetadataOverlayBounds(window);
        Assert(canvas.RenderSize == frame && Keyboard.FocusedElement == focus, "overlay does not resize viewer or steal focus");
        Assert(!overlay.IsHitTestVisible && !overlay.Focusable && ((SolidColorBrush)overlay.Background).Color.A < 255, "passive translucent overlay");
        Assert(!Field<FrameworkElement>(window, "_metadataPanel").IsVisible, "I does not open the right metadata panel");
        var repeat = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.I)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        typeof(KeyEventArgs).GetMethod("SetRepeat", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(repeat, new object[] { true });
        window.RaiseEvent(repeat);
        Assert(repeat.Handled && overlay.Visibility == Visibility.Visible, "holding I does not flicker the overlay");
        PumpZoomFor(1300); Assert(overlay.Visibility == Visibility.Visible, "metadata has no zoom-style expiry");
        Render((FrameworkElement)window.Content, "metadata-overlay-dark.png");
        Field<TextBox>(window, "_searchBox").Focus(); PressKey(window, Key.I);
        Assert(overlay.Visibility == Visibility.Visible, "typing I into search does not toggle metadata");
        canvas.Focus(); PressKey(window, Key.I); Assert(overlay.Visibility == Visibility.Collapsed, "second I hides metadata");
        CheckImageStatusBar(window, first);
        PressKey(window, Key.I);
        Invoke(window, "SetZoomOneToOne"); Invoke(window, "ZoomFromCenter", 2.0);
        Assert((bool)Invoke(window, "BeginImagePan", new Point(170, 140)), "pan available with metadata overlay");
        Invoke(window, "UpdateImagePan", new Point(210, 180), true); Invoke(window, "EndImagePan");
        CheckMetadataOverlayBounds(window); CheckMetadataRows(window, first, 2200, 1500);
        PressKey(window, Key.Oem6); Pump(); CheckMetadataOverlayBounds(window); CheckMetadataRows(window, first, 2200, 1500);
        Wheel(window, -120); WaitOverlayMetadata(window, second); CheckMetadataRows(window, second, 300, 200);
        Assert(overlay.Visibility == Visibility.Visible, "wheel navigation retains metadata toggle");
        PressKey(window, Key.Enter); WaitScan(window);
        Assert(overlay.Visibility == Visibility.Collapsed && Field<bool>(window, "_imageMetadataOverlayEnabled"), "browser hides metadata but retains preference");
        Assert(Field<TextBlock>(window, "_imageStatusPath").Visibility == Visibility.Collapsed
            && Field<TextBlock[]>(window, "_imageStatusValues").All(value => value.Text == ""), "browser clears image-specific footer details");
        PressKey(window, Key.I); Assert(Field<bool>(window, "_imageMetadataOverlayEnabled"), "browser I leaves saved toggle unchanged");
        Invoke(window, "OpenBrowserImage", second); WaitOverlayMetadata(window, second); CheckMetadataRows(window, second, 300, 200);

        MakeImage(second, 820, 460, 96); File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddSeconds(2));
        Wait(delegate { return values[4].Text == "820 x 460 px" && Field<FileRevision>(window, "_displayedImageRevision").Matches(second); }, "watcher updates displayed metadata");
        CheckMetadataRows(window, second, 820, 460);
        string renamed = Path.Combine(photos, "Renamed.png"); File.Move(second, renamed);
        Invoke(window, "ReplacePathReferences", second, renamed); CheckMetadataRows(window, renamed, 820, 460);

        var decoders = Field<List<IImageDecoder>>(services.Decoders, "_decoders");
        var gate = new OverlayGatedDecoder { PathToHold = first }; decoders.Insert(0, gate);
        try
        {
            Invoke(window, "OpenImageTab", first, true); Wait(delegate { return gate.Captured.IsSet; }, "hold old tab metadata");
            Invoke(window, "OpenImageTab", renamed, true); WaitOverlayMetadata(window, renamed);
            gate.Release.Set(); PumpZoomFor(350); CheckMetadataRows(window, renamed, 820, 460);
        }
        finally { gate.Release.Set(); decoders.Remove(gate); }

        gate = new OverlayGatedDecoder { PathToHold = first }; decoders.Insert(0, gate);
        var watcher = Field<FileSystemWatcher>(window, "_watcher");
        try
        {
            Invoke(window, "OpenImageTab", first, true); Wait(delegate { return gate.Captured.IsSet && Ready(window, first); }, "hold old file revision metadata");
            watcher = Field<FileSystemWatcher>(window, "_watcher"); watcher.EnableRaisingEvents = false;
            MakeImage(first, 1250, 850, 96); File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddSeconds(4));
            Invoke(window, "LoadImage", first); gate.Release.Set();
            CheckMetadataRows(window, first, 1250, 850);
        }
        finally { gate.Release.Set(); decoders.Remove(gate); watcher.EnableRaisingEvents = true; }

        Invoke(window, "OpenImageTab", longName, true); WaitOverlayMetadata(window, longName);
        window.Width = 980; window.Height = 640; ThemeManager.SetDarkTheme(false); Pump();
        Invoke(window, "SetMetadataPanelVisible", true); Pump(); CheckMetadataOverlayBounds(window);
        CheckImageStatusBar(window, longName);
        Assert(Field<TextBlock[]>(window, "_imageStatusValues")[2].TextTrimming == TextTrimming.CharacterEllipsis, "long footer filename is bounded with complete tooltip");
        Assert(values[2].ActualHeight > values[0].ActualHeight, "long unbroken filename wraps in narrow view");
        Render((FrameworkElement)window.Content, "metadata-overlay-light-narrow.png");
        Invoke(window, "SetMetadataPanelVisible", false); ThemeManager.SetDarkTheme(true); canvas.Focus();
        PressKey(window, Key.U); Pump(); CheckMetadataOverlayBounds(window);
        Render((FrameworkElement)window.Content, "metadata-overlay-compact.png");
        PressKey(window, Key.F11); Pump(); CheckMetadataOverlayBounds(window);
        Render((FrameworkElement)window.Content, "metadata-overlay-fullscreen.png");
        PressKey(window, Key.Escape); Pump(); PressKey(window, Key.U); Pump();

        Invoke(window, "ShowConfigure"); Pump();
        var check = Field<CheckBox>(window, "_configureImageMetadata");
        Assert(check.IsChecked == true && System.Windows.Automation.AutomationProperties.GetAcceleratorKey(check) == "I"
            && check.ToolTip is ToolTip, "Configure exposes saved toggle and shortcut");
        check.IsChecked = false; check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert(!Field<bool>(window, "_imageMetadataOverlayEnabled"), "Configure disables overlay from browser mode");
        check.IsChecked = true; check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Field<Window>(window, "_configureWindow").Close(); Pump();
        Invoke(window, "OpenImageTab", first, true); CheckMetadataRows(window, first, 1250, 850);
        services.Sessions.SaveNamed("Metadata visible", Capture(window));
        string backup = Path.Combine(root, "metadata-backup.json");
        WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
        Assert(ViewerBackupStore.Load(backup).Workspace.ImageMetadataOverlayVisible
            && services.Sessions.LoadNamed("Metadata visible").ImageMetadataOverlayVisible, "backup and named session retain overlay");
        Invoke(window, "SetImageMetadataOverlay", false);
        WaitBackup((Task<int>)Invoke(window, "ImportBackupFileAsync", backup, false));
        WaitOverlayMetadata(window, first);
        Assert(overlay.Visibility == Visibility.Visible, "import restores enabled overlay");
        window.Close(); Pump(); Assert(overlay.Visibility == Visibility.Collapsed, "close clears metadata display");
        window = new MainWindow(services); window.Show(); WaitOverlayMetadata(window, first);
        Assert(Field<Border>(window, "_imageMetadataOverlay").IsVisible && !Field<bool>(window, "_isCompactMode"), "restart restores overlay in normal mode");
        window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true);
        Console.WriteLine("PASS: I metadata toggle, six live rows, native/header bit depth, passive layout, watcher/rename/stale reads, narrow/theme/fullscreen, Configure and session/backup restore.");
    }
}
