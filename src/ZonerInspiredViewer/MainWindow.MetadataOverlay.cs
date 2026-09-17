using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Border _imageMetadataOverlay;
        private readonly TextBlock[] _imageMetadataValues = new TextBlock[6];
        private bool _imageMetadataOverlayEnabled;
        private ImageMetadata _imageOverlayMetadata;
        private FileRevision _imageOverlayMetadataRevision;

        private void BuildImageMetadataOverlay()
        {
            string[] labels = { "File size", "File type", "File name", "Modified", "Resolution", "Bit depth" };
            var rows = new Grid();
            rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            rows.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < labels.Length; i++)
            {
                rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock { Text = labels[i], FontSize = 12, Foreground = Brushes.White,
                    Opacity = 0.72, Margin = new Thickness(0, 3, 8, 3) };
                var value = new TextBlock { FontSize = 13, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 2) };
                AutomationProperties.SetName(value, labels[i]);
                Grid.SetRow(label, i); Grid.SetRow(value, i); Grid.SetColumn(value, 1);
                rows.Children.Add(label); rows.Children.Add(value); _imageMetadataValues[i] = value;
            }
            _imageMetadataOverlay = new Border
            {
                Width = 420, MaxWidth = 420, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(16),
                CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom, ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromArgb(168, 15, 18, 21)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)), BorderThickness = new Thickness(1),
                Child = rows, Visibility = Visibility.Collapsed, IsHitTestVisible = false, Focusable = false
            };
            AutomationProperties.SetName(_imageMetadataOverlay, "Image metadata");
            Panel.SetZIndex(_imageMetadataOverlay, 10);
            _imageView.Children.Add(_imageMetadataOverlay);
            _imageView.SizeChanged += delegate { LayoutImageOverlays(); };
        }

        private void SetImageMetadataOverlay(bool enabled)
        {
            _imageMetadataOverlayEnabled = enabled;
            if (_configureImageMetadata != null) _configureImageMetadata.IsChecked = enabled;
            UpdateImageMetadataOverlay();
            ScheduleSessionSave();
        }

        private void UpdateImageMetadataOverlay()
        {
            if (_imageMetadataOverlay == null) return;
            string[] details = DisplayedImageDetails();
            UpdateImageStatusBar(details);
            if (!_imageMetadataOverlayEnabled || details == null || !HasCurrentImage())
            {
                _imageMetadataOverlay.Visibility = Visibility.Collapsed;
                LayoutImageOverlays();
                return;
            }

            for (int i = 0; i < details.Length; i++) _imageMetadataValues[i].Text = details[i];
            _imageMetadataOverlay.Visibility = Visibility.Visible;
            LayoutImageOverlays();
        }

        private string[] DisplayedImageDetails()
        {
            // Layout may still be settling; loaded pixels remain valid while the frame resizes.
            if (_isClosed || _imageView == null || _imageView.Visibility != Visibility.Visible
                || _mainImage == null || _mainImage.Source == null || String.IsNullOrEmpty(_displayedImagePath)
                || !String.Equals(_displayedImagePath, _activeTabPath, StringComparison.OrdinalIgnoreCase)) return null;

            // Metadata and pixels must refer to the same file revision, including during watcher reloads.
            ImageMetadata metadata = _imageOverlayMetadata;
            if (metadata != null && (!String.Equals(metadata.Path, _displayedImagePath, StringComparison.OrdinalIgnoreCase)
                || !_imageOverlayMetadataRevision.Equals(_displayedImageRevision))) metadata = null;
            string extension = Path.GetExtension(_displayedImagePath).ToLowerInvariant();
            string type = metadata == null || String.IsNullOrEmpty(metadata.FileType)
                ? extension.TrimStart('.').ToUpperInvariant() : metadata.FileType;
            var bitmap = (BitmapSource)_mainImage.Source;
            int width = metadata != null && metadata.PixelWidth > 0 ? metadata.PixelWidth : bitmap.PixelWidth;
            int height = metadata != null && metadata.PixelHeight > 0 ? metadata.PixelHeight : bitmap.PixelHeight;
            return new[]
            {
                _displayedImageRevision.Length >= 0 ? ImageExtensions.FormatBytes(_displayedImageRevision.Length) : "Unavailable",
                type + " (" + extension + ")",
                Path.GetFileName(_displayedImagePath),
                _displayedImageRevision.Modified > 0
                    ? new DateTime(_displayedImageRevision.Modified, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "Unavailable",
                width + " x " + height + " px",
                metadata == null || String.IsNullOrEmpty(metadata.BitDepth) ? "Unavailable" : metadata.BitDepth
            };
        }
    }
}
