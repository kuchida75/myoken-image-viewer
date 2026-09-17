using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void FocusViewerSetting(MainWindow window, UIElement control)
    {
        var pages = Field<List<StackPanel>>(window, "_configurationPages");
        for (int i = 0; i < pages.Count; i++)
        {
            DependencyObject parent = control;
            while (parent != null && parent != pages[i]) parent = LogicalTreeHelper.GetParent(parent);
            if (parent == null) continue;
            Invoke(window, "ShowConfigure"); Invoke(window, "SelectConfigurationPage", i); Pump();
            control.Focus(); return;
        }
        CloseViewerSettings(window); window.Activate(); control.Focus();
    }

    private static void CloseViewerSettings(MainWindow window)
    {
        var settings = Field<Window>(window, "_configureWindow");
        if (settings != null) { settings.Close(); Pump(); }
    }

    private static void RunConfigurationChecks()
    {
        RunSystemVramChecks();
        string root = Path.Combine(Root, "config-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string shortImage = Path.Combine(photos, "a.png");
        string longImage = Path.Combine(photos, "A much longer image filename for measuring consistent tabs.png");
        MakeImage(shortImage, 640, 420, 96); MakeImage(longImage, 640, 420, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1300, WindowHeight = 800 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        Console.WriteLine("Configure check: workspace initialized");
        Invoke(window, "OpenImageTab", shortImage, true); Wait(delegate { return Ready(window, shortImage); }, "short tab");
        double shortWidth = TabHeader(window, shortImage).Width;
        Invoke(window, "OpenImageTab", longImage, true); Wait(delegate { return Ready(window, longImage); }, "long tab");
        var headers = Field<StackPanel>(window, "_tabStrip").Children.OfType<Border>().ToArray();
        Assert(headers.Length == 2 && TabHeader(window, shortImage).Width == shortWidth && TabHeader(window, longImage).Width == 280,
            "inactive tab stays narrow while active title gets its normal width");
        Invoke(window, "ToggleTabPin", shortImage);
        Console.WriteLine("Configure check: tab widths verified");
        Invoke(window, "SetPinnedTabColor", "#DAEEA2");
        Pump();
        var pinned = TabHeader(window, shortImage);
        Assert(((SolidColorBrush)pinned.Background).Color.ToString() == "#FFDAEEA2", "chosen color applies to pinned header");
        Assert(((DockPanel)pinned.Child).Children.OfType<TextBlock>().All(text => ((SolidColorBrush)text.Foreground).Color == Colors.Black),
            "pinned label and icon keep readable contrast");
        Invoke(window, "ToggleTabPin", longImage);
        Pump();
        Assert(((SolidColorBrush)TabHeader(window, longImage).Background).Color == ((SolidColorBrush)TabHeader(window, shortImage).Background).Color,
            "all pinned tabs share chosen window color");
        Invoke(window, "ToggleTabPin", longImage);
        Pump();
        Assert(((SolidColorBrush)TabHeader(window, longImage).Background).Color != ((SolidColorBrush)TabHeader(window, shortImage).Background).Color,
            "unpin restores ordinary tab palette");
        Invoke(window, "CloseTab", longImage); Pump();
        Assert(TabHeader(window, shortImage).Width == shortWidth, "closing a wider title leaves narrowest-based width unchanged");
        Invoke(window, "ShowBrowser"); WaitScan(window);
        Console.WriteLine("Configure check: pinned colors verified");
        var arrow = Field<Button>(window, "_minimapToggle");
        Assert(arrow.IsVisible && AutomationProperties.GetName(arrow).Contains("Show"), "collapsed minimap retains show arrow");
        arrow.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(Capture(window).ThumbnailMinimapVisible && AutomationProperties.GetName(arrow).Contains("Hide"), "minimap arrow expands view");
        arrow.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(!Capture(window).ThumbnailMinimapVisible && arrow.IsVisible, "minimap arrow collapses without vanishing");

        Field<Button>(window, "_configureButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Console.WriteLine("Configure check: settings opened");
        var configure = Field<Window>(window, "_configureWindow");
        Assert(configure != null && configure.Owner == window, "Configure opens owned non-modal settings");
        Invoke(window, "ShowConfigure"); Assert(Object.ReferenceEquals(configure, Field<Window>(window, "_configureWindow")), "one Configure window per viewer");
        Render((FrameworkElement)configure.Content, "configure-appearance-dark.png");
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = false; Pump();
        Render((FrameworkElement)configure.Content, "configure-appearance-light.png");
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = true;
        FocusViewerSetting(window, Field<Slider>(window, "_thumbnailSizeSlider"));
        Field<Slider>(window, "_thumbnailSizeSlider").Value = 192;
        Field<Slider>(window, "_thumbnailAspectSlider").Value = 1.5;
        Wait(delegate { return Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid").ThumbnailSize == 192; }, "settings update thumbnail cache size");
        AssertInside(Field<Slider>(window, "_thumbnailSizeSlider"), (FrameworkElement)configure.Content);
        Assert(!Field<Slider>(window, "_thumbnailSizeSlider").IsDescendantOf(window), "thumbnail settings removed from workspace");
        Field<TextBox>(window, "_slideshowInterval").Text = "4.5";
        Render((FrameworkElement)configure.Content, "configure-images-dark.png");
        FocusViewerSetting(window, Field<Slider>(window, "_vramSlider"));
        Field<Slider>(window, "_vramSlider").Value = 1536;
        Wait(delegate { return SystemGpuMemoryMonitor.Shared.Snapshot.SampledUtc != default(DateTime); }, "GPU read completes off UI thread");
        Invoke(window, "UpdateStatus");
        Assert(Field<TextBlock>(window, "_systemVramText").Text == SystemGpuMemoryMonitor.Shared.Snapshot.UsageText, "system usage separate from cache budget");
        Render((FrameworkElement)configure.Content, "configure-performance-dark.png");
        configure.Width = 570; configure.Height = 360; Pump();
        AssertInside(Field<Slider>(window, "_vramSlider"), (FrameworkElement)configure.Content);
        Render((FrameworkElement)configure.Content, "configure-performance-small.png");
        CloseViewerSettings(window);
        Assert(Field<double>(window, "_slideshowSeconds") == 4.5, "close commits pending interval");
        Assert(Capture(window).PinnedTabColor == "#DAEEA2" && Capture(window).VramBudgetMb == 1536, "settings captured");
        services.Sessions.SaveNamed("Configured", Capture(window));
        Assert(services.Sessions.LoadNamed("Configured").PinnedTabColor == "#DAEEA2", "named session stores pin color");
        string backup = Path.Combine(root, "backup.json");
        WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
        Assert(ViewerBackupStore.Load(backup).Workspace.PinnedTabColor == "#DAEEA2", "backup stores pin color");
        Invoke(window, "SetPinnedTabColor", "#123456");
        WaitBackup((Task)Invoke(window, "ImportBackupFileAsync", backup, false)); WaitScan(window);
        Assert(Capture(window).PinnedTabColor == "#DAEEA2", "import restores pin color");
        var document = ViewerBackupStore.Load(backup); document.Workspace.PinnedTabColor = "bad";
        bool rejected = false;
        try { ViewerBackupStore.Save(Path.Combine(root, "bad.json"), document); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "invalid backup color rejected");
        window.Width = 980; window.Height = 640; Pump();
        Render((FrameworkElement)window.Content, "configured-browser-narrow.png");
        FocusViewerSetting(window, Field<TextBox>(window, "_slideshowInterval"));
        Field<TextBox>(window, "_slideshowInterval").Text = "5.5";
        window.Close(); Pump();
        Assert(Field<Window>(window, "_configureWindow") == null, "owner close cleans up settings window");
        window = new MainWindow(services); window.Show(); WaitScan(window);
        Assert(Capture(window).PinnedTabColor == "#DAEEA2" && Capture(window).SlideshowSeconds == 5.5
            && Capture(window).ThumbnailSizePixels == 192, "automatic session restores settings including last pending edit");
        Invoke(window, "SetPinnedTabColor", "invalid");
        Assert(Capture(window).PinnedTabColor == "#2F78A6", "legacy/invalid ordinary session color has safe default");
        window.Close(); Pump(); services.Dispose();
        Console.WriteLine("PASS: Configure pages, live settings and persistence, readable chosen pin colors, adaptive uniform tabs, minimap arrow and system VRAM.");
    }

    private static void RunSystemVramChecks()
    {
        string a = "luid_0x00000000_0x00000001", b = "luid_0x00000000_0x00000002";
        var adapters = new[] { new GpuAdapterMemoryInfo { Id = a, Name = "GPU A", CapacityBytes = 8L << 30 },
            new GpuAdapterMemoryInfo { Id = b, Name = "GPU B", CapacityBytes = 32L << 30 } };
        var usage = new Dictionary<string, long> { { a + "_phys_0", 1L << 30 }, { b + "_phys_0", 2L << 30 }, { b + "_phys_1", 3L << 30 } };
        var sample = SystemGpuMemoryMonitor.Aggregate(adapters, usage);
        Assert(sample.UsedBytes == 6L << 30 && sample.CapacityBytes == 40L << 30, "GPU usage aggregated by physical instance; capacity counted once per adapter");
        Assert(!SystemGpuMemoryMonitor.Aggregate(adapters, new Dictionary<string, long>()).UsedBytes.HasValue, "missing telemetry is unavailable, not zero");
        Assert(!SystemGpuMemoryMonitor.Aggregate(new GpuAdapterMemoryInfo[0], usage).CapacityBytes.HasValue, "unmatched counters do not invent capacity");
        using (var release = new ManualResetEventSlim(false))
        {
            int calls = 0;
            var monitor = new SystemGpuMemoryMonitor(delegate { Interlocked.Increment(ref calls); release.Wait(); return sample; });
            var watch = Stopwatch.StartNew(); monitor.Refresh(); monitor.Refresh();
            Assert(watch.ElapsedMilliseconds < 200, "GPU telemetry does not block UI refresh");
            release.Set(); Wait(delegate { return monitor.Snapshot.UsedBytes.HasValue; }, "GPU snapshot");
            monitor.Refresh(); Assert(calls == 1, "only one throttled telemetry read in flight");
        }
        var failure = new SystemGpuMemoryMonitor(delegate { throw new UnauthorizedAccessException("counter unavailable"); });
        failure.Refresh(); Wait(delegate { return failure.Snapshot.Error != null; }, "telemetry failure");
        Assert(!failure.Snapshot.UsedBytes.HasValue && failure.Snapshot.Summary.Contains("unavailable"), "counter access failure stays honest");
        var live = Task.Run(delegate { return SystemGpuMemoryMonitor.ReadSystem(); });
        try
        {
            WaitAdvancedTask(live);
            Console.WriteLine("Live GPU memory: " + live.Result.Summary + "\n" + live.Result.Details);
        }
        catch (Exception ex)
        {
            if (!live.IsFaulted) throw;
            Console.WriteLine("Live GPU telemetry unavailable on this host: " + ex.Message); return;
        }
        if (live.Result.UsedBytes.HasValue) Assert(live.Result.UsedBytes >= 0, "live system GPU sample nonnegative");
    }
}
