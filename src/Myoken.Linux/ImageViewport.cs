namespace Myoken.Linux;

// UI-independent geometry, kept local until it has earned portable-core adoption.
// Zoom is source pixels -> application render-target pixels, not bitmap DIP units.
internal sealed class ImageViewport
{
    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }
    public double Width { get; private set; }
    public double Height { get; private set; }
    public double RenderScaling { get; private set; } = 1;
    public double Zoom { get; private set; } = 1;
    public double X { get; private set; }
    public double Y { get; private set; }
    public bool IsFit { get; private set; } = true;
    public bool HasSource => SourceWidth > 0 && SourceHeight > 0;
    public double Scale => Zoom / RenderScaling;
    public double DrawWidth => SourceWidth * Scale;
    public double DrawHeight => SourceHeight * Scale;
    public bool CanPan => HasSource && (DrawWidth > Width || DrawHeight > Height);
    public double FitZoom => HasSource && Width > 0 && Height > 0
        ? Math.Min(1, Math.Min(Width * RenderScaling / SourceWidth, Height * RenderScaling / SourceHeight)) : 1;

    public void SetSource(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (width == SourceWidth && height == SourceHeight) return;
        SourceWidth = width; SourceHeight = height;
        Fit();
    }

    public void SetViewport(double width, double height, double scaling)
    {
        // A hidden/detached tab can receive a zero-sized arrange. Keep its last view.
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return;
        if (!double.IsFinite(scaling) || scaling <= 0) scaling = 1;
        var centerX = (Width / 2 - X) / Scale;
        var centerY = (Height / 2 - Y) / Scale;
        Width = width; Height = height; RenderScaling = scaling;
        if (IsFit) Fit();
        else
        {
            X = Width / 2 - centerX * Scale;
            Y = Height / 2 - centerY * Scale;
            Constrain();
        }
    }

    public void Fit()
    {
        IsFit = true; Zoom = FitZoom;
        X = (Width - DrawWidth) / 2; Y = (Height - DrawHeight) / 2;
        Constrain();
    }

    public void ZoomAt(double requestedZoom, double anchorX, double anchorY)
    {
        if (!HasSource || !double.IsFinite(requestedZoom) || !double.IsFinite(anchorX) || !double.IsFinite(anchorY)) return;
        var sourceX = (anchorX - X) / Scale;
        var sourceY = (anchorY - Y) / Scale;
        Zoom = Math.Clamp(requestedZoom, Math.Min(0.01, FitZoom), 16);
        IsFit = false;
        X = anchorX - sourceX * Scale; Y = anchorY - sourceY * Scale;
        Constrain(); // Pointer anchoring yields to image-edge bounds when necessary.
    }

    public void PanBy(double dx, double dy)
    {
        if (!CanPan || !double.IsFinite(dx) || !double.IsFinite(dy)) return;
        IsFit = false; X += dx; Y += dy; Constrain();
    }

    private void Constrain()
    {
        X = Bound(X, DrawWidth, Width);
        Y = Bound(Y, DrawHeight, Height);
        // Align actual-size texels to application render-target pixels.
        if (Math.Abs(Zoom - 1) < 1e-10)
        {
            X = Bound(Math.Round(X * RenderScaling) / RenderScaling, DrawWidth, Width);
            Y = Bound(Math.Round(Y * RenderScaling) / RenderScaling, DrawHeight, Height);
        }
    }
    private static double Bound(double offset, double extent, double viewport) => extent <= viewport
        ? (viewport - extent) / 2 : Math.Clamp(offset, viewport - extent, 0);
}
