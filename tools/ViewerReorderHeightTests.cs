using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static Point OrderPoint(FrameworkElement item, FrameworkElement host, bool horizontal, bool after)
    {
        Rect bounds = item.TransformToAncestor(host).TransformBounds(new Rect(item.RenderSize));
        return horizontal ? new Point(after ? bounds.Right - 2 : bounds.Left + 2, bounds.Top + bounds.Height / 2)
            : new Point(bounds.Left + 8, after ? bounds.Bottom - 2 : bounds.Top + 2);
    }

    private static void SimulateOrderDrag(ReorderDrag helper, FrameworkElement handle, string key,
        Action<IDataObject> duringDrag)
    {
        var native = helper.RunDrag;
        bool ran = false, clicked = false;
        helper.RunDrag = delegate(DependencyObject source, IDataObject data, DragDropEffects effects)
        {
            ran = true;
            Assert(effects == DragDropEffects.Move && !data.GetDataPresent(DataFormats.FileDrop), "ordering data cannot transfer files");
            duringDrag(data);
            return DragDropEffects.Move;
        };
        try
        {
            Window owner = Window.GetWindow(handle); if (owner != null) { owner.Activate(); owner.UpdateLayout(); }
            helper.BeginPress(handle, key, new Point(10, 10), delegate { clicked = true; });
            Assert(handle.IsMouseCaptured, "drag candidate captures source; visible=" + handle.IsVisible + ", enabled=" + handle.IsEnabled
                + ", loaded=" + handle.IsLoaded + ", active=" + (owner != null && owner.IsActive) + ", pressed=" + (Field<object>(helper, "_pressed") != null)
                + ", captured=" + Mouse.Captured);
            Assert(!helper.MovePointer(new Point(11, 10), true), "minor pointer movement remains click");
            Assert(helper.MovePointer(new Point(40, 40), true), "system threshold begins ordering drag");
            Assert(ran && !clicked && !handle.IsMouseCaptured, "drag releases capture without activating source");
            Assert(Field<object>(helper, "_marker") == null && Field<string>(helper, "_token") == null, "drag completion clears marker and token");
        }
        finally { helper.RunDrag = native; helper.EndPress(false); }
    }

    private static DragEventArgs RouteOrderEvent(FrameworkElement host, IDataObject data, Point position, RoutedEvent routedEvent)
    {
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, host, position }, null);
        args.RoutedEvent = routedEvent; host.RaiseEvent(args); return args;
    }

    private static void RenderOrderPreview(MainWindow window, ReorderDrag helper, string filename)
    {
        window.UpdateLayout(); Pump();
        var marker = Field<FrameworkElement>(helper, "_marker");
        Assert(marker != null && marker.ActualWidth > 0 && marker.ActualHeight > 0, "insertion marker is laid out");
        var bitmap = new RenderTargetBitmap((int)marker.ActualWidth, (int)marker.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
            context.DrawRectangle(new VisualBrush(marker), null, new Rect(0, 0, marker.ActualWidth, marker.ActualHeight));
        bitmap.Render(visual);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int visible = 0;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] > 128) visible++;
        Render(window, filename);
        Assert(visible > 40, "insertion line renders actual opaque pixels; count=" + visible + ", size=" + marker.RenderSize
            + ", line=" + marker.GetType().GetField("Line").GetValue(marker));
    }

    private static void RunReorderHeightChecks()
    {
        RunOrderMergeChecks();
        var current = new Rect(-1500, 150, 1000, 600);
        foreach (Rect area in new[] { new Rect(-1920, 0, 1920, 1040), new Rect(-1920, 40, 1920, 1040),
            new Rect(-1880, 0, 1880, 1080), new Rect(-1920, 0, 1920, 1078) })
        {
            Rect target = WindowPlacement.VerticalBounds(current, area);
            Assert(target.Left == current.Left && target.Width == current.Width && target.Top == area.Top && target.Height == area.Height,
                "height geometry respects negative monitor coordinates and shown/auto-hidden/side taskbar work area");
        }
        for (int source = 0; source < 5; source++) for (int slot = 0; slot <= 5; slot++)
        {
            var list = Enumerable.Range(0, 5).Select(i => i.ToString()).ToList();
            int destination = slot > source ? slot - 1 : slot;
            bool moved = ReorderList.Move(list, source.ToString(), slot);
            Assert(moved == (source != destination) && list[destination] == source.ToString() && list.Distinct().Count() == 5,
                "ordering insertion handles every source/slot including no-op");
        }
        var unchanged = new List<string> { "a", "b" };
        Assert(!ReorderList.Move(unchanged, "missing", 0) && !ReorderList.Move(unchanged, "a", 3)
            && !ReorderList.Move(unchanged, "a", -1) && unchanged.SequenceEqual(new[] { "a", "b" }), "invalid order request is harmless");

        string root = Path.Combine(Root, "rh-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        string photos = Path.Combine(root, "Photos"), secondFolder = Path.Combine(root, "Travel"), thirdFolder = Path.Combine(root, "Portraits");
        Directory.CreateDirectory(photos); Directory.CreateDirectory(secondFolder); Directory.CreateDirectory(thirdFolder);
        Directory.CreateDirectory(Path.Combine(photos, "Nested"));
        string image = Path.Combine(photos, "Landscape.png"); MakeImage(image, 1000, 700, 96);
        MakeImage(Path.Combine(photos, "Portrait.png"), 500, 800, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1200, WindowHeight = 700 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        Invoke(window, "OpenLatestBrowserTab"); WaitScan(window);
        string first = Field<string>(window, "_activeTabId");
        Invoke(window, "OpenLatestBrowserTab"); WaitScan(window);
        string second = Field<string>(window, "_activeTabId");
        Invoke(window, "OpenLatestBrowserTab"); WaitScan(window);
        string third = Field<string>(window, "_activeTabId");
        Invoke(window, "ToggleTabPin", first); Pump();
        var helper = Field<ReorderDrag>(window, "_tabReorder");
        var scroller = Field<ScrollViewer>(window, "_tabScroller");
        var order = Field<List<string>>(window, "_tabOrder");
        SimulateOrderDrag(helper, TabHeader(window, first), first, delegate(IDataObject data)
        {
            Point point = OrderPoint(TabHeader(window, third), scroller, true, true);
            Assert(helper.Preview(data, point) == 3, "tab insertion marker follows target midpoint");
            Assert(Field<object>(helper, "_marker") != null, "visible tab insertion adorner");
            var over = RouteOrderEvent(scroller, data, point, DragDrop.DragOverEvent);
            Assert(over.Handled && over.Effects == DragDropEffects.Move, "routed tab drag accepts private ordering before parent file handling");
            RenderOrderPreview(window, helper, "tab-reorder-marker-dark.png");
            var drop = RouteOrderEvent(scroller, data, point, DragDrop.DropEvent);
            Assert(drop.Handled && drop.Effects == DragDropEffects.Move, "routed tab drop commits order");
        });
        Pump();
        Assert(order.SequenceEqual(new[] { second, third, first }), "pinned tab freely moves to end");
        Assert(Field<string>(window, "_activeTabId") == third && Field<Dictionary<string, ImageTabState>>(window, "_tabs")[first].IsPinned,
            "reorder preserves active tab and pin state");
        SimulateOrderDrag(helper, TabHeader(window, first), first, delegate(IDataObject data)
        {
            helper.Preview(data, OrderPoint(TabHeader(window, second), scroller, true, false));
            var cancel = (QueryContinueDragEventArgs)Activator.CreateInstance(typeof(QueryContinueDragEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { true, DragDropKeyStates.LeftMouseButton }, null);
            cancel.RoutedEvent = DragDrop.QueryContinueDragEvent;
            scroller.RaiseEvent(cancel);
            Assert(cancel.Action == DragAction.Cancel && cancel.Handled && Field<object>(helper, "_marker") == null, "Escape cancels native reorder");
        });
        Assert(order.SequenceEqual(new[] { second, third, first }), "cancel leaves tab order unchanged");
        var foreign = new DataObject(DataFormats.FileDrop, new[] { image });
        Assert(helper.Preview(foreign, new Point(50, 10)) < 0 && !helper.Commit(foreign, new Point(50, 10)), "Explorer data is not consumed as tab reorder");
        bool click = false;
        helper.BeginPress(TabHeader(window, first), first, new Point(10, 10), delegate { click = true; Invoke(window, "ActivateImageTab", first); });
        helper.EndPress(true); WaitScan(window);
        Assert(click && Field<string>(window, "_activeTabId") == first, "ordinary tab click still activates");
        Console.WriteLine("Reorder check: tabs, cancellation and click behavior");

        var pane = Field<FolderNavigationPane>(window, "_folderNavigation");
        pane.ToggleFavorite(photos); pane.ToggleFavorite(secondFolder); pane.ToggleFavorite(thirdFolder);
        pane.SetFavoriteDetails(photos, "Original photos", "Keep this description"); Pump();
        var tree = Field<TreeView>(pane, "_tree"); var favoriteRoot = Field<TreeViewItem>(pane, "_favoritesNode");
        var item = (TreeViewItem)favoriteRoot.Items[0];
        item.IsExpanded = true;
        Wait(delegate { return item.Items.OfType<TreeViewItem>().Any(); }, "favorite child loads");
        item.IsSelected = true; WaitScan(window);
        var child = item.Items.OfType<TreeViewItem>().First();
        var favoriteDrag = Field<ReorderDrag>(pane, "_favoriteReorder");
        SimulateOrderDrag(favoriteDrag, (FrameworkElement)item.Header, photos, delegate(IDataObject data)
        {
            var last = (FrameworkElement)((TreeViewItem)favoriteRoot.Items[2]).Header;
            Point point = OrderPoint(last, tree, false, true);
            Assert(favoriteDrag.Preview(data, point) == 3, "favorite marker targets headers despite expanded subtree");
            Assert(!helper.Commit(data, new Point(40, 10)), "other ordering surface rejects favorite token");
            RenderOrderPreview(window, favoriteDrag, "favorite-reorder-marker-dark.png");
            var drop = RouteOrderEvent(tree, data, point, DragDrop.DropEvent);
            Assert(drop.Handled && drop.Effects == DragDropEffects.Move, "routed favorite drop commits");
        });
        Pump();
        Assert(pane.CapturePreferences().FavoriteFolders.SequenceEqual(new[] { secondFolder, thirdFolder, photos }), "favorites move up/down in displayed order");
        Assert(Object.ReferenceEquals(favoriteRoot.Items[2], item) && item.IsExpanded && item.Items.Contains(child)
            && item.IsSelected && tree.SelectedItem == item, "favorite reorder retains subtree and selection");
        Assert(Field<string>(window, "_currentFolder") == photos && pane.GetFavoriteDetails(photos).Name == "Original photos"
            && pane.GetFavoriteDetails(photos).Description == "Keep this description", "favorite drag retains folder and alias");
        SimulateOrderDrag(favoriteDrag, (FrameworkElement)item.Header, photos, delegate(IDataObject data)
        {
            Assert(favoriteDrag.Preview(data, new Point(20, tree.ActualHeight - 5)) < 0, "drop outside favorites is rejected");
        });
        Console.WriteLine("Reorder check: favorite subtree and shortcut preservation");

        for (int i = 0; i < 12; i++) Invoke(window, "DuplicateTab", second);
        WaitScan(window); Pump(); scroller.ScrollToHorizontalOffset(0); Pump();
        Assert(scroller.ScrollableWidth > 0, "many tabs overflow");
        SimulateOrderDrag(helper, TabHeader(window, second), second, delegate(IDataObject data)
        {
            helper.Preview(data, new Point(scroller.ActualWidth - 5, scroller.ActualHeight / 2));
            for (int i = 0; i < 5; i++) helper.ScrollEdge();
            Assert(scroller.HorizontalOffset > 0, "dragging near strip edge auto-scrolls");
        });
        foreach (string id in order.ToArray().Where(id => id != first && id != second && id != third)) Invoke(window, "CloseTab", id);
        Invoke(window, "ActivateImageTab", first); WaitScan(window);
        Invoke(window, "OpenBrowserImage", image); Wait(delegate { return Ready(window, image); }, "height image");
        RunHeightControlChecks(window);

        var slideshow = Field<ToggleButton>(window, "_slideshowButton");
        FocusViewerSetting(window, slideshow);
        var configuration = Field<Window>(window, "_configureWindow");
        Assert(Window.GetWindow(slideshow) == configuration && !slideshow.IsDescendantOf(window), "slideshow command lives only under Configure");
        Field<TextBox>(window, "_slideshowInterval").Text = "10";
        slideshow.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(Field<bool>(window, "_slideshowPlaying") && Field<Window>(window, "_configureWindow") == null
            && !Field<bool>(window, "_isFullscreen"), "Configure start reveals windowed slideshow");
        FocusViewerSetting(window, slideshow);
        slideshow.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(!Field<bool>(window, "_slideshowPlaying") && Field<Window>(window, "_configureWindow") != null, "Configure stop works without closing settings");
        slideshow.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        FocusViewerSetting(window, slideshow);
        PressKey(Field<Window>(window, "_configureWindow"), Key.Escape); Pump();
        Assert(!Field<bool>(window, "_slideshowPlaying") && Field<Window>(window, "_configureWindow") == null, "Configure Escape stops slideshow");

        services.Sessions.SaveNamed("Reordered", Capture(window));
        string backup = Path.Combine(root, "reordered.json");
        WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
        var document = ViewerBackupStore.Load(backup);
        Assert(document.Workspace.Tabs.Select(tab => tab.Id).SequenceEqual(order), "backup retains freely ordered pinned tabs");
        Invoke(window, "ReorderTab", first, 0);
        Invoke(pane, "ReorderFavorite", photos, 0);
        WaitBackup((Task)Invoke(window, "ImportBackupFileAsync", backup, false)); WaitScan(window);
        pane = Field<FolderNavigationPane>(window, "_folderNavigation");
        Assert(Field<List<string>>(window, "_tabOrder").SequenceEqual(new[] { second, third, first })
            && pane.CapturePreferences().FavoriteFolders.SequenceEqual(new[] { secondFolder, thirdFolder, photos }), "backup import restores tab and favorite order");
        window.Close(); Pump();
        window = new MainWindow(services); window.Show(); WaitScan(window);
        Assert(Field<List<string>>(window, "_tabOrder").SequenceEqual(new[] { second, third, first }), "restart retains freely placed pinned tab");
        Assert(Field<FolderNavigationPane>(window, "_folderNavigation").CapturePreferences().FavoriteFolders.SequenceEqual(new[] { secondFolder, thirdFolder, photos }),
            "restart retains favorite ordering");
        Assert(services.Sessions.LoadNamed("Reordered").Tabs.Select(tab => tab.Id).SequenceEqual(new[] { second, third, first }), "named session retains tab order");
        window.Close(); Pump(); services.Dispose();
        Console.WriteLine("PASS: drag ordering/cancellation/edge scroll, favorite merge and restore, monitor-height toggle, Configure slideshow.");
    }

    private static void RunHeightControlChecks(MainWindow window)
    {
        Rect area = WindowPlacement.CurrentWorkArea(window);
        window.Height = Math.Max(window.MinHeight, Math.Min(700, area.Height - 100));
        window.Top = area.Top + Math.Max(0, (area.Height - window.Height) / 2); Pump();
        double beforeTop = window.Top, beforeHeight = window.ActualHeight, beforeLeft = window.Left, beforeWidth = window.ActualWidth;
        var button = Field<ToggleButton>(window, "_verticalHeightButton");
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(Field<bool>(window, "_isVerticallyMaximized") && button.IsChecked == true, "height toggle stays active after layout events");
        AssertNear(window.Top, area.Top, "height matches monitor work top"); AssertNear(window.ActualHeight, area.Height, "height matches available monitor height");
        AssertNear(window.Left, beforeLeft, "height preserves horizontal position"); AssertNear(window.ActualWidth, beforeWidth, "height preserves width");
        Assert(window.WindowState == WindowState.Normal, "height toggle is not full maximize");
        CheckCentered(window); CheckVisible(window);
        Render((FrameworkElement)window.Content, "vertical-height-dark.png");
        Invoke(window, "ToggleFullscreen"); Pump(); Assert(!button.IsEnabled, "height unavailable in fullscreen");
        Invoke(window, "ToggleFullscreen"); Pump(); Assert(button.IsEnabled && Field<bool>(window, "_isVerticallyMaximized"), "fullscreen roundtrip retains vertical height");
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        AssertNear(window.Top, beforeTop, "second toggle restores top"); AssertNear(window.ActualHeight, beforeHeight, "second toggle restores height");
        FocusViewerSetting(window, Field<CheckBox>(window, "_configureVerticalHeight"));
        var setting = Field<CheckBox>(window, "_configureVerticalHeight");
        setting.IsChecked = true; setting.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(Field<bool>(window, "_isVerticallyMaximized"), "Configure offers same vertical height control");
        CloseViewerSettings(window);
        window.Width -= 20; Pump(); Assert(Field<bool>(window, "_isVerticallyMaximized"), "horizontal resize retains height mode");
        window.Height -= 40; Pump(); Assert(!Field<bool>(window, "_isVerticallyMaximized"), "manual vertical resize releases height mode");
        window.WindowState = WindowState.Maximized; Pump(); Assert(!button.IsEnabled, "height unavailable during normal maximize");
        window.WindowState = WindowState.Normal; Pump();
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        AssertNear(Capture(window).WindowHeight, area.Height, "resulting window height persists in session");
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Console.WriteLine("Height check: taskbar-aware bounds, restore, resize and fullscreen");
    }

    private static void RunOrderMergeChecks()
    {
        var baseline = new BrowserPreferences { FavoriteFolders = new List<string> { "A", "B", "C" } };
        var local = BrowserPreferencesMerge.Copy(baseline); local.FavoriteFolders = new List<string> { "C", "A", "B" };
        var latest = BrowserPreferencesMerge.Copy(baseline); latest.FavoriteFolders.Insert(1, "X"); latest.FavoriteFolders.Remove("B");
        latest.FavoriteDetails.Add(new FavoriteFolderDetails { Path = "A", Name = "Remote name", Description = "Retained" });
        var merged = BrowserPreferencesMerge.Merge(baseline, local, latest);
        Assert(merged.FavoriteFolders.SequenceEqual(new[] { "C", "X", "A" }) && merged.FavoriteDetails.Single().Name == "Remote name",
            "explicit favorite reorder preserves remote additions, deletions and aliases");
        Assert(BrowserPreferencesMerge.Merge(baseline, baseline, merged).FavoriteFolders.SequenceEqual(merged.FavoriteFolders), "idle autosave preserves remote order");
        local = BrowserPreferencesMerge.Copy(baseline); local.FavoriteFolders.Insert(0, "D");
        Assert(BrowserPreferencesMerge.Merge(baseline, local, baseline).FavoriteFolders.SequenceEqual(local.FavoriteFolders), "new favorite can move before first before save");
        string root = Path.Combine(Root, "order-store-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string file = Path.Combine(root, "session.json");
        var seed = new SessionStore(file); seed.SaveBrowserPreferences(baseline);
        var a = new SessionStore(file); var b = new SessionStore(file);
        var pa = a.LoadBrowserPreferences(); var pb = b.LoadBrowserPreferences();
        pa.FavoriteFolders.Reverse(); a.SaveBrowserPreferences(pa);
        pb.FavoriteDetails.Add(new FavoriteFolderDetails { Path = "A", Name = "New alias" }); b.SaveBrowserPreferences(pb);
        b.SaveBrowserPreferences(pb);
        Assert(seed.LoadBrowserPreferences().FavoriteFolders.SequenceEqual(new[] { "C", "B", "A" }), "idle instance and alias edit do not undo stored reorder");
        a.SaveBrowserPreferences(pa);
        Assert(seed.LoadBrowserPreferences().FavoriteDetails.Single().Name == "New alias", "reordering window autosave retains remote alias");
    }
}
