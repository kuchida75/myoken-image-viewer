using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private ToggleButton _zoomLockButton;
        private Button _fitWidthButton, _fitHeightButton, _actualSizeButton;
        private bool _zoomLocked;
        private double _lockedZoom = 1;

        private void AddZoomControls(Panel toolbar)
        {
            _zoomLockButton = new ToggleButton { Width = 32, Height = 32,
                Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(4), Focusable = false };
            _zoomLockButton.Click += delegate { ToggleZoomLock(); };
            toolbar.Children.Add(_zoomLockButton);
            _fitWidthButton = CommandPresentation.Button("\uE7EA", "Fit width", null,
                "Fit the image to the frame width, preserving proportions. Drag to pan vertically. Small images may be enlarged.",
                delegate { FitImageAxis(true); }, false);
            _fitHeightButton = CommandPresentation.Button("\uE7EB", "Fit height", null,
                "Fit the image to the frame height, preserving proportions. Drag to pan horizontally. Small images may be enlarged.",
                delegate { FitImageAxis(false); }, false);
            _fitWidthButton.Content = AxisFitIcon(_fitWidthButton, false);
            _fitHeightButton.Content = AxisFitIcon(_fitHeightButton, true);
            _actualSizeButton = CommandPresentation.Button("\uE799", "Actual size (100%)", null,
                "Show the displayed image at 100% zoom (1:1), centered in the frame.", SetZoomOneToOne, true);
            _actualSizeButton.Content = CommandPresentation.Label("\uE799", "1:1");
            foreach (Button button in new[] { _fitWidthButton, _fitHeightButton, _actualSizeButton })
            {
                button.Focusable = false;
                toolbar.Children.Add(button);
            }
            UpdateZoomControls();
        }

        private static FrameworkElement AxisFitIcon(Button owner, bool vertical)
        {
            var icon = new Border { Width = 18, Height = 16, BorderThickness = new Thickness(1, 0, 1, 0),
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(vertical ? 90 : 0),
                Child = new TextBlock { Text = "\uE973\uE974", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 8, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center } };
            icon.SetBinding(Border.BorderBrushProperty, new Binding("Foreground") { Source = owner });
            return icon;
        }

        private void ToggleZoomLock()
        {
            _zoomLocked = !_zoomLocked;
            RememberLockedZoom();
            if (_zoomLocked && HasCurrentImage()) MarkActiveViewCustom();
            UpdateZoomControls();
            ScheduleSessionSave();
        }

        private void RememberLockedZoom()
        {
            // Store source-relative zoom so an AI-resolution change does not change apparent size.
            if (_zoomLocked && HasCurrentImage()) _lockedZoom = _imageScale.ScaleX * _autoUpscaleFactor;
        }

        private void RestoreZoomSettings(SessionState state)
        {
            _zoomLocked = state.ZoomLocked;
            _lockedZoom = IsValidLockedZoom(state.LockedZoom) ? state.LockedZoom : 1;
            UpdateZoomControls();
        }

        internal static bool IsValidLockedZoom(double value)
        {
            return ImageViewport.IsFinite(value) && value > 0 && value <= 192;
        }

        private void UpdateZoomControls()
        {
            if (_zoomLockButton == null) return;
            _zoomLockButton.IsChecked = _zoomLocked;
            _zoomLockButton.Content = CommandPresentation.Label(_zoomLocked ? "\uE72E" : "\uE785", null);
            CommandPresentation.Describe(_zoomLockButton, "Zoom lock", null, _zoomLocked
                ? "On: keep zoom when changing images and tabs. Zoom controls change the locked level. Click to unlock."
                : "Keep the current zoom level when changing images and tabs. Click to lock.");
            bool ready = !_isClosed && _imageView != null && _imageView.IsVisible && HasCurrentImage();
            foreach (Button button in new[] { _fitWidthButton, _fitHeightButton, _actualSizeButton })
                if (button != null) button.IsEnabled = ready;
        }

        private ImageView RestoreZoomView(ImageTabState state)
        {
            Size image = DisplayedImageSize(), frame = _imageCanvas.RenderSize;
            if (!_zoomLocked) return ImageViewport.Restore(image, frame, UpscaleViewState(state));
            double zoom = Math.Max(0.02, Math.Min(64, _lockedZoom / _autoUpscaleFactor));
            ImageView view = state.HasCustomView && Math.Abs(state.Zoom - _lockedZoom) < 0.000001
                ? ImageViewport.Restore(image, frame, UpscaleViewState(state))
                : ImageViewport.Center(image, frame, zoom);
            state.HasCustomView = true;
            return view;
        }

        private void FitImageAxis(bool width)
        {
            if (!HasCurrentImage() || !_imageView.IsVisible) return;
            EndImagePan();
            _imageNavigator.EndDrag();
            SetImageView(ImageViewport.FitAxis(DisplayedImageSize(), _imageCanvas.RenderSize, width));
            MarkActiveViewCustom();
        }

        private void FitImageAfterContentChange()
        {
            if (!_zoomLocked) { FitImageToView(true); return; }
            SetImageView(ImageViewport.Center(DisplayedImageSize(), _imageCanvas.RenderSize, _lockedZoom / _autoUpscaleFactor));
            MarkActiveViewCustom();
        }
    }
}
