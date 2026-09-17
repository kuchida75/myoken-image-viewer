using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private bool _fileTransferBusy;
        private string _transferStatus;

        private bool CanPasteFiles()
        {
            if (_fileTransferBusy || !Directory.Exists(_currentFolder)) return false;
            try { return Clipboard.ContainsFileDropList(); }
            catch (Exception) { return false; }
        }

        private string[] SelectedFilePaths()
        {
            return _browserView.Visibility == Visibility.Visible
                ? _thumbnailGrid.SelectedItems.Select(item => item.Path).ToArray()
                : String.IsNullOrEmpty(_activeTabPath) ? new string[0] : new[] { _activeTabPath };
        }

        private void CopySelectedFiles(bool cut)
        {
            if (_fileTransferBusy) return;
            string[] paths = SelectedFilePaths();
            if (paths.Length == 0) return;
            try
            {
                Clipboard.SetDataObject(FileClipboardData.Create(paths, cut), true);
                _transferStatus = paths.Length + (cut ? " items ready to move" : " items copied to clipboard");
                UpdateBrowserFooter();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Clipboard unavailable"); }
        }

        private void PasteFiles(string destination)
        {
            if (_fileTransferBusy) return;
            try
            {
                uint sequence = FileClipboardData.GetClipboardSequenceNumber();
                IDataObject data = Clipboard.GetDataObject();
                string[] paths = FileClipboardData.Paths(data);
                if (paths.Length == 0) return;
                bool cut = FileClipboardData.IsCut(data);
                TransferFilesAsync(paths, destination, cut, cut ? (uint?)sequence : null);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Clipboard unavailable"); }
        }

        private bool HandleFileShortcut(Key key)
        {
            if (key == Key.A && _browserView.Visibility == Visibility.Visible) _thumbnailGrid.SelectAll();
            else if (key == Key.C) CopySelectedFiles(false);
            else if (key == Key.X) CopySelectedFiles(true);
            else if (key == Key.V) PasteFiles(_currentFolder);
            else return false;
            return true;
        }

        private void StartBrowserDrag(object sender, EventArgs e)
        {
            if (_fileTransferBusy) return;
            string[] paths = SelectedFilePaths();
            if (paths.Length == 0) return;
            try
            {
                DragDrop.DoDragDrop(_thumbnailGrid, FileClipboardData.Create(paths, false), DragDropEffects.Copy);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Unable to drag files"); }
        }

        private string BrowserDropDestination(DependencyObject source)
        {
            while (source != null && source != _browserView)
            {
                var tile = source as ThumbnailTile;
                if (tile != null && tile.Item != null && tile.Item.IsDirectory) return tile.Item.Path;
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            return _currentFolder;
        }

        private bool CanCopyDrop(IDataObject data, DragDropEffects allowed, string destination)
        {
            if (_fileTransferBusy || (allowed & DragDropEffects.Copy) == 0 || !Directory.Exists(destination)) return false;
            try
            {
                string[] paths = FileClipboardData.Paths(data);
                return paths.Length > 0 && !paths.Any(path => Directory.Exists(path) && FileTransferService.IsWithin(destination, path));
            }
            catch (Exception) { return false; }
        }

        private void BrowserDragOver(object sender, DragEventArgs e)
        {
            string destination = BrowserDropDestination(e.OriginalSource as DependencyObject);
            e.Effects = CanCopyDrop(e.Data, e.AllowedEffects, destination) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void BrowserDrop(object sender, DragEventArgs e)
        {
            string destination = BrowserDropDestination(e.OriginalSource as DependencyObject);
            e.Handled = true;
            e.Effects = DragDropEffects.None;
            if (!CanCopyDrop(e.Data, e.AllowedEffects, destination)) return;
            string[] paths = FileClipboardData.Paths(e.Data);
            e.Effects = DragDropEffects.Copy;
            TransferFilesAsync(paths, destination, false, null);
        }

        private ConflictAction ConfirmTransferRename(string source, string proposed)
        {
            MessageBoxResult answer = MessageBox.Show(this,
                Path.GetFileName(source) + " already exists in the destination.\n\nUse the name "
                + Path.GetFileName(proposed) + "?\n\nNo skips this item. Cancel stops the remaining transfer.",
                "Keep both files", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            return answer == MessageBoxResult.Yes ? ConflictAction.Rename
                : answer == MessageBoxResult.No ? ConflictAction.Skip : ConflictAction.Cancel;
        }

        private async void TransferFilesAsync(string[] paths, string destination, bool move, uint? clipboardSequence)
        {
            if (_fileTransferBusy) return;
            StopSlideshow();
            _fileTransferBusy = true;
            _transferStatus = move ? "Moving items..." : "Copying items...";
            UpdateStatus();
            TransferResult result;
            try
            {
                result = await Task.Run(delegate
                {
                    return FileTransferService.Execute(paths, destination, move,
                        (source, proposed) => Dispatcher.Invoke(() => ConfirmTransferRename(source, proposed)),
                        (index, total, name) => Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _transferStatus = (move ? "Moving " : "Copying ") + (index + 1) + " / " + total + ": " + name;
                            UpdateBrowserFooter();
                        })));
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = new TransferResult();
                result.Errors.Add(ex.Message);
            }
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                _fileTransferBusy = false;
                if (move) RemapTransferredPaths(result.Completed);
                if (clipboardSequence.HasValue && !result.Canceled && result.Errors.Count == 0 && result.Skipped == 0
                    && result.Completed.Count > 0 && clipboardSequence.Value == FileClipboardData.GetClipboardSequenceNumber())
                {
                    try { Clipboard.Clear(); } catch (Exception) { }
                }
                _transferStatus = result.Completed.Count + (move ? " moved" : " copied")
                    + (result.Skipped > 0 ? ", " + result.Skipped + " skipped" : "")
                    + (result.Canceled ? ", canceled" : "") + (result.Errors.Count > 0 ? ", " + result.Errors.Count + " failed" : "");
                if (Directory.Exists(_currentFolder)) RefreshFolder();
                UpdateStatus();
                if (result.Errors.Count > 0)
                    MessageBox.Show(this, String.Join("\n", result.Errors.Take(8))
                        + "\n\nSome items may have been partially transferred. Existing files were not overwritten.",
                        "File transfer incomplete", MessageBoxButton.OK, MessageBoxImage.Warning);
            }));
        }

        private static string RemapPath(string path, TransferredItem item)
        {
            if (String.IsNullOrEmpty(path)) return path;
            if (FileTransferService.SamePath(path, item.Source)) return item.Destination;
            if (item.IsDirectory && FileTransferService.IsWithin(path, item.Source))
                return item.Destination.TrimEnd('\\') + path.Substring(item.Source.TrimEnd('\\').Length);
            return path;
        }

        private void RemapTransferredPaths(IList<TransferredItem> items)
        {
            if (items.Count == 0) return;
            SaveWorkspaceState();
            Func<string, string> remap = CreateTransferredPathMapper(items);
            var states = _tabs.Values.Concat(new[] { _homeBrowser, _latestBrowserView }).Where(state => state != null);
            foreach (ImageTabState state in states.Distinct())
                {
                    if (state.Path != null && !String.Equals(state.Path, remap(state.Path), StringComparison.Ordinal))
                        _services.VramCache.Remove(state.Path);
                    state.Path = remap(state.Path);
                    state.FolderPath = remap(state.FolderPath);
                    if (!state.IsBrowser && state.Path != null) state.FolderPath = Path.GetDirectoryName(state.Path);
                    state.SelectedPath = remap(state.SelectedPath);
                    state.SelectedPaths = state.SelectedPaths.Select(remap).ToList();
                    state.ForwardFolders = state.ForwardFolders.Select(remap).ToList();
                }
            RefreshChangedFolderNavigation(items, new TransferredItem[0]);
            DisplayTab();
            ScheduleSessionSave();
        }
    }
}
