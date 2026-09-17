using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunGpuProcessingChecks()
    {
        var igpu = new GpuAdapterMemoryInfo { Key = "i", Id = "luid_0x00000000_0x00000001", Name = "AMD Radeon Graphics", Index = 0, IsIntegrated = true, CapacityBytes = 2L << 30, SharedCapacityBytes = 32L << 30 };
        var rtx = new GpuAdapterMemoryInfo { Key = "n", Id = "luid_0x00000000_0x00000002", Name = "NVIDIA GeForce RTX 5090", Index = 1, IsIntegrated = false, CapacityBytes = 32L << 30 };
        var devices = new[] { igpu, rtx };
        Assert(GpuHardware.SelectProcessing("Auto", devices) == rtx && GpuHardware.SelectProcessing("i", devices) == igpu, "processing selects actual discrete adapter even when GPU 0 is integrated");
        Assert(GpuHardware.SelectProcessing("missing", devices) == null && GpuHardware.SelectProcessing("CPU", devices) == null, "missing/manual CPU never silently pick another GPU");
        Assert(GpuHardware.SelectIntegrated(devices) == igpu && GpuHardware.SelectIntegrated(new[] { rtx }) == null, "thumbnail offload requires a confirmed UMA adapter");
        var sample = SystemGpuMemoryMonitor.Aggregate(devices, new Dictionary<string, long> { { igpu.Id + "_phys_0", 20L << 20 }, { rtx.Id + "_phys_0", 3L << 30 } },
            new Dictionary<string, long> { { igpu.Id + "_phys_0", 400L << 20 }, { rtx.Id + "_phys_0", 0 } });
        Assert(sample.Adapters.Length == 2 && sample.Adapters[0].SharedUsed == 400L << 20 && sample.Adapters[1].SharedUsed == 0, "per-adapter dedicated/shared counters remain separate");
        Assert(sample.AdapterSummary.Contains("iGPU") && sample.AdapterSummary.Contains("RTX 5090") && sample.AdapterSummary.Contains("shared"), "status names both GPUs and shared RAM");
        Assert(sample.Adapters[0].Summary.StartsWith("GPU 0 - iGPU") && sample.Adapters[1].Summary.StartsWith("GPU 1 - RTX 5090"), "readouts number the actual adapter index");
        var missing = SystemGpuMemoryMonitor.Aggregate(devices, new Dictionary<string, long>());
        Assert(missing.Adapters.All(a => !a.DedicatedUsed.HasValue && !a.SharedUsed.HasValue), "missing counters never become zero usage");
        var pixels = new byte[256 * 128 * 4];
        for (int y = 0; y < 128; y++) for (int x = 0; x < 256; x++)
        {
            int p = (y * 256 + x) * 4; pixels[p] = (byte)((x + y) % 200); pixels[p + 1] = (byte)(y * 2); pixels[p + 2] = (byte)x;
            pixels[p + 3] = (byte)((x + y) % 3 == 0 ? 0 : 160 + y % 80);
        }
        var source = AdvancedBitmap(256, 128, pixels);
        Console.WriteLine("GPU adapters: " + String.Join("; ", GpuHardware.Devices.Select(d => d.Name + " index=" + d.Index + " UMA=" + d.IsIntegrated)));
        foreach (var adapter in GpuHardware.Devices)
        {
            string reason;
            Assert(GpuHardware.CanAllocate(adapter, 1024 * 1024, 0, out reason), "DXGI memory admission: " + adapter.Name + " " + reason);
            using (var resizer = new GpuThumbnailResizer(adapter))
            {
                var resized = resizer.Resize(source, 128, 32, CancellationToken.None);
                Assert(resized.IsFrozen && resized.PixelWidth == 128 && resized.PixelHeight == 64, "real GPU output size/ownership: " + adapter.Name);
                byte[] output = BitmapBytes(resized);
                for (int y = 0; y < 64; y++) for (int x = 0; x < 128; x++)
                {
                    double alpha = 0; var channels = new double[3];
                    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                    {
                        int at = (((y * 2 + dy) * 256) + x * 2 + dx) * 4;
                        double a = pixels[at + 3]; alpha += a;
                        for (int c = 0; c < 3; c++) channels[c] += pixels[at + c] * a;
                    }
                    int index = (y * 128 + x) * 4;
                    for (int c = 0; c < 3; c++) Assert(Math.Abs(output[index + c] - channels[c] / alpha) < 2, "GPU area filter channel/alpha math");
                    Assert(Math.Abs(output[index + 3] - alpha / 4) < 2, "GPU alpha preserved");
                }
                using (var canceled = new CancellationTokenSource())
                {
                    canceled.Cancel(); bool stopped = false;
                    try { resizer.Resize(source, 128, 32, canceled.Token); } catch (OperationCanceledException) { stopped = true; }
                    Assert(stopped, "GPU job cancellation checked before allocation");
                }
                bool limited = false; try { resizer.Resize(source, 128, 0, CancellationToken.None); } catch (InvalidOperationException) { limited = true; }
                Assert(limited, "GPU working-set limit enforced before allocation");
                Console.WriteLine("PASS: real compute shader pixels/readback, transparency, cancellation and allocation limit on " + adapter.Name);
            }
        }

        string root = Path.Combine(Root, "gpu-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string photo = Path.Combine(photos, "Image.png"); SaveAdvancedBitmap(source, photo);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.ThumbnailGpu.Configure("Integrated GPU", 128);
        var thumbnail = services.Thumbnails.GetThumbnailAsync(ImageFileItem.FromPath(photo), 128, CancellationToken.None);
        WaitAdvancedTask(thumbnail);
        if (GpuHardware.SelectIntegrated(GpuHardware.Devices) != null)
        {
            Assert(services.ThumbnailGpu.GpuJobs > 0 && !services.ThumbnailGpu.Status.Contains("fallback"), "cache miss used real integrated GPU: " + services.ThumbnailGpu.Status);
            long jobs = services.ThumbnailGpu.GpuJobs;
            services.Thumbnails.GetThumbnailAsync(ImageFileItem.FromPath(photo), 128, CancellationToken.None).Wait();
            Assert(services.ThumbnailGpu.GpuJobs == jobs, "cache hit avoids GPU work");
            services.ThumbnailGpu.Configure("Auto", 128, true);
            for (int i = 0; i < 4; i++) services.ThumbnailGpu.Process(photo, 128, n => source, CancellationToken.None);
            Assert(services.ThumbnailGpu.Status.Contains("CPU"), "Auto chooses fast CPU baseline when transfer/dispatch loses: " + services.ThumbnailGpu.Status);
            services.ThumbnailGpu.Configure("Auto", 128, true);
            for (int i = 0; i < 4; i++) services.ThumbnailGpu.Process(photo, 128, n => { if (n == 128) Thread.Sleep(80); return source; }, CancellationToken.None);
            Assert(!services.ThumbnailGpu.Status.Contains("CPU faster"), "Auto selects GPU when measured end-to-end processing is faster");
        }
        var processing = GpuHardware.SelectProcessing("Auto", GpuHardware.Devices);
        if (processing != null)
        {
            using (var detector = new SubjectSegmentation(4, true, null, processing, 4096))
            {
                services.ThumbnailGpu.Configure("Integrated GPU", 128);
                long before = services.ThumbnailGpu.GpuJobs;
                var detect = Task.Run(delegate { return detector.Detect(source, CancellationToken.None); }); WaitAdvancedTask(detect);
                var concurrentAi = Task.Run(delegate { return detector.Detect(source, CancellationToken.None); });
                var concurrentThumbs = Task.Run(delegate { for (int i = 0; i < 8; i++) services.ThumbnailGpu.Process(photo, 128, n => source, CancellationToken.None); });
                WaitAdvancedTask(Task.WhenAll(concurrentAi, concurrentThumbs));
                Assert(detector.Backend.Contains(processing.Name), "DirectML runs on selected device, not assumed GPU 0: " + detector.Backend);
                if (GpuHardware.SelectIntegrated(GpuHardware.Devices) != null) Assert(services.ThumbnailGpu.GpuJobs >= before + 8, "iGPU thumbnails complete concurrently with selected-GPU AI");
                Console.WriteLine("PASS: AI execution on " + detector.Backend);
            }
            using (var detector = new SubjectSegmentation(4, true, null, processing, 1))
            {
                var detect = Task.Run(delegate { return detector.Detect(source, CancellationToken.None); }); WaitAdvancedTask(detect);
                Assert(detector.Backend.Contains("CPU fallback") && detector.Backend.Contains("limit"), "AI memory admission falls back to CPU");
            }
        }
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1450, WindowHeight = 900,
            ProcessingGpuKey = processing == null ? "CPU" : processing.Key, ProcessingGpuLimitMb = 4096, ThumbnailGpuMode = "Integrated GPU", ThumbnailGpuLimitMb = 64 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            var state = Capture(window);
            Assert(state.ThumbnailGpuMode == "Integrated GPU" && state.ThumbnailGpuLimitMb == 64 && state.ProcessingGpuLimitMb == 4096, "GPU configuration restores");
            Field<ToggleButton>(window, "_performanceToggle").IsChecked = true;
            Invoke(window, "UpdateGpuDisplay", sample); Pump();
            var panel = Field<WrapPanel>(window, "_gpuStatusPanel");
            Assert(panel.Children.Count == 2 && panel.Children.Cast<TextBlock>().All(t => t.IsVisible), "both GPUs visible in expanded performance details");
            CheckGpuStatusColors(panel, false);
            Render(window, "gpu-viewer-status.png");
            var unchanged = panel.Children[0];
            ThemeManager.SetDarkTheme(false); Pump();
            CheckGpuStatusColors(panel, false);
            Assert(ReferenceEquals(unchanged, panel.Children[0]), "theme updates GPU inline colors without rebuilding telemetry");
            Render(window, "gpu-status-light.png");
            ThemeManager.SetDarkTheme(true); Pump();
            double slotWidth = ((TextBlock)panel.Children[0]).ActualWidth;
            Point secondAt = ((TextBlock)panel.Children[1]).TranslatePoint(new Point(), panel);
            sample.Adapters[0].DedicatedUsed = 750L << 20; sample.Adapters[1].DedicatedUsed = 12L << 30;
            Invoke(window, "UpdateGpuDisplay", sample); Pump(); CheckGpuStatusColors(panel, false);
            Assert(((TextBlock)panel.Children[1]).Inlines.OfType<Run>().ElementAt(3).Text == ImageExtensions.FormatBytes(12L << 30)
                && ((TextBlock)panel.Children[0]).ActualWidth == slotWidth
                && ((TextBlock)panel.Children[1]).TranslatePoint(new Point(), panel) == secondAt, "updated values preserve numbered GPU positions");
            Invoke(window, "UpdateGpuDisplay", missing); Pump(); CheckGpuStatusColors(panel, true);
            Assert(panel.Children.Cast<TextBlock>().All(t => AutomationProperties.GetName(t).Contains("unavailable")), "missing values remain accessible and unavailable, not zero");
            Render(window, "gpu-status-unavailable.png");
            var telemetry = Task.Run(delegate { return SystemGpuMemoryMonitor.ReadSystem(); }); WaitAdvancedTask(telemetry);
            Assert(telemetry.Result.Adapters.Length == GpuHardware.Devices.Length, "live telemetry maps every discovered GPU");
            Console.WriteLine("Live GPU usage: " + telemetry.Result.AdapterSummary);
            Invoke(window, "UpdateGpuDisplay", telemetry.Result); Pump(); Render(window, "gpu-viewer-live.png");
            Invoke(window, "ShowConfigure"); Invoke(window, "SelectConfigurationPage", 2); Pump();
            var configure = Field<Window>(window, "_configureWindow");
            Field<ScrollViewer>(window, "_configurationScroll").ScrollToBottom(); Pump(); Render(configure, "gpu-configuration.png");
            configure.Close();
            window.Width = 850; Pump(); Invoke(window, "UpdateGpuDisplay", sample); Pump();
            foreach (TextBlock text in panel.Children) AssertInside(text, panel);
            Render(window, "gpu-status-narrow.png");
            ThemeManager.SetDarkTheme(false); Pump(); CheckGpuStatusColors(panel, false);
            foreach (TextBlock text in panel.Children) AssertInside(text, panel);
            Render(window, "gpu-status-narrow-light.png");
            ThemeManager.SetDarkTheme(true); Pump();
            string backup = Path.Combine(root, "gpu-backup.json"); WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
            Assert(ViewerBackupStore.Load(backup).Workspace.ThumbnailGpuMode == "Integrated GPU", "GPU preferences export");
            var bad = ViewerBackupStore.Load(backup); bad.Workspace.ThumbnailGpuMode = "unsupported";
            string invalid = Path.Combine(root, "invalid.json"); SessionStore.WriteObject(invalid, bad, typeof(ViewerBackupDocument)); ExpectBadBackup(invalid, "GPU mode validation");
            var restored = new SessionState { LastFolder = photos, ProcessingGpuKey = "missing-adapter", ThumbnailGpuMode = "CPU" };
            Invoke(window, "ApplySessionState", restored, false); WaitScan(window);
            using (var detector = (SubjectSegmentation)Invoke(window, "CreateSubjectDetector"))
                Assert(Field<GpuAdapterMemoryInfo>(detector, "_adapter") == null, "missing saved GPU degrades to CPU without resetting selection");
            Invoke(window, "RestoreGpuSettings", new SessionState());
            Assert(Capture(window).ProcessingGpuKey == "Auto" && Capture(window).ThumbnailGpuMode == "Auto" && Capture(window).ProcessingGpuLimitMb == 2048, "reset/legacy defaults restored");
        }
        finally { window.Close(); services.Dispose(); Pump(); }
        Console.WriteLine("PASS: explicit adapter selection, dual-GPU compute, CPU/Auto fallback, per-adapter memory telemetry, layout, limits and persistence.");
    }

    private static void CheckGpuStatusColors(WrapPanel panel, bool unavailable)
    {
        Color background = ((SolidColorBrush)Application.Current.Resources[ThemeKeys.ToolbarBackground]).Color;
        double back = BackgroundLuminance(background.R, background.G, background.B);
        foreach (TextBlock text in panel.Children)
        {
            Run[] runs = text.Inlines.OfType<Run>().ToArray();
            Assert(runs[0].Text.StartsWith("GPU ") && runs[0].FontWeight == FontWeights.SemiBold, "GPU number is prominent");
            Run dedicated = runs[3], shared = runs.Last();
            Color dedicatedColor = ((SolidColorBrush)dedicated.Foreground).Color, sharedColor = ((SolidColorBrush)shared.Foreground).Color;
            Color muted = ((SolidColorBrush)Application.Current.Resources[ThemeKeys.MutedText]).Color;
            if (unavailable)
                Assert(dedicated.Text == "unavailable" && shared.Text == "unavailable" && dedicatedColor == muted && sharedColor == muted
                    && dedicated.FontWeight == FontWeights.Normal, "unavailable readings are muted instead of highlighted as valid usage");
            else
                Assert(dedicatedColor != sharedColor && dedicatedColor != muted && sharedColor != muted
                    && dedicated.FontWeight == FontWeights.SemiBold && shared.FontWeight == FontWeights.SemiBold, "dedicated and shared usage have distinct emphasized colors");
            Assert(((SolidColorBrush)runs[4].Foreground).Color == muted, "capacity is subordinate to changing usage");
            foreach (Color color in new[] { dedicatedColor, sharedColor })
            {
                double luminance = BackgroundLuminance(color.R, color.G, color.B);
                Assert((Math.Max(back, luminance) + 0.05) / (Math.Min(back, luminance) + 0.05) >= 4.5, "GPU usage contrast in both themes");
            }
        }
    }
}
