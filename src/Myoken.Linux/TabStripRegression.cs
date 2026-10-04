using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Myoken.Linux;

internal sealed partial class MainWindow
{
    private async Task RunTabStripChecksAsync(string folder)
    {
        CheckUi(_tabItems.Count == 1, "tab-strip test starts with Browser only");
        var originalWidth = Width;
        var documents = _files.Take(40).Select(AddImageTab).ToList();
        var duplicateName = "同じ名前_" + new string('a', 80) + "_2026-10-04.png";
        var special = new List<DocumentTab>();
        foreach (var directory in new[] { "left", "right" })
        {
            var parent = Path.GetFullPath(Path.Combine(folder, "..", "tab-fixtures", directory));
            Directory.CreateDirectory(parent);
            var path = Path.Combine(parent, duplicateName);
            File.Copy(_files[0], path);
            special.Add(AddImageTab(path));
        }
        documents.AddRange(special);
        await SelectForTestAsync(documents[^1]);
        Width = 820;
        await WaitUiAsync(() => Math.Abs(Bounds.Width - 820) < 1 && _tabs.SelectedHeaderVisible,
            "last selected header not revealed after narrowing");
        CheckUi(_tabs.HeadersShareRow && _tabs.ContentTop == 44,
            "42 image tabs stay on one row without reducing content height");
        CheckUi(_tabs.BrowserHeader.Bounds.Width > 0 && _tabs.OpenTabsButton.Bounds.Width > 0
            && _tabs.HeaderScroll.Extent.Width > _tabs.HeaderScroll.Viewport.Width,
            "Browser and open-tabs controls remain outside the overflowing strip");
        CheckUi(documents.Take(documents.Count - 1).All(t => !((ImageViewer)t.Content).HasImage),
            "opening many tab headers does not decode their inactive images");
        CaptureForTest("tabs-overflow-narrow");

        Point Root(Control control, Point point) => control.TranslatePoint(point, this)
            ?? throw new InvalidOperationException("Detached tab test control.");
        using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var scroll = _tabs.HeaderScroll;
        var offset = scroll.Offset.X;
        var activeLast = (ImageViewer)documents[^1].Content;
        var zoomLast = activeLast.View.Zoom;
        var wheel = new PointerWheelEventArgs(scroll, pointer, this, Root(scroll, new Point(100, 16)), 1,
            new PointerPointProperties(), KeyModifiers.None, new Vector(0, 1));
        scroll.RaiseEvent(wheel);
        await Task.Delay(50);
        CheckUi(wheel.Handled && scroll.Offset.X < offset && _tabs.SelectedItem == documents[^1]
            && activeLast.View.Zoom == zoomLast, "header wheel scrolls horizontally without changing the selected image or its zoom");

        // Exercise a visible button's popup and actual MenuItem.Click routing.
        _tabs.OpenTabsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitUiAsync(() => _tabs.OpenTabsMenu.IsOpen, "open-tabs popup did not open");
        var entries = _tabs.OpenTabsMenu.Items.Cast<MenuItem>().ToArray();
        CheckUi(entries.Length == _tabItems.Count,
            $"open-tabs menu includes every image and Browser ({entries.Length}/{_tabItems.Count})");
        var sameNames = entries.Where(m => m.Tag is DocumentTab t && special.Contains(t)).ToArray();
        CheckUi(sameNames.Length == 2 && sameNames.All(m => m.Header is StackPanel p
            && ((TextBlock)p.Children[0]).Text == duplicateName)
            && ((TextBlock)((StackPanel)sameNames[0].Header!).Children[1]).Text
                != ((TextBlock)((StackPanel)sameNames[1].Header!).Children[1]).Text,
            "menu preserves full Unicode/underscore filenames and distinguishes parent directories");
        entries.First(m => ReferenceEquals(m.Tag, documents[0])).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await WaitUiAsync(() => _tabs.SelectedItem == documents[0] && _tabs.SelectedHeaderVisible
            && ((ImageViewer)documents[0].Content).HasImage, "menu selection did not reveal first tab");
        CheckUi(!_tabs.OpenTabsMenu.IsOpen, "selecting a menu entry dismisses the popup");

        void Chord(Control target, bool backwards)
        {
            var e = new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Tab,
                KeyModifiers = KeyModifiers.Control | (backwards ? KeyModifiers.Shift : KeyModifiers.None) };
            target.RaiseEvent(e); CheckUi(e.Handled, "tab chord handled before child control input");
        }
        var pathText = _path.Text;
        _path.Focus(); Chord(_path, false);
        CheckUi(_tabs.SelectedItem == documents[1] && _path.Text == pathText, "Ctrl+Tab switches while editing path without changing text");
        Chord(this, true);
        CheckUi(_tabs.SelectedItem == documents[0], "Ctrl+Shift+Tab returns to preceding tab");
        await SelectForTestAsync(_browser); Chord(this, true);
        CheckUi(_tabs.SelectedItem == documents[^1], "backward tab cycling wraps from Browser to last image");
        Chord(this, false);
        CheckUi(_tabs.SelectedItem == _browser, "forward tab cycling wraps back to Browser");

