using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Task _recycleTask;

        private void RenameConfirmedItem(string path, string name)
        {
            if (_fileTransferBusy || _backupBusy || _rotationSaving || _enhancementSaving) return;
            TransferredItem renamed = FileTransferService.Rename(path, name);
            RemapTransferredPaths(new[] { renamed }); RefreshFolder();
        }

        private void ChooseMoveDestination(string[] paths)
        {
            if (_fileTransferBusy || _backupBusy || _rotationSaving || _enhancementSaving || paths.Length == 0) return;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Move selected files and folders to" })
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    TransferFilesAsync(paths, dialog.SelectedPath, true, null);
        }

        private void DeleteBrowserItems(string[] paths)
        {
            if (_fileTransferBusy || _backupBusy || _rotationSaving || _enhancementSaving || paths.Length == 0) return;
            string name = paths.Length == 1 ? Path.GetFileName(paths[0]) : paths.Length + " selected items";
            if (MessageBox.Show(this, "Send " + name + " to the Recycle Bin?\n\nSelected folders include all of their contents.",
                "Recycle files and folders", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _recycleTask = RecycleBrowserItemsAsync(paths);
        }

        private async Task RecycleBrowserItemsAsync(string[] paths)
        {
            if (_fileTransferBusy || _backupBusy || _rotationSaving || _enhancementSaving) return;
            StopSlideshow(); SaveWorkspaceState();
            _fileTransferBusy = true; IsEnabled = false; _configurationContent.IsEnabled = false;
            _transferStatus = "Sending items to Recycle Bin..."; UpdateStatus();
            var completed = new List<TransferredItem>(); var errors = new List<string>(); bool canceled = false;
            await Task.Run(delegate
            {
                try
                {
                    foreach (string path in FileTransferService.TopLevelSources(paths))
                    {
                        try
                        {
                            bool directory = Directory.Exists(path);
                            FileTransferService.Recycle(path);
                            completed.Add(new TransferredItem { Source = path, IsDirectory = directory });
                        }
                        catch (OperationCanceledException) { canceled = true; break; }
                        catch (Exception error) { errors.Add(Path.GetFileName(path) + ": " + error.Message); }
                    }
                }
                catch (Exception error) { errors.Add(error.Message); }
            }).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                bool returnFocus = IsActive;
                try
                {
                    if (completed.Count > 0) ApplyRecycledItems(completed);
                    _transferStatus = completed.Count + " recycled" + (canceled ? ", canceled" : "")
                        + (errors.Count > 0 ? ", " + errors.Count + " failed" : "");
                }
                finally
                {
                    _fileTransferBusy = false; IsEnabled = true; _configurationContent.IsEnabled = true;
                    UpdateStatus(); ScheduleSessionSave();
                    UIElement target = _browserView.IsVisible ? (UIElement)_thumbnailGrid : _imageCanvas;
                    FocusManager.SetFocusedElement(this, target);
                    if (returnFocus) target.Focus();
                }
                if (errors.Count > 0) MessageBox.Show(this, String.Join("\n", errors.Take(8)),
                    "Some items could not be recycled", MessageBoxButton.OK, MessageBoxImage.Warning);
            }));
        }

        internal static Func<string, bool> DeletedPathMatcher(IList<TransferredItem> items)
        {
            var paths = new HashSet<string>(items.Select(item => Path.GetFullPath(item.Source).TrimEnd('\\', '/')), StringComparer.OrdinalIgnoreCase);
            var folders = new HashSet<string>(items.Where(item => item.IsDirectory)
                .Select(item => Path.GetFullPath(item.Source).TrimEnd('\\', '/')), StringComparer.OrdinalIgnoreCase);
            // Selection can contain 100k files; do not compare every browser item against every deletion.
            return delegate(string path)
            {
                if (String.IsNullOrEmpty(path)) return false;
                path = Path.GetFullPath(path).TrimEnd('\\', '/');
                if (paths.Contains(path)) return true;
                if (folders.Count == 0) return false;
                for (string parent = Path.GetDirectoryName(path); !String.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                    if (folders.Contains(parent.TrimEnd('\\', '/'))) return true;
                return false;
            };
        }

        private static string ExistingParent(string path)
        {
            while (!String.IsNullOrEmpty(path) && !Directory.Exists(path)) path = Path.GetDirectoryName(path);
            return path;
        }

        private void ApplyRecycledItems(IList<TransferredItem> items)
        {
            Func<string, bool> affected = DeletedPathMatcher(items);
            foreach (ImageTabState state in _tabs.Values.Concat(new[] { _homeBrowser, _latestBrowserView }).Where(s => s != null))
            {
                state.SelectedPaths.RemoveAll(path => affected(path));
                state.ForwardFolders.RemoveAll(path => affected(path));
                if (affected(state.SelectedPath)) state.SelectedPath = null;
                if (!affected(state.Path) && !affected(state.FolderPath)) continue;
                if (state.Path != null) _services.VramCache.Remove(state.Path);
                state.FolderPath = ExistingParent(state.FolderPath ?? Path.GetDirectoryName(state.Path));
                state.Path = null; state.IsBrowser = true; state.ManualAdjustments = null; state.SavedEnhanceRevision = null;
                state.HasCustomView = false; state.RotationQuarterTurns = 0; state.Zoom = 1;
                state.OffsetX = state.OffsetY = state.ViewportWidth = state.ViewportHeight = state.BrowserScrollOffset = 0;
            }
            RefreshChangedFolderNavigation(new TransferredItem[0], items);
            _allItems.RemoveAll(item => affected(item.Path)); RebuildImageOrdinals();
            DisplayTab(); RefreshFolder();
        }

        private void RefreshChangedFolderNavigation(IList<TransferredItem> moved, IList<TransferredItem> deleted)
        {
            if (!moved.Any(item => item.IsDirectory) && !deleted.Any(item => item.IsDirectory)) return;
            BrowserPreferences preferences = _folderNavigation.CapturePreferences();
            Func<string, bool> isDeleted = DeletedPathMatcher(deleted);
            Func<string, string> movedPath = CreateTransferredPathMapper(moved);
            Func<string, string> remap = delegate(string path)
            {
                if (isDeleted(path)) return null;
                return movedPath(path);
            };
            preferences.FavoriteFolders = preferences.FavoriteFolders.Select(remap).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (FavoriteFolderDetails detail in preferences.FavoriteDetails) detail.Path = remap(detail.Path);
            preferences.FavoriteDetails.RemoveAll(detail => detail.Path == null);
            var expanded = new List<string>();
            foreach (string key in preferences.ExpandedNodes)
            {
                int split = key.IndexOf('|');
                if (split < 0) { expanded.Add(key); continue; }
                string root = key.Substring(0, split), path = remap(key.Substring(split + 1));
                if (path == null) continue;
                if (root.StartsWith("favorite:", StringComparison.OrdinalIgnoreCase))
                {
                    string favorite = remap(root.Substring(9)); if (favorite == null) continue;
                    root = "favorite:" + favorite;
                }
                expanded.Add(root + "|" + path);
            }
            preferences.ExpandedNodes = expanded;
            var parent = (Panel)_folderNavigation.Parent; int index = parent.Children.IndexOf(_folderNavigation);
            _folderNavigation.Dispose(); parent.Children.Remove(_folderNavigation);
            UIElement pane = BuildFolderPane(preferences); Grid.SetColumn(pane, 0); parent.Children.Insert(index, pane);
            ApplyFolderLayout(); UpdateFavoriteButton(); ScheduleSessionSave();
        }
    }
}
