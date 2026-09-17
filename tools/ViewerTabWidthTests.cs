using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static double CheckInactiveTabWidth(MainWindow window)
    {
        window.UpdateLayout();
        var headers = Field<StackPanel>(window, "_tabStrip").Children.OfType<Border>().ToArray();
        string activeId = Field<string>(window, "_activeTabId");
        var inactive = headers.Where(tab => !Object.Equals(tab.Tag, activeId)).ToArray();
        Assert(headers.Length > 0 && headers.All(tab => tab.Width >= 96 && tab.Width <= 280), "all tab widths remain bounded");
        Assert(inactive.Length == 0 || inactive.All(tab => tab.Width == inactive[0].Width), "inactive tabs share a compact width");
        var active = headers.FirstOrDefault(tab => Object.Equals(tab.Tag, activeId));
        if (active != null) Assert(active.Width == Field<double>(window, "_activeTabWidth")
            && (inactive.Length == 0 || active.Width >= inactive[0].Width), "active tab keeps its normal title-based width");
        foreach (var tab in headers)
        {
            var label = ((DockPanel)tab.Child).Children.OfType<TextBlock>().Last();
            Assert(label.TextTrimming == TextTrimming.CharacterEllipsis && tab.ToolTip is TabPreview,
                "narrow titles retain ellipsis and hover previews");
            foreach (Button close in ((DockPanel)tab.Child).Children.OfType<Button>()) AssertInside(close, tab);
        }
        return inactive.Length == 0 ? headers[0].Width : inactive[0].Width;
    }

    private static void RunTabWidthChecks()
    {
        string root = Path.Combine(Root, "tab-width-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"), folder = Path.Combine(photos, "X"); Directory.CreateDirectory(folder);
        string wide = Path.Combine(photos, "A very wide image title that must not enlarge every open tab.png");
        string medium = Path.Combine(photos, "Travel photo.png"), small = Path.Combine(photos, "a.png");
        MakeImage(wide, 640, 420, 96); MakeImage(medium, 640, 420, 96); MakeImage(small, 640, 420, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1440, WindowHeight = 780 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        Assert(Field<double>(window, "_idealTabWidth") == 96, "empty tab row has safe default width");
        double browserWidth = Field<Button>(window, "_browserTabButton").ActualWidth;
        double newWidth = Field<Button>(window, "_newTabButton").ActualWidth;
        Invoke(window, "OpenImageTab", wide, true); Wait(delegate { return Ready(window, wide); }, "single long tab");
        Assert(CheckInactiveTabWidth(window) == 280, "single long tab keeps maximum width");
        Invoke(window, "OpenImageTab", medium, true); Wait(delegate { return Ready(window, medium); }, "narrower measured title");
        double mediumWidth = CheckInactiveTabWidth(window);
        Assert(mediumWidth > 96 && mediumWidth < 280, "narrower title reduces inactive widths");
        Invoke(window, "ActivateImageTab", wide); Wait(delegate { return Ready(window, wide); }, "activate wider tab");
        Assert(CheckInactiveTabWidth(window) == mediumWidth, "activating a wide tab preserves the inactive baseline");
        Assert(TabHeader(window, wide).Width == 280 && TabHeader(window, medium).Width == mediumWidth, "only the active wide title expands");
        Render((FrameworkElement)window.Content, "tabs-active-normal-wide.png");
        Invoke(window, "DuplicateTab", wide); Wait(delegate { return Ready(window, wide); }, "duplicate wide tab");
        string duplicate = Field<string>(window, "_activeTabId");
        Assert(CheckInactiveTabWidth(window) == mediumWidth, "duplicating a wider title preserves narrowest baseline");
        Invoke(window, "CloseTab", duplicate);
        Invoke(window, "OpenImageTab", small, true); Wait(delegate { return Ready(window, small); }, "shortest tab");
        Assert(CheckInactiveTabWidth(window) == 96, "very short title sets the minimum inactive width");
        Invoke(window, "ToggleTabPin", small); Pump();
        Assert(CheckInactiveTabWidth(window) == 96, "pinned shortest tab still sets common width");
        Invoke(window, "ActivateImageTab", wide); Wait(delegate { return Ready(window, wide); }, "wide active among compact tabs");
        Invoke(window, "ToggleTabPin", wide); Pump();
        Assert(CheckInactiveTabWidth(window) == 96 && TabHeader(window, wide).Width == 280, "active pinned tab retains normal width");
        Render((FrameworkElement)window.Content, "tabs-narrowest-dark.png");
        Invoke(window, "ToggleTabPin", wide);
        Invoke(window, "ToggleTabPin", small); Invoke(window, "CloseTab", small); Pump();
        Assert(CheckInactiveTabWidth(window) == mediumWidth, "closing narrowest tab expands to next narrowest title");
        Invoke(window, "CloseTab", medium); Pump();
        Assert(CheckInactiveTabWidth(window) == 280, "closing next narrowest leaves a full-width single long tab");
        Invoke(window, "OpenImageTab", medium, true); Wait(delegate { return Ready(window, medium); }, "rename sizing fixture");
        string renamed = Path.Combine(photos, "b.png"); File.Move(medium, renamed);
        Invoke(window, "ReplacePathReferences", medium, renamed); Wait(delegate { return Ready(window, renamed); }, "renamed narrower title");
        Assert(CheckInactiveTabWidth(window) == 96, "rename recalculates narrowest width");
        Invoke(window, "OpenFolderTab", folder); WaitScan(window);
        string folderId = Field<string>(window, "_activeTabId");
        Assert(CheckInactiveTabWidth(window) == 96, "mixed image and folder tabs retain the inactive baseline");
        Invoke(window, "CloseTab", medium); Pump();
        double folderWidth = CheckInactiveTabWidth(window);
        Assert(folderWidth < 180 && folderWidth == Field<double>(window, "_idealTabWidth"), "folder title participates in minimum measurement");
        Assert(Field<Button>(window, "_browserTabButton").ActualWidth == browserWidth && Field<Button>(window, "_newTabButton").ActualWidth == newWidth,
            "Browser and plus commands keep fixed widths");
        ThemeManager.SetDarkTheme(false); Pump(); Render((FrameworkElement)window.Content, "tabs-narrowest-light.png");
        Invoke(window, "CloseTab", folderId); Invoke(window, "CloseTab", wide); WaitScan(window);
        Assert(Field<double>(window, "_idealTabWidth") == 96, "closing all tabs resets baseline");
        Invoke(window, "OpenFolderTab", folder); WaitScan(window); folderId = Field<string>(window, "_activeTabId");
        for (int i = 0; i < 17; i++) Invoke(window, "DuplicateTab", folderId);
        WaitScan(window); window.Width = 980; Pump();
        var scroller = Field<ScrollViewer>(window, "_tabScroller");
        Assert(CheckInactiveTabWidth(window) == 96 && scroller.ScrollableWidth > 0, "many tabs use safe minimum and horizontal overflow");
        scroller.ScrollToRightEnd(); Pump(); AssertInside(Field<Button>(window, "_newTabButton"), scroller);
        Render((FrameworkElement)window.Content, "tabs-narrowest-overflow.png");
        Invoke(window, "OpenImageTab", wide, true); Wait(delegate { return Ready(window, wide); }, "wide active in overflowing row"); Pump();
        Assert(CheckInactiveTabWidth(window) == 96 && TabHeader(window, wide).Width == 280, "overflow preserves full active width");
        AssertInside(TabHeader(window, wide), scroller);
        Assert((bool)Invoke(window, "ReorderTab", wide, 0), "active tab can reorder before compact tabs"); Pump();
        Invoke(window, "ActivateImageTab", folderId); WaitScan(window);
        Invoke(window, "ActivateImageTab", wide); Wait(delegate { return Ready(window, wide); }, "reactivate first wide tab"); Pump();
        AssertInside(TabHeader(window, wide), scroller);
        Render((FrameworkElement)window.Content, "tabs-active-normal-overflow.png");
        services.Sessions.SaveNamed("Narrow tabs", Capture(window));
        window.Close(); Pump(); window = new MainWindow(services); window.Show(); WaitScan(window);
        Wait(delegate { return Ready(window, wide); }, "restored active image"); Pump();
        Assert(CheckInactiveTabWidth(window) == 96 && Capture(window).Tabs.Count == 19 && TabHeader(window, wide).Width == 280,
            "restored tabs preserve compact inactive and normal active widths");
        AssertInside(TabHeader(window, wide), Field<ScrollViewer>(window, "_tabScroller"));
        Invoke(window, "ShowBrowser"); WaitScan(window); CheckInactiveTabWidth(window);
        window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true);
        Console.WriteLine("PASS: compact inactive tabs, normal-width active tab, lifecycle, folders, pinned colors, overflow visibility and session restore.");
    }
}
