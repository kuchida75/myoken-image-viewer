using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed class FolderNavigationEventArgs : EventArgs
    {
        public string Path { get; set; }
        public bool NewTab { get; set; }
    }

    internal sealed class FolderNavigationPane : DockPanel, IDisposable
    {
        private sealed class Node
        {
            public string Path;
            public string RootKey;
            public string Key;
            public bool Loaded;
            public CancellationTokenSource Request;
        }

        private readonly TreeView _tree;
        private readonly TreeViewItem _favoritesNode;
        private readonly List<string> _favorites = new List<string>();
        private readonly Dictionary<string, FavoriteFolderDetails> _favoriteDetails = new Dictionary<string, FavoriteFolderDetails>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _expanded;
        private readonly SemaphoreSlim _workers = new SemaphoreSlim(4, 4);
        private bool _disposed;
        private bool _reorderingFavorites;
        private readonly ReorderDrag _favoriteReorder;

        public event EventHandler<FolderNavigationEventArgs> FolderRequested;
        public event EventHandler PreferencesChanged;

        public FolderNavigationPane(BrowserPreferences preferences)
        {
            preferences = preferences ?? new BrowserPreferences();
            _expanded = new HashSet<string>(preferences.ExpandedNodes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (string path in preferences.FavoriteFolders ?? new List<string>())
            {
                string normalized = NormalizePath(path);
                if (normalized != null && !_favorites.Contains(normalized, StringComparer.OrdinalIgnoreCase)) _favorites.Add(normalized);
            }
            foreach (FavoriteFolderDetails details in preferences.FavoriteDetails ?? new List<FavoriteFolderDetails>())
            {
                if (details == null) continue;
                string path = NormalizePath(details.Path);
                if (path != null && IsFavorite(path))
                    _favoriteDetails[path] = NormalizeDetails(path, details.Name, details.Description);
            }
            ThemeManager.Bind(this, BackgroundProperty, ThemeKeys.PaneBackground);
            var header = new TextBlock { Text = "Folders", FontWeight = FontWeights.SemiBold, FontSize = 15,
                Margin = new Thickness(12, 12, 12, 8) };
            DockPanel.SetDock(header, Dock.Top);
            Children.Add(header);
            _tree = new TreeView { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
            _tree.SizeChanged += delegate
            {
                if (_favoritesNode == null) return;
                foreach (TreeViewItem item in _favoritesNode.Items)
                    ((StackPanel)item.Header).Width = Math.Max(40, _tree.ActualWidth - 58);
            };
            VirtualizingStackPanel.SetIsVirtualizing(_tree, true);
            VirtualizingStackPanel.SetVirtualizationMode(_tree, VirtualizationMode.Recycling);
            _tree.SelectedItemChanged += delegate
            {
                if (_reorderingFavorites) return;
                var item = _tree.SelectedItem as TreeViewItem;
                var node = item == null ? null : item.Tag as Node;
                if (node != null) Open(node.Path, false);
            };
            Children.Add(_tree);
            _favoritesNode = new TreeViewItem { Header = "\u2605 Favorites", FontWeight = FontWeights.SemiBold };
            _favoritesNode.Expanded += delegate(object sender, RoutedEventArgs e)
            {
                if (e.OriginalSource != _favoritesNode) return;
                _expanded.Add("favorites");
                foreach (TreeViewItem child in _favoritesNode.Items) if (child.IsExpanded) EnsureChildren(child);
                Changed();
            };
            _favoritesNode.Collapsed += delegate(object sender, RoutedEventArgs e)
            {
                if (e.OriginalSource != _favoritesNode) return;
                _expanded.Remove("favorites");
                foreach (TreeViewItem child in _favoritesNode.Items) CancelLoads(child);
                Changed();
            };
            _tree.Items.Add(_favoritesNode);
            _favoriteReorder = new ReorderDrag(_tree, false,
                delegate { return _favoritesNode.Items.Cast<TreeViewItem>().Select(item => new ReorderItem
                    { Key = ((Node)item.Tag).Path, Element = (FrameworkElement)item.Header }).ToList(); },
                ReorderFavorite, delegate { return ReorderDrag.FindScrollViewer(_tree); },
                delegate(Point point)
                {
                    return _favoritesNode.IsExpanded && _favoritesNode.IsVisible
                        && _favoritesNode.TransformToAncestor(_tree).TransformBounds(new Rect(_favoritesNode.RenderSize)).Contains(point);
                });
            RefreshFavorites();
            _favoritesNode.IsExpanded = _expanded.Contains("favorites");
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(pictures)) _tree.Items.Add(CreateNode("Pictures", pictures, "pictures"));
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady) _tree.Items.Add(CreateNode(drive.Name, drive.RootDirectory.FullName, "drive:" + drive.Name));
            }
        }

        public bool IsFavorite(string path)
        {
            string normalized = NormalizePath(path);
            return normalized != null && _favorites.Contains(normalized, StringComparer.OrdinalIgnoreCase);
        }

        public void ToggleFavorite(string path)
        {
            string normalized = NormalizePath(path);
            if (normalized == null) return;
            int index = _favorites.FindIndex(value => String.Equals(value, normalized, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) { _favorites.RemoveAt(index); _favoriteDetails.Remove(normalized); }
            else
            {
                if (!Directory.Exists(normalized)) return;
                _favorites.Add(normalized);
                _favoritesNode.IsExpanded = true;
            }
            RefreshFavorites();
            Changed();
        }

        public BrowserPreferences CapturePreferences()
        {
            return new BrowserPreferences
            {
                FavoriteFolders = new List<string>(_favorites),
                FavoriteDetails = _favorites.Where(path => _favoriteDetails.ContainsKey(path)).Select(path => _favoriteDetails[path].Copy()).ToList(),
                ExpandedNodes = _expanded.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        public FavoriteFolderDetails GetFavoriteDetails(string path)
        {
            string normalized = NormalizePath(path);
            FavoriteFolderDetails details;
            return normalized != null && _favoriteDetails.TryGetValue(normalized, out details)
                ? details.Copy() : new FavoriteFolderDetails { Path = normalized, Name = "", Description = "" };
        }

        public void SetFavoriteDetails(string path, string name, string description)
        {
            string normalized = NormalizePath(path);
            if (normalized == null || !IsFavorite(normalized)) return;
            _favoriteDetails[normalized] = NormalizeDetails(normalized, name, description);
            foreach (TreeViewItem item in _favoritesNode.Items)
                if (String.Equals(((Node)item.Tag).Path, normalized, StringComparison.OrdinalIgnoreCase)) UpdateFavoriteHeader(item);
            Changed();
        }

        private static FavoriteFolderDetails NormalizeDetails(string path, string name, string description)
        {
            name = (name ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            description = (description ?? "").Trim();
            return new FavoriteFolderDetails { Path = path, Name = name.Substring(0, Math.Min(200, name.Length)),
                Description = description.Substring(0, Math.Min(2000, description.Length)) };
        }

        private void EditFavorite(string path)
        {
            FavoriteFolderDetails edited = FavoriteDetailsWindow.Edit(Window.GetWindow(this), GetFavoriteDetails(path));
            if (edited != null) SetFavoriteDetails(path, edited.Name, edited.Description);
        }

        private void UpdateFavoriteHeader(TreeViewItem item)
        {
            var node = (Node)item.Tag;
            FavoriteFolderDetails details = GetFavoriteDetails(node.Path);
            string folderName = System.IO.Path.GetFileName(node.Path.TrimEnd('\\'));
            string name = String.IsNullOrEmpty(details.Name) ? (String.IsNullOrEmpty(folderName) ? node.Path : folderName) : details.Name;
            var header = new StackPanel { Width = _tree.ActualWidth > 0 ? Math.Max(40, _tree.ActualWidth - 58) : 202,
                Margin = new Thickness(0, 2, 0, 2) };
            header.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!String.IsNullOrEmpty(details.Description))
            {
                var description = new TextBlock { Text = details.Description.Replace('\r', ' ').Replace('\n', ' '),
                    TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
                ThemeManager.Bind(description, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
                header.Children.Add(description);
            }
            item.Header = header;
            _favoriteReorder.Bind(header, node.Path, delegate { item.IsSelected = true; item.Focus(); });
            var tooltip = new StackPanel { MaxWidth = 420 };
            tooltip.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!String.IsNullOrEmpty(details.Description))
                tooltip.Children.Add(new TextBlock { Text = details.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) });
            tooltip.Children.Add(new TextBlock { Text = node.Path, TextWrapping = TextWrapping.Wrap });
            item.ToolTip = new ScrollViewer { Content = tooltip, MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }

        private void RefreshFavorites()
        {
            foreach (TreeViewItem item in _favoritesNode.Items) CancelLoads(item);
            _favoritesNode.Items.Clear();
            foreach (string path in _favorites)
            {
                string name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
                _favoritesNode.Items.Add(CreateNode(String.IsNullOrEmpty(name) ? path : name, path, "favorite:" + path));
            }
        }

        private bool ReorderFavorite(string path, int insertionSlot)
        {
            if (_disposed) return false;
            int before = _favorites.FindIndex(value => String.Equals(value, path, StringComparison.OrdinalIgnoreCase));
            if (!ReorderList.Move(_favorites, path, insertionSlot)) return false;
            int after = _favorites.FindIndex(value => String.Equals(value, path, StringComparison.OrdinalIgnoreCase));
            var item = (TreeViewItem)_favoritesNode.Items[before];
            var selected = _tree.SelectedItem as TreeViewItem;
            _reorderingFavorites = true;
            try
            {
                _favoritesNode.Items.RemoveAt(before);
                _favoritesNode.Items.Insert(after, item);
                if (selected != null) selected.IsSelected = true;
            }
            finally { _reorderingFavorites = false; }
            Changed(); return true;
        }

        private TreeViewItem CreateNode(string label, string path, string rootKey)
        {
            var node = new Node { Path = path, RootKey = rootKey, Key = rootKey + "|" + path };
            var item = new TreeViewItem { Header = label, Tag = node, ToolTip = path, FontWeight = FontWeights.Normal };
            if (String.Equals(rootKey, "favorite:" + path, StringComparison.OrdinalIgnoreCase)) UpdateFavoriteHeader(item);
            item.Items.Add("...");
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Open" };
            open.Click += delegate { Open(path, false); };
            menu.Items.Add(open);
            var newTab = new MenuItem { Header = "Open in new tab" };
            newTab.Click += delegate { Open(path, true); };
            menu.Items.Add(newTab);
            var favorite = new MenuItem();
            favorite.Click += delegate { ToggleFavorite(path); };
            menu.Items.Add(favorite);
            var renameFavorite = new MenuItem { Header = "Rename favorite...", Tag = "rename-favorite" };
            renameFavorite.Click += delegate { EditFavorite(path); };
            menu.Items.Add(renameFavorite);
            menu.Opened += delegate { renameFavorite.Visibility = IsFavorite(path) ? Visibility.Visible : Visibility.Collapsed; };
            menu.Opened += delegate { favorite.Header = IsFavorite(path) ? "Unpin from favorites" : "Pin to favorites"; };
            item.ContextMenu = menu;
            item.Loaded += delegate { if (item.IsExpanded) EnsureChildren(item); };
            item.Unloaded += delegate { CancelLoads(item); };
            item.Expanded += delegate(object sender, RoutedEventArgs e)
            {
                if (e.OriginalSource != item) return;
                _expanded.Add(node.Key);
                EnsureChildren(item);
                Changed();
            };
            item.Collapsed += delegate(object sender, RoutedEventArgs e)
            {
                if (e.OriginalSource != item) return;
                _expanded.Remove(node.Key);
                CancelLoads(item);
                Changed();
            };
            item.IsExpanded = _expanded.Contains(node.Key);
            return item;
        }

        private async void EnsureChildren(TreeViewItem item)
        {
            var node = (Node)item.Tag;
            if (_disposed || !item.IsLoaded || !item.IsExpanded || node.Request != null) return;
            if (node.Loaded)
            {
                foreach (object child in item.Items)
                {
                    var childItem = child as TreeViewItem;
                    if (childItem != null && childItem.IsExpanded) EnsureChildren(childItem);
                }
                return;
            }
            var request = new CancellationTokenSource();
            node.Request = request;
            DirectoryInfo[] directories = null;
            try
            {
                directories = await Task.Run(async delegate
                {
                    await _workers.WaitAsync(request.Token).ConfigureAwait(false);
                    try
                    {
                        var result = new List<DirectoryInfo>();
                        foreach (DirectoryInfo directory in new DirectoryInfo(node.Path).EnumerateDirectories())
                        {
                            request.Token.ThrowIfCancellationRequested();
                            if ((directory.Attributes & FileAttributes.Hidden) == 0) result.Add(directory);
                        }
                        return result.OrderBy(directory => directory.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                    }
                    finally { _workers.Release(); }
                }, request.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            if (!Dispatcher.HasShutdownStarted)
            {
                await Dispatcher.InvokeAsync(new Action(delegate
                {
                    if (_disposed || node.Request != request) return;
                    node.Request = null;
                    if (request.IsCancellationRequested) return;
                    item.Items.Clear();
                    if (directories == null) item.Items.Add("Unavailable");
                    else
                    {
                        node.Loaded = true;
                        foreach (DirectoryInfo directory in directories)
                            item.Items.Add(CreateNode(directory.Name, directory.FullName, node.RootKey));
                    }
                }));
            }
            request.Dispose();
        }

        private void CancelLoads(TreeViewItem item)
        {
            var node = item.Tag as Node;
            if (node != null && node.Request != null)
            {
                node.Request.Cancel();
                node.Request = null;
            }
            foreach (object child in item.Items)
            {
                var childItem = child as TreeViewItem;
                if (childItem != null) CancelLoads(childItem);
            }
        }

        private void Open(string path, bool newTab)
        {
            if (FolderRequested != null) FolderRequested(this, new FolderNavigationEventArgs { Path = path, NewTab = newTab });
        }

        private void Changed()
        {
            if (!_disposed && PreferencesChanged != null) PreferencesChanged(this, EventArgs.Empty);
        }

        private static string NormalizePath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            try
            {
                string full = System.IO.Path.GetFullPath(path);
                return String.Equals(full, System.IO.Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase)
                    ? full : full.TrimEnd('\\', '/');
            }
            catch (Exception) { return null; }
        }

        public void Dispose()
        {
            _disposed = true;
            _favoriteReorder.Dispose();
            foreach (TreeViewItem item in _tree.Items) CancelLoads(item);
        }
    }
}
