using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Myoken.Linux;

// Visible cells plus overscan own controls and bitmap leases. The bounded cache
// may retain unused thumbnails, independently of file-path storage (still O(n)).
internal sealed class VirtualThumbnailBrowser : UserControl, IDisposable
{
    private sealed class Tile : IDisposable
    {
        public required string Path { get; init; }
        public required Button Button { get; init; }
        public required Image Image { get; init; }
        public required TextBlock Caption { get; init; }
        public LeasedLruCache<ThumbnailKey, Bitmap>.Lease? Thumbnail { get; set; }
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Loading { get; set; } = Task.CompletedTask;
        public bool Failed { get; set; }
        public void Dispose()
        {
            Cancellation.Cancel(); Cancellation.Dispose();
            Image.Source = null; Thumbnail?.Dispose(); Thumbnail = null;
        }
    }

    private readonly Canvas _canvas = new();
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        BringIntoViewOnFocusChange = false
    };
    private readonly TextBlock _empty = new()
    {
        Text = "No supported images in this folder.\nOpen a subfolder in the tree or choose another folder.",
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24)
    };
    private readonly ThumbnailCache _cache = new();
    private readonly TextBlock _cacheInfo = new()
    {
        FontSize = 11, Height = 22, Margin = new Thickness(8, 2),
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Dictionary<int, Tile> _tiles = new();
    private string[] _files = Array.Empty<string>();
    private ThumbnailRange _range;
    private bool _queued, _active = true, _disposed;
    private int _selected = -1;
    public event Action<string>? ImageActivated;
    public event Action<string?>? SelectionChanged;
    internal int RealizedCount => _tiles.Count;
    internal int LoadedCount => _tiles.Values.Count(t => t.Image.Source != null);
    internal int FailedCount => _tiles.Values.Count(t => t.Failed);
    internal int FirstRealized => _range.First;
    internal int EndRealized => _range.End;
    internal int Columns => _range.Columns;
    internal ThumbnailCache Cache => _cache;
    internal string? SelectedPath => _selected >= 0 && _selected < _files.Length ? _files[_selected] : null;

    public VirtualThumbnailBrowser()
    {
        Focusable = true;
        _scroll.Content = _canvas;
        var grid = new Grid(); grid.Children.Add(_scroll); grid.Children.Add(_empty);
        var root = new DockPanel(); DockPanel.SetDock(_cacheInfo, Dock.Bottom);
        root.Children.Add(_cacheInfo); root.Children.Add(grid); Content = root;
        _statsTimer.Tick += (_, _) => UpdateCacheInfo();
        _scroll.ScrollChanged += (_, _) => QueueRefresh();
        SizeChanged += (_, _) => QueueRefresh();
        AttachedToVisualTree += (_, _) => { _statsTimer.Start(); UpdateCacheInfo(); QueueRefresh(); };
        DetachedFromVisualTree += (_, _) => { _statsTimer.Stop(); Clear(); };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public void SetFiles(string[] files) => UpdateFiles(files, preserveView: false);

    public void UpdateFiles(string[] files, bool preserveView, string? preferredSelection = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        var oldIndex = _selected;
        var oldSelected = SelectedPath;
        var oldOffset = _scroll.Offset;
        Clear(); _files = files; _range = default;

        var wanted = preferredSelection ?? oldSelected;
        _selected = wanted == null ? -1 : Array.FindIndex(files, p => StringComparer.Ordinal.Equals(p, wanted));
        if (_selected < 0 && preserveView && oldIndex >= 0 && files.Length > 0)
            _selected = Math.Clamp(oldIndex, 0, files.Length - 1);

        _empty.IsVisible = files.Length == 0;
        var layout = ThumbnailLayout.Calculate(files.Length, Math.Max(1, _scroll.Viewport.Width), 0, 0);
        _canvas.Height = layout.ExtentHeight;
        _scroll.Offset = preserveView
            ? new Vector(0, Math.Clamp(oldOffset.Y, 0, Math.Max(0, layout.ExtentHeight - _scroll.Viewport.Height)))
            : default;
        SelectionChanged?.Invoke(SelectedPath);
        QueueRefresh();
    }

    public bool SelectPath(string path)
    {
        var full = Path.GetFullPath(path);
        var index = Array.FindIndex(_files, p => StringComparer.Ordinal.Equals(p, full));
        if (index < 0) return false;
        SetSelected(index); ScrollToIndex(index); return true;
    }

    public void InvalidatePath(string path)
    {
        var full = Path.GetFullPath(path);
        _cache.InvalidatePath(full);
        foreach (var index in _tiles.Where(p => StringComparer.Ordinal.Equals(p.Value.Path, full)).Select(p => p.Key).ToArray())
        {
            var tile = _tiles[index]; _canvas.Children.Remove(tile.Button); tile.Dispose(); _tiles.Remove(index);
        }
        UpdateCacheInfo(); QueueRefresh();
    }
    public void SetActive(bool active)
    {
        _active = active;
        if (!active) Clear(); else QueueRefresh();
    }
    // Explicit Refresh bypasses even an undetectable same-size/same-mtime edit.
    public void InvalidateCache()
    {
        Clear(); _cache.Clear(); UpdateCacheInfo(); QueueRefresh();
    }
    private void UpdateCacheInfo()
    {
        var s = _cache.Snapshot; var m = s.Memory;
        var text = $"Thumb cache: {m.RetainedBytes / 1048576d:0.0}/{m.BudgetBytes / 1048576d:0} MiB est. · Hits {m.Hits:N0} · Decodes {s.DecodeAttempts:N0}";
        if (_cacheInfo.Text != text) _cacheInfo.Text = text;
        ToolTip.SetTip(_cacheInfo, $"{m.Entries:N0} cached thumbnails; {m.Misses:N0} lookup misses; {m.Evictions:N0} capacity evictions\n"
            + $"{m.OutstandingLeases:N0} displayed leases; {(m.LiveBytes - m.RetainedBytes) / 1048576d:0.00} MiB retired but still leased\n"
            + $"{m.Invalidations:N0} invalidations; {s.StaleResults:N0} changed-during-decode results rejected\n"
            + "Estimated thumbnail storage, not total process/GPU memory. Counters last until exit. Refresh/F5 clears cached thumbnails.");
    }
    private void QueueRefresh()
    {
        if (_queued || _disposed) return;
        _queued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _queued = false;
            if (!_disposed) Refresh();
        }, DispatcherPriority.Background);
    }
    private void Refresh()
    {
        if (!_active || _disposed || !IsEffectivelyVisible) return;
        var width = _scroll.Viewport.Width; var height = _scroll.Viewport.Height;
        if (width <= 0 || height <= 0) return;
        var range = ThumbnailLayout.Calculate(_files.Length, width, height, _scroll.Offset.Y);
        var anchor = _range.Columns > 0 ? (int)Math.Floor(_scroll.Offset.Y / ThumbnailLayout.CellHeight) * _range.Columns : 0;
        var reflow = _range.Columns > 0 && _range.Columns != range.Columns;
        if (_canvas.Height != range.ExtentHeight || _canvas.Width != width)
        {
            _canvas.Height = range.ExtentHeight; _canvas.Width = width;
            _scroll.UpdateLayout();
        }
        if (reflow)
        {
            _scroll.Offset = new Vector(0, Math.Clamp((anchor / range.Columns) * ThumbnailLayout.CellHeight,
                0, Math.Max(0, range.ExtentHeight - height)));
            range = ThumbnailLayout.Calculate(_files.Length, width, height, _scroll.Offset.Y);
        }
        _range = range;
        foreach (var index in _tiles.Keys.Where(i => i < range.First || i >= range.End).ToArray())
        {
            var tile = _tiles[index]; _canvas.Children.Remove(tile.Button); tile.Dispose(); _tiles.Remove(index);
        }
        for (var index = range.First; index < range.End; index++)
        {
            if (!_tiles.TryGetValue(index, out var tile))
            {
                tile = CreateTile(index); _tiles.Add(index, tile); _canvas.Children.Add(tile.Button);
                tile.Loading = LoadAsync(tile, tile.Cancellation.Token);
            }
            Canvas.SetLeft(tile.Button, (index % range.Columns) * ThumbnailLayout.CellWidth + 4);
            Canvas.SetTop(tile.Button, (index / range.Columns) * ThumbnailLayout.CellHeight + 4);
        }
    }
    private Tile CreateTile(int index)
    {
        var path = _files[index];
        var image = new Image { Width = 156, Height = 112, Stretch = Stretch.Uniform };
        var caption = new TextBlock
        {
            Text = Path.GetFileName(path), Width = 156, Height = 38, FontSize = 12,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, ClipToBounds = true
        };
        var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(image); panel.Children.Add(caption);
        var button = new Button { Content = panel, Width = 176, Height = 176, Padding = new Thickness(6) };
        ToolTip.SetTip(button, path);
        button.GotFocus += (_, _) => SetSelected(index);
        button.Click += (_, _) => { SetSelected(index); ImageActivated?.Invoke(path); };
        return new Tile { Path = path, Button = button, Image = image, Caption = caption };
    }
    private async Task LoadAsync(Tile tile, CancellationToken token)
    {
        try
        {
            var lease = await _cache.AcquireAsync(tile.Path, 256, token);
            if (token.IsCancellationRequested || _disposed) { lease.Dispose(); return; }
            tile.Thumbnail = lease; tile.Image.Source = lease.Value;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !_disposed)
            {
                tile.Failed = true; tile.Caption.Text = "Unavailable: " + Path.GetFileName(tile.Path);
                ToolTip.SetTip(tile.Button, tile.Path + "\n" + ex.Message);
            }
        }
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_files.Length == 0 || !_active || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
        var columns = Math.Max(1, _range.Columns);
        var current = Math.Max(0, _selected);
        var page = Math.Max(1, (int)(_scroll.Viewport.Height / ThumbnailLayout.CellHeight)) * columns;
        var next = e.Key switch
        {
            Key.Right => current + 1, Key.Left => current - 1,
            Key.Down => current + columns, Key.Up => current - columns,
            Key.PageDown => current + page, Key.PageUp => current - page,
            Key.Home => 0, Key.End => _files.Length - 1, _ => -1
        };
        if (e.Key is Key.Enter or Key.Space && _selected >= 0)
        { e.Handled = true; ImageActivated?.Invoke(_files[_selected]); return; }
        if (e.Key is not (Key.Right or Key.Left or Key.Down or Key.Up or Key.PageDown or Key.PageUp or Key.Home or Key.End)) return;
        e.Handled = true; SetSelected(Math.Clamp(next, 0, _files.Length - 1));
        ScrollToIndex(_selected);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && _tiles.TryGetValue(_selected, out var tile)) tile.Button.Focus();
        }, DispatcherPriority.Background);
    }
    internal void ScrollToIndex(int index)
    {
        if (_files.Length == 0) return;
        index = Math.Clamp(index, 0, _files.Length - 1);
        var y = (index / Math.Max(1, _range.Columns)) * ThumbnailLayout.CellHeight;
        var bottom = y + ThumbnailLayout.CellHeight;
        var offset = _scroll.Offset.Y;
        if (y < offset) offset = y;
        else if (bottom > offset + _scroll.Viewport.Height) offset = bottom - _scroll.Viewport.Height;
        _scroll.Offset = new Vector(0, Math.Max(0, offset)); QueueRefresh();
    }
    private void SetSelected(int index)
    {
        index = _files.Length == 0 ? -1 : Math.Clamp(index, 0, _files.Length - 1);
        if (index == _selected) return;
        _selected = index; SelectionChanged?.Invoke(SelectedPath);
    }
    internal Task WaitForLoadsAsync() => Task.WhenAll(_tiles.Values.Select(t => t.Loading));
    internal void ActivateForTest(int index) => _tiles[index].Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal void SelectForTest(int index) => SetSelected(index);
    private void Clear()
    {
        _canvas.Children.Clear();
        foreach (var tile in _tiles.Values) tile.Dispose();
        _tiles.Clear();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _statsTimer.Stop(); Clear(); _cache.Dispose();
    }
}
