using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private CancellationTokenSource _animationRequest;
        private DispatcherTimer _animationTimer;
        private ImageAnimation _animation;
        private int _animationFrame;
        private uint _animationLoops;

        private void StopAnimation()
        {
            if (_animationRequest != null) { _animationRequest.Cancel(); _animationRequest = null; }
            if (_animationTimer != null) _animationTimer.Stop();
            _animation = null; _animationFrame = 0; _animationLoops = 0;
        }

        private void StartAnimation(string path, int version)
        {
            if (!String.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase)) return;
            var request = new CancellationTokenSource(); _animationRequest = request;
            FileRevision revision = FileRevision.Read(path);
            long budget = Math.Min(256L, Math.Max(32L, _services.VramCache.BudgetMb / 4)) * 1024 * 1024;
            Task.Factory.StartNew(delegate
            {
                return new ModernImageDecoder(delegate { return _services.LowPriorityIccEnabled; }).ReadAnimation(path, budget, request.Token);
            }, request.Token, TaskCreationOptions.None, TaskScheduler.Default).ContinueWith(task =>
            {
                // Observe decoder failures even if the originating tab was already closed.
                Exception error = task.IsFaulted ? task.Exception.GetBaseException() : null;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    bool current = !_isClosed && _animationRequest == request && version == _imageLoadVersion
                        && path == _activeTabPath && revision.Matches(path) && !request.IsCancellationRequested;
                    if (_animationRequest == request) _animationRequest = null;
                    request.Dispose();
                    if (!current) return;
                    if (error != null)
                    {
                        _transferStatus = "GIF: first frame only (playback unavailable)";
                        _statusText.ToolTip = error.Message; UpdateStatus(); return;
                    }
                    if (task.Status != TaskStatus.RanToCompletion || task.Result == null) return;
                    _animation = task.Result; _animationFrame = 0; _animationLoops = 0;
                    _mainImage.Source = _animation.Frames[0];
                    UpdateImageNavigator();
                    if (_animationTimer == null)
                    {
                        _animationTimer = new DispatcherTimer(DispatcherPriority.Render);
                        _animationTimer.Tick += delegate { AdvanceAnimation(); };
                    }
                    _animationTimer.Interval = TimeSpan.FromMilliseconds(_animation.Delays[0]); _animationTimer.Start();
                }));
            }, TaskScheduler.Default);
        }

        private void AdvanceAnimation()
        {
            if (_isClosed || _animation == null || _imageView.Visibility != Visibility.Visible) { StopAnimation(); return; }
            int next = _animationFrame + 1;
            if (next >= _animation.Frames.Count)
            {
                _animationLoops++;
                if (_animation.Iterations > 0 && _animationLoops >= _animation.Iterations) { _animationTimer.Stop(); return; }
                next = 0;
            }
            _animationFrame = next;
            // Coalesced frames share a canvas; changing pixels must not reset zoom, pan or rotation.
            _mainImage.Source = _animation.Frames[next];
            UpdateImageNavigator();
            _animationTimer.Interval = TimeSpan.FromMilliseconds(_animation.Delays[next]);
        }
    }
}
