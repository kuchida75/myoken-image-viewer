using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void CheckSearchAlignment(MainWindow window, bool secondRow)
    {
        window.UpdateLayout();
        var search = Field<TextBox>(window, "_searchBox");
        var right = (StackPanel)search.Parent; var panel = (ToolbarCommandPanel)right.Parent;
        var left = (Panel)panel.Children[0];
        Point r = right.TranslatePoint(new Point(), panel), l = left.TranslatePoint(new Point(), panel);
        AssertNear(r.X + right.ActualWidth + right.Margin.Right, panel.ActualWidth, "search group stays right aligned");
        Assert(secondRow ? r.Y >= l.Y + left.ActualHeight : Math.Abs(r.Y - l.Y) < 1, "search group uses appropriate toolbar row");
        Assert(!new Rect(l, left.RenderSize).IntersectsWith(new Rect(r, right.RenderSize)), "toolbar command/search groups do not overlap");
        AssertInside(search, panel); AssertInside(Field<Button>(window, "_advancedSearchButton"), panel);
        foreach (FrameworkElement command in left.Children) AssertInside(command, panel);
    }

    private static void RunToolbarLayoutChecks()
    {
        var panel = new ToolbarCommandPanel();
        panel.Children.Add(new Border { Width = 600, Height = 44 }); panel.Children.Add(new Border { Width = 280, Height = 44 });
        foreach (double width in new[] { 879.0, 880, 881, 1500, 980, 600 })
        {
            panel.Measure(new Size(width, Double.PositiveInfinity)); panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
            AssertNear(panel.DesiredSize.Height, width < 880 ? 88 : 44, "stable toolbar wrap breakpoint");
            AssertInside((FrameworkElement)panel.Children[0], panel); AssertInside((FrameworkElement)panel.Children[1], panel);
        }
        string root = Path.Combine(Root, "toolbar-layout-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        MakeImage(Path.Combine(photos, "A.png"), 640, 420, 96); MakeImage(Path.Combine(photos, "B.png"), 640, 420, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1800, WindowHeight = 780 });
        var window = new MainWindow(services); window.Show(); WaitScan(window); Pump();
        CheckSearchAlignment(window, false); Render((FrameworkElement)window.Content, "search-right-wide-dark.png");
        Invoke(window, "SetBackgroundTheme", "Contours", "#76B3A6", "Right edge", 60.0); Pump();
        CheckSearchAlignment(window, false);
        window.Width = 980; Pump(); CheckSearchAlignment(window, false);
        Render((FrameworkElement)window.Content, "search-right-narrow-dark.png");
        ThemeManager.SetDarkTheme(false); Pump(); CheckSearchAlignment(window, false);
        Render((FrameworkElement)window.Content, "search-right-narrow-light.png");
        window.Width = 1800; Pump(); CheckSearchAlignment(window, false);
        Render((FrameworkElement)window.Content, "search-right-wide-light.png");
        var search = Field<TextBox>(window, "_searchBox");
        Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid").Focus();
        Assert((bool)Invoke(window, "RunShortcut", Key.F, ModifierKeys.Control, false) && search.IsKeyboardFocusWithin, "search keyboard focus preserved");
        search.Text = "B.png"; Invoke(window, "ApplySearch");
        Assert(Field<System.Collections.Generic.List<ImageFileItem>>(window, "_filteredItems").Count == 1, "right-aligned search still filters");
        Field<Button>(window, "_advancedSearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var advanced = Field<Window>(window, "_advancedSearchWindow"); Assert(advanced != null && advanced.IsVisible, "advanced search command still opens"); advanced.Close();
        Invoke(window, "SetCompactMode", true); Pump(); Assert(!search.IsVisible, "compact mode hides toolbar search");
        Invoke(window, "RunShortcut", Key.F, ModifierKeys.Control, false); Pump();
        Assert(search.IsVisible && search.IsKeyboardFocusWithin, "search shortcut exits compact mode"); CheckSearchAlignment(window, false);
        window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true);
        Console.WriteLine("PASS: right-aligned toolbar search, wide/narrow wrapping, non-overlap, themes/backgrounds, filter, advanced search and shortcut focus.");
    }
}
