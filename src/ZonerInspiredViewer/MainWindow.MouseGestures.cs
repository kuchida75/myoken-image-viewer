using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZonerInspiredViewer
{
    internal enum ImageMouseGesture { None, FitWindow, OriginalSize, FitWidth, FitHeight }

    internal sealed partial class MainWindow
    {
        private bool _mouseGesturesEnabled = true, _rightGestureActive, _rightGestureWheelUsed;
        private Point _rightGestureStart;
        private CheckBox _mouseGesturesCheckBox;

        private void BuildMouseGestureConfiguration()
        {
            _mouseGesturesCheckBox = new CheckBox { Content = "Right-button drag gestures in image view", IsChecked = _mouseGesturesEnabled };
            CommandPresentation.Describe(_mouseGesturesCheckBox, "Mouse gestures", null,
                "Hold the right mouse button, drag at least 40 pixels and release: up = Fit window, down = Original size (100%), left = Fit width, right = Fit height. Right-button wheel zoom cancels the pending gesture. Applies only to the image, including edge and fullscreen modes.");
            RoutedEventHandler changed = delegate { SetMouseGesturesEnabled(_mouseGesturesCheckBox.IsChecked == true); };
            _mouseGesturesCheckBox.Checked += changed;
            _mouseGesturesCheckBox.Unchecked += changed;
            var controls = new StackPanel();
            controls.Children.Add(_mouseGesturesCheckBox);
            controls.Children.Add(new TextBlock { Text = "Hold right button, drag, then release.\nUp: Fit window    Down: Original size (100%)\nLeft: Fit width    Right: Fit height",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            AddConfigurationRow(0, "Mouse gestures", controls);
        }

        private void SetMouseGesturesEnabled(bool enabled)
        {
            if (_mouseGesturesEnabled == enabled) return;
            _mouseGesturesEnabled = enabled;
            if (!enabled) CancelMouseGesture();
            if (_mouseGesturesCheckBox != null) _mouseGesturesCheckBox.IsChecked = enabled;
            ScheduleSessionSave();
        }

        internal static ImageMouseGesture ClassifyMouseGesture(Point start, Point end)
        {
            double x = end.X - start.X, y = end.Y - start.Y;
            if (!ImageViewport.IsFinite(x) || !ImageViewport.IsFinite(y)) return ImageMouseGesture.None;
            double width = Math.Abs(x), height = Math.Abs(y);
            // WPF positions are device-independent: the gesture distance scales with display DPI.
            if (Math.Max(width, height) < 40) return ImageMouseGesture.None;
            if (width >= height * 1.35) return x < 0 ? ImageMouseGesture.FitWidth : ImageMouseGesture.FitHeight;
            if (height >= width * 1.35) return y < 0 ? ImageMouseGesture.FitWindow : ImageMouseGesture.OriginalSize;
            return ImageMouseGesture.None;
        }

        private bool BeginMouseGesture(Point point)
        {
            if (!_mouseGesturesEnabled || _rightGestureActive || _isPanning || _isClosed
                || !_imageView.IsVisible || !HasCurrentImage() || Mouse.Captured != null) return false;
            _imageCanvas.Focus();
            _rightGestureStart = point;
            _rightGestureWheelUsed = false;
            _rightGestureActive = _imageCanvas.CaptureMouse();
            if (_rightGestureActive && _slideshowTimer != null) _slideshowTimer.Stop();
            return _rightGestureActive;
        }

        private bool CompleteMouseGesture(Point point)
        {
            if (!_rightGestureActive) return false;
            ImageMouseGesture gesture = _mouseGesturesEnabled && !_rightGestureWheelUsed && _imageCanvas.IsMouseCaptured
                && _imageView.IsVisible && HasCurrentImage() && new Rect(_imageCanvas.RenderSize).Contains(point)
                ? ClassifyMouseGesture(_rightGestureStart, point) : ImageMouseGesture.None;
            CancelMouseGesture();
            switch (gesture)
            {
                case ImageMouseGesture.FitWindow: FitImageToView(true); break;
                case ImageMouseGesture.OriginalSize: SetZoomOneToOne(); break;
                case ImageMouseGesture.FitWidth: FitImageAxis(true); break;
                case ImageMouseGesture.FitHeight: FitImageAxis(false); break;
                default: return false;
            }
            return true;
        }

        private void CancelMouseGesture()
        {
            bool wasActive = _rightGestureActive;
            _rightGestureActive = false;
            _rightGestureWheelUsed = false;
            if (!wasActive) return;
            if (_imageCanvas.IsMouseCaptured) _imageCanvas.ReleaseMouseCapture();
            if (_slideshowTimer != null) ArmSlideshow();
        }

        private void ImageCanvasMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed && Keyboard.Modifiers == ModifierKeys.None)
                BeginMouseGesture(e.GetPosition(_imageCanvas));
            e.Handled = true;
        }

        private void ImageCanvasMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_rightGestureActive) return;
            CompleteMouseGesture(e.GetPosition(_imageCanvas));
            e.Handled = true;
        }
    }
}
