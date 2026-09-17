using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunTransferAndSlideshowChecks()
    {
        string root = Path.Combine(Root, "transfers-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "Source"), target = Path.Combine(root, "Target"), moved = Path.Combine(root, "Moved");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(moved);
        string album = Path.Combine(source, "Album"), empty = Path.Combine(source, "Empty");
        Directory.CreateDirectory(album);
        Directory.CreateDirectory(empty);
        for (int i = 1; i <= 4; i++) MakeImage(Path.Combine(source, "Image" + i + ".png"), 240 + i * 10, 160, 96);
        MakeImage(Path.Combine(album, "Nested.png"), 180, 120, 96);
        File.WriteAllText(Path.Combine(source, "notes.txt"), "A non-image file is included in the total.");
        File.WriteAllText(Path.Combine(target, "Image1.png"), "Original destination content");
        string first = Path.Combine(source, "Image1.png"), second = Path.Combine(source, "Image2.png");

        var copy = FileClipboardData.Create(new[] { first, album }, false);
        Assert(FileClipboardData.Paths(copy).SequenceEqual(new[] { first, album }) && !FileClipboardData.IsCut(copy), "Explorer-compatible multi-file Copy data");
        Assert(FileClipboardData.IsCut(FileClipboardData.Create(new[] { first }, true)), "Explorer-compatible Cut effect");
        Assert(FileClipboardData.Paths(new DataObject()).Length == 0 && FileClipboardData.Paths(null).Length == 0, "non-file clipboard ignored");
        Assert(FileTransferService.TopLevelSources(new[] { album + "\\", Path.Combine(album, "Nested.png"), album.ToUpperInvariant() }).Length == 1,
            "nested and duplicate sources transferred once");
        int prompts = 0;
        TransferResult result = FileTransferService.Execute(new[] { first, album, empty }, target, false, (original, proposed) =>
        {
            prompts++;
            Assert(proposed == Path.Combine(target, "Image1 (2).png"), "collision proposes numbered filename");
            return ConflictAction.Rename;
        });
        Assert(prompts == 1 && result.Completed.Count == 3 && result.Errors.Count == 0, "mixed file/folder copy succeeds with rename confirmation");
        Assert(File.ReadAllText(Path.Combine(target, "Image1.png")) == "Original destination content", "collision never overwrites destination");
        Assert(File.Exists(first) && File.Exists(Path.Combine(album, "Nested.png")), "Copy preserves originals");
        Assert(File.Exists(Path.Combine(target, "Album", "Nested.png")) && Directory.Exists(Path.Combine(target, "Empty")), "recursive and empty folders copied");
        result = FileTransferService.Execute(new[] { first }, target, false, (a, b) => ConflictAction.Skip);
        Assert(result.Skipped == 1 && result.Completed.Count == 0, "No skips collision");
        result = FileTransferService.Execute(new[] { first, second }, target, false, (a, b) => ConflictAction.Cancel);
        Assert(result.Canceled && !File.Exists(Path.Combine(target, "Image2.png")), "Cancel stops remaining batch");
        result = FileTransferService.Execute(new[] { album }, target, false, (a, b) => ConflictAction.Rename);
        Assert(Directory.Exists(Path.Combine(target, "Album (2)")) && result.Completed.Count == 1, "folder collision creates separate renamed folder");
        result = FileTransferService.Execute(new[] { album }, Path.Combine(album, "Missing"), false, (a, b) => ConflictAction.Rename);
        Assert(result.Errors.Count == 1, "missing destination fails safely");
        result = FileTransferService.Execute(new[] { album }, album, false, (a, b) => ConflictAction.Rename);
        Assert(result.Errors.Count == 1 && Directory.Exists(album), "cannot copy folder into itself");
        result = FileTransferService.Execute(new[] { first }, source, true, (a, b) => ConflictAction.Rename);
        Assert(result.Skipped == 1 && File.Exists(first), "same-folder Cut is harmless");
        string targetAlbum = Path.Combine(target, "Album");
        result = FileTransferService.Execute(new[] { Path.Combine(target, "Image1 (2).png"), targetAlbum }, moved, true, (a, b) => ConflictAction.Rename);
        Assert(result.Completed.Count == 2 && !Directory.Exists(targetAlbum) && File.Exists(Path.Combine(moved, "Album", "Nested.png")), "Cut moves complete file/folder contents");
        Assert(FileTransferService.IsWithin(Path.Combine(album, "Nested.png"), album)
            && !FileTransferService.IsWithin(album + "Sibling", album), "folder boundaries do not match similarly named siblings");

        var ordered = new SlideshowSequence(new[] { "A", "B", "C" }, false, "B");
        Assert(ordered.Current == "B" && ordered.Next() == "C" && ordered.Next() == "A", "ordered slideshow loops from current image");
        var shuffled = new SlideshowSequence(new[] { "A", "B", "C", "D" }, true, "C", new Random(7));
        Assert(shuffled.Current == "C", "shuffle starts on current image");
        for (int cycle = 0; cycle < 20; cycle++)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < shuffled.Count; i++)
            {
                string previous = shuffled.Current;
                Assert(seen.Add(previous), "shuffle visits each image once per cycle");
                string[] nearby = shuffled.Nearby(1).ToArray();
                string next = shuffled.Next();
                Assert(next != previous, "shuffle avoids immediate repeats, including cycle boundaries");
                Assert(nearby.Last() == next, "preload includes actual next shuffle image even across cycle boundary");
            }
        }
        Assert(new SlideshowSequence(new string[0], true, null).Next() == null, "empty slideshow is harmless");
        Assert(new SlideshowSequence(new[] { "A" }, true, null).Next() == "A", "single image loops");

        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.SaveNamed("Sharing lock", new SessionState { LastFolder = "before" });
        string lockedSession = (string)Invoke(services.Sessions, "GetNamedPath", "Sharing lock");
        var ready = new ManualResetEventSlim(false);
        var reader = System.Threading.Tasks.Task.Run(delegate
        {
            using (var stream = new FileStream(lockedSession, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ready.Set();
                Thread.Sleep(150);
            }
        });
        ready.Wait();
        services.Sessions.SaveNamed("Sharing lock", new SessionState { LastFolder = "after" });
        reader.Wait();
        Assert(services.Sessions.LoadNamed("Sharing lock").LastFolder == "after", "temporary session replacement locks retried without deleting previous session");
        services.Sessions.Save(new SessionState { LastFolder = source, WindowWidth = 1240, WindowHeight = 780 });
        var window = new MainWindow(services);
        window.Show();
        WaitScan(window);
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        var items = Field<List<ImageFileItem>>(window, "_allItems");
        Assert(Field<FolderScanSummary>(window, "_folderSummary").FileCount == 5, "file count includes non-images");
        Assert(Field<TextBlock>(window, "_itemCountText").Text.Contains("5 files (4 images), 2 folders"), "browser shows files, images and folders separately");
        grid.ApplySelection(items[0], ModifierKeys.None);
        grid.ApplySelection(items[3], ModifierKeys.Shift);
        Assert(grid.SelectedCount == 4, "Shift selects contiguous files and folders");
        grid.ApplySelection(items[1], ModifierKeys.Control);
        Assert(grid.SelectedCount == 3, "Ctrl removes selected item");
        grid.ApplySelection(items[5], ModifierKeys.Control);
        Assert(grid.SelectedCount == 4, "Ctrl adds nonadjacent image");
        string[] selection = grid.SelectedPaths.OrderBy(path => path).ToArray();
        Assert(Field<TextBlock>(window, "_itemCountText").Text.Contains("Image 4 / 4"), "selected image number follows full sorted folder");
        Assert(Invoke(window, "CurrentCommandPath") == null, "single-file destructive controls disabled for group selection");
        Invoke(window, "DuplicateTab", new object[] { null });
        WaitScan(window);
        string duplicateId = Field<string>(window, "_activeTabId");
        Assert(grid.SelectedPaths.OrderBy(path => path).SequenceEqual(selection), "duplicate tab retains selection group");
        grid.SelectItem(items[0]);
        Invoke(window, "ShowBrowser");
        WaitScan(window);
        Assert(grid.SelectedPaths.OrderBy(path => path).SequenceEqual(selection), "tab selection groups remain independent");
        Invoke(window, "RefreshFolder");
        WaitScan(window);
        Assert(grid.SelectedPaths.OrderBy(path => path).SequenceEqual(selection), "watcher-style refresh retains group selection");
        Field<ComboBox>(window, "_sortFieldBox").SelectedIndex = (int)BrowserSortField.Name;
        Field<ToggleButton>(window, "_sortDirectionButton").IsChecked = true;
        Invoke(window, "SortSelectionChanged");
        WaitScan(window);
        Assert(grid.SelectedPaths.OrderBy(path => path).SequenceEqual(selection), "sort retains group selection by path");
        var states = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
        Assert(states[duplicateId].SelectedPaths.Count == 1, "group state deep copied");
        var interval = Field<TextBox>(window, "_slideshowInterval");
        interval.Text = "1.25";
        Invoke(window, "ApplySlideshowInterval");
        Field<CheckBox>(window, "_slideshowShuffle").IsChecked = true;
        services.Sessions.SaveNamed("Selection test", Capture(window));
        Assert(services.Sessions.LoadNamed("Selection test").BrowserSelectedPaths.Count == 4, "named session stores multiple selection");
        Render((FrameworkElement)window.Content, "multi-selection-dark.png");
        window.Width = 980; window.Height = 640;
        Pump();
        Render((FrameworkElement)window.Content, "multi-selection-narrow.png");
        Invoke(window, "SetMetadataPanelVisible", true); Pump();
        var counts = Field<TextBlock>(window, "_itemCountText");
        Assert(counts.ActualWidth <= ((FrameworkElement)counts.Parent).ActualWidth + 1, "counts fit narrow center pane with metadata open");
        Render((FrameworkElement)window.Content, "multi-selection-metadata-narrow.png");
        Invoke(window, "SetMetadataPanelVisible", false); Pump();
        ThemeManager.SetDarkTheme(false);
        Pump();
        Render((FrameworkElement)window.Content, "multi-selection-light.png");
        ThemeManager.SetDarkTheme(true);
        window.Close(); Pump();
        window = new MainWindow(services); window.Show(); WaitScan(window);
        grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        Assert(grid.SelectedPaths.OrderBy(path => path).SequenceEqual(selection), "restart restores whole selection");
        Assert(Field<double>(window, "_slideshowSeconds") == 1.25 && Field<CheckBox>(window, "_slideshowShuffle").IsChecked == true,
            "restart restores slideshow preferences");
        Assert(!Field<bool>(window, "_slideshowPlaying"), "slideshow never autostarts on restore");
        Field<TextBox>(window, "_searchBox").Text = "Image1";
        Wait(delegate { return Field<List<ImageFileItem>>(window, "_filteredItems").Count == 1; }, "search filter");
        grid.SelectAll();
        Assert(grid.SelectedCount == 1 && grid.SelectedItems.Single().Path == first, "Select all never includes hidden search results");
        Field<TextBox>(window, "_searchBox").Text = "";
        Invoke(window, "ApplySearch");
        Field<DispatcherTimer>(window, "_searchTimer").Stop();
        grid.SelectAll();
        var large = Enumerable.Range(0, 100000).Select(i => new ImageFileItem { Path = Path.Combine(source, "virtual" + i + ".png"), Name = "virtual" + i, Extension = ".png" }).ToList();
        grid.SetItems(large); grid.SelectAll(); Pump();
        Assert(grid.SelectedCount == 100000 && Field<Dictionary<int, ThumbnailTile>>(grid, "_realized").Count < 300,
            "100k multiselection retains viewport-only tile realization: " + grid.SelectedCount + " selected, "
            + Field<Dictionary<int, ThumbnailTile>>(grid, "_realized").Count + " realized");
        Invoke(window, "RefreshFolder"); WaitScan(window);
        Invoke(window, "TransferFilesAsync", new[] { second }, target, false, null);
        Wait(delegate { return !Field<bool>(window, "_fileTransferBusy"); }, "background copy");
        Assert(File.Exists(Path.Combine(target, "Image2.png")) && File.Exists(second), "UI transfer path copies without deleting source");
        Assert((bool)Invoke(window, "CanCopyDrop", copy, DragDropEffects.Copy | DragDropEffects.Move, target), "Explorer copy/move drop defaults to accepted copy");
        Assert(!(bool)Invoke(window, "CanCopyDrop", copy, DragDropEffects.Move, target), "move-only drag never deletes originals");
        Assert(!(bool)Invoke(window, "CanCopyDrop", copy, DragDropEffects.Copy, album), "folder cannot be dropped onto itself");
        Assert(!(bool)Invoke(window, "CanCopyDrop", new DataObject("Text", "hello"), DragDropEffects.Copy, target), "non-file drag rejected");
        WaitScan(window);
        ThumbnailTile albumTile = FolderTile(window, album);
        Assert((string)Invoke(window, "BrowserDropDestination", albumTile) == album, "folder tile is a drop destination");
        Assert((string)Invoke(window, "BrowserDropDestination", grid) == source, "browser background drops into current folder");

        // Restore ascending order, then verify actual timer ticks, not just sequence generation.
        Field<ToggleButton>(window, "_sortDirectionButton").IsChecked = false;
        Invoke(window, "SortSelectionChanged"); WaitScan(window);
        Field<CheckBox>(window, "_slideshowShuffle").IsChecked = false;
        interval = Field<TextBox>(window, "_slideshowInterval");
        interval.Text = "0.5"; Invoke(window, "ApplySlideshowInterval");
        interval.Text = "NaN"; Invoke(window, "ApplySlideshowInterval");
        Assert(Field<double>(window, "_slideshowSeconds") == 0.5 && interval.Text == "0.5", "invalid slideshow interval restores previous value");
        Field<TextBox>(window, "_searchBox").Text = "Image1"; Invoke(window, "ApplySearch");
        Invoke(window, "StartSlideshow");
        try { Wait(delegate { return Ready(window, first); }, "slideshow first image"); }
        catch
        {
            Console.WriteLine("Slideshow state: playing=" + Field<bool>(window, "_slideshowPlaying")
                + ", active=" + Field<string>(window, "_activeTabPath") + ", displayed=" + Field<string>(window, "_displayedImagePath")
                + ", pending=" + Field<bool>(window, "_imageViewPending") + ", scan=" + Field<bool>(window, "_folderScanPending")
                + ", timer=" + Field<DispatcherTimer>(window, "_slideshowTimer").IsEnabled
                + ", error=" + Field<TextBlock>(window, "_imageError").Text);
            Render((FrameworkElement)window.Content, "slideshow-failure.png");
            services.Decoders.Decode(first, 0, CancellationToken.None);
            throw;
        }
        Assert(Field<bool>(window, "_slideshowPlaying") && !Field<bool>(window, "_isFullscreen"), "slideshow plays inside window");
        string slideshowTab = Field<string>(window, "_activeTabId");
        int tabCount = Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count;
        Wait(delegate { return Ready(window, second); }, "slideshow advances through entire folder despite search filter");
        Assert(Field<TextBlock>(window, "_itemCountText").Text.Contains("Image 2 / 4"), "viewer reports current image number");
        Render((FrameworkElement)window.Content, "slideshow-windowed.png");
        Wait(delegate { return Ready(window, first); }, "slideshow loops to first image");
        Assert(Field<string>(window, "_activeTabId") == slideshowTab && Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count == tabCount,
            "slideshow reuses one tab for all images");
        PressKey(window, Key.Escape);
        WaitScan(window);
        Assert(!Field<bool>(window, "_slideshowPlaying") && !Field<DispatcherTimer>(window, "_slideshowTimer").IsEnabled
            && Field<Grid>(window, "_browserView").Visibility == Visibility.Visible, "Escape stops timer and returns to browser");
        Invoke(window, "StartSlideshow");
        Wait(delegate { return Field<DispatcherTimer>(window, "_slideshowTimer").IsEnabled; }, "restart slideshow");
        Invoke(window, "OpenFolderTab", target); WaitScan(window);
        Assert(!Field<bool>(window, "_slideshowPlaying"), "switching tabs stops slideshow");
        Invoke(window, "LoadFolder", empty); WaitScan(window);
        Invoke(window, "StartSlideshow");
        Assert(!Field<bool>(window, "_slideshowPlaying"), "empty folder cannot start slideshow");
        Invoke(window, "LoadFolder", album); WaitScan(window);
        Invoke(window, "StartSlideshow");
        string nested = Path.Combine(album, "Nested.png");
        Wait(delegate { return Ready(window, nested); }, "singleton slideshow");
        for (int i = 0; i < 8; i++) Pump();
        Assert(Field<bool>(window, "_slideshowPlaying") && Ready(window, nested), "single-image slideshow continues looping");
        PressKey(window, Key.Escape); WaitScan(window);
        string finalDestination = Path.Combine(root, "Final");
        Directory.CreateDirectory(finalDestination);
        Invoke(window, "TransferFilesAsync", new[] { album }, finalDestination, true, null);
        Wait(delegate { return !Field<bool>(window, "_fileTransferBusy"); }, "move current browser folder");
        WaitScan(window);
        Assert(Field<string>(window, "_currentFolder") == Path.Combine(finalDestination, "Album")
            && File.Exists(Path.Combine(finalDestination, "Album", "Nested.png")), "moved folder updates open tab paths including descendants");
        string errors = Path.Combine(root, "Errors");
        Directory.CreateDirectory(errors);
        string broken = Path.Combine(errors, "A-broken.png"), good = Path.Combine(errors, "B-good.png"), missing = Path.Combine(errors, "C-missing.png");
        File.WriteAllText(broken, "Invalid image fixture");
        MakeImage(good, 150, 90, 96); MakeImage(missing, 150, 90, 96);
        Invoke(window, "LoadFolder", errors); WaitScan(window);
        Invoke(window, "StartSlideshow");
        Wait(delegate { return Field<TextBlock>(window, "_imageError").Visibility == Visibility.Visible; }, "corrupt slideshow placeholder");
        Assert(Field<DispatcherTimer>(window, "_slideshowTimer").IsEnabled, "decode failure does not stall timer");
        File.Delete(missing);
        Wait(delegate { return Ready(window, good); }, "slideshow passes corrupt image");
        File.Delete(broken); File.Delete(good);
        Wait(delegate { return !Field<bool>(window, "_slideshowPlaying"); }, "slideshow stops when all images disappear");
        window.Close(); Pump();
        Invoke(window, "ScheduleSessionSave");
        Assert(!Field<DispatcherTimer>(window, "_placementSaveTimer").IsEnabled, "closed window cannot restart session-saving timer");
        RunSlowSlideshowCheck(root);
    }

    private static void RunSlowSlideshowCheck(string root)
    {
        string folder = Path.Combine(root, "Slow");
        Directory.CreateDirectory(folder);
        string first = Path.Combine(folder, "A.png"), second = Path.Combine(folder, "B.png");
        MakeImage(first, 250, 150, 96); MakeImage(second, 250, 150, 96);
        var services = AppServices.Create(Path.Combine(root, "SlowProfile"));
        var decoder = new DelayedSlideshowDecoder(first);
        Field<List<IImageDecoder>>(services.Decoders, "_decoders").Insert(0, decoder);
        services.Sessions.Save(new SessionState { LastFolder = folder, SlideshowSeconds = 0.5 });
        var window = new MainWindow(services);
        window.Show(); WaitScan(window);
        Invoke(window, "StartSlideshow");
        Wait(delegate { return decoder.Waiting; }, "slow decoder starts");
        for (int i = 0; i < 7; i++) Pump();
        Assert(Field<string>(window, "_activeTabPath") == first && !Field<DispatcherTimer>(window, "_slideshowTimer").IsEnabled,
            "slideshow interval does not consume decode time");
        decoder.Gate.Set();
        Wait(delegate { return Ready(window, first); }, "slow image displayed");
        Assert(Field<DispatcherTimer>(window, "_slideshowTimer").IsEnabled, "timer begins when decoded image reaches frame");
        Wait(delegate { return Ready(window, second); }, "slow slideshow continues");
        Invoke(window, "ToggleFullscreen"); Pump();
        Assert(!Field<bool>(window, "_slideshowPlaying"), "manual fullscreen stops windowed slideshow");
        Invoke(window, "StartSlideshow");
        Assert(!Field<bool>(window, "_isFullscreen") && Field<bool>(window, "_slideshowPlaying"), "starting slideshow leaves fullscreen");
        PressKey(window, Key.Enter); WaitScan(window);
        Assert(!Field<bool>(window, "_slideshowPlaying"), "Enter also stops slideshow and returns to browser");
        window.Close(); Pump();
    }

    private sealed class DelayedSlideshowDecoder : IImageDecoder
    {
        private readonly string _delayed;
        private readonly WicImageDecoder _wic = new WicImageDecoder(() => false);
        public readonly ManualResetEventSlim Gate = new ManualResetEventSlim(false);
        public volatile bool Waiting;
        public DelayedSlideshowDecoder(string path) { _delayed = path; }
        public string Name { get { return "Delayed test decoder"; } }
        public bool CanDecode(string extension) { return _wic.CanDecode(extension); }
        public System.Windows.Media.Imaging.BitmapSource Decode(string path, int width, CancellationToken token)
        {
            if (path == _delayed && width == 0) { Waiting = true; Gate.Wait(10000, token); }
            return _wic.Decode(path, width, token);
        }
        public ImageMetadata ReadMetadata(string path) { return _wic.ReadMetadata(path); }
    }
}
