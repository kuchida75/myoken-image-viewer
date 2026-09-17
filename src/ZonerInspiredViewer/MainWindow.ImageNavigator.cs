using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private ImageNavigator _imageNavigator;
        private Border _imageNavigatorOverlay;
        private CheckBox _configureImageNavigator;
        private bool _imageNavigatorEnabled = true;

        private void BuildImageNavigator()
        {
            _imageNavigator = new ImageNavigator();
            CommandPresentation.Describe(_imageNavigator, "Image navigator", "P", "Drag the visible-area rectangle to pan. Click elsewhere in the thumbnail to move there.");
            _imageNavigator.PanRequested += delegate(Point offset)
            {
                if (!HasCurrentImage() || _imageView.Visibility != Visibility.Visible) { _imageNavigator.EndDrag(); return; }
                _imageTranslate.X = offset.X; _imageTranslate.Y = offset.Y; MarkActiveViewCustom();
            };
            _imageNavigator.DragStarted += delegate { EndImagePan(); _slideshowTimer.Stop(); };
            _imageNavigator.DragEnded += delegate { ArmSlideshow(); };
            _imageNavigatorOverlay = new Border { Width = 208, Height = 148, Padding = new Thickness(8), Margin = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromArgb(168, 15, 18, 21)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Child = _imageNavigator, Visibility = Visibility.Collapsed, ClipToBounds = true };
            _imageNavigatorOverlay.SizeChanged += delegate { LayoutImageOverlays(); };
            _imageNavigatorOverlay.PreviewMouseWheel += delegate(object sender, System.Windows.Input.MouseWheelEventArgs e) { e.Handled = true; };
            Panel.SetZIndex(_imageNavigatorOverlay, 20); _imageView.Children.Add(_imageNavigatorOverlay);
        }

        private void SetImageNavigator(bool enabled)
        {
            _imageNavigatorEnabled = enabled;
            if (_configureImageNavigator != null) _configureImageNavigator.IsChecked = enabled;
            UpdateImageNavigator(); ScheduleSessionSave();
        }

        private void HideImageNavigator()
        {
            if (_imageNavigatorOverlay == null) return;
            _imageNavigatorOverlay.Visibility = Visibility.Collapsed; _imageNavigator.Clear(); LayoutImageOverlays();
        }

        private void UpdateImageNavigator()
        {
            if (_imageNavigatorOverlay == null) return;
            if (_isClosed || !_imageNavigatorEnabled || _imageView.Visibility != Visibility.Visible || !HasCurrentImage())
            { HideImageNavigator(); return; }
            Size size = DisplayedImageSize();
            if (size.Width * _imageScale.ScaleX <= _imageCanvas.ActualWidth + 0.5
                && size.Height * _imageScale.ScaleY <= _imageCanvas.ActualHeight + 0.5)
            { HideImageNavigator(); return; }
            _imageNavigatorOverlay.Visibility = Visibility.Visible;
            _imageNavigator.SetView((BitmapSource)_mainImage.Source, CurrentTabState().RotationQuarterTurns, _imageCanvas.RenderSize,
                _imageScale.ScaleX, new Point(_imageTranslate.X, _imageTranslate.Y));
            LayoutImageOverlays();
        }
    }
}
