using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunForwardAndExitChecks()
    {
        ImageView fit = ImageViewport.Fit(new Size(160, 90), new Size(1000, 700));
        Assert(fit.Zoom == 1 && fit.X == 420 && fit.Y == 305, "small image fits centered without enlargement");
        ImageView restored = ImageViewport.Restore(new Size(160, 90), new Size(1000, 700),
            new ImageTabState { HasCustomView = true, Zoom = 2, OffsetX = 340, OffsetY = 260 });
        Assert(restored.Zoom == 2, "manual zoom may still enlarge small images");

        string root = Path.Combine(Root, "forward-exit");
        string branch = Path.Combine(root, "Branch"), leaf = Path.Combine(branch, "Leaf"), other = Path.Combine(root, "Other");
        Directory.CreateDirectory(leaf);
        Directory.CreateDirectory(other);
        string small = Path.Combine(leaf, "Small-300dpi.png");
        MakeImage(small, 160, 90, 300);
        var services = AppServices.Create(Path.Combine(Root, "forward-exit-profile"));
        services.Sessions.Save(new SessionState { LastFolder = leaf, WindowWidth = 1240, WindowHeight = 780 });
        var window = new MainWindow(services);
        window.Show();
        WaitScan(window);
        PressMouseNavigation(window, MouseButton.XButton1);
        Assert(Field<string>(window, "_currentFolder") == branch, "Back goes one level up");
        PressMouseNavigation(window, MouseButton.XButton1);
        Assert(Field<string>(window, "_currentFolder") == root, "second Back goes another level up");
        Assert(Field<ImageTabState>(window, "_homeBrowser").ForwardFolders.SequenceEqual(new[] { leaf, branch }), "Forward stack retains nested path");
        Invoke(window, "DuplicateTab", new object[] { null });
        WaitScan(window);
        string copyId = Field<string>(window, "_activeTabId");
        PressMouseNavigation(window, MouseButton.XButton2);
        Assert(Field<string>(window, "_currentFolder") == branch, "Forward returns to immediate child");
        PressMouseNavigation(window, MouseButton.XButton2);
        Assert(Field<string>(window, "_currentFolder") == leaf, "second Forward returns to grandchild");
        var states = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
        Assert(states[copyId].ForwardFolders.Count == 0 && Field<ImageTabState>(window, "_homeBrowser").ForwardFolders.Count == 2,
            "duplicated tab histories are independent");
        Invoke(window, "ShowBrowser");
        WaitScan(window);
        PressKey(window, Key.BrowserForward);
        WaitScan(window);
        Assert(Field<string>(window, "_currentFolder") == branch, "browser Forward key follows home history");
        SessionState saved = Capture(window);
        services.Sessions.SaveNamed("Forward path", saved);
        Assert(services.Sessions.LoadNamed("Forward path").ForwardFolders.SequenceEqual(new[] { leaf }), "named session stores Forward history");
        window.Close();
        Pump();
        window = new MainWindow(services);
        window.Show();
        WaitScan(window);
        PressMouseNavigation(window, MouseButton.XButton2);
        Assert(Field<string>(window, "_currentFolder") == leaf, "Forward history survives restart");
        Invoke(window, "LoadFolder", root);
        WaitScan(window);
        Assert(Field<ImageTabState>(window, "_homeBrowser").ForwardFolders.Count == 0, "explicit folder navigation clears Forward history");
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        grid.SelectItem(Field<List<ImageFileItem>>(window, "_allItems").Single(item => item.Path == other));
        PressMouseNavigation(window, MouseButton.XButton2);
        Assert(Field<string>(window, "_currentFolder") == other, "Forward without history opens selected subfolder");
        PressMouseNavigation(window, MouseButton.XButton2);
        Assert(Field<string>(window, "_currentFolder") == other, "Forward at an empty folder is harmless");
        var home = Field<ImageTabState>(window, "_homeBrowser");
        home.ForwardFolders.Add(Path.Combine(other, "Missing-" + Guid.NewGuid().ToString("N")));
        home.ForwardFolders.Add(leaf);
        PressMouseNavigation(window, MouseButton.XButton2);
        Assert(Field<string>(window, "_currentFolder") == other && home.ForwardFolders.Count == 0, "invalid and missing children cannot navigate outside current folder");

        Invoke(window, "OpenImageTab", small, true);
        Wait(delegate { return Ready(window, small); }, "small image");
        WaitScan(window);
        var scale = Field<ScaleTransform>(window, "_imageScale");
        Assert(scale.ScaleX == 1, "small image defaults to 100 percent");
        CheckCentered(window);
        var image = Field<Image>(window, "_mainImage");
        var canvas = Field<Canvas>(window, "_imageCanvas");
        Rect bounds = image.TransformToAncestor(canvas).TransformBounds(new Rect(image.RenderSize));
        Assert(bounds.Width == 160 && bounds.Height == 90, "small image keeps pixel dimensions regardless of print DPI");
        Render((FrameworkElement)window.Content, "small-image-native-size.png");
        window.Width = 980;
        window.Height = 640;
        Invoke(window, "SetMetadataPanelVisible", true);
        Pump();
        Assert(scale.ScaleX == 1, "resizing does not enlarge a small fitted image");
        Invoke(window, "RotateImage", 1);
        Pump();
        Assert(scale.ScaleX == 1, "rotating a small image does not enlarge it");
        CheckCentered(window);
        Invoke(window, "ZoomFromCenter", 10.0);
        Pump();
        Assert(scale.ScaleX == 10, "manual enlargement remains available");
        Assert((bool)Invoke(window, "BeginImagePan", new Point(100, 100)), "enlarged small image can pan");
        string id = Field<string>(window, "_activeTabId");
        int count = Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count;
        PressKey(window, Key.Escape);
        WaitScan(window);
        Assert(Field<string>(window, "_activeTabId") == id && Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count == count,
            "Escape returns to browser without closing or replacing tab");
        Assert(Field<Grid>(window, "_browserView").Visibility == Visibility.Visible && image.Source == null && !canvas.IsMouseCaptured,
            "Escape releases image and active pan");
        Assert(grid.SelectedItem != null && grid.SelectedItem.Path == small, "Escape selects original image in browser");
        Invoke(window, "OpenBrowserImage", small);
        Wait(delegate { return Ready(window, small); }, "reopen after Escape");
        Assert(scale.ScaleX == 10, "custom zoom preserved when leaving and returning");
        Invoke(window, "FitImageToView", true);
        Assert(scale.ScaleX == 1, "Fit resets manual enlargement to original size");
        Invoke(window, "ToggleFullscreen");
        Pump();
        Assert(scale.ScaleX == 1, "fullscreen fit still does not upscale");
        PressKey(window, Key.Escape);
        WaitScan(window);
        Assert(!Field<bool>(window, "_isFullscreen") && Field<Grid>(window, "_browserView").Visibility == Visibility.Visible,
            "one Escape leaves fullscreen and image mode");
        Invoke(window, "OpenBrowserImage", small);
        Wait(delegate { return Ready(window, small); }, "Escape with search focused");
        Field<TextBox>(window, "_searchBox").Focus();
        PressKey(window, Key.Escape);
        WaitScan(window);
        Assert(Field<Grid>(window, "_browserView").Visibility == Visibility.Visible, "Escape exits image mode even with search focus");
        Keyboard.ClearFocus();
        window.Close();
        Pump();
        window = new MainWindow(services);
        window.Show();
        WaitScan(window);
        Assert(Field<string>(window, "_activeTabId") == id && Field<Grid>(window, "_browserView").Visibility == Visibility.Visible,
            "browser mode after Escape is restored on restart");
        Invoke(window, "OpenBrowserImage", small);
        Wait(delegate { return Ready(window, small); }, "restart small image");
        Assert(Field<ScaleTransform>(window, "_imageScale").ScaleX == 1, "default small-image fit preserved on restart");
        Invoke(window, "ZoomFromCenter", 10.0);
        Assert((bool)Invoke(window, "BeginImagePan", new Point(100, 100)), "drag before Enter");
        PressKey(window, Key.Enter);
        WaitScan(window);
        Assert(Field<string>(window, "_activeTabId") == id && Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count == count,
            "Enter returns to browser in the same tab");
        Assert(Field<Grid>(window, "_browserView").Visibility == Visibility.Visible
            && Field<Image>(window, "_mainImage").Source == null && !Field<Canvas>(window, "_imageCanvas").IsMouseCaptured,
            "Enter releases image and active drag");
        Assert(Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid").SelectedItem.Path == small, "Enter selects viewed image");
        PressKey(window, Key.Enter);
        Wait(delegate { return Ready(window, small); }, "Enter opens selected image from browser");
        Assert(Field<ScaleTransform>(window, "_imageScale").ScaleX == 10, "Enter round trip preserves custom zoom");
        Field<TextBox>(window, "_searchBox").Focus();
        PressKey(window, Key.Enter);
        Assert(Field<Grid>(window, "_imageView").Visibility == Visibility.Visible, "Enter in text input does not navigate");
        Keyboard.ClearFocus();
        Invoke(window, "ToggleFullscreen");
        Pump();
        PressKey(window, Key.Enter);
        WaitScan(window);
        Assert(!Field<bool>(window, "_isFullscreen") && Field<Grid>(window, "_browserView").Visibility == Visibility.Visible,
            "Enter leaves fullscreen and image mode together");
        Invoke(window, "LoadFolder", branch);
        WaitScan(window);
        Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid").SelectItem(
            Field<List<ImageFileItem>>(window, "_allItems").Single(item => item.Path == leaf));
        PressKey(window, Key.Enter);
        WaitScan(window);
        Assert(Field<string>(window, "_currentFolder") == leaf, "Enter still opens selected folder");
        window.Close();
        Pump();
        window = new MainWindow(services);
        window.Show();
        WaitScan(window);
        Assert(Field<string>(window, "_activeTabId") == id && Field<Grid>(window, "_browserView").Visibility == Visibility.Visible,
            "browser state after Enter survives restart");
        window.Close();
        Pump();
    }

    private static void PressMouseNavigation(MainWindow window, MouseButton button)
    {
        var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button) { RoutedEvent = Mouse.PreviewMouseDownEvent };
        Field<Grid>(window, "_browserView").RaiseEvent(e);
        WaitScan(window);
        Assert(e.Handled, "navigation mouse button is handled");
    }
}
