using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ZonerInspiredViewer
{
    internal sealed class SuperResolutionWindow : Window
    {
        private readonly SuperResolutionRequest _request;
        private readonly string _sourcePath;
        private readonly FileRevision _sourceRevision;
        private readonly ComboBox _scale;
        private readonly Button _previewButton, _cancelButton, _saveButton;
        private readonly ToggleButton _compare, _actualSize;
        private readonly TextBlock _dimensions, _status;
        private readonly ProgressBar _progress;
        private readonly ScrollViewer _viewport;
        private readonly Image _preview;
        private CancellationTokenSource _cancellation;
        private SuperResolutionResult _result;
        private bool _busy, _closeRequested;
        internal Task PendingWork { get; private set; }
        internal EnhancedSaveResult SavedResult { get; private set; }
        internal double SelectedScale { get { return (double)_scale.SelectedItem; } }

        internal SuperResolutionWindow(SuperResolutionRequest request, string source, FileRevision revision)
        {
            _request = request; _sourcePath = source; _sourceRevision = revision;
            Title = "AI upscale - " + UpscaleModels.Get(request.ModelId).Name + " - " + Path.GetFileName(source); Width = 1000; Height = 760; MinWidth = 560; MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            ThemeManager.PrepareWindow(this);
            var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
            var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
            var commands = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) }; top.Children.Add(commands);
            commands.Children.Add(new TextBlock { Text = "Scale", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _scale = new ComboBox { ItemsSource = SuperResolution.Scales, SelectedItem = request.Scale, Width = 86, Height = 30,
                ItemStringFormat = "{0:0.##}x", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(_scale, "AI upscale factor"); commands.Children.Add(_scale);
            _previewButton = CommandPresentation.Button("\uE9D9", "Preview", null, "Rebuild from the original pixels with the current Enhance adjustments.",
                delegate { PendingWork = GenerateAsync(); }, true); commands.Children.Add(_previewButton);
            _compare = new ToggleButton { Content = "Original", Height = 30, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
            CommandPresentation.Describe(_compare, "Compare original", null, "Show the original at the same display size, with the same Enhance adjustments. Toggle off for the AI result.");
            _compare.Click += delegate { UpdatePreview(); }; commands.Children.Add(_compare);
            _actualSize = new ToggleButton { Content = "1:1", Height = 30, Width = 42, Margin = new Thickness(0, 0, 6, 0) };
            CommandPresentation.Describe(_actualSize, "Actual pixels", null, "Toggle actual output pixels or fit-to-window. Scroll to inspect the full image.");
            _actualSize.Click += delegate { UpdatePreview(); }; commands.Children.Add(_actualSize);
            _saveButton = CommandPresentation.Button("\uE792", "Save As", null, "Save the completed preview as a separate image. The source cannot be overwritten.", SaveAs, true);
            commands.Children.Add(_saveButton);
            _cancelButton = CommandPresentation.Button("\uE711", "Cancel", "Esc", "Cancel processing without changing any image file.",
                delegate { if (_cancellation != null) _cancellation.Cancel(); }, true); commands.Children.Add(_cancelButton);
            _dimensions = new TextBlock { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap }; top.Children.Add(_dimensions);
            var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
            _progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0, 0, 0, 6) }; bottom.Children.Add(_progress);
            _status = new TextBlock { TextWrapping = TextWrapping.Wrap }; bottom.Children.Add(_status);
            _preview = new Image { Source = request.Source, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.HighQuality);
            _viewport = new ScrollViewer { Content = _preview, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false };
            ThemeManager.Bind(_viewport, Control.BackgroundProperty, ThemeKeys.ImageBackground); root.Children.Add(_viewport);
            _viewport.SizeChanged += delegate { UpdatePreview(); };
            _scale.SelectionChanged += delegate
            {
                _result = null; _compare.IsChecked = false; UpdateDimensions(); UpdateButtons(); UpdatePreview();
                _progress.Value = 0;
                SetStatus("Scale changed. Preview is not yet generated.", UpscaleStatus.None);
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                if (_busy && _cancellation != null) _cancellation.Cancel(); else Close(); e.Handled = true;
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (_busy) { _closeRequested = true; _cancellation.Cancel(); e.Cancel = true; }
            };
            Closed += delegate { _preview.Source = null; _result = null; };
            Loaded += delegate { PendingWork = GenerateAsync(); };
            UpdateDimensions(); UpdateButtons();
        }

        private void UpdateDimensions()
        {
            try
            {
                Size size = SuperResolution.OutputSize(_request.Source.PixelWidth, _request.Source.PixelHeight, SelectedScale);
                bool rotate = ImageViewport.NormalizeRotation(_request.Rotation) % 2 != 0;
                _dimensions.Text = String.Format("{0} x {1}  >  {2} x {3} px  |  {4:0.##}x  |  Current Enhance adjustments",
                    rotate ? _request.Source.PixelHeight : _request.Source.PixelWidth, rotate ? _request.Source.PixelWidth : _request.Source.PixelHeight,
                    rotate ? size.Height : size.Width, rotate ? size.Width : size.Height, SelectedScale);
            }
            catch (Exception error) { _dimensions.Text = error.Message; }
        }

        private void UpdateButtons()
        {
            _scale.IsEnabled = !_busy; _previewButton.IsEnabled = !_busy; _cancelButton.IsEnabled = _busy;
            _saveButton.IsEnabled = !_busy && _result != null; _compare.IsEnabled = !_busy && _result != null;
        }

        private void SetStatus(string message, UpscaleStatus state)
        {
            _status.Text = message;
            ThemeManager.Bind(_status, TextBlock.ForegroundProperty, UpscalePresentation.TextKey(state));
            ThemeManager.Bind(_progress, Control.ForegroundProperty, UpscalePresentation.TextKey(state));
        }

        private void UpdatePreview()
        {
            BitmapSource bitmap = _result == null ? _request.Source : _compare.IsChecked == true ? _result.Comparison : _result.Pixels;
            _preview.Source = bitmap;
            BitmapSource reference = _result == null ? bitmap : _result.Pixels;
            double zoom = _actualSize.IsChecked == true ? 1 : Math.Min(1, Math.Min(Math.Max(1, _viewport.ActualWidth - 20) / reference.PixelWidth,
                Math.Max(1, _viewport.ActualHeight - 20) / reference.PixelHeight));
            _preview.Width = reference.PixelWidth * zoom; _preview.Height = reference.PixelHeight * zoom;
        }

        internal async Task GenerateAsync()
        {
            if (_busy) return;
            _request.Scale = SelectedScale; _result = null; _compare.IsChecked = false;
            _busy = true; _cancellation = new CancellationTokenSource(); _progress.Value = 0; UpdateButtons();
            SetStatus("Preparing " + SelectedScale.ToString("0.##") + "x preview", UpscaleStatus.Working);
            SuperResolutionResult completed = null; string message; UpscaleStatus state;
            try
            {
                completed = await SuperResolution.RunAsync(_request, _cancellation.Token, (value, stage) =>
                    Dispatcher.BeginInvoke(new Action(delegate { if (_busy) { _progress.Value = value; SetStatus(stage, UpscaleStatus.Working); } }))).ConfigureAwait(false);
                message = UpscaleModels.Get(completed.ModelId).Name + " | " + completed.Backend + " | " + completed.Analysis;
                state = _request.Scale == 1 ? UpscaleStatus.Bypassed : UpscaleStatus.Ready;
            }
            catch (OperationCanceledException) { message = "Canceled. Original unchanged."; state = UpscaleStatus.Canceled; }
            catch (Exception error) { message = "Upscale unavailable: " + error.Message; state = UpscaleStatus.Error; }
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                _result = completed; SetStatus(message, state);
                if (completed != null) _progress.Value = 1;
                FinishWork(); if (!_closeRequested) UpdatePreview();
            }));
        }

        private void FinishWork()
        {
            _busy = false; _cancellation.Dispose(); _cancellation = null; UpdateButtons();
            if (_closeRequested) Close();
        }

        private async void SaveAs()
        {
            if (_busy || _result == null) return;
            var dialog = new SaveFileDialog { Title = "Save AI upscaled copy", Filter = EnhancedImageStore.Filter, DefaultExt = ".png",
                AddExtension = true, CheckPathExists = true, OverwritePrompt = true, InitialDirectory = Path.GetDirectoryName(_sourcePath),
                FileName = Path.GetFileNameWithoutExtension(_sourcePath) + "-upscaled-" + SelectedScale.ToString("0.##", CultureInfo.InvariantCulture) + "x.png" };
            if (dialog.ShowDialog(this) != true) return;
            try { PendingWork = SaveResultAsync(dialog.FileName, File.Exists(dialog.FileName)); await PendingWork; if (SavedResult != null && IsVisible) Close(); }
            catch (OperationCanceledException) { SetStatus("Save canceled. Original unchanged.", UpscaleStatus.Canceled); }
            catch (Exception error) { SetStatus("Not saved: " + error.Message, UpscaleStatus.Error); }
        }

        internal async Task SaveResultAsync(string destination, bool overwrite)
        {
            if (_busy || _result == null) throw new InvalidOperationException("Generate a preview before saving.");
            if (String.Equals(Path.GetFullPath(destination), Path.GetFullPath(_sourcePath), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose a different filename. AI upscale always keeps the original.");
            _busy = true; _cancellation = new CancellationTokenSource(); UpdateButtons();
            _progress.Value = 0; SetStatus("Saving upscaled copy", UpscaleStatus.Working);
            Exception failure = null;
            try
            {
                SavedResult = await EnhancedImageStore.SaveAsync(new EnhancedSaveRequest { SourcePath = _sourcePath, SourceRevision = _sourceRevision,
                    Destination = destination, DestinationRevision = FileRevision.Read(destination), AllowOverwrite = overwrite, Pixels = _result.Pixels },
                    _cancellation.Token, p => Dispatcher.BeginInvoke(new Action(delegate { if (_busy) { _progress.Value = p; SetStatus("Saving upscaled copy", UpscaleStatus.Working); } }))).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; }
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                SetStatus(failure == null ? "Saved upscaled copy" : failure is OperationCanceledException ? "Save canceled. Original unchanged." : "Not saved: " + failure.Message,
                    failure == null ? UpscaleStatus.Ready : failure is OperationCanceledException ? UpscaleStatus.Canceled : UpscaleStatus.Error);
                FinishWork();
            }));
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
