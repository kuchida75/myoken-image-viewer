using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunMouseGestureChecks()
    {
        RunRotationGesturePathChecks();
        Point start = new Point(200, 200);
        Assert(MainWindow.ClassifyMouseGesture(start, new Point(200, 160)) == ImageMouseGesture.FitWindow, "up at threshold fits window");
        Assert(MainWindow.ClassifyMouseGesture(start, new Point(200, 240)) == ImageMouseGesture.OriginalSize, "down shows original size");
        Assert(MainWindow.ClassifyMouseGesture(start, new Point(160, 200)) == ImageMouseGesture.FitWidth, "left fits width");
        Assert(MainWindow.ClassifyMouseGesture(start, new Point(240, 200)) == ImageMouseGesture.FitHeight, "right fits height");
        foreach (Point end in new[] { start, new Point(239, 200), new Point(270, 270), new Point(Double.NaN, 200), new Point(Double.PositiveInfinity, 0) })
            Assert(MainWindow.ClassifyMouseGesture(start, end) == ImageMouseGesture.None, "jitter, diagonal and invalid gestures are ignored");

        string root = Path.Combine(Root, "mouse-gestures-" + Guid.NewGuid().ToString("N"));
        string photos = Path.Combine(root, "Photos"), profile = Path.Combine(root, "Profile");
        Directory.CreateDirectory(photos);
        string large = Path.Combine(photos, "A.png"), small = Path.Combine(photos, "B.png");
        MakeImage(large, 2400, 1600, 96); MakeImage(small, 180, 90, 96);
        using (var services = AppServices.Create(profile))
        {
            services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1280, WindowHeight = 860 });
            var window = new MainWindow(services); window.Show(); WaitScan(window);
            try
            {
                Assert(Capture(window).MouseGesturesEnabled == true, "mouse gestures enabled by default and for older sessions");
                Assert(!(bool)Invoke(window, "BeginMouseGesture", start), "gestures cannot start in thumbnails");
                Invoke(window, "OpenImageTab", large, true); Wait(delegate { return Ready(window, large); }, "gesture image ready");
                var canvas = Field<Canvas>(window, "_imageCanvas");
                string tabId = Field<string>(window, "_activeTabId");
                foreach (bool edge in new[] { false, true })
                {
                    Invoke(window, "SetEdgeViewing", edge);
                    Wait(delegate { return Ready(window, large); }, "gesture layout ready");
                    GestureAndAssert(window, new Vector(0, -80), Math.Min(canvas.ActualWidth / 2400, canvas.ActualHeight / 1600), "up fits window");
                    GestureAndAssert(window, new Vector(0, 80), 1, "down shows 100 percent");
                    GestureAndAssert(window, new Vector(-80, 0), canvas.ActualWidth / 2400, "left fits width");
                    GestureAndAssert(window, new Vector(80, 0), canvas.ActualHeight / 1600, "right fits height");
                    RotationGestureAndAssert(window, -1);
                    RotationGestureAndAssert(window, 1);
                    CheckWheelCancelsRotationGesture(window);
                    Assert(Field<string>(window, "_activeTabId") == tabId && Ready(window, large), "gestures retain image and tab");
                    if (edge) CheckEdgeBars(window, false);
                }
                RunRightClickChecks(window, large, small);
                PumpZoomFor(1100);
                Assert(Field<Border>(window, "_mouseGestureFeedback").Visibility == Visibility.Collapsed, "completion feedback expires automatically");
                Point center = new Point(canvas.ActualWidth / 2, canvas.ActualHeight / 2);
                double zoom = Field<ScaleTransform>(window, "_imageScale").ScaleX;
                foreach (Vector delta in new[] { new Vector(0, 0), new Vector(15, 0), new Vector(90, 90) })
                {
                    Assert((bool)Invoke(window, "BeginMouseGesture", center), "begin ignored gesture");
                    Invoke(window, "UpdateMouseGestureFeedback", center + delta);
                    Assert(Field<Border>(window, "_mouseGestureFeedback").Visibility == Visibility.Collapsed, "unrecognized movement shows no misleading action");
                    Assert(!(bool)Invoke(window, "CompleteMouseGesture", center + delta), "plain clicks and ambiguous drags do not change zoom");
                    AssertZoom(window, zoom, "ignored gesture retains zoom");
                    Assert(!canvas.IsMouseCaptured, "ignored gesture releases capture");
                }
                Assert((bool)Invoke(window, "BeginMouseGesture", center), "begin outside release");
                Assert(!(bool)Invoke(window, "CompleteMouseGesture", new Point(-80, center.Y)), "release outside image frame cancels gesture");

                Assert((bool)Invoke(window, "BeginMouseGesture", center), "begin wheel-compatible gesture");
                Invoke(window, "UpdateMouseGestureFeedback", center + new Vector(80, 0));
                Assert(Field<Border>(window, "_mouseGestureFeedback").IsVisible, "pending gesture feedback before wheel");
                Assert((bool)Invoke(window, "ApplyMouseWheelZoom", center, 120, true), "right wheel still zooms during capture");
                Assert(!Field<Border>(window, "_mouseGestureFeedback").IsVisible, "wheel zoom hides pending action immediately");
                double wheelZoom = Field<ScaleTransform>(window, "_imageScale").ScaleX;
                Assert(!(bool)Invoke(window, "CompleteMouseGesture", center + new Vector(80, 0)), "wheel zoom suppresses release gesture");
                AssertZoom(window, wheelZoom, "release retains wheel zoom");

                Assert((bool)Invoke(window, "BeginMouseGesture", center), "begin capture-loss gesture");
                canvas.ReleaseMouseCapture(); Pump();
                Assert(!Field<bool>(window, "_rightGestureActive") && !(bool)Invoke(window, "CompleteMouseGesture", center + new Vector(0, 80)), "capture loss cancels pending action");
                AssertZoom(window, wheelZoom, "canceled capture retains zoom");

                Assert((bool)Invoke(window, "BeginMouseGesture", center), "begin Escape gesture");
                PressKey(window, Key.Escape); WaitScan(window); Pump();
                Assert(!canvas.IsMouseCaptured && !Field<bool>(window, "_rightGestureActive"), "Escape releases gesture capture");
                CheckEdgeBrowser(window, large, "gesture Escape");
                Assert(!(bool)Invoke(window, "CompleteMouseGesture", center + new Vector(0, -80)), "no delayed gesture after leaving image view");
                Invoke(window, "OpenBrowserImage", large); Wait(delegate { return Ready(window, large); }, "gesture image reopened");

                var setting = Field<CheckBox>(window, "_mouseGesturesCheckBox");
                center = new Point(canvas.ActualWidth / 2, canvas.ActualHeight / 2);
                Assert((bool)Invoke(window, "BeginMouseGesture", center), "begin before disabling setting");
                setting.IsChecked = false;
                Assert(!Capture(window).MouseGesturesEnabled.Value && !canvas.IsMouseCaptured, "settings disable and cancel gesture immediately");
                Assert(!(bool)Invoke(window, "BeginMouseGesture", center), "disabled gestures do not capture");
                Assert((bool)Invoke(window, "ApplyMouseWheelZoom", center, -120, true), "right wheel zoom works with gestures disabled");
                setting.IsChecked = true;
                Assert((bool)Invoke(window, "BeginMouseGesture", center), "re-enabled gestures capture");
                Invoke(window, "OpenBrowserImage", small); Wait(delegate { return Ready(window, small); }, "navigation cancels gesture");
                Assert(!canvas.IsMouseCaptured && !Field<bool>(window, "_rightGestureActive"), "new image cancels pending gesture");
                GestureAndAssert(window, new Vector(0, -80), Math.Min(canvas.ActualWidth / 180, canvas.ActualHeight / 90), "explicit Fit window enlarges small image to the first frame edge");
                CheckGestureFitFillsFrame(window);
                Assert(!Capture(window).AdaptiveZoomEnabled, "one-shot fit does not enable global Adaptive zoom");
                Invoke(window, "QueueImageView"); Wait(delegate { return Ready(window, small); }, "explicit fit survives queued layout refresh");
                CheckGestureFitFillsFrame(window);
                AssertNear(canvas.ActualHeight, ((Grid)window.Content).ActualHeight, "edge fit uses space previously occupied by status bars");
                GestureAndAssert(window, new Vector(-80, 0), canvas.ActualWidth / 180, "width explicitly enlarges small image");
                GestureAndAssert(window, new Vector(0, 80), 1, "down restores small image pixels");
                Invoke(window, "RotateImage", 1); Pump();
                GestureAndAssert(window, new Vector(-80, 0), canvas.ActualWidth / 90, "width uses rotated image dimensions");
                Invoke(window, "RotateImage", -1); Pump();

                Invoke(window, "ToggleFullscreen"); Pump();
                GestureAndAssert(window, new Vector(80, 0), canvas.ActualHeight / 90, "gestures work in fullscreen");
                RotationGestureAndAssert(window, -1); RotationGestureAndAssert(window, 1);
                Invoke(window, "ToggleFullscreen"); Pump();
                ThemeManager.SetDarkTheme(false); Pump();
                GestureAndAssert(window, new Vector(0, 80), 1, "gestures work in light theme");
                Render((FrameworkElement)window.Content, "mouse-gesture-applied-light.png");
                ThemeManager.SetDarkTheme(true);

                Field<TextBox>(window, "_slideshowInterval").Text = "0.5"; Invoke(window, "ApplySlideshowInterval");
                Invoke(window, "BeginSlideshow", new string[] { small, large }, small);
                Wait(delegate { return Ready(window, small); }, "slideshow gesture image");
                center = new Point(canvas.ActualWidth / 2, canvas.ActualHeight / 2);
                Assert((bool)Invoke(window, "BeginMouseGesture", center), "slideshow accepts gestures");
                Invoke(window, "ArmSlideshow"); PumpZoomFor(700);
                Assert(Ready(window, small) && !Field<DispatcherTimer>(window, "_slideshowTimer").IsEnabled, "slideshow waits while right button is captured");
                Assert((bool)Invoke(window, "CompleteMouseGesture", center + new Vector(0, 80)), "slideshow gesture executes");
                Wait(delegate { return Ready(window, large); }, "slideshow resumes after gesture");
                Invoke(window, "StopSlideshow");

                setting.IsChecked = false;
                SessionState state = Capture(window);
                services.Sessions.SaveNamed("Gestures", state);
                Assert(services.Sessions.LoadNamed("Gestures").MouseGesturesEnabled == false, "named session retains gesture setting");
                string backupPath = Path.Combine(root, "backup.json");
                ViewerBackupStore.Save(backupPath, (ViewerBackupDocument)Invoke(window, "CaptureBackupSnapshot"));
                Assert(ViewerBackupStore.Load(backupPath).Workspace.MouseGesturesEnabled == false, "backup retains gesture setting");
                state.MouseGesturesEnabled = null;
                Invoke(window, "ApplySessionState", state, false); Wait(delegate { return Ready(window, large); }, "legacy settings restored");
                Assert(Capture(window).MouseGesturesEnabled == true && setting.IsChecked == true, "missing preference uses enabled default and synchronizes checkbox");
                setting.IsChecked = false;
            }
            finally { window.Close(); Pump(); ThemeManager.SetDarkTheme(true); }
        }
        using (var services = AppServices.Create(profile))
        {
            var window = new MainWindow(services); window.Show();
            try { Wait(delegate { return Ready(window, large); }, "gesture settings restart"); Assert(Capture(window).MouseGesturesEnabled == false, "disabled gesture setting survives restart"); }
            finally { window.Close(); Pump(); }
        }
        Console.WriteLine("PASS: fitting and Up-Left/Up-Right rotation gestures, sampled paths/jitter/diagonal rejection, centered previews and Applied confirmation, unchanged originals, wheel cancellation, full-frame fit, edge/fullscreen, slideshow and persisted settings.");
    }

    private static void GestureAndAssert(MainWindow window, Vector delta, double expectedZoom, string message)
    {
        var canvas = Field<Canvas>(window, "_imageCanvas");
        Point center = new Point(canvas.ActualWidth / 2, canvas.ActualHeight / 2);
        double before = Field<ScaleTransform>(window, "_imageScale").ScaleX;
        Size frame = canvas.RenderSize;
        Assert((bool)Invoke(window, "BeginMouseGesture", center), message + " begins");
        Invoke(window, "UpdateMouseGestureFeedback", center + delta); Pump();
        var feedback = Field<Border>(window, "_mouseGestureFeedback");
        string arrow = delta.Y < 0 ? "\u2191" : delta.Y > 0 ? "\u2193" : delta.X < 0 ? "\u2190" : "\u2192";
        string action = delta.Y < 0 ? "Fit window" : delta.Y > 0 ? "Original size (100%)" : delta.X < 0 ? "Fit width" : "Fit height";
        Assert(feedback.IsVisible && !feedback.IsHitTestVisible && !feedback.Focusable, "gesture preview is visible and passes through input");
        Assert(Field<TextBlock>(window, "_mouseGestureArrow").Text == arrow && Field<TextBlock>(window, "_mouseGestureAction").Text == action, "gesture preview matches direction and action");
        Assert(Field<TextBlock>(window, "_mouseGestureHint").Text == "Release to apply", "preview explains release action");
        Point location = feedback.TranslatePoint(new Point(), Field<Grid>(window, "_imageView"));
        AssertNear(location.X + feedback.ActualWidth / 2, canvas.ActualWidth / 2, "gesture cue centered horizontally");
        AssertNear(location.Y + feedback.ActualHeight / 2, canvas.ActualHeight / 2, "gesture cue centered vertically");
        Assert(canvas.RenderSize == frame, "gesture preview does not reserve image space");
        if (delta.X < 0 && Field<bool>(window, "_edgeActive")) Render((FrameworkElement)window.Content, "mouse-gesture-preview-dark.png");
        AssertZoom(window, before, message + " waits for release");
        Assert((bool)Invoke(window, "CompleteMouseGesture", center + delta), message + " executes");
        Pump(); AssertZoom(window, expectedZoom, message); CheckZoomCentered(window);
        Assert(feedback.IsVisible && Field<TextBlock>(window, "_mouseGestureHint").Text == "Applied"
            && Field<DispatcherTimer>(window, "_mouseGestureFeedbackTimer").IsEnabled, "completed gesture is confirmed even if zoom is unchanged");
        Assert(!canvas.IsMouseCaptured && !Field<bool>(window, "_rightGestureActive"), message + " releases capture");
    }

    private static void CheckGestureFitFillsFrame(MainWindow window)
    {
        var canvas = Field<Canvas>(window, "_imageCanvas");
        Size image = (Size)Invoke(window, "DisplayedImageSize");
        double zoom = Field<ScaleTransform>(window, "_imageScale").ScaleX;
        double width = image.Width * zoom, height = image.Height * zoom;
        Assert(width <= canvas.ActualWidth + .01 && height <= canvas.ActualHeight + .01, "Fit window preserves whole image without cropping");
        Assert(Math.Abs(width - canvas.ActualWidth) < .01 || Math.Abs(height - canvas.ActualHeight) < .01, "Fit window reaches one current canvas edge without obsolete bar margins");
        CheckZoomCentered(window);
    }
}
