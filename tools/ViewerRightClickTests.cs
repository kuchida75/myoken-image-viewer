using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunRightClickChecks(MainWindow window, string image, string other)
    {
        var canvas = Field<Canvas>(window, "_imageCanvas");
        Point point = new Point(canvas.ActualWidth / 2, canvas.ActualHeight / 2);
        int wait = System.Windows.Forms.SystemInformation.DoubleClickTime + 70;
        bool initialLock = Capture(window).ZoomLocked;
        RightImageClick(window, point, 1);
        Assert(Capture(window).ZoomLocked == initialLock && !Field<bool>(window, "_slideshowPlaying"), "single right-click does not run a command");
        RightImageClick(window, point, 2);
        Assert(Capture(window).ZoomLocked == initialLock && Field<DispatcherTimer>(window, "_rightDoubleClickTimer").IsEnabled, "double-click defers lock until triple-click interval passes");
        PumpZoomFor(wait);
        Assert(Capture(window).ZoomLocked != initialLock, "double right-click toggles Zoom lock");
        Assert(Field<Border>(window, "_mouseGestureFeedback").IsVisible && Field<TextBlock>(window, "_mouseGestureAction").Text == (initialLock ? "Zoom unlocked" : "Zoom locked"), "lock state has an explicit visual cue");
        RightImageClick(window, point, 1); RightImageClick(window, point, 2); PumpZoomFor(wait);
        Assert(Capture(window).ZoomLocked == initialLock, "another double-click toggles lock back");

        var interval = Field<TextBox>(window, "_slideshowInterval"); string oldInterval = interval.Text;
        interval.Text = "30"; Invoke(window, "ApplySlideshowInterval");
        RightImageClick(window, point, 1); RightImageClick(window, point, 2); RightImageClick(window, point, 3);
        Wait(delegate { return Ready(window, image) && Field<bool>(window, "_slideshowPlaying"); }, "triple-click starts slideshow at current image");
        Assert(Field<Border>(window, "_mouseGestureFeedback").IsVisible && Field<TextBlock>(window, "_mouseGestureAction").Text == "Slideshow started", "slideshow start has a visual cue");
        PumpZoomFor(wait);
        Assert(Capture(window).ZoomLocked == initialLock, "triple-click does not also toggle Zoom lock");
        RightImageClick(window, point, 1); RightImageClick(window, point, 2); RightImageClick(window, point, 3);
        Assert(Field<bool>(window, "_slideshowPlaying") && Field<TextBlock>(window, "_mouseGestureAction").Text == "Slideshow running", "triple-click does not stop an active slideshow");
        Invoke(window, "ToggleSlideshowPause");
        RightImageClick(window, point, 1); RightImageClick(window, point, 2); RightImageClick(window, point, 3);
        Assert(!Field<bool>(window, "_slideshowPaused") && Field<TextBlock>(window, "_mouseGestureAction").Text == "Slideshow resumed", "triple-click resumes a paused slideshow with a cue");
        Invoke(window, "StopSlideshow"); interval.Text = oldInterval; Invoke(window, "ApplySlideshowInterval");

        RightImageClick(window, point, 1); RightImageClick(window, point, 2);
        Assert((bool)Invoke(window, "BeginRightButtonInput", point, 3), "third press can start a drag instead of a click");
        Invoke(window, "UpdateMouseGestureFeedback", point + new Vector(-80, 0));
        Invoke(window, "CompleteRightButtonInput", point + new Vector(-80, 0)); PumpZoomFor(wait);
        Assert(Capture(window).ZoomLocked == initialLock && !Field<bool>(window, "_slideshowPlaying"), "third-press drag cancels pending click commands");
        RightImageClick(window, point, 1); RightImageClick(window, point, 2);
        Invoke(window, "BeginRightButtonInput", point, 3);
        Invoke(window, "ApplyMouseWheelZoom", point, 120, true);
        Invoke(window, "CompleteRightButtonInput", point); PumpZoomFor(wait);
        Assert(Capture(window).ZoomLocked == initialLock && !Field<bool>(window, "_slideshowPlaying"), "wheel zoom does not count as a double or triple click");

        RightImageClick(window, point, 1); RightImageClick(window, point, 2);
        Invoke(window, "OpenBrowserImage", other); Wait(delegate { return Ready(window, other); }, "navigate during pending double-click"); PumpZoomFor(wait);
        Assert(Capture(window).ZoomLocked == initialLock, "pending double-click cannot affect another image");
        Invoke(window, "OpenBrowserImage", image); Wait(delegate { return Ready(window, image); }, "return after right-click checks");
        point = new Point(canvas.ActualWidth / 2, canvas.ActualHeight / 2);
        RightImageClick(window, point, 1); RightImageClick(window, point, 2);
        var setting = Field<CheckBox>(window, "_mouseGesturesCheckBox"); setting.IsChecked = false; PumpZoomFor(wait);
        Assert(Capture(window).ZoomLocked == initialLock && !(bool)Invoke(window, "BeginRightButtonInput", point, 1), "disabling gestures cancels and disables click actions");
        setting.IsChecked = true;
        Console.WriteLine("PASS: right double/triple-click timing, lock toggle and cues, slideshow start/resume, no overlap, drag/wheel cancellation and image/settings isolation.");
    }

    private static void RightImageClick(MainWindow window, Point point, int count)
    {
        Assert((bool)Invoke(window, "BeginRightButtonInput", point, count), "right-click begins in image view");
        Invoke(window, "CompleteRightButtonInput", point);
    }
}
