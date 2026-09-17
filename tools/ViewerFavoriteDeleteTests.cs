using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunFavoriteAndDeleteChecks()
    {
        var version = new Version(BuildInfo.Version);
        var fileVersion = FileVersionInfo.GetVersionInfo(typeof(MainWindow).Assembly.Location);
        Assert(version.Build > 0 && fileVersion.ProductVersion == BuildInfo.Version
            && fileVersion.FileVersion == BuildInfo.Version + ".0", "runtime and native executable version metadata agree");
        string root = Path.Combine(Root, "favorite-delete-" + Guid.NewGuid().ToString("N"));
        string photos = Path.Combine(root, "Actual folder name"), other = Path.Combine(root, "Other");
        Directory.CreateDirectory(Path.Combine(photos, "Branch")); Directory.CreateDirectory(other);
        string first = Path.Combine(photos, "01.png"), middle = Path.Combine(photos, "02.png"), last = Path.Combine(photos, "03.png");
        string unrelated = Path.Combine(other, "Solo.png");
        foreach (string path in new[] { first, middle, last, unrelated }) MakeImage(path, 640, 420, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 780 });
        services.Sessions.SaveBrowserPreferences(new BrowserPreferences { FavoriteFolders = new List<string> { photos }, FavoriteDetails = null });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        Assert(window.Title == BuildInfo.WindowTitle, "window title identifies current build");
        var pane = Field<FolderNavigationPane>(window, "_folderNavigation");
        var favoriteRoot = Field<TreeViewItem>(pane, "_favoritesNode");
        var favorite = (TreeViewItem)favoriteRoot.Items[0];
        Assert(((TextBlock)((StackPanel)favorite.Header).Children[0]).Text == "Actual folder name", "old favorites keep original labels");
        favorite.IsExpanded = true;
        Wait(delegate { return TreeChild(favorite, "Branch") != null; }, "favorite branch");
        Invoke(window, "LoadFolder", other); WaitScan(window);
        int navigations = 0; pane.FolderRequested += delegate { navigations++; };
        string alias = "Summer travels - favorites for the family album";
        string description = "High-resolution originals and edited favorites.\nKeep this collection for the annual printed album.";
        pane.SetFavoriteDetails(photos + "\\", alias, description);
        Assert(navigations == 0 && Field<string>(window, "_currentFolder") == other, "editing favorite cannot navigate or change active folder");
        Assert(Object.ReferenceEquals(favorite, favoriteRoot.Items[0]) && favorite.IsExpanded && TreeChild(favorite, "Branch") != null,
            "alias edits retain favorite expansion and loaded children");
        Assert(Directory.Exists(photos) && File.Exists(middle) && Directory.GetDirectories(root).Length == 3, "alias does not rename actual folder");
        Assert(((TextBlock)((StackPanel)favorite.Header).Children[0]).Text == alias, "custom name appears in tree");
        Assert(((StackPanel)favorite.Header).Children.Count == 2 && ((ScrollViewer)favorite.ToolTip).MaxHeight == 400,
            "description has compact tree line and bounded hover details");
        var captured = pane.CapturePreferences();
        captured.FavoriteDetails[0].Name = "Changed copy";
        Assert(pane.GetFavoriteDetails(photos).Name == alias, "preferences snapshots do not share mutable aliases");
        FavoriteFolderDetails detached = pane.GetFavoriteDetails(photos); detached.Description = "Changed copy";
        Assert(pane.GetFavoriteDetails(photos).Description == description, "favorite detail reads are independent");

        var dialogTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        dialogTimer.Tick += delegate
        {
            var editor = Application.Current.Windows.OfType<FavoriteDetailsWindow>().FirstOrDefault();
            if (editor == null) return;
            dialogTimer.Stop();
            Field<TextBox>(editor, "_name").Text = "Memorable favorite";
            Field<TextBox>(editor, "_description").Text = description;
            Render((FrameworkElement)editor.Content, "favorite-editor-dark.png");
            editor.DialogResult = true;
        };
        dialogTimer.Start();
        var edit = favorite.ContextMenu.Items.OfType<MenuItem>().Single(item => Object.Equals(item.Tag, "rename-favorite"));
        edit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert(pane.GetFavoriteDetails(photos).Name == "Memorable favorite", "favorite context menu saves alias and description");
        pane.SetFavoriteDetails(photos, "", description);
        Assert(((TextBlock)((StackPanel)favorite.Header).Children[0]).Text == "Actual folder name", "empty alias restores real folder label");
        pane.SetFavoriteDetails(photos, alias, description);
        var cancelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        cancelTimer.Tick += delegate
        {
            var editor = Application.Current.Windows.OfType<FavoriteDetailsWindow>().FirstOrDefault();
            if (editor == null) return;
            cancelTimer.Stop();
            Field<TextBox>(editor, "_name").Text = "Canceled alias";
            editor.Width = 400; editor.Height = 340; editor.UpdateLayout();
            Render((FrameworkElement)editor.Content, "favorite-editor-light-narrow.png");
            editor.DialogResult = false;
        };
        ThemeManager.SetDarkTheme(false); Pump();
        cancelTimer.Start(); edit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        ThemeManager.SetDarkTheme(true);
        Assert(pane.GetFavoriteDetails(photos).Name == alias, "cancel leaves favorite unchanged");
        services.Sessions.SaveNamed("Alias isolation", Capture(window));
        Invoke(window, "ApplySessionState", services.Sessions.LoadNamed("Alias isolation"), false); WaitScan(window);
        Assert(pane.GetFavoriteDetails(photos).Name == alias, "aliases survive named-session changes");
        window.Width = 980; window.Height = 640; Pump();
        Render((FrameworkElement)window.Content, "favorite-alias-narrow.png");
        window.Close(); Pump();
        window = new MainWindow(services); window.Show(); WaitScan(window);
        pane = Field<FolderNavigationPane>(window, "_folderNavigation");
        Assert(pane.GetFavoriteDetails(photos).Name == alias && pane.GetFavoriteDetails(photos).Description == description, "favorite alias and description survive restart");
        favoriteRoot = Field<TreeViewItem>(pane, "_favoritesNode"); favorite = (TreeViewItem)favoriteRoot.Items[0];
        Assert(favorite.IsExpanded, "alias retains restored expansion keys");
        pane.ToggleFavorite(photos); pane.ToggleFavorite(photos);
        Assert(pane.GetFavoriteDetails(photos).Name == "", "unpin clears old alias and repin starts fresh");
        pane.SetFavoriteDetails(photos, alias, description);

        Invoke(window, "LoadFolder", photos); WaitScan(window);
        Invoke(window, "OpenBrowserImage", middle); Wait(delegate { return Ready(window, middle); }, "image to recycle");
        string originalTab = Field<string>(window, "_activeTabId");
        Invoke(window, "DuplicateTab", originalTab); Wait(delegate { return Ready(window, middle); }, "duplicate image");
        string activeTab = Field<string>(window, "_activeTabId");
        Invoke(window, "OpenImageTab", unrelated, true); Wait(delegate { return Ready(window, unrelated); }, "unrelated image");
        string unrelatedTab = Field<string>(window, "_activeTabId");
        Invoke(window, "ActivateImageTab", activeTab); Wait(delegate { return Ready(window, middle); }, "delete target restored");
        WaitScan(window);
        var tabs = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
        int tabCount = tabs.Count;
        Invoke(window, "RecycleConfirmedImage", middle);
        Wait(delegate { return Ready(window, last); }, "recycle advances to next image"); WaitScan(window);
        Assert(!File.Exists(middle) && File.Exists(first) && File.Exists(last), "only displayed image was recycled");
        Assert(tabs.Count == tabCount && Field<string>(window, "_activeTabId") == activeTab, "delete keeps original active tab identity and all tabs");
        Assert(tabs[originalTab].IsBrowser && tabs[originalTab].Path == null, "other copy of deleted image becomes a browser tab");
        Assert(tabs[unrelatedTab].Path == unrelated && !tabs[unrelatedTab].IsBrowser, "unrelated tabs untouched");
        System.Windows.Media.Imaging.BitmapSource deletedCache;
        Assert(!services.VramCache.TryGet(middle, out deletedCache), "deleted image removed from decoded cache");
        Assert(Field<TextBlock>(window, "_itemCountText").Text.Contains("Image 2 / 2"), "image position and folder total update after recycle");
        Render((FrameworkElement)window.Content, "delete-next-image.png");
        Invoke(window, "ToggleFullscreen"); Pump();
        Invoke(window, "RecycleConfirmedImage", last);
        Wait(delegate { return Ready(window, first); }, "last image falls back to previous"); WaitScan(window);
        Assert(Field<bool>(window, "_isFullscreen"), "next image retains fullscreen when already enabled");
        Invoke(window, "RecycleConfirmedImage", first); WaitScan(window);
        Assert(Field<string>(window, "_activeTabId") == activeTab && tabs.Count == tabCount
            && tabs[activeTab].IsBrowser && !Field<bool>(window, "_isFullscreen"), "last deletion returns to browser in same tab and exits fullscreen");
        Assert(Field<FolderScanSummary>(window, "_folderSummary").ImageCount == 0, "empty folder count updates");
        Invoke(window, "RecycleConfirmedImage", first);
        Assert(Field<string>(window, "_activeTabId") == activeTab && tabs.Count == tabCount, "missing image deletion leaves workspace unchanged");
        window.Close(); Pump();
        window = new MainWindow(services); window.Show(); WaitScan(window);
        Assert(Field<string>(window, "_activeTabId") == activeTab && Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count == tabCount,
            "post-delete browser tabs survive session restore");

        string ordered = Path.Combine(root, "Order"); Directory.CreateDirectory(ordered);
        foreach (string name in new[] { "01.png", "02.png", "03.png" }) MakeImage(Path.Combine(ordered, name), 200, 140, 96);
        Invoke(window, "LoadFolder", ordered); WaitScan(window);
        Field<System.Windows.Controls.Primitives.ToggleButton>(window, "_sortDirectionButton").IsChecked = true;
        Invoke(window, "SortSelectionChanged"); WaitScan(window);
        Invoke(window, "OpenBrowserImage", Path.Combine(ordered, "02.png"));
        Wait(delegate { return Ready(window, Path.Combine(ordered, "02.png")); }, "descending delete target");
        Invoke(window, "RecycleConfirmedImage", Path.Combine(ordered, "02.png"));
        Wait(delegate { return Ready(window, Path.Combine(ordered, "01.png")); }, "delete follows descending order"); WaitScan(window);
        Field<TextBox>(window, "_searchBox").Text = "01"; Invoke(window, "ApplySearch");
        Invoke(window, "RecycleConfirmedImage", Path.Combine(ordered, "01.png"));
        Wait(delegate { return Ready(window, Path.Combine(ordered, "03.png")); }, "deleting only filtered result falls back to folder neighbor"); WaitScan(window);
        Invoke(window, "StartSlideshow");
        Assert(Field<bool>(window, "_slideshowPlaying"), "slideshow started for delete check");
        Invoke(window, "RecycleConfirmedImage", Path.Combine(ordered, "03.png")); WaitScan(window);
        Assert(!Field<bool>(window, "_slideshowPlaying"), "deleting stops slideshow");
        window.Close(); Pump();
    }
}
