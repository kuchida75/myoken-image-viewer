using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed class AdvancedSearchWindow : Window
    {
        private readonly RecursiveSearchIndex _index;
        private readonly Action<ImageFileItem, bool> _open;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private CancellationTokenSource _scanRequest, _queryRequest;
        private readonly DispatcherTimer _queryTimer, _refreshTimer, _layoutTimer;
        private FileSystemWatcher _watcher;
        private SearchSnapshot _snapshot;
        private readonly TextBox _query;
        private readonly SearchHistoryInput _searchHistoryInput;
        private readonly TextBlock _status, _selection, _empty;
        private readonly ComboBox _sort;
        private readonly ToggleButton _descending;
        private readonly Slider _size, _aspect;
        private readonly Button _stop;
        private readonly VirtualizedThumbnailGrid _grid;
        private List<ImageFileItem> _results = new List<ImageFileItem>();
        private bool _closed, _initialized, _started, _scanRunning, _queryRunning, _stopped;
        private bool _refreshNeeded = true;
        private int _queryVersion, _changePosted;
        private string _scanStatus = "", _error = "", _watcherWarning = "";
        internal string RootFolder { get; private set; }

        public AdvancedSearchWindow(AppServices services, string root, string initialQuery, int thumbnailSize, double aspect,
            Action<ImageFileItem, bool> open, SearchHistory history = null)
        {
            RootFolder = RecursiveSearchIndex.CanonicalPath(root);
            _index = new RecursiveSearchIndex(services.AppDataRoot, services.CpuWorkerCount);
            _open = open;
            Title = "Advanced search - " + RootFolder;
            Width = 1080; Height = 740; MinWidth = 640; MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ThemeManager.PrepareWindow(this);
            _queryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _layoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
            _queryTimer.Tick += delegate { _queryTimer.Stop(); RunQuery(); EnsureRefresh(); };
            _refreshTimer.Tick += delegate { _refreshTimer.Stop(); EnsureRefresh(); };

            var body = new DockPanel { Margin = new Thickness(12) }; Content = body;
            ThemeManager.Bind(body, Panel.BackgroundProperty, ThemeKeys.WindowBackground);
            var title = Label("Advanced search", 20); title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(0, 0, 0, 6); DockPanel.SetDock(title, Dock.Top); body.Children.Add(title);
            var scope = Label("Within: " + RootFolder, 12); scope.ToolTip = RootFolder;
            scope.TextTrimming = TextTrimming.CharacterEllipsis; scope.Margin = new Thickness(0, 0, 0, 12);
            DockPanel.SetDock(scope, Dock.Top); body.Children.Add(scope);
            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(searchRow, Dock.Top); body.Children.Add(searchRow);
            _stop = IconButton("\uE71A", "Stop search and index refresh", Stop);
            DockPanel.SetDock(_stop, Dock.Right); searchRow.Children.Add(_stop);
            var refresh = IconButton("\uE72C", "Refresh search index", delegate
            {
                _started = true; _stopped = false; _refreshNeeded = true; _error = ""; EnsureRefresh(); RunQuery();
            });
            DockPanel.SetDock(refresh, Dock.Right); searchRow.Children.Add(refresh);
            var name = Label("Name", 13); name.VerticalAlignment = VerticalAlignment.Center; name.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(name, Dock.Left); searchRow.Children.Add(name);
            _query = new TextBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Text = initialQuery ?? "" };
            AutomationProperties.SetName(_query, "Recursive image and folder name search");
            _searchHistoryInput = new SearchHistoryInput(_query, history ?? new SearchHistory(), delegate
                { _queryTimer.Stop(); _stopped = false; RunQuery(); EnsureRefresh(); });
            DockPanel.SetDock(_searchHistoryInput.Button, Dock.Right); searchRow.Children.Add(_searchHistoryInput.Button);
            searchRow.Children.Add(_query);
            var options = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(options, Dock.Top); body.Children.Add(options);
            var sortGroup = new StackPanel { Orientation = Orientation.Horizontal, Height = 32, Margin = new Thickness(0, 0, 16, 6) };
            options.Children.Add(sortGroup);
            var sortLabel = Label("Sort", 12); sortLabel.VerticalAlignment = VerticalAlignment.Center; sortLabel.Margin = new Thickness(0, 0, 8, 0); sortGroup.Children.Add(sortLabel);
            _sort = new ComboBox { Width = 122, Height = 30, ItemsSource = Enum.GetValues(typeof(BrowserSortField)), SelectedIndex = 0 };
            AutomationProperties.SetName(_sort, "Search result sort"); sortGroup.Children.Add(_sort);
            _descending = new ToggleButton { Content = "\uE74A", FontFamily = new FontFamily("Segoe MDL2 Assets"), Width = 32, Height = 30,
                Margin = new Thickness(6, 0, 0, 0), ToolTip = "Descending order" };
            AutomationProperties.SetName(_descending, "Descending order"); sortGroup.Children.Add(_descending);
            _size = AddSlider(options, "Size", 96, 320, thumbnailSize, 32);
            _aspect = AddSlider(options, "Shape", 0.5, 2, aspect, 0.25);

            var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom); body.Children.Add(footer);
            _selection = Label("", 12); _selection.TextTrimming = TextTrimming.CharacterEllipsis; _selection.Height = 20; footer.Children.Add(_selection);
            _status = Label("", 12); _status.TextWrapping = TextWrapping.Wrap; _status.MinHeight = 34; footer.Children.Add(_status);
            var results = new Grid(); body.Children.Add(results);
            _grid = new VirtualizedThumbnailGrid(services.Thumbnails) { ContextMenu = null,
                ItemMenuFactory = ResultMenu, ItemDetails = RelativeParent };
            _grid.SetThumbnailLayout(thumbnailSize, aspect);
            _grid.ItemActivated += delegate(object sender, ThumbnailActivatedEventArgs e) { OpenResult(e.Item, false); };
            _grid.ItemSelected += delegate { UpdateSelection(); };
            results.Children.Add(_grid);
            _empty = Label("", 15); _empty.TextWrapping = TextWrapping.Wrap; _empty.MaxWidth = 480;
            _empty.HorizontalAlignment = HorizontalAlignment.Center; _empty.VerticalAlignment = VerticalAlignment.Center; _empty.IsHitTestVisible = false;
            results.Children.Add(_empty);
            _query.TextChanged += delegate
            {
                _stopped = false; _error = ""; CancelQuery(); _queryTimer.Stop(); _queryTimer.Start();
            };
            _sort.SelectionChanged += delegate { CancelQuery(); RunQuery(); };
            _descending.Click += delegate { CancelQuery(); RunQuery(); };
            _layoutTimer.Tick += delegate { _layoutTimer.Stop(); _grid.SetThumbnailLayout((int)_size.Value, _aspect.Value); };
            _size.ValueChanged += delegate { _layoutTimer.Stop(); _layoutTimer.Start(); };
            _aspect.ValueChanged += delegate { _layoutTimer.Stop(); _layoutTimer.Start(); };
            PreviewKeyDown += SearchKeyDown;
            Loaded += delegate { Initialize(); _query.Focus(); _query.SelectAll(); };
            Closed += delegate
            {
                CommitSearchHistory();
                _closed = true; _lifetime.Cancel(); CancelQuery();
                _queryTimer.Stop(); _refreshTimer.Stop(); _layoutTimer.Stop();
                if (_watcher != null) { _watcher.Dispose(); _watcher = null; }
                _grid.SetItems(null);
            };
            UpdateStatus();
        }

        internal void CommitSearchHistory(bool submitted = false) { _searchHistoryInput.Commit(submitted); }

        private async void Initialize()
        {
            try
            {
                _snapshot = await Task.Run(delegate { return _index.Load(RootFolder, _lifetime.Token); }, _lifetime.Token);
                if (_closed) return;
                InstallWatcher(); _initialized = true; RunQuery(); EnsureRefresh();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!_closed) { _initialized = true; _error = ex.Message; UpdateStatus(); } }
        }

        private void InstallWatcher()
        {
            if (_watcher != null) { _watcher.Dispose(); _watcher = null; }
            _watcherWarning = "";
            try
            {
                _watcher = new FileSystemWatcher(RootFolder) { IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 32768 };
                _watcher.Changed += Changed; _watcher.Created += Changed; _watcher.Deleted += Changed;
                _watcher.Renamed += delegate(object sender, RenamedEventArgs e)
                {
                    if (!_index.IsExcluded(e.FullPath) || !_index.IsExcluded(e.OldFullPath)) QueueChange();
                };
                _watcher.Error += delegate { Post(delegate { _watcherWarning = "Folder monitoring interrupted; refreshing index. Use Refresh to reconnect."; MarkDirty(); }); };
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is System.Security.SecurityException)) throw;
                if (_watcher != null) { _watcher.Dispose(); _watcher = null; }
                _watcherWarning = "Live folder monitoring unavailable. Use Refresh to update results.";
            }
        }

        private void Changed(object sender, FileSystemEventArgs e)
        {
            if (_index.IsExcluded(e.FullPath)) return;
            QueueChange();
        }

        private void QueueChange()
        {
            if (Interlocked.Exchange(ref _changePosted, 1) != 0) return;
            Post(delegate { Interlocked.Exchange(ref _changePosted, 0); MarkDirty(); });
        }

        private void MarkDirty()
        {
            _refreshNeeded = true;
            if (!_started || _stopped) return;
            // One trailing refresh absorbs event bursts; typing never restarts the directory crawl.
            if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
            UpdateStatus();
        }

        private void Post(Action action)
        {
            if (_closed || Dispatcher.HasShutdownStarted) return;
            try { Dispatcher.BeginInvoke(new Action(delegate { if (!_closed) action(); }), DispatcherPriority.Background); }
            catch (InvalidOperationException) { }
        }

        private async void EnsureRefresh()
        {
            if (_closed || !_initialized || _stopped || _scanRunning || !_refreshNeeded) return;
            if (!_started && String.IsNullOrWhiteSpace(_query.Text)) return;
            _started = true; _scanRunning = true; _refreshNeeded = false; _error = "";
            _scanStatus = "Updating index...";
            if (_watcher == null || _watcherWarning.Length != 0) InstallWatcher();
            var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _scanRequest = request;
            UpdateStatus();
            try
            {
                SearchSnapshot snapshot = await Task.Run(() => _index.RefreshAsync(RootFolder, progress => Post(delegate
                {
                    if (_scanRequest != request || request.IsCancellationRequested) return;
                    _scanStatus = "Updating index: " + progress.Folders.ToString("N0") + " folders, " + progress.Items.ToString("N0") + " entries";
                    UpdateStatus();
                }), request.Token), request.Token);
                if (_closed || request.IsCancellationRequested) return;
                _snapshot = snapshot; RunQuery();
            }
            catch (OperationCanceledException) { _refreshNeeded = true; }
            catch (Exception ex)
            {
                if (!_closed)
                {
                    _error = "Refresh failed: " + ex.Message;
                    // Explicit Refresh retries failed locations; an unavailable root is not presented as live data.
                    _refreshNeeded = false;
                }
            }
            finally
            {
                _scanRunning = false; _scanRequest = null; request.Dispose();
                if (!_closed)
                {
                    UpdateStatus();
                    if (_refreshNeeded && !_stopped) _refreshTimer.Start();
                }
            }
        }

        private void CancelQuery()
        {
            _queryVersion++;
            if (_queryRequest != null) _queryRequest.Cancel();
            _queryRunning = false;
        }

        private async void RunQuery()
        {
            if (_closed) return;
            CancelQuery();
            int version = _queryVersion;
            string term = _query.Text.Trim();
            if (term.Length == 0 || _snapshot == null)
            {
                _results = new List<ImageFileItem>(); _grid.SetItems(_results); UpdateSelection(); UpdateStatus(); return;
            }
            var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _queryRequest = request;
            _queryRunning = true; UpdateStatus();
            try
            {
                List<ImageFileItem> matches = await _index.QueryAsync(_snapshot, term,
                    (BrowserSortField)_sort.SelectedItem, _descending.IsChecked == true, request.Token);
                if (_closed || version != _queryVersion || request.IsCancellationRequested) return;
                _results = matches; _grid.SetItems(matches); UpdateSelection();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!_closed && version == _queryVersion) _error = "Search failed: " + ex.Message; }
            finally
            {
                if (_queryRequest == request) _queryRequest = null;
                request.Dispose();
                if (!_closed && version == _queryVersion) { _queryRunning = false; UpdateStatus(); }
            }
        }

        private void Stop()
        {
            _stopped = true; _queryTimer.Stop(); _refreshTimer.Stop(); CancelQuery();
            if (_scanRequest != null) _scanRequest.Cancel();
            UpdateStatus();
        }

        private void UpdateSelection()
        {
            ImageFileItem item = _grid.SelectedItem;
            _selection.Text = item == null ? "" : (_results.IndexOf(item) + 1).ToString("N0") + " / " + _results.Count.ToString("N0") + "  |  " + item.Path;
            _selection.ToolTip = item == null ? null : item.Path;
        }

        private void UpdateStatus()
        {
            string state = _stopped ? "Stopped. Previous results retained." : _scanRunning ? _scanStatus
                : _snapshot == null ? "Index not built." : "Indexed " + _snapshot.Items.Length.ToString("N0") + " entries; updated " + _snapshot.UpdatedUtc.ToLocalTime().ToString("g") + ".";
            bool cached = _snapshot != null && (_snapshot.FromCache || _scanRunning || _refreshNeeded || _error.Length != 0);
            _status.Text = _results.Count.ToString("N0") + (cached ? " cached matches. " : " matches. ") + state;
            if (_queryRunning) _status.Text += " Searching...";
            if (_snapshot != null && (_snapshot.SkippedFolders > 0 || _snapshot.SkippedLinks > 0))
                _status.Text += " Skipped: " + _snapshot.SkippedFolders + " unavailable entries, " + _snapshot.SkippedLinks + " links.";
            if (_snapshot != null && !String.IsNullOrEmpty(_snapshot.CacheWarning)) _status.Text += " " + _snapshot.CacheWarning;
            if (_error.Length > 0) _status.Text += " " + _error;
            if (_watcherWarning.Length > 0) _status.Text += " " + _watcherWarning;
            _stop.IsEnabled = !_stopped && (_scanRunning || _queryRunning || _refreshTimer.IsEnabled || _queryTimer.IsEnabled);
            _empty.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _empty.Text = String.IsNullOrWhiteSpace(_query.Text) ? "Search images and folders by name"
                : _stopped ? "Search stopped" : _error.Length > 0 ? "Search unavailable. See status below."
                : _scanRunning || _queryRunning || !_initialized ? "Searching..." : "No matching images or folders";
        }

        private string RelativeParent(ImageFileItem item)
        {
            string parent = Path.GetDirectoryName(item.Path);
            return String.Equals(parent, RootFolder, StringComparison.OrdinalIgnoreCase) ? "." : parent.Substring(RootFolder.TrimEnd('\\').Length + 1);
        }

        private ContextMenu ResultMenu(ImageFileItem item)
        {
            var menu = new ContextMenu();
            ThemeManager.Bind(menu, Control.BackgroundProperty, ThemeKeys.ControlBackground);
            ThemeManager.Bind(menu, Control.ForegroundProperty, ThemeKeys.Text);
            var open = new MenuItem { Header = "Open" }; open.Click += delegate { OpenResult(item, false); }; menu.Items.Add(open);
            var parent = new MenuItem { Header = "Open containing folder" }; parent.Click += delegate { OpenResult(item, true); }; menu.Items.Add(parent);
            return menu;
        }

        private void OpenResult(ImageFileItem item, bool containingFolder)
        {
            if (item == null || _closed) return;
            string path = containingFolder ? Path.GetDirectoryName(item.Path) : item.Path;
            if (containingFolder || item.IsDirectory ? Directory.Exists(path) : File.Exists(path))
            {
                _open(item, containingFolder); Close();
            }
            else
            {
                _results.RemoveAll(result => String.Equals(result.Path, item.Path, StringComparison.OrdinalIgnoreCase));
                _grid.SetItems(_results); UpdateSelection();
                _error = "This result is no longer available. Refresh the index."; UpdateStatus();
            }
        }

        private void SearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); return; }
            if (_query.IsKeyboardFocusWithin)
            {
                if (e.Key == Key.Enter) { CommitSearchHistory(true); _queryTimer.Stop(); _stopped = false; RunQuery(); EnsureRefresh(); e.Handled = true; }
                else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None && _results.Count > 0) { _grid.Focus(); _grid.HandleNavigationKey(Key.Right, ModifierKeys.None); e.Handled = true; }
                return;
            }
            if (!_grid.IsKeyboardFocusWithin) return;
            if (e.Key == Key.Enter && !e.IsRepeat) { OpenResult(_grid.SelectedItem, false); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { _grid.SelectAll(); e.Handled = true; }
            else e.Handled = _grid.HandleNavigationKey(e.Key, Keyboard.Modifiers);
        }

        private static TextBlock Label(string text, double size)
        {
            var label = new TextBlock { Text = text, FontSize = size };
            ThemeManager.Bind(label, TextBlock.ForegroundProperty, ThemeKeys.Text); return label;
        }

        internal static Button IconButton(string glyph, string name, Action click)
        {
            var button = new Button { Content = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16,
                Width = 32, Height = 32, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), ToolTip = name };
            button.Template = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
    <Border Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='2'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/>
    </Border>
    <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='{DynamicResource Theme.ControlHover}'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='Frame' Property='Background' Value='{DynamicResource Theme.ControlPressed}'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
    </ControlTemplate.Triggers>
</ControlTemplate>");
            AutomationProperties.SetName(button, name); button.Click += delegate { click(); }; return button;
        }

        private static Slider AddSlider(Panel parent, string label, double min, double max, double value, double step)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Height = 32, Margin = new Thickness(0, 0, 16, 6) };
            var text = Label(label, 12); text.VerticalAlignment = VerticalAlignment.Center; text.Margin = new Thickness(0, 0, 8, 0); group.Children.Add(text);
            var slider = new Slider { Width = 120, Minimum = min, Maximum = max, Value = value, TickFrequency = step,
                IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, AutoToolTipPlacement = AutoToolTipPlacement.TopLeft };
            AutomationProperties.SetName(slider, "Search thumbnail " + label.ToLowerInvariant()); group.Children.Add(slider); parent.Children.Add(group); return slider;
        }
    }
}
