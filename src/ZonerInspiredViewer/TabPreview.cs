using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed class TabPreview : ToolTip
    {
        private readonly string _path;
        private readonly ThumbnailCacheService _cache;
        private readonly Image _image;
        private readonly TextBlock _message;
        private readonly FolderPreview _folderPreview;
        private CancellationTokenSource _request;

        public TabPreview(string path, ThumbnailCacheService cache, bool isFolder = false)
        {
            _path = path;
            _cache = cache;
            Placement = PlacementMode.Bottom;
            Padding = new Thickness(10);
            ThemeManager.Bind(this, BackgroundProperty, ThemeKeys.PaneBackground);
            ThemeManager.Bind(this, ForegroundProperty, ThemeKeys.Text);
            ThemeManager.Bind(this, BorderBrushProperty, ThemeKeys.Border);
            var panel = new StackPanel { Width = 256 };
            var frame = new Grid { Height = 160 };
            ThemeManager.Bind(frame, Panel.BackgroundProperty, ThemeKeys.TileImageBackground);
            _image = new Image { Stretch = Stretch.Uniform };
            _message = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeManager.Bind(_message, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            frame.Children.Add(_image);
            frame.Children.Add(_message);
            if (isFolder)
            {
                _image.Visibility = Visibility.Collapsed;
                _message.Visibility = Visibility.Collapsed;
                _folderPreview = new FolderPreview(cache, 256);
                _folderPreview.SetFolder(path);
                frame.Children.Add(_folderPreview);
            }
            panel.Children.Add(frame);
            panel.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path), FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 4)
            });
            var folder = new TextBlock
            {
                Text = Path.GetDirectoryName(path), TextTrimming = TextTrimming.CharacterEllipsis
            };
            ThemeManager.Bind(folder, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            panel.Children.Add(folder);
            Content = panel;
            Opened += LoadPreview;
            Closed += delegate
            {
                if (_request != null) _request.Cancel();
                _request = null;
                _image.Source = null;
                if (_folderPreview != null) _folderPreview.Release();
            };
        }

        private async void LoadPreview(object sender, RoutedEventArgs e)
        {
            if (_folderPreview != null) return;
            if (_request != null) _request.Cancel();
            var request = new CancellationTokenSource();
            _request = request;
            _message.Text = "Loading...";
            BitmapSource bitmap = null;
            try
            {
                bitmap = await _cache.GetThumbnailAsync(ImageFileItem.FromPath(_path), 256, request.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            if (Dispatcher.HasShutdownStarted)
            {
                request.Dispose();
                return;
            }
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                if (_request == request)
                {
                    if (IsOpen && !request.IsCancellationRequested)
                    {
                        _image.Source = bitmap;
                        _message.Text = bitmap == null ? "Preview unavailable" : "";
                    }
                    _request = null;
                }
                request.Dispose();
            }));
        }
    }
}
