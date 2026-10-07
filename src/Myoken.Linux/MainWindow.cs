using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Myoken.Core;

namespace Myoken.Linux;

internal sealed partial class MainWindow : Window
{
    private readonly TextBox _path = new() { PlaceholderText = "Folder path", MinWidth = 160 };
    private readonly TextBlock _status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private readonly FolderTree _folders = new();
    private readonly VirtualThumbnailBrowser _thumbnails = new();
    private readonly PreviewCache _previewCache = new();
    private readonly PreviewPreloader _preloader;
    private readonly DocumentTabs _tabs = new();
    private readonly ObservableCollection<DocumentTab> _tabItems = new();
    private readonly DocumentTab _browser;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private CancellationTokenSource? _browseCts;
    private SessionStore? _sessions;
    private string[] _files = Array.Empty<string>();
    private string _folder = string.Empty;
    private string _browserStatus = "Choose an image folder.";
    private string? _persistenceWarning;
    private bool _starting = true, _closed, _dirty, _preloadTestEnabled;

    public MainWindow()
    {
        Title = "Myoken Ubuntu/GNOME - Linux preview L003b";
        _preloader = new PreviewPreloader(_previewCache);
        Width = 1200; Height = 820; MinWidth = 820; MinHeight = 480;
        var root = new DockPanel();
        var toolbar = new DockPanel { Margin = new Thickness(8), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(ActionButton("Home", async () => await NavigateAsync(Home)));
        actions.Children.Add(ActionButton("Up", async () => await NavigateAsync(Directory.GetParent(_folder)?.FullName ?? _folder)));
        actions.Children.Add(ActionButton("Choose folder", PickFolderAsync));
        actions.Children.Add(ActionButton("Refresh", RefreshAsync));
        actions.Children.Add(ActionButton("Go", async () => await NavigateAsync(_path.Text ?? string.Empty)));
        DockPanel.SetDock(actions, Dock.Left); toolbar.Children.Add(actions);
        _path.Margin = new Thickness(8, 0, 0, 0); toolbar.Children.Add(_path);
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);

        var browserGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("250,6,*") };
        browserGrid.Children.Add(_folders);
        var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(splitter, 1); browserGrid.Children.Add(splitter);
        Grid.SetColumn(_thumbnails, 2); browserGrid.Children.Add(_thumbnails);
        _browser = new DocumentTab { Content = browserGrid };
        _tabItems.Add(_browser); _tabs.Bind(_tabItems, _browser);
        root.Children.Add(_tabs); Content = root;

