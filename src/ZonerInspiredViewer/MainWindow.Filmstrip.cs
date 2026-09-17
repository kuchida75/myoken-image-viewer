using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        internal static readonly string[] FilmstripPositions = { "Bottom center", "Left center", "Right center" };
        internal const int DefaultFilmstripSize = 160, MinimumFilmstripSize = 96, MaximumFilmstripSize = 256;
        private const double FilmstripTileGap = 2;
        private static readonly double[] FilmstripScales = { 0.58, 0.72, 0.86, 1, 0.86, 0.72, 0.58 };
        private static readonly double[] FilmstripOpacities = { 0.34, 0.54, 0.76, 1, 0.76, 0.54, 0.34 };
        private int _filmstripSizePixels = DefaultFilmstripSize;
        private bool _filmstripEnabled = true;
        private string _filmstripPosition = "Bottom center";
        private Border _filmstripOverlay;
        private Canvas _filmstripTiles;
        private StackPanel _filmstripSizeControls;
        private Slider _filmstripSizeSlider;
        private TextBlock _filmstripSizeText;
        private DispatcherTimer _filmstripSizeTimer;
        private TextBlock _filmstripCaption;
        private CheckBox _configureFilmstrip;
        private ComboBox _configureFilmstripPosition;
        private DispatcherTimer _filmstripTimer;
        private CancellationTokenSource _filmstripRequest;
        private readonly List<string> _filmstripPaths = new List<string>();

        private void BuildFilmstrip()
        {
            _filmstripTiles = new Canvas();
            _filmstripCaption = new TextBlock { Foreground = Brushes.White, FontSize = 11,
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 1 },
                Margin = new Thickness(3, 2, 3, 0) };
            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition());
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            content.Children.Add(_filmstripTiles); Grid.SetRow(_filmstripCaption, 1); content.Children.Add(_filmstripCaption);
            _filmstripOverlay = new Border { Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(6), Child = content, ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(_filmstripOverlay, "Nearby images");
            // Canvas children do not contribute a desired size to the image workspace.
            // A null background lets image input pass through everywhere outside the strip.
            var floatingLayer = new Canvas { ClipToBounds = true };
            floatingLayer.Children.Add(_filmstripOverlay);
            Panel.SetZIndex(floatingLayer, 25); _imageView.Children.Add(floatingLayer);
            _filmstripTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _filmstripTimer.Tick += delegate
            {
                if (!_filmstripOverlay.IsMouseOver && !_filmstripSizeControls.IsMouseOver && !_filmstripSizeSlider.IsMouseCaptureWithin) HideFilmstrip();
            };
            _filmstripOverlay.MouseEnter += delegate { _filmstripTimer.Stop(); };
            _filmstripOverlay.MouseLeave += delegate { if (_filmstripOverlay.IsVisible) _filmstripTimer.Start(); };
            _filmstripOverlay.PreviewMouseWheel += ImageCanvasMouseWheel;
        }

        private void BuildFilmstripSizeControl(WrapPanel controls)
        {
            _filmstripSizeControls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 4),
                Visibility = Visibility.Collapsed };
            var label = new TextBlock { Text = "Preview size", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            ThemeManager.Bind(label, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            _filmstripSizeSlider = new Slider { Minimum = MinimumFilmstripSize, Maximum = MaximumFilmstripSize,
                Value = DefaultFilmstripSize, TickFrequency = 16, SmallChange = 16, LargeChange = 32, IsSnapToTickEnabled = true,
                Width = 120, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Nearby thumbnail size. Previews scale down together to fit the image frame." };
            AutomationProperties.SetName(_filmstripSizeSlider, "Wheel preview thumbnail size");
            _filmstripSizeText = new TextBlock { Width = 46, Text = DefaultFilmstripSize + " px", Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center };
            ThemeManager.Bind(_filmstripSizeText, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            _filmstripSizeControls.Children.Add(label); _filmstripSizeControls.Children.Add(_filmstripSizeSlider);
            _filmstripSizeControls.Children.Add(_filmstripSizeText); controls.Children.Add(_filmstripSizeControls);
            _filmstripSizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _filmstripSizeTimer.Tick += delegate { _filmstripSizeTimer.Stop(); ShowFilmstrip(); };
            _filmstripSizeSlider.ValueChanged += delegate { SetFilmstripSize((int)_filmstripSizeSlider.Value, true); };
            _filmstripSizeSlider.GotKeyboardFocus += delegate { if (_slideshowTimer != null) _slideshowTimer.Stop(); };
            _filmstripSizeSlider.LostKeyboardFocus += delegate { ArmSlideshow(); };
            _filmstripSizeSlider.PreviewMouseLeftButtonUp += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate { if (!_isClosed && _imageView.IsVisible) _imageCanvas.Focus(); }));
            };
            _filmstripSizeControls.MouseEnter += delegate { if (_filmstripTimer != null) _filmstripTimer.Stop(); };
            _filmstripSizeControls.MouseLeave += delegate { if (_filmstripOverlay != null && _filmstripOverlay.IsVisible) _filmstripTimer.Start(); };
        }

        private void SetFilmstripSize(int pixels, bool preview)
        {
            int size = pixels <= 0 ? DefaultFilmstripSize : Math.Max(MinimumFilmstripSize, Math.Min(MaximumFilmstripSize, (int)Math.Round(pixels / 16.0) * 16));
            bool changed = _filmstripSizePixels != size;
            _filmstripSizePixels = size;
            _filmstripSizeSlider.Value = size; _filmstripSizeText.Text = size + " px";
            if (!changed) return;
            LayoutImageOverlays();
            if (preview && _filmstripEnabled && _imageView.IsVisible)
            {
                if (!_filmstripOverlay.IsVisible) ShowFilmstrip();
                else { _filmstripSizeTimer.Stop(); _filmstripSizeTimer.Start(); }
                _filmstripTimer.Stop(); _filmstripTimer.Start();
            }
            ScheduleSessionSave();
        }

        private void BuildFilmstripConfiguration()
        {
            _configureFilmstrip = new CheckBox { Content = "Nearby thumbnails on wheel navigation" };
            _configureFilmstrip.Click += delegate { SetFilmstripSettings(_configureFilmstrip.IsChecked == true, _filmstripPosition); };
            _configureFilmstripPosition = new ComboBox { ItemsSource = FilmstripPositions, MinWidth = 160 };
            _configureFilmstripPosition.SelectionChanged += delegate
            {
                if (_configureFilmstripPosition.SelectedItem != null)
                    SetFilmstripSettings(_filmstripEnabled, (string)_configureFilmstripPosition.SelectedItem);
            };
            AddConfigurationRow(1, "Wheel previews", _configureFilmstrip);
            AddConfigurationRow(1, "Preview position", _configureFilmstripPosition);
            SetFilmstripSettings(true, "Bottom center");
        }

        private void SetFilmstripSettings(bool enabled, string position)
        {
            _filmstripEnabled = enabled;
            _filmstripPosition = Array.IndexOf(FilmstripPositions, position) >= 0 ? position : "Bottom center";
            if (_configureFilmstrip != null) _configureFilmstrip.IsChecked = enabled;
            if (_configureFilmstripPosition != null)
            { _configureFilmstripPosition.SelectedItem = _filmstripPosition; _configureFilmstripPosition.IsEnabled = enabled; }
            if (_filmstripSizeControls != null) _filmstripSizeControls.IsEnabled = enabled;
            if (!enabled) HideFilmstrip(); else LayoutImageOverlays();
            ScheduleSessionSave();
        }

        private void NavigateWithFilmstrip(int delta)
        {
            if (_folderScanPending || _navigationImages.Count == 0 || _imageView.Visibility != Visibility.Visible) return;
            OpenRelativeImage(delta);
            ShowFilmstrip();
        }

        private void ShowFilmstrip()
        {
            HideFilmstrip();
            int current = FindFilteredIndex(_activeTabPath);
            if (!_filmstripEnabled || _isClosed || _folderScanPending || current < 0 || _imageView.Visibility != Visibility.Visible) return;
            var request = new CancellationTokenSource(); _filmstripRequest = request;
            var tasks = new List<Task>();
            var source = PresentationSource.FromVisual(this);
            double dpi = source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
            int cacheSize = Math.Max(64, Math.Min(2048, (int)Math.Ceiling(_filmstripSizePixels * dpi / 32) * 32));
            for (int offset = -3; offset <= 3; offset++)
            {
                ImageFileItem item = _navigationImages[WrapImageIndex(current + offset, _navigationImages.Count)];
                string path = item.Path;
                _filmstripPaths.Add(path);
                var image = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                var tile = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3),
                    Opacity = FilmstripOpacities[offset + 3],
                    BorderBrush = offset == 0 ? new SolidColorBrush(Color.FromRgb(89, 223, 208)) : Brushes.Transparent,
                    Background = Brushes.Transparent, Child = image,
                    Effect = offset == 0 ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 8, ShadowDepth = 3, Opacity = 0.65 } : null,
                    Cursor = Cursors.Hand, ToolTip = Path.GetFileName(path), Tag = path };
                AutomationProperties.SetName(tile, (offset == 0 ? "Current image: " : "Open image: ") + Path.GetFileName(path));
                tile.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (!File.Exists(path)) return;
                    OpenNavigationImage(path); ShowFilmstrip(); _imageCanvas.Focus();
                };
                _filmstripTiles.Children.Add(tile);
                tasks.Add(LoadFilmstripThumbnailAsync(item, image, cacheSize, request.Token));
            }
            DisposeFilmstripRequestAsync(tasks, request);
            _filmstripCaption.Text = (current + 1) + " / " + _navigationImages.Count;
            _filmstripCaption.ToolTip = Path.GetFileName(_activeTabPath);
            _filmstripOverlay.Visibility = Visibility.Visible; LayoutImageOverlays();
            _filmstripTimer.Start();
        }

        private async Task LoadFilmstripThumbnailAsync(ImageFileItem item, Image image, int size, CancellationToken token)
        {
            System.Windows.Media.Imaging.BitmapSource bitmap = null;
            try
            {
                bitmap = await _services.Thumbnails.GetThumbnailAsync(item, size, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { }
            if (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                if (_isClosed || token.IsCancellationRequested) return;
                if (bitmap != null) image.Source = bitmap;
                else
                {
                    var tile = image.Parent as Border;
                    if (tile != null) tile.Child = new TextBlock { Text = "?", Foreground = Brushes.White, FontSize = 18,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                }
            }));
        }

        private async void DisposeFilmstripRequestAsync(List<Task> tasks, CancellationTokenSource request)
        {
            try { await Task.WhenAll(tasks); }
            catch (TaskCanceledException) { }
            finally
            {
                if (_filmstripRequest == request) _filmstripRequest = null;
                request.Dispose();
            }
        }

        private void HideFilmstrip()
        {
            if (_filmstripSizeTimer != null) _filmstripSizeTimer.Stop();
            if (_filmstripTimer != null) _filmstripTimer.Stop();
            if (_filmstripRequest != null) { _filmstripRequest.Cancel(); _filmstripRequest = null; }
            if (_filmstripOverlay == null) return;
            _filmstripOverlay.Visibility = Visibility.Collapsed;
            _filmstripTiles.Children.Clear(); _filmstripPaths.Clear();
            LayoutImageOverlays();
        }

        private Thickness FilmstripSpace()
        {
            // Clearance for other overlay controls only; never inset or resize the image canvas.
            if (_filmstripOverlay == null || _filmstripOverlay.Visibility != Visibility.Visible) return new Thickness();
            Size size = FilmstripDimensions();
            if (_filmstripPosition == "Bottom center") return new Thickness(0, 0, 0, size.Height + 8);
            return _filmstripPosition == "Left center" ? new Thickness(size.Width + 8, 0, 0, 0) : new Thickness(0, 0, size.Width + 8, 0);
        }

        private Size FilmstripDimensions()
        {
            bool vertical = _filmstripPosition != "Bottom center";
            double width = Math.Max(0, _imageView.ActualWidth - 32), height = Math.Max(0, _imageView.ActualHeight - 32 - (vertical ? 56 : 0));
            // The seven frames taper together; reserve caption, padding and gaps before fitting the central frame.
            double gaps = 6 * FilmstripTileGap;
            double size = Math.Min(_filmstripSizePixels, Math.Min(Math.Max(0, width - 12 - (vertical ? 0 : gaps)) / (vertical ? 1 : 5.32),
                Math.Max(0, height - 30 - (vertical ? gaps : 0)) / (vertical ? 3.99 : 0.75)));
            return new Size(Math.Min(width, vertical ? size + 12 : size * 5.32 + 12 + gaps),
                Math.Min(height, vertical ? size * 3.99 + 30 + gaps : size * 0.75 + 30));
        }

        private void LayoutFilmstrip()
        {
            if (_filmstripOverlay == null || _filmstripOverlay.Visibility != Visibility.Visible) return;
            double width = Math.Max(0, _imageView.ActualWidth - 32), height = Math.Max(0, _imageView.ActualHeight - 32);
            bool vertical = _filmstripPosition != "Bottom center";
            Size dimensions = FilmstripDimensions();
            double stripWidth = dimensions.Width;
            double x = vertical ? (_filmstripPosition == "Left center" ? 16 : 16 + width - stripWidth) : 16 + (width - stripWidth) / 2;
            double stripHeight = dimensions.Height;
            double y = vertical ? 16 + 56 + Math.Max(0, height - 56 - stripHeight) / 2 : 16 + height - stripHeight;
            _filmstripOverlay.Width = stripWidth; _filmstripOverlay.Height = stripHeight;
            Canvas.SetLeft(_filmstripOverlay, x);
            Canvas.SetTop(_filmstripOverlay, Math.Max(16, y));
            double contentWidth = Math.Max(0, stripWidth - 12), contentHeight = Math.Max(0, stripHeight - 30);
            double size = Math.Max(0, vertical ? contentWidth : (contentWidth - 6 * FilmstripTileGap) / 5.32);
            double at = 0;
            for (int i = 0; i < _filmstripTiles.Children.Count; i++)
            {
                var tile = (Border)_filmstripTiles.Children[i];
                tile.Width = size * FilmstripScales[i]; tile.Height = size * 0.75 * FilmstripScales[i];
                Canvas.SetLeft(tile, vertical ? (contentWidth - tile.Width) / 2 : at);
                Canvas.SetTop(tile, vertical ? at : (contentHeight - tile.Height) / 2);
                at += (vertical ? tile.Height : tile.Width) + FilmstripTileGap;
            }
        }
    }
}
