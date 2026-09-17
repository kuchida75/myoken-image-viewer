using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void CheckWorkflowBounds(MainWindow window)
    {
        Pump(); CheckSearchAlignment(window, false);
        var commands = Field<WrapPanel>(window, "_workflowCommands");
        foreach (FrameworkElement child in commands.Children)
        {
            if (!child.IsVisible) continue;
            AssertInside(child, commands);
            var group = child as Panel;
            if (group != null) foreach (FrameworkElement control in group.Children) if (control.IsVisible) AssertInside(control, commands);
        }
        var location = Field<FolderLocationBar>(window, "_folderLocation");
        if (location.IsVisible) AssertInside(location, Field<Grid>(window, "_browserView"));
    }

    private static void RunWorkflowChecks()
    {
        var map = new ShortcutMap(); string error;
        Assert(map.Match(Key.L, ModifierKeys.Control, true).Id == "folderAddress" && map.Match(Key.O, ModifierKeys.Control, false).Id == "openFolder", "new folder shortcut defaults");
        Assert(map.Load(new List<ShortcutOverride> { new ShortcutOverride { Action = "help", Keys = new List<ShortcutGesture> { ShortcutMap.Gesture(Key.L, ModifierKeys.Control) } } }, out error)
            && map.Keys("folderAddress").Count == 0 && map.Match(Key.L, ModifierKeys.Control, true).Id == "help", "existing custom Ctrl+L wins over new default");
        Assert(map.Load(new List<ShortcutOverride> { new ShortcutOverride { Action = "navRight", Keys = ShortcutKeys(Key.L) } }, out error)
            && map.Keys("folderAddress").Count == 0 && map.Match(Key.L, ModifierKeys.Control, true).Id == "navRight", "existing thumbnail selection modifiers also take precedence");
        string root = Path.Combine(Root, "workflow-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Summer photos"), child = Path.Combine(photos, "Weekend"), empty = Path.Combine(root, "Empty");
        Directory.CreateDirectory(child); Directory.CreateDirectory(empty);
        string a = Path.Combine(photos, "Garden-01.png"), b = Path.Combine(photos, "Garden-02.png");
        MakeImage(a, 1400, 920, 96); MakeImage(b, 900, 650, 96); MakeImage(Path.Combine(child, "Trip.png"), 380, 240, 96);
        var services = AppServices.CreateInstance(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1440, WindowHeight = 900 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            var fileButton = Field<Button>(window, "_fileMenuButton");
            var fileLabel = ((StackPanel)fileButton.Content).Children.OfType<TextBlock>().ToArray();
            Assert(fileLabel.Length == 3 && fileLabel[0].Text == "\uE8A5" && fileLabel[1].Text == "File"
                && fileLabel[2].Text == "\uE70D", "File uses document icon, label and trailing dropdown chevron");
            Assert(AutomationProperties.GetName(fileButton) == "File" && fileButton.ToolTip != null,
                "File button retains accessible name and description");
            Assert(Field<WrapPanel>(window, "_browserFileCommands").IsVisible && !Field<WrapPanel>(window, "_viewerZoomCommands").IsVisible
                && !Field<WrapPanel>(window, "_viewerEditCommands").IsVisible, "browser excludes image-only controls");
            Assert(!Field<StackPanel>(window, "_performanceDetails").IsVisible, "diagnostics begin collapsed");
            Assert(Field<ToggleButton>(window, "_enhanceButton").IsVisible, "global Quick Enhance remains accessible in browser");
            Field<Button>(window, "_advancedSearchButton").Focus();
            Field<Button>(window, "_advancedSearchButton").MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Assert(Field<ToggleButton>(window, "_enhanceButton").IsKeyboardFocused, "Tab follows global commands with contextual tools in visual order");
            CheckWorkflowBounds(window); Render((FrameworkElement)window.Content, "workflow-browser-dark.png");
            window.Width = 980; CheckWorkflowBounds(window); Render((FrameworkElement)window.Content, "workflow-browser-narrow.png");
            var search = Field<TextBox>(window, "_searchBox"); search.Text = "Garden-01"; Invoke(window, "ApplySearch");
            Assert(Field<List<ImageFileItem>>(window, "_filteredItems").Count == 1, "filename filtering works");
            Field<Button>(window, "_clearSearchButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
            Assert(search.Text == "" && Field<List<ImageFileItem>>(window, "_filteredItems").Count == 3, "clear filter restores all images and folders");
            search.Text = "Garden"; search.Focus(); PressFocusedKey(Key.Escape); Pump();
            Assert(search.Text == "", "Escape clears focused filename filter");
            Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid").Focus();
            Assert((bool)Invoke(window, "RunShortcut", Key.L, ModifierKeys.Control, false), "address shortcut handled");
            var location = Field<FolderLocationBar>(window, "_folderLocation");
            Assert(location.IsEditing && location.Address.IsKeyboardFocused, "address shortcut focuses editor");
            location.Address.Text = Path.Combine(root, "missing"); Assert(!location.Commit() && Field<string>(window, "_currentFolder") == photos, "invalid address does not replace current folder");
            Render((FrameworkElement)window.Content, "workflow-address-error.png");
            location.Address.Text = "Weekend"; Assert(location.Commit(), "relative folder address opens"); WaitScan(window);
            Assert(Field<string>(window, "_currentFolder") == child, "relative address resolves from current folder");
            Invoke(window, "FocusFolderAddress"); location.Address.Text = photos; Assert(location.Commit(), "absolute address opens"); WaitScan(window);
            Invoke(window, "FocusFolderAddress"); location.Address.Text = empty; PressFocusedKey(Key.Escape); Pump();
            Assert(!location.IsEditing && Field<string>(window, "_currentFolder") == photos, "Escape discards uncommitted folder address");
            Invoke(window, "FocusFolderAddress");
            Invoke(window, "OpenImageTab", a, true); WaitGpuImage(window, a); CheckWorkflowBounds(window);
            Assert(!location.IsEditing, "opening an image cancels stale address editing");
            Assert(Field<WrapPanel>(window, "_viewerZoomCommands").IsVisible && Field<WrapPanel>(window, "_viewerEditCommands").IsVisible
                && !Field<WrapPanel>(window, "_browserFileCommands").IsVisible, "viewer exposes navigation/zoom/editing and keeps file menu");
            Assert((string)Invoke(window, "WorkflowPath") == a, "file utility targets displayed image");
            var fileMenu = (ContextMenu)Invoke(window, "BuildFileActionsMenu");
            Assert(fileMenu.Items.OfType<MenuItem>().Any(m => (string)m.Header == "Copy full path" && m.IsEnabled)
                && fileMenu.Items.OfType<MenuItem>().Any(m => (string)m.Header == "Show in Explorer" && m.IsEnabled), "file utilities are available without launching Explorer or changing clipboard in tests");
            fileMenu.PlacementTarget = Field<Button>(window, "_fileMenuButton"); fileMenu.IsOpen = true; Pump(); Render(fileMenu, "workflow-file-menu.png"); fileMenu.IsOpen = false;
            Render((FrameworkElement)window.Content, "workflow-viewer-narrow.png");
            window.Width = 1440; CheckWorkflowBounds(window); Render((FrameworkElement)window.Content, "workflow-viewer-dark.png");
            ThemeManager.SetDarkTheme(false); CheckWorkflowBounds(window); Render((FrameworkElement)window.Content, "workflow-viewer-light.png"); ThemeManager.SetDarkTheme(true);
            Field<ToggleButton>(window, "_performanceToggle").IsChecked = true; Pump();
            Assert(Field<StackPanel>(window, "_performanceDetails").IsVisible && Capture(window).PerformanceDetailsVisible, "performance disclosure captures preference");
            Invoke(window, "ShowConfigure"); var configure = Field<Window>(window, "_configureWindow");
            var settingsSearch = Field<TextBox>(window, "_configurationSearch"); settingsSearch.Text = "VRAM"; Pump();
            Assert(Field<Slider>(window, "_imageGpuBudget").IsVisible && !Field<Slider>(window, "_thumbnailSizeSlider").IsVisible, "settings search finds matching controls across pages");
            Render((FrameworkElement)configure.Content, "workflow-settings-search.png");
            settingsSearch.Text = "slideshow"; Pump(); Assert(Field<ToggleButton>(window, "_slideshowButton").IsVisible, "settings search finds slideshow controls");
            settingsSearch.Text = "zz-no-such-setting"; Pump(); Assert(Field<TextBlock>(window, "_configurationNoResults").IsVisible, "settings search handles no results");
            Invoke(window, "SelectConfigurationPage", 0); Pump(); Assert(settingsSearch.Text == "" && Field<CheckBox>(window, "_darkThemeCheckBox").IsVisible, "category selection clears search");
            Render((FrameworkElement)configure.Content, "workflow-settings-appearance.png");
            Invoke(window, "SelectConfigurationPage", 2); Pump(); Render((FrameworkElement)configure.Content, "workflow-settings-performance.png");
            configure.Width = 760; configure.Height = 480; Pump();
            AssertInside(settingsSearch, (FrameworkElement)configure.Content);
            foreach (var button in Field<List<ToggleButton>>(window, "_configurationNavigation")) AssertInside(button, (FrameworkElement)configure.Content);
            Render((FrameworkElement)configure.Content, "workflow-settings-narrow.png");
            configure.Close(); Pump();
            var views = (ContextMenu)Invoke(window, "BuildViewOptionsMenu");
            views.Items.OfType<MenuItem>().Single(m => (string)m.Header == "Folder tree").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            Assert(!Field<FolderNavigationPane>(window, "_folderNavigation").IsVisible, "View menu updates panel state");
            Invoke(window, "ToggleFolderTree");
            Invoke(window, "SetCompactMode", true); Pump(); Assert(!Field<FrameworkElement>(window, "_performanceBar").IsVisible, "compact mode hides diagnostic chrome");
            Invoke(window, "RunShortcut", Key.L, ModifierKeys.Control, false); WaitScan(window); Pump();
            Assert(location.IsEditing && location.Address.IsKeyboardFocused && !Field<bool>(window, "_isCompactMode"), "address shortcut exits compact and viewer modes without closing tab");
            location.CancelEdit(); Invoke(window, "OpenBrowserImage", a); WaitGpuImage(window, a);
        }
        finally { window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true); }
        var reopenedServices = AppServices.CreateInstance(Path.Combine(root, "Profile"));
        var reopened = new MainWindow(reopenedServices); reopened.Show();
        try
        {
            WaitGpuImage(reopened, a);
            Assert(Field<ToggleButton>(reopened, "_performanceToggle").IsChecked == true, "performance disclosure and image tab restore");
        }
        finally { reopened.Close(); Pump(); reopenedServices.Dispose(); }
        Console.WriteLine("PASS: contextual commands, wide/narrow dark/light layout, breadcrumbs/address validation/cancel, search clear, file utilities, View menu, searchable settings, shortcut migration, compact mode and persistence.");
    }
}
