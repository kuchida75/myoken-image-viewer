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
    private static void RunTreeEnhanceBackupChecks()
    {
        string root = Path.Combine(Root, "tb-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string dark = Path.Combine(photos, "Dark.png"), bright = Path.Combine(photos, "Bright.png"), missing = Path.Combine(photos, "Missing.png");
        MakeEnhanceFixture(dark, "dark"); MakeEnhanceFixture(bright, "bright"); MakeImage(missing, 360, 240, 96);
        byte[] original = File.ReadAllBytes(dark);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 780 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        var tree = Field<FolderNavigationPane>(window, "_folderNavigation");
        var toggle = Field<Button>(window, "_folderTreeToggle");
        var toolbar = Field<UIElement>(window, "_topToolbar");
        var column = Field<ColumnDefinition>(window, "_folderColumn");
        Assert(Field<GridSplitter>(window, "_leftSplitter").ResizeBehavior == GridResizeBehavior.PreviousAndNext,
            "divider resizes tree/workspace rather than the arrow rail");
        column.Width = new GridLength(310); Pump(); Keyboard.ClearFocus();
        PressKey(window, Key.J); Pump();
        Assert(tree.Visibility == Visibility.Collapsed && toolbar.IsVisible && toggle.IsVisible, "J hides only tree and keeps reveal arrow");
        Assert(System.Windows.Automation.AutomationProperties.GetName(toggle) == "Show folder tree" && column.ActualWidth == 0, "hidden tree arrow announces show and reclaims space");
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(tree.IsVisible && System.Windows.Automation.AutomationProperties.GetName(toggle) == "Hide folder tree", "arrow restores tree");
        AssertNear(column.ActualWidth, 310, "tree width restored");
        PressKey(window, Key.J); PressKey(window, Key.U); PressKey(window, Key.J); Pump();
        Assert(tree.IsVisible && toolbar.Visibility == Visibility.Collapsed, "J can show tree independently inside compact");
        PressKey(window, Key.U); Pump();
        Assert(tree.Visibility == Visibility.Collapsed && toolbar.IsVisible, "leaving compact restores explicit normal tree preference");
        Field<TextBox>(window, "_searchBox").Focus(); PressKey(window, Key.J);
        Assert(tree.Visibility == Visibility.Collapsed, "J does not toggle while typing"); Keyboard.ClearFocus();
        PressKey(window, Key.J); Pump();
        Assert(Field<ToggleButton>(window, "_enhanceButton").IsEnabled, "Quick Enhance available before opening image");
        SetEnhance(window, true);
        Invoke(window, "OpenBrowserImage", dark); Wait(delegate { return Ready(window, dark); }, "enhance from browser"); WaitScan(window); WaitEnhance(window);
        string darkId = Field<string>(window, "_activeTabId");
        Invoke(window, "ToggleTabPin", darkId);
        Invoke(window, "OpenImageTab", bright, true); Wait(delegate { return Ready(window, bright); }, "second enhanced tab"); WaitEnhance(window);
        string brightId = Field<string>(window, "_activeTabId");
        Assert(Field<bool>(window, "_quickEnhanceEnabled"), "enhance stays enabled across tabs");
        Invoke(window, "BrowseActiveTab"); WaitScan(window);
        Assert(Field<ToggleButton>(window, "_enhanceButton").IsChecked == true, "enhance remains checked in browser");
        SetEnhance(window, false);
        Invoke(window, "ActivateImageTab", darkId); Wait(delegate { return Ready(window, dark); }, "disabled enhancement in another tab");
        Assert(Field<Image>(window, "_mainImage").Effect == null, "disabling enhancement affects all tabs");
        using (var otherServices = AppServices.Create(Path.Combine(root, "OtherProfile")))
        {
            otherServices.Sessions.Save(new SessionState { LastFolder = photos });
            var other = new MainWindow(otherServices); other.Show(); WaitScan(other);
            SetEnhance(window, true);
            Assert(!Field<bool>(other, "_quickEnhanceEnabled"), "enhancement preference is isolated by window");
            other.Close(); Pump();
        }
        WaitEnhance(window);
        tree.ToggleFavorite(photos); tree.SetFavoriteDetails(photos, "Backup favorites", "Original photos");
        Field<Slider>(window, "_thumbnailSizeSlider").Value = 192;
        Field<Slider>(window, "_thumbnailAspectSlider").Value = 1.5;
        Invoke(window, "SetMetadataPanelVisible", true);
        Field<Slider>(window, "_vramSlider").Value = 1536;
        PressKey(window, Key.J); Pump();
        Render((FrameworkElement)window.Content, "tree-hidden-enhance-dark.png");
        Invoke(window, "OpenImageTab", missing, true); Wait(delegate { return Ready(window, missing); }, "image to be missing on import");
        string missingId = Field<string>(window, "_activeTabId");
        services.Sessions.SaveNamed("Photo workspace", Capture(window));
        string backup = Path.Combine(root, "backup.json");
        WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
        Assert(window.IsEnabled && !Field<bool>(window, "_backupBusy") && File.Exists(backup), "export finishes and restores input");
        ViewerBackupDocument exported = ViewerBackupStore.Load(backup);
        Assert(exported.Workspace.Tabs.Count == 3 && exported.Workspace.Tabs[0].IsPinned
            && exported.Workspace.QuickEnhanceEnabled == true && exported.Workspace.FolderTreeHidden, "backup contains ordered pins and viewer configuration");
        Assert(exported.Browser.FavoriteDetails.Single().Name == "Backup favorites" && exported.SavedSessions.Count == 1,
            "backup includes favorite details and named sessions");
        var menu = (ContextMenu)Invoke(window, "BuildSessionsMenu");
        Assert(menu.Items.OfType<MenuItem>().Any(item => Object.Equals(item.Tag, "export-backup"))
            && menu.Items.OfType<MenuItem>().Any(item => Object.Equals(item.Tag, "import-backup")), "backup commands discoverable in Sessions menu");

        File.Delete(missing);
        SetEnhance(window, false);
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = false;
        PressKey(window, Key.J); Pump();
        tree.SetFavoriteDetails(photos, "Changed locally", "Before import");
        services.Sessions.SaveNamed("Unrelated session", Capture(window));
        var import = (Task<int>)Invoke(window, "ImportBackupFileAsync", backup, false);
        WaitBackup(import); WaitScan(window); Wait(delegate { return Ready(window, dark); }, "fallback to surviving pinned image"); WaitEnhance(window);
        Assert(import.Result == 2, "missing image tabs removed from current workspace and named session");
        var tabs = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
        Assert(tabs.Count == 2 && !tabs.ContainsKey(missingId) && tabs.ContainsKey(brightId) && tabs[darkId].IsPinned,
            "import keeps surviving tabs and closes missing image tab");
        Assert(Field<string>(window, "_activeTabId") == darkId && Field<bool>(window, "_quickEnhanceEnabled"), "import restores global enhancement and active fallback");
        Assert(ThemeManager.IsDarkTheme && Field<bool>(window, "_folderTreeHidden")
            && Field<Grid>(window, "_browserView").Visibility == Visibility.Collapsed, "import restores layout/theme and viewer mode");
        Assert(services.VramCache.BudgetMb == 1536 && Field<int>(window, "_thumbnailSizePixels") == 192
            && Math.Abs(Field<double>(window, "_thumbnailAspectRatio") - 1.5) < 0.01, "import restores cache and thumbnail settings");
        tree = Field<FolderNavigationPane>(window, "_folderNavigation");
        Assert(tree.GetFavoriteDetails(photos).Name == "Backup favorites" && services.Sessions.NamedSessionExists("Unrelated session"),
            "import replaces favorites while retaining unrelated named sessions");
        Assert(services.Sessions.LoadNamed("Photo workspace").Tabs.Count == 2, "import cleans missing paths from saved workspace");
        string[] recovery = Directory.GetFiles(Path.Combine(services.AppDataRoot, "backups"), "*.json");
        Assert(recovery.Length == 1 && ViewerBackupStore.Load(recovery[0]).Workspace.QuickEnhanceEnabled == false, "recovery backup preserves pre-import configuration");
        string bad = Path.Combine(root, "bad.json");
        File.WriteAllText(bad, "{ malformed backup");
        ExpectImportFailure(window, bad, darkId, 2);
        var unsupported = ViewerBackupStore.Load(backup); unsupported.SchemaVersion = 99;
        SessionStore.WriteObject(bad, unsupported, typeof(ViewerBackupDocument));
        ExpectImportFailure(window, bad, darkId, 2);
        unsupported.SchemaVersion = 1; unsupported.Workspace.Tabs[0].Path = "relative.png";
        SessionStore.WriteObject(bad, unsupported, typeof(ViewerBackupDocument));
        ExpectImportFailure(window, bad, darkId, 2);
        Assert(Directory.GetFiles(Path.Combine(services.AppDataRoot, "backups"), "*.json").Length == 1,
            "invalid imports do not start replacing settings or create recovery snapshots");
        CheckBackupValidation(backup, bad, photos, missing);
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = false;
        window.Width = 980; window.Height = 640; Pump();
        Render((FrameworkElement)window.Content, "tree-arrow-light-narrow.png");
        PressKey(window, Key.J); Pump();
        window.Close(); Pump();
        window = new MainWindow(services); window.Show(); Wait(delegate { return Ready(window, dark); }, "restart imported workspace"); WaitEnhance(window);
        Assert(Field<bool>(window, "_folderTreeHidden") && !Field<bool>(window, "_isCompactMode")
            && Field<ToggleButton>(window, "_enhanceButton").IsChecked == true, "tree preference and global enhancement survive restart in normal mode");
        window.Close(); Pump(); services.Dispose();
        Assert(File.ReadAllBytes(dark).SequenceEqual(original), "enhancement and backups never modify original image");
        ThemeManager.SetDarkTheme(true);
        Console.WriteLine("PASS: J/arrow tree control, compact interaction, global enhancement, JSON backup/restore/recovery, validation and missing-tab cleanup.");
    }

    private static void WaitBackup(Task task)
    {
        Wait(delegate { return task.IsCompleted; }, "backup operation");
        task.GetAwaiter().GetResult();
    }

    private static void CheckBackupValidation(string backup, string bad, string photos, string missing)
    {
        var duplicate = ViewerBackupStore.Load(backup);
        duplicate.Workspace.Tabs.Add(duplicate.Workspace.Tabs[0]);
        SessionStore.WriteObject(bad, duplicate, typeof(ViewerBackupDocument));
        ExpectBadBackup(bad, "duplicate tab IDs rejected");
        var numeric = ViewerBackupStore.Load(backup); numeric.Workspace.WindowWidth = 100000000;
        SessionStore.WriteObject(bad, numeric, typeof(ViewerBackupDocument));
        ExpectBadBackup(bad, "unsafe numeric layout rejected");
        var relative = ViewerBackupStore.Load(backup); relative.Workspace.LastFolder = "C:relative";
        SessionStore.WriteObject(bad, relative, typeof(ViewerBackupDocument));
        ExpectBadBackup(bad, "drive-relative path rejected");
        var folderOnly = ViewerBackupStore.Load(backup);
        folderOnly.SavedSessions.Clear();
        folderOnly.Workspace.Tabs = new List<SessionTabDto>
        {
            new SessionTabDto { Id = "missing", Path = missing, FolderPath = photos },
            new SessionTabDto { Id = "folder", IsBrowser = true, FolderPath = photos, IsPinned = true }
        };
        folderOnly.Workspace.ActiveTabId = "missing";
        ViewerBackupStore.Save(bad, folderOnly);
        var loaded = ViewerBackupStore.Load(bad);
        Assert(loaded.Workspace.Tabs.Count == 1 && loaded.Workspace.Tabs[0].IsBrowser
            && loaded.Workspace.ActiveTabId == "folder", "import keeps folder-only tabs and selects surviving folder fallback");
        folderOnly.Workspace.Tabs.RemoveAt(1);
        ViewerBackupStore.Save(bad, folderOnly);
        loaded = ViewerBackupStore.Load(bad);
        Assert(loaded.Workspace.Tabs.Count == 0 && loaded.Workspace.ActiveTabId == null
            && loaded.Workspace.ActiveTabPath == null, "all missing images fall back to home browser");
        using (var stream = File.Create(bad)) stream.SetLength(65L * 1024 * 1024);
        ExpectBadBackup(bad, "oversized backup rejected before deserialization");
    }

    private static void ExpectBadBackup(string path, string message)
    {
        bool failed = false;
        try { ViewerBackupStore.Load(path); }
        catch (InvalidDataException) { failed = true; }
        Assert(failed, message);
    }

    private static void ExpectImportFailure(MainWindow window, string path, string active, int count)
    {
        Task task = (Task)Invoke(window, "ImportBackupFileAsync", path, false);
        Wait(delegate { return task.IsCompleted; }, "invalid backup rejected");
        Assert(task.IsFaulted, "invalid backup must fail validation");
        var error = task.Exception;
        Assert(error != null && Field<string>(window, "_activeTabId") == active
            && Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count == count
            && window.IsEnabled && !Field<bool>(window, "_backupBusy"), "invalid import leaves workspace intact and usable");
    }
}
