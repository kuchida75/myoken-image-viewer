using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private DispatcherTimer _rightDoubleClickTimer;
        private bool _rightClickCandidate, _rightClickMoved, _ignoreRightClickSequence;
        private int _rightClickCount;
        private Point _rightClickStart;
        private long _rightClickPressedAt;
        private string _rightDoubleClickTab, _rightDoubleClickPath;

        private bool BeginRightButtonInput(Point point, int clickCount)
        {
            bool ignored = clickCount != 1 && _ignoreRightClickSequence;
            if (!BeginMouseGesture(point)) { CancelRightClickAction(); return false; }
            _ignoreRightClickSequence = ignored;
            _rightClickCount = clickCount;
            _rightClickStart = point;
            _rightClickPressedAt = Stopwatch.GetTimestamp();
            _rightClickMoved = false;
            _rightClickCandidate = true;
            return true;
        }

        private void TrackRightClickMovement(Point point)
        {
            if (_rightClickCandidate && (Math.Abs(point.X - _rightClickStart.X) > SystemParameters.MinimumHorizontalDragDistance
                || Math.Abs(point.Y - _rightClickStart.Y) > SystemParameters.MinimumVerticalDragDistance)) _rightClickMoved = true;
        }

        private void CompleteRightButtonInput(Point point)
        {
            if (!_rightGestureActive) { CancelRightClickAction(); return; }
            TrackRightClickMovement(point);
            int count = _rightClickCount;
            double heldMilliseconds = (Stopwatch.GetTimestamp() - _rightClickPressedAt) * 1000.0 / Stopwatch.Frequency;
            bool click = _rightClickCandidate && !_rightClickMoved && !_ignoreRightClickSequence && !_rightGestureWheelUsed
                && heldMilliseconds <= System.Windows.Forms.SystemInformation.DoubleClickTime && _imageCanvas.IsMouseCaptured
                && _mouseGesturesEnabled && _imageView.IsVisible && HasCurrentImage() && new Rect(_imageCanvas.RenderSize).Contains(point);
            bool gesture = CompleteMouseGesture(point);
            if (!click || gesture) return;
            _ignoreRightClickSequence = false;
            if (count == 2)
            {
                if (_rightDoubleClickTimer == null)
                {
                    _rightDoubleClickTimer = new DispatcherTimer();
                    _rightDoubleClickTimer.Tick += delegate { ApplyPendingRightDoubleClick(); };
                }
                _rightDoubleClickTab = _activeTabId;
                _rightDoubleClickPath = _activeTabPath;
                // A third click gets the OS double-click interval before the double-click action runs.
                _rightDoubleClickTimer.Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime + 20);
                _rightDoubleClickTimer.Start();
            }
            else if (count == 3)
            {
                bool running = _slideshowPlaying && !_slideshowPaused;
                bool resumed = _slideshowPlaying && _slideshowPaused;
                if (_folderScanPending || _fileTransferBusy || _backupBusy || _rotationSaving || _enhancementSaving) return;
                if (!_slideshowPlaying) StartSlideshow();
                else if (_slideshowPaused) ToggleSlideshowPause();
                if (_slideshowPlaying) ShowMouseGestureFeedback(resumed ? ImageMouseGesture.SlideshowResume
                    : running ? ImageMouseGesture.SlideshowRunning : ImageMouseGesture.SlideshowStart, true);
            }
        }

        private void ApplyPendingRightDoubleClick()
        {
            bool apply = !_isClosed && _mouseGesturesEnabled && !_rightGestureActive && _imageView.IsVisible && HasCurrentImage()
                && _activeTabId == _rightDoubleClickTab && _activeTabPath == _rightDoubleClickPath;
            CancelRightClickAction();
            if (!apply) return;
            ToggleZoomLock();
            ShowMouseGestureFeedback(ImageMouseGesture.ZoomLock, true);
        }

        private void CancelRightClickAction()
        {
            if (_rightDoubleClickTimer != null) _rightDoubleClickTimer.Stop();
            _rightDoubleClickTab = null; _rightDoubleClickPath = null;
            _rightClickCandidate = false;
            _ignoreRightClickSequence = true;
        }
    }
}
