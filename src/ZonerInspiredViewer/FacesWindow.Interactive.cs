using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed partial class FacesWindow
    {
        private void ApplyCanvas(BitmapSource bitmap, FaceCanvasOptions options, FaceCrop[] faces)
        {
            var fields = _tabs.SelectedIndex == 1 ? _interactive : _reference;
            SetCanvasFields(fields, options);
            _displayedCanvasOptions = options.Copy();
            _preview.Source = bitmap; _canvasSize = new Size(options.Width, options.Height); _fit = true; UpdateZoom();
            _linkedFaces = _tabs.SelectedIndex == 1 ? faces : null; _linkedCells = _linkedFaces == null ? null : FaceImages.ReferenceLayout(options, faces.Length);
            _preview.Cursor = _linkedFaces == null ? null : Cursors.Hand;
        }
        private void RefreshLinkedCanvas()
        {
            if (_busy || _closed || _canvasSnapshot != null || _tabs.SelectedIndex != 1 || _faces.Count == 0) return;
            FaceCanvasOptions options; try { options = Options(); } catch (Exception error) { _status.Text = error.Message; return; }
            var faces = _linkedFaces ?? _faces.ToArray();
            BeginWork(async token =>
            {
                var result = await Task.Run(() => FaceLinks.Refresh(faces, _workers, token, Progress), token);
                BitmapSource bitmap = result.Faces.Length == 0 ? null : await Task.Run(() => FaceImages.Preview(result.Faces, options, token), token);
                token.ThrowIfCancellationRequested();
                // Commit the new review and canvas together only after checks/rendering complete.
                var review = new FaceScanResult { Backend = _backend.Text, ExecutionEvidence = _backend.ToolTip as string }; review.Faces.AddRange(result.Faces); SetReview(review);
                if (bitmap != null) ApplyCanvas(bitmap, options, result.Faces);
                _status.Text = (bitmap == null ? "No available original images. " : "Refreshed: " + result.Faces.Length + " faces. ")
                    + "Removed " + result.RemovedFaces + " face links from " + result.UnavailablePaths.Length + " unavailable originals.";
                _status.ToolTip = result.UnavailablePaths.Length == 0 ? null : String.Join(Environment.NewLine, result.UnavailablePaths.Take(30));
            }, "Checking original links...");
        }
        private void BeginPan(Point point)
        {
            _drag = point; _dragOffset = new Point(_viewport.HorizontalOffset, _viewport.VerticalOffset); _dragged = false;
            _preview.Focus(); _preview.CaptureMouse(); _preview.Cursor = Cursors.Hand;
        }
        private void MovePan(Point point)
        {
            if (_drag == null) return;
            if (Math.Abs(point.X - _drag.Value.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(point.Y - _drag.Value.Y) > SystemParameters.MinimumVerticalDragDistance) _dragged = true;
            if (_dragged) { _viewport.ScrollToHorizontalOffset(_dragOffset.X + _drag.Value.X - point.X); _viewport.ScrollToVerticalOffset(_dragOffset.Y + _drag.Value.Y - point.Y); }
        }
        private bool CompletePan(Point point)
        {
            bool click = _drag != null && !_dragged; EndPan();
            return click && ActivateLinkedFace(point);
        }
        private void EndPan()
        {
            _drag = null; _preview.ReleaseMouseCapture();
            _preview.Cursor = _canvasSnapshot == null && _linkedFaces != null ? Cursors.Hand : null;
        }
        private void Escape()
        {
            if (_busy) _cancel.Cancel();
            else if (_framingPending || _canvasResizePending || _canvasResizeRunning) StopWork();
            else if (_canvasSnapshot != null) ReturnToCanvas();
            else Close();
        }
        private FaceCrop LinkedFaceAt(Point point)
        {
            if (_canvasSnapshot != null || _linkedFaces == null || _linkedCells == null || _preview.ActualWidth <= 0 || _preview.ActualHeight <= 0) return null;
            double x = point.X * _canvasSize.Width / _preview.ActualWidth, y = point.Y * _canvasSize.Height / _preview.ActualHeight;
            for (int i = 0; i < _linkedCells.Length; i++)
            {
                var cell = _linkedCells[i];
                if (x >= cell.X && x < cell.X + cell.Width && y >= cell.Y && y < cell.Y + cell.Height) return _linkedFaces[i];
            }
            return null;
        }
        private bool ActivateLinkedFace(Point point)
        {
            if (_busy || _closed || _framingPending || _canvasResizePending || _canvasResizeRunning) return false;
            var face = LinkedFaceAt(point); if (face == null) return false;
            string path = face.Source.Path;
            BeginWork(async token =>
            {
                BitmapSource bitmap;
                try { bitmap = await Task.Run(() => FaceImages.OpenOriginal(path, token), token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    _status.Text = "Original image not available";
                    _status.ToolTip = path + Environment.NewLine + error.GetBaseException().Message; return;
                }
                token.ThrowIfCancellationRequested(); EndPan();
                _canvasSnapshot = new CanvasSnapshot { Image = (BitmapSource)_preview.Source, Size = _canvasSize, Zoom = _zoom, Fit = _fit,
                    X = _viewport.HorizontalOffset, Y = _viewport.VerticalOffset, Status = _canvasSize.Width + " x " + _canvasSize.Height + " px / " + _linkedFaces.Length + " face crops" };
                _preview.Source = bitmap; _canvasSize = new Size(bitmap.PixelWidth, bitmap.PixelHeight); _fit = true; UpdateZoom();
                _viewport.ScrollToHorizontalOffset(0); _viewport.ScrollToVerticalOffset(0);
                _preview.Cursor = null; _backToCanvas.Visibility = Visibility.Visible;
                _status.Text = Path.GetFileName(path) + " / " + bitmap.PixelWidth + " x " + bitmap.PixelHeight + " px";
                _status.ToolTip = path; _preview.Focus();
            }, "Opening original image...");
            return true;
        }
        private void ReturnToCanvas()
        {
            if (_busy || _canvasSnapshot == null) return;
            EndPan(); var saved = _canvasSnapshot; _canvasSnapshot = null;
            _preview.Source = saved.Image; _canvasSize = saved.Size; _zoom = saved.Zoom; _fit = saved.Fit; UpdateZoom();
            _backToCanvas.Visibility = Visibility.Collapsed; _preview.Cursor = Cursors.Hand;
            _status.Text = saved.Status; _status.ToolTip = null; UpdateControls(); _preview.Focus();
            // Scroll offsets are applied after the restored canvas has its new layout.
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_closed || _canvasSnapshot != null || _preview.Source != saved.Image) return;
                _viewport.ScrollToHorizontalOffset(saved.X); _viewport.ScrollToVerticalOffset(saved.Y);
            }));
        }
    }
}
