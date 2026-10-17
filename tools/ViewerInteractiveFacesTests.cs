using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunInteractiveFacesChecks()
    {
        string root = Path.Combine(Root, "interactive-faces-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
        string a = Path.Combine(root, "Original-A.png"), b = Path.Combine(root, "Original-B.png");
        MakeImage(a, 1600, 1000, 96); MakeImage(b, 900, 1400, 96);
        var first = FaceFixture(FaceImages.Inspect(a, CancellationToken.None), FaceSolid(128, 128, 80, 160, 210));
        var second = FaceFixture(FaceImages.Inspect(b, CancellationToken.None), FaceSolid(128, 128, 170, 110, 40));
        var review = new FaceScanResult(); review.Faces.AddRange(new[] { first, second, first, second, first, second, first });
        var window = new FacesWindow(root, 4, "Auto", 4096); window.Show(); window.SetReview(review); Pump();
        try
        {
            var tabs = Field<TabControl>(window, "_tabs");
            Assert(tabs.Items.Cast<TabItem>().Select(t => (string)t.Header).SequenceEqual(new[] { "Faces reference", "Interactive faces" }), "Interactive faces replaces the Photo mosaic tab");
            var update = Field<Button>(window, "_update"); var image = Field<Image>(window, "_preview"); var viewport = Field<ScrollViewer>(window, "_viewport");
            update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(delegate { return window.PendingWork.IsCompleted; }, "PNG reference preview");
            Assert(!(bool)Invoke(window, "ActivateLinkedFace", new Point(10, 10)) && Field<Button>(window, "_export").IsEnabled, "PNG reference remains static and exportable");
            tabs.SelectedIndex = 1; update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(delegate { return window.PendingWork.IsCompleted; }, "Interactive canvas preview"); Pump();
            Assert(!Field<Button>(window, "_export").IsEnabled && Field<Int32Rect[]>(window, "_linkedCells").Length == 7, "Interactive canvas has source links, not a misleading PNG export");
            Assert(!(bool)Invoke(window, "ActivateLinkedFace", new Point(-10, 0)), "Click outside the map does nothing");
            var options = (FaceCanvasOptions)Invoke(window, "Options"); var cells = FaceImages.ReferenceLayout(options, 7);
            foreach (double zoom in new[] { .25, .6, 1.0 })
            {
                typeof(FacesWindow).GetField("_fit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(window, false);
                Invoke(window, "SetZoom", zoom); Pump();
                for (int i = 0; i < cells.Length; i++)
                {
                    var at = new Point((cells[i].X + cells[i].Width * .5) * image.ActualWidth / options.Width, (cells[i].Y + cells[i].Height * .5) * image.ActualHeight / options.Height);
                    Assert(Invoke(window, "LinkedFaceAt", at) == review.Faces[i], "Scaled map hit-testing preserves each exact original link");
                }
            }
            Invoke(window, "SetZoom", .6); Pump(); viewport.ScrollToHorizontalOffset(120); viewport.ScrollToVerticalOffset(240); Pump();
            var beforeDrag = window.PendingWork;
            Invoke(window, "BeginPan", new Point(200, 200)); Invoke(window, "MovePan", new Point(160, 140)); Pump();
            Assert(!(bool)Invoke(window, "CompletePan", new Point(20, 20)) && window.PendingWork == beforeDrag && Field<object>(window, "_canvasSnapshot") == null, "Dragging the map never opens an original");
            Assert(viewport.HorizontalOffset > 120 && viewport.VerticalOffset > 240, "Left-drag pans the enlarged map");
            double savedX = viewport.HorizontalOffset, savedY = viewport.VerticalOffset, savedZoom = Field<double>(window, "_zoom"); var canvas = image.Source;
            var cell = cells[1]; var point = new Point((cell.X + cell.Width * .5) * image.ActualWidth / options.Width, (cell.Y + cell.Height * .5) * image.ActualHeight / options.Height);
            Assert((bool)Invoke(window, "ActivateLinkedFace", point), "Face click opens its original"); Wait(delegate { return window.PendingWork.IsCompleted; }, "Linked original opens"); Pump();
            Assert(window.IsVisible && Field<object>(window, "_canvasSnapshot") != null && ((BitmapSource)image.Source).PixelWidth == 900
                && ((BitmapSource)image.Source).PixelHeight == 1400, "Original is decoded at full resolution inside the same Faces view");
            Assert(Field<TextBlock>(window, "_status").Text.Contains("Original-B.png") && Field<Button>(window, "_backToCanvas").IsVisible, "Original has a clear file status and canvas return action");
            typeof(FacesWindow).GetField("_fit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(window, false);
            Invoke(window, "SetZoom", 2.0); Pump(); viewport.ScrollToHorizontalOffset(200); viewport.ScrollToVerticalOffset(300); Pump();
            Assert(image.ActualWidth == 1800 && image.ActualHeight == 2800 && viewport.HorizontalOffset > 0 && viewport.VerticalOffset > 0, "Linked original can zoom and pan without changing proportions");
            Invoke(window, "BeginPan", new Point(200, 200)); Invoke(window, "MovePan", new Point(160, 140)); Pump(); Invoke(window, "CompletePan", new Point(100, 100));
            Assert(viewport.HorizontalOffset > 200 && viewport.VerticalOffset > 300, "Left-drag pans the full-resolution original");
            Render(window, "faces-interactive-original.png"); PressKey(window, Key.Escape); Pump();
            Assert(window.IsVisible && image.Source == canvas && Field<object>(window, "_canvasSnapshot") == null, "Esc returns to the canvas without closing Faces");
            AssertNear(Field<double>(window, "_zoom"), savedZoom, "Canvas zoom restored"); AssertNear(viewport.HorizontalOffset, savedX, "Canvas horizontal position restored"); AssertNear(viewport.VerticalOffset, savedY, "Canvas vertical position restored");
            Invoke(window, "ActivateLinkedFace", point); Wait(delegate { return window.PendingWork.IsCompleted; }, "Original opens before mouse Back");
            var forward = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.XButton2) { RoutedEvent = Mouse.PreviewMouseDownEvent };
            window.RaiseEvent(forward); Assert(!forward.Handled && Field<object>(window, "_canvasSnapshot") != null, "Forward mouse button does not unexpectedly leave the original");
            var back = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.XButton1) { RoutedEvent = Mouse.PreviewMouseDownEvent };
            window.RaiseEvent(back); Pump();
            Assert(back.Handled && window.IsVisible && image.Source == canvas && Field<object>(window, "_canvasSnapshot") == null, "Mouse Back returns from the original to the same canvas");
            AssertNear(Field<double>(window, "_zoom"), savedZoom, "Mouse Back restores canvas zoom"); AssertNear(viewport.HorizontalOffset, savedX, "Mouse Back restores canvas horizontal position"); AssertNear(viewport.VerticalOffset, savedY, "Mouse Back restores canvas vertical position");
            back = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.XButton1) { RoutedEvent = Mouse.PreviewMouseDownEvent };
            window.RaiseEvent(back); Assert(!back.Handled && window.IsVisible, "Mouse Back on the map does not close Faces");
            Render(window, "faces-interactive-canvas.png");
            FaceImages.WorkGate.Wait();
            try
            {
                Invoke(window, "ActivateLinkedFace", point); Pump(); Invoke(window, "Escape");
                Wait(delegate { return window.PendingWork.IsCompleted; }, "Opening original cancels");
                Assert(image.Source == canvas && Field<object>(window, "_canvasSnapshot") == null, "Canceled loading retains the canvas");
            }
            finally { FaceImages.WorkGate.Release(); }
            File.Delete(b); Invoke(window, "ActivateLinkedFace", point); Wait(delegate { return window.PendingWork.IsCompleted; }, "Missing original is handled");
            Assert(Field<TextBlock>(window, "_status").Text == "Original image not available" && image.Source == canvas && window.IsVisible, "Missing original is reported in the status bar without losing the canvas");
            Assert(((string)Field<TextBlock>(window, "_status").ToolTip).Contains(b), "Unavailable status retains the original path for inspection");
            window.Width = 780; window.Height = 540; Pump(); Render(window, "faces-interactive-missing-narrow.png");
            AssertInside(Field<Button>(window, "_update"), (FrameworkElement)window.Content);
            var refresh = Field<Button>(window, "_refreshLinks"); Assert(refresh.IsVisible && refresh.IsEnabled, "Interactive map exposes Refresh links");
            var check = FaceLinks.Refresh(review.Faces, 30, CancellationToken.None, null);
            Assert(check.RemovedFaces == 3 && check.UnavailablePaths.SequenceEqual(new[] { b }) && check.Faces.Length == 4 && check.Faces.All(f => f == first), "Link refresh checks each source once and removes every face linked to missing originals");
            using (var canceled = new CancellationTokenSource())
            { canceled.Cancel(); FaceReject(delegate { FaceLinks.Refresh(review.Faces, 4, canceled.Token, null); }, "Canceled link check never commits a result"); }
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(delegate { return window.PendingWork.IsCompleted; }, "Refresh links rebuilds map"); Pump();
            Assert(Field<ListBox>(window, "_review").Items.Count == 4 && Field<FaceCrop[]>(window, "_linkedFaces").Length == 4 && image.Source != null, "Refresh removes unavailable crops and regenerates the linked map");
            Assert(Field<TextBlock>(window, "_status").Text.Contains("Removed 3 face links from 1") && Field<Int32Rect[]>(window, "_linkedCells").Sum(c => (long)c.Width * c.Height) == (long)Field<Size>(window, "_canvasSize").Width * Field<Size>(window, "_canvasSize").Height, "Refreshed map reports removals and fills the canvas");
            Render(window, "faces-interactive-refreshed.png");
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(delegate { return window.PendingWork.IsCompleted; }, "All-valid refresh");
            Assert(Field<ListBox>(window, "_review").Items.Count == 4 && Field<TextBlock>(window, "_status").Text.Contains("Removed 0"), "Refreshing valid links preserves the reviewed faces");
            var beforeRefresh = image.Source; FaceImages.WorkGate.Wait();
            try
            {
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpZoomFor(100); Invoke(window, "Escape");
                Wait(delegate { return window.PendingWork.IsCompleted; }, "Refresh cancellation finishes before publication");
                Assert(image.Source == beforeRefresh && Field<ListBox>(window, "_review").Items.Count == 4, "Canceled refresh preserves the complete current map and review");
            }
            finally { FaceImages.WorkGate.Release(); }
            File.Delete(a); refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(delegate { return window.PendingWork.IsCompleted; }, "All originals missing refresh");
            Assert(Field<ListBox>(window, "_review").Items.Count == 0 && image.Source == null && !Field<Button>(window, "_update").IsEnabled
                && Field<TextBlock>(window, "_status").Text.StartsWith("No available original images"), "All-missing refresh clears the map without creating an empty white canvas");
        }
        finally { window.Close(); Pump(); }
        Console.WriteLine("PASS: interactive face links, scale-aware hit tests, same-view full-resolution originals, mouse pan, Esc/state restore, PNG separation, cancellation, unavailable-source status and partial/all-missing link refresh with automatic canvas rebuild.");
    }
}
