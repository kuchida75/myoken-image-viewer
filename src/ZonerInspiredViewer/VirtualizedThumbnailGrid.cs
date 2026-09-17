using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed class ThumbnailActivatedEventArgs : EventArgs
    {
        public ImageFileItem Item { get; set; }
    }

    internal sealed class ThumbnailCommandEventArgs : EventArgs
    {
        public ImageFileItem Item { get; set; }
        public string Command { get; set; }
    }

    internal sealed class VirtualizedThumbnailGrid : ScrollViewer
    {
        public const int MinimumThumbnailSize = 96;
        public const int MaximumThumbnailSize = 320;
        public const int ThumbnailSizeStep = 32;
        public const int DefaultThumbnailSize = 128;

        private readonly Canvas _canvas;
        private readonly Dictionary<int, ThumbnailTile> _realized;
        private readonly ThumbnailCacheService _thumbnailCache;
        private IList<ImageFileItem> _items;
        private ImageFileItem _selectedItem;
        private readonly HashSet<string> _selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _selectionAnchor;
        private Point _dragOrigin;
        private bool _dragCandidate;
        private ImageFileItem _pendingPlainSelection;
        private int _columnCount;
        private int _thumbnailSize;
        private double _aspectRatio = 1;
        private double _tileWidth;
        private double _tileHeight;

        public event EventHandler<ThumbnailActivatedEventArgs> ItemActivated;
        public event EventHandler<ThumbnailActivatedEventArgs> ItemSelected;
        public event EventHandler<ThumbnailCommandEventArgs> CommandRequested;
        public event EventHandler DragRequested;
        public event EventHandler ItemsChanged;

        public int ItemCount { get { return _items.Count; } }
        public int ColumnCount { get { return _columnCount; } }
        public double TileHeight { get { return _tileHeight; } }
        public int ItemsRevision { get; private set; }
        public ImageFileItem ItemAt(int index) { return index >= 0 && index < _items.Count ? _items[index] : null; }

        public Func<string, bool> IsFavoriteFolder { get; set; }
        public Func<ImageFileItem, ContextMenu> ItemMenuFactory { get; set; }
        public Func<ImageFileItem, string> ItemDetails { get; set; }
        public Func<bool> CanPaste { get; set; }
        public int SelectedCount { get { return _selectedPaths.Count; } }
        public IList<string> SelectedPaths { get { return _selectedPaths.ToArray(); } }
        public IList<ImageFileItem> SelectedItems { get { return _items.Where(item => _selectedPaths.Contains(item.Path)).ToArray(); } }

        public VirtualizedThumbnailGrid(ThumbnailCacheService thumbnailCache)
        {
            _thumbnailCache = thumbnailCache;
            _items = new List<ImageFileItem>();
            _realized = new Dictionary<int, ThumbnailTile>();
            _canvas = new Canvas();
            _thumbnailSize = DefaultThumbnailSize;
            _tileWidth = ThumbnailTile.CalculateTileWidth(_thumbnailSize);
            _tileHeight = ThumbnailTile.CalculateTileHeight(_thumbnailSize);

            Content = _canvas;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            ThemeManager.Bind(this, Control.BackgroundProperty, ThemeKeys.WorkspaceBackground);
            ThemeManager.Bind(_canvas, Panel.BackgroundProperty, ThemeKeys.WorkspaceBackground);
            CanContentScroll = false;
            Focusable = true;
            ContextMenu = BuildBackgroundMenu();
            MouseMove += GridMouseMove;
            MouseLeftButtonUp += delegate
            {
                _dragCandidate = false;
                if (_pendingPlainSelection != null) { SelectItem(_pendingPlainSelection); NotifySelection(); }
                _pendingPlainSelection = null;
            };
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.OriginalSource == _canvas || e.OriginalSource == this)
                {
                    Focus(); SelectItem(null); NotifySelection();
                }
            };

            ScrollChanged += delegate { RealizeVisibleTiles(); };
            SizeChanged += delegate
            {
                UpdateExtent();
                RealizeVisibleTiles();
            };
        }

        public ImageFileItem SelectedItem
        {
            get { return _selectedItem; }
        }

        public int ThumbnailSize
        {
            get { return _thumbnailSize; }
        }

        public static int NormalizeThumbnailSize(double value)
        {
            int clamped = Math.Max(MinimumThumbnailSize, Math.Min(MaximumThumbnailSize, (int)Math.Round(value)));
            int steps = (int)Math.Round((clamped - MinimumThumbnailSize) / (double)ThumbnailSizeStep);
            return MinimumThumbnailSize + (steps * ThumbnailSizeStep);
        }

        public void SetThumbnailSize(int thumbnailSize)
        {
            SetThumbnailLayout(thumbnailSize, _aspectRatio);
        }

        public double ThumbnailAspectRatio { get { return _aspectRatio; } }

        public static double NormalizeAspectRatio(double ratio)
        {
            return Double.IsNaN(ratio) || Double.IsInfinity(ratio) || ratio <= 0
                ? 1 : Math.Max(0.5, Math.Min(2, Math.Round(ratio * 4) / 4));
        }

        public void SetThumbnailLayout(int thumbnailSize, double aspectRatio)
        {
            int normalized = NormalizeThumbnailSize(thumbnailSize);
            double ratio = NormalizeAspectRatio(aspectRatio);
            if (normalized == _thumbnailSize && ratio == _aspectRatio)
            {
                return;
            }

            int anchorIndex = 0;
            if (_columnCount > 0 && _tileHeight > 0)
            {
                anchorIndex = Math.Max(0, (int)Math.Floor(VerticalOffset / _tileHeight) * _columnCount);
            }

            _thumbnailSize = normalized;
            _aspectRatio = ratio;
            _tileWidth = ThumbnailTile.CalculateTileWidth(_thumbnailSize);
            _tileHeight = ThumbnailTile.CalculateTileHeight(_thumbnailSize, _aspectRatio);
            ClearRealized();
            UpdateExtent();

            if (_columnCount > 0 && _items.Count > 0)
            {
                anchorIndex = Math.Min(anchorIndex, _items.Count - 1);
                ScrollToVerticalOffset((anchorIndex / _columnCount) * _tileHeight);
            }

            RealizeVisibleTiles();
        }

        public void SetItems(IList<ImageFileItem> items)
        {
            _items = items ?? new List<ImageFileItem>();
            _selectedPaths.IntersectWith(_items.Select(item => item.Path));
            string lead = _selectedItem == null ? null : _selectedItem.Path;
            _selectedItem = _items.FirstOrDefault(item => item.Path == lead && _selectedPaths.Contains(item.Path));
            if (_selectedItem == null) _selectedItem = _items.FirstOrDefault(item => _selectedPaths.Contains(item.Path));
            ClearRealized();
            UpdateExtent();
            RealizeVisibleTiles();
            NotifyItemsChanged();
        }

        private void NotifyItemsChanged()
        {
            ItemsRevision++;
            if (ItemsChanged != null) ItemsChanged(this, EventArgs.Empty);
        }

        public void RefreshTiles()
        {
            UpdateExtent();
            RealizeVisibleTiles();
            NotifyItemsChanged();
        }

        public void SelectItem(ImageFileItem item)
        {
            _selectedPaths.Clear();
            if (item != null) _selectedPaths.Add(item.Path);
            _selectedItem = item;
            _selectionAnchor = item == null ? null : item.Path;
            PaintSelection();
        }

        public void RestoreSelection(IEnumerable<string> paths, string lead)
        {
            _selectedPaths.Clear();
            _selectedPaths.UnionWith(paths ?? new string[0]);
            if (lead != null) _selectedPaths.Add(lead);
            _selectedPaths.IntersectWith(_items.Select(item => item.Path));
            _selectedItem = _items.FirstOrDefault(item => String.Equals(item.Path, lead, StringComparison.OrdinalIgnoreCase));
            if (_selectedItem == null) _selectedItem = _items.FirstOrDefault(item => _selectedPaths.Contains(item.Path));
            _selectionAnchor = _selectedItem == null ? null : _selectedItem.Path;
            PaintSelection();
        }

        public void SelectAll()
        {
            _selectedPaths.UnionWith(_items.Select(item => item.Path));
            if (_selectedItem == null) _selectedItem = _items.FirstOrDefault();
            _selectionAnchor = _selectedItem == null ? null : _selectedItem.Path;
            PaintSelection(); NotifySelection();
        }

        public void ApplySelection(ImageFileItem item, ModifierKeys modifiers)
        {
            bool control = (modifiers & ModifierKeys.Control) != 0;
            if ((modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor != null)
            {
                int anchor = -1, target = -1;
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Path == _selectionAnchor) anchor = i;
                    if (_items[i].Path == item.Path) target = i;
                }
                if (!control) _selectedPaths.Clear();
                if (anchor >= 0 && target >= 0)
                    for (int i = Math.Min(anchor, target); i <= Math.Max(anchor, target); i++) _selectedPaths.Add(_items[i].Path);
                else _selectedPaths.Add(item.Path);
            }
            else
            {
                if (!control) _selectedPaths.Clear();
                if (!control || !_selectedPaths.Remove(item.Path)) _selectedPaths.Add(item.Path);
                _selectionAnchor = item.Path;
            }
            _selectedItem = _selectedPaths.Contains(item.Path) ? item : _items.FirstOrDefault(i => _selectedPaths.Contains(i.Path));
            PaintSelection(); NotifySelection();
        }

        public bool HandleNavigationKey(Key key, ModifierKeys modifiers)
        {
            if (key != Key.Left && key != Key.Right && key != Key.Up && key != Key.Down
                && key != Key.Home && key != Key.End && key != Key.PageUp && key != Key.PageDown) return false;
            if (modifiers != ModifierKeys.None && modifiers != ModifierKeys.Shift
                && modifiers != (ModifierKeys.Control | ModifierKeys.Shift)) return false;
            _dragCandidate = false;
            _pendingPlainSelection = null;
            Focus();
            if (key == Key.PageUp || key == Key.PageDown)
            {
                if (key == Key.PageUp) PageUp();
                else PageDown();
                return true;
            }
            if (_items.Count == 0) return true;
            UpdateExtent();
            int current = SelectedIndex();
            int target;
            if (key == Key.Home || key == Key.End)
            {
                int step = key == Key.Home ? 1 : -1;
                target = step > 0 ? 0 : _items.Count - 1;
                while (target >= 0 && target < _items.Count && _items[target].IsDirectory) target += step;
                if (target < 0 || target >= _items.Count) return true;
            }
            else
            {
                int step;
                if (key == Key.Left) step = -1;
                else if (key == Key.Right) step = 1;
                else if (key == Key.Up) step = -_columnCount;
                else if (key == Key.Down) step = _columnCount;
                else return false;
                target = current < 0 ? 0 : Math.Max(0, Math.Min(_items.Count - 1, current + step));
                if (current >= 0 && ((key == Key.Up && current < _columnCount)
                    || (key == Key.Down && current / _columnCount == (_items.Count - 1) / _columnCount))) target = current;
            }
            ApplySelection(_items[target], modifiers);
            BringSelectionIntoView();
            return true;
        }

        private int SelectedIndex()
        {
            if (_selectedItem == null) return -1;
            for (int i = 0; i < _items.Count; i++)
                if (String.Equals(_items[i].Path, _selectedItem.Path, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public void BringSelectionIntoView()
        {
            int index = SelectedIndex();
            if (index < 0 || ViewportHeight <= 0) return;
            UpdateExtent();
            double top = (index / _columnCount) * _tileHeight;
            if (top < VerticalOffset || _tileHeight >= ViewportHeight) ScrollToVerticalOffset(top);
            else if (top + _tileHeight > VerticalOffset + ViewportHeight)
                ScrollToVerticalOffset(top + _tileHeight - ViewportHeight);
        }

        private void PaintSelection()
        {
            foreach (KeyValuePair<int, ThumbnailTile> pair in _realized)
            {
                PaintTileSelection(pair.Value);
            }
        }

        private void PaintTileSelection(ThumbnailTile tile)
        {
            bool selected = tile.Item != null && _selectedPaths.Contains(tile.Item.Path);
            bool active = selected && _selectedItem != null
                && String.Equals(tile.Item.Path, _selectedItem.Path, StringComparison.OrdinalIgnoreCase);
            tile.SetSelected(selected, active);
        }

        private void NotifySelection()
        {
            if (ItemSelected != null) ItemSelected(this, new ThumbnailActivatedEventArgs { Item = _selectedItem });
        }

        private void UpdateExtent()
        {
            double width = ViewportWidth > 1 ? ViewportWidth : ActualWidth;
            _columnCount = Math.Max(1, (int)Math.Floor(Math.Max(1, width) / _tileWidth));
            int rows = (_items.Count + _columnCount - 1) / _columnCount;
            _canvas.Width = Math.Max(width, _columnCount * _tileWidth);
            _canvas.Height = Math.Max(ViewportHeight, rows * _tileHeight);
        }

        private void RealizeVisibleTiles()
        {
            if (_items.Count == 0 || ActualWidth <= 1)
            {
                ClearRealized();
                return;
            }

            UpdateExtent();

            int firstRow = Math.Max(0, (int)Math.Floor(VerticalOffset / _tileHeight) - 3);
            int lastRow = Math.Min(
                (_items.Count + _columnCount - 1) / _columnCount,
                (int)Math.Ceiling((VerticalOffset + ViewportHeight) / _tileHeight) + 4);
            int firstIndex = firstRow * _columnCount;
            int lastIndex = Math.Min(_items.Count - 1, (lastRow * _columnCount) - 1);

            var remove = new List<int>();
            foreach (int index in _realized.Keys)
            {
                if (index < firstIndex || index > lastIndex)
                {
                    remove.Add(index);
                }
            }

            for (int i = 0; i < remove.Count; i++)
            {
                ThumbnailTile tile = _realized[remove[i]];
                tile.Release();
                _canvas.Children.Remove(tile);
                _realized.Remove(remove[i]);
            }

            for (int index = firstIndex; index <= lastIndex; index++)
            {
                if (index < 0 || index >= _items.Count)
                {
                    continue;
                }

                ThumbnailTile tile;
                if (!_realized.TryGetValue(index, out tile))
                {
                    tile = new ThumbnailTile(_thumbnailCache, _thumbnailSize, _aspectRatio, IsFavoriteFolder, ItemMenuFactory, ItemDetails);
                    tile.MouseLeftButtonDown += TileMouseLeftButtonDown;
                    tile.MouseRightButtonDown += delegate(object sender, MouseButtonEventArgs e)
                    {
                        var clicked = (ThumbnailTile)sender;
                        if (!_selectedPaths.Contains(clicked.Item.Path)) SelectItem(clicked.Item);
                        else _selectedItem = clicked.Item;
                        PaintSelection();
                        clicked.Focus(); NotifySelection();
                    };
                    tile.ContextMenuOpening += delegate(object sender, ContextMenuEventArgs e)
                    {
                        UpdateMenu(((ThumbnailTile)sender).ContextMenu);
                    };
                    tile.CommandRequested += TileCommandRequested;
                    _realized[index] = tile;
                    _canvas.Children.Add(tile);
                }

                tile.SetItem(_items[index]);
                PaintTileSelection(tile);
                int row = index / _columnCount;
                int column = index % _columnCount;
                Canvas.SetLeft(tile, column * _tileWidth);
                Canvas.SetTop(tile, row * _tileHeight);
            }
        }

        private void ClearRealized()
        {
            foreach (ThumbnailTile tile in _realized.Values) tile.Release();
            _canvas.Children.Clear();
            _realized.Clear();
        }

        private void TileMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ThumbnailTile tile = sender as ThumbnailTile;
            if (tile == null || tile.Item == null)
            {
                return;
            }

            tile.Focus();
            _dragOrigin = e.GetPosition(this);
            _dragCandidate = e.ClickCount == 1;
            _pendingPlainSelection = null;
            if (Keyboard.Modifiers == ModifierKeys.None && _selectedPaths.Contains(tile.Item.Path) && SelectedCount > 1 && e.ClickCount == 1)
            {
                _pendingPlainSelection = tile.Item;
                _selectedItem = tile.Item;
                PaintSelection(); NotifySelection();
            }
            else ApplySelection(tile.Item, Keyboard.Modifiers);

            if (e.ClickCount >= 2 && Keyboard.Modifiers == ModifierKeys.None && ItemActivated != null)
            {
                ItemActivated(this, new ThumbnailActivatedEventArgs { Item = tile.Item });
            }
            e.Handled = true;
        }

        private void GridMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragCandidate || e.LeftButton != MouseButtonState.Pressed || DragRequested == null) return;
            Point current = e.GetPosition(this);
            if (Math.Abs(current.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(current.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragCandidate = false;
            _pendingPlainSelection = null;
            DragRequested(this, EventArgs.Empty);
        }

        private ContextMenu BuildBackgroundMenu()
        {
            var menu = new ContextMenu();
            ThemeManager.Bind(menu, Control.BackgroundProperty, ThemeKeys.ControlBackground);
            ThemeManager.Bind(menu, Control.ForegroundProperty, ThemeKeys.Text);
            foreach (string command in new[] { "copy", "cut", "paste" })
            {
                string action = command;
                var item = new MenuItem { Header = Char.ToUpper(command[0]) + command.Substring(1), Tag = command };
                item.Click += delegate
                {
                    if (CommandRequested != null) CommandRequested(this, new ThumbnailCommandEventArgs { Command = action });
                };
                menu.Items.Add(item);
            }
            menu.Opened += delegate { UpdateMenu(menu); };
            return menu;
        }

        private void UpdateMenu(ContextMenu menu)
        {
            foreach (MenuItem item in menu.Items.OfType<MenuItem>())
            {
                string command = item.Tag as string;
                if (command == "paste") item.IsEnabled = CanPaste != null && CanPaste();
                if (command == "copy" || command == "cut") item.IsEnabled = SelectedCount > 0;
                if (command == "rename") item.IsEnabled = SelectedCount == 1;
                if (command == "move" || command == "delete") item.IsEnabled = SelectedCount > 0;
                if (command != null && command.StartsWith("batch-", StringComparison.Ordinal)) item.IsEnabled = SelectedCount > 0;
            }
        }

        private void TileCommandRequested(object sender, ThumbnailCommandEventArgs e)
        {
            if (CommandRequested != null)
            {
                CommandRequested(this, e);
            }
        }
    }

    internal sealed class ThumbnailTile : Border
    {
        private static readonly DropShadowEffect PreviewShadow = CreatePreviewShadow();

        private static DropShadowEffect CreatePreviewShadow()
        {
            var shadow = new DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 6, ShadowDepth = 3,
                Direction = 270, Opacity = 0.4, RenderingBias = RenderingBias.Performance
            };
            shadow.Freeze();
            return shadow;
        }

        private readonly ThumbnailCacheService _thumbnailCache;
        private readonly int _thumbnailSize;
        private readonly Image _image;
        private readonly TextBlock _placeholder;
        private readonly TextBlock _title;
        private readonly TextBlock _details;
        private readonly Grid _imageHost;
        private readonly Border _previewFrame;
        private FolderPreview _folderPreview;
        private CancellationTokenSource _request;
        private readonly Func<string, bool> _isFavorite;
        private readonly Func<ImageFileItem, ContextMenu> _menuFactory;
        private readonly Func<ImageFileItem, string> _itemDetails;
        private int _loadVersion;

        public event EventHandler<ThumbnailCommandEventArgs> CommandRequested;

        public ThumbnailTile(ThumbnailCacheService thumbnailCache, int thumbnailSize, double aspectRatio = 1,
            Func<string, bool> isFavorite = null, Func<ImageFileItem, ContextMenu> menuFactory = null,
            Func<ImageFileItem, string> itemDetails = null)
        {
            _isFavorite = isFavorite;
            _menuFactory = menuFactory;
            _itemDetails = itemDetails;
            _thumbnailCache = thumbnailCache;
            _thumbnailSize = thumbnailSize;
            Focusable = true;
            Width = CalculateTileWidth(_thumbnailSize);
            Height = CalculateTileHeight(_thumbnailSize, aspectRatio);
            Padding = new Thickness(8);
            Margin = new Thickness(0);
            BorderThickness = new Thickness(1);
            ThemeManager.Bind(this, BorderBrushProperty, ThemeKeys.Border);
            ThemeManager.Bind(this, BackgroundProperty, ThemeKeys.TileBackground);
            SnapsToDevicePixels = true;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_thumbnailSize / aspectRatio) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var imageHost = new Grid();
            _imageHost = imageHost;
            ThemeManager.Bind(imageHost, Panel.BackgroundProperty, ThemeKeys.TileImageBackground);
            imageHost.ClipToBounds = true;
            _image = new Image();
            _image.Stretch = Stretch.Uniform;
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            _placeholder = new TextBlock();
            _placeholder.HorizontalAlignment = HorizontalAlignment.Center;
            _placeholder.VerticalAlignment = VerticalAlignment.Center;
            _placeholder.FontWeight = FontWeights.SemiBold;
            ThemeManager.Bind(_placeholder, TextBlock.ForegroundProperty, ThemeKeys.SubtleText);
            imageHost.Children.Add(_image);
            imageHost.Children.Add(_placeholder);
            _previewFrame = new Border { Child = imageHost, Effect = PreviewShadow };
            Grid.SetRow(_previewFrame, 0);
            grid.Children.Add(_previewFrame);

            _title = new TextBlock();
            _title.Margin = new Thickness(0, 8, 0, 0);
            _title.TextTrimming = TextTrimming.CharacterEllipsis;
            _title.FontSize = 12;
            ThemeManager.Bind(_title, TextBlock.ForegroundProperty, ThemeKeys.Text);
            Grid.SetRow(_title, 1);
            grid.Children.Add(_title);

            _details = new TextBlock();
            _details.Margin = new Thickness(0, 2, 0, 0);
            _details.TextTrimming = TextTrimming.CharacterEllipsis;
            _details.FontSize = 11;
            ThemeManager.Bind(_details, TextBlock.ForegroundProperty, ThemeKeys.SubtleText);
            Grid.SetRow(_details, 2);
            grid.Children.Add(_details);

            Child = grid;
        }

        public ImageFileItem Item { get; private set; }

        public static double CalculateTileWidth(int thumbnailSize)
        {
            return thumbnailSize + 20;
        }

        public static double CalculateTileHeight(int thumbnailSize, double aspectRatio = 1)
        {
            return thumbnailSize / aspectRatio + 68;
        }

        public void SetItem(ImageFileItem item)
        {
            if (Item == item)
            {
                return;
            }

            Item = item;
            Release();
            int version = _loadVersion;

            _image.Source = null;
            _placeholder.Text = item.IsDirectory ? "" : item.Extension.TrimStart('.').ToUpperInvariant();
            _title.Text = item.Name;
            _details.Text = _itemDetails != null ? _itemDetails(item) : item.IsDirectory ? "Folder" : ImageExtensions.FormatBytes(item.Length);
            if (_itemDetails != null) ToolTip = item.Path;
            ContextMenu = _menuFactory != null ? _menuFactory(item) : BuildContextMenu();
            if (item.IsDirectory)
            {
                if (_folderPreview == null)
                {
                    _folderPreview = new FolderPreview(_thumbnailCache, _thumbnailSize);
                    _imageHost.Children.Add(_folderPreview);
                }
                _folderPreview.Visibility = Visibility.Visible;
                _folderPreview.SetFolder(item.Path);
                return;
            }
            if (_folderPreview != null) _folderPreview.Visibility = Visibility.Collapsed;

            var request = new CancellationTokenSource();
            _request = request;
            Task<BitmapSource> task = _thumbnailCache.GetThumbnailAsync(item, _thumbnailSize, request.Token);
            task.ContinueWith(
                delegate(Task<BitmapSource> completed)
                {
                    // Observe failed/canceled work even when its tile was recycled.
                    var failure = completed.Exception;
                    if (Dispatcher.HasShutdownStarted) { request.Dispose(); return; }
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(
                            delegate
                            {
                                if (_request == request) _request = null;
                                request.Dispose();
                                if (version != _loadVersion || Item != item)
                                {
                                    return;
                                }

                                if (completed.Status == TaskStatus.RanToCompletion)
                                {
                                    _image.Source = completed.Result;
                                    _placeholder.Text = "";
                                }
                                else
                                {
                                    _image.Source = null;
                                    _placeholder.Text = item.Extension.TrimStart('.').ToUpperInvariant();
                                }
                            }));
                },
                TaskScheduler.Default);
        }

        public void Release()
        {
            _loadVersion++;
            if (_request != null) _request.Cancel();
            _request = null;
            _image.Source = null;
            if (_folderPreview != null) _folderPreview.Release();
        }

        public void SetSelected(bool selected, bool active = false)
        {
            _title.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            if (active)
            {
                ThemeManager.Bind(this, BorderBrushProperty, ThemeKeys.ActiveThumbnailBorder);
                ThemeManager.Bind(this, BackgroundProperty, ThemeKeys.ActiveThumbnailBackground);
            }
            else if (selected)
            {
                ThemeManager.Bind(this, BorderBrushProperty, ThemeKeys.SelectedBorder);
                ThemeManager.Bind(this, BackgroundProperty, ThemeKeys.SelectedBackground);
            }
            else
            {
                ThemeManager.Bind(this, BorderBrushProperty, ThemeKeys.Border);
                ThemeManager.Bind(this, BackgroundProperty, ThemeKeys.TileBackground);
            }
        }

        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();
            ThemeManager.Bind(menu, Control.BackgroundProperty, ThemeKeys.ControlBackground);
            ThemeManager.Bind(menu, Control.ForegroundProperty, ThemeKeys.Text);
            ThemeManager.Bind(menu, Control.BorderBrushProperty, ThemeKeys.Border);
            AddMenuItem(menu, "Open", "open");
            AddMenuItem(menu, "Open in new tab", "open-new-tab");
            if (Item != null && Item.IsDirectory)
            {
                AddMenuItem(menu, "Pin to favorites", "toggle-favorite");
                menu.Opened += delegate
                {
                    ((MenuItem)menu.Items[2]).Header = _isFavorite != null && _isFavorite(Item.Path)
                        ? "Unpin from favorites" : "Pin to favorites";
                };
            }
            AddMenuItem(menu, "Rename", "rename");
            AddMenuItem(menu, "Move", "move");
            AddMenuItem(menu, "Delete", "delete");
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "Batch rename...", "batch-rename");
            AddMenuItem(menu, "Batch resize...", "batch-resize");
            AddMenuItem(menu, "Batch convert format...", "batch-convert");
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "Copy", "copy");
            AddMenuItem(menu, "Cut", "cut");
            AddMenuItem(menu, Item != null && Item.IsDirectory ? "Paste into folder" : "Paste", "paste");
            return menu;
        }

        private void AddMenuItem(ContextMenu menu, string header, string command)
        {
            var item = new MenuItem();
            item.Header = header;
            item.Tag = command;
            item.Click += delegate
            {
                if (CommandRequested != null && Item != null)
                {
                    CommandRequested(this, new ThumbnailCommandEventArgs { Item = Item, Command = command });
                }
            };
            menu.Items.Add(item);
        }
    }
}