        // Reveal the first header before selecting its neighbour so both targets
        // are visible; never inject a close into a hidden/off-screen header.
        await SelectForTestAsync(documents[0]);
        await WaitUiAsync(() => _tabs.SelectedHeaderVisible, "first header not visible for close test");
        await SelectForTestAsync(documents[1]);
        await WaitUiAsync(() => _tabs.SelectedHeaderVisible, "active header not visible for close test");
        var active = (ImageViewer)documents[1].Content;
        var bitmap = active.Surface.Bitmap;
        var header = _tabs.HeaderFor(documents[0]);
        CheckUi(header.Bounds.Left >= scroll.Offset.X - 0.5
            && header.Bounds.Right <= scroll.Offset.X + scroll.Viewport.Width + 0.5,
            "inactive middle-close target is visibly inside the header viewport");
        var point = new Point(header.Bounds.Width / 2, header.Bounds.Height / 2);
        var pressed = new PointerPointProperties(RawInputModifiers.MiddleMouseButton, PointerUpdateKind.MiddleButtonPressed);
        var released = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.MiddleButtonReleased);
        header.RaiseEvent(new PointerPressedEventArgs(header, pointer, this, Root(header, point), 10, pressed, KeyModifiers.None, 1));
        CheckUi(pointer.Captured == header && _tabItems.Contains(documents[0]), "middle press captures header without closing early");
        header.RaiseEvent(new PointerReleasedEventArgs(header, pointer, this, Root(header, point), 11, released, KeyModifiers.None, MouseButton.Middle));
        CheckUi(!_tabItems.Contains(documents[0]) && pointer.Captured == null && _tabs.SelectedItem == documents[1]
            && ReferenceEquals(bitmap, active.Surface.Bitmap), "middle-click inactive tab closes it without reloading or switching the active image");

        var cancelHeader = _tabs.HeaderFor(documents[1]);
        cancelHeader.RaiseEvent(new PointerPressedEventArgs(cancelHeader, pointer, this, Root(cancelHeader, point), 12, pressed, KeyModifiers.None, 1));
        cancelHeader.RaiseEvent(new PointerReleasedEventArgs(cancelHeader, pointer, this, Root(cancelHeader, new Point(-20, -20)), 13,
            released, KeyModifiers.None, MouseButton.Middle));
        CheckUi(_tabItems.Contains(documents[1]) && pointer.Captured == null, "middle release outside header cancels the close");
        _tabs.CloseButtonFor(documents[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        CheckUi(_tabs.SelectedItem == documents[2], "closing active tab selects the adjacent right-hand document");
        await SelectForTestAsync(documents[^1]);
        RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.W, KeyModifiers = KeyModifiers.Control });
        CheckUi(_tabs.SelectedItem == documents[^2] && !_tabItems.Contains(documents[^1]), "Ctrl+W on last tab selects its left neighbour");
        await SelectForTestAsync(_browser);
        var browserHeader = _tabs.BrowserHeader;
        browserHeader.RaiseEvent(new PointerPressedEventArgs(browserHeader, pointer, this, Root(browserHeader, new Point(10, 10)), 14,
            pressed, KeyModifiers.None, 1));
        browserHeader.RaiseEvent(new PointerReleasedEventArgs(browserHeader, pointer, this, Root(browserHeader, new Point(10, 10)), 15,
            released, KeyModifiers.None, MouseButton.Middle));
        RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.W, KeyModifiers = KeyModifiers.Control });
        CheckUi(_tabItems.Contains(_browser) && _tabs.SelectedItem == _browser, "Browser cannot be closed by middle-click or Ctrl+W");

        foreach (var tab in documents) CloseTab(tab);
        CheckUi(_tabItems.Count == 1 && _tabs.SelectedItem == _browser, "closing remaining image tabs leaves Browser usable");
        Chord(this, false); Chord(this, true);
        CheckUi(_tabs.SelectedItem == _browser, "single-Browser cycling is a no-op");
        Width = originalWidth;
        await WaitUiAsync(() => Math.Abs(Bounds.Width - originalWidth) < 1 && _thumbnails.LoadedCount > 0,
            "browser did not recover after tab-strip tests");
        CheckUi(_tabs.ContentTop == 44, "header height remains unchanged with Browser only");
        Console.WriteLine("PASS: L002c1 single-row tab strip, popup, keyboard and middle-close regression checks");
    }
}
