using System;
using System.Globalization;
using System.IO;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps;

namespace ZonerInspiredViewer
{
    internal sealed class PrintPreviewWindow : Window
    {
        private readonly ImagePrintRequest _request;
        private readonly ComboBox _printers, _paper, _orientation, _fit, _zoom;
        private readonly TextBox _margin, _copies;
        private readonly Button _print, _close, _refresh;
        private readonly StackPanel _settingsPanel;
        private readonly TextBlock _status, _details;
        private readonly ScrollViewer _viewport;
        private readonly Viewbox _preview;
        private readonly CancellationTokenSource _renderCancellation = new CancellationTokenSource();
        private BitmapSource _pixels;
        private ImagePrinterProfile _profile;
        private ImagePrintSettings _settings;
        private ImagePrintLayout _layout;
        private XpsDocumentWriter _writer;
        private ImagePrinterConnection _connection;
        private FixedDocumentSequence _printDocument;
        private bool _sync, _closed, _printing, _closeAfterPrint, _loadingPrinters, _loadingSettings, _renderFinished;
        private int _settingsVersion;
        private string _renderError, _printerNotice;
        internal Task PendingWork { get; private set; }
        internal Task SettingsWork { get; private set; }
        internal Task PrintersWork { get; private set; }

        internal PrintPreviewWindow(ImagePrintRequest request, bool discoverPrinters = true)
        {
            _request = request;
            Title = "Print preview - " + request.Name; Width = 1080; Height = 820; MinWidth = 640; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; ThemeManager.PrepareWindow(this);
            var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
            _settingsPanel = new StackPanel(); DockPanel.SetDock(_settingsPanel, Dock.Top); root.Children.Add(_settingsPanel);
            var first = new WrapPanel(); _settingsPanel.Children.Add(first);
            _printers = Choice(280); AddField(first, "Printer", _printers);
            _refresh = CommandPresentation.Button("\uE72C", "Refresh printers", null, "Reload installed Windows printers.",
                delegate { PrintersWork = RefreshPrintersAsync(); }, false); first.Children.Add(_refresh);
            _paper = Choice(230); _paper.ItemsSource = ImagePrintPaper.Defaults(); _paper.SelectedIndex = 0; AddField(first, "Paper", _paper);
            var second = new WrapPanel(); _settingsPanel.Children.Add(second);
            _orientation = Choice(120); _orientation.Items.Add("Portrait"); _orientation.Items.Add("Landscape");
            bool rotated = ImageViewport.NormalizeRotation(request.Rotation) % 2 != 0;
            _orientation.SelectedIndex = (rotated ? request.Pixels.PixelHeight > request.Pixels.PixelWidth : request.Pixels.PixelWidth > request.Pixels.PixelHeight) ? 1 : 0;
            AddField(second, "Orientation", _orientation);
            _fit = Choice(150); _fit.Items.Add("Fit entire image"); _fit.Items.Add("Fill (crop edges)"); _fit.SelectedIndex = 0; AddField(second, "Sizing", _fit);
            _margin = Number("10", 55); AddField(second, "Margin (mm)", _margin);
            _copies = Number("1", 44); AddField(second, "Copies", _copies);
            CommandPresentation.Describe(_margin, "Page margin", null, "Minimum margin from each paper edge, 0-100 mm. Printer hardware margins also apply.");
            CommandPresentation.Describe(_copies, "Copies", null, "Number of copies, 1-99, subject to printer support.");
            CommandPresentation.Describe(_fit, "Print sizing", null, "Fit keeps the whole image. Fill centers the image and crops it to the available area.");
            var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            _details = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) };
            ThemeManager.Bind(_details, TextBlock.ForegroundProperty, ThemeKeys.MutedText); footer.Children.Add(_details);
            _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; footer.Children.Add(_status);
            var commands = new DockPanel(); footer.Children.Add(commands);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _print = CommandPresentation.Button("\uE749", "Print", "Ctrl+P", "Send this preview to the selected printer.", BeginPrint, true);
            _print.IsEnabled = false; actions.Children.Add(_print);
            _close = CommandPresentation.Button("\uE711", "Close", "Esc", "Close the preview, or cancel a pending print submission.", CloseOrCancel, true);
            _close.Margin = new Thickness(0); actions.Children.Add(_close); DockPanel.SetDock(actions, Dock.Right); commands.Children.Add(actions);
            var zoomRow = new WrapPanel(); commands.Children.Add(zoomRow); _zoom = Choice(100);
            foreach (string label in new[] { "Fit page", "50%", "100%", "200%" }) _zoom.Items.Add(label);
            _zoom.SelectedIndex = 0; AddField(zoomRow, "Preview", _zoom);
            _preview = new Viewbox { Stretch = Stretch.Uniform };
            _viewport = new ScrollViewer { Content = new Border { Child = _preview, Margin = new Thickness(24) },
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            ThemeManager.Bind(_viewport, Control.BackgroundProperty, ThemeKeys.WorkspaceBackground); root.Children.Add(_viewport);
            _viewport.SizeChanged += delegate { UpdateZoom(); }; _zoom.SelectionChanged += delegate { UpdateZoom(); };
            _printers.SelectionChanged += delegate { if (!_sync) SettingsWork = LoadPrinterAsync(); };
            _paper.SelectionChanged += delegate { if (!_sync) SettingsWork = ReadSettingsAsync(); };
            _orientation.SelectionChanged += delegate { if (!_sync) SettingsWork = ReadSettingsAsync(); };
            _copies.TextChanged += delegate { if (!_sync) SettingsWork = ReadSettingsAsync(); };
            _fit.SelectionChanged += delegate { UpdatePreview(); }; _margin.TextChanged += delegate { UpdatePreview(); };
            Loaded += delegate
            {
                PendingWork = PrepareImageAsync();
                if (discoverPrinters) PrintersWork = RefreshPrintersAsync(); else { _printerNotice = "No printer selected. Preview only."; UpdatePreview(); }
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { CloseOrCancel(); e.Handled = true; }
                else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control) { if (!e.IsRepeat) BeginPrint(); e.Handled = true; }
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            { if (_printing) { e.Cancel = true; _closeAfterPrint = true; CancelPrint(); } };
            Closed += delegate { _closed = true; _settingsVersion++; if (!_renderFinished) _renderCancellation.Cancel(); _preview.Child = null; _pixels = null; };
        }

