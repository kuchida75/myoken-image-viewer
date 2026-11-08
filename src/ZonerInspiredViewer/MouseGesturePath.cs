using System;
using System.Windows;

namespace ZonerInspiredViewer
{
    // Keep bounded state instead of retaining every mouse sample.
    internal sealed class MouseGesturePath
    {
        private readonly Point _start;
        private Point _corner, _extreme;
        private ImageMouseGesture _first, _rotation;
        private bool _invalid;

        internal MouseGesturePath(Point start) { _start = start; }

        internal ImageMouseGesture Track(Point point)
        {
            if (!ImageViewport.IsFinite(point.X) || !ImageViewport.IsFinite(point.Y)) _invalid = true;
            if (_invalid) return ImageMouseGesture.None;
            if (_rotation != ImageMouseGesture.None)
            {
                var next = MainWindow.ClassifyMouseGesture(_extreme, point);
                var continuing = _rotation == ImageMouseGesture.RotateLeft ? ImageMouseGesture.FitWidth : ImageMouseGesture.FitHeight;
                if (next != ImageMouseGesture.None && next != continuing)
                { _invalid = true; return ImageMouseGesture.None; }
                if ((_rotation == ImageMouseGesture.RotateLeft && point.X < _extreme.X)
                    || (_rotation == ImageMouseGesture.RotateRight && point.X > _extreme.X)) _extreme = point;
                return _rotation;
            }

            var straight = MainWindow.ClassifyMouseGesture(_start, point);
            if (_first == ImageMouseGesture.None)
            {
                _first = straight;
                if (_first == ImageMouseGesture.FitWindow) _corner = point;
                return straight;
            }
            if (_first == ImageMouseGesture.FitWindow)
            {
                var turn = MainWindow.ClassifyMouseGesture(_corner, point);
                if (turn == ImageMouseGesture.FitWidth || turn == ImageMouseGesture.FitHeight)
                {
                    _rotation = turn == ImageMouseGesture.FitWidth ? ImageMouseGesture.RotateLeft : ImageMouseGesture.RotateRight;
                    _extreme = point;
                    return _rotation;
                }
                // Follow a continuing upward stroke while allowing a little vertical hand jitter at a corner.
                if (point.Y < _corner.Y - 12) _corner = point;
            }
            return straight;
        }
    }
}
