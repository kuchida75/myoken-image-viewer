using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr large, IntPtr small, uint icons);

    private static void RunEnhancementChecks()
    {
        string folder = Path.Combine(Root, "enhance-images");
        Directory.CreateDirectory(folder);
        string dark = Path.Combine(folder, "Dark.png"), bright = Path.Combine(folder, "Bright.png");
        BitmapSource darkBitmap = MakeEnhanceFixture(dark, "dark");
        BitmapSource brightBitmap = MakeEnhanceFixture(bright, "bright");
        BitmapSource soft = MakeEnhanceFixture(Path.Combine(folder, "Soft.png"), "soft");
        BitmapSource sharp = MakeEnhanceFixture(Path.Combine(folder, "Sharp.png"), "sharp");
        BitmapSource noisy = MakeEnhanceFixture(Path.Combine(folder, "Noisy.png"), "noise");
        var darkAnalysis = QuickEnhance.Analyze(darkBitmap, 8, CancellationToken.None);
        var brightAnalysis = QuickEnhance.Analyze(brightBitmap, 8, CancellationToken.None);
        var softAnalysis = QuickEnhance.Analyze(soft, 8, CancellationToken.None);
        var sharpAnalysis = QuickEnhance.Analyze(sharp, 8, CancellationToken.None);
        var noisyAnalysis = QuickEnhance.Analyze(noisy, 8, CancellationToken.None);
        Assert(darkAnalysis.ExposureStops > 0.5 && brightAnalysis.ExposureStops < -0.5, "exposure responds to dark and bright images");
        Assert(softAnalysis.Sharpness > sharpAnalysis.Sharpness && softAnalysis.Sharpness > noisyAnalysis.Sharpness,
            "adaptive sharpening distinguishes soft, sharp and noisy: " + softAnalysis.Sharpness + ", " + sharpAnalysis.Sharpness + ", " + noisyAnalysis.Sharpness);
        var transparent = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        transparent.Freeze();
        var emptyAnalysis = QuickEnhance.Analyze(transparent, 8, CancellationToken.None);
        Assert(emptyAnalysis.ExposureStops == 0 && emptyAnalysis.Sharpness == 0, "transparent images remain unchanged");
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool canceled = false;
        try { QuickEnhance.Analyze(soft, 8, cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
        cancellation.Dispose();
        Assert(canceled, "analysis honors cancellation");
        var sequential = QuickEnhance.Analyze(soft, 1, CancellationToken.None);
        Assert(sequential.Sharpness == softAnalysis.Sharpness && sequential.ExposureStops == softAnalysis.ExposureStops,
            "parallel analysis is deterministic");
        CheckSharpeningAndAlpha();

        byte[] original = File.ReadAllBytes(dark);
        var services = AppServices.Create(Path.Combine(Root, "enhance-profile"));
        var initial = new SessionState { LastFolder = folder, ActiveTabId = dark, WindowWidth = 1240, WindowHeight = 780 };
        initial.Tabs.Add(new SessionTabDto { Id = dark, Path = dark, FolderPath = folder });
        services.Sessions.Save(initial);
        var window = new MainWindow(services);
        window.Show();
        Wait(delegate { return Ready(window, dark); }, "enhancement fixture");
        WaitScan(window);
        Assert(window.Icon != null && ((BitmapSource)window.Icon).PixelWidth == 256, "window uses high-resolution icon");
        string iconPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "src", "ZonerInspiredViewer", "Assets", "AppIcon.ico"));
        var icon = new IconBitmapDecoder(new Uri(iconPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert(icon.Frames.Count == 7 && icon.Frames.Any(frame => frame.PixelWidth == 16)
            && icon.Frames.Any(frame => frame.PixelWidth == 256), "icon contains small through high-DPI sizes");
        string executable = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "ZenImageViewer", "ZenImageViewer.exe"));
        Assert(ExtractIconEx(executable, -1, IntPtr.Zero, IntPtr.Zero, 0) > 0, "executable has native Windows icon resource");
        var convertedIcon = new FormatConvertedBitmap((BitmapSource)window.Icon, PixelFormats.Bgra32, null, 0);
        var corner = new byte[4];
        convertedIcon.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
        Assert(corner[3] == 0, "icon preserves transparent background");

        Image image = Field<Image>(window, "_mainImage");
        BitmapSource source = (BitmapSource)image.Source;
        byte[] before = RenderImagePixels(window);
        Render((FrameworkElement)window.Content, "enhance-before.png");
        SetEnhance(window, true);
        WaitEnhance(window);
        byte[] after = RenderImagePixels(window);
        Assert(after.Sum(b => (long)b) > before.Sum(b => (long)b) + 10000, "GPU-compatible effect visibly lifts dark exposure");
        Assert(Object.ReferenceEquals(source, image.Source), "enhancement preserves decoded source bitmap");
        Render((FrameworkElement)window.Content, "enhance-after.png");
        SetEnhance(window, false);
        Pump();
        Assert(image.Effect == null && before.SequenceEqual(RenderImagePixels(window)), "toggle returns pixel-identical original view");

        Invoke(window, "OpenImageTab", bright, true);
        Wait(delegate { return Ready(window, bright); }, "bright exposure fixture");
        long brightBefore = RenderImagePixels(window).Sum(b => (long)b);
        SetEnhance(window, true);
        WaitEnhance(window);
        Assert(RenderImagePixels(window).Sum(b => (long)b) < brightBefore - 10000, "bright exposure is reduced");
        SetEnhance(window, false);
        SetEnhance(window, true);
        Invoke(window, "ActivateImageTab", dark);
        Wait(delegate { return Ready(window, dark); }, "switch during analysis");
        WaitEnhance(window);
        Assert(((Point)image.Effect.GetValue(QuickEnhanceEffect.AdjustmentsProperty)).X > 1,
            "new tab analyzes its own exposure instead of using stale bright-image analysis");
        SetEnhance(window, true);
        SetEnhance(window, false);
        Pump();
        Assert(image.Effect == null && Field<CancellationTokenSource>(window, "_enhancementRequest") == null,
            "cancel before analysis completion leaves original");

        SetEnhance(window, true);
        WaitEnhance(window);
        Invoke(window, "RotateImage", 1);
        Invoke(window, "ZoomFromCenter", 3.0);
        var states = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
        SessionState view = Capture(window);
        Invoke(window, "ActivateImageTab", bright);
        Wait(delegate { return Ready(window, bright); }, "inactive tab duplication");
        Border header = Field<StackPanel>(window, "_tabStrip").Children.OfType<Border>().Single(tab => Object.Equals(tab.Tag, dark));
        ContextMenu menu = header.ContextMenu;
        menu.PlacementTarget = header;
        menu.IsOpen = true;
        Pump();
        Render((FrameworkElement)menu, "duplicate-tab-menu.png");
        menu.IsOpen = false;
        ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Wait(delegate { return Ready(window, dark); }, "duplicated inactive image tab");
        WaitEnhance(window);
        string duplicateId = Field<string>(window, "_activeTabId");
        Assert(duplicateId != dark && states[duplicateId] != states[dark] && states[duplicateId].Path == dark, "duplicate has independent identity/state");
        var order = Field<List<string>>(window, "_tabOrder");
        Assert(order.IndexOf(duplicateId) == order.IndexOf(dark) + 1, "duplicate inserted next to source");
        Assert(states[duplicateId].RotationQuarterTurns == 1 && states[duplicateId].QuickEnhanceEnabled, "duplicate preserves rotation and enhancement");
        AssertNear(states[duplicateId].Zoom, view.Tabs.Single(t => t.Id == dark).Zoom, "duplicate preserves zoom");
        SetEnhance(window, false);
        Invoke(window, "RotateImage", 1);
        Assert(!states[dark].QuickEnhanceEnabled && states[dark].RotationQuarterTurns == 1,
            "enhancement changes globally while duplicate rotation remains independent");

        Invoke(window, "ShowBrowser");
        WaitScan(window);
        Assert(Field<ToggleButton>(window, "_enhanceButton").IsEnabled && image.Effect == null, "browser clears image effect but allows global enhance toggle");
        Field<ComboBox>(window, "_sortFieldBox").SelectedIndex = (int)BrowserSortField.Modified;
        WaitScan(window);
        Field<TextBox>(window, "_searchBox").Text = "Dark";
        Invoke(window, "ApplySearch");
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        grid.SelectItem(Field<List<ImageFileItem>>(window, "_navigationImages").Single());
        ((MenuItem)Field<Button>(window, "_browserTabButton").ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        WaitScan(window);
        string browserId = Field<string>(window, "_activeTabId");
        Assert(states[browserId].IsBrowser && states[browserId].SearchText == "Dark" && states[browserId].SelectedPath == dark
            && states[browserId].SortField == BrowserSortField.Modified, "pinned browser duplication preserves browser settings");
        Invoke(window, "DuplicateTab", browserId);
        WaitScan(window);
        Assert(states[Field<string>(window, "_activeTabId")].SearchText == "Dark", "ordinary browser tab can duplicate");
        SetEnhance(window, true);
        Invoke(window, "ActivateImageTab", dark);
        Wait(delegate { return Ready(window, dark); }, "enhanced session save");
        WaitEnhance(window);
        Invoke(window, "FitImageToView", true);
        window.Width = 980;
        window.Height = 640;
        Invoke(window, "SetMetadataPanelVisible", true);
        Pump();
        CheckCentered(window);
        AssertInside(Field<ToggleButton>(window, "_enhanceButton"), (FrameworkElement)window.Content);
        Render((FrameworkElement)window.Content, "enhance-narrow.png");
        ThemeManager.SetDarkTheme(false);
        Pump();
        Render((FrameworkElement)window.Content, "enhance-light.png");
        ThemeManager.SetDarkTheme(true);
        SessionState snapshot = Capture(window);
        services.Sessions.SaveNamed("Enhanced duplicates", snapshot);
        Assert(services.Sessions.LoadNamed("Enhanced duplicates").Tabs.Single(t => t.Id == dark).QuickEnhanceEnabled,
            "named session stores enhancement toggle");
        int tabCount = states.Count;
        window.Close();
        Pump();
        window = new MainWindow(services);
        window.Show();
        Wait(delegate { return Ready(window, dark); }, "enhanced restart");
        WaitEnhance(window);
        states = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
        Assert(states.Count == tabCount && states.Values.Count(t => t.Path == dark && !t.IsBrowser) == 2,
            "restart preserves same-file duplicate tabs");
        Assert(states[dark].QuickEnhanceEnabled && states[duplicateId].QuickEnhanceEnabled, "restart restores window-wide enhancement toggle");
        Invoke(window, "OpenBrowserImage", bright);
        Wait(delegate { return Ready(window, bright); }, "navigate enhanced tab to different file");
        WaitEnhance(window);
        Assert(states[dark].QuickEnhanceEnabled, "another image retains window-wide enhancement");
        SetEnhance(window, true);
        window.Close();
        Pump();
        Assert(File.ReadAllBytes(dark).SequenceEqual(original), "source file bytes never change");
    }

    private static void SetEnhance(MainWindow window, bool enabled)
    {
        var button = Field<ToggleButton>(window, "_enhanceButton");
        button.IsChecked = enabled;
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static void CheckSharpeningAndAlpha()
    {
        const int width = 128, height = 96;
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = (y * width + x) * 4;
            byte value = (byte)((0.46 + Math.Tanh(Math.Sin(x / 6.0) * 3) * 0.26 + Math.Cos(y / 8.0) * 0.06) * 255);
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            pixels[offset + 3] = (byte)(x < 8 ? 0 : (x < 16 ? 128 : 255));
        }
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        source.Freeze();
        var analysis = QuickEnhance.Analyze(source, 8, CancellationToken.None);
        analysis.ExposureStops = 0;
        var image = new Image { Source = source, Width = width, Height = height, Stretch = Stretch.Fill };
        image.Measure(new Size(width, height));
        image.Arrange(new Rect(0, 0, width, height));
        var original = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        original.Render(image);
        image.Effect = new QuickEnhanceEffect(analysis, source);
        var enhanced = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        enhanced.Render(image);
        var before = new byte[pixels.Length];
        var after = new byte[pixels.Length];
        original.CopyPixels(before, width * 4, 0);
        enhanced.CopyPixels(after, width * 4, 0);
        int changed = 0;
        for (int i = 0; i < after.Length; i += 4)
        {
            if (before[i] != after[i]) changed++;
            Assert(before[i + 3] == after[i + 3], "enhancement preserves transparency");
            Assert(after[i] <= after[i + 3], "enhanced color remains premultiplied");
            Assert(Math.Abs(after[i] - before[i]) <= 11, "sharpening is bounded to limit halos");
        }
        Assert(changed > 64, "adaptive sharpening visibly changes soft-edge pixels without exposure changes");
    }

    private static void WaitEnhance(MainWindow window)
    {
        Wait(delegate { return Field<CancellationTokenSource>(window, "_enhancementRequest") == null
            && Field<Image>(window, "_mainImage").Effect is QuickEnhanceEffect; }, "quick enhancement applied");
    }

    private static byte[] RenderImagePixels(MainWindow window)
    {
        var canvas = Field<Canvas>(window, "_imageCanvas");
        var bitmap = new RenderTargetBitmap((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return bytes;
    }

    private static BitmapSource MakeEnhanceFixture(string path, string mode)
    {
        const int width = 640, height = 420;
        var pixels = new byte[width * height * 4];
        var random = new Random(42);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double value = 0.46 + Math.Sin(x / 8.0) * 0.25 + Math.Cos(y / 9.0) * 0.12;
            if (mode == "dark") value *= 0.25;
            if (mode == "bright") value = 0.7 + value * 0.28;
            if (mode == "sharp") value = 0.46 + Math.Sign(Math.Sin(x / 2.0)) * 0.25 + Math.Sign(Math.Cos(y / 2.0)) * 0.12;
            if (mode == "noise") value = 0.46 + (random.NextDouble() - 0.5) * 0.5;
            int offset = (y * width + x) * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = (byte)(value * 255);
            pixels[offset + 3] = 255;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        return bitmap;
    }
}
