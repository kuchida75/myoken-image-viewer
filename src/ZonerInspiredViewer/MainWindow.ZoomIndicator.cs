using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Border _zoomIndicator;
        private TextBlock _zoomIndicatorText;
        private DispatcherTimer _zoomIndicatorTimer;
        private double _lastIndicatedZoom = Double.NaN;

        private void BuildZoomIndicator()
        {
            _zoomIndicatorText = new TextBlock
            {
                FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            _zoomIndicator = new Border
            {
                Width = 104, Height = 36, CornerRadius = new CornerRadius(4), Margin = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(168, 15, 18, 21)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)), BorderThickness = new Thickness(1),
                Child = _zoomIndicatorText, Visibility = Visibility.Collapsed, IsHitTestVisible = false, Focusable = false
            };
            AutomationProperties.SetName(_zoomIndicator, "Image zoom");
            Panel.SetZIndex(_zoomIndicator, 10);
            _imageView.Children.Add(_zoomIndicator);
            _zoomIndicatorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _zoomIndicatorTimer.Tick += delegate { HideZoomIndicator(); };
        }

        private void UpdateZoomIndicator()
        {
            if (_zoomIndicator == null || _isClosed || !HasCurrentImage() || _imageView.Visibility != Visibility.Visible) return;
            double zoom = _imageScale.ScaleX;
            if (!ImageViewport.IsFinite(zoom) || zoom <= 0) { ResetZoomIndicator(); return; }
            if (Math.Abs(zoom - _lastIndicatedZoom) < 0.000001) return;
            _lastIndicatedZoom = zoom;
            if (Math.Abs(zoom - 1) < 0.000001) { HideZoomIndicator(); return; }

            // Extra precision near 1x avoids reporting a rounded 1x for a fitted image.
            string format = Math.Abs(zoom - 1) < 0.005 ? "0.######" : "0.###";
            _zoomIndicatorText.Text = zoom.ToString(format, CultureInfo.InvariantCulture) + "x";
            AutomationProperties.SetName(_zoomIndicatorText, "Zoom " + _zoomIndicatorText.Text);
            _zoomIndicator.Visibility = Visibility.Visible;
            _zoomIndicatorTimer.Stop();
            _zoomIndicatorTimer.Start();
        }

        private void HideZoomIndicator()
        {
            if (_zoomIndicatorTimer != null) _zoomIndicatorTimer.Stop();
            if (_zoomIndicator != null) _zoomIndicator.Visibility = Visibility.Collapsed;
        }

        private void ResetZoomIndicator()
        {
            HideZoomIndicator();
            _lastIndicatedZoom = Double.NaN;
        }
    }
}
