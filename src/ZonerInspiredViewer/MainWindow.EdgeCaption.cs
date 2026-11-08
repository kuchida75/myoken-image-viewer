using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private WindowChrome _edgeCaptionChrome, _previousWindowChrome;
        private Thickness _edgeCaptionOriginalMargin;
        private bool _edgeCaptionInsetsQueued;

        private void BuildEdgeCaption()
        {
            StateChanged += delegate { QueueEdgeCaptionInsets(); };
            SizeChanged += delegate { QueueEdgeCaptionInsets(); };
            LocationChanged += delegate { QueueEdgeCaptionInsets(); };
        }

        private void UpdateEdgeCaption()
        {
            if (_edgeRoot == null) return;
            bool enabled = _edgeActive && !_isFullscreen;
            if (enabled && _edgeCaptionChrome == null)
            {
                _previousWindowChrome = WindowChrome.GetWindowChrome(this);
                _edgeCaptionOriginalMargin = _edgeRoot.Margin;
                _edgeCaptionChrome = new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(0),
                    ResizeBorderThickness = SystemParameters.WindowResizeBorderThickness, CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false };
                WindowChrome.SetWindowChrome(this, _edgeCaptionChrome);
            }
            else if (!enabled && _edgeCaptionChrome != null)
            {
                WindowChrome.SetWindowChrome(this, _previousWindowChrome);
                _edgeRoot.Margin = _edgeCaptionOriginalMargin;
                _edgeCaptionChrome = null; _previousWindowChrome = null;
            }
            QueueEdgeCaptionInsets();
            PositionEdgeBars();
        }

        private void QueueEdgeCaptionInsets()
        {
            if (_edgeCaptionChrome == null || _edgeCaptionInsetsQueued || _isClosed) return;
            _edgeCaptionInsetsQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                _edgeCaptionInsetsQueued = false;
                if (_edgeCaptionChrome == null || _isClosed) return;
                Thickness inset = new Thickness();
                var source = PresentationSource.FromVisual(this);
                if (WindowState == WindowState.Maximized && source != null && source.CompositionTarget != null)
                {
                    // Windows keeps invisible resize borders outside a maximized monitor work area.
                    // Exclude only those off-screen pixels, so image fitting uses the actual visible frame.
                    Rect work = WindowPlacement.CurrentWorkArea(this);
                    Matrix toDevice = source.CompositionTarget.TransformToDevice;
                    Point workStart = toDevice.Transform(work.TopLeft), workEnd = toDevice.Transform(work.BottomRight);
                    Point start = PointToScreen(new Point()), end = PointToScreen(new Point(ActualWidth, ActualHeight));
                    inset = new Thickness(Math.Max(0, workStart.X - start.X) / toDevice.M11,
                        Math.Max(0, workStart.Y - start.Y) / toDevice.M22,
                        Math.Max(0, end.X - workEnd.X) / toDevice.M11,
                        Math.Max(0, end.Y - workEnd.Y) / toDevice.M22);
                }
                _edgeRoot.Margin = new Thickness(_edgeCaptionOriginalMargin.Left + inset.Left, _edgeCaptionOriginalMargin.Top + inset.Top,
                    _edgeCaptionOriginalMargin.Right + inset.Right, _edgeCaptionOriginalMargin.Bottom + inset.Bottom);
            }));
        }
    }
}
