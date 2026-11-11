using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private FrameworkElement _imageStatusBar;
        private TextBlock _imageStatusPath, _itemCountText;
        private ImageFileItem _thumbnailMetadataItem;
        private readonly TextBlock[] _imageStatusValues = new TextBlock[6];
        private static readonly string[] ImageStatusLabels = { "Size: ", "Type: ", "Name: ", "Last modified: ", "Resolution: ", "Bit depth: " };

        private FrameworkElement BuildImageStatusBar()
        {
            var content = new StackPanel { Margin = new Thickness(10, 5, 10, 3) };
            _imageStatusPath = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 0, 0, 4), Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(_imageStatusPath, "Image file path");
            ThemeManager.Bind(_imageStatusPath, TextBlock.ForegroundProperty, ThemeKeys.Text);
            content.Children.Add(_imageStatusPath);

            var details = new WrapPanel();
            content.Children.Add(details);
            _itemCountText = new TextBlock { Margin = new Thickness(0, 0, 14, 4),
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(_itemCountText, "Current folder file count");
            ThemeManager.Bind(_itemCountText, TextBlock.ForegroundProperty, ThemeKeys.Text);
            details.Children.Add(_itemCountText);
            for (int i = 0; i < _imageStatusValues.Length; i++)
            {
                var value = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 14, 4), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
                ThemeManager.Bind(value, TextBlock.ForegroundProperty, ThemeKeys.Text);
                AutomationProperties.SetName(value, "Image " + ImageStatusLabels[i].TrimEnd(':', ' ').ToLowerInvariant());
                _imageStatusValues[i] = value; details.Children.Add(value);
            }
            details.SizeChanged += delegate
            {
                double maxWidth = Math.Max(1, details.ActualWidth - 14);
                _itemCountText.MaxWidth = maxWidth;
                for (int i = 0; i < _imageStatusValues.Length; i++)
                    _imageStatusValues[i].MaxWidth = i == 2 ? Math.Min(240, maxWidth) : maxWidth;
            };
            var band = new Border { Child = content, BorderThickness = new Thickness(0, 1, 0, 0) };
            ThemeManager.Bind(band, Border.BackgroundProperty, ThemeKeys.ToolbarBackground);
            ThemeManager.Bind(band, Border.BorderBrushProperty, ThemeKeys.Border);
            _imageStatusBar = band;
            return band;
        }

        private void UpdateThumbnailMetadataRequest(ImageFileItem item)
        {
            // A rescan replaces the catalog items, even when selection restores the same path.
            if (_isClosed || Object.ReferenceEquals(item, _thumbnailMetadataItem)) return;
            _thumbnailMetadataItem = item;
            if (item != null && !item.IsDirectory)
            {
                LoadMetadata(item.Path);
                return;
            }

            _metadataLoadVersion++;
            _imageOverlayMetadata = null;
            _metadataPanel.ShowMessage(item == null ? "" : "Folder: " + item.Path);
        }

        private void UpdateImageStatusBar(string[] details)
        {
            if (_imageStatusPath == null) return;
            _filmstripSizeControls.Visibility = details != null ? Visibility.Visible : Visibility.Collapsed;
            UpdateUpscaleSizeControl();
            string path = details != null ? _displayedImagePath : null;
            if (!_isClosed && _browserView.Visibility == Visibility.Visible)
            {
                ImageFileItem item = _thumbnailGrid.SelectedItem;
                if (item != null && !item.IsDirectory)
                {
                    path = item.Path;
                    ImageMetadata metadata = Object.ReferenceEquals(item, _thumbnailMetadataItem)
                        && _imageOverlayMetadata != null && String.Equals(_imageOverlayMetadata.Path, path, StringComparison.OrdinalIgnoreCase)
                        ? _imageOverlayMetadata : null;
                    FileRevision revision = metadata != null ? _imageOverlayMetadataRevision
                        : new FileRevision { Length = item.Length, Modified = item.LastWriteUtc.Ticks };
                    details = DescribeImage(path, revision, metadata);
                }
            }
            bool visible = details != null;
            _imageStatusPath.Text = path ?? "";
            _imageStatusPath.ToolTip = path;
            _imageStatusPath.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < _imageStatusValues.Length; i++)
            {
                TextBlock value = _imageStatusValues[i];
                value.Text = visible ? ImageStatusLabels[i] + details[i] : "";
                value.ToolTip = visible ? value.Text : null;
                value.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
