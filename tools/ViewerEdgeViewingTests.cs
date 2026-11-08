using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunEdgeViewingChecks()
    {
        string root = Path.Combine(Root, "edge-view-" + Guid.NewGuid().ToString("N"));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string photo = Path.Combine(photos, "A.png"); MakeImage(photo, 2400, 1600, 96);
        MakeImage(Path.Combine(photos, "B.png"), 800, 1600, 96);
        using (var services = AppServices.Create(Path.Combine(root, "Profile")))
        {
            services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 920 });
            var window = new MainWindow(services); window.Show(); WaitScan(window);
            try
            {
                Assert(!Capture(window).EdgeToEdgeViewing, "edge viewing is opt-in for compatibility");
                Invoke(window, "OpenImageTab", photo, true); Wait(delegate { return Ready(window, photo); }, "normal image");
                var canvas = Field<Canvas>(window, "_imageCanvas");
                var frame = (Grid)window.Content;
                Size normal = canvas.RenderSize;
                Invoke(window, "SetEdgeViewing", true);
                Wait(delegate { return Ready(window, photo) && canvas.ActualWidth > normal.Width && canvas.ActualHeight > normal.Height; }, "edge image gains space");
                AssertNear(canvas.ActualWidth, frame.ActualWidth, "edge canvas fills whole content width");
                AssertNear(canvas.ActualHeight, frame.ActualHeight, "edge canvas fills whole content height");
                Assert(WindowChrome.GetWindowChrome(window) != null && WindowChrome.GetWindowChrome(window).CaptionHeight == 0,
                    "edge image has no title bar or invisible caption hit region");
                Assert((double)typeof(MainWindow).GetProperty("EdgeTopInset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(window, null) == 0,
                    "hidden caption reserves no space above image overlays");
                Assert(canvas.ActualHeight >= window.ActualHeight - 20, "edge canvas recovers title-bar height");
                SystemCommands.MaximizeWindow(window);
                Wait(delegate { return window.WindowState == WindowState.Maximized && Ready(window, photo); }, "borderless edge window maximizes");
                var work = WindowPlacement.CurrentWorkArea(window);
                Point frameScreen = frame.PointToScreen(new Point(frame.ActualWidth / 2, 0));
                Point workScreen = PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice.Transform(new Point(work.Left, work.Top));
                Assert(frameScreen.Y >= workScreen.Y - 1, "maximized image stays inside monitor work area");
                SystemCommands.RestoreWindow(window);
                Wait(delegate { return window.WindowState == WindowState.Normal && Ready(window, photo); }, "borderless edge window restores");
                Assert(Field<Grid>(window, "_edgeBody").Visibility == Visibility.Hidden, "side panels cannot intercept edge input");
                CheckCentered(window); CheckVisible(window);
                Size edge = canvas.RenderSize;
                CheckEdgeBars(window, false);
                canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.PreviewMouseMoveEvent });
                PressKey(window, Key.Right);
                string portrait = Path.Combine(photos, "B.png");
                Wait(delegate { return Ready(window, portrait); }, "hidden-controls keyboard navigation");
                CheckEdgeBars(window, false);
                canvas.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = UIElement.MouseWheelEvent });
                Wait(delegate { return Ready(window, photo); }, "hidden-controls wheel navigation");
                double oldZoom = Field<ScaleTransform>(window, "_imageScale").ScaleX;
                Assert((bool)Invoke(window, "ApplyMouseWheelZoom", new Point(100, 100), 120, true), "edge right-wheel zoom works");
                Assert(Field<ScaleTransform>(window, "_imageScale").ScaleX > oldZoom, "zoom changes while controls stay hidden");
                Invoke(window, "FitImageToView", true); PumpZoomFor(2300);
                CheckEdgeBars(window, false);
                Render((FrameworkElement)window.Content, "edge-view-hidden.png");
                Assert(canvas.RenderSize == edge, "hiding bars does not resize image");
                PressKey(window, Key.Escape); WaitScan(window); Pump();
                CheckEdgeBrowser(window, photo, "one Escape");
                Invoke(window, "OpenBrowserImage", photo); Wait(delegate { return Ready(window, photo); }, "edge reopened after Escape");
                CheckEdgeBars(window, false);
                EdgeDoubleClick(canvas); WaitScan(window); Pump();
                CheckEdgeBrowser(window, photo, "one double-click");
                Invoke(window, "OpenBrowserImage", photo); Wait(delegate { return Ready(window, photo); }, "edge reopened for Enter");
                PressKey(window, Key.Enter); WaitScan(window); Pump();
                CheckEdgeBrowser(window, photo, "one Enter");
                Invoke(window, "OpenBrowserImage", photo); Wait(delegate { return Ready(window, photo); }, "edge reopened for fullscreen exit");
                Invoke(window, "ToggleFullscreen"); Pump();
                PressKey(window, Key.Escape); WaitScan(window); Pump();
                Assert(!Field<bool>(window, "_isFullscreen"), "one Escape also leaves fullscreen");
                CheckEdgeBrowser(window, photo, "fullscreen Escape");
                Invoke(window, "OpenBrowserImage", photo); Wait(delegate { return Ready(window, photo); }, "edge reopened for slideshow");
                Invoke(window, "BeginSlideshow", new string[] { photo, portrait }, photo); Pump();
                Assert(Field<bool>(window, "_slideshowPlaying"), "slideshow starts in edge mode");
                PressKey(window, Key.Escape); WaitScan(window); Pump();
                Assert(!Field<bool>(window, "_slideshowPlaying"), "Escape stops slideshow");
                CheckEdgeBrowser(window, photo, "slideshow Escape");
                Invoke(window, "OpenBrowserImage", photo); Wait(delegate { return Ready(window, photo); }, "edge reopened for layout checks");
                Invoke(window, "SetImageMetadataOverlay", true); Invoke(window, "SetManualEnhanceVisible", true); Pump();
                AssertInside(Field<Border>(window, "_imageMetadataOverlay"), Field<Grid>(window, "_imageView"));
                foreach (bool dark in new[] { false, true })
                {
                    ThemeManager.SetDarkTheme(dark); window.Width = 980; Pump();
                    Wait(delegate { return Ready(window, photo); }, "edge resize settles"); CheckCentered(window);
                    Render((FrameworkElement)window.Content, dark ? "edge-view-dark.png" : "edge-view-light.png");
                }
                Invoke(window, "SetCompactMode", true); Pump(); AssertNear(canvas.ActualWidth, frame.ActualWidth, "compact edge remains full width");
                Invoke(window, "SetCompactMode", false); Pump();
                Invoke(window, "ToggleFullscreen"); Pump();
                Wait(delegate { return Ready(window, photo); }, "edge fullscreen layout");
                Assert(WindowChrome.GetWindowChrome(window) == null, "fullscreen releases edge chrome");
                AssertNear(canvas.ActualHeight, frame.ActualHeight, "fullscreen edge uses full content height");
                Invoke(window, "ToggleFullscreen"); Pump();
                Assert(WindowChrome.GetWindowChrome(window) != null && WindowChrome.GetWindowChrome(window).CaptionHeight == 0, "leaving fullscreen keeps title bar hidden");
                var saved = Capture(window); Assert(saved.EdgeToEdgeViewing, "edge preference captured");
                services.Sessions.SaveNamed("Edge", saved);
                Assert(services.Sessions.LoadNamed("Edge").EdgeToEdgeViewing, "named session retains edge setting");
                var backup = (ViewerBackupDocument)Invoke(window, "CaptureBackupSnapshot");
                string file = Path.Combine(root, "backup.json"); ViewerBackupStore.Save(file, backup);
                Assert(ViewerBackupStore.Load(file).Workspace.EdgeToEdgeViewing, "backup retains edge setting");
                Invoke(window, "BrowseActiveTab"); WaitScan(window); Pump();
                Assert(!Field<bool>(window, "_edgeActive") && Field<Grid>(window, "_edgeBody").IsVisible, "browser restores original panels");
                Assert(WindowChrome.GetWindowChrome(window) == null, "browser restores normal Windows title bar");
                Assert(Capture(window).EdgeToEdgeViewing, "browsing does not clear preference");
                Invoke(window, "OpenBrowserImage", photo); Wait(delegate { return Ready(window, photo); }, "edge image reopened");
                Assert(Field<bool>(window, "_edgeActive"), "returning to an image restores edge mode");
                CheckEdgeBars(window, false);
                Invoke(window, "SetEdgeViewing", false); Wait(delegate { return Ready(window, photo); }, "normal view restored");
                Assert(Field<Grid>(window, "_imageView").Parent == Field<Grid>(window, "_edgeWorkspace"), "normal image reattached to workspace");
                Assert(!Capture(window).EdgeToEdgeViewing, "edge setting can be disabled");
                Assert(WindowChrome.GetWindowChrome(window) == null, "disabling edge restores normal title bar");
                Invoke(window, "ApplySessionState", saved, false); Wait(delegate { return Ready(window, photo); }, "edge saved session applied");
                Assert(Field<bool>(window, "_edgeActive"), "session restores edge layout");
                CheckEdgeBars(window, false);
                saved.EdgeToEdgeViewing = false; Invoke(window, "ApplySessionState", saved, false);
                Wait(delegate { return Ready(window, photo); }, "normal saved session applied");
                Assert(!Field<bool>(window, "_edgeActive"), "session can restore normal layout");
                Field<CheckBox>(window, "_edgeCheck").IsChecked = true;
                Wait(delegate { return Ready(window, photo) && Field<bool>(window, "_edgeActive"); }, "settings checkbox immediately enables edge mode");
                Field<CheckBox>(window, "_edgeCheck").IsChecked = false;
                Wait(delegate { return Ready(window, photo) && !Field<bool>(window, "_edgeActive"); }, "settings checkbox immediately disables edge mode");
            }
            finally { window.Close(); Pump(); ThemeManager.SetDarkTheme(true); }
        }
        Console.WriteLine("PASS: edge-to-edge frame, centered fit, controls hidden through mouse/navigation/zoom/idle, direct thumbnail exit with Escape/double-click/Enter, fullscreen and slideshow cancellation, restored controls/focus/selection, themes, resize and session/backup persistence.");
    }

    private static void CheckEdgeBrowser(MainWindow window, string photo, string action)
    {
        Assert(!Field<bool>(window, "_edgeActive") && Field<Grid>(window, "_browserView").IsVisible, action + " goes directly to thumbnails");
        Assert(Field<Grid>(window, "_edgeBody").IsVisible && !Field<Canvas>(window, "_imageCanvas").IsMouseCaptured, action + " restores panels and releases pan");
        CheckEdgeBars(window, true);
        foreach (FrameworkElement bar in (FrameworkElement[])Invoke(window, "EdgeBars"))
            Assert(bar.IsVisible && bar.Opacity == 1, action + " restores visible, opaque menus, tabs and status bars");
        var thumbnails = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        Assert(thumbnails.SelectedItem != null && thumbnails.SelectedItem.Path == photo && thumbnails.IsKeyboardFocusWithin, action + " restores selection and thumbnail keyboard focus");
        Assert(Capture(window).EdgeToEdgeViewing, action + " preserves edge preference");
    }

    private static void CheckEdgeBars(MainWindow window, bool shown)
    {
        Assert(Field<bool>(window, "_edgeBarsShown") == shown, "edge controls visibility state");
        foreach (FrameworkElement bar in (FrameworkElement[])Invoke(window, "EdgeBars"))
        {
            Assert(bar.IsHitTestVisible == shown && bar.IsEnabled == shown, "hidden controls cannot intercept pointer or keyboard focus");
            Assert(shown ? bar.Opacity > .8 : bar.Opacity < .01, "controls have expected opacity");
        }
    }

    private static void EdgeDoubleClick(Canvas canvas)
    {
        var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
        typeof(MouseButtonEventArgs).GetProperty("ClickCount").SetValue(e, 2, null);
        canvas.RaiseEvent(e);
        Assert(e.Handled, "edge double-click is handled");
    }
}
