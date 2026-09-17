using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ImageMagick;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunBatchChecks()
    {
        string root = Path.Combine(Root, "batch-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(root);
        BatchRenameChecks(root); BatchExportChecks(root); BatchThumbnailChecks(root); BatchUiChecks(root);
        Console.WriteLine("PASS: batch rename/resize/convert preview, execution, collisions, rollback, copies/backups, cancellation, metadata/formats, presets, tab remapping and dark/light layouts.");
    }
    private static List<BatchItem> BatchPreview(string[] paths, BatchOptions options)
    { return BatchPlan.Create(paths, options, CancellationToken.None); }
    private static BatchResult BatchRun(List<BatchItem> plan, BatchOptions options, string root)
    { return BatchExecutor.Execute(plan, options, Path.Combine(root, "Profile"), CancellationToken.None, null); }
    private static void BatchRenameChecks(string root)
    {
        string folder = Path.Combine(root, "Rename"); Directory.CreateDirectory(folder);
        string a = Path.Combine(folder, "01.jpg"), b = Path.Combine(folder, "02.jpg"); File.WriteAllText(a, "first"); File.WriteAllText(b, "second");
        var options = BatchOptions.Default(BatchKind.Rename); options.Template = "##";
        var plan = BatchPreview(new[] { b, a }, options);
        Assert(plan.All(row => row.Ready) && plan[0].NewName == "01.jpg", "rename sequence follows selection order and permits swaps");
        var result = BatchRun(plan, options, root);
        Assert(result.Errors.Count == 0 && result.Completed.Count == 2 && File.ReadAllText(a) == "second" && File.ReadAllText(b) == "first", "staged swap keeps both originals");
        Assert(File.Exists(result.Journal) && !Directory.GetFiles(folder, ".zen-rename-*").Any(), "durable recovery log and no leftover staging files");
        using (var cancel = new CancellationTokenSource())
        {
            plan = BatchPreview(new[] { b, a }, options);
            result = BatchExecutor.Execute(plan, options, Path.Combine(root, "Profile"), cancel.Token, (item, status) => { if (status == "Preparing rename") cancel.Cancel(); });
            Assert(result.Canceled && result.Completed.Count == 0 && File.ReadAllText(a) == "second" && File.ReadAllText(b) == "first", "cancel during staging rolls back every source");
        }
        string outsider = Path.Combine(folder, "occupied.jpg");
        options.Template = "occupied"; plan = BatchPreview(new[] { a }, options);
        result = BatchExecutor.Execute(plan, options, Path.Combine(root, "Profile"), CancellationToken.None, (item, status) => { if (status == "Preparing rename") File.WriteAllText(outsider, "late external file"); });
        Assert(result.Errors.Count == 1 && result.Completed.Count == 0 && File.ReadAllText(outsider) == "late external file" && File.ReadAllText(a) == "second", "late collision is never overwritten; staged source restored");
        plan = BatchPreview(new[] { a }, options); Assert(!plan[0].Ready && plan[0].Status.StartsWith("Error:"), "existing conflicts block Stop policy");
        options.Conflict = BatchConflict.KeepBoth; plan = BatchPreview(new[] { a }, options); Assert(plan[0].NewName == "occupied (2).jpg", "unique names are included in preview");
        options.Conflict = BatchConflict.Skip; Assert(!BatchPreview(new[] { a }, options)[0].Ready, "skip conflicts");
        options.Template = "CON"; Assert(BatchPreview(new[] { a }, options)[0].Status.StartsWith("Error:"), "reserved Windows names blocked");
        options.Template = "../escape"; Assert(!BatchPreview(new[] { a }, options)[0].Ready, "path escape blocked");
        options.Template = "##"; options.Conflict = BatchConflict.Stop; plan = BatchPreview(new[] { b, a }, options); plan[1].Included = false;
        result = BatchRun(plan, options, root); Assert(result.Completed.Count == 0 && File.ReadAllText(a) == "second" && File.ReadAllText(b) == "first", "excluded dependency cannot be clobbered");
        options.Template = "{folder}_*_##"; options.Start = 26; options.Letters = true;
        Assert(BatchPlan.Rename(a, false, 1, options) == "Rename_01_AA.jpg", "folder/original/letter sequence tokens");
        options.UseTemplate = false; options.Find = "01"; options.Replace = "First Name"; options.RemoveText = "Name"; options.RemoveStart = 1;
        options.Prefix = "pre_"; options.Suffix = "_end"; options.Case = BatchCase.Uppercase; options.StripSpaces = true;
        Assert(BatchPlan.Rename(a, false, 0, options) == "PRE_IRST_END.jpg", "text rules preserve the real extension");
        options = BatchOptions.Default(BatchKind.Rename); options.UseTemplate = false; options.Case = BatchCase.Lowercase;
        string mixed = Path.Combine(folder, "MIXED.jpg"); File.WriteAllText(mixed, "case");
        result = BatchRun(BatchPreview(new[] { mixed }, options), options, root);
        Assert(result.Completed.Count == 1 && Directory.GetFiles(folder).Any(path => Path.GetFileName(path) == "mixed.jpg"), "case-only rename via staging");
        string sub = Path.Combine(folder, "Album"), child = Path.Combine(sub, "Child.jpg"); Directory.CreateDirectory(sub); File.WriteAllText(child, "inside");
        options = BatchOptions.Default(BatchKind.Rename); options.Template = "Trip_##";
        plan = BatchPreview(new[] { sub, child }, options); Assert(plan.Count == 1 && plan[0].IsDirectory, "parent-folder selection excludes nested items");
        result = BatchRun(plan, options, root); Assert(File.ReadAllText(Path.Combine(folder, "Trip_01", "Child.jpg")) == "inside", "batch folder rename preserves contents");
        Console.WriteLine("PASS: templates/text/metadata tokens, sequence, case-only/cyclic/folder rename, conflict policies, cancellation rollback and late collisions.");
    }
    private static void BatchExportChecks(string root)
    {
        string folder = Path.Combine(root, "Export"); Directory.CreateDirectory(folder);
        string input = Path.Combine(folder, "Portrait.png"); MakeImage(input, 600, 400, 96); byte[] original = File.ReadAllBytes(input);
        var options = BatchOptions.Default(BatchKind.Resize); options.Width = 200; options.Height = 200;
        int w, h; BatchImages.Size(600, 400, options, out w, out h); Assert(w == 200 && h == 133, "fit bounding box");
        options.Fit = BatchFit.Height; BatchImages.Size(600, 400, options, out w, out h); Assert(w == 300 && h == 200, "fit height");
        options.ResizeMode = BatchResizeMode.Percentage; options.Percent = 25; BatchImages.Size(600, 400, options, out w, out h); Assert(w == 150 && h == 100, "percentage");
        options.ResizeMode = BatchResizeMode.LongEdge; options.Width = 300; BatchImages.Size(600, 400, options, out w, out h); Assert(w == 300 && h == 200, "long edge");
        options.ResizeMode = BatchResizeMode.ShortEdge; BatchImages.Size(600, 400, options, out w, out h); Assert(w == 450 && h == 300, "short edge");
        options.ResizeMode = BatchResizeMode.PrintSize; options.PrintWidth = 2.54; options.PrintHeight = 2.54; options.PrintCentimeters = true; options.Dpi = 200; options.Fit = BatchFit.WidthAndHeight;
        BatchImages.Size(600, 400, options, out w, out h); Assert(w == 200 && h == 133, "print centimeters and DPI");
        options.ResizeMode = BatchResizeMode.Pixels; options.Width = 900; options.Height = 900; BatchImages.Size(600, 400, options, out w, out h); Assert(w == 600 && h == 400, "reduce only keeps small images");
        options.Direction = BatchDirection.EnlargeOnly; options.Width = 200; options.Height = 200; BatchImages.Size(600, 400, options, out w, out h); Assert(w == 600 && h == 400, "enlarge only keeps large images");
        options.Direction = BatchDirection.Both; options.PreserveAspect = false; BatchImages.Size(600, 400, options, out w, out h); Assert(w == 200 && h == 200, "explicit aspect ratio override");
        options.PreserveAspect = true; options.Subfolder = "Small";
        var plan = BatchPreview(new[] { input }, options); var result = BatchRun(plan, options, root);
        Assert(result.Errors.Count == 0 && result.Completed.Count == 1, "resize output written");
        using (var image = new MagickImage(plan[0].Destination)) Assert(image.Width == 200 && image.Height == 133, "encoded resize dimensions verified");
        Assert(original.SequenceEqual(File.ReadAllBytes(input)) && File.GetLastWriteTimeUtc(input) == File.GetLastWriteTimeUtc(plan[0].Destination), "original unchanged and modified date preserved");
        foreach (string extension in BatchImages.Extensions)
        {
            options = BatchOptions.Default(BatchKind.Convert); options.Format = extension; options.Subfolder = "Converted"; options.Lossless = true;
            plan = BatchPreview(new[] { input }, options); Assert(plan[0].Ready, "encoder ready " + extension + ": " + plan[0].Status);
            result = BatchRun(plan, options, root); Assert(result.Errors.Count == 0 && result.Completed.Count == 1, "format written " + extension + ": " + String.Join(";", result.Errors));
            var decoded = new DecoderRegistry(delegate { return false; }).Decode(plan[0].Destination, 0, CancellationToken.None);
            Assert(decoded.PixelWidth == 600 && decoded.PixelHeight == 400, "viewer decodes batch output " + extension);
        }
        string metadata = Path.Combine(folder, "Metadata.jpg");
        using (var image = CodecFixture(MagickColors.Crimson, 80, 60))
        {
            var exif = new ExifProfile(); exif.SetValue(ExifTag.Make, "Batch fixture"); exif.SetValue(ExifTag.Orientation, (ushort)6); exif.SetValue(ExifTag.DateTimeOriginal, "2020:02:03 04:05:06"); image.SetProfile(exif); image.SetProfile(ColorProfiles.SRGB); image.Orientation = OrientationType.RightTop; image.Write(metadata, MagickFormat.Jpeg);
        }
        Assert(BatchImages.Token(metadata, "taken", "yyyyMMdd") == "20200203" && BatchImages.Token(metadata, "width", "") == "60", "date-taken and oriented dimension tokens");
        options = BatchOptions.Default(BatchKind.Convert); options.Format = ".png";
        plan = BatchPreview(new[] { metadata }, options); result = BatchRun(plan, options, root);
        using (var image = new MagickImage(plan[0].Destination)) Assert(image.Width == 60 && image.Height == 80 && image.GetExifProfile().GetValue(ExifTag.Orientation).Value == 1 && image.GetExifProfile().GetValue(ExifTag.Make).Value == "Batch fixture", "orientation normalized and EXIF retained");
        options.PreserveMetadata = false; options.OutputSuffix = "_private";
        plan = BatchPreview(new[] { metadata }, options); result = BatchRun(plan, options, root);
        using (var image = new MagickImage(plan[0].Destination)) Assert(image.GetExifProfile() == null && (image.GetColorProfile() != null || image.ColorSpace == ColorSpace.sRGB), "strip personal metadata but retain color interpretation (PNG may encode standard sRGB as a chunk)");
        string gif = Path.Combine(folder, "Animated.gif");
        using (var frames = new MagickImageCollection()) { frames.Add(CodecFixture(MagickColors.Red, 40, 30)); frames.Add(CodecFixture(MagickColors.Blue, 40, 30)); frames.Write(gif, MagickFormat.Gif); }
        plan = BatchPreview(new[] { gif, input }, options); Assert(!plan[0].Ready && plan[0].Status.StartsWith("Skipped:"), "animated input skipped");
        Assert(!BatchPreview(new[] { folder }, options)[0].Ready, "directory input skipped");
        options = BatchOptions.Default(BatchKind.Resize); options.Width = 240; options.Height = 240; options.OutputSuffix = ""; options.Conflict = BatchConflict.Overwrite;
        plan = BatchPreview(new[] { input }, options); Assert(plan[0].Overwrite, "overwrite is explicit in preview");
        result = BatchRun(plan, options, root); Assert(result.Completed.Count == 1 && Directory.GetFiles(Path.Combine(folder, ".zen-batch-backups")).Any(path => File.ReadAllBytes(path).SequenceEqual(original)), "overwrite keeps exact original backup");
        string protectedFile = Path.Combine(folder, "Protected.png"); MakeImage(protectedFile, 100, 80, 96);
        options = BatchOptions.Default(BatchKind.Resize); options.Subfolder = "Safe";
        plan = BatchPreview(new[] { protectedFile }, options); File.AppendAllText(protectedFile, "changed");
        result = BatchRun(plan, options, root); Assert(result.Errors.Count == 1 && !File.Exists(plan[0].Destination), "stale preview source rejected");
        MakeImage(protectedFile, 100, 80, 96); plan = BatchPreview(new[] { protectedFile }, options); Directory.CreateDirectory(Path.GetDirectoryName(plan[0].Destination)); File.WriteAllText(plan[0].Destination, "late destination");
        result = BatchRun(plan, options, root); Assert(result.Errors.Count == 1 && File.ReadAllText(plan[0].Destination) == "late destination", "late export collision protected");
        plan = BatchPreview(new[] { protectedFile }, options);
        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); result = BatchExecutor.Execute(plan, options, Path.Combine(root, "Profile"), cancel.Token, null); Assert(result.Canceled && !File.Exists(plan[0].Destination), "pre-canceled export writes nothing"); }
        Assert(!Directory.GetFiles(folder, ".zen-batch-*.tmp", SearchOption.AllDirectories).Any(), "no unfinished export temp files");
        options = BatchOptions.Default(BatchKind.Resize); options.Conflict = BatchConflict.Overwrite; options.OutputSuffix = "";
        plan = BatchPreview(new[] { protectedFile }, options); byte[] readonlyOriginal = File.ReadAllBytes(protectedFile); File.SetAttributes(protectedFile, FileAttributes.ReadOnly);
        try { result = BatchRun(plan, options, root); Assert(result.Errors.Count == 1 && File.ReadAllBytes(protectedFile).SequenceEqual(readonlyOriginal), "read-only overwrite refuses without changing original"); }
        finally { File.SetAttributes(protectedFile, FileAttributes.Normal); }
        string heic = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "tools", "Fixtures", "libheif-example.heic"));
        options = BatchOptions.Default(BatchKind.Resize); Assert(!BatchPreview(new[] { heic }, options)[0].Ready, "HEIC encoder is correctly reported as unavailable");
        options.KeepFormat = false; options.Format = ".png"; options.OutputFolder = Path.Combine(folder, "FromHeic");
        plan = BatchPreview(new[] { heic }, options);
        if (plan[0].Ready) { result = BatchRun(plan, options, root); Assert(result.Completed.Count == 1, "HEIC input resized to PNG"); }
        else Assert(plan[0].Status.StartsWith("Skipped: Animation or multi-image"), "multi-image HEIC is explicitly skipped, never silently flattened");
        Console.WriteLine("PASS: resize geometry, six encoders, on-disk originals, metadata/orientation/ICC, dates, overwrite backups, stale inputs and cancellation.");
    }

    private static void BatchUiChecks(string root)
    {
        string photos = Path.Combine(root, "UI"); Directory.CreateDirectory(photos);
        string first = Path.Combine(photos, "Photo01.png"), second = Path.Combine(photos, "Photo02.png"); MakeImage(first, 400, 300, 96); MakeImage(second, 600, 400, 96);
        var presets = new List<BatchPreset> { new BatchPreset { Name = "Web 1280", Options = BatchOptions.Default(BatchKind.Resize) } }; presets[0].Options.Width = 1280;
        foreach (var kind in new[] { BatchKind.Rename, BatchKind.Resize, BatchKind.Convert })
        {
            var dialog = new BatchWindow(kind, new[] { first, second }, Path.Combine(root, "UiProfile"), presets, delegate { }); dialog.Show();
            try
            {
                Wait(delegate { return dialog.PreviewReady && dialog.Plan != null; }, "batch dialog preview " + kind);
                Assert(dialog.Plan.Count == 2 && dialog.Plan.All(item => item.Ready), "dialog previews selected items");
                var grid = Field<DataGrid>(dialog, "_grid"); Assert(grid.EnableRowVirtualization && grid.EnableColumnVirtualization, "preview grid virtualized");
                Render(dialog, "batch-" + kind.ToString().ToLowerInvariant() + "-dark.png");
                ThemeManager.SetDarkTheme(false); dialog.Width = 900; dialog.Height = 580; Pump();
                Render(dialog, "batch-" + kind.ToString().ToLowerInvariant() + "-light-narrow.png");
                Assert(grid.ActualWidth >= 440 && Field<Button>(dialog, "_run").ActualWidth >= 100, "preview and action fit at minimum size");
                ThemeManager.SetDarkTheme(true);
                if (kind == BatchKind.Resize)
                {
                    dialog.Plan[1].Included = false; Task work = dialog.ExecuteAsync(); Wait(() => work.IsCompleted, "resize dialog executes"); work.GetAwaiter().GetResult();
                    Assert(dialog.Result.Completed.Count == 1 && dialog.Result.Skipped == 1, "per-row selection honored by actual dialog execution");
                }
                else if (kind == BatchKind.Convert)
                {
                    grid.ItemsSource = Enumerable.Range(0, 100000).Select(index => new BatchItem { Source = Path.Combine(photos, "Photo" + index + ".png"), Destination = Path.Combine(photos, "Photo" + index + ".jpg"), Status = "Ready", Ready = true }).ToList();
                    Pump(); Assert(grid.ItemContainerGenerator.ContainerFromIndex(50000) == null, "100k-row preview does not create offscreen containers");
                }
            }
            finally { dialog.Close(); Pump(); }
        }
        using (var services = AppServices.Create(Path.Combine(root, "MainProfile")))
        {
            var state = new SessionState { LastFolder = photos, BatchPresets = presets, ActiveTabId = first, WindowWidth = 1100, WindowHeight = 760 };
            state.Tabs.Add(new SessionTabDto { Id = first, Path = first, FolderPath = photos }); state.Tabs.Add(new SessionTabDto { Id = second, Path = second, FolderPath = photos }); services.Sessions.Save(state);
            var window = new MainWindow(services); window.Show(); Wait(() => Ready(window, first), "batch main image"); WaitScan(window);
            Assert(Capture(window).BatchPresets.Single().Options.Width == 1280, "presets restore through automatic session");
            var menu = (ContextMenu)Invoke(window, "BuildBatchMenu"); Assert(menu.Items.Count == 3 && menu.Items.Cast<MenuItem>().All(item => item.IsEnabled), "all batch menu commands available for an image");
            string renamedFirst = Path.Combine(photos, "Photo01_01.png");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) }; Task executing = null;
            timer.Tick += delegate
            {
                var dialog = Field<BatchWindow>(window, "_batchWindow");
                if (dialog == null || !dialog.PreviewReady) return;
                if (executing == null) executing = dialog.ExecuteAsync();
                else if (executing.IsCompleted) { timer.Stop(); dialog.Close(); }
            };
            timer.Start(); try { Invoke(window, "ShowBatch", BatchKind.Rename); } finally { timer.Stop(); }
            executing.GetAwaiter().GetResult(); Wait(() => Ready(window, renamedFirst), "batch rename remaps active viewer tab"); WaitScan(window);
            Assert(Capture(window).Tabs.Any(tab => tab.Path == renamedFirst) && Capture(window).Tabs.Any(tab => tab.Path == second), "renamed active tab retains identity and other tab is unchanged");
            byte[][] colors = BatchColorBytes(); DateTime sameDate = DateTime.UtcNow.AddMinutes(-5);
            File.WriteAllBytes(renamedFirst, colors[0]); File.SetLastWriteTimeUtc(renamedFirst, sameDate);
            Wait(() => Ready(window, renamedFirst) && ToolPixels((System.Windows.Media.Imaging.BitmapSource)Field<Image>(window, "_mainImage").Source)[2] > 200, "watcher reads first color");
            FileRevision revision = FileRevision.Read(renamedFirst);
            File.WriteAllBytes(renamedFirst, colors[1]); File.SetLastWriteTimeUtc(renamedFirst, sameDate);
            Assert(revision.Matches(renamedFirst), "same-size/date overwrite fixture");
            Wait(() => Ready(window, renamedFirst) && ToolPixels((System.Windows.Media.Imaging.BitmapSource)Field<Image>(window, "_mainImage").Source)[0] > 200, "watcher invalidates same-revision pixels");
            var swap = new[] { new TransferredItem { Source = renamedFirst, Destination = second }, new TransferredItem { Source = second, Destination = renamedFirst } };
            var mapper = (Func<string, string>)typeof(MainWindow).GetMethod("CreateTransferredPathMapper", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { swap });
            Assert(mapper(renamedFirst) == second && mapper(second) == renamedFirst, "tab path remapping is simultaneous, not cascading");
            var document = ViewerBackupStore.Create(Capture(window), new BrowserPreferences(), new NamedSessionDocument[0]); string backup = Path.Combine(root, "batch-backup.json"); ViewerBackupStore.Save(backup, document);
            Assert(ViewerBackupStore.Load(backup).Workspace.BatchPresets.Single().Name == "Web 1280", "presets included in backup round trip");
            window.Close(); Pump();
        }
        Console.WriteLine("PASS: native batch dialogs and minimum-size themes, per-item exclusions, modal execution, session/backup presets and active-tab remapping.");
    }
    private static byte[][] BatchColorBytes()
    {
        var results = new List<byte[]>();
        foreach (var color in new[] { MagickColors.Red, MagickColors.Blue })
            using (var image = CodecFixture(color, 80, 60)) using (var stream = new MemoryStream()) { image.Write(stream, MagickFormat.Png); results.Add(stream.ToArray()); }
        int size = results.Max(data => data.Length);
        return results.Select(data => { var padded = new byte[size]; Array.Copy(data, padded, data.Length); return padded; }).ToArray();
    }
    private static void BatchThumbnailChecks(string root)
    {
        string image = Path.Combine(root, "same-revision.png"); byte[][] colors = BatchColorBytes(); DateTime date = DateTime.UtcNow.AddMinutes(-10);
        File.WriteAllBytes(image, colors[0]); File.SetLastWriteTimeUtc(image, date);
        using (var services = AppServices.Create(Path.Combine(root, "ThumbProfile")))
        {
            var first = services.Thumbnails.GetThumbnailAsync(ImageFileItem.FromPath(image), 128, CancellationToken.None).Result;
            FileRevision revision = FileRevision.Read(image); File.WriteAllBytes(image, colors[1]); File.SetLastWriteTimeUtc(image, date);
            Assert(revision.Matches(image), "thumbnail fixture keeps date and size");
            services.Thumbnails.Invalidate(new[] { image });
            var second = services.Thumbnails.GetThumbnailAsync(ImageFileItem.FromPath(image), 128, CancellationToken.None).Result;
            Assert(!ToolPixels(first).SequenceEqual(ToolPixels(second)), "batch invalidation refreshes same-revision thumbnails across size caches");
            File.WriteAllBytes(image, colors[0]); File.SetLastWriteTimeUtc(image, date);
            services.Thumbnails.Invalidate(new[] { root });
            var restored = services.Thumbnails.GetThumbnailAsync(ImageFileItem.FromPath(image), 128, CancellationToken.None).Result;
            Assert(!ToolPixels(restored).SequenceEqual(ToolPixels(second)), "folder rename invalidation reaches descendant image caches");
        }
    }
}
