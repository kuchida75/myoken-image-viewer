using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Myoken.Linux;

internal sealed class ImageSurface : Control
{
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor PanCursor = new(StandardCursorType.Hand);
    private static readonly Cursor DragCursor = new(StandardCursorType.SizeAll);
    private TopLevel? _top;
    private IPointer? _dragPointer;
    private Point _lastPoint;
    public ImageViewport View { get; } = new();
    // Borrowed from ImageViewer; this surface never disposes image ownership.
    public Bitmap? Bitmap { get; set; }
    public bool PixelMode { get; private set; }
    public event Action? ViewChanged;
    public event Action? ActualSizeRequested;
    public Func<double, double>? LimitZoom { get; set; }

    // Smooth is the default at both fractional and integer enlargements. Nearest
    // neighbour is reserved for explicit pixel inspection or genuine 1:1 output.
    // Evaluate on every draw: a full-resolution upgrade must not leave a stale
    // preview interpolation mode, and source zoom alone does not imply 1:1 pixels.
    internal BitmapInterpolationMode SamplingMode =>
        (PixelMode && View.Zoom > 1) ||
        (Math.Abs(View.Zoom - 1) < 1e-10 && Bitmap != null
            && Bitmap.PixelSize.Width == View.SourceWidth && Bitmap.PixelSize.Height == View.SourceHeight)
        ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;

    public void SetPixelMode(bool enabled)
    {
        if (PixelMode == enabled) return;
        PixelMode = enabled; NotifyViewChanged();
    }
    public ImageSurface()
    {
        Focusable = true; ClipToBounds = true; UseLayoutRounding = false;
        SizeChanged += (_, _) => UpdateViewport();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        if (_top != null) _top.ScalingChanged += OnScalingChanged;
        UpdateViewport();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopDrag();
        if (_top != null) _top.ScalingChanged -= OnScalingChanged;
        _top = null; base.OnDetachedFromVisualTree(e);
    }
    private void OnScalingChanged(object? sender, EventArgs e) => UpdateViewport();
    private void UpdateViewport()
    {
        View.SetViewport(Bounds.Width, Bounds.Height, _top?.RenderScaling ?? 1);
        NotifyViewChanged();
    }
    public void NotifyViewChanged()
    {
        Cursor = _dragPointer != null ? DragCursor : View.CanPan ? PanCursor : ArrowCursor;
        InvalidateVisual(); ViewChanged?.Invoke();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Bitmap == null || !View.HasSource) return;
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = SamplingMode }))
        using (context.PushClip(new Rect(Bounds.Size)))
            context.DrawImage(Bitmap, new Rect(Bitmap.Size), new Rect(View.X, View.Y, View.DrawWidth, View.DrawHeight));
    }
    public void ZoomAt(double zoom, Point point)
    {
        StopDrag(); View.ZoomAt(LimitZoom?.Invoke(zoom) ?? zoom, point.X, point.Y); NotifyViewChanged();
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Bitmap == null || e.Delta.Y == 0) return;
        ZoomAt(View.Zoom * Math.Pow(1.2, Math.Clamp(e.Delta.Y, -8, 8)), e.GetPosition(this));
        e.Handled = true;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Bitmap == null) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsMiddleButtonPressed) return;
        Focus();
        if (point.Properties.IsLeftButtonPressed && e.ClickCount == 2)
        {
            StopDrag();
            if (View.IsFit) ActualSizeRequested?.Invoke();
            else { View.Fit(); NotifyViewChanged(); }
            e.Handled = true; return;
        }
        if (View.CanPan)
        {
            _lastPoint = point.Position; _dragPointer = e.Pointer;
            e.Pointer.Capture(this); NotifyViewChanged(); e.Handled = true;
        }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragPointer != e.Pointer) return;
        var point = e.GetPosition(this);
        View.PanBy(point.X - _lastPoint.X, point.Y - _lastPoint.Y);
        _lastPoint = point; NotifyViewChanged(); e.Handled = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragPointer != e.Pointer) return;
        StopDrag(); NotifyViewChanged(); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragPointer = null; NotifyViewChanged();
    }
    public void StopDrag()
    {
        var pointer = _dragPointer; _dragPointer = null;
        if (pointer?.Captured == this) pointer.Capture(null);
    }
}