        private static ComboBox Choice(double width) { return new ComboBox { Width = width, Height = 30, VerticalContentAlignment = VerticalAlignment.Center }; }
        private static TextBox Number(string value, double width) { return new TextBox { Text = value, Width = width, Height = 30, VerticalContentAlignment = VerticalAlignment.Center }; }
        private static void AddField(Panel row, string label, FrameworkElement control)
        {
            var field = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 8) };
            field.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
            System.Windows.Automation.AutomationProperties.SetName(control, label); field.Children.Add(control); row.Children.Add(field);
        }

        private async Task PrepareImageAsync()
        {
            UpdatePreview();
            BitmapSource pixels = null; string error = null;
            try { pixels = await ImagePrinting.RenderAsync(_request, _renderCancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { error = "Preview canceled."; }
            catch (Exception failure) { error = "Cannot prepare image: " + failure.Message; }
            if (Dispatcher.HasShutdownStarted) { _renderCancellation.Dispose(); return; }
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                _renderFinished = true; _renderCancellation.Dispose();
                if (_closed) return;
                _pixels = pixels; _renderError = error; UpdatePreview();
            }));
        }

        private async Task RefreshPrintersAsync()
        {
            if (_printing || _loadingPrinters) return;
            var previous = _printers.SelectedItem as ImagePrinterInfo;
            _settingsVersion++; _profile = null; _loadingSettings = false;
            _loadingPrinters = true; _refresh.IsEnabled = false; _settings = null; UpdatePreview();
            System.Collections.Generic.List<ImagePrinterInfo> printers = null; Exception error = null;
            try { printers = await ImagePrinting.RunSta(ImagePrinting.Printers).ConfigureAwait(false); }
            catch (Exception failure) { error = failure; }
            if (Dispatcher.HasShutdownStarted) return;
            Task next = null;
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                _loadingPrinters = false;
                if (_closed) return;
                _sync = true; _printers.ItemsSource = printers;
                _printers.SelectedItem = previous == null || printers == null ? null : printers.Find(p => p.Name == previous.Name);
                if (_printers.SelectedItem == null) _printers.SelectedIndex = printers == null || printers.Count == 0 ? -1 : 0;
                _sync = false;
                _printerNotice = error != null ? "Printers unavailable: " + error.Message : printers.Count == 0 ? "No Windows printers found. Preview only." : null;
                _refresh.IsEnabled = true; SettingsWork = next = LoadPrinterAsync();
            }));
            if (next != null) await next.ConfigureAwait(false);
        }

        private async Task LoadPrinterAsync()
        {
            int version = ++_settingsVersion; _profile = null; _settings = null;
            var printer = _printers.SelectedItem as ImagePrinterInfo;
            if (printer == null) { _loadingSettings = false; UpdatePreview(); return; }
            _loadingSettings = true; _printerNotice = null; UpdatePreview();
            ImagePrinterProfile profile = null; Exception error = null;
            try { profile = await ImagePrinting.RunSta(() => ImagePrinting.LoadProfile(printer.Name)).ConfigureAwait(false); }
            catch (Exception failure) { error = failure; }
            if (Dispatcher.HasShutdownStarted) return;
            Task next = null;
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                if (_closed || version != _settingsVersion) return;
                _loadingSettings = false; _profile = profile;
                if (error != null) { _printerNotice = "Printer unavailable: " + error.Message; UpdatePreview(); return; }
                _sync = true; _paper.ItemsSource = profile.Papers;
                _paper.SelectedItem = profile.Papers.Find(p => p.Kind == profile.DefaultPaper) ?? profile.Papers[0]; _sync = false;
                next = ReadSettingsAsync();
            }));
            if (next != null) await next.ConfigureAwait(false);
        }

        private bool ReadNumbers(out double margin, out int copies)
        {
            margin = 0; copies = 0;
            return Double.TryParse(_margin.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out margin) && !Double.IsNaN(margin) && !Double.IsInfinity(margin)
                && margin >= 0 && margin <= 100 && Int32.TryParse(_copies.Text, out copies) && copies >= 1 && copies <= 99;
        }

        private async Task ReadSettingsAsync()
        {
            int version = ++_settingsVersion; _settings = null; _loadingSettings = false;
            var profile = _profile; var paper = _paper.SelectedItem as ImagePrintPaper;
            int copies;
            if (profile == null || paper == null || !Int32.TryParse(_copies.Text, out copies) || copies < 1 || copies > 99) { UpdatePreview(); return; }
            bool landscape = _orientation.SelectedIndex == 1; _loadingSettings = true; _printerNotice = null; UpdatePreview();
            ImagePrintSettings settings = null; Exception error = null;
            try { settings = await ImagePrinting.RunSta(() => ImagePrinting.Settings(profile.Name, paper, landscape, copies)).ConfigureAwait(false); }
            catch (Exception failure) { error = failure; }
            if (Dispatcher.HasShutdownStarted) return;
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                if (_closed || version != _settingsVersion) return;
                _settings = settings; _printerNotice = error == null ? null : error.Message;
                _loadingSettings = false; UpdatePreview();
            }));
        }

        private void UpdatePreview()
        {
            if (_closed || _printing) return;
            _print.IsEnabled = false; _layout = null;
            _paper.IsEnabled = _orientation.IsEnabled = _copies.IsEnabled = !_loadingSettings && !_loadingPrinters;
            _printers.IsEnabled = !_loadingPrinters;
            double margin; int copies; var paper = _paper.SelectedItem as ImagePrintPaper;
            if (!ReadNumbers(out margin, out copies)) { _preview.Child = null; SetStatus("Use margins from 0 to 100 mm and copies from 1 to 99.", true); return; }
            if (_pixels == null) { SetStatus(_renderError ?? "Preparing full-resolution image...", _renderError != null); return; }
            if (paper == null) { _preview.Child = null; SetStatus("Choose a paper size.", true); return; }
            try
            {
                Size size = _settings != null ? _settings.PageSize : _orientation.SelectedIndex == 1 ? new Size(paper.Height, paper.Width) : new Size(paper.Width, paper.Height);
                _layout = ImagePrintLayout.Calculate(new Size(_pixels.PixelWidth, _pixels.PixelHeight), size, _settings == null ? new Rect(size) : _settings.ImageableArea, margin, _fit.SelectedIndex == 1);
                _preview.Child = _layout.CreatePage(_pixels); UpdateZoom();
                _details.Text = String.Format("1 page  |  {0:0.#} x {1:0.#} mm  |  {2} x {3} px  |  {4:0} DPI at print size",
                    size.Width * 25.4 / 96, size.Height * 25.4 / 96, _pixels.PixelWidth, _pixels.PixelHeight, _layout.EffectiveDpi);
                bool loading = _loadingPrinters || _loadingSettings;
                _print.IsEnabled = !loading && _settings != null && _profile != null;
                SetStatus(loading ? "Reading printer settings..." : _printerNotice ?? (_settings == null ? "No printer selected. Preview only." : "Ready"), !loading && _printerNotice != null && _profile != null);
            }
            catch (Exception error) { _layout = null; _preview.Child = null; SetStatus(error.Message, true); }
        }

        private void UpdateZoom()
        {
            if (_layout == null) return;
            double scale = _zoom.SelectedIndex == 0 ? Math.Min(Math.Max(1, _viewport.ActualWidth - 65) / _layout.PageSize.Width,
                Math.Max(1, _viewport.ActualHeight - 65) / _layout.PageSize.Height) : _zoom.SelectedIndex == 1 ? 0.5 : _zoom.SelectedIndex == 2 ? 1 : 2;
            _preview.Width = _layout.PageSize.Width * scale; _preview.Height = _layout.PageSize.Height * scale;
        }

        private void SetStatus(string text, bool error)
        {
            _status.Text = text; ThemeManager.Bind(_status, TextBlock.ForegroundProperty, error ? ThemeKeys.UpscaleErrorText : ThemeKeys.MutedText);
        }

        private void BeginPrint()
        {
            if (_printing) return;
            UpdatePreview(); if (!_print.IsEnabled || _settings == null || _layout == null) return;
            try
            {
                _connection = new ImagePrinterConnection(_profile.Name);
                _connection.Queue.CurrentJobSettings.Description = "Image viewer - " + _request.Name;
                var writer = PrintQueue.CreateXpsDocumentWriter(_connection.Queue); _writer = writer;
                _printDocument = _layout.CreateDocument(_pixels);
                writer.WritingCompleted += delegate(object sender, System.Windows.Documents.Serialization.WritingCompletedEventArgs e)
                { Dispatcher.BeginInvoke(new Action(() => FinishPrint(writer, e.Cancelled, e.Error))); };
                writer.WritingCancelled += delegate(object sender, System.Windows.Documents.Serialization.WritingCancelledEventArgs e)
                { Dispatcher.BeginInvoke(new Action(() => FinishPrint(writer, true, e.Error))); };
                _printing = true; _settingsPanel.IsEnabled = false; _print.IsEnabled = false;
                _close.Content = CommandPresentation.Label("\uE711", "Cancel"); SetStatus("Sending image to printer...", false);
                using (var stream = new MemoryStream(_settings.Ticket)) writer.WriteAsync(_printDocument, new PrintTicket(stream));
            }
            catch (Exception error) { FinishPrint(_writer, false, error); }
        }

        private void FinishPrint(XpsDocumentWriter writer, bool canceled, Exception error)
        {
            if (writer != _writer) return;
            _writer = null; _printing = false; _printDocument = null;
            if (_connection != null) { _connection.Dispose(); _connection = null; }
            if (_closed) return;
            _settingsPanel.IsEnabled = true; _close.Content = CommandPresentation.Label("\uE711", "Close"); UpdatePreview();
            SetStatus(canceled ? "Print submission canceled. Check the Windows print queue for pages already accepted."
                : error != null ? "Print failed: " + error.Message : "Sent to printer. Progress is available in the Windows print queue.", error != null && !canceled);
            if (_closeAfterPrint) Close();
        }

        private void CancelPrint()
        {
            if (_writer == null) return;
            try { _writer.CancelAsync(); SetStatus("Canceling print submission...", false); }
            catch (Exception error) { SetStatus("Cannot cancel: " + error.Message + " Check the Windows print queue.", true); }
        }
        private void CloseOrCancel() { if (_printing) CancelPrint(); else Close(); }
    }
}
