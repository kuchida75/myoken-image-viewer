using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static T WaitUpscale<T>(Task<T> task)
    { Wait(delegate { return task.IsCompleted; }, "AI upscale completion"); return task.GetAwaiter().GetResult(); }

    private static BitmapSource UpscaleFixture(int width, int height)
    {
        var data = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int at = (y * width + x) * 4;
            double wave = x < width / 2 ? Math.Sin(x * 0.5) * Math.Sin(y * 0.4) : ((x / 3 + y / 3) % 2 == 0 ? -1 : 1);
            data[at] = (byte)(75 + wave * 25); data[at + 1] = (byte)(110 + wave * 25); data[at + 2] = (byte)(165 + wave * 25);
            data[at + 3] = (byte)(x < 8 ? 0 : x < 16 ? 128 : 255);
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, data, width * 4); image.Freeze(); return image;
    }

    private static void RunSuperResolutionChecks()
    {
        Directory.CreateDirectory(Root);
        string folder = Path.Combine(Root, "upscale-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(folder);
        BitmapSource source = UpscaleFixture(205, 73);
        string path = Path.Combine(folder, "source.png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using (var stream = File.Create(path)) encoder.Save(stream);
        byte[] originalFile = File.ReadAllBytes(path);
        var request = new SuperResolutionRequest { Source = source, Workers = 4, Scale = 1.6 };
        SuperResolutionResult cpu = null;
        foreach (double factor in SuperResolution.Scales)
        {
            request.Scale = factor;
            var result = WaitUpscale(SuperResolution.RunAsync(request, CancellationToken.None, null));
            Assert(result.Pixels.PixelWidth == (int)Math.Round(205 * factor) && result.Pixels.PixelHeight == (int)Math.Round(73 * factor), "exact fractional output dimensions " + factor);
            Assert(result.Pixels.IsFrozen && result.Comparison.IsFrozen, "upscale outputs cross threads safely");
            if (factor == 1) Assert(result.Tiles == 0 && result.Backend.Contains("bypassed"), "1x bypasses all AI reconstruction");
            else Assert(result.Tiles == 2 && result.Backend == "CPU" && !String.IsNullOrEmpty(result.Analysis), "real tiled CPU inference and subject analysis");
            if (factor == 1.6) cpu = result;
        }
        byte[] actual = SubjectSegmentation.ReadPixels(cpu.Pixels, cpu.Pixels.PixelWidth, cpu.Pixels.PixelHeight);
        byte[] baseline = SubjectSegmentation.ReadPixels(source, cpu.Pixels.PixelWidth, cpu.Pixels.PixelHeight);
        Console.WriteLine("AI/baseline mean channel difference: " + actual.Zip(baseline, (a, b) => Math.Abs(a - b)).Average().ToString("0.000"));
        Assert(actual.Where((v, i) => i % 4 < 3 && baseline[i - i % 4 + 3] == 255 && Math.Abs(v - baseline[i]) > 2).Count() > 80,
            "AI reconstruction changes detail beyond ordinary resampling");
        Assert(actual.Where((v, i) => i % 4 == 3).SequenceEqual(baseline.Where((v, i) => i % 4 == 3)), "alpha is retained through upscale and rendering");
        for (int i = 0; i < actual.Length; i += 4)
            if (actual[i + 3] == 255)
                Assert(Math.Abs((actual[i + 2] - actual[i + 1]) - (baseline[i + 2] - baseline[i + 1])) <= 1
                    && Math.Abs((actual[i + 1] - actual[i]) - (baseline[i + 1] - baseline[i])) <= 1, "neutral upscale retains source chroma");
        byte[] foreground = { 70, 110, 165, 255 }, background = (byte[])foreground.Clone(), skin = (byte[])foreground.Clone();
        SuperResolution.BlendDetail(foreground, 0, 200, 1, false); SuperResolution.BlendDetail(background, 0, 200, 0, false);
        SuperResolution.BlendDetail(skin, 0, 200, 1, true);
        Assert(foreground[0] > background[0] && skin[0] < foreground[0], "subject/background weights differ and person skin detail is gentler");
        foreach (var adapter in GpuHardware.Devices.Where(d => d.IsIntegrated != true))
        {
            request.Scale = 1.6; request.Adapter = adapter; request.SoftLimitMb = 16384;
            var gpu = WaitUpscale(SuperResolution.RunAsync(request, CancellationToken.None, null));
            Assert(gpu.Backend.StartsWith("DirectML:"), "upscale runs on selected discrete GPU");
            byte[] gp = SubjectSegmentation.ReadPixels(gpu.Pixels, gpu.Pixels.PixelWidth, gpu.Pixels.PixelHeight);
            Assert(gp.Zip(actual, (a, b) => Math.Abs(a - b)).Average() < 1, "DirectML/CPU upscale parity");
            Console.WriteLine("PASS: AI upscale on " + gpu.Backend);
            request.SoftLimitMb = 1;
            var fallback = WaitUpscale(SuperResolution.RunAsync(request, CancellationToken.None, null));
            Assert(fallback.Backend.Contains("CPU fallback"), "AI soft memory limit falls back to CPU");
        }
        request.Adapter = null; request.SoftLimitMb = 2048;
        foreach (double invalid in new[] { 0, 1.1, Double.NaN, Double.PositiveInfinity, 4 })
        {
            bool rejected = false; try { SuperResolution.OutputSize(100, 100, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert(rejected, "unsupported/nonfinite scale rejected");
        }
        bool limit = false; try { SuperResolution.OutputSize(4000, 3000, 1.25); } catch (InvalidOperationException) { limit = true; }
        Assert(limit, "input memory safety limit"); limit = false;
        try { SuperResolution.OutputSize(2800, 2800, 3); } catch (InvalidOperationException) { limit = true; }
        Assert(limit, "output memory safety limit");
        var flat = Enumerable.Repeat(new byte[] { 80, 110, 155, 255 }, 403 * 29).SelectMany(p => p).ToArray();
        var flatImage = BitmapSource.Create(403, 29, 96, 96, PixelFormats.Bgra32, null, flat, 403 * 4); flatImage.Freeze();
        var flatResult = WaitUpscale(SuperResolution.RunAsync(new SuperResolutionRequest { Source = flatImage, Scale = 1.6 }, CancellationToken.None, null));
        byte[] fp = SubjectSegmentation.ReadPixels(flatResult.Pixels, flatResult.Pixels.PixelWidth, flatResult.Pixels.PixelHeight);
        foreach (int boundary in new[] { 192, 384 })
        {
            int x = (int)Math.Round(boundary * flatResult.Pixels.PixelWidth / 403.0), row = flatResult.Pixels.PixelHeight / 2;
            for (int c = 0; c < 3; c++)
                Assert(Math.Abs(fp[(row * flatResult.Pixels.PixelWidth + x - 1) * 4 + c] - fp[(row * flatResult.Pixels.PixelWidth + x) * 4 + c]) <= 2,
                    "halo and fractional filtering avoid tile-edge discontinuities");
        }
        string brokenModels = Path.Combine(folder, "BrokenModels"); Directory.CreateDirectory(brokenModels);
        foreach (string model in new[] { "humanseg.onnx", "u2netp.onnx" })
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", model), Path.Combine(brokenModels, model));
        File.WriteAllBytes(Path.Combine(brokenModels, "super-resolution-10.onnx"), new byte[] { 1, 2, 3 });
        bool badModel = false;
        try { WaitUpscale(SuperResolution.RunAsync(new SuperResolutionRequest { Source = source, ModelDirectory = brokenModels }, CancellationToken.None, null)); }
        catch (InvalidDataException) { badModel = true; }
        Assert(badModel, "bad AI model rejected instead of silently substituting ordinary resize");
        using (var cancel = new CancellationTokenSource())
        {
            bool canceled = false;
            var task = SuperResolution.RunAsync(request, cancel.Token, (p, message) => { if (p > 0.18) cancel.Cancel(); });
            try { WaitUpscale(task); } catch (OperationCanceledException) { canceled = true; }
            Assert(canceled, "mid-tile cancellation produces no completed result");
        }
        var manual = new ManualAdjustments(); for (int i = 0; i < manual.Values.Length; i++) manual.Values[i] = 8 + i;
        request.Manual = manual; request.Rotation = 1;
        var adjusted = WaitUpscale(SuperResolution.RunAsync(request, CancellationToken.None, null));
        BitmapSource expected = EnhancedImageRenderer.Render(cpu.Pixels, null, manual, 1, CancellationToken.None, null);
        byte[] adjustedPixels = SubjectSegmentation.ReadPixels(adjusted.Pixels, adjusted.Pixels.PixelWidth, adjusted.Pixels.PixelHeight);
        byte[] expectedPixels = SubjectSegmentation.ReadPixels(expected, expected.PixelWidth, expected.PixelHeight);
        Assert(adjusted.Pixels.PixelWidth == cpu.Pixels.PixelHeight && adjusted.Pixels.PixelHeight == cpu.Pixels.PixelWidth,
            "rotation applies after upscale at full output dimensions");
        Assert(adjustedPixels.Zip(expectedPixels, (a, b) => Math.Abs(a - b)).Average() < 1.5, "all ten Enhance values apply once after reconstruction");

        request.Rotation = 0; request.Manual = null;
        var window = new SuperResolutionWindow(request, path, FileRevision.Read(path)); window.Show();
        try
        {
            Wait(delegate { return window.PendingWork != null && window.PendingWork.IsCompleted; }, "upscale preview window"); Pump();
            Assert(Field<SuperResolutionResult>(window, "_result") != null && Field<Button>(window, "_saveButton").IsEnabled, "completed preview enables Save As");
            var compare = Field<ToggleButton>(window, "_compare"); compare.IsChecked = true; compare.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(Object.ReferenceEquals(Field<Image>(window, "_preview").Source, Field<SuperResolutionResult>(window, "_result").Comparison), "original comparison works");
            compare.IsChecked = false; compare.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(); Render(window, "upscale-dark.png");
            window.Width = 570; window.Height = 480; ThemeManager.SetDarkTheme(false); Pump();
            foreach (string field in new[] { "_scale", "_saveButton", "_cancelButton", "_dimensions", "_status", "_viewport" })
                AssertInside(Field<FrameworkElement>(window, field), (FrameworkElement)window.Content);
            Render(window, "upscale-light-small.png"); ThemeManager.SetDarkTheme(true);
            Field<ComboBox>(window, "_scale").SelectedItem = 1.25;
            Assert(!Field<Button>(window, "_saveButton").IsEnabled, "scale changes invalidate stale preview before save");
            var work = window.GenerateAsync(); Wait(delegate { return work.IsCompleted; }, "regenerate from source"); work.GetAwaiter().GetResult();
            bool refused = false; try { var save = window.SaveResultAsync(path, true); Wait(delegate { return save.IsCompleted; }, "source overwrite rejected"); save.GetAwaiter().GetResult(); }
            catch (IOException) { refused = true; }
            Assert(refused, "AI workflow cannot overwrite original");
            string destination = Path.Combine(folder, "result.png"); var saving = window.SaveResultAsync(destination, false);
            Wait(delegate { return saving.IsCompleted; }, "save upscaled copy"); saving.GetAwaiter().GetResult();
            Assert(window.SavedResult != null && File.Exists(destination), "upscale copy saved atomically");
            var saved = new BitmapImage(); saved.BeginInit(); saved.CacheOption = BitmapCacheOption.OnLoad; saved.UriSource = new Uri(destination); saved.EndInit();
            Assert(saved.PixelWidth == 256 && saved.PixelHeight == 91, "saved copy has exact requested dimensions");
            Assert(originalFile.SequenceEqual(File.ReadAllBytes(path)), "preview/save never alter original image");
            var closing = window.GenerateAsync(); window.Close();
            Wait(delegate { return closing.IsCompleted; }, "close cancels native work"); closing.GetAwaiter().GetResult();
            Assert(!window.IsVisible && !Field<bool>(window, "_busy"), "closing cancels and drains work before releasing the dialog");
        }
        finally { window.Close(); ThemeManager.SetDarkTheme(true); Pump(); }
        var state = new SessionState { SuperResolutionScale = 1.4 }; var store = new SessionStore(Path.Combine(folder, "Profile"));
        store.Save(state); Assert(store.Load().SuperResolutionScale == 1.4, "AI scale persists in session");
        CheckUpscaleViewerIntegration(folder, path);
        Console.WriteLine("PASS: AI fractional scales, real CPU/DirectML inference, alpha/chroma, regional/skin protection, cancellation, memory limits, adjustments/rotation, compare, save-copy and responsive dialog.");
    }

    private static void CheckUpscaleViewerIntegration(string folder, string path)
    {
        var services = AppServices.CreateInstance(Path.Combine(folder, "Viewer-profile"));
        services.Sessions.Save(new SessionState { LastFolder = folder, WindowWidth = 1350, WindowHeight = 850, ProcessingGpuKey = "CPU" });
        var main = new MainWindow(services); main.Show(); WaitScan(main);
        try
        {
            Assert(!Field<Button>(main, "_upscaleButton").IsEnabled, "upscale disabled in browser");
            Invoke(main, "OpenImageTab", path, true); Wait(delegate { return Ready(main, path); }, "source viewer"); WaitScan(main);
            Invoke(main, "ChangeManualAdjustment", 0, 23.0);
            Field<ToggleButton>(main, "_enhanceButton").IsChecked = true; Invoke(main, "ToggleQuickEnhance");
            Wait(delegate { return Field<Button>(main, "_upscaleButton").IsEnabled; }, "Quick Enhance completes before upscale");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            Exception failure = null; string resultPath = Path.Combine(folder, "viewer-upscaled.png");
            bool started = false;
            timer.Tick += async delegate
            {
                var dialog = Application.Current.Windows.OfType<SuperResolutionWindow>().FirstOrDefault();
                if (dialog == null || dialog.PendingWork == null || !dialog.PendingWork.IsCompleted || started) return;
                started = true; timer.Stop();
                try
                {
                    var request = Field<SuperResolutionRequest>(dialog, "_request");
                    Assert(request.Manual.Values[0] == 23 && request.QuickEffect != null, "viewer snapshots current manual and Quick Enhance");
                    await dialog.SaveResultAsync(resultPath, false); dialog.Close();
                }
                catch (Exception error) { failure = error; dialog.Close(); }
            };
            timer.Start(); Invoke(main, "ShowSuperResolution"); timer.Stop();
            if (failure != null) throw failure;
            Assert(started, "toolbar opens working upscale dialog");
            Wait(delegate { return Ready(main, resultPath); }, "upscale result opens in viewer"); WaitScan(main);
            var current = (ImageTabState)Invoke(main, "CurrentTabState");
            Assert(current.ManualAdjustments == null && current.SavedEnhanceRevision.HasValue && Field<Image>(main, "_mainImage").Effect == null,
                "saved upscale opens without reapplying baked adjustments");
            Assert(Capture(main).SuperResolutionScale == 1.6, "viewer captures selected scale");
            Render(main, "upscale-viewer-result.png");
            var state = Capture(main); state.SuperResolutionScale = 1.4;
            Invoke(main, "ApplySessionState", state, false); WaitScan(main);
            Assert(Field<double>(main, "_upscaleScale") == 1.4, "restore reapplies upscale factor");
            string backup = Path.Combine(folder, "upscale-backup.json"); WaitBackup((Task)Invoke(main, "ExportBackupFileAsync", backup));
            var document = ViewerBackupStore.Load(backup);
            Assert(document.Workspace.SuperResolutionScale == 1.4, "backup retains upscale factor");
            document.Workspace.SuperResolutionScale = 1.1;
            string invalid = Path.Combine(folder, "invalid-upscale.json"); SessionStore.WriteObject(invalid, document, typeof(ViewerBackupDocument));
            ExpectBadBackup(invalid, "invalid upscale factor rejected on import");
            Invoke(main, "BrowseActiveTab"); Assert(!Field<Button>(main, "_upscaleButton").IsEnabled, "upscale disables on return to browser");
        }
        finally { main.Close(); services.Dispose(); Pump(); }
    }
}
