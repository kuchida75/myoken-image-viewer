using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Border _mouseGestureFeedback;
        private TextBlock _mouseGestureArrow, _mouseGestureAction, _mouseGestureHint;
        private DispatcherTimer _mouseGestureFeedbackTimer;

        private void BuildMouseGestureFeedback()
        {
            _mouseGestureArrow = new TextBlock { FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 52,
                Foreground = new SolidColorBrush(Color.FromRgb(111, 226, 207)), TextAlignment = TextAlignment.Center };
            _mouseGestureAction = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            _mouseGestureHint = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(204, 214, 218)),
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
            var content = new StackPanel();
            content.Children.Add(_mouseGestureArrow); content.Children.Add(_mouseGestureAction); content.Children.Add(_mouseGestureHint);
            _mouseGestureFeedback = new Border { Child = content, Padding = new Thickness(24, 12, 24, 18),
                Margin = new Thickness(16), MaxWidth = 290, CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(206, 14, 20, 24)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(84, 164, 229, 220)), BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed, IsHitTestVisible = false, Focusable = false, ClipToBounds = true };
            AutomationProperties.SetName(_mouseGestureFeedback, "Mouse gesture");
            Panel.SetZIndex(_mouseGestureFeedback, 40);
            _imageView.Children.Add(_mouseGestureFeedback);
            _mouseGestureFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _mouseGestureFeedbackTimer.Tick += delegate { HideMouseGestureFeedback(); };
        }

        private void UpdateMouseGestureFeedback(Point point)
        {
            TrackRightClickMovement(point);
            if (!_rightGestureActive || _rightGestureWheelUsed || !_imageView.IsVisible || !HasCurrentImage()
                || !new Rect(_imageCanvas.RenderSize).Contains(point)) { HideMouseGestureFeedback(); return; }
            ShowMouseGestureFeedback(_rightGesturePath.Track(point), false);
        }

        private void ShowMouseGestureFeedback(ImageMouseGesture gesture, bool applied)
        {
            if (_mouseGestureFeedback == null || _isClosed || !_imageView.IsVisible || gesture == ImageMouseGesture.None)
            { HideMouseGestureFeedback(); return; }
            string arrow, action;
            switch (gesture)
            {
                case ImageMouseGesture.FitWindow: arrow = "\u2191"; action = "Fit window"; break;
                case ImageMouseGesture.OriginalSize: arrow = "\u2193"; action = "Original size (100%)"; break;
                case ImageMouseGesture.FitWidth: arrow = "\u2190"; action = "Fit width"; break;
                case ImageMouseGesture.FitHeight: arrow = "\u2192"; action = "Fit height"; break;
                case ImageMouseGesture.RotateLeft: arrow = "\u2191 \u2190"; action = "Rotate left 90\u00b0"; break;
                case ImageMouseGesture.RotateRight: arrow = "\u2191 \u2192"; action = "Rotate right 90\u00b0"; break;
                case ImageMouseGesture.ZoomLock: arrow = "2\u00d7"; action = _zoomLocked ? "Zoom locked" : "Zoom unlocked"; break;
                case ImageMouseGesture.AdaptiveZoom: arrow = "\u2194"; action = _adaptiveZoomEnabled ? "Auto zoom: On" : "Auto zoom: Off"; break;
                case ImageMouseGesture.SlideshowStart: arrow = "\u25b6"; action = "Slideshow started"; break;
                case ImageMouseGesture.SlideshowResume: arrow = "\u25b6"; action = "Slideshow resumed"; break;
                case ImageMouseGesture.SlideshowRunning: arrow = "\u25b6"; action = "Slideshow running"; break;
                default: HideMouseGestureFeedback(); return;
            }
            _mouseGestureFeedbackTimer.Stop();
            _mouseGestureArrow.Text = arrow;
            _mouseGestureAction.Text = action;
            _mouseGestureHint.Text = applied ? "Applied" : "Release to apply";
            AutomationProperties.SetName(_mouseGestureFeedback, action + ": " + _mouseGestureHint.Text);
            _mouseGestureFeedback.Visibility = Visibility.Visible;
            if (applied) _mouseGestureFeedbackTimer.Start();
        }

        private void HideMouseGestureFeedback()
        {
            if (_mouseGestureFeedbackTimer != null) _mouseGestureFeedbackTimer.Stop();
            if (_mouseGestureFeedback != null) _mouseGestureFeedback.Visibility = Visibility.Collapsed;
        }
    }
}
