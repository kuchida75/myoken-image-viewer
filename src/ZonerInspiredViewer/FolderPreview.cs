using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed class FolderPreview : Grid
    {
        private readonly ThumbnailCacheService _cache;
        private readonly int _previewSize;
        private readonly Image[] _images = new Image[4];
        private readonly DispatcherTimer _refreshTimer;
        private string _folder;
        private CancellationTokenSource _request;
        private FileSystemWatcher _watcher;

        public FolderPreview(ThumbnailCacheService cache, int thumbnailSize)
        {
            _cache = cache;
            _previewSize = Math.Max(64, (thumbnailSize + 1) / 2);
            Margin = new Thickness(3);
            IsHitTestVisible = false;
            var tab = new Border { Width = thumbnailSize * 0.38, Height = 13,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 0, 0), CornerRadius = new CornerRadius(3, 3, 0, 0),
                BorderThickness = new Thickness(1) };
            ThemeManager.Bind(tab, Border.BackgroundProperty, "Folder.Fill");
            ThemeManager.Bind(tab, Border.BorderBrushProperty, "Folder.Border");
            Children.Add(tab);
            var body = new Border { Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(5),
                CornerRadius = new CornerRadius(0, 3, 3, 3), BorderThickness = new Thickness(1) };
            ThemeManager.Bind(body, Border.BackgroundProperty, "Folder.Fill");
            ThemeManager.Bind(body, Border.BorderBrushProperty, "Folder.Border");
            var mosaic = new Grid();
            for (int i = 0; i < 2; i++)
            {
                mosaic.ColumnDefinitions.Add(new ColumnDefinition());
                mosaic.RowDefinitions.Add(new RowDefinition());
            }
            for (int i = 0; i < _images.Length; i++)
            {
                var slot = new Border { Margin = new Thickness(1), ClipToBounds = true };
                ThemeManager.Bind(slot, Border.BackgroundProperty, "Folder.PreviewBackground");
                _images[i] = new Image { Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(_images[i], BitmapScalingMode.HighQuality);
                slot.Child = _images[i];
                Grid.SetRow(slot, i / 2);
                Grid.SetColumn(slot, i % 2);
                mosaic.Children.Add(slot);
            }
            body.Child = mosaic;
            Children.Add(body);
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _refreshTimer.Tick += delegate { _refreshTimer.Stop(); LoadPreview(); };
            Loaded += delegate { Start(); };
            IsVisibleChanged += delegate { if (IsVisible) Start(); else Release(); };
            Unloaded += delegate { Release(); };
        }

        public void SetFolder(string folder)
        {
            if (String.Equals(_folder, folder, StringComparison.OrdinalIgnoreCase)) { Start(); return; }
            Release();
            _folder = folder;
            Start();
        }

        public void Release()
        {
            _refreshTimer.Stop();
            if (_request != null) _request.Cancel();
            _request = null;
            if (_watcher != null) { _watcher.Dispose(); _watcher = null; }
            foreach (Image image in _images) image.Source = null;
        }

        private void Start()
        {
            if (!IsLoaded || !IsVisible || _folder == null || _watcher != null) return;
            try
            {
                _watcher = new FileSystemWatcher(_folder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false
                };
                _watcher.Created += FolderChanged;
                _watcher.Changed += FolderChanged;
                _watcher.Deleted += FolderChanged;
                _watcher.Renamed += FolderChanged;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception)
            {
                if (_watcher != null) { _watcher.Dispose(); _watcher = null; }
            }
            LoadPreview();
        }

        private void FolderChanged(object sender, FileSystemEventArgs e)
        {
            var renamed = e as RenamedEventArgs;
            if (!ImageExtensions.IsBrowsableImage(e.FullPath)
                && (renamed == null || !ImageExtensions.IsBrowsableImage(renamed.OldFullPath))) return;
            if (Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!Object.ReferenceEquals(sender, _watcher)) return;
                _refreshTimer.Stop();
                _refreshTimer.Start();
            }));
        }

        private async void LoadPreview()
        {
            if (_folder == null || !IsVisible) return;
            if (_request != null) _request.Cancel();
            var request = new CancellationTokenSource();
            _request = request;
            BitmapSource[] bitmaps = new BitmapSource[0];
            try
            {
                bitmaps = await _cache.GetFolderThumbnailsAsync(_folder, _previewSize, request.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            if (!Dispatcher.HasShutdownStarted)
            {
                await Dispatcher.InvokeAsync(new Action(delegate
                {
                    if (_request != request) return;
                    if (!request.IsCancellationRequested && IsVisible)
                        for (int i = 0; i < _images.Length; i++)
                            _images[i].Source = i < bitmaps.Length ? bitmaps[i] : null;
                    _request = null;
                }));
            }
            request.Dispose();
        }
    }
}
