using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private BatchWindow _batchWindow;
        private List<BatchPreset> _batchPresets = new List<BatchPreset>();
        private Button _batchButton;

        private ContextMenu BuildBatchMenu()
        {
            var menu = new ContextMenu(); bool enabled = SelectedFilePaths().Length > 0 && !_fileTransferBusy && !_backupBusy && !_rotationSaving && !_enhancementSaving;
            foreach (var kind in new[] { BatchKind.Rename, BatchKind.Resize, BatchKind.Convert })
            {
                BatchKind selected = kind;
                var item = WorkflowMenuItem(kind == BatchKind.Convert ? "Batch convert format..." : "Batch " + kind.ToString().ToLowerInvariant() + "...",
                    kind == BatchKind.Rename ? "\uE8AC" : kind == BatchKind.Resize ? "\uE740" : "\uE8AB", null, delegate { ShowBatch(selected); }, enabled);
                item.Tag = "batch-" + kind.ToString().ToLowerInvariant(); menu.Items.Add(item);
            }
            return menu;
        }
        private MenuItem BatchMenuItem()
        {
            var group = new MenuItem { Header = "Batch", Icon = CommandPresentation.Label("\uE8F1", null), IsEnabled = SelectedFilePaths().Length > 0 && !_fileTransferBusy };
            var menu = BuildBatchMenu(); var children = menu.Items.Cast<MenuItem>().ToArray(); menu.Items.Clear();
            foreach (var child in children) group.Items.Add(child); return group;
        }
        private void ShowBatch(BatchKind kind)
        {
            if (_fileTransferBusy || _backupBusy || _rotationSaving || _enhancementSaving || _batchWindow != null) return;
            string[] paths = SelectedFilePaths(); if (paths.Length == 0) return;
            StopSlideshow(); SaveWorkspaceState(); _rescanTimer.Stop();
            if (_scanCancellation != null) _scanCancellation.Cancel();
            _sortVersion++; _folderScanPending = false; _isScanningFolder = false;
            var dialog = new BatchWindow(kind, paths, _services.AppDataRoot, _batchPresets, ScheduleSessionSave, _services.Thumbnails.Invalidate) { Owner = this, Icon = Icon };
            _batchWindow = dialog; _fileTransferBusy = true; _configurationContent.IsEnabled = false; UpdateStatus();
            try { dialog.ShowDialog(); }
            finally
            {
                _batchWindow = null; _fileTransferBusy = false; _configurationContent.IsEnabled = true;
                if (dialog.Result != null)
                {
                    ApplyBatchResult(kind, dialog.Result);
                    _transferStatus = dialog.Result.Completed.Count + (kind == BatchKind.Rename ? " renamed" : " written")
                        + ", " + dialog.Result.Skipped + " skipped, " + dialog.Result.Errors.Count + " errors" + (dialog.Result.Canceled ? ", canceled" : "");
                }
                if (Directory.Exists(_currentFolder)) RefreshFolder(); UpdateStatus(); ScheduleSessionSave();
                if (_browserView.IsVisible) _thumbnailGrid.Focus(); else _imageCanvas.Focus();
            }
        }
        private void ApplyBatchResult(BatchKind kind, BatchResult result)
        {
            if (kind == BatchKind.Rename) RemapTransferredPaths(result.Completed);
            else
            {
                foreach (var item in result.Completed) _services.VramCache.Remove(item.Destination);
                if (_activeTabPath != null && result.Completed.Any(item => FileTransferService.SamePath(item.Destination, _activeTabPath)))
                { LoadImage(_activeTabPath); LoadMetadata(_activeTabPath); }
            }
        }
        private static Func<string, string> CreateTransferredPathMapper(IList<TransferredItem> items)
        {
            var exact = items.ToDictionary(item => item.Source.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase);
            return delegate(string path)
            {
                if (String.IsNullOrEmpty(path)) return path;
                TransferredItem item;
                if (exact.TryGetValue(path.TrimEnd('\\'), out item)) return item.Destination;
                for (string parent = Path.GetDirectoryName(path); !String.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                    if (exact.TryGetValue(parent.TrimEnd('\\'), out item) && item.IsDirectory)
                        return item.Destination.TrimEnd('\\') + path.Substring(item.Source.TrimEnd('\\').Length);
                return path;
            };
        }
    }
}
