using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void PressControlKey(UIElement target, Key key)
    {
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled) target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key)
            { RoutedEvent = Keyboard.KeyDownEvent });
    }

    private static void RunFolderEnhanceHistoryChecks()
    {
        RunEnhanceKeyAndHistoryChecks();
        RunFolderCommandChecks();
    }

    private static void RunEnhanceKeyAndHistoryChecks()
    {
        var shortcuts = new ShortcutMap(); string error;
        Assert(shortcuts.Match(Key.E, ModifierKeys.None, false).Id == "manualEnhance" && shortcuts.Match(Key.E, ModifierKeys.None, true) == null, "E opens Enhance only in image mode");
        Assert(shortcuts.Load(new List<ShortcutOverride> { new ShortcutOverride { Action = "metadata", Keys = ShortcutKeys(Key.E) } }, out error)
            && shortcuts.Match(Key.E, ModifierKeys.None, false).Id == "metadata" && shortcuts.Display("manualEnhance") == "Unassigned", "legacy custom E is preserved");
        Assert(shortcuts.Load(new List<ShortcutOverride> { new ShortcutOverride { Action = "manualEnhance", Keys = ShortcutKeys(Key.F9) } }, out error)
            && shortcuts.Match(Key.F9, ModifierKeys.None, false).Id == "manualEnhance", "custom Enhance key survives new default");

        string root = Path.Combine(Root, "history-enhance-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string photo = Path.Combine(photos, "A.png"); MakeImage(photo, 640, 480, 96); MakeImage(Path.Combine(photos, "B.png"), 640, 480, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1440, WindowHeight = 920 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            Invoke(window, "OpenImageTab", photo, true); Wait(delegate { return Ready(window, photo); }, "E image");
            var canvas = Field<Canvas>(window, "_imageCanvas"); canvas.Focus(); PressControlKey(canvas, Key.E); Pump();
            var sliders = Field<List<Slider>>(window, "_manualSliders");
            Console.WriteLine("Enhance key: visible=" + Field<Border>(window, "_manualEnhanceOverlay").IsVisible + ", focus=" + Keyboard.FocusedElement);
            Assert(Field<Border>(window, "_manualEnhanceOverlay").IsVisible && sliders[0].IsKeyboardFocused, "E reveals Enhance and focuses first slider");
            foreach (Slider slider in sliders)
            {
                slider.Focus(); slider.Value = 0;
                PressControlKey(slider, Key.Right); AssertNear(slider.Value, 1, "Right increments focused slider");
                PressControlKey(slider, Key.Left); AssertNear(slider.Value, 0, "Left decrements focused slider");
                slider.Value = slider.Minimum; PressControlKey(slider, Key.Left); AssertNear(slider.Value, slider.Minimum, "slider lower bound");
                slider.Value = 100; PressControlKey(slider, Key.Right); AssertNear(slider.Value, 100, "slider upper bound");
                slider.Value = 0;
                Assert(Ready(window, photo), "slider arrows never change displayed image");
            }
            PressControlKey(sliders[9], Key.E); Pump();
            Assert(!Field<Border>(window, "_manualEnhanceOverlay").IsVisible && canvas.IsKeyboardFocused, "E closes Enhance and returns viewer keys");
            var search = Field<TextBox>(window, "_searchBox"); search.Focus();
            Assert(!(bool)Invoke(window, "RunShortcut", Key.E, ModifierKeys.None, false), "E remains text while searching");
            Invoke(window, "BrowseActiveTab"); WaitScan(window);
            var history = Field<SearchHistory>(window, "_searchHistory");
            var historyInput = Field<SearchHistoryInput>(window, "_searchHistoryInput");
            for (int i = 1; i <= 6; i++) { search.Focus(); search.Text = "query" + i; PressControlKey(search, Key.Enter); }
            Assert(history.Capture().SequenceEqual(new[] { "query6", "query5", "query4", "query3", "query2" }), "only five newest completed searches retained");
            search.Text = " QUERY4 "; PressControlKey(search, Key.Enter);
            Assert(history.Capture().Count == 5 && history.Capture()[0] == "QUERY4" && history.Capture().Count(s => s.Equals("query4", StringComparison.OrdinalIgnoreCase)) == 1, "duplicates move to front case-insensitively");
            search.Text = " "; PressControlKey(search, Key.Enter); Assert(history.Capture().Count == 5, "empty searches ignored");
            ContextMenu menu = historyInput.BuildMenu();
            ((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(search.Text == "query6" && history.Capture()[0] == "query6", "history selection recalls filter and updates recency");
            menu = historyInput.BuildMenu(); menu.PlacementTarget = historyInput.Button; menu.IsOpen = true; Pump();
            Render(menu, "search-history-dark.png"); menu.IsOpen = false;
            ThemeManager.SetDarkTheme(false); menu = historyInput.BuildMenu(); menu.PlacementTarget = historyInput.Button; menu.IsOpen = true; Pump();
            Render(menu, "search-history-light.png"); menu.IsOpen = false; ThemeManager.SetDarkTheme(true);
            window.Width = 980; Pump(); CheckSearchAlignment(window, false);
            AssertInside(historyInput.Button, (FrameworkElement)search.Parent);
            Render((FrameworkElement)window.Content, "search-history-toolbar-narrow.png");

            Invoke(window, "ShowAdvancedSearch"); Pump();
            var advanced = Field<AdvancedSearchWindow>(window, "_advancedSearchWindow");
            var query = Field<TextBox>(advanced, "_query"); query.Focus(); query.Text = "recursive"; PressControlKey(query, Key.Enter);
            Assert(history.Capture()[0] == "recursive", "Advanced search shares toolbar history");
            query.Text = "uncommitted";
            history.Clear(); advanced.Close(); Pump();
            Assert(history.Capture().Count == 0, "clear also cancels pending entries from another search field");
            window.Activate(); search.Focus(); search.Text = "A.png"; PressControlKey(search, Key.Enter);
            Assert(Field<List<ImageFileItem>>(window, "_filteredItems").Count == 1, "history submission still filters normally");
            services.Sessions.SaveNamed("History", Capture(window));
            string backup = Path.Combine(root, "history.json"); WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
            Assert(ViewerBackupStore.Load(backup).Workspace.SearchHistory.SequenceEqual(new[] { "A.png" }), "history included in backup");
            window.Close(); Pump(); window = new MainWindow(services); window.Show(); WaitScan(window);
            history = Field<SearchHistory>(window, "_searchHistory"); historyInput = Field<SearchHistoryInput>(window, "_searchHistoryInput");
            Assert(history.Capture().SequenceEqual(new[] { "A.png" }) && services.Sessions.LoadNamed("History").SearchHistory[0] == "A.png", "automatic and named sessions preserve history");
            menu = historyInput.BuildMenu(); ((MenuItem)menu.Items[menu.Items.Count - 1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert(history.Capture().Count == 0 && Field<TextBox>(window, "_searchBox").Text == "A.png", "Clear history preserves current filter");
            window.Close(); Pump(); window = new MainWindow(services); window.Show(); WaitScan(window);
            Assert(Field<SearchHistory>(window, "_searchHistory").Capture().Count == 0, "cleared history stays empty after restart");
        }
        finally { window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true); }
        Console.WriteLine("PASS: E toggle/focus/migration, all ten native Left/Right slider controls, shared five-entry search history, recall/reset, themes/layout and saved history.");
    }

    private static void RunFolderCommandChecks()
    {
        string root = Path.Combine(Root, "folder-commands-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"), album = Path.Combine(photos, "Album"), child = Path.Combine(album, "Child");
        string sibling = Path.Combine(photos, "Album-extra"), destination = Path.Combine(root, "Destination");
        Directory.CreateDirectory(child); Directory.CreateDirectory(sibling); Directory.CreateDirectory(destination);
        string photo = Path.Combine(child, "Nested.png"); MakeImage(photo, 480, 320, 96);
        File.WriteAllText(Path.Combine(child, "notes.txt"), "Folder contents are preserved.");
        string unrelated = Path.Combine(sibling, "Keep.png"); MakeImage(unrelated, 300, 200, 96);
        var removed = Enumerable.Range(0, 100000).Select(i => new TransferredItem { Source = Path.Combine(photos, "Image" + i + ".png") }).ToList();
        removed.Add(new TransferredItem { Source = album, IsDirectory = true });
        var watch = System.Diagnostics.Stopwatch.StartNew(); var matches = MainWindow.DeletedPathMatcher(removed);
        Assert(removed.All(item => matches(item.Source)) && matches(photo) && !matches(unrelated) && !matches(null)
            && matches(photo.ToUpperInvariant()), "100k deletion index matches exact files/descendants without prefix siblings");
        watch.Stop(); Console.WriteLine("100k deletion-state matches: " + watch.ElapsedMilliseconds + " ms");
        foreach (string invalid in new[] { "..", "..\\Escape", "bad:name", "Trailing." })
        {
            bool rejected = false; try { FileTransferService.Rename(album, invalid); } catch (IOException) { rejected = true; }
            Assert(rejected && File.Exists(photo), "invalid rename cannot escape or change source: " + invalid);
        }
        bool collision = false; try { FileTransferService.Rename(album, "Album-extra"); } catch (IOException) { collision = true; }
        Assert(collision && File.Exists(unrelated) && File.Exists(photo), "rename collision cannot overwrite existing folder");
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 920 });
        services.Sessions.SaveBrowserPreferences(new BrowserPreferences { FavoriteFolders = new List<string> { album, sibling },
            FavoriteDetails = new List<FavoriteFolderDetails> { new FavoriteFolderDetails { Path = album, Name = "Trips", Description = "Original alias" } },
            ExpandedNodes = new List<string> { "favorites", "favorite:" + album + "|" + album, "favorite:" + album + "|" + child } });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            Invoke(window, "OpenImageTab", photo, true); Wait(delegate { return Ready(window, photo); }, "folder nested tab");
            string imageId = Field<string>(window, "_activeTabId");
            Invoke(window, "OpenFolderTab", child); WaitScan(window); string folderId = Field<string>(window, "_activeTabId");
            Invoke(window, "ShowBrowser"); WaitScan(window);
            var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
            grid.ApplySelection(Field<List<ImageFileItem>>(window, "_allItems").Single(i => i.Path == album), ModifierKeys.None);
            Assert((string)Invoke(window, "CurrentCommandPath") == album && Field<Button>(window, "_renameButton").IsEnabled
                && Field<Button>(window, "_deleteButton").IsEnabled, "selected folder enables toolbar/F2/Delete operations");
            Pump(); var tile = FolderTile(window, album);
            foreach (string command in new[] { "rename", "move", "delete", "copy", "cut", "paste" })
                Assert(tile.ContextMenu.Items.OfType<MenuItem>().Any(i => Object.Equals(i.Tag, command)), "folder menu includes " + command);
            Render((FrameworkElement)window.Content, "folder-operations.png");
            Invoke(window, "RenameConfirmedItem", album, "Renamed"); WaitScan(window);
            string renamed = Path.Combine(photos, "Renamed"), newChild = Path.Combine(renamed, "Child"), newPhoto = Path.Combine(newChild, "Nested.png");
            var tabs = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
            Assert(!Directory.Exists(album) && File.Exists(newPhoto) && File.Exists(Path.Combine(newChild, "notes.txt")), "folder rename preserves entire subtree");
            Assert(tabs[imageId].Path == newPhoto && tabs[folderId].FolderPath == newChild, "rename remaps open descendant image and browser tabs");
            var pane = Field<FolderNavigationPane>(window, "_folderNavigation");
            Assert(pane.IsFavorite(renamed) && pane.GetFavoriteDetails(renamed).Name == "Trips" && pane.GetFavoriteDetails(renamed).Description == "Original alias", "favorite path changes but alias remains");
            Assert(pane.CapturePreferences().ExpandedNodes.Contains("favorite:" + renamed + "|" + newChild) && pane.IsFavorite(sibling), "expanded tree remaps without touching prefix siblings");
            Invoke(window, "TransferFilesAsync", new[] { renamed }, destination, true, null);
            Wait(delegate { return !Field<bool>(window, "_fileTransferBusy"); }, "folder move"); WaitScan(window);
            string moved = Path.Combine(destination, "Renamed"), movedChild = Path.Combine(moved, "Child");
            Assert(!Directory.Exists(renamed) && File.Exists(Path.Combine(movedChild, "Nested.png")) && tabs[imageId].Path == Path.Combine(movedChild, "Nested.png"), "Move command pipeline moves folders and remaps tabs");
            Assert(Field<FolderNavigationPane>(window, "_folderNavigation").IsFavorite(moved), "Move remaps favorite shortcuts");
            string loose = Path.Combine(destination, "Loose.png"), empty = Path.Combine(destination, "Empty");
            MakeImage(loose, 100, 80, 96); Directory.CreateDirectory(empty);
            Invoke(window, "LoadFolder", destination); WaitScan(window);
            grid.RestoreSelection(new[] { moved, loose, empty }, moved); Invoke(window, "UpdateStatus");
            Assert(!Field<Button>(window, "_renameButton").IsEnabled && Field<Button>(window, "_moveButton").IsEnabled
                && Field<Button>(window, "_deleteButton").IsEnabled, "mixed multi-selection supports move/recycle, single-item rename only");
            int count = tabs.Count;
            WaitBackup((Task)Invoke(window, "RecycleBrowserItemsAsync", (object)new[] { moved, Path.Combine(movedChild, "Nested.png"), loose, empty })); WaitScan(window);
            Assert(!Directory.Exists(moved) && !Directory.Exists(empty) && !File.Exists(loose) && File.Exists(unrelated), "recursive/mixed deletion recycles only selected top-level items");
            Assert(tabs.Count == count && tabs[imageId].IsBrowser && tabs[imageId].FolderPath == destination
                && tabs[folderId].IsBrowser && tabs[folderId].FolderPath == destination, "deleted descendants retain tabs at surviving parent");
            pane = Field<FolderNavigationPane>(window, "_folderNavigation");
            Assert(!pane.IsFavorite(moved) && pane.IsFavorite(sibling) && !pane.CapturePreferences().ExpandedNodes.Any(s => s.Contains(moved)), "deleted folder shortcuts/expanded paths removed without touching unrelated favorite");
            Assert(Field<TextBlock>(window, "_itemCountText").Text.Contains("0"), "browser counts refresh after folder recycle");
            window.Close(); Pump(); window = new MainWindow(services); window.Show(); WaitScan(window);
            Assert(Field<Dictionary<string, ImageTabState>>(window, "_tabs")[imageId].FolderPath == destination
                && !Field<FolderNavigationPane>(window, "_folderNavigation").IsFavorite(moved), "folder operation state survives restart");
        }
        finally { window.Close(); Pump(); services.Dispose(); }
        Console.WriteLine("PASS: folder menus/toolbar, safe rename, recursive move/recycle, mixed selection, tab/favorite/tree remapping, sibling safety and session restore.");
    }
}
