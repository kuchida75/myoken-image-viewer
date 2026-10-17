using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunFaceFramingChecks()
    {
        string root = Path.Combine(Root, "face-framing-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Portrait.png"); var pixels = FaceSolid(400, 400, 20, 150, 30);
        for (int y = 150; y < 250; y++) for (int x = 150; x < 250; x++)
        { int i = (y * 400 + x) * 4; pixels.Data[i] = 30; pixels.Data[i + 1] = 30; pixels.Data[i + 2] = 230; }
        SaveAdvancedBitmap(pixels.Bitmap(), path); var source = FaceImages.Inspect(path, CancellationToken.None);
        var face = new FaceCrop { Source = source, Box = new FaceBox(.375, .375, .25, .25, .99) };
        foreach (double ratio in new[] { 1, .8, 2.0 / 3, 4.0 / 3, 1.5, 16.0 / 9 })
        foreach (int amount in new[] { 0, 30, 200 })
        foreach (var box in new[] { face.Box, new FaceBox(0, 0, .2, .3, .9), new FaceBox(.8, .8, .2, .2, .9) })
        {
            var rect = FaceImages.CropBounds(box, 400, 300, new FaceCropOptions { Aspect = ratio, Surroundings = amount });
            Assert(rect.X >= 0 && rect.Y >= 0 && rect.X + rect.Width <= 400 && rect.Y + rect.Height <= 300, "Framed crops fit inside the source at every edge");
            Assert(Math.Abs(rect.Width - rect.Height * ratio) <= 1.5, "Square/rectangular proportions are preserved to pixel rounding");
        }
        var tight = FaceImages.Reframe(new[] { face }, new FaceCropOptions { Surroundings = 0 }, 30, CancellationToken.None, null)[0];
        var wide = FaceImages.Reframe(new[] { face }, new FaceCropOptions { Surroundings = 100 }, 30, CancellationToken.None, null)[0];
        Assert(tight.Crop.Width == 100 && wide.Crop.Width == 200 && tight.Pixels.Data[1] == 30 && wide.Pixels.Data[1] == 150, "Surroundings changes the actual source area, not image stretching");
        Assert(wide.Box.Equals(face.Box) && wide.Source == source && FaceImages.Inspect(path, CancellationToken.None).Hash == source.Hash, "Recropping retains detections/provenance and never changes originals");
        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); FaceReject(delegate { FaceImages.Reframe(new[] { face }, new FaceCropOptions(), 2, cancel.Token, null); }, "Queued reframing honors cancellation"); }
        FaceReject(delegate { new FaceCropOptions { Surroundings = 201 }.Validate(); }, "Invalid surroundings rejected");
        FaceReject(delegate { new FaceCropOptions { Aspect = Double.NaN }.Validate(); }, "Non-finite aspect rejected");
        var portrait = FaceImages.Reframe(new[] { face }, new FaceCropOptions { Surroundings = 50, Aspect = .8 }, 2, CancellationToken.None, null)[0];
        var options = new FaceCanvasOptions { Width = 256, AutoColumns = true, CellAspect = .8 }; options.Validate(1);
        Assert(options.Height == 320, "Canvas auto-height follows portrait crop proportions");
        FaceImages.Export(new[] { portrait }, options.Copy(), Path.Combine(root, "portrait-reference.png"), CancellationToken.None, null);
        var review = new FaceScanResult { Backend = "Fixture crops - no inference" }; review.Faces.Add(tight);
        var window = new FacesWindow(root, 4, "Auto", 4096); window.Show(); window.SetReview(review); Pump();
        try
        {
            var slider = Field<Slider>(window, "_surroundings"); var shapes = Field<ComboBox>(window, "_cropShape");
            Assert(slider.Value == 0 && shapes.Items.Count == 6, "Review restores framing and exposes square/portrait/landscape shapes");
            Field<TabControl>(window, "_tabs").SelectedIndex = 1;
            Field<Button>(window, "_update").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(delegate { return window.PendingWork.IsCompleted; }, "Initial framed map");
            slider.Value = 100; shapes.SelectedIndex = 1;
            Wait(delegate { return !Field<bool>(window, "_framingPending") && window.PendingWork.IsCompleted; }, "Debounced framing and canvas update"); Pump();
            var crops = Field<FaceCrop[]>(window, "_linkedFaces");
            Assert(crops != null && crops[0].Framing.Surroundings == 100 && crops[0].Framing.Aspect == .8 && Field<Image>(window, "_preview").Source != null, "Surroundings/shape changes rebuild crop review and interactive map without detection");
            Assert(Math.Abs(Field<Size>(window, "_canvasSize").Width / Field<Size>(window, "_canvasSize").Height - .8) < .001, "Interactive map cells match the selected rectangle");
            window.Width = 780; window.Height = 540; Pump(); Render(window, "face-framing-narrow.png");
            AssertInside(slider, (FrameworkElement)window.Content); AssertInside(shapes, (FrameworkElement)window.Content);
            var originalCanvas = Field<Image>(window, "_preview").Source;
            slider.Value = 150; Invoke(window, "StopWork"); Pump();
            Assert(slider.Value == 100 && Field<Image>(window, "_preview").Source == originalCanvas, "Cancel pending framing restores controls without losing the map");
            FaceImages.WorkGate.Wait();
            try
            {
                slider.Value = 50; Wait(delegate { return Field<bool>(window, "_busy"); }, "Queued framing operation begins"); Invoke(window, "StopWork");
                Wait(delegate { return window.PendingWork.IsCompleted; }, "Queued framing cancels");
                Assert(slider.Value == 100 && Field<Image>(window, "_preview").Source == originalCanvas, "Canceled framing commits neither new crops nor a partial map");
            }
            finally { FaceImages.WorkGate.Release(); }
            Field<Button>(window, "_resetFraming").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(delegate { return !Field<bool>(window, "_framingPending") && window.PendingWork.IsCompleted; }, "Reset framing");
            Assert(slider.Value == 30 && shapes.SelectedIndex == 0, "Reset restores square/30 percent defaults");
        }
        finally { window.Close(); Pump(); }
        File.WriteAllText(path, "Changed source"); FaceReject(delegate { FaceImages.Reframe(new[] { wide }, new FaceCropOptions(), 2, CancellationToken.None, null); }, "Recropping refuses changed source files");
        Console.WriteLine("PASS: face surroundings, six crop shapes, edge-fitting geometry, pixel changes, source preservation, ratio-aware PNG/map layout, live recrop without detection, narrow controls, reset and atomic cancellation.");
    }
}
