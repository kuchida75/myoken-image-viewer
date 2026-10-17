using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunCanvasNavigatorChecks()
    {
        string root = Path.Combine(Root, "canvas-navigator-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Original.png"); MakeImage(path, 800, 600, 96);
        var face = FaceFixture(FaceImages.Inspect(path, CancellationToken.None), FaceSolid(128, 128, 80, 160, 210));
        var review = new FaceScanResult(); review.Faces.AddRange(Enumerable.Repeat(face, 24));
        var window = new FacesWindow(root, 4, "Auto", 4096); window.Show(); window.SetReview(review); Pump();
        try
        {
            Field<TabControl>(window, "_tabs").SelectedIndex = 1;
            var fields = Field<object>(window, "_interactive"); Field<TextBox>(fields, "Width").Text = "4096";
            Invoke(window, "UpdateCanvas"); Wait(delegate { return window.PendingWork.IsCompleted; }, "Navigator map preview"); Pump();
            var overlay = Field<Border>(window, "_canvasNavigatorOverlay"); var navigator = Field<ImageNavigator>(window, "_canvasNavigator");
            var viewport = Field<ScrollViewer>(window, "_viewport"); var preview = Field<Image>(window, "_preview"); var source = preview.Source;
            Assert(overlay.Visibility == Visibility.Collapsed && navigator.Source == null, "Fitted canvas hides the overview and releases its source");
            typeof(FacesWindow).GetField("_fit", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, false);
            Invoke(window, "SetZoom", 1.0); Pump();
            Assert(overlay.IsVisible && navigator.Source == source && !navigator.ImageBounds.IsEmpty && !navigator.ViewportBounds.IsEmpty, "Zoomed canvas shows a nonblank overview with a viewport rectangle");
            AssertInside(overlay, (FrameworkElement)viewport.Parent);
            double x = viewport.HorizontalOffset, y = viewport.VerticalOffset;
            var center = NavigatorCenter(navigator.ViewportBounds);
            Assert(navigator.BeginDrag(center), "Canvas viewport rectangle captures drag");
            navigator.UpdateDrag(new Point(center.X + 4, center.Y + 3), true); Pump();
            Assert(viewport.HorizontalOffset > x && viewport.VerticalOffset > y, "Small overview drags pan both axes smoothly");
            navigator.UpdateDrag(new Point(10000, 10000), true); Pump();
            AssertNear(viewport.HorizontalOffset, viewport.ScrollableWidth, "Overview clamps to the right canvas edge");
            AssertNear(viewport.VerticalOffset, viewport.ScrollableHeight, "Overview clamps to the bottom canvas edge");
            navigator.UpdateDrag(new Point(-10000, -10000), true); Pump(); navigator.EndDrag();
            AssertNear(viewport.HorizontalOffset, 0, "Overview clamps to the left canvas edge"); AssertNear(viewport.VerticalOffset, 0, "Overview clamps to the top canvas edge");
            Assert(Field<object>(window, "_canvasSnapshot") == null && preview.Source == source, "Navigator input never activates a linked face");
            navigator.BeginDrag(new Point(navigator.ImageBounds.Right - 1, navigator.ImageBounds.Bottom - 1)); Pump(); navigator.EndDrag();
            Assert(viewport.HorizontalOffset > 100 && viewport.VerticalOffset > 100, "Clicking elsewhere in the overview jumps to that region");
            double savedX = viewport.HorizontalOffset, savedY = viewport.VerticalOffset;
            Invoke(window, "ActivateLinkedFace", new Point(10, 10)); Wait(delegate { return window.PendingWork.IsCompleted; }, "Open linked original during navigation"); Pump();
            Assert(Field<object>(window, "_canvasSnapshot") != null && !overlay.IsVisible, "Canvas overview is hidden while its original photograph is open");
            Invoke(window, "ReturnToCanvas"); Pump();
            Assert(overlay.IsVisible && navigator.Source == source, "Returning to the canvas restores its overview");
            AssertNear(viewport.HorizontalOffset, savedX, "Navigator return keeps horizontal position"); AssertNear(viewport.VerticalOffset, savedY, "Navigator return keeps vertical position");
            Render(window, "face-canvas-navigator-dark.png"); Render(navigator, "face-canvas-navigator-preview.png");
            navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds)); window.Width = 780; window.Height = 540; Pump();
            Assert(!navigator.IsDragging && overlay.IsVisible, "Resizing cancels capture but retains the overview");
            AssertInside(overlay, (FrameworkElement)viewport.Parent); Render(window, "face-canvas-navigator-narrow.png");
            ThemeManager.SetDarkTheme(false); Pump(); Render(window, "face-canvas-navigator-light.png"); ThemeManager.SetDarkTheme(true);
            string saved = Path.Combine(root, "Navigator.myokenfaces"); window.SaveCanvasFile(saved); Wait(delegate { return window.PendingWork.IsCompleted; }, "Save zoomed navigator state"); Pump();
            window.OpenCanvasFile(saved); Wait(delegate { return window.PendingWork.IsCompleted; }, "Reopen zoomed navigator state"); Pump();
            Assert(overlay.IsVisible && !navigator.ViewportBounds.IsEmpty, "Saved canvas restores its zoomed overview automatically");
            navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds));
            typeof(FacesWindow).GetField("_fit", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, true); Invoke(window, "UpdateZoom"); Pump();
            Assert(!overlay.IsVisible && !navigator.IsDragging && navigator.Source == null, "Fitting releases drag capture and hides the overview");
            Field<TabControl>(window, "_tabs").SelectedIndex = 0; Invoke(window, "UpdateCanvas"); Wait(delegate { return window.PendingWork.IsCompleted; }, "Static reference preview");
            typeof(FacesWindow).GetField("_fit", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, false); Invoke(window, "SetZoom", 1.0); Pump();
            Assert(overlay.IsVisible, "Static Faces reference canvases also support the overview");
            Invoke(window, "InvalidatePreview"); Pump(); Assert(!overlay.IsVisible && navigator.Source == null, "Invalidating a canvas releases the navigator thumbnail");
        }
        finally { window.Close(); Pump(); ThemeManager.SetDarkTheme(true); }
        Console.WriteLine("PASS: canvas overview visibility, cached image, drag/click navigation, edge clamping, original/map return, resize and fit capture release, saved viewport restore, static references, dark/light and narrow layouts.");
    }
}
