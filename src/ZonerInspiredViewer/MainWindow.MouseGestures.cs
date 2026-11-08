using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZonerInspiredViewer
{
    internal enum ImageMouseGesture { None, FitWindow, OriginalSize, FitWidth, FitHeight, RotateLeft, RotateRight, ZoomLock, SlideshowStart, SlideshowResume, SlideshowRunning, AdaptiveZoom }

    internal sealed partial class MainWindow
    {
        private bool _mouseGesturesEnabled = true, _rightGestureActive, _rightGestureWheelUsed;
        private MouseGesturePath _rightGesturePath;
        private CheckBox _mouseGesturesCheckBox;

        private void BuildMouseGestureConfiguration()
        {
            _mouseGesturesCheckBox = new CheckBox { Content = "Right-button gestures in image view", IsChecked = _mouseGesturesEnabled };
            CommandPresentation.Describe(_mouseGesturesCheckBox, "Mouse gestures", null,
                "Right-button gestures: double-click toggles Zoom lock; triple-click starts/resumes slideshow. The double-click action waits briefly for a possible third click. Drag up = Fit window, down = Original size, left/right = Fit width/height; Up then Left/Right rotates 90 degrees. Each drag leg needs 40 pixels. Center cues preview and confirm actions. Wheel zoom cancels pending actions; rotation follows the auto-save setting.");
            RoutedEventHandler changed = delegate { SetMouseGesturesEnabled(_mouseGesturesCheckBox.IsChecked == true); };
            _mouseGesturesCheckBox.Checked += changed;
            _mouseGesturesCheckBox.Unchecked += changed;
            var controls = new StackPanel();
            controls.Children.Add(_mouseGesturesCheckBox);
            controls.Children.Add(new TextBlock { Text = "Hold right button, drag, then release.\nUp: Fit window    Down: Original size (100%)\nLeft: Fit width    Right: Fit height\nUp then Left: Rotate left 90\u00b0\nUp then Right: Rotate right 90\u00b0\nDouble right-click: Toggle Zoom lock\nTriple right-click: Start/resume slideshow",
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
            CancelRightClickAction();
            HideMouseGestureFeedback();
            _rightGesturePath = new MouseGesturePath(point);
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
                ? _rightGesturePath.Track(point) : ImageMouseGesture.None;
            CancelMouseGesture();
            switch (gesture)
            {
                case ImageMouseGesture.FitWindow: FitWindowFromGesture(); break;
                case ImageMouseGesture.OriginalSize: SetZoomOneToOne(); break;
                case ImageMouseGesture.FitWidth: FitImageAxis(true); break;
                case ImageMouseGesture.FitHeight: FitImageAxis(false); break;
                case ImageMouseGesture.RotateLeft:
                case ImageMouseGesture.RotateRight:
                    if (_rotationSaving || _fileTransferBusy || _backupBusy) return false;
                    RotateImage(gesture == ImageMouseGesture.RotateLeft ? -1 : 1);
                    break;
                default: return false;
            }
            ShowMouseGestureFeedback(gesture, true);
            return true;
        }

        private void CancelMouseGesture()
        {
            bool wasActive = _rightGestureActive;
            _rightGestureActive = false;
            _rightGestureWheelUsed = false;
            _rightGesturePath = null;
            CancelRightClickAction();
            HideMouseGestureFeedback();
            if (!wasActive) return;
            if (_imageCanvas.IsMouseCaptured) _imageCanvas.ReleaseMouseCapture();
            if (_slideshowTimer != null) ArmSlideshow();
        }

        private void ImageCanvasMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed && Keyboard.Modifiers == ModifierKeys.None)
                BeginRightButtonInput(e.GetPosition(_imageCanvas), e.ClickCount);
            e.Handled = true;
        }

        private void ImageCanvasMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_rightGestureActive) return;
            CompleteRightButtonInput(e.GetPosition(_imageCanvas));
            e.Handled = true;
        }
    }
}
