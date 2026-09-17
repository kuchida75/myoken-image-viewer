using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static T WaitAdvancedUpscale<T>(Task<T> task)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(90);
        while (!task.IsCompleted && DateTime.UtcNow < limit) Pump();
        Assert(task.IsCompleted, "advanced model completed within test timeout"); return task.GetAwaiter().GetResult();
    }

    private static void CheckExportParity(UpscaleModel model)
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "dependency-downloads"));
        string inputFile = Path.Combine(root, model.Id + "-input.f32"), expectedFile = Path.Combine(root, model.Id + "-expected.f32");
        if (!File.Exists(inputFile) || !File.Exists(expectedFile)) return;
        byte[] inputBytes = File.ReadAllBytes(inputFile), expectedBytes = File.ReadAllBytes(expectedFile);
        var input = new float[inputBytes.Length / 4]; var expected = new float[expectedBytes.Length / 4];
        Buffer.BlockCopy(inputBytes, 0, input, 0, inputBytes.Length); Buffer.BlockCopy(expectedBytes, 0, expected, 0, expectedBytes.Length);
        using (var options = new SessionOptions { IntraOpNumThreads = 8 })
        using (var session = new InferenceSession(Path.Combine(UpscaleModels.DirectoryPath, model.File), options))
        using (var result = session.Run(new[] { NamedOnnxValue.CreateFromTensor("image", new DenseTensor<float>(input, new[] { 1, 3, 128, 128 })) }))
        {
            var actual = result.First().AsTensor<float>().ToArray();
            Assert(actual.Length == expected.Length && actual.Zip(expected, (a, b) => Math.Abs(a - b)).Max() < 0.002,
                model.Name + " ONNX export matches original PyTorch checkpoint");
        }
        Console.WriteLine("PASS: " + model.Name + " ONNX/PyTorch checkpoint parity.");
    }

    private static void RunUpscaleModelChecks()
    {
        Directory.CreateDirectory(Root);
        var source = UpscaleFixture(49, 37);
        var adapter = GpuHardware.Devices.FirstOrDefault(d => d.IsIntegrated != true);
        var results = new System.Collections.Generic.List<byte[]>();
        foreach (var model in UpscaleModels.All.Where(m => m.Id != UpscaleModels.Supir))
        {
            if (model.Id != UpscaleModels.Basic) CheckExportParity(model);
            var request = new SuperResolutionRequest { Source = source, ModelId = model.Id, Scale = 1.6, Workers = 8 };
            var cpu = WaitAdvancedUpscale(SuperResolution.RunAsync(request, CancellationToken.None, null));
            Assert(cpu.ModelId == model.Id && cpu.Backend == "CPU" && cpu.Tiles == 1, "selected model really ran on CPU: " + model.Name);
            Assert(cpu.Pixels.PixelWidth == 78 && cpu.Pixels.PixelHeight == 59, "fractional model output dimensions");
            byte[] pixels = ToolPixels(cpu.Pixels);
            results.Add(pixels);
            byte[] resized = SubjectSegmentation.ReadPixels(source, 78, 59);
            Assert(pixels.Where((v, i) => i % 4 == 3).SequenceEqual(resized.Where((v, i) => i % 4 == 3)), "model preserves transparency");
            if (adapter != null)
            {
                request.Adapter = adapter; request.SoftLimitMb = 16384;
                var gpu = WaitAdvancedUpscale(SuperResolution.RunAsync(request, CancellationToken.None, null));
                Assert(gpu.ModelId == model.Id && gpu.Backend.StartsWith("DirectML:"), "selected model runs on real GPU: " + model.Name);
                Assert(ToolPixels(gpu.Pixels).Zip(pixels, (a, b) => Math.Abs(a - b)).Average() < 1, "CPU/DirectML model parity");
                request.SoftLimitMb = 1;
                var fallback = WaitAdvancedUpscale(SuperResolution.RunAsync(request, CancellationToken.None, null));
                Assert(fallback.ModelId == model.Id && fallback.Backend.Contains("CPU fallback"), "pressure fallback keeps selected model, not Basic");
            }
            Console.WriteLine("PASS: " + model.Name + " real CPU/DirectML inference, alpha, fractional scale and memory fallback.");
            if (model.Id != UpscaleModels.Basic) CheckModelTiles(model.Id, adapter);
        }
        for (int i = 0; i < results.Count; i++)
            for (int j = i + 1; j < results.Count; j++)
                Assert(!results[i].SequenceEqual(results[j]), "bundled model choices have distinct reconstructed pixels");
        bool rejected = false;
        try { WaitAdvancedUpscale(SuperResolution.RunAsync(new SuperResolutionRequest { Source = source, ModelId = "missing" }, CancellationToken.None, null)); }
        catch (ArgumentException) { rejected = true; } Assert(rejected, "unknown model rejected");
        rejected = false;
        try { WaitAdvancedUpscale(SuperResolution.RunAsync(new SuperResolutionRequest { Source = source, ModelId = UpscaleModels.Supir }, CancellationToken.None, null)); }
        catch (InvalidOperationException error) { rejected = error.Message.Contains("not installed"); }
        Assert(rejected, "missing SUPIR fails clearly without pretending to run Basic");
        Assert(SupirProcess.Quote("C:\\models with spaces\\") == "\"C:\\models with spaces\\\\\"", "worker quoting preserves spaces and trailing slash");
        CheckUpscaleModelMenu(source);
        Console.WriteLine("PASS: model menu, active switching/cancel/stale results, S/slider/export routing, restore/backups/defaults and SUPIR setup guards.");
    }

    private static void CheckUpscaleModelMenu(BitmapSource source)
    {
        string folder = Path.Combine(Root, "model-menu-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "source.png");
        using (var file = File.Create(path)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); encoder.Save(file); }
        byte[] originalBytes = File.ReadAllBytes(path);
        var services = AppServices.CreateInstance(Path.Combine(folder, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = folder, WindowWidth = 1440, WindowHeight = 900 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            Assert(Capture(window).SuperResolutionModel == UpscaleModels.Basic, "old sessions default to Basic");
            var menu = (ContextMenu)Invoke(window, "BuildUpscaleModelMenu");
            Assert(menu.Items.OfType<MenuItem>().Count(i => i.Tag is string) == 5, "five model choices");
            Assert((string)menu.Items.OfType<MenuItem>().Single(i => (string)i.Tag == UpscaleModels.SwinIr).Header == "SwinIR-M Real-World \u00d74", "requested SwinIR variant is installed in menu");
            Assert(((string)menu.Items.OfType<MenuItem>().Single(i => (string)i.Tag == UpscaleModels.Supir).Header).Contains("setup required"), "SUPIR installation state is visible");
            menu.PlacementTarget = Field<Button>(window, "_upscaleModelButton"); menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; Pump();
            Render(menu, "upscale-model-menu-dark.png"); menu.IsOpen = false;
            ThemeManager.SetDarkTheme(false); menu = (ContextMenu)Invoke(window, "BuildUpscaleModelMenu");
            menu.PlacementTarget = Field<Button>(window, "_upscaleModelButton"); menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; Pump();
            Render(menu, "upscale-model-menu-light.png"); menu.IsOpen = false; ThemeManager.SetDarkTheme(true);
            Invoke(window, "ChooseUpscaleModel", UpscaleModels.RealEsrgan);
            Assert(Capture(window).SuperResolutionModel == UpscaleModels.RealEsrgan, "menu changes saved model in browser");
            Invoke(window, "OpenImageTab", path, true); Wait(delegate { return Ready(window, path); }, "model test source"); WaitScan(window);
            Field<System.Windows.Controls.Canvas>(window, "_imageCanvas").Focus();
            Invoke(window, "RunShortcut", System.Windows.Input.Key.S, System.Windows.Input.ModifierKeys.None, false);
            Task obsolete = Field<Task>(window, "_autoUpscaleTask");
            Invoke(window, "ChooseUpscaleModel", UpscaleModels.RealHat);
            Task active = Field<Task>(window, "_autoUpscaleTask");
            WaitModelTask(active); WaitModelTask(obsolete);
            Wait(delegate { return !Field<bool>(window, "_imageViewPending"); }, "model switch layout");
            Assert(Field<string>(window, "_transferStatus").StartsWith("HAT / Real-HAT") && Field<BitmapSource>(window, "_autoUpscaleOriginal").PixelWidth == 49,
                "changing model cancels old result and starts from native pixels");
            Invoke(window, "ChooseUpscaleModel", UpscaleModels.RealEsrgan); WaitModelTask(Field<Task>(window, "_autoUpscaleTask"));
            Wait(delegate { return !Field<bool>(window, "_imageViewPending"); }, "Real-ESRGAN preview layout");
            SetInlineUpscaleSize(window, 1.4);
            Assert(Field<string>(window, "_transferStatus").StartsWith("Real-ESRGAN") && Capture(window).SuperResolutionModel == UpscaleModels.RealEsrgan, "slider rebuild uses selected model");
            Invoke(window, "ChooseUpscaleModel", UpscaleModels.SwinIr); WaitModelTask(Field<Task>(window, "_autoUpscaleTask"));
            Wait(delegate { return !Field<bool>(window, "_imageViewPending"); }, "SwinIR preview layout");
            Assert(Field<string>(window, "_transferStatus").StartsWith("SwinIR-M Real-World") && Field<TextBlock>(window, "_upscaleSizeText").Text == "1.4x"
                && Field<BitmapSource>(window, "_autoUpscaleOriginal").PixelWidth == 49, "SwinIR switching preserves selected fractional scale and native source");
            CheckUpscaleAppearance(window, UpscaleStatus.Ready, UpscaleStatus.Ready);
            Assert((string)((ComboBoxItem)Field<ComboBox>(window, "_configureUpscaleModel").SelectedItem).Tag == UpscaleModels.SwinIr, "Configure model selection follows toolbar");
            menu = (ContextMenu)Invoke(window, "BuildUpscaleModelMenu");
            Assert(menu.Items.OfType<MenuItem>().Single(i => i.IsChecked).Tag.Equals(UpscaleModels.SwinIr), "SwinIR is the sole checked model");
            window.Width = 980; Pump(); Render(window, "upscale-model-viewer-narrow.png");
            AssertInside(Field<Button>(window, "_upscaleButton"), (FrameworkElement)window.Content);
            AssertInside(Field<Button>(window, "_upscaleModelButton"), (FrameworkElement)window.Content);
            var state = Capture(window); services.Sessions.Save(state);
            Assert(services.Sessions.Load().SuperResolutionModel == UpscaleModels.SwinIr, "SwinIR persists on disk");
            var document = (ViewerBackupDocument)Invoke(window, "CaptureBackupSnapshot");
            string backup = Path.Combine(folder, "backup.json"); ViewerBackupStore.Save(backup, document);
            Assert(ViewerBackupStore.Load(backup).Workspace.SuperResolutionModel == UpscaleModels.SwinIr, "SwinIR included in exported workspace");
            document.Workspace.SuperResolutionModel = "untrusted-unknown"; CheckRejectedModelBackup(document, backup);
            string saved = Path.Combine(folder, "upscaled.png");
            WaitAdvancedUpscale((Task<EnhancedSaveResult>)Invoke(window, "SaveEnhancedImageAsync", saved, false, false));
            Wait(delegate { return Ready(window, saved); }, "model result export");
            Assert(((BitmapSource)Field<Image>(window, "_mainImage").Source).PixelWidth == 69 && originalBytes.SequenceEqual(File.ReadAllBytes(path)), "export selected model/scale without original writes");
            Invoke(window, "ApplySessionState", state, false); WaitScan(window);
            Assert(Capture(window).SuperResolutionModel == UpscaleModels.SwinIr && Field<BitmapSource>(window, "_autoUpscaleOriginal") == null,
                "session restore keeps model but not transient AI pixels");
            Invoke(window, "ApplySessionState", new SessionState { LastFolder = folder }, false); WaitScan(window);
            Assert(Capture(window).SuperResolutionModel == UpscaleModels.Basic, "default settings restore Basic");
        }
        finally { window.Close(); services.Dispose(); ThemeManager.SetDarkTheme(true); Pump(); }
    }

    private static void WaitModelTask(Task task)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(90); while (!task.IsCompleted && DateTime.UtcNow < limit) Pump();
        Assert(task.IsCompleted, "viewer model job finished"); task.GetAwaiter().GetResult();
    }

    private static void CheckModelTiles(string id, GpuAdapterMemoryInfo adapter)
    {
        var flat = Enumerable.Repeat(new byte[] { 80, 110, 155, 255 }, 95 * 45).SelectMany(p => p).ToArray();
        var source = BitmapSource.Create(95, 45, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, flat, 95 * 4); source.Freeze();
        var request = new SuperResolutionRequest { Source = source, ModelId = id, Scale = 1.6, Adapter = adapter, SoftLimitMb = 16384 };
        var result = WaitAdvancedUpscale(SuperResolution.RunAsync(request, CancellationToken.None, null));
        Assert(result.Tiles == 2, "advanced model covers multiple overlapping tiles");
        byte[] pixels = ToolPixels(result.Pixels); int x = (int)Math.Round(64 * result.Pixels.PixelWidth / 95.0), y = result.Pixels.PixelHeight / 2;
        for (int c = 0; c < 3; c++) Assert(Math.Abs(pixels[(y * result.Pixels.PixelWidth + x - 1) * 4 + c] - pixels[(y * result.Pixels.PixelWidth + x) * 4 + c]) <= 2,
            "advanced model has no flat-field tile seam");
        using (var cancel = new CancellationTokenSource())
        {
            bool canceled = false;
            try { WaitAdvancedUpscale(SuperResolution.RunAsync(request, cancel.Token, (p, stage) => { if (stage.StartsWith("AI detail")) cancel.Cancel(); })); }
            catch (OperationCanceledException) { canceled = true; }
            Assert(canceled, "advanced inference cancels between tiles");
        }
        Console.WriteLine("PASS: " + id + " multi-tile continuity and cancellation.");
    }
    private static void CheckRejectedModelBackup(ViewerBackupDocument document, string path)
    {
        bool rejected = false; try { ViewerBackupStore.Save(path, document); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "backup rejects unknown model IDs");
    }
}
