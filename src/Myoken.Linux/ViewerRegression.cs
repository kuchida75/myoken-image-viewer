using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Myoken.Linux;

internal sealed partial class MainWindow
{
    private async Task RunViewerChecksAsync(string folder)
    {
        var largePath = Path.GetFullPath(Path.Combine(folder, "..", "large.png"));
        var tab = AddImageTab(largePath);
        var viewer = (ImageViewer)tab.Content!;
        await SelectForTestAsync(tab);
        await WaitUiAsync(() => viewer.HasImage && viewer.View.Width > 0, "large fixture failed to load");
        CheckUi(viewer.View.SourceWidth == 6000 && viewer.View.SourceHeight == 2000, "viewer retains original dimensions beyond 4096");
        CheckUi(viewer.View.IsFit && viewer.DecodedSize.Width <= 4096, "initial Fit uses bounded preview");
        viewer.InvokeActualSizeButton();
        await viewer.DetailTask;
        CheckUi(viewer.IsFullResolution && viewer.DecodedSize == new PixelSize(6000, 2000), "100% decodes original pixels rather than enlarging 4096 preview");
        CheckUi(Math.Abs(viewer.View.Zoom - 1) < 1e-9 && Math.Abs(viewer.View.Scale * viewer.View.RenderScaling - 1) < 1e-9,
            "100% maps source pixels to application render-target pixels");

        var surface = viewer.Surface;
        var anchor = new Point(viewer.View.Width * 0.4, viewer.View.Height * 0.4);
        var originalX = (anchor.X - viewer.View.X) / viewer.View.Scale;
        var originalY = (anchor.Y - viewer.View.Y) / viewer.View.Scale;
        using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var wheel = new PointerWheelEventArgs(surface, pointer, surface, anchor, 1,
            new PointerPointProperties(), KeyModifiers.None, new Vector(0, 1));
        surface.RaiseEvent(wheel);
        CheckUi(wheel.Handled && Math.Abs(viewer.View.Zoom - 1.2) < 1e-9, "wheel event routes to image zoom");
        CheckUi(Math.Abs((anchor.X - viewer.View.X) / viewer.View.Scale - originalX) < 1e-6
            && Math.Abs((anchor.Y - viewer.View.Y) / viewer.View.Scale - originalY) < 1e-6, "wheel zoom keeps cursor's source point anchored away from edges");

        viewer.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Right });
        viewer.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Down });
        var zoom = viewer.View.Zoom; var x = viewer.View.X; var y = viewer.View.Y;
        CaptureForTest("viewer-zoom-pan");
        var other = AddImageTab(_files[1]);
        await SelectForTestAsync(other);
        CheckUi(!viewer.HasImage && ((ImageViewer)other.Content!).View.IsFit, "inactive tab releases bitmap and another tab starts with independent Fit state");
        await SelectForTestAsync(tab);
        await WaitUiAsync(() => viewer.IsFullResolution, "reactivated zoomed tab did not reload source pixels");
        CheckUi(Math.Abs(viewer.View.Zoom - zoom) < 1e-9 && Math.Abs(viewer.View.X - x) < 1e-6
            && Math.Abs(viewer.View.Y - y) < 1e-6, "zoom and pan survive tab switching without retaining hidden bitmaps");
        viewer.InvokeFitButton();
        CheckUi(viewer.View.IsFit && !viewer.View.CanPan, "Fit button resets zoom and pan");

        // Cancelling a queued/in-flight full decode must not repopulate a hidden tab.
        await SelectForTestAsync(_browser);
        await SelectForTestAsync(tab);
        viewer.InvokeActualSizeButton(); var pending = viewer.DetailTask;
        await SelectForTestAsync(_browser); await pending;
        CheckUi(!viewer.HasImage && _tabs.SelectedItem == _browser, "cancelled full decode cannot populate hidden tab or change active selection");
        CloseTab(tab); CloseTab(other);

        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try
        {
            var unexpected = await ViewerImageLoader.LoadAsync(largePath, true, cancelled.Token);
            unexpected.Bitmap.Dispose(); throw new InvalidOperationException("Cancelled loader returned an image.");
        }
        catch (OperationCanceledException) { Console.WriteLine("PASS: cancelled viewer decode rejected"); }
        var bad = AddImageTab(Path.GetFullPath(Path.Combine(folder, "..", "corrupt.png")));
        await SelectForTestAsync(bad);
        CheckUi(bad.Content is ImageViewer { HasImage: false, ActualSizeEnabled: false }, "corrupt image shows error without enabling actual-size controls");
        await SelectForTestAsync(_browser); CloseTab(bad);
        Console.WriteLine("PASS: L002b viewer, full-resolution, input and per-tab regression checks");
    }
}
