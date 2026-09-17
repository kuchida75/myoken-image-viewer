using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private TextBlock _itemCountText;
        private TextBlock _transferText;
        private ToggleButton _slideshowButton;
        private TextBox _slideshowInterval;
        private CheckBox _slideshowShuffle;
        private DispatcherTimer _slideshowTimer;
        private SlideshowSequence _slideshowSequence;
        private double _slideshowSeconds = 3;
        private bool _slideshowPlaying;
        private bool _slideshowAdvancing;
        private string _slideshowTabId;
        private FolderScanSummary _folderSummary;
        private readonly Dictionary<string, int> _imageOrdinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private UIElement BuildBrowserFooter()
        {
            var footer = new StackPanel { Margin = new Thickness(10, 5, 10, 5) };
            var controls = new WrapPanel();
            _slideshowButton = new ToggleButton { Content = "\u25b6 Slideshow", MinWidth = 104, Height = 28,
                Margin = new Thickness(0, 0, 10, 4), ToolTip = "Start or stop slideshow" };
            _slideshowButton.Click += delegate
            {
                if (_slideshowPlaying) StopSlideshow();
                else
                {
                    StartSlideshow();
                    if (_slideshowPlaying && _configureWindow != null) _configureWindow.Close();
                }
            };
            AutomationProperties.SetName(_slideshowButton, "Slideshow");
            var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 4) };
            _slideshowInterval = new TextBox { Text = "3", Width = 56, Height = 28, VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Seconds per image (0.5 to 3600)" };
            AutomationProperties.SetName(_slideshowInterval, "Slideshow interval in seconds");
            _slideshowInterval.LostKeyboardFocus += delegate { ApplySlideshowInterval(); };
            _slideshowInterval.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { ApplySlideshowInterval(); e.Handled = true; }
            };
            options.Children.Add(_slideshowInterval);
            options.Children.Add(new TextBlock { Text = "s", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 12, 0) });
            _slideshowShuffle = new CheckBox { Content = "Shuffle", VerticalAlignment = VerticalAlignment.Center };
            _slideshowShuffle.Click += delegate
            {
                if (_slideshowPlaying) ResetSlideshowSequence();
                UpdateSlideshowOverlay();
                ScheduleSessionSave();
            };
            options.Children.Add(_slideshowShuffle);
            _itemCountText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 4),
                TextWrapping = TextWrapping.Wrap };
            ThemeManager.Bind(_itemCountText, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            controls.Children.Add(_itemCountText);
            controls.SizeChanged += delegate { _itemCountText.MaxWidth = Math.Max(1, controls.ActualWidth - 14); };
            footer.Children.Add(controls);
            BuildImageStatusBar(footer, controls);
            _transferText = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
            ThemeManager.Bind(_transferText, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            footer.Children.Add(_transferText);
            _slideshowTimer = new DispatcherTimer(DispatcherPriority.Background);
            _slideshowTimer.Tick += delegate { AdvanceSlideshow(); };
            var band = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0) };
            ThemeManager.Bind(band, Border.BackgroundProperty, ThemeKeys.ToolbarBackground);
            ThemeManager.Bind(band, Border.BorderBrushProperty, ThemeKeys.Border);
            return band;
        }

        private void RebuildImageOrdinals()
        {
            _imageOrdinals.Clear();
            int ordinal = 0;
            foreach (ImageFileItem item in _allItems)
                if (!item.IsDirectory) _imageOrdinals[item.Path] = ++ordinal;
        }

        private void UpdateBrowserFooter()
        {
            if (_itemCountText == null || _thumbnailGrid == null) return;
            UpdateImageStatusBar(DisplayedImageDetails());
            _slideshowButton.IsEnabled = _slideshowPlaying || (!_folderScanPending && _imageOrdinals.Count > 0 && !_fileTransferBusy);
            string counts = _folderSummary == null ? (_isScanningFolder ? "Scanning..." : "Folder unavailable")
                : _folderSummary.FileCount + " files (" + _folderSummary.ImageCount + " images), " + _folderSummary.FolderCount + " folders";
            string path = _imageView != null && _imageView.Visibility == Visibility.Visible ? _activeTabPath
                : _thumbnailGrid.SelectedItem == null ? null : _thumbnailGrid.SelectedItem.Path;
            int ordinal;
            if (path != null && _imageOrdinals.TryGetValue(path, out ordinal)) counts += " | Image " + ordinal + " / " + _imageOrdinals.Count;
            if (_browserView.Visibility == Visibility.Visible)
            {
                if (_thumbnailGrid.SelectedCount > 0) counts += " | " + _thumbnailGrid.SelectedCount + " selected";
                if (!String.IsNullOrWhiteSpace(_searchBox.Text)) counts += " | " + _filteredItems.Count + " shown";
            }
            _itemCountText.Text = counts;
            _transferText.Text = _transferStatus ?? "";
            ThemeManager.Bind(_transferText, TextBlock.ForegroundProperty, !String.IsNullOrEmpty(_autoUpscaleStatus) && _transferStatus == _autoUpscaleStatus
                ? UpscalePresentation.TextKey(_autoUpscaleStatusKind) : ThemeKeys.MutedText);
            _transferText.Visibility = String.IsNullOrEmpty(_transferStatus) ? Visibility.Collapsed : Visibility.Visible;
            UpdateSlideshowOverlay();
        }

        private void ApplySlideshowInterval()
        {
            double seconds;
            if (Double.TryParse(_slideshowInterval.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out seconds)
                && seconds >= 0.5 && seconds <= 3600) _slideshowSeconds = seconds;
            _slideshowInterval.Text = _slideshowSeconds.ToString(CultureInfo.CurrentCulture);
            if (_slideshowTimer.IsEnabled) { _slideshowTimer.Stop(); ArmSlideshow(); }
            UpdateSlideshowOverlay();
            ScheduleSessionSave();
        }

        private void ResetSlideshowSequence()
        {
            _slideshowSequence = new SlideshowSequence(_allItems.Where(item => !item.IsDirectory).Select(item => item.Path),
                _slideshowShuffle.IsChecked == true, _activeTabPath);
        }

        private void StartSlideshow()
        {
            if (_folderScanPending || _fileTransferBusy) { StopSlideshow(); return; }
            ApplySlideshowInterval();
            ResetSlideshowSequence();
            if (_slideshowSequence.Count == 0) { StopSlideshow(); return; }
            if (_isFullscreen) ToggleFullscreen();
            _slideshowPlaying = true;
            _slideshowPaused = false;
            _slideshowButton.IsChecked = true;
            _slideshowButton.Content = "\u25a0 Slideshow";
            _slideshowTabId = _activeTabId;
            if (File.Exists(_slideshowSequence.Current)) ShowSlideshowImage(_slideshowSequence.Current);
            else AdvanceSlideshow();
        }

        private void ShowSlideshowImage(string path)
        {
            _slideshowTimer.Stop();
            _slideshowAdvancing = true;
            try { OpenBrowserImage(path); }
            finally { _slideshowAdvancing = false; }
            _slideshowTabId = _activeTabId;
            // Cached images can finish before OpenBrowserImage returns.
            if (!_imageViewPending || _imageError.Visibility == Visibility.Visible) ArmSlideshow();
            UpdateSlideshowOverlay();
        }

        private void ArmSlideshow()
        {
            if (_isClosed || !_slideshowPlaying || _slideshowPaused || _slideshowTimer.IsEnabled || _activeTabId != _slideshowTabId
                || (_imageNavigator != null && _imageNavigator.IsDragging)
                || (_manualEnhanceOverlay != null && (_manualEnhanceOverlay.IsKeyboardFocusWithin || _manualEnhanceOverlay.IsMouseCaptureWithin))
                || (_slideshowOverlayInterval != null && _slideshowOverlayInterval.IsKeyboardFocusWithin)
                || (_filmstripSizeSlider != null && _filmstripSizeSlider.IsKeyboardFocusWithin)
                || (_imageViewPending && _imageError.Visibility != Visibility.Visible)) return;
            _slideshowTimer.Interval = TimeSpan.FromSeconds(_slideshowSeconds);
            _slideshowTimer.Start();
        }

        private void AdvanceSlideshow()
        {
            MoveSlideshow(1);
        }

        private void MoveSlideshow(int direction)
        {
            _slideshowTimer.Stop();
            if (!_slideshowPlaying || _activeTabId != _slideshowTabId) { StopSlideshow(); return; }
            for (int i = 0; i < _slideshowSequence.Count; i++)
            {
                string path = direction < 0 ? _slideshowSequence.Previous() : _slideshowSequence.Next();
                if (!File.Exists(path)) continue;
                ShowSlideshowImage(path);
                return;
            }
            StopSlideshow();
        }

        private void StopSlideshow()
        {
            _slideshowPlaying = false;
            _slideshowPaused = false;
            if (_slideshowTimer != null) _slideshowTimer.Stop();
            if (_slideshowButton != null)
            {
                _slideshowButton.IsChecked = false;
                _slideshowButton.Content = "\u25b6 Slideshow";
            }
            UpdateSlideshowOverlay();
        }
    }
}
