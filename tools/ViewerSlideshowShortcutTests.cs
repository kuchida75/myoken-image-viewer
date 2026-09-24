using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static List<ShortcutGesture> ShortcutKeys(Key key, ModifierKeys modifiers = ModifierKeys.None)
    { return new List<ShortcutGesture> { ShortcutMap.Gesture(key, modifiers) }; }

    private static void RunShortcutMapChecks()
    {
        var map = new ShortcutMap(); string error;
        Assert(map.Load(null, out error), "all default shortcuts are valid and non-conflicting: " + error);
        Assert(map.Display("compact") == "U" && map.Display("fitHeight") == "H" && map.Display("fitWidth") == "W",
            "new compact and axis-fit defaults");
        Assert(map.Match(Key.Right, ModifierKeys.None, true).Id == "navRight"
            && map.Match(Key.Right, ModifierKeys.None, false).Id == "next", "same key has view-specific action");
        Assert(map.Match(Key.Up, ModifierKeys.None, false).Id == "previous"
            && map.Match(Key.Down, ModifierKeys.None, false).Id == "next"
            && map.Match(Key.Up, ModifierKeys.None, true).Id == "navUp"
            && map.Match(Key.Down, ModifierKeys.None, true).Id == "navDown", "arrow navigation remains view-specific");
        var older = new List<ShortcutOverride>
        {
            new ShortcutOverride { Action = "compact", Keys = ShortcutKeys(Key.H) },
            new ShortcutOverride { Action = "upscale", Keys = ShortcutKeys(Key.U) },
            new ShortcutOverride { Action = "manualEnhance", Keys = ShortcutKeys(Key.W) },
            new ShortcutOverride { Action = "metadata", Keys = ShortcutKeys(Key.Down) }
        };
        Assert(map.Load(older, out error), "older custom bindings load across new defaults: " + error);
        Assert(map.Display("compact") == "H" && map.Display("fitHeight") == "Unassigned"
            && map.Display("fitWidth") == "Unassigned" && map.Match(Key.U, ModifierKeys.None, false).Id == "upscale"
            && map.Match(Key.Down, ModifierKeys.None, false).Id == "metadata"
            && map.Match(Key.Right, ModifierKeys.None, false).Id == "next", "saved custom keys take priority without losing unrelated defaults");
        map.ResetAll();
        Assert(map.Match(Key.Right, ModifierKeys.Control | ModifierKeys.Shift, true).Id == "navRight", "selection modifiers preserved");
        Assert(!map.Set("metadata", ShortcutKeys(Key.J), out error) && error.Contains("conflicts"), "global/image conflict rejected");
        Assert(!map.Set("help", ShortcutKeys(Key.Right, ModifierKeys.Control), out error), "selection variant conflict rejected");
        Assert(map.Set("metadata", ShortcutKeys(Key.T), out error), "different-view assignment allowed");
        Assert(map.Match(Key.I, ModifierKeys.None, false) == null, "replaced shortcut no longer fires");
        foreach (var gesture in new[] { ShortcutMap.Gesture(Key.Escape, ModifierKeys.None), ShortcutMap.Gesture(Key.Tab, ModifierKeys.None),
            ShortcutMap.Gesture(Key.F4, ModifierKeys.Alt), ShortcutMap.Gesture(Key.Space, ModifierKeys.Alt),
            ShortcutMap.Gesture(Key.L, ModifierKeys.Windows), ShortcutMap.Gesture(Key.Delete, ModifierKeys.Control | ModifierKeys.Alt),
            new ShortcutGesture { KeyCode = Int32.MaxValue } })
            Assert(!map.Set("help", new List<ShortcutGesture> { gesture }, out error), "reserved/invalid keys rejected " + gesture.KeyCode);
        Assert(!map.Set("help", null, out error), "null key list rejected safely");
        Assert(!map.Load(new List<ShortcutOverride> { new ShortcutOverride { Action = "missing", Keys = new List<ShortcutGesture>() } }, out error), "unknown command rejected");
        Assert(map.Match(Key.T, ModifierKeys.None, false).Id == "metadata", "failed load is atomic");
        Assert(map.Set("metadata", new List<ShortcutGesture>(), out error) && map.Display("metadata") == "Unassigned", "unassignment supported");
        var saved = map.Export(); var copy = new ShortcutMap(); Assert(copy.Load(saved, out error), "round-trip shortcut map");
        saved[0].Keys.Add(ShortcutMap.Gesture(Key.F10, ModifierKeys.None));
        Assert(copy.Display("metadata") == "Unassigned", "restored assignments do not alias imported lists");
        map.ResetAll(); Assert(map.Match(Key.I, ModifierKeys.None, false).Id == "metadata", "reset defaults");
        var sequence = new SlideshowSequence(new[] { "a", "b", "c" }, false, "a");
        Assert(sequence.Previous() == "c" && sequence.Next() == "a" && sequence.Position == 1, "previous wraps and position follows cycle");
        sequence = new SlideshowSequence(new[] { "a", "b", "c" }, true, "a");
        sequence.Next(); string last = sequence.Next(), next = sequence.Next();
        Assert(sequence.Previous() == last && sequence.Next() == next, "shuffle backtracking preserves previous cycle");
    }

    private static Button SlideshowButton(MainWindow window, string name)
    {
        var content = (StackPanel)Field<Border>(window, "_slideshowOverlay").Child;
        return ((WrapPanel)content.Children[0]).Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
    }

    private static void CheckSlideshowOverlayLayout(MainWindow window)
    {
        window.UpdateLayout(); var overlay = Field<Border>(window, "_slideshowOverlay"); var view = Field<Grid>(window, "_imageView");
        Assert(overlay.IsVisible, "active slideshow overlay remains visible"); AssertInside(overlay, view);
        Point location = overlay.TranslatePoint(new Point(), view);
        AssertNear(location.X + overlay.ActualWidth, view.ActualWidth - 16, "slideshow right inset");
        AssertNear(location.Y + overlay.ActualHeight, view.ActualHeight - 16, "slideshow bottom inset");
        var stack = (StackPanel)overlay.Child;
        foreach (UIElement child in ((WrapPanel)stack.Children[0]).Children) AssertInside((FrameworkElement)child, overlay);
        var metadata = Field<Border>(window, "_imageMetadataOverlay");
        if (metadata.IsVisible)
        {
            AssertInside(metadata, view);
            Rect left = new Rect(metadata.TranslatePoint(new Point(), view), metadata.RenderSize);
            Rect right = new Rect(location, overlay.RenderSize);
            Assert(!left.IntersectsWith(right), "metadata and slideshow controls never overlap");
        }
        byte alpha = ((SolidColorBrush)overlay.Background).Color.A;
        Assert(alpha > 0 && alpha < 255, "slideshow panel is translucent");
    }

    private static void RunSlideshowShortcutChecks()
    {
        RunShortcutMapChecks();
        string root = Path.Combine(Root, "slideshow-shortcuts-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string a = Path.Combine(photos, "A.png"), b = Path.Combine(photos, "B.png"), c = Path.Combine(photos, "C.png");
        MakeImage(a, 1200, 780, 96); MakeImage(b, 720, 480, 96); MakeImage(c, 960, 640, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1440, WindowHeight = 820, SlideshowSeconds = 30 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        var overlay = Field<Border>(window, "_slideshowOverlay"); var timer = Field<DispatcherTimer>(window, "_slideshowTimer");
        Assert(overlay.Visibility == Visibility.Collapsed, "slideshow initially hidden");
        Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid").Focus(); PressKey(window, Key.F5);
        Wait(delegate { return Ready(window, a); }, "slideshow first image");
        Assert(Field<bool>(window, "_slideshowPlaying") && !Field<bool>(window, "_isFullscreen"), "F5 starts windowed slideshow");
        CheckSlideshowOverlayLayout(window);
        Invoke(window, "ToggleSlideshowPause"); Assert(!timer.IsEnabled && Field<bool>(window, "_slideshowPaused"), "pause stops timer");
        PumpZoomFor(650); Assert(Ready(window, a) && overlay.IsVisible, "paused controls stay visible");
        SlideshowButton(window, "Next slide").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Wait(delegate { return Ready(window, b); }, "paused next slide");
        Assert(!timer.IsEnabled && Field<bool>(window, "_slideshowPlaying"), "manual skip retains paused slideshow");
        SlideshowButton(window, "Previous slide").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Wait(delegate { return Ready(window, a); }, "paused previous slide");
        Invoke(window, "MoveSlideshow", -1); Wait(delegate { return Ready(window, c); }, "previous slide wraps");
        Invoke(window, "ToggleSlideshowShuffle"); Assert(Field<CheckBox>(window, "_slideshowShuffle").IsChecked == true, "overlay shuffle shares saved setting");
        Assert(AutomationProperties.GetItemStatus(Field<Button>(window, "_slideshowShuffleButton")) == "On", "shuffle has accessible state");
        var interval = Field<TextBox>(window, "_slideshowOverlayInterval"); interval.Focus(); interval.Text = "1.5";
        interval.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent }); Pump();
        Assert(Field<double>(window, "_slideshowSeconds") == 1.5 && Field<bool>(window, "_slideshowPaused"), "interval Enter commits without leaving image or resuming pause");
        Invoke(window, "ToggleSlideshowPause"); Assert(timer.IsEnabled, "resume re-arms timer");
        interval.Focus(); interval.Text = "NaN"; Assert(!timer.IsEnabled, "editing interval suspends timer");
        Field<Canvas>(window, "_imageCanvas").Focus(); Pump();
        Assert(interval.Text == "1.5" && timer.IsEnabled, "invalid interval restores prior value and resumes");
        Invoke(window, "ToggleSlideshowPause"); Invoke(window, "SetImageMetadataOverlay", true); Pump();
        CheckSlideshowOverlayLayout(window); Render((FrameworkElement)window.Content, "slideshow-overlay-dark.png");
        window.Width = 980; Pump(); CheckSlideshowOverlayLayout(window);
        Render((FrameworkElement)window.Content, "slideshow-overlay-narrow.png");
        ThemeManager.SetDarkTheme(false); Pump(); CheckSlideshowOverlayLayout(window);
        Render((FrameworkElement)window.Content, "slideshow-overlay-light.png");
        Invoke(window, "SetCompactMode", true); Pump(); CheckSlideshowOverlayLayout(window);
        Render((FrameworkElement)window.Content, "slideshow-overlay-compact.png");
        var pause = Field<Button>(window, "_slideshowPauseButton"); pause.Focus();
        Assert(!(bool)Invoke(window, "RunShortcut", Key.Space, ModifierKeys.None, false), "focused overlay buttons keep native Space activation");
        SlideshowButton(window, "Stop slideshow").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Assert(!Field<bool>(window, "_slideshowPlaying") && overlay.Visibility == Visibility.Collapsed
            && Field<Grid>(window, "_imageView").IsVisible, "stop hides controls but keeps image");
        Invoke(window, "SetCompactMode", false); ThemeManager.SetDarkTheme(true);

        var map = Field<ShortcutMap>(window, "_shortcuts"); string error;
        Invoke(window, "ShowConfigure"); Invoke(window, "SelectConfigurationPage", 5); Pump();
        var config = Field<Window>(window, "_configureWindow");
        var commands = Field<ComboBox>(window, "_shortcutCommand");
        commands.SelectedItem = map.Commands.Single(d => d.Id == "browse");
        Invoke(window, "BeginShortcutCapture", true); PressKey(config, Key.K); Pump();
        Assert(map.Display("browse") == "K", "editor records replacement through routed key event");
        Invoke(window, "BeginShortcutCapture", false); PressKey(config, Key.J);
        Assert(Field<TextBlock>(window, "_shortcutStatus").Text.Contains("conflicts") && map.Keys("browse").Count == 1, "capture reports conflict without changing assignments");
        PressKey(config, Key.Escape); Assert(config.IsVisible && Field<int>(window, "_shortcutCaptureIndex") == -2, "Escape cancels capture without closing Configure");
        Invoke(window, "BeginShortcutCapture", false); Assert((bool)Invoke(window, "CaptureShortcut", Key.B, ModifierKeys.Control | ModifierKeys.Shift, false), "modifier capture handled");
        Assert(map.Keys("browse").Count == 2, "additional alias recorded");
        Field<ComboBox>(window, "_shortcutKeys").SelectedIndex = 1; Invoke(window, "RemoveShortcut");
        Assert(map.Keys("browse").Count == 1, "remove alias");
        config.Width = 570; Pump(); Render((FrameworkElement)config.Content, "shortcuts-configure-dark.png");
        foreach (var button in Field<List<System.Windows.Controls.Primitives.ToggleButton>>(window, "_configurationNavigation")) AssertInside(button, (FrameworkElement)config.Content);
        ThemeManager.SetDarkTheme(false); Pump(); Render((FrameworkElement)config.Content, "shortcuts-configure-light.png");
        config.Close(); Pump();
        Field<Canvas>(window, "_imageCanvas").Focus(); PressKey(window, Key.Enter);
        Assert(Field<Grid>(window, "_imageView").IsVisible, "old Enter binding removed");
        PressKey(window, Key.K); WaitScan(window); Assert(Field<Grid>(window, "_browserView").IsVisible, "replacement exits image to same browser tab");
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid"); grid.Focus(); PressKey(window, Key.Home);
        Assert(grid.SelectedItem.Path == a, "browser Home remains scoped");
        Assert(!map.Set("navRight", ShortcutKeys(Key.L), out error) && error.Contains("conflicts"), "selection modifiers respect new Ctrl+L address binding");
        Assert(map.Set("folderAddress", new List<ShortcutGesture>(), out error), "address binding can be cleared for a custom navigation key");
        Assert(map.Set("navRight", ShortcutKeys(Key.L), out error), "custom thumbnail movement");
        PressKey(window, Key.Right); Assert(grid.SelectedItem.Path == a, "old browser navigation binding inactive");
        PressKey(window, Key.L); Assert(grid.SelectedItem.Path == b && Field<Grid>(window, "_browserView").IsVisible, "custom arrow-equivalent selects without opening");
        Assert(map.Match(Key.L, ModifierKeys.Shift, true).Id == "navRight", "custom movement supports range selection");
        Field<TextBox>(window, "_searchBox").Focus(); Assert(!(bool)Invoke(window, "RunShortcut", Key.L, ModifierKeys.None, false), "custom character leaves text entry intact");
        grid.Focus(); Assert(map.Set("metadata", ShortcutKeys(Key.M, ModifierKeys.Control), out error), "modifier binding persisted");
        Invoke(window, "OpenBrowserImage", b); Wait(delegate { return Ready(window, b); }, "custom image binding fixture");
        Field<Canvas>(window, "_imageCanvas").Focus(); bool metadata = Field<bool>(window, "_imageMetadataOverlayEnabled");
        Invoke(window, "RunShortcut", Key.M, ModifierKeys.Control, false);
        Assert(Field<bool>(window, "_imageMetadataOverlayEnabled") != metadata, "custom Ctrl binding dispatches");
        Invoke(window, "RunShortcut", Key.M, ModifierKeys.Control, true);
        Assert(Field<bool>(window, "_imageMetadataOverlayEnabled") != metadata, "held custom toggle does not repeat");
        Assert(map.Set("help", ShortcutKeys(Key.Q), out error), "letter-only global shortcut");
        Field<TextBox>(window, "_searchBox").Focus();
        Assert(!(bool)Invoke(window, "RunShortcut", Key.Q, ModifierKeys.None, false), "letter-only global shortcuts do not interrupt typing");
        Field<Canvas>(window, "_imageCanvas").Focus();
        Assert(map.Set("help", ShortcutKeys(Key.F8), out error), "rebind help"); Invoke(window, "ShortcutsChanged");
        var helpButton = Field<Button>(window, "_helpButton");
        CommandPresentation.Describe(helpButton, "Help", "F1", "Keyboard shortcuts and history.");
        Assert(AutomationProperties.GetAcceleratorKey(helpButton) == "F8", "tooltips reflect current per-window assignment");
        Invoke(window, "ShowHelp"); var help = Field<HelpWindow>(window, "_helpWindow");
        var text = Field<RichTextBox>(help, "_shortcutText");
        var helpRow = text.Document.Blocks.OfType<Table>().First().RowGroups[0].Rows
            .First(row => new TextRange(row.Cells[1].ContentStart, row.Cells[1].ContentEnd).Text.StartsWith("Help "));
        Assert(new TextRange(helpRow.Cells[0].ContentStart, helpRow.Cells[0].ContentEnd).Text.Trim() == "F8", "Help lists current assignments");
        help.Close(); window.Activate(); Field<Canvas>(window, "_imageCanvas").Focus();
        Invoke(window, "StartSlideshow"); Wait(delegate { return overlay.IsVisible; }, "slideshow restart");
        PressKey(window, Key.Escape); WaitScan(window);
        Assert(!timer.IsEnabled && overlay.Visibility == Visibility.Collapsed && Field<Grid>(window, "_browserView").IsVisible, "fixed Escape remains reliable after customization");

        services.Sessions.SaveNamed("Custom shortcuts", Capture(window));
        var document = (ViewerBackupDocument)Invoke(window, "CaptureBackupSnapshot");
        string backup = Path.Combine(root, "shortcut-backup.json"); ViewerBackupStore.Save(backup, document);
        var loaded = ViewerBackupStore.Load(backup); var restored = new ShortcutMap();
        Assert(restored.Load(loaded.Workspace.ShortcutOverrides, out error) && restored.Display("help") == "F8", "backup preserves shortcuts");
        loaded.Workspace.ShortcutOverrides.Add(new ShortcutOverride { Action = "unknown", Keys = new List<ShortcutGesture>() });
        bool rejected = false; try { ViewerBackupStore.Save(Path.Combine(root, "invalid.json"), loaded); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "invalid backup shortcut rejected");
        var otherServices = AppServices.Create(Path.Combine(root, "OtherProfile"));
        otherServices.Sessions.Save(new SessionState { LastFolder = photos });
        var other = new MainWindow(otherServices); other.Show(); WaitScan(other);
        Assert(Field<ShortcutMap>(other, "_shortcuts").Display("help") == "F1", "key assignments isolated between windows");
        other.Close(); Pump(); otherServices.Dispose();
        window.Close(); Pump(); window = new MainWindow(services); window.Show(); WaitScan(window);
        Assert(Field<ShortcutMap>(window, "_shortcuts").Display("help") == "F8"
            && Field<ShortcutMap>(window, "_shortcuts").Display("metadata") == "Ctrl+M", "automatic session restores custom keys");
        Assert(!Field<bool>(window, "_slideshowPlaying") && Field<Border>(window, "_slideshowOverlay").Visibility == Visibility.Collapsed, "restore never auto-starts playback");
        Invoke(window, "ApplySessionState", new SessionState { LastFolder = photos }, false); WaitScan(window);
        Assert(Field<ShortcutMap>(window, "_shortcuts").Display("help") == "F1", "old sessions restore default bindings");
        Invoke(window, "ApplySessionState", services.Sessions.LoadNamed("Custom shortcuts"), false); WaitScan(window);
        Assert(Field<ShortcutMap>(window, "_shortcuts").Display("help") == "F8", "named session restores shortcut preferences");
        window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true);
        Console.WriteLine("PASS: persistent translucent slideshow controls, pause/skip/interval/layout, configurable keyboard routing, conflicts, capture, hints and session/backup isolation.");
    }
}
