using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunThumbnailNavigationChecks()
    {
        string root = Path.Combine(Root, "tn-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        string photos = Path.Combine(root, "Photos");
        string folder = Path.Combine(photos, "Album A");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(photos, "Album B"));
        string first = Path.Combine(photos, "Image001.png"), middle = Path.Combine(photos, "Image040.png");
        string next = Path.Combine(photos, "Image041.png"), last = Path.Combine(photos, "Image096.png");
        MakeImage(first, 640, 400, 96);
        for (int i = 2; i <= 96; i++) File.Copy(first, Path.Combine(photos, "Image" + i.ToString("000") + ".png"));
        byte[] original = File.ReadAllBytes(first);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 780 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        IList<ImageFileItem> items = Field<IList<ImageFileItem>>(grid, "_items");
        Assert(items.Count == 98 && items[0].IsDirectory && items[1].IsDirectory, "thumbnail fixture includes folder tiles");
        Invoke(window, "OpenBrowserImage", middle); Wait(delegate { return Ready(window, middle); }, "thumbnail return starting image");
        WaitScan(window); window.Focus();
        string id = Field<string>(window, "_activeTabId");
        int tabCount = Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count;
        PressKey(window, Key.Enter); Pump();
        AssertBrowserSelection(window, middle, "Enter returns to current thumbnail");
        Assert(grid.IsKeyboardFocusWithin, "return to browser gives thumbnail keyboard focus");
        AssertThumbnailVisible(grid);
        int loadVersion = Field<int>(window, "_imageLoadVersion");
        PressKey(window, Key.Right); Pump();
        AssertBrowserSelection(window, next, "Right after Enter selects next thumbnail without opening image");
        Assert(Field<int>(window, "_imageLoadVersion") == loadVersion, "thumbnail selection never starts image loading");
        Assert(Field<string>(window, "_activeTabId") == id && Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count == tabCount,
            "thumbnail navigation preserves tab identity and count");
        PressKey(window, Key.Left); Pump(); AssertBrowserSelection(window, middle, "Left selects previous thumbnail");
        int columns = Field<int>(grid, "_columnCount"), index = ThumbnailIndex(grid);
        PressKey(window, Key.Down); Pump(); AssertBrowserSelection(window, items[index + columns].Path, "Down advances one visual row");
        PressKey(window, Key.Up); Pump(); AssertBrowserSelection(window, middle, "Up returns one visual row");
        PressKey(window, Key.Home); Pump(); AssertBrowserSelection(window, first, "Home selects first image, skipping folders");
        AssertThumbnailVisible(grid);
        PressKey(window, Key.Up); Pump(); AssertBrowserSelection(window, first, "Up on first row preserves column");
        PressKey(window, Key.Left); Pump(); AssertBrowserSelection(window, items[1].Path, "arrows select a folder without entering it");
        PressKey(window, Key.Left); PressKey(window, Key.Left); Pump();
        AssertBrowserSelection(window, folder, "Left stops at first tile");
        PressKey(window, Key.End); Pump(); AssertBrowserSelection(window, last, "End selects last image");
        AssertThumbnailVisible(grid);
        PressKey(window, Key.Right); PressKey(window, Key.Down); Pump(); AssertBrowserSelection(window, last, "arrows stop at final tile");
        int finalRowStart = (items.Count - 1) / columns * columns;
        grid.SelectItem(items[finalRowStart]);
        PressKey(window, Key.Down); Pump(); AssertBrowserSelection(window, items[finalRowStart].Path, "Down on final row preserves column");
        grid.SelectItem(items[finalRowStart - 1]);
        PressKey(window, Key.Down); Pump(); AssertBrowserSelection(window, last, "Down clamps in a shorter final row");

        double bottom = grid.VerticalOffset;
        PressKey(window, Key.PageUp); Pump();
        Assert(grid.VerticalOffset < bottom - 1, "Page Up scrolls thumbnails upward");
        AssertNear(bottom - grid.VerticalOffset, grid.ViewportHeight, "Page Up scrolls one viewport");
        AssertBrowserSelection(window, last, "paging preserves active thumbnail");
        PressKey(window, Key.PageDown); Pump(); AssertNear(grid.VerticalOffset, bottom, "Page Down scrolls back one viewport");
        PressKey(window, Key.PageDown); Pump(); AssertNear(grid.VerticalOffset, bottom, "Page Down stops at bottom");
        PressKey(window, Key.Home); Pump();
        PressKey(window, Key.PageUp); Pump(); AssertNear(grid.VerticalOffset, 0, "Page Up stops at top");
        PressKey(window, Key.Space); Pump(); AssertBrowserSelection(window, first, "browser Space does not navigate image mode");
        PressKey(window, Key.Enter); Wait(delegate { return Ready(window, first); }, "Enter opens selected thumbnail");
        PressKey(window, Key.PageDown); Wait(delegate { return Ready(window, items[3].Path); }, "image Page Down still opens next image");
        PressKey(window, Key.Right); Wait(delegate { return Ready(window, items[4].Path); }, "image Right still opens next image");
        PressKey(window, Key.Enter); Pump(); AssertBrowserSelection(window, items[4].Path, "second return selects newly viewed image");

        grid.HandleNavigationKey(Key.Right, ModifierKeys.Shift); Pump();
        Assert(grid.SelectedCount == 2, "Shift Right extends thumbnail range");
        grid.HandleNavigationKey(Key.Down, ModifierKeys.Shift); Pump();
        Assert(grid.SelectedCount == columns + 2, "Shift Down extends selection by visual row");
        grid.HandleNavigationKey(Key.Up, ModifierKeys.Shift); Pump();
        Assert(grid.SelectedCount == 2, "Shift Up contracts range using original anchor");
        AssertActiveThumbnailColors(grid);
        Render((FrameworkElement)window.Content, "thumbnail-navigation-dark.png");
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = false; Pump();
        AssertActiveThumbnailColors(grid);
        Render((FrameworkElement)window.Content, "thumbnail-navigation-light.png");
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = true; Pump();

        string selected = grid.SelectedItem.Path;
        var search = Field<TextBox>(window, "_searchBox");
        foreach (UIElement control in new UIElement[] { search, Field<ComboBox>(window, "_sortFieldBox"),
            Field<Slider>(window, "_thumbnailSizeSlider"), Field<Slider>(window, "_thumbnailAspectSlider"),
            Field<Slider>(window, "_vramSlider") })
        {
            FocusViewerSetting(window, control);
            Assert(control.IsKeyboardFocusWithin, "navigation fixture can focus " + control.GetType().Name);
            foreach (Key key in new[] { Key.Right, Key.Down, Key.Home, Key.End, Key.PageDown, Key.PageUp }) PressKey(window, key);
            Pump(); AssertBrowserSelection(window, selected, "focused input owns its navigation keys");
        }
        CloseViewerSettings(window);
        var tree = Field<TreeView>(Field<FolderNavigationPane>(window, "_folderNavigation"), "_tree");
        TreeViewItem treeItem = tree.Items.OfType<TreeViewItem>().First();
        Assert(treeItem.Focus(), "tree fixture takes keyboard focus");
        PressKey(window, Key.Right); PressKey(window, Key.End); Pump();
        AssertBrowserSelection(window, selected, "folder tree owns its arrow and boundary keys");
        grid.Focus();
        var pending = typeof(MainWindow).GetField("_folderScanPending", BindingFlags.Instance | BindingFlags.NonPublic);
        pending.SetValue(window, true); PressKey(window, Key.Right); PressKey(window, Key.End); pending.SetValue(window, false);
        AssertBrowserSelection(window, selected, "scan-in-progress does not navigate stale items");

        Field<Slider>(window, "_thumbnailSizeSlider").Value = 192;
        Field<Slider>(window, "_thumbnailAspectSlider").Value = 0.5;
        window.Width = 980; window.Height = 640;
        Wait(delegate { return grid.ThumbnailSize == 192 && grid.ThumbnailAspectRatio == 0.5; }, "debounced thumbnail layout completes");
        grid.BringSelectionIntoView(); Pump(); AssertThumbnailVisible(grid); AssertActiveThumbnailColors(grid);
        columns = Field<int>(grid, "_columnCount"); index = ThumbnailIndex(grid);
        grid.Focus(); PressKey(window, Key.Down); Pump();
        AssertBrowserSelection(window, items[index + columns].Path, "row navigation follows resized thumbnails and narrower window");
        AssertThumbnailVisible(grid);
        Render((FrameworkElement)window.Content, "thumbnail-navigation-narrow.png");
        PressKey(window, Key.J); Pump();
        columns = Field<int>(grid, "_columnCount"); index = ThumbnailIndex(grid);
        PressKey(window, Key.Down); Pump();
        AssertBrowserSelection(window, items[index + columns].Path, "row navigation follows hidden folder tree reflow");
        PressKey(window, Key.J); Pump();

        Field<ToggleButton>(window, "_sortDirectionButton").IsChecked = true;
        Invoke(window, "SortSelectionChanged"); WaitScan(window); grid.Focus();
        PressKey(window, Key.Home); Pump(); AssertBrowserSelection(window, last, "Home follows descending sort");
        PressKey(window, Key.End); Pump(); AssertBrowserSelection(window, first, "End follows descending sort");
        search.Text = "Image04"; Invoke(window, "ApplySearch"); Pump(); grid.Focus();
        PressKey(window, Key.Home); Pump(); AssertBrowserSelection(window, Path.Combine(photos, "Image049.png"), "Home follows filter and sort");
        PressKey(window, Key.End); Pump(); AssertBrowserSelection(window, middle, "End follows filter and sort");
        search.Text = "Album"; Invoke(window, "ApplySearch"); Pump(); grid.Focus();
        grid.SelectItem(null); PressKey(window, Key.Home); PressKey(window, Key.End); Pump();
        Assert(grid.SelectedItem == null, "Home/End do nothing when there are no image thumbnails");
        PressKey(window, Key.Right); Pump(); Assert(grid.SelectedItem.IsDirectory, "arrow with no selection starts at first tile");
        string selectedFolder = grid.SelectedItem.Path;
        PressKey(window, Key.Home); PressKey(window, Key.End); Pump();
        AssertBrowserSelection(window, selectedFolder, "folder-only boundaries preserve selection");
        search.Text = "no-matches"; Invoke(window, "ApplySearch"); Pump(); grid.Focus();
        foreach (Key key in new[] { Key.Left, Key.Right, Key.Up, Key.Down, Key.Home, Key.End, Key.PageUp, Key.PageDown }) PressKey(window, key);
        Assert(grid.SelectedItem == null && Field<Grid>(window, "_browserView").IsVisible, "empty browser navigation is harmless");
        search.Text = ""; Invoke(window, "ApplySearch");
        Field<ToggleButton>(window, "_sortDirectionButton").IsChecked = false;
        Invoke(window, "SortSelectionChanged"); WaitScan(window);
        Wait(delegate { return !Field<System.Windows.Threading.DispatcherTimer>(window, "_searchTimer").IsEnabled; }, "debounced search completes before virtual data fixture");

        var virtualItems = Enumerable.Range(0, 100000).Select(i => new ImageFileItem
        {
            Path = Path.Combine(root, "virtual-" + i + ".png"), Name = "Image " + i, Extension = ".png"
        }).ToList();
        grid.SetItems(virtualItems); Pump(); grid.Focus();
        PressKey(window, Key.End); Pump(); AssertBrowserSelection(window, virtualItems[99999].Path, "End reaches image 100000");
        AssertThumbnailVisible(grid); AssertActiveThumbnailColors(grid);
        Assert(Field<Dictionary<int, ThumbnailTile>>(grid, "_realized").Count < 300, "keyboard navigation keeps 100k folder virtualized");
        PressKey(window, Key.PageUp); Pump(); PressKey(window, Key.Home); Pump();
        AssertBrowserSelection(window, virtualItems[0].Path, "Home returns from 100k-folder end");
        Invoke(window, "RefreshFolder"); WaitScan(window);

        Invoke(window, "OpenBrowserImage", middle); Wait(delegate { return Ready(window, middle); }, "restart selection image");
        PressKey(window, Key.Enter); Pump(); grid.HandleNavigationKey(Key.Right, ModifierKeys.Shift); Pump();
        selected = grid.SelectedItem.Path;
        double scroll = grid.VerticalOffset;
        AssertActiveThumbnailColors(grid);
        window.Close(); Pump();
        window = new MainWindow(services); window.Show(); WaitScan(window);
        grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        AssertBrowserSelection(window, selected, "browser lead selection survives restart");
        Assert(grid.SelectedCount == 2, "browser multi-selection survives restart");
        AssertNear(grid.VerticalOffset, scroll, "browser page position survives restart");
        AssertActiveThumbnailColors(grid); grid.Focus(); PressKey(window, Key.Right); Pump();
        AssertBrowserSelection(window, Path.Combine(photos, "Image042.png"), "arrow after restart navigates thumbnails");
        Assert(grid.SelectedCount == 1, "plain arrow replaces range with a single active thumbnail");
        Assert(File.ReadAllBytes(first).SequenceEqual(original), "thumbnail controls never modify image bytes");
        window.Close(); Pump();
    }

    private static int ThumbnailIndex(VirtualizedThumbnailGrid grid)
    {
        return Field<IList<ImageFileItem>>(grid, "_items").ToList().FindIndex(item => item.Path == grid.SelectedItem.Path);
    }

    private static void AssertBrowserSelection(MainWindow window, string path, string message)
    {
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        Assert(Field<Grid>(window, "_browserView").Visibility == Visibility.Visible
            && Field<Grid>(window, "_imageView").Visibility == Visibility.Collapsed
            && Field<Image>(window, "_mainImage").Source == null
            && grid.SelectedItem != null && grid.SelectedItem.Path == path,
            message + ": expected " + path + ", selected " + (grid.SelectedItem == null ? "none" : grid.SelectedItem.Path)
            + ", browser " + Field<Grid>(window, "_browserView").Visibility);
    }

    private static void AssertThumbnailVisible(VirtualizedThumbnailGrid grid, [CallerLineNumber] int line = 0)
    {
        int index = ThumbnailIndex(grid), columns = Field<int>(grid, "_columnCount");
        double height = Field<double>(grid, "_tileHeight"), top = index / columns * height;
        Assert(top >= grid.VerticalOffset - 1 && top + Math.Min(height, grid.ViewportHeight) <= grid.VerticalOffset + grid.ViewportHeight + 1,
            "active thumbnail is scrolled into viewport (test line " + line + ", index " + index + ", columns " + columns
            + ", row top " + top + ", height " + height + ", offset " + grid.VerticalOffset + ", viewport " + grid.ViewportHeight + ")");
    }

    private static void AssertActiveThumbnailColors(VirtualizedThumbnailGrid grid)
    {
        grid.RefreshTiles();
        var realized = Field<Dictionary<int, ThumbnailTile>>(grid, "_realized");
        ThumbnailTile active = realized.Values.Single(t => t.Item.Path == grid.SelectedItem.Path);
        Assert(((SolidColorBrush)active.Background).Color == ((SolidColorBrush)Application.Current.Resources[ThemeKeys.ActiveThumbnailBackground]).Color
            && ((SolidColorBrush)active.BorderBrush).Color == ((SolidColorBrush)Application.Current.Resources[ThemeKeys.ActiveThumbnailBorder]).Color,
            "active thumbnail uses distinct theme-aware blue highlight");
        Assert(Field<TextBlock>(active, "_title").FontWeight == FontWeights.SemiBold, "active filename is emphasized without resizing tile");
        foreach (ThumbnailTile tile in realized.Values.Where(t => t != active))
            Assert(((SolidColorBrush)tile.Background).Color != ((SolidColorBrush)active.Background).Color,
                "active color differs from ordinary and other selected thumbnails");
    }
}
