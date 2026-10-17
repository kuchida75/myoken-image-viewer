using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private List<string> _faceCanvasPaths = new List<string>();
        private string _lastFaceCanvas;
        private bool _includeFaceCanvasesInBackup = true;
        private int _faceRestoreVersion;
        private CheckBox _includeCanvasBackupCheck;
        private void RememberFaceCanvas(string path)
        {
            _faceCanvasPaths.RemoveAll(p => String.Equals(p, path, StringComparison.OrdinalIgnoreCase)); _faceCanvasPaths.Insert(0, path);
            _faceCanvasPaths = _faceCanvasPaths.Take(64).ToList(); _lastFaceCanvas = path; ScheduleSessionSave();
        }
        private void CaptureFaceSessions(SessionState state)
        {
            state.FaceCanvasPaths = new List<string>(_faceCanvasPaths); state.LastFaceCanvas = _lastFaceCanvas;
            state.IncludeFaceCanvasesInBackup = _includeFaceCanvasesInBackup;
        }
        private void RestoreFaceSessions(SessionState state, bool openLastCanvas)
        {
            _faceCanvasPaths = (state.FaceCanvasPaths ?? new List<string>()).Where(p => FaceCanvasStore.ValidPath(p) && FaceCanvasStore.IsPath(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToList();
            _lastFaceCanvas = FaceCanvasStore.ValidPath(state.LastFaceCanvas) && FaceCanvasStore.IsPath(state.LastFaceCanvas) ? state.LastFaceCanvas : null;
            _includeFaceCanvasesInBackup = state.IncludeFaceCanvasesInBackup ?? true;
            if (_includeCanvasBackupCheck != null) _includeCanvasBackupCheck.IsChecked = _includeFaceCanvasesInBackup;
            int revision = ++_faceRestoreVersion; string last = _lastFaceCanvas;
            if (last == null || !openLastCanvas) return;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += delegate
            {
                if (_isClosed || revision != _faceRestoreVersion) { timer.Stop(); return; }
                if (!IsLoaded || _backupBusy || _fileTransferBusy || _rotationSaving || _enhancementSaving) return;
                timer.Stop();
                if (File.Exists(last)) ShowFacesForPath(_currentFolder, false, last);
                else { _lastFaceCanvas = null; _statusText.Text = "Saved face canvas is not available: " + Path.GetFileName(last); ScheduleSessionSave(); }
            };
            timer.Start();
        }
        private MenuItem SavedFaceCanvasesMenu()
        {
            var menu = new MenuItem { Header = "Saved face canvases" };
            foreach (string path in _faceCanvasPaths)
            {
                string file = path; var item = new MenuItem { Header = Path.GetFileName(path), ToolTip = path, IsEnabled = File.Exists(path) };
                item.Click += delegate { ShowFacesForPath(_currentFolder, false, file); }; menu.Items.Add(item);
            }
            if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "No saved canvases", IsEnabled = false });
            return menu;
        }
    }
}
