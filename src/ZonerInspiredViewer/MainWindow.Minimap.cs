using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private ThumbnailMinimap _thumbnailMinimap;
        private Grid _thumbnailWorkspace;
        private ColumnDefinition _minimapColumn;
        private ToggleButton _minimapButton;
        private Button _minimapToggle;
        private bool _minimapVisible;
        private int _minimapToggleVersion;

        private ToggleButton BuildMinimapButton()
        {
            _minimapButton = new CheckBox { Content = "Show minimap", VerticalAlignment = VerticalAlignment.Center };
            CommandPresentation.Describe(_minimapButton, "Thumbnail minimap", "T", "Toggle the experimental thumbnail minimap in the browser.");
            _minimapButton.Click += delegate { SetThumbnailMinimap(!_minimapVisible, true); };
            return _minimapButton;
        }

        private Grid BuildThumbnailWorkspace()
        {
            _thumbnailWorkspace = new Grid();
            _thumbnailWorkspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _thumbnailWorkspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            _minimapColumn = new ColumnDefinition { Width = new GridLength(0) };
            _thumbnailWorkspace.ColumnDefinitions.Add(_minimapColumn);
            _thumbnailWorkspace.Children.Add(_thumbnailGrid);
            _thumbnailMinimap = new ThumbnailMinimap(_thumbnailGrid, _services.Thumbnails) { Visibility = Visibility.Collapsed };
            Grid.SetColumn(_thumbnailMinimap, 2); _thumbnailWorkspace.Children.Add(_thumbnailMinimap);
            _minimapToggle = new Button { Width = 20, Height = 48, Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12 };
            _minimapToggle.Click += delegate { SetThumbnailMinimap(!_minimapVisible, true); };
            Grid.SetColumn(_minimapToggle, 1); _thumbnailWorkspace.Children.Add(_minimapToggle);
            UpdateMinimapToggle();
            _thumbnailWorkspace.SizeChanged += delegate { UpdateMinimapWidth(); };
            return _thumbnailWorkspace;
        }

        private void UpdateMinimapWidth()
        {
            double width = _minimapVisible ? Math.Min(168, Math.Max(0, _thumbnailWorkspace.ActualWidth * 0.24)) : 0;
            if (Math.Abs(_minimapColumn.Width.Value - width) > 0.5) _minimapColumn.Width = new GridLength(width);
        }

        private void SetThumbnailMinimap(bool visible, bool preservePosition)
        {
            _minimapButton.IsChecked = visible;
            if (_minimapVisible == visible) return;
            int revision = _thumbnailGrid.ItemsRevision, toggleVersion = ++_minimapToggleVersion;
            int anchor = (int)Math.Floor(_thumbnailGrid.VerticalOffset / _thumbnailGrid.TileHeight) * _thumbnailGrid.ColumnCount;
            double fraction = _thumbnailGrid.VerticalOffset % _thumbnailGrid.TileHeight / _thumbnailGrid.TileHeight;
            bool atEnd = _thumbnailGrid.ScrollableHeight > 0 && _thumbnailGrid.VerticalOffset >= _thumbnailGrid.ScrollableHeight - 1;
            _minimapVisible = visible;
            UpdateMinimapToggle();
            _thumbnailMinimap.EndDrag();
            _thumbnailMinimap.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _thumbnailGrid.VerticalScrollBarVisibility = visible ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
            UpdateMinimapWidth();
            if (preservePosition)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                {
                    if (_isClosed || toggleVersion != _minimapToggleVersion || revision != _thumbnailGrid.ItemsRevision) return;
                    _thumbnailWorkspace.UpdateLayout();
                    _thumbnailGrid.ScrollToVerticalOffset(atEnd ? _thumbnailGrid.ScrollableHeight
                        : (anchor / Math.Max(1, _thumbnailGrid.ColumnCount) + fraction) * _thumbnailGrid.TileHeight);
                    _thumbnailGrid.Focus();
                }));
            ScheduleSessionSave();
        }

        private void UpdateMinimapToggle()
        {
            _minimapToggle.Content = _minimapVisible ? "\uE76C" : "\uE76B";
            string label = _minimapVisible ? "Hide thumbnail minimap" : "Show thumbnail minimap";
            CommandPresentation.Describe(_minimapToggle, label, "T", "Toggle the experimental thumbnail minimap in the browser.");
            AutomationProperties.SetName(_minimapToggle, label);
        }
    }
}
