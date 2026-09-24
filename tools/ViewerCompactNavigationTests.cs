using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunCompactAndBoundaryChecks()
    {
        string root = Path.Combine(Root, "cn-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        string photos = Path.Combine(root, "Photos");
        Directory.CreateDirectory(Path.Combine(photos, "Folder"));
        string first = Path.Combine(photos, "01.png"), middle = Path.Combine(photos, "02.png"), last = Path.Combine(photos, "03.png");
        foreach (string path in new[] { first, middle, last }) MakeImage(path, 1600, 1000, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 780 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        Assert(!Field<bool>(window, "_isCompactMode"), "new window starts in normal mode");
        Invoke(window, "OpenBrowserImage", middle); Wait(delegate { return Ready(window, middle); }, "boundary starting image");
        WaitScan(window); window.Focus();
        string id = Field<string>(window, "_activeTabId");
        Invoke(window, "ToggleTabPin", id);
        Wait(delegate { return Field<HashSet<string>>(window, "_preloading").Count == 0; }, "initial neighbor preload completes");
        services.VramCache.Remove(first);
        PressKey(window, Key.End); Wait(delegate { return Ready(window, last); }, "End opens final image");
        BitmapSource cached;
        Wait(delegate { return services.VramCache.TryGet(first, out cached); }, "first image preloaded from final image");
        Wheel(window, -120); Wait(delegate { return Ready(window, first); }, "wheel past final wraps to first");
        Wheel(window, 120); Wait(delegate { return Ready(window, last); }, "wheel before first wraps to final");
        PressKey(window, Key.Home); Wait(delegate { return Ready(window, first); }, "Home opens first image");
        PressKey(window, Key.Left); Wait(delegate { return Ready(window, last); }, "previous key wraps");
        PressKey(window, Key.Right); Wait(delegate { return Ready(window, first); }, "next key wraps");
        Assert(Field<string>(window, "_activeTabId") == id && Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count == 1
            && Field<Dictionary<string, ImageTabState>>(window, "_tabs")[id].IsPinned, "boundary navigation retains pinned tab identity");

        Field<ToggleButton>(window, "_sortDirectionButton").IsChecked = true;
        Invoke(window, "SortSelectionChanged"); WaitScan(window);
        PressKey(window, Key.Home); Wait(delegate { return Ready(window, last); }, "Home follows descending sort");
        PressKey(window, Key.End); Wait(delegate { return Ready(window, first); }, "End follows descending sort");
        Wheel(window, -120); Wait(delegate { return Ready(window, last); }, "descending wrap follows sort");
        Field<ToggleButton>(window, "_sortDirectionButton").IsChecked = false;
        Invoke(window, "SortSelectionChanged"); WaitScan(window);

        var search = Field<TextBox>(window, "_searchBox");
        search.Text = "02"; Invoke(window, "ApplySearch"); window.Focus();
        PressKey(window, Key.Home); Wait(delegate { return Ready(window, middle); }, "boundary respects filename filter");
        int loadVersion = Field<int>(window, "_imageLoadVersion");
        PressKey(window, Key.End); Wheel(window, -120); Wheel(window, 120);
        Assert(Field<int>(window, "_imageLoadVersion") == loadVersion, "single-image sequence does not repeatedly decode");
        search.Text = "no matching images"; Invoke(window, "ApplySearch");
        PressKey(window, Key.Home); PressKey(window, Key.End); Wheel(window, -120);
        Assert(Ready(window, middle), "empty result navigation leaves image unchanged");
        search.Text = ""; Invoke(window, "ApplySearch");
        search.Focus();
        PressKey(window, Key.Home); PressKey(window, Key.End); PressKey(window, Key.U);
        Assert(Ready(window, middle) && !Field<bool>(window, "_isCompactMode"), "text fields retain Home/End/U editing");
        var imageClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
        Field<Canvas>(window, "_imageCanvas").RaiseEvent(imageClick);
        Assert(Object.ReferenceEquals(Keyboard.FocusedElement, Field<Canvas>(window, "_imageCanvas")), "clicking image takes keyboard focus from search");
        var pending = typeof(MainWindow).GetField("_folderScanPending", BindingFlags.NonPublic | BindingFlags.Instance);
        pending.SetValue(window, true); PressKey(window, Key.End);
        Assert(Ready(window, middle), "boundary waits for folder ordering to finish");
        pending.SetValue(window, false);

        var canvas = Field<Canvas>(window, "_imageCanvas");
        var column = Field<ColumnDefinition>(window, "_folderColumn");
        column.Width = new GridLength(305); Pump();
        Invoke(window, "FitImageToView", true); Pump(); CheckCentered(window);
        double normalWidth = canvas.ActualWidth, normalHeight = canvas.ActualHeight;
        SessionState placement = Capture(window);
        Render((FrameworkElement)window.Content, "normal-image-layout.png");
        PressKey(window, Key.U); Pump();
        Assert(Field<bool>(window, "_isCompactMode") && Field<UIElement>(window, "_topToolbar").Visibility == Visibility.Collapsed
            && Field<FolderNavigationPane>(window, "_folderNavigation").Visibility == Visibility.Collapsed
            && Field<GridSplitter>(window, "_leftSplitter").Visibility == Visibility.Collapsed, "U hides toolbar, folder tree and splitter");
        AssertNear(column.ActualWidth, 0, "compact removes unused folder column");
        Assert(canvas.ActualWidth > normalWidth + 300 && canvas.ActualHeight > normalHeight + 60, "compact reclaims horizontal and vertical space");
        Assert(Field<StackPanel>(window, "_tabStrip").IsVisible, "tabs remain available in compact mode");
        CheckCentered(window); CheckRenderedPixels(window);
        Render((FrameworkElement)window.Content, "compact-image-dark.png");
        var repeat = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.U)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        typeof(KeyEventArgs).GetMethod("SetRepeat", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(repeat, new object[] { true });
        window.RaiseEvent(repeat);
        Assert(repeat.Handled && Field<bool>(window, "_isCompactMode"), "held U does not flicker modes");
        AssertNear(Capture(window).WindowWidth, placement.WindowWidth, "compact retains window width");
        AssertNear(Capture(window).WindowHeight, placement.WindowHeight, "compact retains window height");
        PressKey(window, Key.U); Pump();
        AssertNear(column.ActualWidth, 305, "normal mode restores resized folder width");
        AssertNear(canvas.ActualWidth, normalWidth, "normal image frame width restored");
        CheckCentered(window);

        Invoke(window, "ZoomFromCenter", 3.0); Pump();
        double zoom = Field<Dictionary<string, ImageTabState>>(window, "_tabs")[id].Zoom;
        Assert((bool)Invoke(window, "BeginImagePan", new Point(100, 100)), "start pan before mode change");
        PressKey(window, Key.U); Pump();
        Assert(!Field<bool>(window, "_isPanning") && !canvas.IsMouseCaptured, "mode change releases image drag");
        CheckVisible(window);
        AssertNear(Field<Dictionary<string, ImageTabState>>(window, "_tabs")[id].Zoom, zoom, "compact retains manual zoom");
        PressKey(window, Key.U); Pump(); CheckVisible(window);
        Invoke(window, "FitImageToView", true); Pump();
        PressKey(window, Key.U); Pump();
        Invoke(window, "ToggleFullscreen"); Pump(); CheckCentered(window);
        Assert(Field<bool>(window, "_isCompactMode"), "fullscreen keeps compact mode");
        Invoke(window, "ToggleFullscreen"); Pump(); CheckCentered(window);
        PressKey(window, Key.Escape); WaitScan(window);
        Assert(Field<Grid>(window, "_browserView").IsVisible && Field<bool>(window, "_isCompactMode"), "Esc retains usable compact browser");
        PressKey(window, Key.U); Pump();
        Assert(!Field<bool>(window, "_isCompactMode"), "U restores normal from browser mode");

        Invoke(window, "OpenBrowserImage", middle); Wait(delegate { return Ready(window, middle); }, "compact restart image");
        PressKey(window, Key.U); Pump();
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = false;
        window.Width = 980; window.Height = 640; Pump(); CheckCentered(window);
        Render((FrameworkElement)window.Content, "compact-image-light-narrow.png");
        services.Sessions.SaveNamed("Compact save", Capture(window));
        window.Close(); Pump();
        window = new MainWindow(services); window.Show(); Wait(delegate { return Ready(window, middle); }, "restart from compact"); WaitScan(window);
        Assert(!Field<bool>(window, "_isCompactMode") && Field<UIElement>(window, "_topToolbar").IsVisible
            && Field<FolderNavigationPane>(window, "_folderNavigation").IsVisible, "restart always normal even when saved compact");
        Invoke(window, "ApplySessionState", services.Sessions.LoadNamed("Compact save"), false);
        Wait(delegate { return Ready(window, middle); }, "named session saved from compact"); WaitScan(window);
        Assert(!Field<bool>(window, "_isCompactMode"), "named sessions do not persist compact preference");

        var nav = Field<List<ImageFileItem>>(window, "_navigationImages");
        string missing = Path.Combine(photos, "Missing.png");
        nav.Clear();
        nav.Add(new ImageFileItem { Path = first }); nav.Add(new ImageFileItem { Path = missing }); nav.Add(new ImageFileItem { Path = middle });
        Invoke(window, "OpenBoundaryImage", true);
        Assert(Ready(window, middle), "End keeps current endpoint without reload");
        Invoke(window, "OpenRelativeImage", -1); Wait(delegate { return Ready(window, first); }, "navigation skips stale missing file");
        // The transition refreshes navigation; inject an all-missing snapshot to check termination.
        nav.Clear(); nav.Add(new ImageFileItem { Path = missing });
        Invoke(window, "OpenBoundaryImage", false); Invoke(window, "OpenRelativeImage", 1);
        Assert(Ready(window, first), "all-missing snapshot safely leaves current image");
        window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true);
        Console.WriteLine("PASS: Home/End and wraparound, sort/filter/empty/stale cases, compact rendering, pan, fullscreen and normal startup.");
    }

    private static void Wheel(MainWindow window, int delta)
    {
        var e = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = UIElement.MouseWheelEvent };
        Field<Canvas>(window, "_imageCanvas").RaiseEvent(e); Pump();
        Assert(e.Handled, "image wheel handled");
    }
}
