using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static bool LiveCanvasIdle(FacesWindow window)
    { return !Field<bool>(window, "_canvasResizePending") && !Field<bool>(window, "_canvasResizeRunning"); }
    private static void RunLiveCanvasChecks()
    {
        string root = Path.Combine(Root, "live-canvas-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "Original.png"); MakeImage(path, 800, 600, 96);
        var source = FaceImages.Inspect(path, CancellationToken.None); var review = new FaceScanResult();
        for (int i = 0; i < 7; i++) review.Faces.Add(FaceFixture(source, FaceSolid(128, 128, (byte)(40 + i * 20), (byte)(160 - i * 10), (byte)(70 + i * 15))));
        var window = new FacesWindow(root, 4, "Auto", 4096); window.Show(); window.SetReview(review); Field<TabControl>(window, "_tabs").SelectedIndex = 1; Pump();
        var fields = Field<object>(window, "_interactive"); var width = Field<Slider>(fields, "WidthSlider"); var height = Field<Slider>(fields, "HeightSlider"); var aspect = Field<Slider>(fields, "AspectSlider");
        var preview = Field<Image>(window, "_preview");
        try
        {
            width.Value = 768; Assert(preview.Source == null && Field<bool>(window, "_canvasResizePending"), "Size slider queues the first canvas without Update canvas");
            Wait(delegate { return LiveCanvasIdle(window); }, "First automatic canvas");
            Assert(preview.Source != null && Field<Size>(window, "_canvasSize").Width == 768 && !Field<bool>(window, "_busy"), "Automatic preview renders cached crops without a blocking operation");
            var prior = preview.Source; aspect.Value = 2; Assert(preview.Source == prior, "Existing canvas stays visible during automatic resizing");
            Wait(delegate { return LiveCanvasIdle(window); }, "Automatic aspect update");
            Assert(Field<Size>(window, "_canvasSize") == new Size(768, 384), "Aspect ratio automatically applies to the displayed canvas");
            height.Value = 512; Wait(delegate { return LiveCanvasIdle(window); }, "Automatic height update");
            Assert(Field<Size>(window, "_canvasSize") == new Size(768, 512), "Height automatically updates the canvas");
            prior = preview.Source;
            FaceImages.WorkGate.Wait();
            try
            {
                width.Value = 900; PumpZoomFor(250);
                Assert(width.IsEnabled && height.IsEnabled && aspect.IsEnabled && !window.PendingWork.IsCompleted, "Sliders remain interactive while a render is queued/running");
                Assert(!Field<Button>(window, "_saveCanvas").IsEnabled && !(bool)Invoke(window, "ActivateLinkedFace", new Point(10, 10)), "Pending layout cannot be saved or activate old face links");
                for (int i = 0; i < 20; i++) width.Value = 1000 + i;
                width.Value = 1024; aspect.Value = 1.5; height.Value = 640; PumpZoomFor(200);
                Assert(preview.Source == prior && width.IsEnabled, "Rapid edits keep the previous preview and responsive inputs");
            }
            finally { FaceImages.WorkGate.Release(); }
            Wait(delegate { return LiveCanvasIdle(window); }, "Latest size wins after rapid slider changes");
            Assert(Field<Size>(window, "_canvasSize") == new Size(1024, 640) && Field<TextBox>(fields, "Width").Text == "1024"
                && Field<TextBox>(fields, "Height").Text == "640", "Superseded renders never replace the latest dimensions or slider values");
            Assert(Field<FaceCrop[]>(window, "_linkedFaces").Length == 7 && Field<Button>(window, "_saveCanvas").IsEnabled, "Completed preview restores accurate face links and Save");
            Render(window, "face-canvas-live-dark.png");
            prior = preview.Source; FaceImages.WorkGate.Wait();
            try
            {
                width.Value = 1536; PumpZoomFor(200); Invoke(window, "Escape");
                Wait(delegate { return LiveCanvasIdle(window); }, "Escape cancels automatic resize");
                Assert(window.IsVisible && preview.Source == prior && Field<TextBox>(fields, "Width").Text == "1024", "Escape preserves the canvas and restores its applied size");
            }
            finally { FaceImages.WorkGate.Release(); }
            var pixels = review.Faces[0].Pixels; review.Faces[0].Pixels = null;
            try
            {
                width.Value = 1700; Wait(delegate { return LiveCanvasIdle(window); }, "Failed preview is handled");
                Assert(preview.Source == prior && Field<TextBox>(fields, "Width").Text == "1024", "Failed rendering retains the last good canvas and rolls controls back");
            }
            finally { review.Faces[0].Pixels = pixels; }
            FaceImages.WorkGate.Wait();
            try
            {
                width.Value = 1600; PumpZoomFor(200); Field<CheckBox>(window, "_includeSubfolders").IsChecked = true;
                Wait(delegate { return LiveCanvasIdle(window); }, "Scope change cancels queued resize");
                Assert(preview.Source == null && Field<ListBox>(window, "_review").Items.Count == 0, "Scope changes cannot resurrect an old canvas");
            }
            finally { FaceImages.WorkGate.Release(); }
            window.SetReview(review); width.Value = 1200; Field<TabControl>(window, "_tabs").SelectedIndex = 0; Pump();
            Wait(delegate { return LiveCanvasIdle(window); }, "Tab change cancels pending resize");
            Assert(preview.Source == null, "Interactive render is never published into reference mode");
            fields = Field<object>(window, "_reference"); width = Field<Slider>(fields, "WidthSlider"); width.Value = 640;
            Wait(delegate { return LiveCanvasIdle(window); }, "Static reference automatically updates");
            Assert(preview.Source != null && Field<FaceCrop[]>(window, "_linkedFaces") == null, "Automatic updates also render the static reference mode");
            ThemeManager.SetDarkTheme(false); window.Width = 780; window.Height = 540; Pump(); Render(window, "face-canvas-live-narrow-light.png");
            FaceImages.WorkGate.Wait();
            try
            {
                width.Value = 800; PumpZoomFor(200); window.Close();
                Wait(delegate { return !window.IsVisible && LiveCanvasIdle(window); }, "Close joins canceled resize work");
                Assert(preview.Source == null, "Closing releases preview only after live rendering stops");
            }
            finally { FaceImages.WorkGate.Release(); }
            Assert(FaceImages.Inspect(path, CancellationToken.None).Hash == source.Hash, "Live resizing never changes original photographs");
        }
        finally { window.Close(); Pump(); ThemeManager.SetDarkTheme(true); }
        Console.WriteLine("PASS: automatic width/height/aspect previews, initial render, debounce/latest-only cancellation, responsive sliders, retained preview, safe links/Save, failure/Stop rollback, scope/tab invalidation, reference mode, close joining and unchanged originals.");
    }
}
