using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ImageMagick;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunFacesChecks()
    {
        RunFaceAutoColumnsChecks();
        RunInteractiveFacesChecks();
        var rtx = new GpuAdapterMemoryInfo { Key = "10DE:0001", Name = "NVIDIA RTX 5090", Index = 1, CapacityBytes = 32L << 30 };
        var amd = new GpuAdapterMemoryInfo { Key = "1002:0001", Name = "AMD Radeon", Index = 0, IsIntegrated = true };
        Assert(FaceDetection.SelectRtx("Auto", new[] { amd, rtx }) == rtx, "Faces selects actual RTX, not the integrated GPU");
        Assert(FaceDetection.SelectRtx("CPU", new[] { amd, rtx }) == null && FaceDetection.SelectRtx(amd.Key, new[] { amd, rtx }) == null, "Faces never silently substitutes CPU or another adapter");
        using (var profile = new MemoryStream(Encoding.UTF8.GetBytes("[{\"args\":{\"provider\":\"DmlExecutionProvider\"}}]")))
            Assert(FaceDetection.VerifyExecutionProfile(profile).Contains("no CPU"), "Actual GPU kernel evidence is accepted");
        foreach (string json in new[] { "[]", "[{\"args\":{\"provider\":\"CPUExecutionProvider\"}}]", "[{\"args\":{\"provider\":\"DmlExecutionProvider\"}},{\"args\":{\"provider\":\"CPUExecutionProvider\"}}]" })
            FaceReject(delegate { using (var input = new MemoryStream(Encoding.UTF8.GetBytes(json))) FaceDetection.VerifyExecutionProfile(input); }, "CPU, empty and mixed-provider traces are rejected");
        var box = new FaceBox(.2, .3, .2, .25, .99);
        Assert(box.Valid && !new FaceBox(Double.NaN, 0, 1, 1, 1).Valid, "Face bounds reject nonfinite values");
        Assert(FaceDetection.Suppress(new[] { box, new FaceBox(.21, .3, .2, .25, .9) }).Length == 1, "Overlapping detections are suppressed");
        var turned = FaceDetection.Unrotate(new FaceBox(.3, .6, .25, .2, .99), 1);
        Assert(Math.Abs(turned.X - .6) < .0001 && Math.Abs(turned.Y - .45) < .0001, "Quarter-turn detections map back to original coordinates");
        var bounds = FaceImages.CropBounds(new FaceBox(0, 0, .15, .2, .99), 100, 200);
        Assert(bounds.X == 0 && bounds.Y == 0 && bounds.Width <= 100 && bounds.Height <= 200, "Padded crops stay inside originals");

        string root = Path.Combine(Root, "faces-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
        CheckFaceFolderCollection(root);
        string folder = Path.Combine(root, "Photos"); Directory.CreateDirectory(folder);
        var red = FaceSolid(80, 40, 0, 0, 220); var blue = FaceSolid(40, 80, 220, 0, 0);
        string redPath = Path.Combine(folder, "Face2.png"), bluePath = Path.Combine(folder, "Face11.png");
        SaveAdvancedBitmap(red.Bitmap(), redPath); SaveAdvancedBitmap(blue.Bitmap(), bluePath);
        var redSource = FaceImages.Inspect(redPath, CancellationToken.None); var blueSource = FaceImages.Inspect(bluePath, CancellationToken.None);
        var faces = new[] { FaceFixture(redSource, red), FaceFixture(blueSource, blue) };
        var options = new FaceCanvasOptions { Width = 256, Columns = 2, AutoHeight = true };
        options.Validate(2); Assert(options.Height == 128, "Reference auto-height includes every face");
        FaceReject(delegate { new FaceCanvasOptions { Width = 16000, Height = 16000, AutoHeight = false }.Validate(1); }, "Canvas pixel limit is enforced");
        FaceReject(delegate { new FaceCanvasOptions { Width = 128, Columns = 128 }.Validate(1); }, "Tiny cells are rejected");
        FaceReject(delegate { new FaceCanvasOptions { Width = 256, Height = 128, Columns = 1, AutoHeight = false }.Validate(2); }, "Manual height cannot drop faces");
        var preview = FaceImages.Preview(faces, options.Copy(), CancellationToken.None);
        Assert(preview.IsFrozen && preview.PixelWidth == 256 && preview.PixelHeight == 128, "Preview is immutable and has correct geometry");
        byte[] previewBytes = BitmapBytes(preview);
        Assert(previewBytes[(10 * 256 + 64) * 4] == 255 && previewBytes[(64 * 256 + 64) * 4 + 2] > 200, "Reference preserves aspect ratio with centered white margins");
        string output = Path.Combine(root, "reference.png"); FaceImages.Export(faces, options.Copy(), output, CancellationToken.None, null);
        Assert(File.Exists(output), "Full-resolution PNG exports"); byte[] original = File.ReadAllBytes(output);
        FaceReject(delegate { FaceImages.Export(faces, options.Copy(), output, CancellationToken.None, null); }, "Existing PNG cannot be overwritten");
        Assert(original.SequenceEqual(File.ReadAllBytes(output)), "Conflicting export leaves existing bytes intact");
        FaceReject(delegate { FaceImages.Export(faces, options.Copy(), redPath, CancellationToken.None, null); }, "Original cannot be overwritten");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel(); FaceReject(delegate { FaceImages.Export(faces, options.Copy(), Path.Combine(root, "canceled.png"), canceled.Token, null); }, "Canceled export does not publish");
        }
        Assert(!File.Exists(Path.Combine(root, "canceled.png")) && !Directory.EnumerateFiles(root, "*.tmp").Any(), "Cancellation and failures leave no temporary output");
        using (var canceled = new CancellationTokenSource())
        {
            string path = Path.Combine(root, "late-canceled.png");
            FaceReject(delegate { FaceImages.Export(faces, options.Copy(), path, canceled.Token, delegate { canceled.Cancel(); }); }, "Late cancellation prevents publication");
            Assert(!File.Exists(path), "Late-canceled export leaves no PNG");
        }
        string lateConflict = Path.Combine(root, "late-conflict.png");
        FaceReject(delegate { FaceImages.Export(faces, options.Copy(), lateConflict, CancellationToken.None, delegate { if (!File.Exists(lateConflict)) File.WriteAllText(lateConflict, "keep"); }); }, "Late destination conflict is never overwritten");
        Assert(File.ReadAllText(lateConflict) == "keep", "Late-conflict destination retains original bytes");
        SaveAdvancedBitmap(FaceSolid(80, 40, 0, 220, 0).Bitmap(), redPath);
        FaceReject(delegate { FaceImages.Export(faces, options.Copy(), Path.Combine(root, "stale.png"), CancellationToken.None, null); }, "Changed sources are rejected before publication");
        CheckFaceFullResolution(root);
        using (var cancellation = new CancellationTokenSource())
        {
            FaceImages.WorkGate.Wait();
            try
            {
                var waiting = Task.Run(() => FaceImages.Preview(faces, options.Copy(), cancellation.Token), cancellation.Token); cancellation.Cancel();
                try { waiting.Wait(); } catch (AggregateException) { }
                Assert(waiting.IsCanceled, "Queued canvas work cancels without waiting for the active producer");
            }
            finally { FaceImages.WorkGate.Release(); }
        }

        using (var stream = new FileStream(Path.Combine(root, "bad.onnx"), FileMode.Create)) stream.WriteByte(1);
        FaceReject(delegate { FaceDetection.VerifyModel(Path.Combine(root, "bad.onnx")); }, "Damaged face model is rejected");
        Assert(FaceDetection.VerifyModel(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "yunet-face.onnx")).Length == 229738, "Pinned face model checksum verified");
        var realScan = CheckFacesGpu(root, folder);
        CheckFacesShortcutScan(root);
        CheckFacesWindow(root, faces, realScan);
        Console.WriteLine("PASS: Faces geometry, limits, crop review, reference/interactive canvas pixels, PNG safety, model integrity and RTX inference.");
    }
    private static void CheckFacesShortcutScan(string root)
    {
        string parent = Path.Combine(root, "ShortcutScan"), folder = Path.Combine(parent, "Selected folder"); Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FaceReference", "astronaut.png"), Path.Combine(folder, "Portrait.png"));
        var services = AppServices.CreateInstance(Path.Combine(root, "ShortcutProfile"));
        services.Sessions.Save(new SessionState { LastFolder = parent, WindowWidth = 1200, WindowHeight = 800 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        bool finished = false; Exception failure = null; var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += delegate
        {
            var dialog = window.OwnedWindows.OfType<FacesWindow>().FirstOrDefault();
            if (dialog == null) return;
            if ((dialog.PendingWork == null || !dialog.PendingWork.IsCompleted) && elapsed.Elapsed.TotalSeconds < 30) return;
            timer.Stop();
            try
            {
                Assert(dialog.PendingWork != null && dialog.PendingWork.IsCompleted && Field<TextBox>(dialog, "_folder").Text == folder,
                    "F6 starts scanning the selected folder without another folder prompt. Path: " + Field<TextBox>(dialog, "_folder").Text
                    + "; task: " + (dialog.PendingWork == null ? "none" : dialog.PendingWork.Status.ToString()) + "; status: " + Field<TextBlock>(dialog, "_status").Text);
                Assert(Field<ListBox>(dialog, "_review").Items.Count > 0 && Field<TextBlock>(dialog, "_backend").Text.Contains("verified RTX inference"),
                    "Automatic F6 scan completes actual RTX detection: " + Field<TextBlock>(dialog, "_status").Text);
                finished = true;
            }
            catch (Exception error) { failure = error; }
            finally { dialog.Close(); }
        };
        try
        {
            var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid"); grid.SelectItem(grid.ItemAt(0)); grid.Focus();
            timer.Start(); Invoke(window, "RunShortcut", Key.F6, ModifierKeys.None, false);
            if (failure != null) throw failure;
            Assert(finished, "F6 automatic scan was observed and completed");
        }
        finally { timer.Stop(); window.Close(); Pump(); services.Dispose(); }
        Console.WriteLine("PASS: F6 selected-folder auto-scan completes on RTX without choosing a folder or pressing Scan faces.");
    }
    private static FaceScanResult CheckFacesGpu(string root, string folder)
    {
        var adapter = FaceDetection.SelectRtx("Auto", GpuHardware.Devices);
        if (adapter == null) throw new Exception("RTX hardware is required for this acceptance test.");
        FaceReject(delegate { using (var limited = new FaceDetection(4, "Auto", 1, CancellationToken.None)) { } }, "GPU memory limit prevents session allocation");
        string portrait = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FaceReference", "astronaut.png");
        Assert(File.Exists(portrait), "Public-domain astronaut fixture is present");
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(portrait); bitmap.EndInit(); bitmap.Freeze();
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); byte[] bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
        var pixels = new FacePixels(converted.PixelWidth, converted.PixelHeight, bytes);
        using (var detector = new FaceDetection(8, "Auto", 4096, CancellationToken.None))
        {
            Assert(detector.Backend.Contains("RTX") && detector.ExecutionEvidence.Contains("no CPU"), "Warmup verifies actual RTX execution");
            var detected = detector.Detect(pixels, CancellationToken.None);
            Assert(detected.Length > 0 && detected.All(b => b.Valid), "Real portrait produces valid face locations on RTX");
            Assert(detector.Detect(FaceSolid(512, 512, 255, 255, 255), CancellationToken.None).Length == 0, "Blank image has no false faces");
            Assert(detector.Detect(pixels.Rotate(1), CancellationToken.None).Length > 0, "Sideways portrait is detected using rotation fallback");
            var large = new byte[2048 * 1536 * 4];
            for (int y = 0; y < pixels.Height; y++) Buffer.BlockCopy(pixels.Data, y * pixels.Width * 4, large, ((y + 500) * 2048 + 1000) * 4, pixels.Width * 4);
            var tiled = detector.Detect(new FacePixels(2048, 1536, large), CancellationToken.None);
            Assert(tiled.Length > 0 && tiled.Any(b => b.X > .4), "Overlapping detection tiles retain global source coordinates");
            var input = new float[3 * 512 * 512];
            for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++) for (int c = 0; c < 3; c++) input[c * 512 * 512 + y * 512 + x] = (float)pixels.Sample(x, y, c);
            var gpu = (float[][])typeof(FaceDetection).GetMethod("Run", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(detector, new object[] { input, CancellationToken.None });
            using (var cpuOptions = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 })
            using (var cpu = new InferenceSession(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "yunet-face.onnx"), cpuOptions))
            using (var result = cpu.Run(new[] { NamedOnnxValue.CreateFromTensor(cpu.InputMetadata.Keys.Single(), new DenseTensor<float>(input, new[] { 1, 3, 512, 512 })) }))
            {
                float[][] reference = result.Select(r => r.AsTensor<float>().ToArray()).ToArray();
                for (int i = 0; i < gpu.Length; i++)
                    Assert(gpu[i].Length == reference[i].Length && gpu[i].Zip(reference[i], (a, b) => Math.Abs(a - b)).Max() < .05, "RTX tensor head agrees with test-only CPU reference " + i);
            }
            using (var canceled = new CancellationTokenSource())
            { canceled.Cancel(); FaceReject(delegate { detector.Detect(pixels, canceled.Token); }, "Inference cancellation is honored"); }
            Console.WriteLine("PASS: " + detector.Backend + "; " + detector.ExecutionEvidence);
        }
        File.Copy(portrait, Path.Combine(folder, "Portrait.png"));
        Directory.CreateDirectory(Path.Combine(folder, "Child")); File.Copy(portrait, Path.Combine(folder, "Child", "NotScanned.png"));
        Directory.CreateDirectory(Path.Combine(folder, "Child", "Grandchild")); File.Copy(portrait, Path.Combine(folder, "Child", "Grandchild", "Nested.png"));
        Directory.CreateDirectory(Path.Combine(folder, "Child", ".viewer-enhance-backups")); File.Copy(portrait, Path.Combine(folder, "Child", ".viewer-enhance-backups", "Backup.png"));
        var scan = FaceImages.Scan(folder, 30, "Auto", 4096, CancellationToken.None, null);
        Assert(scan.Images == 3 && scan.Faces.Count > 0 && scan.Faces.All(f => f.Source.Path.IndexOf("Child") < 0), "Scan is bounded to the chosen folder and retains immutable crop provenance");
        Console.WriteLine("PASS: folder scan " + scan.Faces.Count + " face(s), " + scan.Images + " images");
        var recursive = FaceImages.Scan(folder, 30, "Auto", 4096, CancellationToken.None, null, true);
        Assert(recursive.Images == 5 && recursive.Faces.Count == 3 && recursive.Faces.Any(f => f.Source.Path.Contains("Grandchild"))
            && recursive.Faces.All(f => !f.Source.Path.Contains("backups")), "Recursive RTX scan detects nested portraits without scanning backups");
        Console.WriteLine("PASS: recursive RTX folder scan " + recursive.Faces.Count + " face(s), " + recursive.Images + " images");
        return scan;
    }
    private static void CheckFaceFolderCollection(string root)
    {
        string folder = Path.Combine(root, "Collection"), child = Path.Combine(folder, "Child"), nested = Path.Combine(child, "Nested");
        Directory.CreateDirectory(nested);
        string direct = Path.Combine(folder, "Direct.png"), one = Path.Combine(child, "Image2.png"), two = Path.Combine(child, "Image11.png"), deep = Path.Combine(nested, "Deep.webp");
        foreach (string path in new[] { direct, one, two, deep }) File.WriteAllBytes(path, new byte[0]);
        File.WriteAllText(Path.Combine(child, "notes.txt"), "Not a photo");
        foreach (string name in new[] { ".viewer-rotation-backups", ".viewer-enhance-backups", ".viewer-orientation-backups", ".zen-batch-backups" })
        { string backup = Path.Combine(child, name); Directory.CreateDirectory(backup); File.WriteAllBytes(Path.Combine(backup, "Backup.png"), new byte[0]); }
        var plain = FaceImages.CollectPaths(folder, false, new FaceScanResult(), CancellationToken.None, null);
        Assert(plain.SequenceEqual(new[] { direct }), "Default face collection remains limited to the root folder");
        var expected = new[] { direct, one, two, deep }; Array.Sort(expected, BrowserSort.CompareNames);
        var recursive = FaceImages.CollectPaths(folder, true, new FaceScanResult(), CancellationToken.None, null);
        Assert(recursive.SequenceEqual(expected) && recursive.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4, "Recursive collection includes every nested supported image once in natural order and skips backup folders");
        string loop = Path.Combine(child, "Loop");
        string boundary = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        Assert(Path.GetFullPath(loop).StartsWith(boundary, StringComparison.OrdinalIgnoreCase) && Path.GetFullPath(folder).StartsWith(boundary, StringComparison.OrdinalIgnoreCase), "Junction test stays inside the generated fixture root");
        using (var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + loop + "\" \"" + folder + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
        { process.WaitForExit(); if (process.ExitCode != 0) throw new Exception("Fixture junction creation failed: " + process.StandardError.ReadToEnd()); }
        try
        {
            var linked = new FaceScanResult();
            Assert(FaceImages.CollectPaths(folder, true, linked, CancellationToken.None, null).SequenceEqual(expected) && linked.Skipped == 1, "Recursive collection skips junction cycles without duplicate images");
            FaceReject(delegate { FaceImages.CollectPaths(loop, true, new FaceScanResult(), CancellationToken.None, null); }, "Linked root is rejected too");
        }
        finally { Directory.Delete(loop); }
        using (var canceled = new CancellationTokenSource())
        { canceled.Cancel(); FaceReject(delegate { FaceImages.CollectPaths(folder, true, new FaceScanResult(), canceled.Token, null); }, "Recursive collection honors cancellation before traversal"); }

        string imageLimit = Path.Combine(root, "ImageLimit"); Directory.CreateDirectory(Path.Combine(imageLimit, "A")); Directory.CreateDirectory(Path.Combine(imageLimit, "B"));
        for (int i = 0; i <= FaceImages.MaxImages; i++) File.WriteAllBytes(Path.Combine(imageLimit, i % 2 == 0 ? "A" : "B", i + ".png"), new byte[0]);
        var limited = new FaceScanResult();
        Assert(FaceImages.CollectPaths(imageLimit, true, limited, CancellationToken.None, null).Count == FaceImages.MaxImages && limited.Limited, "Image cap applies to the entire recursive tree, not each folder");
        string entryLimit = Path.Combine(root, "EntryLimit"); Directory.CreateDirectory(Path.Combine(entryLimit, "A")); Directory.CreateDirectory(Path.Combine(entryLimit, "B"));
        for (int i = 0; i < FaceImages.MaxEntries; i++) File.WriteAllBytes(Path.Combine(entryLimit, i % 2 == 0 ? "A" : "B", i + ".txt"), new byte[0]);
        var entries = new FaceScanResult();
        Assert(FaceImages.CollectPaths(entryLimit, true, entries, CancellationToken.None, null).Count == 0 && entries.Limited, "Entry cap includes directories and unsupported files across the tree");
        using (var canceled = new CancellationTokenSource())
        {
            bool stopped = false;
            try { FaceImages.CollectPaths(entryLimit, true, new FaceScanResult(), canceled.Token, value => { if (value.StartsWith("Finding images:")) canceled.Cancel(); }); }
            catch (OperationCanceledException) { stopped = true; }
            Assert(stopped, "Cancellation interrupts enumeration while traversing nested folders");
        }
        Console.WriteLine("PASS: optional recursive face collection, natural order, backup/link exclusions, global limits and traversal cancellation");
    }
    private static void CheckFaceFullResolution(string root)
    {
        var pixels = FaceSolid(512, 512, 0, 0, 0);
        for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
            for (int c = 0; c < 3; c++) pixels.Data[(y * 512 + x) * 4 + c] = (byte)((x % 2) * 255);
        string sourcePath = Path.Combine(root, "detail.png"); SaveAdvancedBitmap(pixels.Bitmap(), sourcePath);
        var face = FaceFixture(FaceImages.Inspect(sourcePath, CancellationToken.None), pixels);
        var options = new FaceCanvasOptions { Width = 512, Columns = 1 };
        byte[] preview = BitmapBytes(FaceImages.Preview(new[] { face }, options.Copy(), CancellationToken.None));
        string output = Path.Combine(root, "detail-export.png"); FaceImages.Export(new[] { face }, options.Copy(), output, CancellationToken.None, null);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(output); bitmap.EndInit(); bitmap.Freeze();
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); byte[] saved = BitmapBytes(converted);
        int a = (256 * 512 + 256) * 4, b = a + 4;
        Assert(Math.Abs(saved[a] - saved[b]) > 200 && Math.Abs(preview[a] - preview[b]) < 5, "Export retains native crop detail absent from 128px review previews");
        string staleOutput = Path.Combine(root, "changed-during-export.png"); bool changed = false;
        FaceReject(delegate { FaceImages.Export(new[] { face }, options.Copy(), staleOutput, CancellationToken.None, delegate
        { if (!changed) { SaveAdvancedBitmap(FaceSolid(512, 512, 20, 40, 60).Bitmap(), sourcePath); changed = true; } }); }, "Source changed during PNG rendering is rejected before publication");
        Assert(!File.Exists(staleOutput), "Changed source never publishes stale PNG output");
        string transparentPath = Path.Combine(root, "transparent.png"); SaveAdvancedBitmap(AdvancedBitmap(128, 128, new byte[128 * 128 * 4]), transparentPath);
        var transparent = FaceFixture(FaceImages.Inspect(transparentPath, CancellationToken.None), FaceSolid(128, 128, 255, 255, 255));
        FaceImages.Export(new[] { transparent }, new FaceCanvasOptions { Width = 128, Columns = 1 }, Path.Combine(root, "transparent-canvas.png"), CancellationToken.None, null);
        var white = new BitmapImage(new Uri(Path.Combine(root, "transparent-canvas.png")));
        Assert(BitmapBytes(new FormatConvertedBitmap(white, PixelFormats.Bgra32, null, 0)).Take(4).All(v => v == 255), "Transparent source pixels flatten onto white consistently");
        string oriented = Path.Combine(root, "oriented.jpg");
        using (var image = new MagickImage(sourcePath))
        {
            image.Crop(new MagickGeometry(0, 0, 80, 40)); image.ResetPage(); image.Orientation = OrientationType.RightTop;
            var exif = new ExifProfile(); exif.SetValue(ExifTag.Orientation, (ushort)6); image.SetProfile(exif); image.Write(oriented, MagickFormat.Jpeg);
        }
        var orientation = FaceImages.Inspect(oriented, CancellationToken.None);
        Assert(orientation.Width == 40 && orientation.Height == 80, "Face source geometry honors EXIF orientation before detection and cropping");
    }
    private static void CheckFacesWindow(string root, FaceCrop[] faces, FaceScanResult realScan)
    {
        var result = new FaceScanResult { Backend = "Test preview - no inference" }; result.Faces.AddRange(faces);
        var window = new FacesWindow(root, 4, "Auto", 4096); window.Show(); window.SetReview(result); Pump();
        var includeSubfolders = Field<CheckBox>(window, "_includeSubfolders");
        Assert(includeSubfolders.IsChecked == false && includeSubfolders.IsEnabled, "Include subfolders defaults off");
        includeSubfolders.IsChecked = true;
        Assert(Field<ListBox>(window, "_review").Items.Count == 0, "Changing recursion scope clears the previous review");
        window.SetReview(result); includeSubfolders.IsChecked = false; window.SetReview(result);
        var update = Field<Button>(window, "_update"); update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Wait(delegate { return window.PendingWork != null && window.PendingWork.IsCompleted; }, "Faces preview completes");
        Pump();
        var image = Field<Image>(window, "_preview"); Assert(image.Source != null && image.ActualWidth > 100, "Faces preview is visible and fitted: " + Field<TextBlock>(window, "_status").Text + " / width=" + image.ActualWidth);
        var list = Field<ListBox>(window, "_review"); list.SelectAll(); Field<Button>(window, "_remove").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(list.Items.Count == 0 && image.Source == null && !update.IsEnabled, "Removing crops invalidates canvas without deleting photos");
        window.SetReview(result);
        foreach (bool dark in new[] { true, false })
        {
            ThemeManager.SetDarkTheme(dark); window.Width = dark ? 1180 : 780; window.Height = dark ? 800 : 540; Pump();
            update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(delegate { return window.PendingWork.IsCompleted; }, "Faces themed preview completes");
            Pump();
            Assert(Field<Button>(window, "_export").ActualWidth > 60 && Field<ScrollViewer>(window, "_viewport").ActualHeight > 100, "Faces controls fit dark/light and narrow layouts");
            var shot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); shot.Render(window);
            SaveAdvancedBitmap(shot, Path.Combine(root, dark ? "faces-dark.png" : "faces-light-narrow.png"));
        }
        ThemeManager.SetDarkTheme(true); window.Width = 1180; window.Height = 800;
        var actual = new FaceScanResult { Backend = realScan.Backend, ExecutionEvidence = realScan.ExecutionEvidence };
        actual.Faces.AddRange(Enumerable.Repeat(realScan.Faces[0], 7)); window.SetReview(actual);
        update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(delegate { return window.PendingWork.IsCompleted; }, "Real face reference preview");
        var actualShot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); actualShot.Render(window);
        SaveAdvancedBitmap(actualShot, Path.Combine(root, "faces-rtx-reference.png"));
        Field<TabControl>(window, "_tabs").SelectedIndex = 1;
        update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(delegate { return window.PendingWork.IsCompleted; }, "Real interactive face canvas");
        Assert(image.Source != null && Field<Int32Rect[]>(window, "_linkedCells").Length == 7, "Interactive canvas contains one link for each reviewed face");
        var mosaicShot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); mosaicShot.Render(window);
        SaveAdvancedBitmap(mosaicShot, Path.Combine(root, "faces-rtx-interactive.png"));
        Field<TabControl>(window, "_tabs").SelectedIndex = 0;
        Field<TextBox>(window, "_folder").Text = Path.GetDirectoryName(realScan.Faces[0].Source.Path); includeSubfolders.IsChecked = true;
        Field<Button>(window, "_scan").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(!includeSubfolders.IsEnabled, "Recursion checkbox is disabled throughout the scan");
        Wait(delegate { return window.PendingWork.IsCompleted; }, "Faces checkbox enables recursive RTX scanning");
        Assert(list.Items.Count == 3 && Field<TextBlock>(window, "_status").Text.Contains("5 images"), "Include subfolders is wired into the actual Faces scan command: " + Field<TextBlock>(window, "_status").Text);
        FaceImages.WorkGate.Wait();
        try
        {
            update.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Assert(!includeSubfolders.IsEnabled, "Scan scope cannot be changed during active work"); window.Close();
            Wait(delegate { return window.PendingWork.IsCompleted && !window.IsVisible; }, "Closing Faces cancels queued work and joins it before disposal");
        }
        finally { FaceImages.WorkGate.Release(); }
        ThemeManager.SetDarkTheme(true);
    }
    private static void RunFaceAutoColumnsChecks()
    {
        foreach (int count in new[] { 1, 2, 3, 5, 7, 17, 31, 1013, 1024 })
        foreach (bool autoHeight in new[] { true, false })
        {
            var options = new FaceCanvasOptions { Width = 1025, Height = 897, AutoColumns = true, AutoHeight = autoHeight };
            options.Validate(count);
            var cells = FaceImages.ReferenceLayout(options, count);
            Assert(cells.Length == count && cells.All(c => c.Width > 0 && c.Height > 0 && c.X >= 0 && c.Y >= 0
                && c.X + c.Width <= options.Width && c.Y + c.Height <= options.Height), "Automatic reference contains every face inside the canvas: " + count);
            Assert(cells.Sum(c => (long)c.Width * c.Height) == (long)options.Width * options.Height, "Automatic reference has no unused cells: " + count);
            for (int i = 0; i < cells.Length; i++) for (int j = i + 1; j < cells.Length; j++)
                Assert(!new Rect(cells[i].X, cells[i].Y, cells[i].Width, cells[i].Height).IntersectsWith(
                    new Rect(cells[j].X + .01, cells[j].Y + .01, cells[j].Width - .02, cells[j].Height - .02)), "Automatic cells do not overlap");
        }
        var pixels = FaceSolid(64, 64, 30, 90, 180);
        var face = FaceFixture(new FaceSource { Path = "Fixture.png", Width = 64, Height = 64 }, pixels);
        var result = new FaceScanResult(); result.Faces.AddRange(Enumerable.Repeat(face, 7));
        var auto = new FaceCanvasOptions { Width = 513, AutoColumns = true };
        auto.Validate(7); var preview = FaceImages.Preview(result.Faces, auto.Copy(), CancellationToken.None); var bytes = BitmapBytes(preview);
        foreach (var cell in FaceImages.ReferenceLayout(auto, 7))
        {
            int offset = ((cell.Y + cell.Height / 2) * preview.PixelWidth + cell.X + cell.Width / 2) * 4;
            Assert(bytes[offset + 2] == 180 && bytes[offset + 1] == 90, "Every automatic cell contains a reviewed face, including the last row");
        }
        SaveAdvancedBitmap(preview, Path.Combine(Root, "faces-auto-seven.png"));
        var window = new FacesWindow(Root, 4, "Auto", 4096); window.Show(); window.SetReview(result); Pump();
        var fields = Field<object>(window, "_reference"); var toggle = Field<CheckBox>(fields, "AutoColumns"); var columns = Field<TextBox>(fields, "Columns");
        Assert(toggle.IsChecked == true && columns.IsReadOnly && columns.Text == "3", "Auto columns defaults on and displays calculated columns");
        toggle.IsChecked = false; columns.Text = "5"; window.SetReview(result);
        Assert(!columns.IsReadOnly && columns.Text == "5" && !((FaceCanvasOptions)Invoke(window, "Options")).AutoColumns, "Manual columns remain available and are not overwritten by review changes");
        toggle.IsChecked = true;
        var review = Field<ListBox>(window, "_review");
        while (review.Items.Count > 4) { review.SelectedIndex = review.Items.Count - 1; Field<Button>(window, "_remove").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        Assert(columns.Text == "2" && ((FaceCanvasOptions)Invoke(window, "Options")).Columns == 2, "Removing crops recalculates automatic columns");
        window.Close(); Pump();
        Console.WriteLine("PASS: automatic reference columns, balanced odd/prime counts, full canvas coverage, no duplicate cells, rendered last row, default/manual modes and crop-removal recalculation.");
    }
    private static FacePixels FaceSolid(int w, int h, byte b, byte g, byte r)
    {
        var bytes = new byte[w * h * 4]; for (int i = 0; i < bytes.Length; i += 4) { bytes[i] = b; bytes[i + 1] = g; bytes[i + 2] = r; bytes[i + 3] = 255; }
        return new FacePixels(w, h, bytes);
    }
    private static FaceCrop FaceFixture(FaceSource source, FacePixels pixels)
    {
        return new FaceCrop { Source = source, Box = new FaceBox(0, 0, 1, 1, .99), Crop = new Int32Rect(0, 0, source.Width, source.Height),
            Pixels = pixels.Resize(128), Thumbnail = pixels.Bitmap() };
    }
    private static void FaceReject(Action action, string name)
    { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Assert(rejected, name); }
}
