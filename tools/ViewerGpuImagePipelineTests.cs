using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunGpuImageDeviceChecks()
    {
        const long mb = 1024 * 1024;
        Assert(GpuImageCache.EffectiveBudget(1024, 200 * mb, new GpuBudgetSample { Available = true, Budget = 2000 * mb, Usage = 400 * mb }) == 1024 * mb, "user budget bounds allocation");
        Assert(GpuImageCache.EffectiveBudget(4096, 200 * mb, new GpuBudgetSample { Available = true, Budget = 1000 * mb, Usage = 900 * mb }) == 200 * mb, "Windows pressure accounts for other allocations and headroom");
        Assert(GpuImageCache.EffectiveBudget(4096, 200 * mb, new GpuBudgetSample()) == 0, "unavailable budget blocks GPU allocation");
        string root = Path.Combine(Root, "gpu-image-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
        string png = Path.Combine(root, "test.png"), jpg = Path.Combine(root, "test.jpg"); MakeImage(png, 633, 419, 96);
        var wic = new WicImageDecoder(() => false);
        BitmapSource source = wic.Decode(png, 0, CancellationToken.None);
        var encoder = new JpegBitmapEncoder { QualityLevel = 96 }; encoder.Frames.Add(BitmapFrame.Create(source));
        using (var output = File.Create(jpg)) encoder.Save(output);
        var adapter = GpuHardware.SelectProcessing("Auto", GpuHardware.Devices); Assert(adapter != null, "GPU available for native test");
        GpuBudgetSample live = new GpuMemoryPressureMonitor().Read(adapter);
        Assert(live.Available && live.Budget > 0 && live.Usage >= 0, "live DXGI budget available");
        using (var device = new GpuImageDevice(adapter))
        using (var texture = device.Create(source.PixelWidth, source.PixelHeight))
        {
            device.Upload(texture, source, CancellationToken.None);
            Assert(ToolPixels(device.Readback(texture)).SequenceEqual(ToolPixels(source)), "shared GPU texture preserves uploaded pixels");
            var d3d = new D3DImage(); d3d.Lock(); d3d.SetBackBuffer(D3DResourceType.IDirect3DSurface9, texture.Surface, true);
            d3d.AddDirtyRect(new Int32Rect(0, 0, texture.Width, texture.Height)); d3d.Unlock();
            var picture = new Image { Source = d3d, Stretch = Stretch.Uniform };
            var window = new Window { Content = picture, Width = 720, Height = 510, Title = "Isolated GPU image test" };
            window.Show(); Pump(); Render(picture, "gpu-image-shared.png");
            Assert(d3d.IsFrontBufferAvailable && d3d.PixelWidth == texture.Width, "WPF receives native GPU surface");
            d3d.Lock(); d3d.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero); d3d.Unlock(); window.Close(); Pump();
            byte[] bytes = File.ReadAllBytes(jpg); int width, height; device.JpegSize(bytes, out width, out height);
            Assert(width == source.PixelWidth && height == source.PixelHeight, "nvJPEG header dimensions");
            device.DecodeJpeg(texture, bytes, CancellationToken.None);
            byte[] gpu = ToolPixels(device.Readback(texture)), cpu = ToolPixels(wic.Decode(jpg, 0, CancellationToken.None));
            double error = gpu.Zip(cpu, (a,b) => Math.Abs(a - b)).Average();
            Assert(error < 3, "GPU JPEG output close to WIC: " + error);
            Console.WriteLine("PASS: " + adapter.Name + " nvJPEG/CUDA-to-D3D11 decode, shared WPF surface, upload/readback parity; JPEG mean channel difference " + error.ToString("F3"));
        }
    }

    private static void RunGpuImageCacheChecks()
    {
        const long mb = 1024 * 1024;
        string root = Path.Combine(Root, "gpu-cache-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
        string[] paths = Enumerable.Range(0, 3).Select(i => Path.Combine(root, "Image" + i + ".png")).ToArray();
        foreach (string path in paths) MakeImage(path, 512, 512, 96);
        var decoder = new WicImageDecoder(() => false);
        var sources = paths.Select(p => decoder.Decode(p, 0, CancellationToken.None)).ToArray();
        long budget = 2048 * mb;
        var cache = new GpuImageCache(new GpuMemoryPressureMonitor(), a => new GpuBudgetSample { Available = budget > 0, Budget = Interlocked.Read(ref budget) });
        GpuImageLease active = null;
        try
        {
            for (int i = 0; i < paths.Length; i++)
                using (var lease = cache.Acquire(paths[i], sources[i], FileRevision.Read(paths[i]), CancellationToken.None))
                    Assert(lease != null && lease.Texture.Surface != IntPtr.Zero, "GPU cache admits texture " + i);
            Assert(cache.Statistics.Count == 3 && cache.Statistics.Uploads == 3, "three resident GPU images");
            active = cache.Acquire(paths[0], sources[0], FileRevision.Read(paths[0]), CancellationToken.None);
            Assert(cache.Statistics.Uploads == 3 && cache.Statistics.Hits == 1, "cache hit avoids reupload");
            Interlocked.Exchange(ref budget, 2 * mb); Invoke(cache, "Maintain");
            Assert(cache.Statistics.Count == 1 && cache.Statistics.Evictions == 2, "pressure evicts inactive textures first");
            Assert(active.Texture.Surface != IntPtr.Zero && !active.Texture.Retired, "displayed texture remains alive during trim");
            Interlocked.Exchange(ref budget, 0); Invoke(cache, "Maintain");
            Assert(cache.Statistics.ReleaseActive && cache.Statistics.Used > 0, "unavailable budget requests UI handoff before releasing active texture");
            var released = active.Texture; active.Dispose(); active = null;
            Wait(() => cache.Statistics.Used == 0, "eviction after active texture handoff");
            Assert(released.Surface == IntPtr.Zero, "native texture released after final lease");
            Interlocked.Exchange(ref budget, 2048 * mb);
            active = cache.Acquire(paths[0], sources[0], FileRevision.Read(paths[0]), CancellationToken.None);
            Assert(active != null, "cache recovers after pressure subsides");
            var old = active.Texture;
            MakeImage(paths[0], 520, 512, 96);
            BitmapSource changed = decoder.Decode(paths[0], 0, CancellationToken.None);
            using (var updated = cache.Acquire(paths[0], changed, FileRevision.Read(paths[0]), CancellationToken.None))
                Assert(updated != null && updated.Texture != old && old.Retired && old.Surface != IntPtr.Zero, "changed file cannot reuse stale pixels or release a displayed surface");
            active.Dispose(); active = null;
            using (var token = new CancellationTokenSource())
            {
                token.Cancel(); bool canceled = false;
                try { cache.Acquire(paths[1], sources[1], FileRevision.Read(paths[1]), token.Token); } catch (OperationCanceledException) { canceled = true; }
                Assert(canceled, "obsolete GPU upload is canceled");
            }
            cache.Configure(false, "Auto", 128, true);
            Wait(() => cache.Statistics.Used == 0, "disabled renderer releases cache");
            Assert(cache.Acquire(paths[1], sources[1], FileRevision.Read(paths[1]), CancellationToken.None) == null, "disabled renderer declines allocations");
            cache.Configure(true, "missing-adapter", 128, true);
            Assert(cache.Acquire(paths[1], sources[1], FileRevision.Read(paths[1]), CancellationToken.None) == null, "missing GPU uses fallback");
            cache.Configure(true, "Auto", 128, true);
            active = cache.Acquire(paths[1], sources[1], FileRevision.Read(paths[1]), CancellationToken.None);
            Assert(active != null, "renderer recovers after GPU reselection");
            var beforeClear = active.Texture;
            cache.Clear();
            using (var afterClear = cache.Acquire(paths[1], sources[1], FileRevision.Read(paths[1]), CancellationToken.None))
                Assert(afterClear != null && afterClear.Texture != beforeClear && beforeClear.Retired && beforeClear.Surface != IntPtr.Zero,
                    "clear invalidates residency immediately without destroying a pinned surface");
            active.Dispose(); active = null;
            cache.MarkDecoded(paths[1], sources[1], false);
            using (var original = cache.Acquire(paths[1], sources[1], FileRevision.Read(paths[1]), CancellationToken.None))
            {
                var equivalent = decoder.Decode(paths[1], 0, CancellationToken.None); cache.MarkDecoded(paths[1], equivalent, false);
                using (var duplicate = cache.Acquire(paths[1], equivalent, FileRevision.Read(paths[1]), CancellationToken.None))
                    Assert(duplicate != null && duplicate.Texture == original.Texture, "equivalent concurrent decodes share the resident texture");
                var edited = BitmapSource.Create(512, 512, 96, 96, PixelFormats.Pbgra32, null, new byte[512 * 512 * 4], 512 * 4); edited.Freeze();
                using (var result = cache.Acquire(paths[1], edited, FileRevision.Read(paths[1]), CancellationToken.None))
                    Assert(result != null && result.Texture != original.Texture, "same-size edited or AI pixels do not alias decoded originals");
            }
            active = cache.Acquire(paths[1], sources[1], FileRevision.Read(paths[1]), CancellationToken.None);
            var last = active.Texture; cache.Dispose();
            Assert(last.Surface != IntPtr.Zero, "disposing cache does not destroy a still-displayed surface");
            active.Dispose(); active = null; Wait(() => last.Surface == IntPtr.Zero, "last lease release after cache disposal");
        }
        finally { if (active != null) active.Dispose(); cache.Dispose(); }
        Console.WriteLine("PASS: GPU texture LRU, no-reupload hits, live-budget policy, inactive-first eviction, pinned-surface handoff, revision/reset/decoded/edited identity, cancellation, disable/missing-adapter recovery and disposal.");
    }

    private static void WaitGpuImage(MainWindow window, string path)
    {
        Wait(() => Ready(window, path) && Field<GpuImageLease>(window, "_gpuImageLease") != null, "GPU image " + Path.GetFileName(path));
        var stats = Field<AppServices>(window, "_services").GpuImages.Statistics;
        Console.WriteLine("GPU view " + Path.GetFileName(path) + ": textures=" + stats.Count + ", hits=" + stats.Hits + ", uploads=" + stats.Uploads
            + ", decodes=" + stats.JpegDecodes + ", evictions=" + stats.Evictions + ", used=" + stats.Used + ", budget=" + stats.Target + ", " + stats.Status);
    }

    private static void RunGpuImageViewerChecks()
    {
        string root = Path.Combine(Root, "gpu-viewer-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string a = Path.Combine(photos, "A.png"), b = Path.Combine(photos, "B.jpg");
        MakeImage(a, 900, 650, 96);
        var source = new WicImageDecoder(() => false).Decode(a, 0, CancellationToken.None);
        var encoder = new JpegBitmapEncoder { QualityLevel = 96 }; encoder.Frames.Add(BitmapFrame.Create(source));
        using (var output = File.Create(b)) encoder.Save(output);
        var services = AppServices.CreateInstance(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1400, WindowHeight = 820 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            Invoke(window, "OpenImageTab", a, true); WaitGpuImage(window, a);
            var main = Field<Image>(window, "_mainImage"); var gpu = Field<Image>(window, "_gpuImage");
            var canvas = Field<Canvas>(window, "_imageCanvas"); var enabled = Field<CheckBox>(window, "_imageGpuEnabledCheck");
            Assert(main.Opacity == 0 && gpu.Source is D3DImage && gpu.IsVisible, "resident surface replaces WPF bitmap presentation");
            byte[] gpuPixels = ManualPixels(canvas);
            enabled.IsChecked = false; Pump(); Assert(main.Opacity == 1 && gpu.Source == null, "fallback immediately restores CPU bitmap");
            byte[] cpuPixels = ManualPixels(canvas);
            Assert(gpuPixels.Zip(cpuPixels, (x,y) => Math.Abs(x-y)).Average() < 1, "GPU/WPF view matches at identical transform");
            enabled.IsChecked = true; WaitGpuImage(window, a);
            Invoke(window, "RotateImage", 1); Invoke(window, "SetZoomOneToOne"); Pump();
            Assert(Object.ReferenceEquals(main.RenderTransform, gpu.RenderTransform), "GPU follows zoom/pan/rotation transforms");
            SetEnhance(window, true); Wait(() => main.Effect != null, "GPU Quick Enhance"); Pump();
            Assert(Object.ReferenceEquals(main.Effect, gpu.Effect), "GPU surface uses same Quick Enhance shader");
            Invoke(window, "SetManualEnhanceVisible", true);
            Field<System.Collections.Generic.List<Slider>>(window, "_manualSliders")[1].Value = 20; Pump();
            byte[] enhancedGpu = ManualPixels(canvas); enabled.IsChecked = false; Pump();
            byte[] enhancedCpu = ManualPixels(canvas);
            Assert(enhancedGpu.Zip(enhancedCpu, (x,y) => Math.Abs(x-y)).Average() < 1.5, "manual and Quick Enhance GPU/WPF parity");
            enabled.IsChecked = true; WaitGpuImage(window, a);
            Invoke(window, "OpenRelativeImage", 1); WaitGpuImage(window, b);
            Assert(services.GpuImages.Statistics.JpegDecodes > 0, "viewer uses actual nvJPEG decode");
            Invoke(window, "OpenRelativeImage", -1); WaitGpuImage(window, a);
            Assert(services.GpuImages.Statistics.Hits > 0, "navigation uses resident cache");
            Invoke(window, "RefreshFolder");
            Assert(Field<bool>(window, "_folderScanPending"), "refresh overlaps navigation test");
            Invoke(window, "OpenRelativeImage", 1); WaitGpuImage(window, b);
            Invoke(window, "OpenRelativeImage", -1); WaitGpuImage(window, a);
            Invoke(window, "RefreshFolder"); Invoke(window, "OpenRelativeImage", 1); Invoke(window, "BrowseActiveTab"); WaitScan(window);
            Assert(Field<FrameworkElement>(window, "_browserView").IsVisible, "deferred navigation cannot reopen a viewer after returning to the browser");
            Invoke(window, "OpenBrowserImage", a); WaitGpuImage(window, a);
            Render((FrameworkElement)window.Content, "gpu-viewer-dark.png");
            ThemeManager.SetDarkTheme(false); window.Width = 980; Pump(); Render((FrameworkElement)window.Content, "gpu-viewer-light-narrow.png"); ThemeManager.SetDarkTheme(true);
            Invoke(window, "ShowConfigure"); Pump(); Invoke(window, "SelectConfigurationPage", 2); Pump();
            var configure = Field<Window>(window, "_configureWindow");
            Field<CheckBox>(window, "_imageGpuEnabledCheck").BringIntoView(); Pump();
            Render((FrameworkElement)configure.Content, "gpu-viewer-configure.png"); configure.Close();
            Field<Slider>(window, "_imageGpuBudget").Value = 512;
            SessionState saved = Capture(window);
            Assert(saved.GpuImagesEnabled == true && saved.GpuJpegEnabled == true && saved.ImageGpuBudgetMb == 512, "GPU preferences captured");
            string backup = Path.Combine(root, "backup.json"); ViewerBackupStore.Save(backup, (ViewerBackupDocument)Invoke(window, "CaptureBackupSnapshot"));
            Assert(ViewerBackupStore.Load(backup).Workspace.ImageGpuBudgetMb == 512, "VRAM settings survive backup");
            Field<Slider>(window, "_imageGpuBudget").Value = 256;
            Invoke(window, "RestoreImageGpuSettings", saved);
            Assert(services.GpuImages.LimitMb == 512 && services.GpuImages.Enabled && services.GpuImages.JpegEnabled, "saved GPU preferences restore controls and cache policy");
            Invoke(window, "BrowseActiveTab"); WaitScan(window);
            Assert(Field<GpuImageLease>(window, "_gpuImageLease") == null && main.Opacity == 1, "browser releases active GPU surface");
            Invoke(window, "OpenBrowserImage", a); WaitGpuImage(window, a);
        }
        finally { window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true); }
        var restoredServices = AppServices.CreateInstance(Path.Combine(root, "Profile"));
        var restoredWindow = new MainWindow(restoredServices); restoredWindow.Show();
        try
        {
            WaitGpuImage(restoredWindow, a);
            Assert(restoredServices.GpuImages.LimitMb == 512 && Field<Slider>(restoredWindow, "_imageGpuBudget").Value == 512,
                "VRAM settings and GPU image restore after restart");
        }
        finally { restoredWindow.Close(); Pump(); restoredServices.Dispose(); }
        Console.WriteLine("PASS: GPU viewer presentation, WPF fallback and pixel parity, enhancement/rotation, native JPEG/navigation cache, refresh-overlap navigation, configuration, backup and lifecycle.");
    }
}
