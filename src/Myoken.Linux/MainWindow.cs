using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Myoken.Core;

namespace Myoken.Linux;

internal sealed class MainWindow : Window
{
    private const int PageSize = 72;
    private readonly TextBox _path = new() { Watermark = "Folder path", MinWidth = 240 };
    private readonly TextBlock _status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ListBox _folders = new();
    private readonly WrapPanel _thumbnails = new() { Orientation = Orientation.Horizontal };
    private readonly TabControl _tabs = new();
    private readonly ObservableCollection<TabItem> _tabItems = new();
    private readonly TabItem _browser = new() { Header = "Browser" };
    private readonly List<Bitmap> _pageImages = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private CancellationTokenSource? _browseCts, _pageCts, _imageCts;
    private SessionStore? _sessions;
    private string[] _files = Array.Empty<string>();
    private string _folder = string.Empty;
    private string? _persistenceWarning;
    private int _page;
    private bool _starting = true, _closed, _dirty;

    public MainWindow()
    {
        Title = "Myoken Ubuntu/GNOME - Linux preview";
        Width = 1200; Height = 820; MinWidth = 720; MinHeight = 480;
        var root = new DockPanel();
        var toolbar = new DockPanel { Margin = new Thickness(8), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(Button("Home", async () => await NavigateAsync(Home)));
        actions.Children.Add(Button("Up", async () => await NavigateAsync(Directory.GetParent(_folder)?.FullName ?? _folder)));
        actions.Children.Add(Button("Choose folder", PickFolderAsync));
        actions.Children.Add(Button("Go", async () => await NavigateAsync(_path.Text ?? string.Empty)));
        DockPanel.SetDock(actions, Dock.Left);
        toolbar.Children.Add(actions);
        _path.Margin = new Thickness(8, 0, 0, 0);
        toolbar.Children.Add(_path);
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);

        var browserGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,6,*") };
        _folders.ItemTemplate = new FuncDataTemplate<string>((value, _) => new TextBlock
        {
            Text = "Folder: " + Path.GetFileName(Path.TrimEndingDirectorySeparator(value ?? string.Empty)),
            Margin = new Thickness(6), TextTrimming = TextTrimming.CharacterEllipsis
        });
        browserGrid.Children.Add(_folders);
        var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(splitter, 1); browserGrid.Children.Add(splitter);
        var images = new DockPanel();
        var pager = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8) };
        pager.Children.Add(Button("Previous page", async () => { _page = Math.Max(0, _page - 1); await RenderPageAsync(); }));
        pager.Children.Add(_pageLabel);
        pager.Children.Add(Button("Next page", async () => { _page = Math.Min(Math.Max(0, (_files.Length - 1) / PageSize), _page + 1); await RenderPageAsync(); }));
        DockPanel.SetDock(pager, Dock.Bottom); images.Children.Add(pager);
        images.Children.Add(new ScrollViewer
        {
            Content = _thumbnails,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        });
        Grid.SetColumn(images, 2); browserGrid.Children.Add(images);
        _browser.Content = browserGrid;
        _tabItems.Add(_browser); _tabs.ItemsSource = _tabItems; _tabs.SelectedItem = _browser;
        root.Children.Add(_tabs); Content = root;

        _path.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await NavigateAsync(_path.Text ?? string.Empty); }
        };
        _folders.SelectionChanged += async (_, _) =>
        {
            if (_folders.SelectedItem is string folder) await NavigateAsync(folder);
        };
        _tabs.SelectionChanged += async (_, e) =>
        {
            if (ReferenceEquals(e.Source, _tabs) && !_starting)
            { _dirty = true; await ShowSelectedImageAsync(); }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F11)
            { WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen; e.Handled = true; }
            else if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _tabs.SelectedItem is TabItem tab && tab != _browser)
            { CloseTab(tab); e.Handled = true; }
            else if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            { _path.Focus(); _path.SelectAll(); e.Handled = true; }
        };
        Opened += async (_, _) => await StartAsync();
        _saveTimer.Tick += (_, _) => SaveSession();
        Closed += (_, _) =>
        {
            _closed = true; _saveTimer.Stop(); SaveSession();
            Cancel(ref _browseCts); Cancel(ref _pageCts); Cancel(ref _imageCts);
            ClearThumbnails(); ClearPreviews(); _sessions?.Dispose();
        };
    }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private Button Button(string text, Func<Task> action)
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
            _tabs.SelectedItem = _tabItems.FirstOrDefault(t => t.Tag is string p && StringComparer.Ordinal.Equals(p, state.ActiveImagePath)) ?? _browser;
            _starting = false;
            await ShowSelectedImageAsync();
            _dirty = true; _saveTimer.Start();
            if (Program.SmokeTest)
            {
                if (_files.Length == 0) throw new InvalidOperationException("Smoke test requires an image fixture.");
                _starting = true;
                _tabs.SelectedItem = AddImageTab(_files[0]);
                _starting = false;
                await ShowSelectedImageAsync();
                if ((_tabs.SelectedItem as TabItem)?.Content is not Image { Source: not null })
                    throw new InvalidOperationException("Image preview did not load.");
                SaveSession();
                Console.WriteLine("PASS: Linux window, folder scan, thumbnail page and image tab launched.");
                ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(0);
            }
        }
        catch (Exception ex)
        {
            _starting = false; Status(ex.Message); Console.Error.WriteLine(ex);
            if (Program.SmokeTest)
                ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(1);
        }
    }

    private async Task PickFolderAsync()
    {
        var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "Choose an image folder", AllowMultiple = false });
        if (selected.FirstOrDefault()?.TryGetLocalPath() is string path) await NavigateAsync(path);
    }

    private async Task NavigateAsync(string path)
    {
        if (_closed) return;
        Cancel(ref _browseCts); Cancel(ref _pageCts);
        _browseCts = new CancellationTokenSource();
        var token = _browseCts.Token;
        try
        {
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Folder is unavailable: " + full);
            Status("Reading " + full);
            var result = await Task.Run(() =>
            {
                var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = 0 };
                var directories = new List<string>(); var files = new List<string>();
                foreach (var entry in Directory.EnumerateDirectories(full, "*", options))
                { token.ThrowIfCancellationRequested(); directories.Add(entry); }
                foreach (var entry in Directory.EnumerateFiles(full, "*", options))
                { token.ThrowIfCancellationRequested(); if (ImageDecoder.IsSupported(entry)) files.Add(entry); }
                directories.Sort((a, b) => NaturalNameComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
                files.Sort((a, b) => NaturalNameComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
                return (Directories: directories.ToArray(), Files: files.ToArray());
            }, token);
            if (token.IsCancellationRequested || _closed) return;
            _folder = full; _path.Text = full; _files = result.Files; _page = 0;
            _folders.SelectedItem = null; _folders.ItemsSource = result.Directories;
            _tabs.SelectedItem = _browser; _dirty = true;
            await RenderPageAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested && !_closed) Status(ex.Message); }
    }

    private async Task RenderPageAsync()
    {
        if (_closed) return;
        Cancel(ref _pageCts); _pageCts = new CancellationTokenSource();
        var token = _pageCts.Token;
        ClearThumbnails();
        _pageLabel.Text = $"Page {_page + 1} / {Math.Max(1, (_files.Length + PageSize - 1) / PageSize)}";
        Status($"{_files.Length:N0} images in {_folder}");
        var tasks = new List<Task>();
        foreach (var path in _files.Skip(_page * PageSize).Take(PageSize))
        {
            var image = new Image { Width = 148, Height = 112, Stretch = Stretch.Uniform };
            var caption = new TextBlock { Text = Path.GetFileName(path), MaxWidth = 148, TextTrimming = TextTrimming.CharacterEllipsis };
            var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(image); panel.Children.Add(caption);
            var button = new Button { Content = panel, Margin = new Thickness(4), Padding = new Thickness(6) };
            ToolTip.SetTip(button, path);
            button.Click += (_, _) => { _tabs.SelectedItem = AddImageTab(path); _dirty = true; };
            _thumbnails.Children.Add(button);
            tasks.Add(FillThumbnailAsync(path, image, caption, token));
        }
        await Task.WhenAll(tasks);
    }

    private async Task FillThumbnailAsync(string path, Image image, TextBlock caption, CancellationToken token)
    {
        try
        {
            var bitmap = await ImageDecoder.LoadAsync(path, 256, token);
            if (token.IsCancellationRequested || _closed) { bitmap.Dispose(); return; }
            image.Source = bitmap; _pageImages.Add(bitmap);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !_closed)
            { caption.Text = "Unavailable: " + Path.GetFileName(path); ToolTip.SetTip(caption, ex.Message); }
        }
    }

    private TabItem AddImageTab(string path)
    {
        path = Path.GetFullPath(path);
        var existing = _tabItems.FirstOrDefault(t => t.Tag is string p && StringComparer.Ordinal.Equals(p, path));
        if (existing != null) return existing;
        var tab = new TabItem { Tag = path, Content = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(8) } };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock { Text = Path.GetFileName(path), MaxWidth = 190, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "×", Padding = new Thickness(5, 0) };
        close.Click += (_, e) => { e.Handled = true; CloseTab(tab); };
        header.Children.Add(close); tab.Header = header; ToolTip.SetTip(tab, path);
        _tabItems.Add(tab); return tab;
    }

    private async Task ShowSelectedImageAsync()
    {
        Cancel(ref _imageCts); ClearPreviews();
        if (_closed || _tabs.SelectedItem is not TabItem { Tag: string path, Content: Image image }) return;
        _imageCts = new CancellationTokenSource(); var token = _imageCts.Token;
        Status("Opening " + path);
        try
        {
            var bitmap = await ImageDecoder.LoadAsync(path, 4096, token);
            if (token.IsCancellationRequested || _closed) { bitmap.Dispose(); return; }
            image.Source = bitmap;
            Status($"{Path.GetFileName(path)} | fit preview {bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested && !_closed) Status("Cannot open image: " + ex.Message); }
    }

    private void CloseTab(TabItem tab)
    {
        if (tab == _browser) return;
        if (tab.Content is Image image && image.Source is Bitmap bitmap)
        { image.Source = null; bitmap.Dispose(); }
        _tabItems.Remove(tab); _dirty = true;
        if (_tabs.SelectedItem == null) _tabs.SelectedItem = _browser;
    }

    private void ClearPreviews()
    {
        foreach (var tab in _tabItems)
            if (tab.Content is Image image && image.Source is Bitmap bitmap)
            { image.Source = null; bitmap.Dispose(); }
    }

    private void ClearThumbnails()
    {
        _thumbnails.Children.Clear();
        foreach (var bitmap in _pageImages) bitmap.Dispose();
        _pageImages.Clear();
    }

    private void SaveSession()
    {
        if (!_dirty || _starting || _sessions?.CanWrite != true) return;
        try
        {
            _sessions.Save(new SessionState
            {
                FolderPath = _folder,
                ImagePaths = _tabItems.Where(t => t.Tag is string).Select(t => (string)t.Tag!).ToList(),
                ActiveImagePath = (_tabs.SelectedItem as TabItem)?.Tag as string
            });
            _dirty = false;
        }
        catch (Exception ex) { _persistenceWarning = "Session save failed: " + ex.Message; Status(_persistenceWarning); }
    }

    private void Status(string text) => _status.Text = string.IsNullOrEmpty(_persistenceWarning) ? text : text + " | " + _persistenceWarning;

    private static void Cancel(ref CancellationTokenSource? source)
    {
        source?.Cancel(); source?.Dispose(); source = null;
    }
}
