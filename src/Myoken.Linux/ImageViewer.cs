using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Myoken.Linux;

// Each tab retains its small viewport model. Only the active tab owns decoded pixels.
internal sealed class ImageViewer : UserControl, IDisposable
{
    private readonly string _path;
    private readonly ImageSurface _surface = new();
    private readonly TextBlock _zoom = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 130 };
    private readonly TextBlock _message = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        Margin = new Thickness(24), IsHitTestVisible = false
    };
    private readonly Button _fit, _actual, _minus, _plus;
    private ViewerImage? _image;
    private CancellationTokenSource? _loadCts;
    private Task _detailTask = Task.CompletedTask;
    private bool _active, _disposed, _detailLoading, _detailFailed;
    private int _generation;
    private string? _warning;
    public event Action? StatusChanged;
    internal ImageViewport View => _surface.View;
    internal ImageSurface Surface => _surface;
    internal bool HasImage => _image != null;
    internal bool IsFullResolution => _image?.IsFullResolution == true;
    internal PixelSize DecodedSize => _image?.Bitmap.PixelSize ?? default;
    internal bool ActualSizeEnabled => _actual.IsEnabled;
    internal Task DetailTask => _detailTask;
    internal string StatusText => _image == null ? (_message.Text ?? "Opening image…")
        : $"{System.IO.Path.GetFileName(_path)} | {View.SourceWidth} × {View.SourceHeight} | {View.Zoom * 100:0.#}%{(View.IsFit ? " Fit" : "")} | "
            + (IsFullResolution ? "full resolution" : _detailLoading ? "preview — loading full resolution…" : "preview")
            + (string.IsNullOrEmpty(_warning) ? "" : " | " + _warning);

    public ImageViewer(string path)
    {
        _path = path;
        _fit = MakeButton("Fit", Fit, "Fit without enlarging small images (0)");
        _actual = MakeButton("100%", ActualSize, "One source pixel per application render-target pixel (1)");
        _minus = MakeButton("−", () => ZoomBy(1 / 1.2), "Zoom out (−)");
        _plus = MakeButton("+", () => ZoomBy(1.2), "Zoom in (+)");
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8) };
        toolbar.Children.Add(_fit); toolbar.Children.Add(_actual); toolbar.Children.Add(_minus); toolbar.Children.Add(_plus); toolbar.Children.Add(_zoom);
        toolbar.Children.Add(new TextBlock
        {
            Text = "Wheel: zoom · Drag: pan · Double-click: Fit/100%",
            VerticalAlignment = VerticalAlignment.Center, FontSize = 12
        });
        var root = new DockPanel(); DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var area = new Grid { ClipToBounds = true };
        area.Children.Add(_surface); area.Children.Add(_message); root.Children.Add(area); Content = root;
        _surface.ViewChanged += UpdateView;
        _surface.ActualSizeRequested += ActualSize;
        _surface.LimitZoom = zoom => _image != null && !ViewerDecodePolicy.CanDecodeFull(_image.Width, _image.Height)
            ? Math.Min(zoom, PreviewScale) : zoom;
        AddHandler(KeyDownEvent, OnViewerKeyDown, RoutingStrategies.Bubble);
        UpdateView();
    }

    private Button MakeButton(string caption, Action action, string tip)
    {
        var button = new Button { Content = caption };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => { action(); _surface.Focus(); };
        return button;
    }
    private double PreviewScale => _image == null ? 1 : Math.Min(
        (double)_image.Bitmap.PixelSize.Width / _image.Width, (double)_image.Bitmap.PixelSize.Height / _image.Height);

    public async Task ActivateAsync()
    {
        if (_disposed || (_active && _image != null)) return;
        Suspend(); _active = true;
        var generation = _generation;
        _loadCts = new CancellationTokenSource(); var token = _loadCts.Token;
        _message.Text = "Opening " + System.IO.Path.GetFileName(_path) + "…";
        _warning = null; _detailFailed = false; UpdateView();
        try
        {
            var image = await ViewerImageLoader.LoadAsync(_path, false, token);
            if (!IsCurrent(generation, token)) { image.Bitmap.Dispose(); return; }
            _image = image; _surface.Bitmap = image.Bitmap; View.SetSource(image.Width, image.Height);
            _message.Text = string.Empty;
            if (!ViewerDecodePolicy.CanDecodeFull(image.Width, image.Height))
                _warning = "Preview only: full-resolution limit is 64 million pixels.";
            _surface.NotifyViewChanged();
            await _detailTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsCurrent(generation, token))
            { _message.Text = "Cannot open image: " + ex.Message; UpdateView(); }
        }
    }

    private bool IsCurrent(int generation, CancellationToken token) => !_disposed && _active
        && generation == _generation && !token.IsCancellationRequested;

    private void UpdateView()
    {
        _fit.IsEnabled = _minus.IsEnabled = _plus.IsEnabled = _active && HasImage;
        _actual.IsEnabled = _active && _image != null && ViewerDecodePolicy.CanDecodeFull(_image.Width, _image.Height);
        _message.IsVisible = !HasImage;
        _zoom.Text = HasImage ? $"{View.Zoom * 100:0.#}%{(View.IsFit ? " · Fit" : "")}{(IsFullResolution ? "" : " · preview")}" : "";
        if (_active && _image != null && !IsFullResolution && !_detailLoading && !_detailFailed
            && ViewerDecodePolicy.CanDecodeFull(_image.Width, _image.Height) && View.Zoom > PreviewScale + 1e-6)
        {
            _detailLoading = true;
            _detailTask = UpgradeAsync(_generation, _loadCts!.Token);
        }
        StatusChanged?.Invoke();
    }

    private async Task UpgradeAsync(int generation, CancellationToken token)
    {
        try
        {
            var full = await ViewerImageLoader.LoadAsync(_path, true, token);
            if (!IsCurrent(generation, token)) { full.Bitmap.Dispose(); return; }
            if (full.Width != View.SourceWidth || full.Height != View.SourceHeight)
            {
                full.Bitmap.Dispose();
                throw new InvalidDataException("Image dimensions changed while open; switch tabs to reload.");
            }
            var old = _image; _image = full; _surface.Bitmap = full.Bitmap;
            old?.Bitmap.Dispose(); _surface.InvalidateVisual();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsCurrent(generation, token)) { _detailFailed = true; _warning = "Preview only: " + ex.Message; }
        }
        finally
        {
            if (IsCurrent(generation, token)) { _detailLoading = false; UpdateView(); }
        }
    }

    internal void Fit() { _surface.StopDrag(); View.Fit(); _surface.NotifyViewChanged(); }
    internal void ActualSize()
    {
        if (!_actual.IsEnabled) return;
        _surface.ZoomAt(1, new Point(View.Width / 2, View.Height / 2));
    }
    internal void ZoomBy(double factor) => _surface.ZoomAt(View.Zoom * factor, new Point(View.Width / 2, View.Height / 2));
    internal void InvokeActualSizeButton() => _actual.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal void InvokeFitButton() => _fit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private void OnViewerKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_active || !HasImage || e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) return;
        switch (e.Key)
        {
            case Key.D0: case Key.NumPad0: Fit(); break;
            case Key.D1: case Key.NumPad1: ActualSize(); break;
            case Key.Add: case Key.OemPlus: ZoomBy(1.2); break;
            case Key.Subtract: case Key.OemMinus: ZoomBy(1 / 1.2); break;
            case Key.Left: View.PanBy(80, 0); _surface.NotifyViewChanged(); break;
            case Key.Right: View.PanBy(-80, 0); _surface.NotifyViewChanged(); break;
            case Key.Up: View.PanBy(0, 80); _surface.NotifyViewChanged(); break;
            case Key.Down: View.PanBy(0, -80); _surface.NotifyViewChanged(); break;
            default: return;
        }
        e.Handled = true;
    }

    public void Suspend()
    {
        _active = false; _generation++;
        _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = null;
        _detailLoading = false; _detailTask = Task.CompletedTask;
        _surface.StopDrag(); _surface.Bitmap = null;
        _image?.Bitmap.Dispose(); _image = null;
        _surface.InvalidateVisual(); UpdateView();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; Suspend();
    }
}
