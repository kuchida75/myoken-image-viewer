using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunRotationGesturePathChecks()
    {
        foreach (int direction in new[] { -1, 1 })
        {
            var path = new MouseGesturePath(new Point(200, 200));
            ImageMouseGesture result = ImageMouseGesture.None;
            for (int y = 1; y <= 100; y++) result = path.Track(new Point(200, 200 - y));
            Assert(result == ImageMouseGesture.FitWindow, "upward first leg remains Fit window");
            for (int x = 1; x <= 100; x++) result = path.Track(new Point(200 + direction * x, 100 + (x % 3 - 1)));
            Assert(result == (direction < 0 ? ImageMouseGesture.RotateLeft : ImageMouseGesture.RotateRight), "dense two-leg path tolerates minor hand jitter");
            Assert(path.Track(new Point(200 + direction * 100, 100)) == result, "release at last sample retains rotation");
            Assert(path.Track(new Point(200 + direction * 100, 150)) == ImageMouseGesture.None, "a third distinct stroke cancels instead of guessing");
        }
        foreach (double slope in new[] { -1.0, -.4, .4, 1.0 })
        {
            var diagonal = new MouseGesturePath(new Point(300, 300));
            for (int y = 1; y <= 160; y++)
            {
                var result = diagonal.Track(new Point(300 + y * slope, 300 - y));
                Assert(result != ImageMouseGesture.RotateLeft && result != ImageMouseGesture.RotateRight, "a diagonal is not a two-leg rotation");
            }
        }
        var shortTurn = new MouseGesturePath(new Point(200, 200));
        shortTurn.Track(new Point(200, 120));
        Assert(shortTurn.Track(new Point(180, 120)) == ImageMouseGesture.FitWindow, "short sideways jitter does not rotate");
        var wrongOrder = new MouseGesturePath(new Point(200, 200));
        wrongOrder.Track(new Point(140, 200)); wrongOrder.Track(new Point(140, 100));
        var final = wrongOrder.Track(new Point(70, 100));
        Assert(final != ImageMouseGesture.RotateLeft && final != ImageMouseGesture.RotateRight, "rotation must start with Up");
    }

    private static void RotationGestureAndAssert(MainWindow window, int direction)
    {
        var canvas = Field<Canvas>(window, "_imageCanvas");
        Point start = new Point(canvas.ActualWidth / 2, canvas.ActualHeight / 2);
        Point corner = start + new Vector(0, -80), end = corner + new Vector(direction * 80, 0);
        var state = (ImageTabState)Invoke(window, "CurrentTabState");
        string id = Field<string>(window, "_activeTabId"), path = state.Path;
        int before = state.RotationQuarterTurns;
        Size size = (Size)Invoke(window, "DisplayedImageSize");
        byte[] original = File.ReadAllBytes(path);
        Assert(!Capture(window).AutoSaveRotations, "rotation gesture test uses non-destructive default");
        Assert((bool)Invoke(window, "BeginMouseGesture", start), "rotation gesture starts");
        Invoke(window, "UpdateMouseGestureFeedback", corner);
        Assert(Field<TextBlock>(window, "_mouseGestureAction").Text == "Fit window", "first Up leg previews existing fit action");
        Invoke(window, "UpdateMouseGestureFeedback", end); Pump();
        Assert(Field<TextBlock>(window, "_mouseGestureArrow").Text == (direction < 0 ? "\u2191 \u2190" : "\u2191 \u2192"), "rotation cue shows both gesture legs");
        Assert(Field<TextBlock>(window, "_mouseGestureAction").Text == (direction < 0 ? "Rotate left 90\u00b0" : "Rotate right 90\u00b0"), "rotation cue shows correct action");
        Assert(state.RotationQuarterTurns == before, "rotation waits until release");
        if (direction < 0) Render((FrameworkElement)window.Content, "mouse-gesture-rotate-left-preview.png");
        Assert((bool)Invoke(window, "CompleteMouseGesture", end), "rotation executes on release"); Pump();
        Assert(state.RotationQuarterTurns == ImageViewport.NormalizeRotation(before + direction), "rotation changes by exactly one quarter-turn");
        Size rotated = (Size)Invoke(window, "DisplayedImageSize");
        AssertNear(rotated.Width, size.Height, "rotation swaps displayed width");
        AssertNear(rotated.Height, size.Width, "rotation swaps displayed height");
        Assert(Ready(window, path) && Field<string>(window, "_activeTabId") == id && !canvas.IsMouseCaptured, "rotation retains image/tab and releases capture");
        CheckZoomCentered(window);
        Assert(original.SequenceEqual(File.ReadAllBytes(path)), "default rotation gesture leaves original file untouched");
        Assert(Field<TextBlock>(window, "_mouseGestureHint").Text == "Applied", "rotation completion is confirmed");
        Assert(!(bool)Invoke(window, "CompleteMouseGesture", end) && state.RotationQuarterTurns == ImageViewport.NormalizeRotation(before + direction), "extra mouse-up cannot rotate twice");
    }

    private static void CheckWheelCancelsRotationGesture(MainWindow window)
    {
        var canvas = Field<Canvas>(window, "_imageCanvas");
        Point start = new Point(canvas.ActualWidth / 2, canvas.ActualHeight / 2);
        var state = (ImageTabState)Invoke(window, "CurrentTabState");
        int turns = state.RotationQuarterTurns;
        Assert((bool)Invoke(window, "BeginMouseGesture", start), "wheel-cancel rotation begins");
        Invoke(window, "UpdateMouseGestureFeedback", start + new Vector(0, -80));
        Invoke(window, "UpdateMouseGestureFeedback", start + new Vector(80, -80));
        Invoke(window, "ApplyMouseWheelZoom", start, 120, true);
        Assert(!(bool)Invoke(window, "CompleteMouseGesture", start + new Vector(80, -80)), "wheel suppresses pending two-leg rotation");
        Assert(state.RotationQuarterTurns == turns && !Field<Border>(window, "_mouseGestureFeedback").IsVisible, "wheel zoom cannot accidentally rotate on release");
    }
}
