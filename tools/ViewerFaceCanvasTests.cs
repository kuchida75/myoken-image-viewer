using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunFaceCanvasChecks()
    {
        string root = Path.Combine(Root, "face-documents-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
        string image = Path.Combine(root, "Original.png"); MakeImage(image, 640, 480, 96);
        var source = FaceImages.Inspect(image, CancellationToken.None); var seed = new FaceCrop { Source = source, Box = new FaceBox(.25, .2, .3, .4, .99) };
        var crops = FaceImages.Reframe(new[] { seed }, new FaceCropOptions { Surroundings = 75, Aspect = .8 }, 2, CancellationToken.None, null);
        var project = new FaceCanvasProject { Faces = crops, Folder = root, Options = new FaceCanvasOptions { Width = 512, AutoColumns = true, CellAspect = .8 }, Fit = false, Zoom = 1.5, X = 30, Y = 70, Recursive = true };
        string file = Path.Combine(root, "Portraits.myokenfaces"); FaceCanvasStore.Save(file, project, CancellationToken.None);
        var restored = FaceCanvasStore.Load(file);
        Assert(restored.Faces.Length == 1 && restored.Faces[0].Source.Path == image && restored.Faces[0].Source.Hash == source.Hash, "Saved maps retain exact original links and provenance");
        Assert(restored.Faces[0].Framing.Surroundings == 75 && restored.Faces[0].Framing.Aspect == .8 && restored.Options.CellAspect == .8
            && restored.Zoom == 1.5 && restored.X == 30 && restored.Y == 70 && restored.Recursive, "Saved maps retain framing/layout/viewport/scope");
        Assert(restored.Faces[0].Pixels.Data.SequenceEqual(crops[0].Pixels.Data), "Cached thumbnail pixels round-trip losslessly");
        using (var zip = ZipFile.OpenRead(file)) Assert(zip.Entries.Count == 2 && zip.Entries.All(e => e.FullName == "canvas.json" || e.FullName == "faces/0000.png"), "Canvas contains metadata and thumbnails, never original photographs");
        byte[] good = File.ReadAllBytes(file);
        using (var canceled = new CancellationTokenSource())
        { canceled.Cancel(); FaceReject(delegate { FaceCanvasStore.Save(file, project, canceled.Token); }, "Canceled map save is not published"); }
        Assert(File.ReadAllBytes(file).SequenceEqual(good) && !Directory.EnumerateFiles(root, "*.tmp").Any(), "Canceled save preserves the existing map and cleans temporary files");
        FaceReject(delegate { FaceCanvasStore.Save(image, project, CancellationToken.None); }, "Canvas cannot overwrite an original image");
        var malicious = DocumentArchive.Read(file, FaceCanvasStore.Limit, 1025); malicious.Add("../outside.txt", new byte[] { 1 });
        string bad = Path.Combine(root, "bad.myokenfaces"); DocumentArchive.Save(bad, malicious, FaceCanvasStore.Limit, CancellationToken.None);
        FaceReject(delegate { FaceCanvasStore.Load(bad); }, "Traversal entry rejected without extracting anything");
        malicious.Remove("../outside.txt"); malicious.Remove("faces/0000.png"); DocumentArchive.Save(bad, malicious, FaceCanvasStore.Limit, CancellationToken.None);
        FaceReject(delegate { FaceCanvasStore.Load(bad); }, "Missing thumbnail rejected");
        malicious = DocumentArchive.Read(file, FaceCanvasStore.Limit, 1025);
        var manifest = DocumentArchive.Json<FaceCanvasDocument>(malicious["canvas.json"], 8 * 1024 * 1024); manifest.Version = 999;
        malicious["canvas.json"] = DocumentArchive.Json(manifest, typeof(FaceCanvasDocument), 8 * 1024 * 1024); DocumentArchive.Save(bad, malicious, FaceCanvasStore.Limit, CancellationToken.None);
        FaceReject(delegate { FaceCanvasStore.Load(bad); }, "Unknown document version rejected");
        malicious = DocumentArchive.Read(file, FaceCanvasStore.Limit, 1025); var oversizedPng = malicious["faces/0000.png"].ToArray(); oversizedPng[16] = 127;
        malicious["faces/0000.png"] = oversizedPng; DocumentArchive.Save(bad, malicious, FaceCanvasStore.Limit, CancellationToken.None);
        FaceReject(delegate { FaceCanvasStore.Load(bad); }, "Oversized PNG dimensions are rejected before allocating decoded pixels");
        File.Delete(image); restored = FaceCanvasStore.Load(file);
        Assert(FaceImages.Preview(restored.Faces, restored.Options.Copy(), CancellationToken.None) != null, "A saved canvas opens from cached thumbnails even when originals are missing");
        MakeImage(image, 640, 480, 96);

        var state = new SessionState { LastFolder = root, FaceCanvasPaths = new List<string> { file }, LastFaceCanvas = file, IncludeFaceCanvasesInBackup = true };
        var named = new NamedSessionDocument { Name = "Faces workspace", State = state };
        var backup = ViewerBackupStore.Create(state, new BrowserPreferences(), new[] { named });
        string bundle = Path.Combine(root, "viewer.zip"); ViewerBackupStore.Save(bundle, backup); var loaded = ViewerBackupStore.Load(bundle);
        Assert(loaded.CanvasFiles.Count == 1 && loaded.SavedSessions.Count == 1, "ZIP backup includes distinct saved canvases from current and named sessions");
        string importProfile = Path.Combine(root, "ImportedProfile"); Directory.CreateDirectory(importProfile);
        ViewerBackupStore.RestoreCanvases(loaded, importProfile);
        Assert(loaded.Workspace.LastFaceCanvas.StartsWith(importProfile + Path.DirectorySeparatorChar) && File.Exists(loaded.Workspace.LastFaceCanvas)
            && loaded.SavedSessions[0].State.LastFaceCanvas == loaded.Workspace.LastFaceCanvas, "Backup restore writes managed map documents and relinks all sessions without touching original paths");
        ViewerBackupStore.RestoreCanvases(loaded, importProfile);
        Assert(Directory.EnumerateFiles(Path.Combine(importProfile, "face-canvases")).Count() == 1, "Repeated canvas restore is idempotent");
        string legacy = Path.Combine(root, "viewer.json"); ViewerBackupStore.Save(legacy, backup);
        Assert(ViewerBackupStore.Load(legacy).Workspace.LastFaceCanvas == file, "Existing JSON backups remain compatible and retain document links");
        state.IncludeFaceCanvasesInBackup = false; ViewerBackupStore.Save(bundle, backup);
        Assert(ViewerBackupStore.Load(bundle).CanvasFiles.Count == 0, "Backup canvas inclusion can be disabled");

        var window = new FacesWindow(root, 4, "Auto", 4096); window.Show(); window.OpenCanvasFile(file);
        Wait(delegate { return window.PendingWork != null && window.PendingWork.IsCompleted; }, "Saved canvas opens in Faces"); Pump();
        Assert(window.CanvasPath == file && Field<TabControl>(window, "_tabs").SelectedIndex == 1 && Field<Image>(window, "_preview").Source != null
            && Field<Slider>(window, "_surroundings").Value == 75, "Open canvas restores the actual interactive view and framing controls");
        string copy = Path.Combine(root, "Saved-from-view.myokenfaces"); window.SaveCanvasFile(copy); Wait(delegate { return window.PendingWork.IsCompleted; }, "Save canvas from UI");
        Assert(File.Exists(copy) && FaceCanvasStore.Load(copy).Faces[0].Source.Path == image, "Save canvas button pipeline writes an interactive project");
        Render(window, "saved-face-canvas-dark.png"); window.Close(); Pump();
        CheckCanvasSessionAutoRestore(root, file);
        Console.WriteLine("PASS: face canvas ZIP/JSON/thumbnail round-trip, missing-source opening, cancellation/atomic overwrite, invalid/traversal documents, UI Save/Open, optional backup inclusion, startup without Faces popup, retained recent links and named-session canvas reopening.");
    }
    private static void CheckCanvasSessionAutoRestore(string root, string file)
    {
        var services = AppServices.CreateInstance(Path.Combine(root, "SessionProfile"));
        services.Sessions.Save(new SessionState { LastFolder = root, FaceCanvasPaths = new List<string> { file }, LastFaceCanvas = file, WindowWidth = 1200, WindowHeight = 800 });
        var window = new MainWindow(services); bool seen = false; Exception failure = null; var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += delegate
        {
            var faces = window.OwnedWindows.OfType<FacesWindow>().FirstOrDefault();
            if (faces == null || faces.PendingWork == null || !faces.PendingWork.IsCompleted) return;
            timer.Stop();
            try { Assert(faces.CanvasPath == file && Field<Image>(faces, "_preview").Source != null, "Session automatically reopens its last saved canvas without RTX inference"); seen = true; }
            catch (Exception error) { failure = error; }
            finally { faces.Close(); }
        };
        try
        {
            timer.Start(); window.Show(); WaitScan(window); PumpZoomFor(600); timer.Stop();
            if (failure != null) throw failure;
            Assert(!seen && !window.OwnedWindows.OfType<FacesWindow>().Any(), "Startup restores the workspace without opening Faces for an old saved canvas");
            Assert(Capture(window).LastFaceCanvas == file && Capture(window).FaceCanvasPaths.Contains(file), "Canvas paths persist in the automatic session snapshot");
            var recent = (MenuItem)Invoke(window, "SavedFaceCanvasesMenu"); Assert(recent.Items.OfType<MenuItem>().Single().IsEnabled, "Saved canvas remains available through Sessions");
            seen = false; timer.Start(); recent.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); timer.Stop();
            if (failure != null) throw failure; Assert(seen, "Recent canvas opens only when explicitly selected");
            services.Sessions.SaveNamed("Faces named", new SessionState { LastFolder = root, FaceCanvasPaths = new List<string> { file }, LastFaceCanvas = file });
            seen = false; elapsed.Restart(); timer.Start(); Invoke(window, "LoadNamedSession", "Faces named");
            Wait(delegate { return seen || failure != null || elapsed.Elapsed.TotalSeconds > 20; }, "Named session reopens saved canvas");
            if (failure != null) throw failure; Assert(seen && Capture(window).LastFaceCanvas == file, "Named session includes and reopens its saved canvas");
        }
        finally { timer.Stop(); window.Close(); Pump(); services.Dispose(); }
    }
}
