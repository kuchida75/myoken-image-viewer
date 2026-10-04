using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Myoken.Linux;

// A fixed-height, single-row document strip. Browser and the open-tabs menu are
// outside the horizontal scroller. Only the selected document is presented.
// Headers are deliberately not virtualised: this pass targets ordinary tab counts.
internal sealed class DocumentTabs : UserControl, IDisposable
{
    private sealed class Header : Border, IDisposable
    {
        private IPointer? _middle;
        public ToggleButton Select { get; }
        public Button Close { get; }
        public Header(DocumentTab tab, Action select, Action close)
        {
            Width = 250; Height = 34;
            var name = System.IO.Path.GetFileName(tab.Path!);
            Select = new ToggleButton
            {
                Content = new TextBlock { Text = ThumbnailLayout.TabCaption(name), FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis },
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch, Height = 32,
                Padding = new Thickness(8, 4)
            };
            Close = new Button { Content = "×", Width = 30, Height = 32, Padding = new Thickness(4),
                HorizontalContentAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetName(Select, "View " + name);
            AutomationProperties.SetName(Close, "Close " + name);
            ToolTip.SetTip(Select, tab.Path); ToolTip.SetTip(Close, "Close " + name + " (Ctrl+W)");
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,30") };
            grid.Children.Add(Select); Grid.SetColumn(Close, 1); grid.Children.Add(Close); Child = grid;
            Select.Click += (_, _) => select();
            Close.Click += (_, e) => { e.Handled = true; close(); };
            AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed) return;
                _middle = e.Pointer; e.Pointer.Capture(this); e.Handled = true;
            }, RoutingStrategies.Tunnel);
            AddHandler(PointerReleasedEvent, (_, e) =>
            {
                if (_middle != e.Pointer) return;
                var inside = new Rect(Bounds.Size).Contains(e.GetPosition(this));
                ReleaseCapture(); e.Handled = true;
                if (inside && e.InitialPressMouseButton == MouseButton.Middle) close();
            }, RoutingStrategies.Tunnel);
            PointerCaptureLost += (_, _) => _middle = null;
            DetachedFromVisualTree += (_, _) => ReleaseCapture();
        }
        private void ReleaseCapture()
        {
            var pointer = _middle; _middle = null;
            if (pointer?.Captured == this) pointer.Capture(null);
        }
        public void Dispose() => ReleaseCapture();
    }

    private readonly ToggleButton _browserButton = new() { Content = "Browser", FontSize = 16, Height = 34 };
    private readonly Button _previous = new() { Content = "‹", Height = 34, Padding = new Thickness(6) };
    private readonly Button _next = new() { Content = "›", Height = 34, Padding = new Thickness(6) };
    private readonly Button _open = new() { Content = "Open tabs ▾", Height = 34, Padding = new Thickness(8, 4) };
    private readonly StackPanel _headersPanel = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        BringIntoViewOnFocusChange = true
    };
    private readonly ContextMenu _menu = new() { MaxHeight = 480, Placement = PlacementMode.Bottom };
    private readonly ContentControl _content = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };
    private readonly Dictionary<DocumentTab, Header> _headers = new();
    private ObservableCollection<DocumentTab>? _items;
    private DocumentTab? _browser, _selected;
    private bool _revealPending, _disposed;
    public event EventHandler? SelectionChanged;
    public event Action<DocumentTab>? CloseRequested;

    public DocumentTabs()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("44,*") };
        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions("100,32,*,32,108"),
            Margin = new Thickness(4, 2, 4, 4) };
        strip.Children.Add(_browserButton);
        Grid.SetColumn(_previous, 1); strip.Children.Add(_previous);
        _scroll.Content = _headersPanel; Grid.SetColumn(_scroll, 2); strip.Children.Add(_scroll);
        Grid.SetColumn(_next, 3); strip.Children.Add(_next);
        Grid.SetColumn(_open, 4); strip.Children.Add(_open);
        root.Children.Add(strip); Grid.SetRow(_content, 1); root.Children.Add(_content); Content = root;
        AutomationProperties.SetName(_browserButton, "Browser");
        AutomationProperties.SetName(_previous, "Scroll tabs left");
        AutomationProperties.SetName(_next, "Scroll tabs right");
        AutomationProperties.SetName(_open, "Open tabs menu");
        ToolTip.SetTip(_previous, "Scroll tab headers left"); ToolTip.SetTip(_next, "Scroll tab headers right");
        _browserButton.Click += (_, _) => { if (_browser != null) SelectFromUser(_browser); };
        _previous.Click += (_, _) => ScrollBy(-Math.Max(100, _scroll.Viewport.Width * 0.75));
        _next.Click += (_, _) => ScrollBy(Math.Max(100, _scroll.Viewport.Width * 0.75));
        _open.ContextMenu = _menu;
        _open.Click += (_, _) => { if (_menu.IsOpen) _menu.Close(); else _menu.Open(_open); };
        _menu.Opening += (_, _) => BuildMenu();
        _scroll.ScrollChanged += (_, _) => UpdateScrollButtons();
        _scroll.SizeChanged += (_, _) => QueueReveal();
        _headersPanel.SizeChanged += (_, _) => QueueReveal();
        _scroll.LayoutUpdated += (_, _) => { if (_revealPending) RevealSelected(); };
        AttachedToVisualTree += (_, _) => QueueReveal();
        _scroll.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (_scroll.Extent.Width <= _scroll.Viewport.Width) return;
            var delta = Math.Abs(e.Delta.X) > 0 ? e.Delta.X : e.Delta.Y;
            ScrollBy(-delta * 100); e.Handled = true;
        }, RoutingStrategies.Tunnel);
        // Arrow navigation is scoped to the header row, never the image surface.
        strip.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.None || _items == null) return;
            if (e.Key == Key.Left) Cycle(-1);
            else if (e.Key == Key.Right) Cycle(1);
            else if (e.Key == Key.Home && _browser != null) SelectFromUser(_browser);
            else if (e.Key == Key.End && _items.Count > 0) SelectFromUser(_items[^1]);
            else return;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        UpdateScrollButtons();
    }

    public void Bind(ObservableCollection<DocumentTab> items, DocumentTab browser)
    {
        if (_items != null) throw new InvalidOperationException("Document tabs are already bound.");
        if (!items.Contains(browser) || browser.Path != null)
            throw new ArgumentException("The Browser document must be in the collection.");
        _items = items; _browser = browser; items.CollectionChanged += OnItemsChanged;
        ReconcileHeaders(); SelectedItem = browser;
    }
    public DocumentTab? SelectedItem
    {
        get => _selected;
        set
        {
            if (_disposed) return;
            if (value != null && _items?.Contains(value) != true) return;
            if (ReferenceEquals(value, _selected)) { UpdateChecks(); QueueReveal(); return; }
            _selected = value;
            _content.Content = value?.Content;
            UpdateChecks(); QueueReveal(); SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed) return;
        _menu.Close(); _menu.Items.Clear(); ReconcileHeaders();
        if (_selected == null || !_items!.Contains(_selected))
            SelectedItem = _items!.Count == 0 ? null : _items[Math.Clamp(e.OldStartingIndex, 0, _items.Count - 1)];
        else { UpdateChecks(); QueueReveal(); }
    }
    private void ReconcileHeaders()
    {
        if (_items == null) return;
        foreach (var tab in _headers.Keys.Where(t => !_items.Contains(t)).ToArray())
        {
            var header = _headers[tab]; _headersPanel.Children.Remove(header); header.Dispose(); _headers.Remove(tab);
        }
        var index = 0;
        foreach (var tab in _items)
        {
            if (ReferenceEquals(tab, _browser)) continue;
            if (!_headers.TryGetValue(tab, out var header))
            {
                header = new Header(tab, () => SelectFromUser(tab), () => RequestClose(tab));
                _headers.Add(tab, header);
            }
            if (index >= _headersPanel.Children.Count || !ReferenceEquals(_headersPanel.Children[index], header))
            {
                _headersPanel.Children.Remove(header); _headersPanel.Children.Insert(index, header);
            }
            index++;
        }
        ToolTip.SetTip(_open, $"Open tabs: {index} images (full filenames and parent folders)");
    }
    private void UpdateChecks()
    {
        _browserButton.IsChecked = ReferenceEquals(_selected, _browser);
        foreach (var pair in _headers) pair.Value.Select.IsChecked = ReferenceEquals(pair.Key, _selected);
    }
    private void SelectFromUser(DocumentTab tab)
    {
        if (_items?.Contains(tab) != true) return;
        SelectedItem = tab; FocusSelectedHeader();
    }
    public void FocusSelectedHeader()
    {
        if (ReferenceEquals(_selected, _browser)) _browserButton.Focus();
        else if (_selected != null && _headers.TryGetValue(_selected, out var header)) header.Select.Focus();
    }
    private void RequestClose(DocumentTab tab)
    {
        if (ReferenceEquals(tab, _browser) || _items?.Contains(tab) != true) return;
        var wasSelected = ReferenceEquals(tab, _selected);
        CloseRequested?.Invoke(tab);
        if (wasSelected) FocusSelectedHeader();
    }
    public void Cycle(int direction)
    {
        if (_items == null || _items.Count == 0 || direction == 0) return;
        var index = _selected == null ? 0 : Math.Max(0, _items.IndexOf(_selected));
        SelectFromUser(_items[(index + (direction < 0 ? -1 : 1) + _items.Count) % _items.Count]);
    }
    private void BuildMenu()
    {
        _menu.Items.Clear();
        if (_items == null) return;
        var width = Math.Clamp(Bounds.Width - 100, 240, 760);
        foreach (var tab in _items)
        {
            var name = tab.Path == null ? "Browser" : System.IO.Path.GetFileName(tab.Path);
            var label = new StackPanel { Spacing = 2 };
            // TextBlock preserves underscores literally; no access-key parsing or truncation.
            label.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, MaxWidth = width });
            if (tab.Path != null)
                label.Children.Add(new TextBlock { Text = System.IO.Path.GetDirectoryName(tab.Path), FontSize = 11,
                    TextWrapping = TextWrapping.Wrap, MaxWidth = width, Opacity = 0.7 });
            var item = new MenuItem { Header = label, Tag = tab,
                Icon = ReferenceEquals(tab, _selected) ? new TextBlock { Text = "✓" } : null };
            AutomationProperties.SetName(item, tab.Path ?? "Browser"); ToolTip.SetTip(item, tab.Path ?? "Browser");
            item.Click += (_, _) => { _menu.Close(); SelectFromUser(tab); };
            _menu.Items.Add(item);
        }
    }
    private void ScrollBy(double delta)
    {
        _revealPending = false;
        _scroll.Offset = new Vector(Math.Clamp(_scroll.Offset.X + delta, 0,
            Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width)), 0);
        UpdateScrollButtons();
    }
    private void QueueReveal()
    {
        if (_disposed) return;
        _revealPending = true;
        Dispatcher.UIThread.Post(() => { if (!_disposed && _revealPending) RevealSelected(); }, DispatcherPriority.Loaded);
    }
    private void RevealSelected()
    {
        if (_disposed || _scroll.Viewport.Width <= 0) return;
        if (_selected == null || !_headers.TryGetValue(_selected, out var header))
        { _revealPending = false; UpdateScrollButtons(); return; }
        if (!header.IsArrangeValid || header.Bounds.Width <= 0) return;
        var offset = _scroll.Offset.X;
        if (header.Bounds.Left < offset) offset = header.Bounds.Left;
        else if (header.Bounds.Right > offset + _scroll.Viewport.Width)
            offset = header.Bounds.Right - _scroll.Viewport.Width;
        _scroll.Offset = new Vector(Math.Clamp(offset, 0, Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width)), 0);
        _revealPending = false; UpdateScrollButtons();
    }
    private void UpdateScrollButtons()
    {
        _previous.IsEnabled = _scroll.Offset.X > 0.5;
        _next.IsEnabled = _scroll.Offset.X < _scroll.Extent.Width - _scroll.Viewport.Width - 0.5;
    }

    // Integration-test access to the actual controls and layout, not alternate input paths.
    internal Control BrowserHeader => _browserButton;
    internal Control HeaderFor(DocumentTab tab) => _headers[tab];
    internal Button CloseButtonFor(DocumentTab tab) => _headers[tab].Close;
    internal Button OpenTabsButton => _open;
    internal ContextMenu OpenTabsMenu => _menu;
    internal ScrollViewer HeaderScroll => _scroll;
    internal double ContentTop => _content.Bounds.Top;
    // Explicit-height headers are centred by layout within the slightly taller row.
    // Test their common baseline and containment, not an assumed zero top offset.
    internal bool HeadersShareRow => _headers.Count == 0 || _headers.Values.All(h => h.IsArrangeValid
        && Math.Abs(h.Bounds.Top - _headers.Values.First().Bounds.Top) < 0.01
        && h.Bounds.Top >= 0 && h.Bounds.Bottom <= _scroll.Viewport.Height + 0.01
        && Math.Abs(h.Bounds.Height - 34) < 0.01);
    internal bool SelectedHeaderVisible => ReferenceEquals(_selected, _browser)
        || (_selected != null && _headers.TryGetValue(_selected, out var h) && h.Bounds.Width > 0
            && h.Bounds.Left >= _scroll.Offset.X - 0.5 && h.Bounds.Right <= _scroll.Offset.X + _scroll.Viewport.Width + 0.5);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_items != null) _items.CollectionChanged -= OnItemsChanged;
        _menu.Close(); _menu.Items.Clear(); _content.Content = null;
        foreach (var h in _headers.Values) h.Dispose();
        _headersPanel.Children.Clear(); _headers.Clear(); _items = null;
    }
}
