using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed partial class FacesWindow
    {
        private ImageNavigator _canvasNavigator;
        private Border _canvasNavigatorOverlay;

        private void BuildCanvasNavigator(Grid frame)
        {
            _canvasNavigator = new ImageNavigator();
            AutomationProperties.SetName(_canvasNavigator, "Canvas navigator");
            CommandPresentation.Describe(_canvasNavigator, "Canvas navigator", null, "Drag the visible-area rectangle to pan around the canvas. Click another area to move there.");
            _canvasNavigator.DragStarted += EndPan;
            _canvasNavigator.PanRequested += delegate(Point offset)
            {
                if (_busy || _framingPending || _closed || _canvasSnapshot != null) { _canvasNavigator.EndDrag(); return; }
                _viewport.ScrollToHorizontalOffset(Math.Max(0, -offset.X));
                _viewport.ScrollToVerticalOffset(Math.Max(0, -offset.Y));
            };
            _canvasNavigatorOverlay = new Border { Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(175, 15, 18, 21)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Child = _canvasNavigator, Visibility = Visibility.Collapsed };
            Panel.SetZIndex(_canvasNavigatorOverlay, 5); frame.Children.Add(_canvasNavigatorOverlay);
        }

        private void HideCanvasNavigator()
        {
            if (_canvasNavigatorOverlay == null) return;
            _canvasNavigatorOverlay.Visibility = Visibility.Collapsed; _canvasNavigator.Clear();
        }

        private void UpdateCanvasNavigator()
        {
            if (_canvasNavigatorOverlay == null) return;
            var source = _preview.Source as BitmapSource;
            double width = _viewport.ViewportWidth, height = _viewport.ViewportHeight;
            if (_closed || _busy || _framingPending || _canvasResizePending || _canvasResizeRunning || _canvasSnapshot != null || _fit || source == null || width <= 0 || height <= 0
                || (_canvasSize.Width * _zoom <= width + .5 && _canvasSize.Height * _zoom <= height + .5))
            { HideCanvasNavigator(); return; }
            _canvasNavigatorOverlay.Width = Math.Min(192, width * .4);
            _canvasNavigatorOverlay.Height = Math.Min(136, height * .4);
            _canvasNavigatorOverlay.Margin = new Thickness(10, 10, Math.Max(10, _viewport.ActualWidth - width + 10), Math.Max(10, _viewport.ActualHeight - height + 10));
            _canvasNavigatorOverlay.Visibility = Visibility.Visible;
            var offset = new Point(Math.Max(0, (width - _canvasSize.Width * _zoom) / 2) - _viewport.HorizontalOffset,
                Math.Max(0, (height - _canvasSize.Height * _zoom) / 2) - _viewport.VerticalOffset);
            // The preview is downsampled; geometry follows full canvas pixels, not thumbnail pixels.
            _canvasNavigator.SetView(source, 0, new Size(width, height), _zoom, offset, _canvasSize);
        }
    }
}
