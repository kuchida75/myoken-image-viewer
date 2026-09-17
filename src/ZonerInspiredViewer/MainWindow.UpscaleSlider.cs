using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private StackPanel _upscaleSizeControls;
        private Slider _upscaleSizeSlider;
        private TextBlock _upscaleSizeText;
        private DispatcherTimer _upscaleSizeTimer;
        private bool _syncUpscaleSize;
        private double _autoUpscaleAppliedScale = 1;

        private bool AutomaticUpscalePending
        {
            get { return _autoUpscaleRequest != null || _upscaleSizeTimer != null && _upscaleSizeTimer.IsEnabled; }
        }

        private bool HasUpscaleSource()
        {
            // A valid image stays valid while the footer or viewing frame is being laid out.
            return !_isClosed && _imageView != null && _imageView.IsVisible && _mainImage.Source != null
                && !String.IsNullOrEmpty(_activeTabPath) && String.Equals(_displayedImagePath, _activeTabPath, StringComparison.OrdinalIgnoreCase);
        }

        private void BuildUpscaleSizeControl(WrapPanel controls)
        {
            _upscaleSizeControls = new StackPanel { Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 10, 4), Visibility = Visibility.Collapsed };
            var label = new TextBlock { Text = "AI upscale", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            ThemeManager.Bind(label, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            // Indices keep the uneven supported factors reachable with mouse and keyboard.
            _upscaleSizeSlider = new Slider { Minimum = 0, Maximum = SuperResolution.Scales.Length - 1,
                TickFrequency = 1, SmallChange = 1, LargeChange = 1, IsSnapToTickEnabled = true,
                Width = 130, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "AI upscale factor. Rebuilds from original pixels after a short pause. 1x shows original resolution; no file is changed." };
            AutomationProperties.SetName(_upscaleSizeSlider, "AI upscale factor");
            _upscaleSizeText = new TextBlock { Width = 42, Text = "1x", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            ThemeManager.Bind(_upscaleSizeText, TextBlock.ForegroundProperty, ThemeKeys.Text);
            _upscaleSizeControls.Children.Add(label); _upscaleSizeControls.Children.Add(_upscaleSizeSlider);
            _upscaleSizeControls.Children.Add(_upscaleSizeText); controls.Children.Add(_upscaleSizeControls);
            _upscaleSizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _upscaleSizeTimer.Tick += delegate
            {
                _upscaleSizeTimer.Stop();
                if (!HasUpscaleSource()) { UpdateUpscaleSizeControl(); return; }
                if (_enhancementSaving || _rotationSaving || _backupBusy || _fileTransferBusy)
                { RestoreUpscaleSizeSelection(); UpdateEnhanceSaveButtons(); return; }
                StartAutomaticUpscale(_autoUpscaleOriginal ?? (BitmapSource)_mainImage.Source, SelectedUpscaleSize());
            };
            _upscaleSizeSlider.ValueChanged += delegate
            {
                if (_syncUpscaleSize || !_upscaleSizeControls.IsVisible || !HasUpscaleSource()) return;
                double scale = SelectedUpscaleSize();
                _upscaleSizeText.Text = scale.ToString("0.##") + "x";
                _upscaleSizeTimer.Stop();
                // Invalidate in-flight work immediately, before the debounce can start its replacement.
                if (_autoUpscaleRequest != null) { _autoUpscaleRequest.Cancel(); _autoUpscaleRequest = null; }
                _upscaleSizeTimer.Start();
                SetAutomaticUpscaleStatus("AI upscale: preparing " + scale.ToString("0.##") + "x preview", UpscaleStatus.Working);
                UpdateEnhanceSaveButtons();
            };
        }

        private double SelectedUpscaleSize()
        {
            return SuperResolution.Scales[(int)Math.Round(_upscaleSizeSlider.Value)];
        }

        private void SyncUpscaleSize(double scale, BitmapSource source)
        {
            if (_upscaleSizeSlider == null) return;
            _syncUpscaleSize = true;
            try
            {
                double maximum = SuperResolution.AutomaticScale(new Size(source.PixelWidth, source.PixelHeight),
                    new Size(source.PixelWidth * 3.0, source.PixelHeight * 3.0));
                _upscaleSizeSlider.Maximum = Array.IndexOf(SuperResolution.Scales, maximum);
                _upscaleSizeSlider.Value = Array.IndexOf(SuperResolution.Scales, scale);
                _upscaleSizeText.Text = scale.ToString("0.##") + "x";
            }
            finally { _syncUpscaleSize = false; }
        }

        private void RestoreUpscaleSizeSelection()
        {
            var source = _autoUpscaleOriginal ?? _mainImage.Source as BitmapSource;
            if (source != null) SyncUpscaleSize(_autoUpscaleAppliedScale, source);
        }

        private void UpdateUpscaleSizeControl()
        {
            UpdateAutomaticUpscaleAppearance();
            if (_upscaleSizeControls == null) return;
            _upscaleSizeControls.Visibility = HasUpscaleSource()
                && (_autoUpscaleOriginal != null || AutomaticUpscalePending) ? Visibility.Visible : Visibility.Collapsed;
            _upscaleSizeSlider.IsEnabled = !_enhancementSaving && !_rotationSaving && !_backupBusy && !_fileTransferBusy;
        }
    }
}
