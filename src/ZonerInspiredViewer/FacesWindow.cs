using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ZonerInspiredViewer
{
    internal sealed partial class FacesWindow : Window
    {
        private readonly int _workers, _budget;
        private readonly string _adapterKey;
        private readonly ThumbnailCacheService _folderThumbnails;
        private readonly TextBox _folder;
        private readonly CheckBox _includeSubfolders;
        private readonly Button _chooseFolder, _scan, _remove, _openOriginal, _update, _export, _stop, _backToCanvas, _refreshLinks;
        private readonly ListBox _review;
        private readonly ObservableCollection<FaceCrop> _faces = new ObservableCollection<FaceCrop>();
        private readonly TabControl _tabs;
        private readonly CanvasFields _reference, _interactive;
        private readonly TextBlock _status, _backend, _summary, _zoomText;
        private readonly Image _preview;
        private readonly ScrollViewer _viewport;
        private readonly ProgressBar _progress;
        private CancellationTokenSource _cancel;
        private bool _busy, _closeRequested, _closed, _fit = true;
        private double _zoom = 1;
        private Size _canvasSize;
        private Point? _drag;
        private Point _dragOffset;
        private bool _dragged;
        private FaceCrop[] _linkedFaces;
        private Int32Rect[] _linkedCells;
        private CanvasSnapshot _canvasSnapshot;
        private sealed class CanvasSnapshot
        {
            internal BitmapSource Image;
            internal Size Size;
            internal double Zoom, X, Y;
            internal bool Fit;
            internal string Status;
        }
        internal Task PendingWork { get; private set; }
        internal string RequestedOriginal { get; private set; }
        private sealed class CanvasFields
        {
            internal TextBox Width, Height, Columns;
            internal Slider WidthSlider, HeightSlider, AspectSlider;
            internal TextBlock AspectValue;
            internal CheckBox AutoHeight, AutoColumns;
            internal FrameworkElement Panel;
            internal bool Syncing;
        }

        internal FacesWindow(string folder, int workers, string adapterKey, int budget, ThumbnailCacheService folderThumbnails = null)
        {
            _folderThumbnails = folderThumbnails;
            _workers = workers; _adapterKey = adapterKey; _budget = budget;
            Title = "Faces - reference and interactive canvas"; Width = 1180; Height = 800; MinWidth = 780; MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; ThemeManager.PrepareWindow(this);
            Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(FacesStyles));
            var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
            var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _folder = new TextBox { Text = folder ?? "", Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(_folder, "Folder to scan"); header.Children.Add(_folder);
            _chooseFolder = Button("\uED25", "Choose folder", "Choose the root folder for the face scan.", ChooseFolder);
            Grid.SetColumn(_chooseFolder, 1); header.Children.Add(_chooseFolder);
            _scan = Button("\uE721", "Scan faces", "Detect face locations on the configured NVIDIA RTX GPU. No identities are inferred.", Scan);
            Grid.SetColumn(_scan, 2); header.Children.Add(_scan); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            _includeSubfolders = new CheckBox { Content = "Include subfolders", IsChecked = false, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            CommandPresentation.Describe(_includeSubfolders, "Include subfolders", null, "Scan nested folders too. Links and viewer backup folders are skipped; the 2048-image and 8192-entry limits apply across the whole scan.");
            _backend = Text("RTX inference required", 12); _backend.VerticalAlignment = VerticalAlignment.Center; _backend.TextTrimming = TextTrimming.CharacterEllipsis;
            ThemeManager.Bind(_backend, TextBlock.ForegroundProperty, ThemeKeys.GpuDedicatedValue);

            var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            _status = Text("Ready", 12); _status.TextTrimming = TextTrimming.CharacterEllipsis; _status.TextWrapping = TextWrapping.Wrap; _status.MaxHeight = 36;
            _status.VerticalAlignment = VerticalAlignment.Center; _status.Margin = new Thickness(0, 0, 14, 0);
            _progress = new ProgressBar { Height = 3, Margin = new Thickness(0, 6, 0, 6), Visibility = Visibility.Collapsed, IsIndeterminate = true }; footer.Children.Add(_progress);
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(actions);
            _stop = Button("\uE71A", "Stop", "Cancel the current scan, framing update, preview or export.", StopWork); actions.Children.Add(_stop);
            _update = Button("\uE72C", "Update canvas", "Render a preview using the current faces and settings.", UpdateCanvas); actions.Children.Add(_update);
            _openCanvas = Button("\uED25", "Open canvas", "Open a saved .myokenfaces map, including cached thumbnails and original-image links.", OpenCanvas); actions.Children.Add(_openCanvas);
            _saveCanvas = Button("\uE74E", "Save canvas", "Save this interactive map, framing and viewport as .myokenfaces. Original photos remain separate.", SaveCanvas); actions.Children.Add(_saveCanvas);
            _export = Button("\uE74E", "Export PNG", "Create a new full-resolution PNG from the original face crops. Existing files are never overwritten.", Export); actions.Children.Add(_export);
            actions.Children.Add(Button("\uE711", "Close", "Close Faces. Pending work is canceled before the window closes.", Close));
            var statusRow = new Grid { Margin = new Thickness(0, 8, 0, 0), MinHeight = 24 };
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); statusRow.ColumnDefinitions.Add(new ColumnDefinition());
            statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            statusRow.Children.Add(_includeSubfolders); Grid.SetColumn(_status, 1); statusRow.Children.Add(_status);
            Grid.SetColumn(_backend, 2); _backend.HorizontalAlignment = HorizontalAlignment.Right; statusRow.Children.Add(_backend); footer.Children.Add(statusRow);

            var work = new Grid(); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(252), MinWidth = 190 });
            work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 370 }); root.Children.Add(work);
            var left = new DockPanel(); work.Children.Add(left);
            _summary = Text("Review face crops (0)", 14); _summary.FontWeight = FontWeights.SemiBold; _summary.Margin = new Thickness(0, 0, 0, 8);
            DockPanel.SetDock(_summary, Dock.Top); left.Children.Add(_summary);
            var framing = BuildFramingControls(); DockPanel.SetDock(framing, Dock.Top); left.Children.Add(framing);
            var reviewButtons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(reviewButtons, Dock.Bottom); left.Children.Add(reviewButtons);
            _remove = Button("\uE74D", "Remove", "Remove selected crops from this canvas only. Original photos are unchanged.", RemoveFaces); reviewButtons.Children.Add(_remove);
            _openOriginal = Button("\uE8A7", "Open original", "Close Faces and open the selected crop's original photograph in a viewer tab.", OpenOriginal); reviewButtons.Children.Add(_openOriginal);
            _review = new ListBox { ItemsSource = _faces, SelectionMode = SelectionMode.Extended, HorizontalContentAlignment = HorizontalAlignment.Stretch, ItemTemplate = FaceTemplate() };
            ThemeManager.Bind(_review, Control.BackgroundProperty, ThemeKeys.PaneBackground);
            ThemeManager.Bind(_review, Control.ForegroundProperty, ThemeKeys.Text);
            ThemeManager.Bind(_review, Control.BorderBrushProperty, ThemeKeys.Border);
            VirtualizingPanel.SetIsVirtualizing(_review, true); VirtualizingPanel.SetVirtualizationMode(_review, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(_review, true); ScrollViewer.SetHorizontalScrollBarVisibility(_review, ScrollBarVisibility.Disabled);
            AutomationProperties.SetName(_review, "Detected face crops"); left.Children.Add(_review);
            _review.SelectionChanged += delegate { UpdateControls(); };
            var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
            ThemeManager.Bind(splitter, Control.BackgroundProperty, ThemeKeys.Splitter); Grid.SetColumn(splitter, 1); work.Children.Add(splitter);
            var right = new DockPanel(); Grid.SetColumn(right, 2); work.Children.Add(right);
            _tabs = new TabControl { Margin = new Thickness(0, 0, 0, 8) };
            _reference = Fields(); _interactive = Fields();
            _tabs.Items.Add(new TabItem { Header = "Faces reference", Content = _reference.Panel });
            _tabs.Items.Add(new TabItem { Header = "Interactive faces", Content = _interactive.Panel }); _tabs.SelectedIndex = 0;
            DockPanel.SetDock(_tabs, Dock.Top); right.Children.Add(_tabs);
            var zoomRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            _backToCanvas = Button("\uE72B", "Canvas", "Return to the interactive face canvas (Esc).", ReturnToCanvas);
            _backToCanvas.Visibility = Visibility.Collapsed; zoomRow.Children.Add(_backToCanvas);
            _refreshLinks = Button("\uE72C", "Refresh links", "Check linked originals and rebuild this canvas without missing or unavailable images. No files are deleted.", RefreshLinkedCanvas);
            _refreshLinks.Visibility = Visibility.Collapsed; zoomRow.Children.Add(_refreshLinks);
            zoomRow.Children.Add(Button("\uE9A6", "Fit", "Fit the whole canvas inside the preview.", delegate { _fit = true; UpdateZoom(); }));
            zoomRow.Children.Add(Button("\uE9A5", "100%", "View the full output canvas at actual pixel size.", delegate { _fit = false; SetZoom(1); }));
            zoomRow.Children.Add(CommandPresentation.Button("\uE71F", "Zoom out", null, "Reduce canvas magnification.", delegate { _fit = false; SetZoom(_zoom / 1.25); }, false));
            zoomRow.Children.Add(CommandPresentation.Button("\uE710", "Zoom in", null, "Increase canvas magnification.", delegate { _fit = false; SetZoom(_zoom * 1.25); }, false));
            _zoomText = Text("", 12); _zoomText.VerticalAlignment = VerticalAlignment.Center; zoomRow.Children.Add(_zoomText);
            DockPanel.SetDock(zoomRow, Dock.Top); right.Children.Add(zoomRow);
            _preview = new Image { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Focusable = true };
            _viewport = new ScrollViewer { Content = _preview, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            var previewFrame = new Grid { ClipToBounds = true }; previewFrame.Children.Add(_viewport); right.Children.Add(previewFrame);
            ThemeManager.Bind(_viewport, Control.BackgroundProperty, ThemeKeys.WorkspaceBackground); BuildCanvasNavigator(previewFrame);
            _viewport.SizeChanged += delegate { if (_fit) UpdateZoom(); else UpdateCanvasNavigator(); };
            _viewport.ScrollChanged += delegate { UpdateCanvasNavigator(); };
            _viewport.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) { if (_preview.Source == null) return; _fit = false; SetZoom(_zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2)); e.Handled = true; };
            _preview.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            { BeginPan(e.GetPosition(_viewport)); e.Handled = true; };
            _preview.MouseMove += delegate(object sender, MouseEventArgs e)
            { if (e.LeftButton == MouseButtonState.Pressed) MovePan(e.GetPosition(_viewport)); };
            _preview.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            { CompletePan(e.GetPosition(_preview)); e.Handled = true; };
            _preview.LostMouseCapture += delegate { _drag = null; };
            _tabs.SelectionChanged += delegate(object sender, SelectionChangedEventArgs e) { if (e.Source == _tabs) InvalidatePreview(); };
            _folder.TextChanged += delegate { if (!_busy) { _faces.Clear(); InvalidatePreview(); UpdateControls(); } };
            _includeSubfolders.Checked += ScanScopeChanged; _includeSubfolders.Unchecked += ScanScopeChanged;
            PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            { if (e.ChangedButton == MouseButton.XButton1 && _canvasSnapshot != null) { ReturnToCanvas(); e.Handled = true; } };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Escape(); e.Handled = true; } };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (_busy || _canvasResizeRunning)
                {
                    e.Cancel = true; _closeRequested = true; CancelCanvasResize();
                    if (_busy) _cancel.Cancel();
                }
            };
            Closed += delegate { _closed = true; CancelCanvasResize(); _framingTimer.Stop(); EndPan(); HideCanvasNavigator(); _preview.Source = null; _canvasSnapshot = null; _linkedFaces = null; _linkedCells = null; _faces.Clear(); };
            UpdateControls();
        }
        private static TextBlock Text(string text, double size) { return new TextBlock { Text = text, FontSize = size }; }
        private static Button Button(string icon, string label, string tip, Action action) { return CommandPresentation.Button(icon, label, null, tip, action, true); }
        private static TextBox Number(string value, int width, string label)
        { var box = new TextBox { Text = value, Width = width, Height = 30, VerticalContentAlignment = VerticalAlignment.Center }; AutomationProperties.SetName(box, label); return box; }
        private static DataTemplate FaceTemplate()
        {
            var template = new DataTemplate(typeof(FaceCrop)); var root = new FrameworkElementFactory(typeof(StackPanel));
            root.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 6, 4, 6)); root.SetBinding(FrameworkElement.ToolTipProperty, new Binding("FullPath"));
            var image = new FrameworkElementFactory(typeof(Image)); image.SetValue(FrameworkElement.HeightProperty, 112.0); image.SetValue(Image.StretchProperty, Stretch.Uniform); image.SetBinding(Image.SourceProperty, new Binding("Thumbnail")); root.AppendChild(image);
            var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); name.SetBinding(TextBlock.TextProperty, new Binding("Name")); root.AppendChild(name);
            var score = new FrameworkElementFactory(typeof(TextBlock)); score.SetValue(TextBlock.FontSizeProperty, 11.0); score.SetBinding(TextBlock.TextProperty, new Binding("Description")); root.AppendChild(score);
            template.VisualTree = root; return template;
        }
        private void ChooseFolder()
        {
            var picker = new FolderPickerWindow(_folder.Text, _folderThumbnails) { Owner = this, Icon = Icon };
            picker.ShowDialog(); if (picker.SelectedFolder != null) _folder.Text = picker.SelectedFolder;
        }
        internal void StartScan() { if (!_busy && !_closed) Scan(); }
        private void Scan()
        {
            string folder = _folder.Text; bool includeSubfolders = _includeSubfolders.IsChecked == true; var framing = SelectedFraming();
            _backend.Text = "Verifying RTX inference...";
            BeginWork(async token =>
            {
                var result = await Task.Run(() => FaceImages.Scan(folder, _workers, _adapterKey, _budget, token, Progress, includeSubfolders, framing), token);
                SetReview(result); _status.Text = result.Faces.Count + " faces in " + result.Images + " images; " + result.Skipped + " skipped" + (result.Limited ? ". Safety limit reached; scan a smaller folder for remaining images." : ".");
                _status.ToolTip = String.Join(Environment.NewLine, result.Errors);
            }, "Preparing RTX face detector...");
        }
        private void ScanScopeChanged(object sender, RoutedEventArgs e)
        { if (!_busy) { _faces.Clear(); InvalidatePreview(); _status.Text = "Ready"; UpdateControls(); } }
        internal void SetReview(FaceScanResult result)
        {
            _framingTimer.Stop(); _framingPending = false;
            if (result.Faces.Count > 0) _appliedFraming = result.Faces[0].Framing.Copy();
            SyncFraming(_appliedFraming);
            _faces.Clear(); foreach (var face in result.Faces) _faces.Add(face);
            _backend.Text = result.Backend; _backend.ToolTip = result.ExecutionEvidence;
            UpdateReferenceColumns();
            InvalidatePreview(); UpdateControls();
        }
        private void RemoveFaces()
        { foreach (var face in _review.SelectedItems.Cast<FaceCrop>().ToArray()) _faces.Remove(face); UpdateReferenceColumns(); InvalidatePreview(); UpdateControls(); }
        private void UpdateReferenceColumns()
        {
            foreach (var fields in new[] { _reference, _interactive })
                if (fields != null)
                {
                    if (fields.AutoColumns.IsChecked == true && _faces.Count > 0) fields.Columns.Text = FaceCanvasOptions.AutoRowCounts(_faces.Count).Max().ToString(CultureInfo.InvariantCulture);
                    SyncCanvasFields(fields);
                }
        }
        private void OpenOriginal()
        {
            var face = _review.SelectedItem as FaceCrop; if (face == null) return;
            BeginWork(async token =>
            {
                var source = await Task.Run(() => FaceImages.Inspect(face.Source.Path, token), token);
                if (source.Hash != face.Source.Hash || source.Modified != face.Source.Modified) throw new IOException("Original changed since review. Scan again.");
                RequestedOriginal = source.Path; _closeRequested = true;
            }, "Checking original...");
        }
        private FaceCanvasOptions Options()
        { return ReadOptions(true); }
        private FaceCanvasOptions ReadOptions(bool updateFields)
        {
            var fields = _tabs.SelectedIndex == 1 ? _interactive : _reference; int width, height, columns;
            if (!Int32.TryParse(fields.Width.Text, out width) || !Int32.TryParse(fields.Height.Text, out height) || !Int32.TryParse(fields.Columns.Text, out columns))
                throw new InvalidDataException("Enter whole numbers for canvas dimensions and columns.");
            var o = new FaceCanvasOptions { Width = width, Height = height, Columns = columns, AutoHeight = fields.AutoHeight.IsChecked == true, CellAspect = SelectedFraming().Aspect };
            o.AutoColumns = fields.AutoColumns != null && fields.AutoColumns.IsChecked == true;
            o.Validate(_faces.Count); if (updateFields && o.AutoColumns) fields.Columns.Text = o.Columns.ToString(CultureInfo.InvariantCulture);
            if (updateFields && o.AutoHeight) fields.Height.Text = o.Height.ToString(CultureInfo.InvariantCulture); return o;
        }
        private void UpdateCanvas()
        {
            if (_canvasSnapshot != null) return;
            FaceCanvasOptions options; try { options = Options(); } catch (Exception error) { _status.Text = error.Message; return; }
            FaceCrop[] faces = _faces.ToArray();
            BeginWork(async token =>
            {
                var bitmap = await Task.Run(() => FaceImages.Preview(faces, options, token), token);
                ApplyCanvas(bitmap, options, faces);
                _status.Text = options.Width + " x " + options.Height + " px / " + faces.Length + " face crops";
            }, "Rendering canvas preview...");
        }
        private void Export()
        {
            if (_tabs.SelectedIndex != 0 || _canvasSnapshot != null || _canvasResizePending || _canvasResizeRunning) return;
            FaceCanvasOptions options; try { options = Options(); } catch (Exception error) { _status.Text = error.Message; return; }
            var picker = new SaveFileDialog { Title = "Export new face canvas", Filter = "PNG image|*.png", DefaultExt = ".png", AddExtension = true,
                FileName = "faces-reference.png", OverwritePrompt = false };
            if (!ThemedFileDialogs.Show(picker, this, _folderThumbnails)) return; string path = picker.FileName; FaceCrop[] faces = _faces.ToArray();
            BeginWork(async token => { await Task.Run(() => FaceImages.Export(faces, options, path, token, Progress), token); _status.Text = "Saved: " + path; }, "Exporting full-resolution canvas...");
        }
        private void BeginWork(Func<CancellationToken, Task> work, string status)
        {
            if (_busy || _closed) return; CancelCanvasResize(); _busy = true; _status.Text = status; _cancel = new CancellationTokenSource(); UpdateControls();
            PendingWork = Dispatcher.InvokeAsync(new Func<Task>(delegate { return RunWork(work); })).Task.Unwrap();
        }
        private async Task RunWork(Func<CancellationToken, Task> work)
        {
            try { await work(_cancel.Token); }
            catch (OperationCanceledException) { _status.Text = "Canceled. Original photos were not changed."; }
            catch (Exception error) { _status.Text = error.GetBaseException().Message; _status.ToolTip = error.GetBaseException().Message; }
            finally
            {
                _cancel.Dispose(); _cancel = null; _busy = false; UpdateControls();
                if (_closeRequested) Close();
            }
        }
        private void Progress(string value)
        {
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_closed || !_busy) return;
                _status.Text = value;
                if (value.Contains("verified RTX inference")) _backend.Text = value;
            }));
        }
        private void InvalidatePreview()
        {
            CancelCanvasResize(); _displayedCanvasOptions = null;
            if (_preview != null) { EndPan(); HideCanvasNavigator(); _preview.Source = null; _preview.Cursor = null; _canvasSize = new Size(); _canvasSnapshot = null; _linkedFaces = null; _linkedCells = null; }
            if (_backToCanvas != null) _backToCanvas.Visibility = Visibility.Collapsed;
            if (_review != null) UpdateControls();
        }
        private void UpdateControls()
        {
            if (_scan == null || _review == null) return;
            bool editing = !_busy && !_framingPending && _canvasSnapshot == null;
            _scan.IsEnabled = _chooseFolder.IsEnabled = _folder.IsEnabled = _tabs.IsEnabled = editing;
            _includeSubfolders.IsEnabled = editing;
            _review.IsEnabled = editing; _remove.IsEnabled = editing && _review.SelectedItems.Count > 0;
            _openOriginal.IsEnabled = editing && _review.SelectedItems.Count == 1;
            _update.IsEnabled = editing && _faces.Count > 0; _export.IsEnabled = _update.IsEnabled && _tabs.SelectedIndex == 0 && !_canvasResizePending && !_canvasResizeRunning;
            _backToCanvas.IsEnabled = !_busy;
            _refreshLinks.Visibility = _tabs.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            _refreshLinks.IsEnabled = editing && _faces.Count > 0;
            _openCanvas.IsEnabled = editing;
            _saveCanvas.IsEnabled = editing && _tabs.SelectedIndex == 1 && _faces.Count > 0 && _preview.Source != null && !_canvasResizePending && !_canvasResizeRunning;
            _surroundings.IsEnabled = _cropShape.IsEnabled = _resetFraming.IsEnabled = !_busy && _canvasSnapshot == null;
            _stop.IsEnabled = _busy || _framingPending || _canvasResizePending || _canvasResizeRunning;
            _progress.Visibility = _busy || _canvasResizeRunning ? Visibility.Visible : Visibility.Collapsed;
            _summary.Text = "Review face crops (" + _faces.Count + ")";
            UpdateCanvasNavigator();
        }
        private void UpdateZoom()
        {
            if (_canvasSize.IsEmpty || _canvasSize.Width == 0) return;
            if (_fit) _zoom = Math.Min(Math.Max(1, _viewport.ActualWidth - 32) / _canvasSize.Width, Math.Max(1, _viewport.ActualHeight - 32) / _canvasSize.Height);
            _preview.Width = _canvasSize.Width * _zoom; _preview.Height = _canvasSize.Height * _zoom; _zoomText.Text = _zoom.ToString("P0");
            UpdateCanvasNavigator();
        }
        private void SetZoom(double value) { _zoom = Math.Max(.01, Math.Min(8, value)); UpdateZoom(); }

        private const string FacesStyles = @"
