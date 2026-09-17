using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Border _slideshowOverlay;
        private Button _slideshowPauseButton, _slideshowShuffleButton;
        private TextBlock _slideshowPosition;
        private TextBox _slideshowOverlayInterval;
        private bool _slideshowPaused;

        private void BuildSlideshowOverlay()
        {
            var content = new StackPanel();
            var buttons = new WrapPanel();
            buttons.Children.Add(SlideshowOverlayButton("\uE892", "Previous slide", delegate { MoveSlideshow(-1); }));
            _slideshowPauseButton = SlideshowOverlayButton("\uE769", "Pause slideshow", ToggleSlideshowPause);
            buttons.Children.Add(_slideshowPauseButton);
            buttons.Children.Add(SlideshowOverlayButton("\uE893", "Next slide", delegate { MoveSlideshow(1); }));
            _slideshowShuffleButton = SlideshowOverlayButton("\uE8B1", "Shuffle slideshow", ToggleSlideshowShuffle);
            buttons.Children.Add(_slideshowShuffleButton);
            buttons.Children.Add(SlideshowOverlayButton("\uE71A", "Stop slideshow", StopSlideshow));
            _slideshowPosition = new TextBlock { Foreground = Brushes.White, FontSize = 12, Width = 78,
                VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            buttons.Children.Add(_slideshowPosition); content.Children.Add(buttons);
            var timing = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            timing.Children.Add(new TextBlock { Text = "Interval", Foreground = Brushes.White, Opacity = 0.8,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _slideshowOverlayInterval = new TextBox { Width = 62, Height = 26, VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), Foreground = Brushes.White, CaretBrush = Brushes.White,
                ToolTip = "Seconds per image (0.5 to 3600)" };
            AutomationProperties.SetName(_slideshowOverlayInterval, "Slideshow seconds per image");
            _slideshowOverlayInterval.GotKeyboardFocus += delegate { _slideshowTimer.Stop(); };
            _slideshowOverlayInterval.LostKeyboardFocus += delegate { ApplyOverlaySlideshowInterval(); };
            _slideshowOverlayInterval.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true; ApplyOverlaySlideshowInterval(); _imageCanvas.Focus(); ArmSlideshow();
            };
            timing.Children.Add(_slideshowOverlayInterval);
            timing.Children.Add(new TextBlock { Text = "s", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0) });
            content.Children.Add(timing);
            _slideshowOverlay = new Border { Width = 294, Padding = new Thickness(10), Margin = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromArgb(168, 15, 18, 21)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Child = content, Visibility = Visibility.Collapsed, ClipToBounds = true };
            _slideshowOverlay.SizeChanged += delegate { LayoutImageOverlays(); };
            _slideshowOverlay.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) { e.Handled = true; };
            AutomationProperties.SetName(_slideshowOverlay, "Slideshow controls");
            Panel.SetZIndex(_slideshowOverlay, 20); _imageView.Children.Add(_slideshowOverlay);
        }

        private Button SlideshowOverlayButton(string glyph, string name, Action action)
        {
            var button = CommandPresentation.Button(glyph, name, null, null, action, false);
            button.Background = Brushes.Transparent; button.Foreground = Brushes.White;
            button.Resources[ThemeKeys.ControlHover] = new SolidColorBrush(Color.FromRgb(65, 73, 79));
            button.Resources[ThemeKeys.ControlPressed] = new SolidColorBrush(Color.FromRgb(39, 88, 84));
            button.BorderBrush = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));
            button.Margin = new Thickness(0, 0, 4, 0); return button;
        }

        private void ApplyOverlaySlideshowInterval()
        {
            if (!_slideshowPlaying) return;
            _slideshowInterval.Text = _slideshowOverlayInterval.Text; ApplySlideshowInterval();
            _slideshowOverlayInterval.Text = _slideshowSeconds.ToString(CultureInfo.CurrentCulture);
            ArmSlideshow();
        }

        private void ToggleSlideshowPause()
        {
            if (!_slideshowPlaying) return;
            _slideshowPaused = !_slideshowPaused;
            if (_slideshowPaused) _slideshowTimer.Stop(); else ArmSlideshow();
            UpdateSlideshowOverlay();
        }

        private void ToggleSlideshowShuffle()
        {
            _slideshowShuffle.IsChecked = _slideshowShuffle.IsChecked != true;
            if (_slideshowPlaying) ResetSlideshowSequence();
            UpdateSlideshowOverlay(); ScheduleSessionSave();
        }

        private void UpdateSlideshowOverlay()
        {
            if (_slideshowOverlay == null) return;
            _slideshowOverlay.Visibility = !_isClosed && _slideshowPlaying && _imageView.Visibility == Visibility.Visible
                && _activeTabId == _slideshowTabId ? Visibility.Visible : Visibility.Collapsed;
            if (_slideshowOverlay.Visibility == Visibility.Visible)
            {
                if (!Object.Equals(_slideshowPauseButton.Tag, _slideshowPaused))
                {
                    _slideshowPauseButton.Tag = _slideshowPaused;
                    _slideshowPauseButton.Content = CommandPresentation.Label(_slideshowPaused ? "\uE768" : "\uE769", null);
                    CommandPresentation.Describe(_slideshowPauseButton, _slideshowPaused ? "Resume slideshow" : "Pause slideshow", null, null);
                }
                _slideshowShuffleButton.Background = _slideshowShuffle.IsChecked == true
                    ? new SolidColorBrush(Color.FromArgb(160, 32, 108, 99)) : Brushes.Transparent;
                AutomationProperties.SetItemStatus(_slideshowShuffleButton, _slideshowShuffle.IsChecked == true ? "On" : "Off");
                _slideshowPosition.Text = _slideshowSequence.Position + " / " + _slideshowSequence.Count;
                if (!_slideshowOverlayInterval.IsKeyboardFocusWithin) _slideshowOverlayInterval.Text = _slideshowSeconds.ToString(CultureInfo.CurrentCulture);
            }
            LayoutImageOverlays();
        }

        private void LayoutImageOverlays()
        {
            if (_imageMetadataOverlay == null) return;
            Thickness space = FilmstripSpace();
            double width = Math.Max(0, _imageView.ActualWidth - 32 - space.Left - space.Right);
            double height = Math.Max(0, _imageView.ActualHeight - 32 - space.Bottom);
            double rightHeight = 0, reserved = 0;
            bool metadataVisible = _imageMetadataOverlay.Visibility == Visibility.Visible;
            double rightWidth = _slideshowOverlay != null && _slideshowOverlay.IsVisible ? 294
                : _imageNavigatorOverlay != null && _imageNavigatorOverlay.IsVisible ? 208 : 0;
            double leftWidth = Math.Min(420, width);
            // Preserve useful vertical space by narrowing left controls when two readable columns fit.
            if (rightWidth > 0 && width >= rightWidth + 316) leftWidth = Math.Min(leftWidth, width - rightWidth - 16);
            _imageMetadataOverlay.MaxWidth = leftWidth;
            if (_slideshowOverlay != null)
            {
                _slideshowOverlay.MaxWidth = width;
                _slideshowOverlay.MaxHeight = height;
                _slideshowOverlay.Margin = new Thickness(16 + space.Left, 16, 16 + space.Right, 16 + space.Bottom);
                if (_slideshowOverlay.Visibility == Visibility.Visible)
                {
                    rightHeight = _slideshowOverlay.ActualHeight;
                    if (leftWidth + Math.Min(294, width) + 16 > width) reserved = rightHeight + 8;
                }
            }
            if (_imageNavigatorOverlay != null)
            {
                double below = rightHeight > 0 ? rightHeight + 8 : 0;
                bool overlaps = leftWidth + Math.Min(208, width) + 16 > width;
                double metadataHeight = 0;
                if (metadataVisible && overlaps)
                {
                    // Keep metadata readable before assigning remaining height to the overview.
                    _imageMetadataOverlay.Child.Measure(new Size(Math.Max(0, leftWidth - 26), Double.PositiveInfinity));
                    metadataHeight = _imageMetadataOverlay.Child.DesiredSize.Height + 18 + 8;
                }
                if (overlaps && _manualEnhanceOverlay != null && _manualEnhanceOverlay.IsVisible) metadataHeight += 140;
                _imageNavigatorOverlay.MaxWidth = width;
                _imageNavigatorOverlay.MaxHeight = Math.Max(0, height - below - metadataHeight);
                _imageNavigatorOverlay.Margin = new Thickness(16 + space.Left, 16, 16 + space.Right, 16 + space.Bottom + below);
                if (_imageNavigatorOverlay.Visibility == Visibility.Visible && overlaps)
                    reserved = below + _imageNavigatorOverlay.ActualHeight + 8;
            }
            _imageMetadataOverlay.Margin = new Thickness(16 + space.Left, 16, 16 + space.Right, 16 + space.Bottom + reserved);
            _imageMetadataOverlay.MaxHeight = Math.Max(0, height - reserved);
            LayoutManualEnhance(leftWidth, height + space.Bottom, reserved + space.Bottom);
            LayoutFilmstrip();
        }
    }
}
