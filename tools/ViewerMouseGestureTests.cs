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
                    GestureAndAssert(window, new Vector(0, -80), Math.Min(1, Math.Min(canvas.ActualWidth / 2400, canvas.ActualHeight / 1600)), "up fits window");
                    GestureAndAssert(window, new Vector(0, 80), 1, "down shows 100 percent");
                    GestureAndAssert(window, new Vector(-80, 0), canvas.ActualWidth / 2400, "left fits width");
                    GestureAndAssert(window, new Vector(80, 0), canvas.ActualHeight / 1600, "right fits height");
                    Assert(Field<string>(window, "_activeTabId") == tabId && Ready(window, large), "gestures retain image and tab");
                    if (edge) CheckEdgeBars(window, false);
                }
                Point center = new Point(canvas.ActualWidth / 2, canvas.ActualHeight / 2);
                double zoom = Field<ScaleTransform>(window, "_imageScale").ScaleX;
                foreach (Vector delta in new[] { new Vector(0, 0), new Vector(15, 0), new Vector(90, 90) })
                {
                    Assert((bool)Invoke(window, "BeginMouseGesture", center), "begin ignored gesture");
                    Assert(!(bool)Invoke(window, "CompleteMouseGesture", center + delta), "plain clicks and ambiguous drags do not change zoom");
                    AssertZoom(window, zoom, "ignored gesture retains zoom");
                    Assert(!canvas.IsMouseCaptured, "ignored gesture releases capture");
                }
                Assert((bool)Invoke(window, "BeginMouseGesture", center), "begin outside release");
                Assert(!(bool)Invoke(window, "CompleteMouseGesture", new Point(-80, center.Y)), "release outside image frame cancels gesture");

                Assert((bool)Invoke(window, "BeginMouseGesture", center), "begin wheel-compatible gesture");
                Assert((bool)Invoke(window, "ApplyMouseWheelZoom", center, 120, true), "right wheel still zooms during capture");
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
                GestureAndAssert(window, new Vector(0, -80), 1, "normal fit does not enlarge small images");
                GestureAndAssert(window, new Vector(-80, 0), canvas.ActualWidth / 180, "width explicitly enlarges small image");
                GestureAndAssert(window, new Vector(0, 80), 1, "down restores small image pixels");
                Invoke(window, "RotateImage", 1); Pump();
                GestureAndAssert(window, new Vector(-80, 0), canvas.ActualWidth / 90, "width uses rotated image dimensions");
                Invoke(window, "RotateImage", -1); Pump();

                Invoke(window, "ToggleFullscreen"); Pump();
                GestureAndAssert(window, new Vector(80, 0), canvas.ActualHeight / 90, "gestures work in fullscreen");
                Invoke(window, "ToggleFullscreen"); Pump();
                ThemeManager.SetDarkTheme(false); Pump();
                GestureAndAssert(window, new Vector(0, 80), 1, "gestures work in light theme");
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
        Console.WriteLine("PASS: four right-button fit gestures, release-only execution, jitter/diagonal filtering, wheel zoom, capture cancellation, thumbnails/edge/fullscreen, rotated/small images, slideshow pause/resume and setting/session/backup persistence.");
    }

    private static void GestureAndAssert(MainWindow window, Vector delta, double expectedZoom, string message)
    {
        var canvas = Field<Canvas>(window, "_imageCanvas");
        Point center = new Point(canvas.ActualWidth / 2, canvas.ActualHeight / 2);
        double before = Field<ScaleTransform>(window, "_imageScale").ScaleX;
        Assert((bool)Invoke(window, "BeginMouseGesture", center), message + " begins");
        AssertZoom(window, before, message + " waits for release");
        Assert((bool)Invoke(window, "CompleteMouseGesture", center + delta), message + " executes");
        Pump(); AssertZoom(window, expectedZoom, message); CheckZoomCentered(window);
        Assert(!canvas.IsMouseCaptured && !Field<bool>(window, "_rightGestureActive"), message + " releases capture");
    }
}
