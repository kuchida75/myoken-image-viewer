using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed class BatchWindow : Window
    {
        private readonly string[] _paths;
        private readonly string _profile;
        private readonly BatchKind _kind;
        private readonly IList<BatchPreset> _presets;
        private readonly Action _savePresets;
        private readonly Action<IEnumerable<string>> _invalidate;
        private readonly BatchPreset _defaultPreset;
        private readonly List<Action<BatchOptions>> _readers = new List<Action<BatchOptions>>(), _writers = new List<Action<BatchOptions>>();
        private readonly List<Tuple<FrameworkElement, Func<BatchOptions, bool>>> _sections = new List<Tuple<FrameworkElement, Func<BatchOptions, bool>>>();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        private readonly SemaphoreSlim _previewGate = new SemaphoreSlim(1);
        private CancellationTokenSource _previewRequest, _execution;
        private bool _loading, _closed, _busy, _completed;
        private StackPanel _fields;
        private Grid _settings;
        private ComboBox _preset;
        private DataGrid _grid;
        private TextBlock _summary;
        private TextBox _details, _template;
        private Button _run, _close;
        private Button _deletePreset;
        private ProgressBar _progress;
        internal List<BatchItem> Plan { get; private set; }
        internal BatchOptions Options { get; private set; }
        internal BatchResult Result { get; private set; }
        internal Task Work { get; private set; }
        internal bool PreviewReady { get { return Plan != null && !_busy && !_timer.IsEnabled && _previewRequest == null; } }

        internal BatchWindow(BatchKind kind, string[] paths, string profile, IList<BatchPreset> presets, Action savePresets, Action<IEnumerable<string>> invalidate = null)
        {
            _kind = kind; _paths = paths; _profile = profile; _presets = presets; _savePresets = savePresets; _invalidate = invalidate;
            _defaultPreset = new BatchPreset { Name = "Default", Options = BatchOptions.Default(kind) };
            Title = "Batch " + kind.ToString().ToLowerInvariant() + " - " + BuildInfo.AppName;
            Width = 1140; Height = 760; MinWidth = 900; MinHeight = 580; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ThemeManager.PrepareWindow(this); Build(); LoadOptions(BatchOptions.Default(kind));
            _timer.Tick += delegate { _timer.Stop(); Work = PreviewAsync(); };
            Loaded += delegate { Work = PreviewAsync(); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (_busy) { e.Cancel = true; _execution.Cancel(); _summary.Text = "Canceling; waiting for the current image or rename rollback..."; }
                else { _closed = true; _timer.Stop(); if (_previewRequest != null) _previewRequest.Cancel(); }
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        }

        private void Build()
        {
            var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
            ThemeManager.Bind(root, Panel.BackgroundProperty, ThemeKeys.WindowBackground);
            var heading = new TextBlock { Text = _kind == BatchKind.Convert ? "Batch convert format" : "Batch " + _kind.ToString().ToLowerInvariant(), FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
            DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            _details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 72, Visibility = Visibility.Collapsed };
            footer.Children.Add(_details);
            _progress = new ProgressBar { Height = 3, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 6, 0, 10) }; footer.Children.Add(_progress);
            ThemeManager.Bind(_progress, Control.ForegroundProperty, ThemeKeys.Accent); ThemeManager.Bind(_progress, Control.BackgroundProperty, ThemeKeys.Border);
            var buttons = new DockPanel(); footer.Children.Add(buttons);
            _close = CommandPresentation.Button("\uE8BB", "Close", "Esc", "Close, or cancel an active batch safely.", Close, true);
            DockPanel.SetDock(_close, Dock.Right); buttons.Children.Add(_close);
            _run = CommandPresentation.Button("\uE73E", _kind.ToString(), null, "Apply the reviewed batch to the listed items.", StartConfirmed, true);
            _run.MinWidth = 100; _run.IsEnabled = false; DockPanel.SetDock(_run, Dock.Right); buttons.Children.Add(_run);
            _summary = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 14, 0) }; buttons.Children.Add(_summary);
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); root.Children.Add(body);
            _settings = new Grid { Margin = new Thickness(0, 0, 18, 0) }; body.Children.Add(_settings);
            _fields = new StackPanel(); _settings.Children.Add(new ScrollViewer { Content = _fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            var presets = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var remove = CommandPresentation.Button("\uE74D", "Delete preset", null, "Delete the selected saved batch preset.", DeletePreset, false);
            _deletePreset = remove; remove.IsEnabled = false;
            var save = CommandPresentation.Button("\uE74E", "Save preset", null, "Save these batch settings as a named preset.", SavePreset, false);
            DockPanel.SetDock(remove, Dock.Right); DockPanel.SetDock(save, Dock.Right); presets.Children.Add(remove); presets.Children.Add(save);
            _preset = new ComboBox { Height = 32, Margin = new Thickness(0, 0, 8, 0) }; AutomationProperties.SetName(_preset, "Batch preset"); presets.Children.Add(_preset); _fields.Children.Add(presets);
            RefreshPresets(); _preset.SelectionChanged += delegate { var value = _preset.SelectedItem as BatchPreset; _deletePreset.IsEnabled = value != null && value != _defaultPreset; if (!_loading && value != null) { LoadOptions(value.Options); Changed(); } };
            if (_kind == BatchKind.Rename) BuildRename(); else BuildExport();
            var preview = new DockPanel(); Grid.SetColumn(preview, 1); body.Children.Add(preview);
            var title = new TextBlock { Text = "Preview  /  " + _paths.Length + " selected", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(title, Dock.Top); preview.Children.Add(title);
            _grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = false, CanUserAddRows = false, CanUserDeleteRows = false,
                CanUserSortColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, EnableRowVirtualization = true, EnableColumnVirtualization = true,
                RowHeight = 32, GridLinesVisibility = DataGridGridLinesVisibility.None, BorderThickness = new Thickness(1), SelectionMode = DataGridSelectionMode.Single };
            ThemeManager.Bind(_grid, Control.BackgroundProperty, ThemeKeys.PaneBackground); ThemeManager.Bind(_grid, Control.ForegroundProperty, ThemeKeys.Text); ThemeManager.Bind(_grid, Control.BorderBrushProperty, ThemeKeys.Border);
            var headerStyle = new Style(typeof(DataGridColumnHeader)); headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(ThemeKeys.ToolbarBackground)));
            headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(ThemeKeys.Text))); headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
            _grid.ColumnHeaderStyle = headerStyle;
            var rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(ThemeKeys.PaneBackground)));
            rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(ThemeKeys.Text))); _grid.RowStyle = rowStyle;
            var cellStyle = new Style(typeof(DataGridCell)); cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0))); cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6)));
            _grid.CellStyle = cellStyle;
            _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Use", Binding = new Binding("Included") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 44 });
            Column("Current name", "CurrentName", 1.4, "Source"); Column("New name", "NewName", 1.4, "Destination");
            if (_kind != BatchKind.Rename) Column("Dimensions", "Dimensions", 1, "Dimensions"); Column("Status", "Status", 1.6, "Status");
            preview.Children.Add(_grid);
        }

        private void Column(string title, string path, double width, string tooltip)
        {
            var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(tooltip))); style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            _grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = new DataGridLength(width, DataGridLengthUnitType.Star), MinWidth = 95, ElementStyle = style, IsReadOnly = true });
        }
        private void Heading(string text) { _fields.Children.Add(new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 8) }); }
        private Grid Row(string label, FrameworkElement control)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
            ThemeManager.Bind(caption, TextBlock.ForegroundProperty, ThemeKeys.MutedText); row.Children.Add(caption); Grid.SetColumn(control, 1); row.Children.Add(control); _fields.Children.Add(row);
            AutomationProperties.SetName(control, label); control.MinHeight = 30; return row;
        }
        private TextBox Text(string label, Func<BatchOptions, string> get, Action<BatchOptions, string> set, string tooltip = null)
        {
            var box = new TextBox { ToolTip = tooltip, VerticalContentAlignment = VerticalAlignment.Center }; Row(label, box);
            _readers.Add(o => set(o, box.Text)); _writers.Add(o => box.Text = get(o)); box.TextChanged += delegate { Changed(); }; return box;
        }
        private FrameworkElement Number(string label, Func<BatchOptions, double> get, Action<BatchOptions, double> set)
        {
            var box = Text(label, o => get(o).ToString("0.###", CultureInfo.InvariantCulture), (o, text) =>
            { double value; if (!Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) throw new InvalidDataException("Enter a number for " + label + "."); set(o, value); });
            return (FrameworkElement)box.Parent;
        }
        private static int Whole(double value)
        { if (Double.IsNaN(value) || value != Math.Truncate(value)) throw new InvalidDataException("Enter a whole number for counts and quality."); return checked((int)value); }
        private FrameworkElement Choice(string label, string[] choices, Func<BatchOptions, int> get, Action<BatchOptions, int> set)
        {
            var box = new ComboBox { ItemsSource = choices, Height = 32 }; var row = Row(label, box);
            _readers.Add(o => set(o, box.SelectedIndex)); _writers.Add(o => box.SelectedIndex = get(o)); box.SelectionChanged += delegate { Changed(); }; return row;
        }
        private CheckBox Check(string label, Func<BatchOptions, bool> get, Action<BatchOptions, bool> set)
        {
            var box = new CheckBox { Content = label, Margin = new Thickness(0, 0, 0, 10) }; AutomationProperties.SetName(box, label); _fields.Children.Add(box);
            _readers.Add(o => set(o, box.IsChecked == true)); _writers.Add(o => box.IsChecked = get(o)); box.Checked += delegate { Changed(); }; box.Unchecked += delegate { Changed(); }; return box;
        }
        private void VisibleWhen(FrameworkElement element, Func<BatchOptions, bool> visible) { _sections.Add(Tuple.Create(element, visible)); }

        private void BuildRename()
        {
            Heading("Name and sequence"); Check("Use template", o => o.UseTemplate, (o, v) => o.UseTemplate = v);
            _template = Text("Template", o => o.Template, (o, v) => o.Template = v, "* inserts the original name. # inserts a sequence; ### pads to three digits. File extensions are preserved.");
            var metadata = CommandPresentation.Button("\uE946", "Insert metadata", null, "Insert a filename, folder, date or image-dimension token.", InsertMetadata, true); _fields.Children.Add(metadata);
            Choice("Sequence", new[] { "Numbers", "Letters (A, B, ... Z, AA)" }, o => o.Letters ? 1 : 0, (o, v) => o.Letters = v == 1);
            Number("Start", o => o.Start, (o, v) => o.Start = Whole(v)); Number("Step", o => o.Step, (o, v) => o.Step = Whole(v));
            Heading("Text rules"); Text("Find", o => o.Find, (o, v) => o.Find = v); Text("Replace with", o => o.Replace, (o, v) => o.Replace = v);
            Check("Match case", o => o.MatchCase, (o, v) => o.MatchCase = v);
            Text("Remove text", o => o.RemoveText, (o, v) => o.RemoveText = v);
            Number("Remove first", o => o.RemoveStart, (o, v) => o.RemoveStart = Whole(v)); Number("Remove last", o => o.RemoveEnd, (o, v) => o.RemoveEnd = Whole(v));
            Text("Prefix", o => o.Prefix, (o, v) => o.Prefix = v); Text("Suffix", o => o.Suffix, (o, v) => o.Suffix = v);
            Choice("Case", new[] { "Keep case", "lowercase", "UPPERCASE", "Title Case" }, o => (int)o.Case, (o, v) => o.Case = (BatchCase)v);
            Check("Strip spaces", o => o.StripSpaces, (o, v) => o.StripSpaces = v);
            Heading("Conflicts"); Conflict(false);
        }
        private void BuildExport()
        {
            if (_kind == BatchKind.Resize)
            {
                Heading("Size"); Choice("Mode", new[] { "Pixels", "Percentage", "Print size", "Long edge", "Short edge" }, o => (int)o.ResizeMode, (o, v) => o.ResizeMode = (BatchResizeMode)v);
                VisibleWhen(Number("Width / edge", o => o.Width, (o, v) => o.Width = v), o => o.ResizeMode == BatchResizeMode.Pixels || o.ResizeMode == BatchResizeMode.LongEdge || o.ResizeMode == BatchResizeMode.ShortEdge);
                VisibleWhen(Number("Height", o => o.Height, (o, v) => o.Height = v), o => o.ResizeMode == BatchResizeMode.Pixels);
                VisibleWhen(Number("Percentage", o => o.Percent, (o, v) => o.Percent = v), o => o.ResizeMode == BatchResizeMode.Percentage);
                VisibleWhen(Number("Print width", o => o.PrintWidth, (o, v) => o.PrintWidth = v), o => o.ResizeMode == BatchResizeMode.PrintSize);
                VisibleWhen(Number("Print height", o => o.PrintHeight, (o, v) => o.PrintHeight = v), o => o.ResizeMode == BatchResizeMode.PrintSize);
                VisibleWhen(Number("DPI", o => o.Dpi, (o, v) => o.Dpi = v), o => o.ResizeMode == BatchResizeMode.PrintSize);
                VisibleWhen(Choice("Units", new[] { "Inches", "Centimeters" }, o => o.PrintCentimeters ? 1 : 0, (o, v) => o.PrintCentimeters = v == 1), o => o.ResizeMode == BatchResizeMode.PrintSize);
                VisibleWhen(Check("Preserve aspect ratio", o => o.PreserveAspect, (o, v) => o.PreserveAspect = v), o => o.ResizeMode == BatchResizeMode.Pixels || o.ResizeMode == BatchResizeMode.PrintSize);
                VisibleWhen(Choice("Fit within", new[] { "Width and height", "Width", "Height" }, o => (int)o.Fit, (o, v) => o.Fit = (BatchFit)v), o => o.PreserveAspect && (o.ResizeMode == BatchResizeMode.Pixels || o.ResizeMode == BatchResizeMode.PrintSize));
                Choice("Resize", new[] { "Enlarge or reduce", "Reduce only", "Enlarge only" }, o => (int)o.Direction, (o, v) => o.Direction = (BatchDirection)v);
                Choice("Output format", new[] { "Same as source", "JPEG", "PNG", "WebP", "AVIF", "JPEG XL", "GIF (still)" }, o => o.KeepFormat ? 0 : Array.IndexOf(BatchImages.Extensions, o.Format) + 1,
                    (o, v) => { o.KeepFormat = v == 0; if (v > 0) o.Format = BatchImages.Extensions[v - 1]; });
            }
            else
            {
                Heading("Output format"); Choice("Format", new[] { "JPEG", "PNG", "WebP", "AVIF", "JPEG XL", "GIF (still)" }, o => Array.IndexOf(BatchImages.Extensions, o.Format), (o, v) => o.Format = BatchImages.Extensions[v]);
            }
            Heading("Encoding"); Number("Quality (1-100)", o => o.Quality, (o, v) => o.Quality = Whole(v));
            Check("Lossless WebP / JPEG XL", o => o.Lossless, (o, v) => o.Lossless = v);
            Check("Preserve metadata", o => o.PreserveMetadata, (o, v) => o.PreserveMetadata = v);
            Check("Preserve last-modified date", o => o.PreserveDates, (o, v) => o.PreserveDates = v);
            Heading("Destination"); var folder = Text("Folder", o => o.OutputFolder, (o, v) => o.OutputFolder = v, "Leave empty for each image's source folder. Batch export does not include viewer-only adjustments.");
            var browse = CommandPresentation.Button("\uED25", "Choose folder", null, "Select an output folder. Clearing Folder uses the source folders.", delegate
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Batch output folder" })
                    if (dialog.ShowDialog(new BatchDialogOwner(this)) == System.Windows.Forms.DialogResult.OK) folder.Text = dialog.SelectedPath;
            }, true); _fields.Children.Add(browse);
            Text("Subfolder", o => o.Subfolder, (o, v) => o.Subfolder = v); Text("Name suffix", o => o.OutputSuffix, (o, v) => o.OutputSuffix = v);
            Conflict(true);
        }
        private void Conflict(bool overwrite)
        { Choice("Existing names", overwrite ? new[] { "Keep both (new name)", "Skip", "Stop for review", "Overwrite + backup" } : new[] { "Keep both (new name)", "Skip", "Stop for review" }, o => (int)o.Conflict, (o, v) => o.Conflict = (BatchConflict)v); }
        private void InsertMetadata()
        {
            var menu = new ContextMenu { PlacementTarget = _template, Placement = PlacementMode.Bottom };
            foreach (string token in new[] { "{name}", "{folder}", "{created:yyyyMMdd}", "{modified:yyyyMMdd}", "{taken:yyyyMMdd}", "{width}", "{height}" })
            { string value = token; var item = new MenuItem { Header = value }; item.Click += delegate { _template.SelectedText = value; _template.Focus(); }; menu.Items.Add(item); }
            menu.IsOpen = true;
        }
        private void RefreshPresets()
        { _preset.ItemsSource = new[] { _defaultPreset }.Concat(_presets.Where(p => p.Options.Kind == _kind)).ToList(); _preset.SelectedIndex = 0; }
        private void SavePreset()
        {
            try
            {
                BatchOptions options = ReadOptions(); string name = TextPromptWindow.Prompt(this, "Save batch preset", "Preset name", (_preset.SelectedItem as BatchPreset ?? new BatchPreset()).Name ?? "");
                if (String.IsNullOrWhiteSpace(name)) return; name = name.Trim(); if (name.Length > 80) throw new IOException("Preset names are limited to 80 characters.");
                var existing = _presets.FirstOrDefault(p => p.Options.Kind == _kind && String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null && MessageBox.Show(this, "Replace preset " + name + "?", "Save preset", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                if (existing == null && _presets.Count >= 32) throw new IOException("Up to 32 batch presets can be saved.");
                if (existing != null) _presets.Remove(existing); var saved = new BatchPreset { Name = name, Options = options }; _presets.Add(saved); RefreshPresets(); _preset.SelectedItem = saved; _savePresets();
            }
            catch (Exception error) { _summary.Text = error.Message; }
        }
        private void DeletePreset()
        {
            var preset = _preset.SelectedItem as BatchPreset; if (preset == null || preset == _defaultPreset) return;
            if (MessageBox.Show(this, "Delete preset " + preset.Name + "?", "Delete preset", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _presets.Remove(preset); RefreshPresets(); _savePresets();
        }
        internal void LoadOptions(BatchOptions options)
        {
            _loading = true; try { foreach (var write in _writers) write(options); } finally { _loading = false; }
            foreach (var section in _sections) section.Item1.Visibility = section.Item2(options) ? Visibility.Visible : Visibility.Collapsed;
        }
        private BatchOptions ReadOptions()
        { var options = BatchOptions.Default(_kind); foreach (var read in _readers) read(options); options.Validate(); return options; }
        private void Changed()
        {
            if (_loading || _busy || _completed || _closed || _run == null) return;
            _run.IsEnabled = false; if (_previewRequest != null) _previewRequest.Cancel(); _timer.Stop(); _timer.Start();
        }
        internal Task PreviewAsync() { return Dispatcher.InvokeAsync(new Func<Task>(PreviewCoreAsync)).Task.Unwrap(); }
        private async Task PreviewCoreAsync()
        {
            if (_closed || _busy || _completed) return;
            _timer.Stop(); if (_previewRequest != null) _previewRequest.Cancel(); var request = _previewRequest = new CancellationTokenSource();
            _run.IsEnabled = false; Plan = null; _grid.ItemsSource = null; _progress.IsIndeterminate = true; _summary.Text = "Building preview...";
            try
            {
                BatchOptions options = ReadOptions(); foreach (var section in _sections) section.Item1.Visibility = section.Item2(options) ? Visibility.Visible : Visibility.Collapsed;
                await _previewGate.WaitAsync(request.Token);
                List<BatchItem> plan;
                try { plan = await Task.Run(() => BatchPlan.Create(_paths, options, request.Token)); } finally { _previewGate.Release(); }
                if (_closed || request != _previewRequest || request.IsCancellationRequested) return;
                Options = options; Plan = plan; _grid.ItemsSource = plan;
                foreach (var item in plan) item.PropertyChanged += delegate(object sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == "Included" && !_busy && !_completed) UpdateCounts(); };
                UpdateCounts();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_closed && request == _previewRequest) _summary.Text = error.Message; }
            finally { if (request == _previewRequest) { _previewRequest = null; _progress.IsIndeterminate = false; } request.Dispose(); }
        }
        private void StartConfirmed()
        {
            if (!_run.IsEnabled || !PreviewReady) return;
            _grid.CommitEdit(DataGridEditingUnit.Cell, true); _grid.CommitEdit(DataGridEditingUnit.Row, true);
            int overwrite = Plan.Count(item => item.Ready && item.Included && item.Overwrite);
            string message = _kind == BatchKind.Rename ? "Rename " + Plan.Count(item => item.Ready && item.Included) + " selected items using this preview?"
                : "Process " + Plan.Count(item => item.Ready && item.Included) + " images from their on-disk originals?\n\nViewer-only adjustments are excluded. Output is 8-bit; JPEG transparency becomes white and GIF is palette-limited. Metadata preservation depends on the output format."
                + (overwrite == 0 ? "\n\nOriginal files will be kept." : "\n\n" + overwrite + " existing files will be overwritten. Backups are kept in .zen-batch-backups beside each destination.");
            if (MessageBox.Show(this, message, Title, MessageBoxButton.YesNo, overwrite > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question) == MessageBoxResult.Yes) Work = ExecuteAsync();
        }
        internal Task ExecuteAsync() { return Dispatcher.InvokeAsync(new Func<Task>(ExecuteCoreAsync)).Task.Unwrap(); }
        private async Task ExecuteCoreAsync()
        {
            if (!PreviewReady || Plan == null || !Plan.Any(item => item.Ready && item.Included) || Plan.Any(item => item.Included && item.Status.StartsWith("Error:", StringComparison.Ordinal))) return;
            _busy = true; _settings.IsEnabled = false; _grid.IsReadOnly = true; _run.IsEnabled = false; _close.Content = CommandPresentation.Label("\uE711", "Cancel"); _execution = new CancellationTokenSource();
            int finished = 0, total = Plan.Count(item => item.Ready && item.Included); _progress.Value = 0;
            try
            {
                Result = await Task.Run(delegate
                {
                    var result = BatchExecutor.Execute(Plan, Options, _profile, _execution.Token, (item, status) => Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (_closed) return; item.Status = status;
                        if (status == "Completed" || status.StartsWith("Failed:", StringComparison.Ordinal)) finished++;
                        _progress.Value = 100.0 * finished / total; _summary.Text = finished + " / " + total + " processed";
                    })));
                    try { if (_invalidate != null) _invalidate(result.Completed.Select(item => item.Destination)); }
                    catch (Exception error) { result.Errors.Add("Thumbnail refresh: " + error.Message); }
                    return result;
                });
                _summary.Text = Result.Completed.Count + (_kind == BatchKind.Rename ? " renamed" : " written") + " / " + Result.Skipped + " skipped / " + Result.Errors.Count + " errors" + (Result.Canceled ? " / canceled" : "");
                if (Result.Errors.Count > 0 || Result.Journal != null)
                { _details.Text = String.Join(Environment.NewLine, Result.Errors.Take(8)) + (Result.Journal == null ? "" : Environment.NewLine + "Rename recovery log: " + Result.Journal); _details.Visibility = Visibility.Visible; }
            }
            catch (Exception error) { Result = new BatchResult(); Result.Errors.Add(error.Message); _summary.Text = error.Message; }
            finally { _busy = false; _completed = true; _execution.Dispose(); _execution = null; _close.Content = CommandPresentation.Label("\uE8BB", "Close"); }
        }
        private sealed class BatchDialogOwner : System.Windows.Forms.IWin32Window
        { private readonly Window _window; internal BatchDialogOwner(Window window) { _window = window; } public IntPtr Handle { get { return new System.Windows.Interop.WindowInteropHelper(_window).Handle; } } }

        private void UpdateCounts()
        {
            if (Plan == null) return;
            int ready = Plan.Count(item => item.Ready && item.Included), errors = Plan.Count(item => item.Included && item.Status.StartsWith("Error:", StringComparison.Ordinal));
            _summary.Text = ready + " ready / " + (Plan.Count - ready - errors) + " skipped / " + errors + " errors";
            _run.IsEnabled = ready > 0 && errors == 0;
        }
    }
}
