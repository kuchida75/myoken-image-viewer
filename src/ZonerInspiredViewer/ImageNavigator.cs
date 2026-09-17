using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed class ImageNavigator : Grid
    {
        private readonly Image _preview;
        private readonly ViewportOutline _outline;
        private readonly MatrixTransform _transform = new MatrixTransform();
        private Size _imageSize, _frame;
        private int _rotation;
        private double _zoom;
        private Point _offset, _grabOffset;
        internal Rect ImageBounds { get; private set; }
        internal Rect ViewportBounds { get; private set; }
        internal bool IsDragging { get; private set; }
        internal BitmapSource Source { get { return _preview.Source as BitmapSource; } }
        internal event Action<Point> PanRequested;
        internal event Action DragStarted, DragEnded;

        internal ImageNavigator()
        {
            Focusable = true; Background = Brushes.Transparent; ClipToBounds = true; Cursor = Cursors.Hand;
            var canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            _preview = new Image { Stretch = Stretch.Fill, RenderTransform = _transform };
            RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.LowQuality);
            canvas.Children.Add(_preview); Children.Add(canvas);
            _outline = new ViewportOutline { IsHitTestVisible = false }; Children.Add(_outline);
            AutomationProperties.SetName(this, "Image navigator");
            SizeChanged += delegate { EndDrag(); UpdateGeometry(); };
            IsVisibleChanged += delegate { if (!IsVisible) EndDrag(); };
            LostMouseCapture += delegate { EndDrag(); };
        }

        internal void SetView(BitmapSource source, int rotation, Size frame, double zoom, Point offset)
        {
            if (source == null) { Clear(); return; }
            rotation = ImageViewport.NormalizeRotation(rotation);
            Size size = new Size(source.PixelWidth, source.PixelHeight);
            if (_imageSize != size || _rotation != rotation || _frame != frame || _zoom != zoom) EndDrag();
            _preview.Source = source; _preview.Width = size.Width; _preview.Height = size.Height;
            _imageSize = size; _rotation = rotation; _frame = frame; _zoom = zoom; _offset = offset;
            UpdateGeometry();
        }

        internal void Clear()
        {
            EndDrag(); _preview.Source = null; ImageBounds = ViewportBounds = Rect.Empty;
            _outline.SetBounds(Rect.Empty, Rect.Empty);
        }

        internal static Rect FitBounds(Size image, Size available)
        {
            if (image.Width <= 0 || image.Height <= 0 || available.Width <= 0 || available.Height <= 0) return Rect.Empty;
            double scale = Math.Min(available.Width / image.Width, available.Height / image.Height);
            return new Rect((available.Width - image.Width * scale) / 2, (available.Height - image.Height * scale) / 2,
                image.Width * scale, image.Height * scale);
        }

        internal static Rect VisibleBounds(Size image, Size frame, double zoom, Point offset, Rect bounds)
        {
            if (bounds.IsEmpty || zoom <= 0 || !ImageViewport.IsFinite(zoom)) return Rect.Empty;
            var visible = new Rect(-offset.X / zoom, -offset.Y / zoom, frame.Width / zoom, frame.Height / zoom);
            visible.Intersect(new Rect(image));
            if (visible.IsEmpty) return Rect.Empty;
            double scale = bounds.Width / image.Width;
            return new Rect(bounds.X + visible.X * scale, bounds.Y + visible.Y * scale, visible.Width * scale, visible.Height * scale);
        }

        private void UpdateGeometry()
        {
            if (Source == null) return;
            Size rotated = ImageViewport.RotatedSize(_imageSize, _rotation);
            ImageBounds = FitBounds(rotated, RenderSize);
            ViewportBounds = VisibleBounds(rotated, _frame, _zoom, _offset, ImageBounds);
            if (!ImageBounds.IsEmpty)
            {
                double scale = ImageBounds.Width / rotated.Width;
                Matrix transform = ImageViewport.RotationMatrix(_imageSize, _rotation);
                transform.Scale(scale, scale); transform.Translate(ImageBounds.X, ImageBounds.Y);
                _transform.Matrix = transform;
            }
            _outline.SetBounds(ImageBounds, ViewportBounds);
        }

        internal bool BeginDrag(Point point)
        {
            if (!IsVisible || Source == null || ViewportBounds.IsEmpty || !ImageBounds.Contains(point)) return false;
            Focus();
            Rect handle = ViewportBounds; handle.Inflate(4, 4);
            bool inside = handle.Contains(point);
            _grabOffset = inside ? new Point(point.X - ViewportBounds.X, point.Y - ViewportBounds.Y)
                : new Point(ViewportBounds.Width / 2, ViewportBounds.Height / 2);
            IsDragging = CaptureMouse();
            if (!IsDragging) return false;
            ToolTipService.SetIsEnabled(this, false);
            if (DragStarted != null) DragStarted();
            if (!inside) UpdateDrag(point, true);
            return true;
        }

        internal void UpdateDrag(Point point, bool pressed)
        {
            if (!IsDragging) return;
            if (!pressed || Source == null || ImageBounds.IsEmpty || !ImageViewport.IsFinite(point.X) || !ImageViewport.IsFinite(point.Y)) { EndDrag(); return; }
            Size rotated = ImageViewport.RotatedSize(_imageSize, _rotation);
            double scale = ImageBounds.Width / rotated.Width;
            ImageView view = ImageViewport.Constrain(rotated, _frame, _zoom,
                -(point.X - _grabOffset.X - ImageBounds.X) / scale * _zoom,
                -(point.Y - _grabOffset.Y - ImageBounds.Y) / scale * _zoom);
            if (PanRequested != null) PanRequested(new Point(view.X, view.Y));
        }

        internal void EndDrag()
        {
            bool wasDragging = IsDragging; IsDragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            ToolTipService.SetIsEnabled(this, true);
            if (wasDragging && DragEnded != null) DragEnded();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        { base.OnMouseLeftButtonDown(e); e.Handled = true; BeginDrag(e.GetPosition(this)); }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); if (IsDragging) { e.Handled = true; UpdateDrag(e.GetPosition(this), e.LeftButton == MouseButtonState.Pressed); } }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        { base.OnMouseLeftButtonUp(e); e.Handled = true; EndDrag(); }
        protected override void OnMouseWheel(MouseWheelEventArgs e) { e.Handled = true; }
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) { e.Handled = true; }

        private sealed class ViewportOutline : FrameworkElement
        {
            private Rect _image = Rect.Empty, _view = Rect.Empty;
            private readonly Brush _shade = new SolidColorBrush(Color.FromArgb(105, 0, 0, 0));
            private readonly Pen _edge = new Pen(Brushes.Black, 3), _line = new Pen(Brushes.White, 1.5);
            internal ViewportOutline() { _shade.Freeze(); _edge.Freeze(); _line.Freeze(); }
            internal void SetBounds(Rect image, Rect view)
            { if (_image == image && _view == view) return; _image = image; _view = view; InvalidateVisual(); }
            protected override void OnRender(DrawingContext dc)
            {
                if (_image.IsEmpty || _view.IsEmpty) return;
                dc.PushClip(new RectangleGeometry(_image));
                dc.DrawRectangle(_shade, null, new Rect(_image.Left, _image.Top, _image.Width, Math.Max(0, _view.Top - _image.Top)));
                dc.DrawRectangle(_shade, null, new Rect(_image.Left, _view.Bottom, _image.Width, Math.Max(0, _image.Bottom - _view.Bottom)));
                dc.DrawRectangle(_shade, null, new Rect(_image.Left, _view.Top, Math.Max(0, _view.Left - _image.Left), _view.Height));
                dc.DrawRectangle(_shade, null, new Rect(_view.Right, _view.Top, Math.Max(0, _image.Right - _view.Right), _view.Height));
                dc.DrawRectangle(null, _edge, _view); dc.DrawRectangle(null, _line, _view); dc.Pop();
            }
        }
    }
}
