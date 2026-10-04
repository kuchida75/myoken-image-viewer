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

// Pixel-scrolling virtual canvas. Only visible cells plus one row either side own
// controls/bitmaps. File-path storage and the initial directory sort remain O(n).
internal sealed class VirtualThumbnailBrowser : UserControl, IDisposable
{
    private sealed class Tile : IDisposable
    {
        public required string Path { get; init; }
        public required Button Button { get; init; }
        public required Image Image { get; init; }
        public required TextBlock Caption { get; init; }
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Loading { get; set; } = Task.CompletedTask;
        public bool Failed { get; set; }
        public void Dispose()
        {
            Cancellation.Cancel(); Cancellation.Dispose();
            var bitmap = Image.Source as Bitmap; Image.Source = null; bitmap?.Dispose();
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
    private readonly Dictionary<int, Tile> _tiles = new();
    private string[] _files = Array.Empty<string>();
    private ThumbnailRange _range;
    private bool _queued, _active = true, _disposed;
    private int _selected = -1;
    public event Action<string>? ImageActivated;
    internal int RealizedCount => _tiles.Count;
    internal int LoadedCount => _tiles.Values.Count(t => t.Image.Source != null);
    internal int FailedCount => _tiles.Values.Count(t => t.Failed);
    internal int FirstRealized => _range.First;
    internal int EndRealized => _range.End;
    internal int Columns => _range.Columns;

    public VirtualThumbnailBrowser()
    {
        Focusable = true;
        _scroll.Content = _canvas;
        var grid = new Grid(); grid.Children.Add(_scroll); grid.Children.Add(_empty); Content = grid;
        _scroll.ScrollChanged += (_, _) => QueueRefresh();
        SizeChanged += (_, _) => QueueRefresh();
        AttachedToVisualTree += (_, _) => QueueRefresh();
        DetachedFromVisualTree += (_, _) => Clear();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public void SetFiles(string[] files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Clear(); _files = files; _selected = -1; _range = default;
        _scroll.Offset = default;
        _empty.IsVisible = files.Length == 0;
        // Update the extent even when hidden, so a formerly empty browser can recover.
        _canvas.Height = ThumbnailLayout.Calculate(files.Length, Math.Max(1, _scroll.Viewport.Width), 0, 0).ExtentHeight;
        QueueRefresh();
    }

    public void SetActive(bool active)
    {
        _active = active;
        if (!active) Clear(); else QueueRefresh();
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
        button.GotFocus += (_, _) => _selected = index;
        button.Click += (_, _) => { _selected = index; ImageActivated?.Invoke(path); };
        return new Tile { Path = path, Button = button, Image = image, Caption = caption };
    }

    private async Task LoadAsync(Tile tile, CancellationToken token)
    {
        try
        {
            var bitmap = await ImageDecoder.LoadAsync(tile.Path, 256, token);
            if (token.IsCancellationRequested || _disposed) { bitmap.Dispose(); return; }
            tile.Image.Source = bitmap;
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
        {
            e.Handled = true; ImageActivated?.Invoke(_files[_selected]); return;
        }
        if (e.Key is not (Key.Right or Key.Left or Key.Down or Key.Up or Key.PageDown or Key.PageUp or Key.Home or Key.End)) return;
        e.Handled = true; _selected = Math.Clamp(next, 0, _files.Length - 1);
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
    internal Task WaitForLoadsAsync() => Task.WhenAll(_tiles.Values.Select(t => t.Loading));
    internal void ActivateForTest(int index) => _tiles[index].Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private void Clear()
    {
        _canvas.Children.Clear();
        foreach (var tile in _tiles.Values) tile.Dispose();
        _tiles.Clear();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Clear();
    }
}
