using System;
using System.IO;
using System.Windows;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private FacesWindow _facesWindow;
        private void ShowFaces()
        { ShowFacesForSelection(false); }
        private void ShowFacesForSelection(bool autoScan)
        {
            var selected = _browserView.IsVisible && !_folderNavigation.IsKeyboardFocusWithin ? _thumbnailGrid.SelectedItem : null;
            ShowFacesForPath(selected != null && _thumbnailGrid.SelectedPaths.Contains(selected.Path) ? selected.Path : _currentFolder, autoScan);
        }
        private void ShowFacesForPath(string path, bool autoScan = false, string canvasFile = null)
        {
            if (_facesWindow != null) { _facesWindow.Activate(); return; }
            if (_fileTransferBusy || _backupBusy || _rotationSaving || _enhancementSaving) return;
            StopSlideshow();
            string folder = Directory.Exists(path) ? path : File.Exists(path) ? Path.GetDirectoryName(path) : _currentFolder;
            var dialog = new FacesWindow(folder, _services.CpuWorkerCount, _processingGpuKey, _processingGpuLimitMb, _services.Thumbnails) { Owner = this, Icon = Icon };
            dialog.CanvasFileChanged += RememberFaceCanvas;
            if (canvasFile != null) dialog.Loaded += delegate { dialog.OpenCanvasFile(canvasFile); };
            if (autoScan) dialog.Loaded += delegate { dialog.StartScan(); };
            _facesWindow = dialog; _fileTransferBusy = true; _configurationContent.IsEnabled = false; UpdateStatus();
            try { dialog.ShowDialog(); }
            finally { _facesWindow = null; _fileTransferBusy = false; _configurationContent.IsEnabled = true; UpdateStatus(); ScheduleSessionSave(); }
            if (!_isClosed && dialog.RequestedOriginal != null && File.Exists(dialog.RequestedOriginal)) OpenImageTab(dialog.RequestedOriginal, true);
            else if (_browserView.IsVisible) _thumbnailGrid.Focus(); else _imageCanvas.Focus();
        }
    }
}
