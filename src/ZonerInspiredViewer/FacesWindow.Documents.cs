using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ZonerInspiredViewer
{
    internal sealed partial class FacesWindow
    {
        private Button _saveCanvas, _openCanvas;
        internal string CanvasPath { get; private set; }
        internal event Action<string> CanvasFileChanged;
        private void SaveCanvas()
        {
            var picker = new SaveFileDialog { Title = "Save interactive face canvas", Filter = "Myoken face canvas|*.myokenfaces", DefaultExt = ".myokenfaces", AddExtension = true,
                OverwritePrompt = true, FileName = CanvasPath == null ? "faces.myokenfaces" : Path.GetFileName(CanvasPath), InitialDirectory = CanvasPath == null ? null : Path.GetDirectoryName(CanvasPath) };
            if (ThemedFileDialogs.Show(picker, this, _folderThumbnails)) SaveCanvasFile(picker.FileName);
        }
        private void OpenCanvas()
        {
            var picker = new OpenFileDialog { Title = "Open interactive face canvas", Filter = "Myoken face canvas|*.myokenfaces", CheckFileExists = true };
            if (ThemedFileDialogs.Show(picker, this, _folderThumbnails)) OpenCanvasFile(picker.FileName);
        }
        internal void SaveCanvasFile(string path)
        {
            if (_busy || _framingPending || _canvasResizePending || _canvasResizeRunning || _canvasSnapshot != null || _tabs.SelectedIndex != 1 || _preview.Source == null) return;
            FaceCanvasOptions options; try { options = ReadOptions(false); } catch (Exception error) { _status.Text = error.Message; return; }
            var project = new FaceCanvasProject { Faces = _faces.ToArray(), Options = options, Folder = _folder.Text, Recursive = _includeSubfolders.IsChecked == true,
                Fit = _fit, Zoom = _zoom, X = _viewport.HorizontalOffset, Y = _viewport.VerticalOffset };
            BeginWork(async token =>
            {
                await Task.Run(() => FaceCanvasStore.Save(path, project, token), token); SetCanvasPath(path); _status.Text = "Canvas saved: " + Path.GetFileName(path);
            }, "Saving interactive canvas...");
        }
        internal void OpenCanvasFile(string path)
        {
            if (_busy || _framingPending || _canvasSnapshot != null) return;
            BeginWork(async token =>
            {
                var project = await Task.Run(() => FaceCanvasStore.Load(path), token);
                var bitmap = await Task.Run(() => FaceImages.Preview(project.Faces, project.Options, token), token); token.ThrowIfCancellationRequested();
                _folder.Text = project.Folder ?? ""; _includeSubfolders.IsChecked = project.Recursive;
                var review = new FaceScanResult { Backend = "Saved canvas - no detection required" }; review.Faces.AddRange(project.Faces); SetReview(review);
                _tabs.SelectedIndex = 1; SetCanvasFields(_interactive, project.Options);
                ApplyCanvas(bitmap, project.Options, project.Faces); _fit = project.Fit; _zoom = project.Zoom; UpdateZoom();
                await Dispatcher.InvokeAsync(new Action(delegate { if (!_closed && _canvasSnapshot == null) { _viewport.ScrollToHorizontalOffset(project.X); _viewport.ScrollToVerticalOffset(project.Y); } }), System.Windows.Threading.DispatcherPriority.Loaded);
                SetCanvasPath(path); _status.Text = "Canvas opened: " + project.Faces.Length + " face links. Originals are checked when opened or refreshed.";
            }, "Opening interactive canvas...");
        }
        private void SetCanvasPath(string path)
        {
            CanvasPath = Path.GetFullPath(path); Title = "Faces - " + Path.GetFileName(CanvasPath);
            if (CanvasFileChanged != null) CanvasFileChanged(CanvasPath);
        }
    }
}