<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Style TargetType=""ListBox"">
    <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ListBox""><Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}""><ScrollViewer Focusable=""False"" CanContentScroll=""True"" Padding=""{TemplateBinding Padding}""><ItemsPresenter/></ScrollViewer></Border></ControlTemplate></Setter.Value></Setter>
    <Style.Triggers><Trigger Property=""IsEnabled"" Value=""False""><Setter Property=""Opacity"" Value=""0.55""/></Trigger></Style.Triggers>
  </Style>
  <Style TargetType=""TabControl"">
    <Setter Property=""Foreground"" Value=""{DynamicResource Theme.Text}"" />
    <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""TabControl"">
      <Grid><Grid.RowDefinitions><RowDefinition Height=""Auto""/><RowDefinition Height=""*""/></Grid.RowDefinitions>
        <TabPanel IsItemsHost=""True""/>
        <Border Grid.Row=""1"" BorderThickness=""1"" BorderBrush=""{DynamicResource Theme.Border}"" Background=""{DynamicResource Theme.PaneBackground}""><ContentPresenter ContentSource=""SelectedContent""/></Border>
      </Grid>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType=""TabItem"">
    <Setter Property=""Foreground"" Value=""{DynamicResource Theme.Text}""/>
    <Setter Property=""Background"" Value=""{DynamicResource Theme.ToolbarBackground}""/>
    <Setter Property=""BorderBrush"" Value=""{DynamicResource Theme.Border}""/>
    <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""TabItem"">
      <Border Padding=""12,7"" BorderThickness=""1,1,1,0"" BorderBrush=""{TemplateBinding BorderBrush}"" Background=""{TemplateBinding Background}""><ContentPresenter ContentSource=""Header""/></Border>
    </ControlTemplate></Setter.Value></Setter>
    <Style.Triggers><Trigger Property=""IsSelected"" Value=""True""><Setter Property=""Background"" Value=""{DynamicResource Theme.SelectedBackground}""/><Setter Property=""BorderBrush"" Value=""{DynamicResource Theme.Accent}""/></Trigger></Style.Triggers>
  </Style>
  <Style TargetType=""ListBoxItem"">
    <Setter Property=""Foreground"" Value=""{DynamicResource Theme.Text}""/>
    <Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""HorizontalContentAlignment"" Value=""Stretch""/>
    <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ListBoxItem""><Border Background=""{TemplateBinding Background}""><ContentPresenter HorizontalAlignment=""Stretch""/></Border></ControlTemplate></Setter.Value></Setter>
    <Style.Triggers><Trigger Property=""IsSelected"" Value=""True""><Setter Property=""Background"" Value=""{DynamicResource Theme.SelectedBackground}""/></Trigger><Trigger Property=""IsMouseOver"" Value=""True""><Setter Property=""Background"" Value=""{DynamicResource Theme.ControlHover}""/></Trigger></Style.Triggers>
  </Style>
</ResourceDictionary>";
    }
}
