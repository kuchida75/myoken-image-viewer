using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ZonerInspiredViewer
{
    internal sealed partial class FacesWindow
    {
        private bool _canvasResizePending, _canvasResizeRunning;
        private int _canvasResizeVersion;
        private CancellationTokenSource _canvasResizeCancel;
        private CanvasFields _canvasResizeFields;
        private FaceCanvasOptions _displayedCanvasOptions;

        private void QueueCanvasResize(CanvasFields fields)
        {
            if (_closed || _closeRequested || _busy || _framingPending || _canvasSnapshot != null || _faces.Count == 0) return;
            _canvasResizeFields = fields; _canvasResizePending = true; ++_canvasResizeVersion;
            if (_canvasResizeCancel != null) _canvasResizeCancel.Cancel();
            _status.Text = "Updating canvas...";
            if (!_canvasResizeRunning)
            {
                _canvasResizeRunning = true;
                PendingWork = Dispatcher.InvokeAsync(new Func<Task>(RenderCanvasResizes)).Task.Unwrap();
            }
            UpdateControls();
        }
        private void CancelCanvasResize(bool restore = false)
        {
            ++_canvasResizeVersion; _canvasResizePending = false;
            if (_canvasResizeCancel != null) _canvasResizeCancel.Cancel();
            if (restore && _displayedCanvasOptions != null && _canvasSnapshot == null)
                SetCanvasFields(_tabs.SelectedIndex == 1 ? _interactive : _reference, _displayedCanvasOptions);
        }
        private async Task RenderCanvasResizes()
        {
            try
            {
                // One cached preview at a time; newer input cancels and supersedes the old size.
                while (_canvasResizePending && !_closed && !_closeRequested && !_busy && !_framingPending)
                {
                    int version = _canvasResizeVersion; var fields = _canvasResizeFields;
                    using (var cancel = new CancellationTokenSource())
                    {
                        _canvasResizeCancel = cancel;
                        try
                        {
                            await Task.Delay(125, cancel.Token);
                            var options = ReadOptions(false); var faces = _faces.ToArray();
                            var bitmap = await Task.Run(() => FaceImages.Preview(faces, options, cancel.Token), cancel.Token);
                            cancel.Token.ThrowIfCancellationRequested();
                            if (_closed || _closeRequested || _busy || _framingPending || version != _canvasResizeVersion
                                || fields != (_tabs.SelectedIndex == 1 ? _interactive : _reference) || _canvasSnapshot != null) continue;
                            _canvasResizePending = false; EndPan(); ApplyCanvas(bitmap, options, faces);
                            _status.Text = options.Width + " x " + options.Height + " px / " + faces.Length + " face crops"; _status.ToolTip = null;
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception error)
                        {
                            if (version == _canvasResizeVersion)
                            {
                                CancelCanvasResize(true); _status.Text = error.GetBaseException().Message; _status.ToolTip = _status.Text;
                            }
                        }
                        finally { _canvasResizeCancel = null; }
                    }
                }
            }
            finally
            {
                _canvasResizeRunning = false;
                if (!_closed) { UpdateControls(); if (_closeRequested && !_busy) Close(); }
            }
        }
    }
}