        _path.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await NavigateAsync(_path.Text ?? string.Empty); }
        };
        _folders.FolderSelected += path => { _ = NavigateAsync(path); };
        _thumbnails.ImageActivated += path => { _tabs.SelectedItem = AddImageTab(path); _dirty = true; };
        _tabs.SelectionChanged += async (_, _) =>
        {
            if (!_starting && !_closed) { _dirty = true; await ShowSelectedImageAsync(); }
        };
        _tabs.CloseRequested += CloseTab;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            var modifiers = e.KeyModifiers;
            if (!modifiers.HasFlag(KeyModifiers.Control)
                || modifiers.HasFlag(KeyModifiers.Alt) || modifiers.HasFlag(KeyModifiers.Meta)) return;
            if (e.Key == Key.Tab)
            {
                e.Handled = true; _tabs.Cycle(modifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            }
            else if (e.Key == Key.W && !modifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                if (_tabs.SelectedItem is DocumentTab tab && tab != _browser)
                { CloseTab(tab); _tabs.FocusSelectedHeader(); }
            }
        }, RoutingStrategies.Tunnel);
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.F11)
            { WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen; e.Handled = true; }
            else if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            { _path.Focus(); _path.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.F5)
            {
                e.Handled = true;
                try { await RefreshAsync(); } catch (Exception ex) { Status(ex.Message); }
            }
        };
        Opened += async (_, _) => await StartAsync();
        _saveTimer.Tick += (_, _) => SaveSession();
        Closed += (_, _) =>
        {
            _closed = true; _saveTimer.Stop(); SaveSession(); Cancel(ref _browseCts);
            _preloader.Dispose(); _tabs.Dispose(); _thumbnails.Dispose(); _folders.Dispose();
            foreach (var tab in _tabItems) (tab.Content as ImageViewer)?.Dispose();
            _previewCache.Dispose(); _sessions?.Dispose();
        };
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text };
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception ex) { Status(ex.Message); }
        };
        return button;
    }

    private async Task StartAsync()
    {
        try
        {
            SessionState state = new();
            try
            {
                _sessions = new SessionStore(); state = _sessions.Load();
                _persistenceWarning = _sessions.Warning;
            }
            catch (Exception ex) { _persistenceWarning = "Session persistence unavailable: " + ex.Message; }
            foreach (var path in state.ImagePaths)
                if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && ImageDecoder.IsSupported(path))
                    AddImageTab(path);
            var start = Program.StartPath ?? state.FolderPath;
            if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
                start = Directory.Exists(Path.Combine(Home, "Pictures")) ? Path.Combine(Home, "Pictures") : Home;
            await NavigateAsync(start);
            _tabs.SelectedItem = _tabItems.FirstOrDefault(t => t.Path != null && StringComparer.Ordinal.Equals(t.Path, state.ActiveImagePath)) ?? _browser;
            _starting = false;
            await ShowSelectedImageAsync();
            _dirty = true; _saveTimer.Start();
            if (Program.TestMode != null)
            {
                _saveTimer.Stop();
                if (Program.TestMode == "--browser-test")
                {
                    await RunThumbnailCacheChecksAsync();
                    await RunPreviewCacheChecksAsync();
                    await RunPreviewPreloadChecksAsync();
                    await RunOrientationAndMetadataChecksAsync(_folder);
                    await RunColorManagementChecksAsync(_folder);
                }
                await RunUiChecksAsync(Program.TestMode);
                ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(0);
            }
        }
        catch (Exception ex)
        {
            _starting = false; Status(ex.Message); Console.Error.WriteLine(ex);
            if (Program.TestMode != null)
                ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(1);
        }
    }

    private async Task PickFolderAsync()
    {
        var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "Choose an image folder", AllowMultiple = false });
        if (selected.FirstOrDefault()?.TryGetLocalPath() is string path) await NavigateAsync(path);
    }

    private async Task RefreshAsync()
    {
        _preloader.Cancel();
        _thumbnails.InvalidateCache();
        _previewCache.Clear();
        foreach (var tab in _tabItems)
            (tab.Content as ImageViewer)?.InvalidateMetadata();
        await _folders.RefreshSelectedAsync();
        await NavigateAsync(string.IsNullOrEmpty(_folder) ? Home : _folder);
    }

    private async Task NavigateAsync(string path)
    {
        if (_closed) return;
        Cancel(ref _browseCts); _browseCts = new CancellationTokenSource();
        var token = _browseCts.Token;
        try
        {
            var full = DirectoryCatalog.Normalize(path);
            _browserStatus = "Reading " + full;
            _tabs.SelectedItem = _browser; _thumbnails.SetActive(true); UpdateBrowserStatus();
            var files = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var options = new EnumerationOptions { IgnoreInaccessible = false, RecurseSubdirectories = false, AttributesToSkip = 0 };
                var result = new List<string>();
                foreach (var entry in Directory.EnumerateFiles(full, "*", options))
                { token.ThrowIfCancellationRequested(); if (ImageDecoder.IsSupported(entry)) result.Add(entry); }
                result.Sort((a, b) => NaturalNameComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
                token.ThrowIfCancellationRequested(); return result.ToArray();
            }, token);
            if (token.IsCancellationRequested || _closed) return;
            _folder = full; _path.Text = full; _files = files;
            _browserStatus = $"{_files.Length:N0} images in {_folder}";
            _thumbnails.SetFiles(_files); _dirty = true; UpdateBrowserStatus();
            await _folders.RevealAsync(full, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !_closed)
            {
                _browserStatus = "Cannot browse folder: " + ex.Message;
                _path.Text = _folder; UpdateBrowserStatus();
            }
        }
    }

    private DocumentTab AddImageTab(string path)
    {
        path = Path.GetFullPath(path);
        var existing = _tabItems.FirstOrDefault(t => StringComparer.Ordinal.Equals(t.Path, path));
        if (existing != null) return existing;
        var viewer = new ImageViewer(path, _previewCache);
        var tab = new DocumentTab { Path = path, Content = viewer };
        viewer.StatusChanged += () =>
        {
            if (!_closed && ReferenceEquals(_tabs.SelectedItem, tab)) Status(viewer.StatusText);
        };
        _tabItems.Add(tab); return tab;
    }

    private async Task ShowSelectedImageAsync()
    {
        _preloader.Cancel();
        var selected = _tabs.SelectedItem;
        foreach (var tab in _tabItems)
            if (tab != selected) (tab.Content as ImageViewer)?.Suspend();
        _thumbnails.SetActive(ReferenceEquals(selected, _browser));
        if (_closed) return;
        if (ReferenceEquals(selected, _browser)) { UpdateBrowserStatus(); return; }
        if (selected?.Content is not ImageViewer viewer) return;
        await viewer.ActivateAsync();
        if (!_closed && ReferenceEquals(_tabs.SelectedItem, selected))
        {
            Status(viewer.StatusText);
            SchedulePreloadsForSelection();
        }
    }

    private void CloseTab(DocumentTab tab)
    {
        if (tab == _browser || !_tabItems.Contains(tab)) return;
        (tab.Content as ImageViewer)?.Dispose();
        _tabItems.Remove(tab); _dirty = true;
        if (_tabs.SelectedItem == null) _tabs.SelectedItem = _browser;
        else if (!_closed && ReferenceEquals(_tabs.SelectedItem, _browser) == false) SchedulePreloadsForSelection();
    }

    private void SchedulePreloadsForSelection()
    {
        if (_closed || (Program.TestMode != null && !_preloadTestEnabled)) return;
        var selectedPath = _tabs.SelectedItem?.Path;
        if (string.IsNullOrEmpty(selectedPath)) { _preloader.Cancel(); return; }
        var paths = _tabItems.Where(t => t.Path != null).Select(t => t.Path!).ToArray();
        _preloader.Schedule(paths, selectedPath);
    }

    private void SaveSession()
    {
        if (!_dirty || _starting || _sessions?.CanWrite != true) return;
        try
        {
            _sessions.Save(new SessionState
            {
                FolderPath = _folder,
                ImagePaths = _tabItems.Where(t => t.Path != null).Select(t => t.Path!).ToList(),
                ActiveImagePath = _tabs.SelectedItem?.Path
            });
            _dirty = false;
        }
        catch (Exception ex) { _persistenceWarning = "Session save failed: " + ex.Message; Status("Session save failed."); }
    }

    private void UpdateBrowserStatus()
    {
        if (ReferenceEquals(_tabs.SelectedItem, _browser)) Status(_browserStatus);
    }
    private void Status(string text) => _status.Text = string.IsNullOrEmpty(_persistenceWarning) ? text : text + " | " + _persistenceWarning;
    private static void Cancel(ref CancellationTokenSource? source)
    {
        source?.Cancel(); source?.Dispose(); source = null;
    }
}
