using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static BitmapSource ReadToolImage(string path)
    { return new WicImageDecoder(delegate { return false; }).Decode(path, 0, CancellationToken.None); }

    private static byte[] ToolPixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        byte[] pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0); return pixels;
    }

    private static void RunImageToolsChecks()
    {
        string root = Path.Combine(Root, "image-tools-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string image = Path.Combine(photos, "Original.png"); MakeImage(image, 340, 220, 96);
        RunRotationStoreChecks(root);
        RunThumbnailRepairChecks(root);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1200, WindowHeight = 780 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        FocusViewerSetting(window, Field<Button>(window, "_rebuildThumbnailsButton"));
        var configure = Field<Window>(window, "_configureWindow");
        Assert(Window.GetWindow(Field<Button>(window, "_rotateLeftButton")) == configure, "image tools live under Configure");
        Assert(!Field<Button>(window, "_rotateLeftButton").IsEnabled && !Capture(window).AutoSaveRotations, "rotation needs an image selection and defaults to non-destructive");
        Field<Button>(window, "_rebuildThumbnailsButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WaitBackup(Field<Task>(window, "_thumbnailRebuildTask")); Pump();
        Assert(Field<TextBlock>(window, "_thumbnailRebuildStatus").Text.Contains("1 image rebuilt"), "Configure rebuild reports completion");
        Assert(Field<Button>(window, "_rebuildThumbnailsButton").IsEnabled && !Field<Button>(window, "_cancelThumbnailRebuild").IsEnabled, "rebuild returns controls to idle");
        CloseViewerSettings(window);
        var browserGrid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        browserGrid.SelectItem(ImageFileItem.FromPath(image)); browserGrid.Focus();
        var tile = Field<Dictionary<int, ThumbnailTile>>(browserGrid, "_realized").Values.Single(value => value.Item.Path == image);
        Wait(delegate { return Field<Image>(tile, "_image").Source != null; }, "original thumbnail available");
        BitmapSource originalThumbnail = (BitmapSource)Field<Image>(tile, "_image").Source;
        byte[] browserOriginal = File.ReadAllBytes(image);
        Assert((bool)Invoke(window, "RunShortcut", Key.Oem6, ModifierKeys.None, false), "right rotation shortcut works from thumbnails");
        Assert(Field<Grid>(window, "_browserView").Visibility == Visibility.Visible
            && browserGrid.SelectedItem.Path == image && browserOriginal.SequenceEqual(File.ReadAllBytes(image)),
            "thumbnail rotation stays in browser and does not modify the source");
        BitmapSource rotatedThumbnail = (BitmapSource)Field<Image>(tile, "_image").Source;
        Assert(rotatedThumbnail.PixelWidth == originalThumbnail.PixelHeight && rotatedThumbnail.PixelHeight == originalThumbnail.PixelWidth,
            "thumbnail pixels rotate in place");
        Assert(Capture(window).HomeBrowserRotations.Single().Turns == 1, "view-only thumbnail rotation enters session state");
        string viewBackup = Path.Combine(root, "thumbnail-rotation.json");
        ViewerBackupStore.Save(viewBackup, (ViewerBackupDocument)Invoke(window, "CaptureBackupSnapshot"));
        Assert(ViewerBackupStore.Load(viewBackup).Workspace.HomeBrowserRotations.Single().Turns == 1,
            "view-only thumbnail rotation survives a backup round-trip");
        var invalidRotation = ViewerBackupStore.Load(viewBackup);
        invalidRotation.Workspace.HomeBrowserRotations.Single().Turns = 4;
        bool rejectedRotation = false;
        try { ViewerBackupStore.Save(Path.Combine(root, "invalid-thumbnail-rotation.json"), invalidRotation); }
        catch (InvalidDataException) { rejectedRotation = true; }
        Assert(rejectedRotation, "backup rejects invalid thumbnail rotations");
        FocusViewerSetting(window, Field<Button>(window, "_rotateLeftButton"));
        Assert(Field<Button>(window, "_rotateLeftButton").IsEnabled, "Configure rotation is available for a selected thumbnail");
        Field<Button>(window, "_rotateLeftButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(Field<Grid>(window, "_browserView").Visibility == Visibility.Visible && Capture(window).HomeBrowserRotations.Count == 0,
            "Configure rotate left cancels a view-only thumbnail turn without opening viewer");
        Field<Button>(window, "_rotateRightButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        CloseViewerSettings(window);
        Invoke(window, "OpenBrowserImage", image); Wait(delegate { return Ready(window, image); }, "rotated thumbnail opens in same orientation");
        Assert(Field<Dictionary<string, ImageTabState>>(window, "_tabs")[Field<string>(window, "_activeTabId")].RotationQuarterTurns == 1,
            "opening rotated thumbnail carries its view-only orientation");
        Invoke(window, "BrowseActiveTab"); WaitScan(window);
        Invoke(window, "OpenImageTab", image, true); Wait(delegate { return Ready(window, image); }, "rotation source");
        byte[] original = File.ReadAllBytes(image);
        string originalTab = Field<string>(window, "_activeTabId");
        Invoke(window, "DuplicateTab", originalTab); Wait(delegate { return Ready(window, image); }, "rotation duplicate");
        FocusViewerSetting(window, Field<Button>(window, "_rotateRightButton"));
        Field<Button>(window, "_rotateRightButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(Field<Dictionary<string, ImageTabState>>(window, "_tabs")[Field<string>(window, "_activeTabId")].RotationQuarterTurns == 1,
            "Configure right rotates current image");
        Assert(original.SequenceEqual(File.ReadAllBytes(image)) && !Directory.Exists(ImageRotationService.BackupFolder(image)), "default rotate never writes file or backup");
        Field<Button>(window, "_rotateLeftButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(!Field<Button>(window, "_saveRotationButton").IsEnabled, "opposite rotations return to zero and disable save");
        Invoke(window, "SetAutoSaveRotations", true, false);
        Field<Button>(window, "_rotateRightButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WaitBackup(Field<Task>(window, "_rotationSaveTask")); Wait(delegate { return Ready(window, image); }, "saved rotation reload"); Pump();
        Assert(ReadToolImage(image).PixelWidth == 220 && ReadToolImage(image).PixelHeight == 340, "auto-save bakes rotation to source");
        Assert(Field<TextBlock>(window, "_rotationStatus").Text.StartsWith("Rotation saved")
            && Field<Dictionary<string, ImageTabState>>(window, "_tabs").Values.All(tab => tab.RotationQuarterTurns == 0), "successful save resets all matching tabs, avoiding double rotation");
        CheckCentered(window);
        Assert(original.SequenceEqual(File.ReadAllBytes(Directory.GetFiles(ImageRotationService.BackupFolder(image), "*.bak").Single())), "UI rotation retains byte-identical original backup");
        Render((FrameworkElement)Field<Window>(window, "_configureWindow").Content, "image-tools-dark.png");
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = false; Pump();
        Render((FrameworkElement)Field<Window>(window, "_configureWindow").Content, "image-tools-light.png");
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = true;
        var config = Field<Window>(window, "_configureWindow"); config.Width = 570; config.Height = 360; Pump();
        Field<CheckBox>(window, "_autoSaveRotationCheckBox").BringIntoView(); Pump();
        AssertInside(Field<CheckBox>(window, "_autoSaveRotationCheckBox"), (FrameworkElement)config.Content);
        Render((FrameworkElement)config.Content, "image-tools-small.png");
        CloseViewerSettings(window); window.Activate(); Field<FrameworkElement>(window, "_imageCanvas").Focus();
        PressKey(window, Key.Oem4); WaitBackup(Field<Task>(window, "_rotationSaveTask")); Wait(delegate { return Ready(window, image); }, "keyboard autosave");
        Assert(ReadToolImage(image).PixelWidth == 340, "bracket shortcuts use auto-save path");
        byte[] beforeFailure = File.ReadAllBytes(image);
        File.SetAttributes(image, File.GetAttributes(image) | FileAttributes.ReadOnly);
        try
        {
            Invoke(window, "RotateImage", 1); WaitBackup(Field<Task>(window, "_rotationSaveTask"));
            Assert(Field<TextBlock>(window, "_rotationStatus").Text.StartsWith("Not saved:") && window.IsEnabled, "save failure is visible and releases busy state");
            Assert(beforeFailure.SequenceEqual(File.ReadAllBytes(image)), "failed autosave leaves source untouched");
        }
        finally { File.SetAttributes(image, File.GetAttributes(image) & ~FileAttributes.ReadOnly); }
        Field<Dictionary<string, ImageTabState>>(window, "_tabs")[Field<string>(window, "_activeTabId")].RotationQuarterTurns = 0;
        Field<Dictionary<string, ImageTabState>>(window, "_tabs")[Field<string>(window, "_activeTabId")].BrowserRotations.Remove(image);
        Invoke(window, "BrowseActiveTab"); WaitScan(window);
        browserGrid.SelectItem(ImageFileItem.FromPath(image)); browserGrid.Focus();
        Task previousSave = Field<Task>(window, "_rotationSaveTask");
        PressKey(window, Key.Oem6);
        Wait(delegate { return Field<Task>(window, "_rotationSaveTask") != previousSave; }, "thumbnail shortcut starts automatic rotation save");
        WaitBackup(Field<Task>(window, "_rotationSaveTask")); WaitScan(window);
        Assert(Field<Grid>(window, "_browserView").Visibility == Visibility.Visible && browserGrid.SelectedItem.Path == image,
            "thumbnail automatic save keeps browser and selection active");
        Assert(ReadToolImage(image).PixelWidth == 220 && Field<TextBlock>(window, "_rotationStatus").Text.StartsWith("Rotation saved"),
            "thumbnail rotation shortcut saves without opening viewer mode");
        services.Sessions.SaveNamed("Auto rotation", Capture(window));
        string backup = Path.Combine(root, "config.json"); WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
        Assert(ViewerBackupStore.Load(backup).Workspace.AutoSaveRotations, "backup captures auto-save setting");
        window.Close(); Pump();
        window = new MainWindow(services); window.Show(); WaitScan(window);
        Assert(Capture(window).AutoSaveRotations && services.Sessions.LoadNamed("Auto rotation").AutoSaveRotations, "automatic and named sessions retain opt-in");
        Invoke(window, "SetAutoSaveRotations", false, false);
        Invoke(window, "ShowBrowser"); WaitScan(window);
        Invoke(window, "StartThumbnailRebuild");
        var rebuild = Field<Task>(window, "_thumbnailRebuildTask");
        Field<Button>(window, "_cancelThumbnailRebuild").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WaitBackup(rebuild); Assert(Field<CancellationTokenSource>(window, "_thumbnailRebuildRequest") == null, "cancel finishes rebuild safely");
        window.Close(); Pump(); services.Dispose();
        RunBrowserRotationPersistenceChecks(root);
        RunMultiThumbnailRotationChecks(root);
        Console.WriteLine("PASS: Configure thumbnail repair/cancel and rotation buttons, default view-only, backed-up auto-save, failure/shortcut/session handling.");
    }

    private static void RunMultiThumbnailRotationChecks(string root)
    {
        string folder = Path.Combine(root, "MultiRotation"); Directory.CreateDirectory(folder);
        string first = Path.Combine(folder, "First.png"), second = Path.Combine(folder, "Second.png");
        string child = Path.Combine(folder, "Folder"); Directory.CreateDirectory(child);
        MakeImage(first, 310, 190, 96); MakeImage(second, 280, 160, 96);
        byte[] originalFirst = File.ReadAllBytes(first), originalSecond = File.ReadAllBytes(second);
        var services = AppServices.Create(Path.Combine(root, "MultiRotationProfile"));
        services.Sessions.Save(new SessionState { LastFolder = folder });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        grid.RestoreSelection(new[] { first, second, child }, child);
        Assert(grid.SelectedCount == 3, "mixed multi-selection includes two images and a folder");
        FocusViewerSetting(window, Field<Button>(window, "_rotateRightButton"));
        var rotate = Field<Button>(window, "_rotateRightButton");
        Assert(rotate.IsEnabled, "rotation remains available when the lead selection is a folder");
        rotate.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(Field<Grid>(window, "_browserView").Visibility == Visibility.Visible && grid.SelectedCount == 3,
            "batch thumbnail rotation stays in browser and preserves selection");
        Assert(Capture(window).HomeBrowserRotations.Select(item => item.Path).OrderBy(path => path)
            .SequenceEqual(new[] { first, second }.OrderBy(path => path)), "only selected images gain view-only rotations");
        Assert(originalFirst.SequenceEqual(File.ReadAllBytes(first)) && originalSecond.SequenceEqual(File.ReadAllBytes(second)),
            "batch rotation remains non-destructive by default");
        Field<Button>(window, "_rotateLeftButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(Capture(window).HomeBrowserRotations.Count == 0, "opposite multi-rotation resets both previews");
        Invoke(window, "SetAutoSaveRotations", true, false);
        rotate.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WaitBackup(Field<Task>(window, "_rotationSaveTask")); WaitScan(window); Pump();
        Assert(Field<Grid>(window, "_browserView").Visibility == Visibility.Visible && grid.SelectedCount == 3,
            "batch auto-save keeps the browser and mixed selection");
        Assert(ReadToolImage(first).PixelWidth == 190 && ReadToolImage(second).PixelWidth == 160,
            "both selected PNG files are saved with their rotations");
        Assert(Capture(window).HomeBrowserRotations.Count == 0 && Directory.Exists(child),
            "saved rotations clear view-only state without touching selected folders");
        Assert(Directory.GetFiles(ImageRotationService.BackupFolder(first), "*.bak").Length == 2,
            "each saved image has its own original-file backup");
        Assert(Field<TextBlock>(window, "_rotationStatus").Text.Contains("2 rotations saved"),
            "batch result reports its saved count");
        CloseViewerSettings(window); window.Close(); Pump(); services.Dispose();
    }

    private static void RunBrowserRotationPersistenceChecks(string root)
    {
        string folder = Path.Combine(root, "RotationPersistence"); Directory.CreateDirectory(folder);
        string image = Path.Combine(folder, "Before.png"), renamed = Path.Combine(folder, "After.png");
        MakeImage(image, 240, 140, 96);
        var services = AppServices.Create(Path.Combine(root, "RotationProfile"));
        services.Sessions.Save(new SessionState { LastFolder = folder });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        grid.SelectItem(ImageFileItem.FromPath(image));
        Invoke(window, "RotateBrowserImage", 1);
        services.Sessions.Save(Capture(window));
        window.Close(); Pump();

        window = new MainWindow(services); window.Show(); WaitScan(window);
        grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        Assert(Capture(window).HomeBrowserRotations.Single().Path == image, "automatic session restores view-only thumbnail rotation");
        var tile = Field<Dictionary<int, ThumbnailTile>>(grid, "_realized").Values.Single(value => value.Item.Path == image);
        Wait(delegate { return Field<Image>(tile, "_image").Source != null; }, "restored rotated thumbnail");
        Assert(((BitmapSource)Field<Image>(tile, "_image").Source).PixelHeight >
            ((BitmapSource)Field<Image>(tile, "_image").Source).PixelWidth, "restored thumbnail renders rotated pixels");
        File.Move(image, renamed);
        Invoke(window, "RemapTransferredPaths", new List<TransferredItem>
        { new TransferredItem { Source = image, Destination = renamed, IsDirectory = false } });
        WaitScan(window);
        Assert(Capture(window).HomeBrowserRotations.Single().Path == renamed,
            "renaming an image keeps its view-only thumbnail rotation");
        Invoke(window, "OpenBrowserImage", renamed); Wait(delegate { return Ready(window, renamed); }, "renamed rotation opens");
        Assert(Field<Dictionary<string, ImageTabState>>(window, "_tabs")[Field<string>(window, "_activeTabId")].RotationQuarterTurns == 1,
            "renamed thumbnail retains the in-view orientation");
        window.Close(); Pump(); services.Dispose();
    }

    private static void RunRotationStoreChecks(string root)
    {
        string folder = Path.Combine(root, "Rotation"); Directory.CreateDirectory(folder);
        string png = Path.Combine(folder, "Alpha.png");
        byte[] pixels = { 0, 0, 255, 255, 0, 255, 0, 160, 255, 0, 0, 80, 255, 255, 255, 0, 100, 90, 80, 255, 30, 20, 10, 100 };
        BitmapSource source = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 12); source.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using (var output = File.Create(png)) encoder.Save(output);
        byte[] original = File.ReadAllBytes(png); FileRevision revision = FileRevision.Read(png);
        string backup = ImageRotationService.Save(png, 1, revision, CancellationToken.None);
        BitmapSource rotated = ReadToolImage(png); byte[] actual = ToolPixels(rotated);
        Assert(rotated.PixelWidth == 2 && rotated.PixelHeight == 3 && original.SequenceEqual(File.ReadAllBytes(backup)), "PNG dimensions and exact original backup");
        byte[] before = ToolPixels(ReadToolImage(backup));
        for (int y = 0; y < 2; y++) for (int x = 0; x < 3; x++) for (int c = 0; c < 4; c++)
            Assert(actual[(x * 2 + 1 - y) * 4 + c] == before[(y * 3 + x) * 4 + c], "PNG rotation preserves every pixel and alpha channel");
        ImageRotationService.Save(png, 3, FileRevision.Read(png), CancellationToken.None);
        Assert(ToolPixels(ReadToolImage(png)).SequenceEqual(before), "left inverse restores exact PNG pixels");
        byte[] unchanged = File.ReadAllBytes(png);
        bool stale = false; try { ImageRotationService.Save(png, 1, revision, CancellationToken.None); } catch (IOException) { stale = true; }
        Assert(stale && unchanged.SequenceEqual(File.ReadAllBytes(png)), "stale image revision cannot overwrite file");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel(); bool rejected = false;
            try { ImageRotationService.Save(png, 1, FileRevision.Read(png), canceled.Token); } catch (OperationCanceledException) { rejected = true; }
            Assert(rejected && unchanged.SequenceEqual(File.ReadAllBytes(png)), "canceled rotation does not replace original");
        }
        Assert(!Directory.GetFiles(folder, "*.tmp").Any(), "rotation staging files cleaned");
        var cache = new VramImageCache(128, new GpuMemoryPressureMonitor());
        revision = FileRevision.Read(png); cache.Add(png, ReadToolImage(png), revision);
        ImageRotationService.Save(png, 1, revision, CancellationToken.None);
        BitmapSource cached;
        Assert(!cache.TryGet(png, out cached) && cache.UsedBytes == 0, "file replacement invalidates decoded cache");
        cache.Add(png, source, revision); Assert(!cache.TryGet(png, out cached), "stale in-flight preload cannot repopulate cache");
        string jpeg = Path.Combine(folder, "Metadata.jpg");
        var metadata = new BitmapMetadata("jpg") { Title = "Rotation test", CameraManufacturer = "Fixture camera" };
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
        var jpg = new JpegBitmapEncoder { QualityLevel = 95 };
        jpg.Frames.Add(BitmapFrame.Create(source, null, metadata, null)); using (var output = File.Create(jpeg)) jpg.Save(output);
        byte[] jpegOriginal = File.ReadAllBytes(jpeg);
        backup = ImageRotationService.Save(jpeg, 1, FileRevision.Read(jpeg), CancellationToken.None);
        using (var input = File.OpenRead(jpeg))
        {
            var decoded = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var saved = (BitmapMetadata)decoded.Frames[0].Metadata;
            Assert(decoded.Frames[0].PixelWidth == 2 && decoded.Frames[0].PixelHeight == 3, "JPEG stored rotation dimensions");
            Assert(saved.Title == "Rotation test" && saved.CameraManufacturer == "Fixture camera" && Convert.ToInt32(saved.GetQuery("/app1/ifd/{ushort=274}")) == 1,
                "JPEG standard metadata preserved and EXIF orientation normalized");
        }
        Assert(jpegOriginal.SequenceEqual(File.ReadAllBytes(backup)), "JPEG backup is byte-identical, independent of re-encoding");
        string mislabeled = Path.Combine(folder, "Mislabeled.jpg"); File.WriteAllBytes(mislabeled, original);
        bool refused = false;
        try { ImageRotationService.Save(mislabeled, 1, FileRevision.Read(mislabeled), CancellationToken.None); }
        catch (NotSupportedException) { refused = true; }
        Assert(refused && original.SequenceEqual(File.ReadAllBytes(mislabeled)), "mislabeled PNG cannot be silently converted to JPEG");
        string animation = Path.Combine(folder, "Animation.png");
        using (var file = File.Create(animation))
        {
            file.Write(original, 0, 33);
            byte[] control = { 0, 0, 0, 8, 97, 99, 84, 76, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0 };
            file.Write(control, 0, control.Length); file.Write(original, 33, original.Length - 33);
        }
        byte[] animated = File.ReadAllBytes(animation); refused = false;
        try { ImageRotationService.Save(animation, 1, FileRevision.Read(animation), CancellationToken.None); }
        catch (NotSupportedException) { refused = true; }
        Assert(refused && animated.SequenceEqual(File.ReadAllBytes(animation)), "PNG animation-control marker prevents flattening, including malformed animations");
        Assert(!ImageRotationService.CanSave("image.webp") && !ImageRotationService.CanSave("image.avif"), "unsupported writers remain view-only");
        Console.WriteLine("Rotation store: PNG pixels/alpha, JPEG metadata, backup integrity, stale/canceled operations and cache invalidation");
    }

    private sealed class ScaledFailureDecoder : IImageDecoder
    {
        private readonly WicImageDecoder _inner = new WicImageDecoder(delegate { return false; });
        public int FullDecodes;
        public string Name { get { return "Scaled decode failure fixture"; } }
        public bool CanDecode(string extension) { return extension == ".png"; }
        public BitmapSource Decode(string path, int width, CancellationToken token)
        {
            if (width > 0) throw new NotSupportedException("Fixture scaled decode unavailable");
            Interlocked.Increment(ref FullDecodes); return _inner.Decode(path, width, token);
        }
        public ZonerInspiredViewer.ImageMetadata ReadMetadata(string path) { return _inner.ReadMetadata(path); }
    }

    private static void RunThumbnailRepairChecks(string root)
    {
        string photos = Path.Combine(root, "Repair"); Directory.CreateDirectory(photos);
        string image = Path.Combine(photos, "Tall.png"); MakeImage(image, 100, 800, 96);
        var decoders = new DecoderRegistry(delegate { return false; }); var failure = new ScaledFailureDecoder();
        Field<List<IImageDecoder>>(decoders, "_decoders").Insert(0, failure);
        var service = new ThumbnailCacheService(decoders, Path.Combine(root, "RepairCache"), 4);
        var item = ImageFileItem.FromPath(image);
        var first = service.GetThumbnailAsync(item, 128, CancellationToken.None); WaitBackup(first);
        Assert(first.Result.PixelHeight == 128 && first.Result.PixelWidth == 16 && first.Result.IsFrozen && failure.FullDecodes == 1,
            "full decode fallback produces bounded detached frozen thumbnail");
        string cache = (string)Invoke(service, "GetCachePath", item, 128);
        Assert(File.Exists(cache), "fallback persists in size-specific catalog");
        File.WriteAllBytes(cache, new byte[] { 1, 2, 3 });
        var repaired = service.GetThumbnailAsync(item, 128, CancellationToken.None); WaitBackup(repaired);
        Assert(repaired.Result.PixelHeight == 128 && failure.FullDecodes == 2, "corrupt disk thumbnail self-repairs");
        MakeImage(cache, 128, 128, 96);
        var rebuild = service.RebuildFolderAsync(photos, 128, null, CancellationToken.None); WaitBackup(rebuild);
        var fresh = service.GetThumbnailAsync(item, 128, CancellationToken.None); WaitBackup(fresh);
        Assert(rebuild.Result.Completed == 1 && rebuild.Result.Failed == 0 && fresh.Result.PixelHeight == 128 && fresh.Result.PixelWidth == 16,
            "forced rebuild replaces even a decodable but incorrect thumbnail");
        Assert(File.Exists((string)Invoke(service, "GetCachePath", item, 64)) && File.Exists((string)Invoke(service, "GetCachePath", item, 256)), "rebuild covers minimap and tab hover cache sizes");
        string invalid = Path.Combine(photos, "Broken.jpg"); File.WriteAllBytes(invalid, new byte[] { 1, 2, 3 });
        rebuild = service.RebuildFolderAsync(photos, 192, null, CancellationToken.None); WaitBackup(rebuild);
        Assert(rebuild.Result.Completed == 1 && rebuild.Result.Failed == 1 && !String.IsNullOrEmpty(rebuild.Result.LastError), "failed image reported without stopping healthy rebuilds");
        string blocked = (string)Invoke(service, "GetCachePath", item, 320); Directory.CreateDirectory(blocked);
        rebuild = service.RebuildFolderAsync(photos, 320, null, CancellationToken.None); WaitBackup(rebuild);
        Assert(rebuild.Result.Completed == 0 && rebuild.Result.Failed == 2, "cache output failure is reported, not a false rebuild success");
        Directory.Delete(blocked);
        string child = Path.Combine(photos, "Child"); Directory.CreateDirectory(child);
        string childImage = Path.Combine(child, "Preview.png"); MakeImage(childImage, 70, 100, 96);
        string deep = Path.Combine(child, "Deep"); Directory.CreateDirectory(deep); string deepImage = Path.Combine(deep, "Ignored.png"); MakeImage(deepImage, 70, 100, 96);
        rebuild = service.RebuildFolderAsync(photos, 192, null, CancellationToken.None); WaitBackup(rebuild);
        Assert(rebuild.Result.Completed == 2 && rebuild.Result.Failed == 1
            && File.Exists((string)Invoke(service, "GetCachePath", ImageFileItem.FromPath(childImage), 96))
            && !File.Exists((string)Invoke(service, "GetCachePath", ImageFileItem.FromPath(deepImage), 96)), "bounded child-folder previews rebuild without recursing into deeper directories");
        Console.WriteLine("Thumbnail repair: scaled fallback, corrupt/wrong cache recovery, selected size, minimap/hover and failure reporting");
    }
}
