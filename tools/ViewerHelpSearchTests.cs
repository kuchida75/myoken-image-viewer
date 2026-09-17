using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static T SearchTask<T>(Task<T> task)
    {
        Wait(delegate { return task.IsCompleted; }, "background search operation");
        return task.GetAwaiter().GetResult();
    }

    private static void WaitSearch(AdvancedSearchWindow window, int count)
    {
        Wait(delegate
        {
            return Field<bool>(window, "_initialized") && !Field<bool>(window, "_scanRunning")
                && !Field<bool>(window, "_queryRunning") && Field<List<ImageFileItem>>(window, "_results").Count == count;
        }, "recursive search result count " + count);
        Pump();
    }

    private static void RunHelpSearchChecks()
    {
        string root = Path.Combine(Root, "hs-" + Guid.NewGuid().ToString("N").Substring(0, 10));
        string photos = Path.Combine(root, "Photos"), album = Path.Combine(photos, "Album"), deep = Path.Combine(album, "Deep");
        string profile = Path.Combine(photos, "Profile"), sibling = Path.Combine(root, "Photos2");
        Directory.CreateDirectory(deep); Directory.CreateDirectory(profile); Directory.CreateDirectory(sibling);
        string first = Path.Combine(photos, "Portrait01.png"), nested = Path.Combine(album, "Portrait01.png"), last = Path.Combine(deep, "Portrait20.png");
        MakeImage(first, 640, 400, 96); MakeImage(nested, 300, 460, 96); MakeImage(last, 420, 280, 96);
        MakeImage(Path.Combine(profile, "Portrait-cache.png"), 80, 80, 96);
        MakeImage(Path.Combine(sibling, "Portrait-outside.png"), 80, 80, 96);
        File.WriteAllText(Path.Combine(photos, "Portrait-not-image.txt"), "fixture");
        File.WriteAllText(Path.Combine(photos, "Portrait-raw.cr2"), "fixture");
        var index = new RecursiveSearchIndex(profile, 30);
        Assert(index.WorkerCount == 8, "search scan concurrency bounded to eight workers");
        Assert(!RecursiveSearchIndex.InScope(sibling, photos), "path boundary excludes sibling prefix");
        Assert(RecursiveSearchIndex.InScope(deep, photos), "path boundary includes descendants");
        Assert(RecursiveSearchIndex.CanonicalPath(Path.GetPathRoot(root)) == Path.GetPathRoot(root), "drive root separator retained");
        Assert(index.Load(photos, CancellationToken.None) == null, "cold index cache absent");
        SearchSnapshot snapshot = SearchTask(index.RefreshAsync(photos, null, CancellationToken.None));
        Assert(snapshot.Items.Length == 5 && snapshot.Items.Count(item => item.IsDirectory) == 2,
            "recursive scan includes two nested directories and three images, excluding profile and unsupported files");
        List<ImageFileItem> hits = SearchTask(index.QueryAsync(snapshot, "pOrTrAiT", BrowserSortField.Name, false, CancellationToken.None));
        Assert(hits.Count == 3 && hits.Any(item => item.Path == last), "recursive case-insensitive name match reaches grandchild");
        Assert(SearchTask(index.QueryAsync(snapshot, "Deep", BrowserSortField.Name, false, CancellationToken.None)).Single().IsDirectory, "folder name matches");
        Assert(SearchTask(index.QueryAsync(snapshot, "not-there", BrowserSortField.Name, false, CancellationToken.None)).Count == 0, "empty matching set");
        Assert(SearchTask(index.QueryAsync(new SearchSnapshot { Items = new ImageFileItem[0] }, "x", BrowserSortField.Name, false, CancellationToken.None)).Count == 0, "empty index query");
        hits = SearchTask(index.QueryAsync(snapshot, "Portrait", BrowserSortField.Number, true, CancellationToken.None));
        Assert(hits[0].Path == last, "search uses existing descending natural sort");
        foreach (BrowserSortField field in Enum.GetValues(typeof(BrowserSortField)))
            Assert(SearchTask(index.QueryAsync(snapshot, "Portrait", field, false, CancellationToken.None)).Count == 3, "search sort " + field);
        SearchSnapshot cached = index.Load(photos, CancellationToken.None);
        Assert(cached.FromCache && cached.Items.Length == 5 && cached.Items.Single(item => item.Path == first).Length == new FileInfo(first).Length,
            "cache round-trip preserves metadata and cached state");
        byte[] goodCache = File.ReadAllBytes(index.CachePath(photos));
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            Task<SearchSnapshot> busy = index.RefreshAsync(photos, delegate { entered.Set(); release.Wait(); }, CancellationToken.None);
            try
            {
                Wait(delegate { return entered.IsSet; }, "paused background index worker");
                Assert(!busy.IsCompleted, "refresh remains busy while cached queries run");
                Assert(SearchTask(index.QueryAsync(cached, "Portrait", BrowserSortField.Name, false, CancellationToken.None)).Count == 3,
                    "cached matches remain available independently of a slow scan");
            }
            finally { release.Set(); SearchTask(busy); }
        }
        goodCache = File.ReadAllBytes(index.CachePath(photos));
        using (var midScan = new CancellationTokenSource())
        {
            Task<SearchSnapshot> stopped = index.RefreshAsync(photos, delegate { midScan.Cancel(); }, midScan.Token);
            Wait(delegate { return stopped.IsCompleted; }, "mid-scan cancellation");
            Assert(stopped.IsCanceled, "cancel while directory workers are running");
            Assert(File.ReadAllBytes(index.CachePath(photos)).SequenceEqual(goodCache), "mid-scan cancellation preserves cache");
        }
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            Task<SearchSnapshot> stopped = index.RefreshAsync(photos, null, cancel.Token);
            Wait(delegate { return stopped.IsCompleted; }, "canceled index");
            Assert(stopped.IsCanceled, "canceled index operation reports cancellation");
            Assert(File.ReadAllBytes(index.CachePath(photos)).SequenceEqual(goodCache), "canceled scan preserves completed cache");
        }
        File.WriteAllBytes(index.CachePath(photos), new byte[] { 1, 2, 3 });
        Assert(index.Load(photos, CancellationToken.None) == null, "corrupt cache rejected without crash");
        var otherIndex = new RecursiveSearchIndex(profile, 4);
        Task<SearchSnapshot> writerA = index.RefreshAsync(photos, null, CancellationToken.None);
        Task<SearchSnapshot> writerB = otherIndex.RefreshAsync(photos, null, CancellationToken.None);
        SearchTask(writerA); SearchTask(writerB);
        Assert(index.Load(photos, CancellationToken.None).Items.Length == 5, "concurrent index writers leave readable complete snapshot");
        Assert(!Directory.GetFiles(Path.GetDirectoryName(index.CachePath(photos)), "*.tmp").Any(), "index temporary files released");
        string loop = Path.Combine(album, "Loop");
        using (var linkProcess = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + loop + "\" \"" + photos + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
        {
            linkProcess.WaitForExit();
            if (linkProcess.ExitCode != 0) throw new Exception("Fixture junction creation failed: " + linkProcess.StandardError.ReadToEnd());
        }
        try
        {
            SearchSnapshot linked = SearchTask(index.RefreshAsync(photos, null, CancellationToken.None));
            Assert(linked.Items.Length == 5 && linked.SkippedLinks == 1, "nested junction cycle is skipped and counted");
        }
        finally { Directory.Delete(loop); }

        var services = AppServices.Create(profile);
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 780 });
        var main = new MainWindow(services); main.Show(); WaitScan(main);
        Field<TextBox>(main, "_searchBox").Focus(); PressKey(main, Key.F1); Pump();
        HelpWindow help = Field<HelpWindow>(main, "_helpWindow");
        Assert(help != null && help.IsVisible && help.Title.Contains(BuildInfo.Version), "F1 works with search-field focus and shows current version");
        Assert(help.Builds.Count >= 26 && help.Builds.First().Version == BuildInfo.Version, "every build version is embedded and current first");
        Assert(help.Builds.Any(build => build.Result == "Failed"), "failed build history retained");
        Assert(help.Builds.First().ToString().StartsWith("v" + BuildInfo.Version), "native dropdown selection renders version label, not CLR type name");
        Assert(help.ReleaseNotes.Contains("Advanced Search") && HelpWindow.Shortcuts.Contains("Ctrl+Shift+F"), "embedded Help documents new search command");
        foreach (HelpBuild build in help.Builds)
        {
            Field<ComboBox>(help, "_versions").SelectedItem = build;
            Assert(HelpDocument.Text(Field<RichTextBox>(help, "_buildText")).Contains(build.Version)
                && HelpDocument.Text(Field<RichTextBox>(help, "_buildText")).Contains(build.Note), "per-version changelog note " + build.Version);
        }
        Field<ComboBox>(help, "_versions").SelectedIndex = 0;
        help.SelectSection(1); Pump(); Render((FrameworkElement)help.Content, "help-history-dark.png");
        var versions = Field<ComboBox>(help, "_versions"); versions.IsDropDownOpen = true; Pump();
        var historyPopup = (Popup)versions.Template.FindName("PART_Popup", versions);
        Render((FrameworkElement)historyPopup.Child, "help-version-dropdown-dark.png"); versions.IsDropDownOpen = false;
        ThemeManager.SetDarkTheme(false); help.SelectSection(0); help.Width = 580; help.Height = 460; Pump();
        Render((FrameworkElement)help.Content, "help-shortcuts-light-narrow.png");
        ThemeManager.SetDarkTheme(true); help.Close(); Pump();
        main.Width = 980; main.Height = 640; Pump();
        Render((FrameworkElement)main.Content, "help-search-toolbar-narrow.png");
        Field<Button>(main, "_helpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Assert(Field<HelpWindow>(main, "_helpWindow") != null, "Help button reopens closed Help window");
        Field<HelpWindow>(main, "_helpWindow").Close();

        Field<Button>(main, "_advancedSearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var search = Field<AdvancedSearchWindow>(main, "_advancedSearchWindow");
        Assert(search != null && search.RootFolder == photos, "advanced search button captures current folder");
        Wait(delegate { return Field<bool>(search, "_initialized"); }, "search cache loaded");
        Assert(!Field<bool>(search, "_scanRunning") && !Field<bool>(search, "_started"), "empty query does not crawl subtree");
        TextBox query = Field<TextBox>(search, "_query"); query.Text = "Portrait";
        WaitSearch(search, 3);
        var grid = Field<VirtualizedThumbnailGrid>(search, "_grid");
        Assert(Field<TextBox>(main, "_searchBox").Text == "", "recursive search leaves simple folder filter unchanged");
        Assert(grid.ContextMenu == null, "search background has no nonfunctional clipboard menu");
        var tiles = Field<Dictionary<int, ThumbnailTile>>(grid, "_realized").Values;
        Assert(tiles.Any(tile => Field<TextBlock>(tile, "_details").Text == "Album\\Deep"), "result captions disambiguate nested paths");
        Assert(tiles.All(tile => (string)tile.ToolTip == tile.Item.Path && tile.ContextMenu.Items.Count == 2), "full-path tooltips and focused result menus");
        grid.Focus(); grid.HandleNavigationKey(Key.End, ModifierKeys.None); Pump();
        Assert(grid.SelectedItem.Path == last, "result keyboard End selects final image");
        Render((FrameworkElement)search.Content, "advanced-search-dark.png");
        Field<Slider>(search, "_size").Value = 192; Field<Slider>(search, "_aspect").Value = 0.5;
        search.Width = 640; search.Height = 480; ThemeManager.SetDarkTheme(false);
        Wait(delegate { return grid.ThumbnailSize == 192 && grid.ThumbnailAspectRatio == 0.5; }, "search sliders resized");
        Pump(); Render((FrameworkElement)search.Content, "advanced-search-light-narrow.png");
        ThemeManager.SetDarkTheme(true);

        string added = Path.Combine(deep, "Portrait-live.png"); MakeImage(added, 200, 120, 96); WaitSearch(search, 4);
        File.Delete(added); WaitSearch(search, 3);
        string renamed = Path.Combine(album, "Renamed"); Directory.Move(deep, renamed);
        Wait(delegate { return Field<List<ImageFileItem>>(search, "_results").Any(item => item.Path == Path.Combine(renamed, "Portrait20.png")); }, "watcher refreshes renamed subtree");
        WaitSearch(search, 3);
        query.Text = "nothing"; query.Text = "Portrait20"; WaitSearch(search, 1);
        Assert(Field<List<ImageFileItem>>(search, "_results")[0].Path == Path.Combine(renamed, "Portrait20.png"), "superseded query cannot replace newer results");
        Invoke(search, "Stop");
        MakeImage(Path.Combine(renamed, "Portrait21.png"), 120, 80, 96);
        for (int i = 0; i < 15; i++) Pump();
        Assert(!Field<bool>(search, "_scanRunning") && Field<bool>(search, "_stopped"), "Stop suspends watcher-triggered scanning");
        query.Text = "Portrait"; WaitSearch(search, 4);
        var vanished = new ImageFileItem { Name = "vanished.png", Path = Path.Combine(photos, "vanished.png") };
        Invoke(search, "OpenResult", vanished, false);
        Assert(search.IsVisible && Field<string>(search, "_error").Contains("no longer"), "missing cached result handled without closing window or opening tab");
        var result = Field<List<ImageFileItem>>(search, "_results").Single(item => item.Path == nested);
        Invoke(search, "OpenResult", result, false);
        Wait(delegate { return Ready(main, nested); }, "recursive result opens exact nested image");
        Assert(Field<AdvancedSearchWindow>(main, "_advancedSearchWindow") == null, "opening result closes owned search window");
        WaitScan(main); Invoke(main, "ShowAdvancedSearch"); Pump();
        search = Field<AdvancedSearchWindow>(main, "_advancedSearchWindow");
        Assert(search.RootFolder == album, "new search follows newly active image folder");
        Field<TextBox>(search, "_query").Text = "Renamed"; WaitSearch(search, 1);
        result = Field<List<ImageFileItem>>(search, "_results")[0]; Invoke(search, "OpenResult", result, true);
        WaitScan(main);
        Assert(Field<string>(main, "_currentFolder") == album && Field<Grid>(main, "_browserView").Visibility == Visibility.Visible,
            "Open containing folder navigates exact parent into browser tab");

        // Large synthetic snapshots test query/virtualization cost independently from filesystem speed.
        var large = new SearchSnapshot { Root = photos, Items = Enumerable.Range(0, 100000).Select(i => new ImageFileItem
            { Name = "Image" + i.ToString("000000") + ".png", Path = Path.Combine(photos, "Image" + i.ToString("000000") + ".png"), Extension = ".png" }).ToArray() };
        var watch = Stopwatch.StartNew();
        hits = SearchTask(index.QueryAsync(large, "Image", BrowserSortField.Name, false, CancellationToken.None));
        Console.WriteLine("Search: 100,000 synthetic matches sorted in " + watch.ElapsedMilliseconds + " ms (includes dispatcher polling).");
        Assert(hits.Count == 100000, "100k query has no result truncation");
        var hugeGrid = new VirtualizedThumbnailGrid(services.Thumbnails) { ContextMenu = null };
        var host = new Window { Content = hugeGrid, Width = 800, Height = 600 }; host.Show(); hugeGrid.SetItems(hits); Pump();
        hugeGrid.HandleNavigationKey(Key.End, ModifierKeys.None); Pump();
        Assert(hugeGrid.SelectedItem.Name == "Image099999.png", "large result set reaches final match");
        Assert(Field<Dictionary<int, ThumbnailTile>>(hugeGrid, "_realized").Count < 300, "100k results stay virtualized");
        hugeGrid.SetItems(null); host.Close();
        Invoke(main, "ShowAdvancedSearch"); Pump(); search = Field<AdvancedSearchWindow>(main, "_advancedSearchWindow");
        Field<TextBox>(search, "_query").Text = "Portrait"; main.Close(); Pump();
        Assert(!search.IsVisible && Field<bool>(search, "_closed"), "owner close cancels owned search and releases window");
        services.Dispose();
        Console.WriteLine("PASS: embedded Help/version history, recursive scope/cache, parallel queries, watcher changes, result actions, cancellation and 100k virtualization.");
    }
}
