using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static IEnumerable<T> StackVisuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T) yield return (T)root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (T child in StackVisuals<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static Border StackHeader(MainWindow window, string id)
    {
        return Field<StackPanel>(window, "_tabStrip").Children.OfType<Border>()
            .Single(header => header.Tag is TabStackDto && ((TabStackDto)header.Tag).Id == id);
    }

    private static void RunTabStackChecks()
    {
        string root = Path.Combine(Root, "ts-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string a = Path.Combine(photos, "Portrait.png"), b = Path.Combine(photos, "Landscape.png"), c = Path.Combine(photos, "Missing later.png");
        MakeImage(a, 640, 420, 96); MakeImage(b, 640, 420, 96); MakeImage(c, 640, 420, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1320, WindowHeight = 780 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        Invoke(window, "OpenImageTab", a, true); Wait(delegate { return Ready(window, a); }, "stack first image");
        Invoke(window, "OpenImageTab", b, true); Wait(delegate { return Ready(window, b); }, "stack second image");
        Invoke(window, "OpenImageTab", c, true); Wait(delegate { return Ready(window, c); }, "stack missing fixture");
        Invoke(window, "OpenLatestBrowserTab"); WaitScan(window);
        string folder = Field<string>(window, "_activeTabId");
        Invoke(window, "ToggleTabPin", b);
        var tabs = Field<Dictionary<string, ImageTabState>>(window, "_tabs");

        // Exercise the real modal selector and the checkbox-to-model binding.
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
        {
            var dialog = Application.Current.Windows.OfType<TabStackWindow>().Single(); dialog.UpdateLayout();
            Field<TextBox>(dialog, "_name").Text = "Photo review";
            foreach (CheckBox check in StackVisuals<CheckBox>(dialog))
            {
                var choice = check.DataContext as StackTabChoice;
                if (choice != null) check.IsChecked = choice.Id == a || choice.Id == b;
            }
            Assert(dialog.SelectedIds.Length == 2, "stack picker checkboxes update selected tabs");
            Pump(); dialog.UpdateLayout();
            Assert(((SolidColorBrush)StackVisuals<ListBox>(dialog).Single().Background).Color == ((SolidColorBrush)Application.Current.FindResource(ThemeKeys.PaneBackground)).Color,
                "stack picker list follows dark theme");
            Render(dialog, "tab-stack-picker-dark.png");
            StackVisuals<Button>(dialog).Single(button => Object.Equals(button.Content, "Create stack")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }));
        Invoke(window, "EditTabStack", null, a); Pump();
        string review = tabs[a].StackId;
        Assert(review != null && tabs[b].StackId == review && tabs[b].IsPinned, "stack creation retains image identities and pin");
        string missing = (string)Invoke(window, "SetTabStack", null, "Temporary", new string[] { c }); Pump();
        Invoke(window, "RenameTabStack", review, "Portraits & travel");
        Invoke(window, "RenameTabStack", review, "   ");
        Assert(Capture(window).TabStacks.Single(stack => stack.Id == review).Name == "Portraits & travel", "valid rename persists; invalid rename refused");
        Assert(tabs.Count == 4, "stacking never closes tabs");
        Invoke(window, "ActivateImageTab", a); Wait(delegate { return Ready(window, a); }, "activate stacked image");
        Invoke(window, "ToggleTabStack", review); Pump();
        Assert(!Field<StackPanel>(window, "_tabStrip").Children.OfType<Border>().Any(header => Object.Equals(header.Tag, a)), "collapsed stack hides member headers");
        Assert(Ready(window, a) && StackHeader(window, review).BorderThickness.Top == 4, "collapse retains image and highlights containing stack");
        Render((FrameworkElement)window.Content, "tab-stacks-collapsed-dark.png");
        var stackMenu = StackHeader(window, review).ContextMenu; stackMenu.PlacementTarget = StackHeader(window, review); stackMenu.IsOpen = true; Pump();
        MenuItem selectB = stackMenu.Items.OfType<MenuItem>().Single(item => Object.Equals(item.Tag, b));
        stackMenu.IsOpen = false; selectB.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Wait(delegate { return Ready(window, b); }, "switch collapsed member via stack menu");
        Assert(Capture(window).TabStacks.Single(stack => stack.Id == review).IsCollapsed, "switching members retains collapsed state");
        Invoke(window, "CloseTab", b); Assert(tabs.ContainsKey(b), "stacked pinned tabs cannot close");
        Invoke(window, "DuplicateTab", b); Wait(delegate { return Ready(window, b); }, "duplicate stacked image");
        string duplicate = Field<string>(window, "_activeTabId");
        Assert(tabs[duplicate].StackId == review && !tabs[duplicate].IsPinned, "duplicate belongs to source stack without pin");
        Invoke(window, "MoveTabToStack", duplicate, missing);
        Assert(tabs[duplicate].StackId == missing, "move between stacks");
        Invoke(window, "MoveTabToStack", duplicate, null);
        Assert(tabs[duplicate].StackId == null && Capture(window).TabStacks.Count == 2, "remove a tab keeps it open");
        Invoke(window, "CloseTab", duplicate); Pump();
        Invoke(window, "ToggleTabStack", review); Pump();
        Invoke(window, "ActivateImageTab", a); Wait(delegate { return Ready(window, a); }, "expanded stack");
        Render((FrameworkElement)window.Content, "tab-stacks-expanded-dark.png");
        var scroller = Field<ScrollViewer>(window, "_tabScroller");
        var drag = Field<ReorderDrag>(window, "_tabReorder");
        SimulateOrderDrag(drag, StackHeader(window, missing), "stack:" + missing, delegate(IDataObject data)
        {
            Point point = OrderPoint(StackHeader(window, review), scroller, true, false);
            Assert(drag.Preview(data, point) >= 0 && drag.Commit(data, point), "whole stack uses native reorder controller");
        }); Pump();
        Assert(Capture(window).Tabs[0].Id == c, "stack block moves before another stack");
        var visible = (IList<ReorderItem>)Invoke(window, "TabReorderItems");
        int beforeB = visible.ToList().FindIndex(item => item.Key == b);
        Assert((bool)Invoke(window, "ReorderVisibleTab", folder, beforeB) && tabs[folder].StackId == review, "dropping folder tab among members joins stack");
        Invoke(window, "MoveTabToStack", folder, null);
        Invoke(window, "ToggleTabStack", review); Pump();
        services.Sessions.SaveNamed("Stacked session", Capture(window));
        string backup = Path.Combine(root, "stacks.json");
        WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
        SessionState saved = Capture(window);
        Assert(saved.TabStacks.Count == 2 && saved.Tabs.Count == 4, "capture includes all grouped and ungrouped tabs");
        window.Close(); Pump(); window = new MainWindow(services); window.Show(); WaitScan(window);
        Wait(delegate { return Ready(window, a); }, "restart active stacked image");
        Assert(Capture(window).TabStacks.Single(stack => stack.Id == review).IsCollapsed && Capture(window).Tabs[0].Id == c, "restart restores names, membership, order and collapse");
        Invoke(window, "UnstackTabs", review);
        Assert(Capture(window).Tabs.Count == 4 && Capture(window).TabStacks.Count == 1 && Capture(window).Tabs.Single(tab => tab.Id == b).IsPinned, "unstack all preserves order, views and pins");
        Invoke(window, "LoadNamedSession", "Stacked session"); WaitScan(window); Wait(delegate { return Ready(window, a); }, "load named stacked session");
        Assert(Capture(window).TabStacks.Count == 2, "named sessions restore stacks");
        var decoded = ViewerBackupStore.Load(backup);
        Assert(decoded.Workspace.TabStacks.Count == 2 && decoded.SavedSessions.Single().State.TabStacks.Count == 2, "backup includes live and named stacks");
        string bad = Path.Combine(root, "invalid.json");
        decoded.Workspace.TabStacks.Add(decoded.Workspace.TabStacks[0].Copy());
        SessionStore.WriteObject(bad, decoded, typeof(ViewerBackupDocument));
        bool rejected = false; try { ViewerBackupStore.Load(bad); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "duplicate stack IDs rejected before import");
        decoded = ViewerBackupStore.Load(backup); decoded.Workspace.Tabs[0].StackId = "unknown";
        SessionStore.WriteObject(bad, decoded, typeof(ViewerBackupDocument));
        rejected = false; try { ViewerBackupStore.Load(bad); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "dangling stack membership rejected");
        File.Delete(c);
        var imported = (Task<int>)Invoke(window, "ImportBackupFileAsync", backup, false);
        WaitBackup(imported); WaitScan(window); Wait(delegate { return Ready(window, a); }, "stack backup imported");
        Assert(imported.Result == 2 && Capture(window).TabStacks.Count == 1 && Capture(window).Tabs.Count == 3,
            "missing image tabs and empty stacks removed on backup import");
        Assert(services.Sessions.LoadNamed("Stacked session").TabStacks.Count == 1, "missing cleanup also updates named stacks");
        ThemeManager.SetDarkTheme(false); Invoke(window, "ToggleTabStack", review); window.Width = 980; Pump();
        Render((FrameworkElement)window.Content, "tab-stacks-light-narrow.png");
        var choiceWindow = new TabStackWindow("Long descriptive stack name", new List<StackTabChoice> {
            new StackTabChoice { Id = a, Label = new string('A', 160), Path = a, Selected = true } }, false) { Owner = window, Width = 400, Height = 320 };
        choiceWindow.Show(); Pump(); Render(choiceWindow, "tab-stack-picker-light-narrow.png"); choiceWindow.Close(); Pump();
        Task<string> reset = (Task<string>)Invoke(window, "ResetViewerAsync", false); WaitBackup(reset); WaitScan(window);
        Assert(Capture(window).TabStacks.Count == 0 && Capture(window).Tabs.Count == 0, "reset clears stack metadata and tabs");
        Assert(ViewerBackupStore.Load(reset.Result).Workspace.TabStacks.Count == 1, "reset recovery backup retains stacks");
        // Legacy profiles and backups have neither of the new optional fields.
        var legacy = new SessionState { LastFolder = photos, Tabs = new List<SessionTabDto> { new SessionTabDto { Id = a, Path = a, FolderPath = photos } } };
        ViewerBackupStore.Save(bad, ViewerBackupStore.Create(legacy, new BrowserPreferences(), new NamedSessionDocument[0]));
        Invoke(window, "ApplySessionState", ViewerBackupStore.Load(bad).Workspace, false); WaitScan(window);
        Assert(Capture(window).TabStacks.Count == 0 && Capture(window).Tabs.Single().StackId == null, "older backups remain compatible");
        window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true);
        Console.WriteLine("PASS: stack picker, group/rename/collapse/switch/duplicate/pin/unstack, drag blocks/members, auto/named sessions, backup validation/missing files, reset and legacy compatibility.");
    }
}
