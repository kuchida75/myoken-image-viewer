using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static IDictionary MapPreviews(ThumbnailMinimap map) { return (IDictionary)Field<object>(map, "_previews"); }
    private static int MapLoadedCount(ThumbnailMinimap map)
    {
        return MapPreviews(map).Values.Cast<object>().Count(value => value.GetType().GetField("Bitmap").GetValue(value) != null);
    }

    private static void RunMinimapGeometryChecks()
    {
        foreach (int count in new[] { 0, 1, 7, 1000, 100000 })
            foreach (int columns in new[] { 1, 4, 20 })
                foreach (double height in new[] { 20.0, 500, 2000, 10000 })
                    foreach (double progress in new[] { 0.0, 0.37, 1.0 })
                    {
                        double scroll = Math.Max(0, Math.Ceiling(count / (double)columns) * 196 - 500);
                        MinimapLayout layout = MinimapLayout.Create(count, columns, 196, 500, scroll * progress, 168, height);
                        Assert(layout.LastIndex - layout.FirstIndex + 1 <= MinimapLayout.MaximumPreviews, "minimap geometry keeps preview count bounded");
                        Assert(layout.FirstIndex >= 0 && layout.LastIndex < count, "minimap geometry stays in item range");
                        Assert(layout.MapOffset >= 0 && layout.Viewport.Top >= 0 && layout.Viewport.Bottom <= height + 0.01,
                            "minimap viewport stays inside rail at all sizes");
                        if (count == 0) { Assert(layout.LastIndex == -1 && layout.MaximumScroll == 0, "empty map has no previews or drag range"); continue; }
                        if (progress == 1) Assert(layout.LastIndex == count - 1, "last item reachable without sampling");
                        if (progress == 0) Assert(layout.FirstIndex == 0, "first item represented at top");
                        AssertNear(layout.MaximumScroll, scroll, "minimap maps exact browser scroll range");
                        int middle = (layout.FirstIndex + layout.LastIndex) / 2;
                        Rect rect = layout.Bounds(middle);
                        if (rect.Top >= 0 && rect.Bottom < height)
                            Assert(layout.ItemAt(new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)) == middle, "miniature hit testing retains exact item index");
                    }

    }

    private static void RunMinimapChecks()
    {
        RunMinimapGeometryChecks();
        string root = Path.Combine(Root, "mm-" + Guid.NewGuid().ToString("N").Substring(0, 10));
        string photos = Path.Combine(root, "Photos"), album = Path.Combine(photos, "Album");
        Directory.CreateDirectory(album);
        string first = Path.Combine(photos, "photo0000.png"), second = Path.Combine(photos, "photo0001.png");
        MakeImage(first, 480, 320, 96); MakeImage(second, 200, 300, 96);
        for (int i = 2; i < 1000; i++) File.Copy(i % 2 == 0 ? first : second, Path.Combine(photos, "photo" + i.ToString("0000") + ".png"));
        byte[] original = File.ReadAllBytes(first);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 820 });
        var main = new MainWindow(services); main.Show(); WaitScan(main);
        var grid = Field<VirtualizedThumbnailGrid>(main, "_thumbnailGrid");
        var map = Field<ThumbnailMinimap>(main, "_thumbnailMinimap");
        Assert(grid.ItemCount == 1001 && !map.IsVisible && !Capture(main).ThumbnailMinimapVisible, "old/default session starts with minimap off");
        double normalWidth = grid.ActualWidth;
        grid.ScrollToVerticalOffset(196 * 25 + 30); Pump();
        int anchor = (int)(grid.VerticalOffset / grid.TileHeight) * grid.ColumnCount;
        grid.SelectItem(grid.ItemAt(anchor)); string selected = grid.SelectedItem.Path;
        grid.Focus(); PressKey(main, Key.T);
        Wait(delegate { return map.IsVisible && MapLoadedCount(map) >= 12; }, "minimap enabled and miniature images decoded");
        Assert(grid.ActualWidth < normalWidth && map.ActualWidth >= 60 && map.ActualWidth <= 168, "minimap widens right scrollbar with responsive width");
        Assert(grid.VerticalScrollBarVisibility == ScrollBarVisibility.Hidden, "normal scrollbar replaced while minimap enabled");
        Assert(Math.Abs((int)(grid.VerticalOffset / grid.TileHeight) * grid.ColumnCount - anchor) <= grid.ColumnCount, "toggle preserves current thumbnail region");
        Assert(grid.SelectedItem.Path == selected, "minimap toggle does not change selection");
        Render((FrameworkElement)main.Content, "minimap-browser-dark.png");
        AssertMapRendering(map);
        MinimapLayout layout = Field<MinimapLayout>(map, "_layout");
        double before = grid.VerticalOffset;
        Assert(map.BeginDrag(new Point(layout.Width / 2, layout.Viewport.Top + layout.Viewport.Height / 2)), "viewport band captures mouse for scrubbing");
        map.DragTo(new Point(layout.Width / 2, layout.Height + 1000)); Pump();
        AssertNear(grid.VerticalOffset, grid.ScrollableHeight, "drag clamps to end of folder");
        Assert(Field<MinimapLayout>(map, "_layout").LastIndex == grid.ItemCount - 1, "map shows final folder images at bottom");
        map.DragTo(new Point(layout.Width / 2, layout.Height + 990)); Pump();
        Assert(grid.VerticalOffset < grid.ScrollableHeight, "drag reverses immediately after reaching bottom");
        map.DragTo(new Point(layout.Width / 2, -1000)); Pump();
        AssertNear(grid.VerticalOffset, 0, "drag clamps to beginning");
        map.EndDrag(); Assert(!map.IsMouseCaptured, "pointer released on drag end");
        map.DragTo(new Point(0, 500)); Pump(); AssertNear(grid.VerticalOffset, 0, "released drag cannot scroll");
        layout = Field<MinimapLayout>(map, "_layout");
        var targetPoint = new Point(layout.CellWidth * 1.5, Math.Min(layout.Height - 2, layout.Viewport.Bottom + 100));
        int targetIndex = layout.ItemAt(targetPoint); double expected = layout.CenterItemOffset(targetIndex);
        Assert(map.BeginDrag(targetPoint), "click on a miniature captures pointer"); map.EndDrag(); Pump();
        AssertNear(grid.VerticalOffset, expected, "click scrolls to exact miniature row");
        Assert(grid.SelectedItem.Path == selected && Field<Grid>(main, "_imageView").Visibility != Visibility.Visible, "minimap never selects or opens images");
        before = grid.VerticalOffset;
        map.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent }); Pump();
        Assert(grid.VerticalOffset > before, "minimap wheel scrolls browser");
        layout = Field<MinimapLayout>(map, "_layout");
        map.BeginDrag(new Point(layout.Width / 2, layout.Viewport.Top + layout.Viewport.Height / 2));
        map.ReleaseMouseCapture(); Assert(!Field<bool>(map, "_dragging"), "lost capture terminates minimap drag");
        map.BeginDrag(new Point(layout.Width / 2, layout.Viewport.Top + layout.Viewport.Height / 2));
        PressKey(main, Key.Escape); Assert(!map.IsMouseCaptured, "Escape releases minimap capture");

        Field<TextBox>(main, "_searchBox").Focus(); PressKey(main, Key.T);
        Assert(map.IsVisible, "T preserves text editing focus");
        foreach (UIElement control in new UIElement[] { Field<ComboBox>(main, "_sortFieldBox"), Field<Slider>(main, "_thumbnailSizeSlider"),
            Field<Slider>(main, "_thumbnailAspectSlider"), Field<Slider>(main, "_vramSlider"), Field<ComboBox>(main, "_enhanceMode") })
        {
            FocusViewerSetting(main, control); PressKey(main, Key.T); Assert(map.IsVisible, "T preserves focused " + control.GetType().Name);
        }
        CloseViewerSettings(main);
        var tree = Field<TreeView>(Field<FolderNavigationPane>(main, "_folderNavigation"), "_tree");
        tree.Items.OfType<TreeViewItem>().First().Focus(); PressKey(main, Key.T); Assert(map.IsVisible, "T preserves folder tree typing");
        grid.Focus(); PressKey(main, Key.T); Pump();
        Assert(!map.IsVisible && grid.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && MapPreviews(map).Count == 0, "T returns normal scrollbar and releases preview bitmap references");
        Wait(delegate { return Field<int>(map, "_activeLoads") == 0; }, "hidden map cancels outstanding loads");
        Field<ToggleButton>(main, "_minimapButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Wait(delegate { return map.IsVisible && MapLoadedCount(map) > 0; }, "minimap icon reopens map");

        Field<TextBox>(main, "_searchBox").Text = "photo009";
        Wait(delegate { return grid.ItemCount == 10 && MapPreviews(map).Count == 10; }, "map follows filename filter");
        Assert(MapPreviews(map).Values.Cast<object>().All(value => ((ImageFileItem)value.GetType().GetField("Item").GetValue(value)).Name.StartsWith("photo009")),
            "old unfiltered miniatures discarded");
        Field<TextBox>(main, "_searchBox").Text = "";
        Wait(delegate { return grid.ItemCount == 1001; }, "clear minimap filter");
        Field<ToggleButton>(main, "_sortDirectionButton").IsChecked = true; Invoke(main, "SortSelectionChanged"); WaitScan(main); Pump();
        Assert(grid.ItemAt(1).Name == "photo0999.png", "minimap source follows descending sort");
        Field<ToggleButton>(main, "_sortDirectionButton").IsChecked = false; Invoke(main, "SortSelectionChanged"); WaitScan(main);
        string added = Path.Combine(photos, "photo-added.png"); File.Copy(first, added);
        Wait(delegate { return grid.ItemCount == 1002; }, "watcher updates minimap source after create");
        File.Delete(added); Wait(delegate { return grid.ItemCount == 1001; }, "watcher updates minimap source after delete");
        Invoke(main, "OpenFolderTab", album); WaitScan(main); Pump();
        Assert(map.IsVisible && MapPreviews(map).Count == 0 && !map.BeginDrag(new Point(30, 30)), "empty folder map stays safe and inactive");
        Invoke(main, "ShowBrowser"); WaitScan(main);
        Wait(delegate { return map.IsVisible && MapLoadedCount(map) > 0; }, "map resumes on earlier browser tab");
        Invoke(main, "OpenBrowserImage", first); Wait(delegate { return Ready(main, first); }, "image opened with minimap enabled");
        Assert(!map.IsVisible && MapPreviews(map).Count == 0, "image view suspends minimap previews");
        main.Focus(); PressKey(main, Key.T); Assert(Capture(main).ThumbnailMinimapVisible, "image-mode T does not toggle browser preference");
        PressKey(main, Key.Enter); WaitScan(main);
        Wait(delegate { return map.IsVisible && MapLoadedCount(map) > 0; }, "return to browser resumes minimap");
        grid.Focus(); PressKey(main, Key.End); Pump();
        AssertNear(grid.VerticalOffset, grid.ScrollableHeight, "keyboard navigation updates minimap scroll position");
        AssertNear(Field<MinimapLayout>(map, "_layout").Progress, 1, "minimap band tracks End key");
        PressKey(main, Key.T); Pump(); AssertNear(grid.VerticalOffset, grid.ScrollableHeight, "toggle off at bottom retains bottom");
        PressKey(main, Key.T); Pump(); AssertNear(grid.VerticalOffset, grid.ScrollableHeight, "toggle on at bottom retains bottom");

        main.Width = 980; main.Height = 680;
        Field<Slider>(main, "_thumbnailSizeSlider").Value = 192; Field<Slider>(main, "_thumbnailAspectSlider").Value = 0.75;
        Invoke(main, "SetMetadataPanelVisible", true);
        Wait(delegate { return grid.ThumbnailSize == 192 && grid.ThumbnailAspectRatio == 0.75; }, "resized thumbnail layout with minimap");
        ThemeManager.SetDarkTheme(false); Pump();
        Assert(map.ActualWidth <= Field<Grid>(main, "_thumbnailWorkspace").ActualWidth * 0.25, "minimap leaves most of narrow workspace to browser");
        Assert(Field<MinimapLayout>(map, "_layout").Viewport.Bottom <= map.ActualHeight + 0.01, "resized aspect/metadata layout keeps band in rail");
        Render((FrameworkElement)main.Content, "minimap-browser-light-narrow.png");
        Invoke(main, "SetMetadataPanelVisible", false); ThemeManager.SetDarkTheme(true);
        grid.Focus(); PressKey(main, Key.H); Pump(); Assert(map.IsVisible, "compact browser retains enabled minimap");
        PressKey(main, Key.J); Pump(); Assert(map.IsVisible, "tree toggle retains minimap");
        PressKey(main, Key.H); Pump();

        ImageFileItem[] realItems = Enumerable.Range(0, grid.ItemCount).Select(grid.ItemAt).ToArray();
        var huge = Enumerable.Range(0, 100000).Select(i => new ImageFileItem { Path = i % 2 == 0 ? first : second,
            Name = "Synthetic" + i.ToString("000000") + ".png", Extension = ".png", LastWriteUtc = realItems[1].LastWriteUtc }).ToArray();
        Field<System.Windows.Threading.DispatcherTimer>(main, "_searchTimer").Stop();
        grid.SetItems(huge); Pump();
        for (int i = 0; i < 12; i++)
        {
            grid.ScrollToVerticalOffset(grid.ScrollableHeight * i / 11.0); Pump();
            Assert(MapPreviews(map).Count <= MinimapLayout.MaximumPreviews && Field<int>(map, "_activeLoads") <= 4,
                "100k scrubbing keeps minimap residency and concurrent work bounded");
            Assert(Field<Dictionary<int, ThumbnailTile>>(grid, "_realized").Count < 300, "main browser remains virtualized with minimap enabled");
        }
        Assert(Field<MinimapLayout>(map, "_layout").LastIndex == 99999, "100k minimap includes final exact entry");
        Wait(delegate { return MapLoadedCount(map) >= 40; }, "100k end-of-folder miniatures finish loading after scrubbing stops");
        AssertMapRendering(map);
        Render((FrameworkElement)main.Content, "minimap-100k-end.png");
        grid.SetItems(realItems); grid.ScrollToVerticalOffset(0); Pump();
        SessionState saved = Capture(main);
        Assert(saved.ThumbnailMinimapVisible, "minimap visibility captured window-wide");
        services.Sessions.SaveNamed("Minimap", saved);
        Assert(services.Sessions.LoadNamed("Minimap").ThumbnailMinimapVisible, "named session preserves minimap preference");
        string backupPath = Path.Combine(root, "minimap-backup.json");
        ViewerBackupStore.Save(backupPath, ViewerBackupStore.Create(saved, new BrowserPreferences(), new NamedSessionDocument[0]));
        Assert(ViewerBackupStore.Load(backupPath).Workspace.ThumbnailMinimapVisible, "configuration backup round-trip includes minimap");
        main.Close(); Pump();
        Assert(Field<bool>(map, "_disposed") && MapPreviews(map).Count == 0, "close releases minimap subscriptions and bitmaps");
        Wait(delegate { return Field<int>(map, "_activeLoads") == 0; }, "closed minimap jobs finish cancellation");
        main = new MainWindow(services); main.Show(); WaitScan(main);
        map = Field<ThumbnailMinimap>(main, "_thumbnailMinimap");
        Wait(delegate { return map.IsVisible && MapLoadedCount(map) > 0; }, "automatic session restores minimap");
        Assert(File.ReadAllBytes(first).SequenceEqual(original), "minimap leaves source image bytes unchanged");
        main.Close(); services.Dispose(); Pump();
        Console.WriteLine("PASS: experimental minimap geometry, exact-row click/drag/wheel, focus, thumbnails, themes, sort/filter/watchers, lifecycle, sessions and bounded 100k virtualization.");
    }

    private static void AssertMapRendering(ThumbnailMinimap map)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(map.ActualWidth), (int)Math.Ceiling(map.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        // Normalize the right-column visual offset when capturing just the rail.
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(map) { Stretch = Stretch.Fill }, null, new Rect(0, 0, map.ActualWidth, map.ActualHeight));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(Root, "minimap-rail-dark.png"))) encoder.Save(stream);
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int colorful = 0;
        Rect viewport = Field<MinimapLayout>(map, "_layout").Viewport;
        viewport.Inflate(3, 3);
        for (int i = 0; i < pixels.Length; i += 4)
            if (!viewport.Contains(new Point(i / 4 % bitmap.PixelWidth, i / 4 / bitmap.PixelWidth))
                && pixels[i + 3] > 200 && Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]))
                - Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2])) > 40) colorful++;
        Assert(colorful > 300, "minimap paints real colorful image pixels, not only placeholders: " + colorful);
    }
}
