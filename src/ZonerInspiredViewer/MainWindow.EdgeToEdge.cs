using System.Windows;
using System.Windows.Controls;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Grid _edgeRoot, _edgeBody, _edgeCenter, _edgeWorkspace;
        private FrameworkElement _edgeTabs, _edgeFooter;
        private CheckBox _edgeCheck;
        private bool _edgeEnabled, _edgeActive, _edgeBarsShown = true;

        private void InitializeEdgeViewing(Grid root)
        {
            _edgeRoot = root;
            foreach (FrameworkElement bar in EdgeBars()) bar.SizeChanged += delegate { PositionEdgeBars(); };
        }

        private FrameworkElement[] EdgeBars()
        { return new[] { (FrameworkElement)_topToolbar, _edgeTabs, _edgeFooter, _performanceBar }; }

        private void SetEdgeViewing(bool enabled)
        {
            _edgeEnabled = enabled;
            if (_edgeCheck != null) _edgeCheck.IsChecked = enabled;
            UpdateEdgeViewing();
            ScheduleSessionSave();
        }

        private void UpdateEdgeViewing()
        {
            if (_edgeRoot == null) return;
            bool active = _edgeEnabled && _imageView.Visibility == Visibility.Visible;
            if (active == _edgeActive) return;
            EndImagePan();
            _edgeActive = active;
            if (active)
            {
                _edgeWorkspace.Children.Remove(_imageView);
                _edgeRoot.Children.Add(_imageView);
                Grid.SetRow(_imageView, 0); Grid.SetRowSpan(_imageView, 3); Panel.SetZIndex(_imageView, 5);
                foreach (FrameworkElement bar in new[] { _edgeTabs, _edgeFooter })
                {
                    _edgeCenter.Children.Remove(bar); _edgeRoot.Children.Add(bar);
                    Grid.SetRow(bar, 0); Grid.SetRowSpan(bar, 3); Panel.SetZIndex(bar, 10);
                }
                _edgeTabs.VerticalAlignment = VerticalAlignment.Top;
                _edgeFooter.VerticalAlignment = VerticalAlignment.Bottom;
                _edgeBody.Visibility = Visibility.Hidden;
                Panel.SetZIndex(_topToolbar, 10); Panel.SetZIndex(_performanceBar, 10);
                SetEdgeBars(false);
                _imageCanvas.Focus();
            }
            else
            {
                SetEdgeBars(true);
                _edgeRoot.Children.Remove(_imageView); _edgeWorkspace.Children.Add(_imageView);
                Grid.SetRow(_imageView, 0); Grid.SetRowSpan(_imageView, 1); Panel.SetZIndex(_imageView, 0);
                foreach (FrameworkElement bar in new[] { _edgeTabs, _edgeFooter })
                {
                    _edgeRoot.Children.Remove(bar); _edgeCenter.Children.Add(bar);
                    Grid.SetRowSpan(bar, 1); Panel.SetZIndex(bar, 0);
                    bar.VerticalAlignment = VerticalAlignment.Stretch; bar.Margin = new Thickness();
                }
                Grid.SetRow(_edgeTabs, 0); Grid.SetRow(_edgeFooter, 2);
                _edgeBody.Visibility = Visibility.Visible;
                Panel.SetZIndex(_topToolbar, 0); Panel.SetZIndex(_performanceBar, 0);
                LayoutImageOverlays();
            }
            QueueImageView();
        }

        private double EdgeTopInset
        { get { return _edgeActive && _edgeBarsShown ? (_topToolbar.IsVisible ? ((FrameworkElement)_topToolbar).ActualHeight : 0) + _edgeTabs.ActualHeight : 0; } }
        private double EdgeBottomInset
        { get { return _edgeActive && _edgeBarsShown ? (_performanceBar.IsVisible ? _performanceBar.ActualHeight : 0) + _edgeFooter.ActualHeight : 0; } }

        private void PositionEdgeBars()
        {
            if (!_edgeActive) return;
            _edgeTabs.Margin = new Thickness(0, _topToolbar.IsVisible ? ((FrameworkElement)_topToolbar).ActualHeight : 0, 0, 0);
            _edgeFooter.Margin = new Thickness(0, 0, 0, _performanceBar.IsVisible ? _performanceBar.ActualHeight : 0);
            if (_zoomIndicator != null) _zoomIndicator.Margin = new Thickness(16, 16 + EdgeTopInset, 16, 16);
            LayoutImageOverlays();
        }

        private void SetEdgeBars(bool shown)
        {
            _edgeBarsShown = shown;
            foreach (FrameworkElement bar in EdgeBars())
            {
                bar.IsHitTestVisible = shown;
                bar.IsEnabled = shown;
                bar.BeginAnimation(OpacityProperty, null);
                bar.Opacity = shown ? 1 : 0;
            }
            PositionEdgeBars();
            if (!_edgeActive && _zoomIndicator != null) _zoomIndicator.Margin = new Thickness(16);
        }
    }
}
