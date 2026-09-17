using System;
using System.Windows;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal struct ImageView
    {
        public double Zoom;
        public double X;
        public double Y;
    }

    internal static class ImageViewport
    {
        public static int NormalizeRotation(int turns)
        {
            return ((turns % 4) + 4) % 4;
        }

        public static Size RotatedSize(Size image, int turns)
        {
            return NormalizeRotation(turns) % 2 == 0 ? image : new Size(image.Height, image.Width);
        }

        public static Matrix RotationMatrix(Size image, int turns)
        {
            // Rotate into positive image coordinates before viewport zoom and pan.
            switch (NormalizeRotation(turns))
            {
                case 1: return new Matrix(0, 1, -1, 0, image.Height, 0);
                case 2: return new Matrix(-1, 0, 0, -1, image.Width, image.Height);
                case 3: return new Matrix(0, -1, 1, 0, 0, image.Width);
                default: return Matrix.Identity;
            }
        }

        public static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        public static ImageView Fit(Size image, Size viewport)
        {
            double zoom = Math.Min(1, Math.Min(viewport.Width / image.Width, viewport.Height / image.Height));
            return new ImageView
            {
                Zoom = zoom,
                X = (viewport.Width - image.Width * zoom) / 2,
                Y = (viewport.Height - image.Height * zoom) / 2
            };
        }

        public static ImageView Center(Size image, Size viewport, double zoom)
        {
            zoom = IsFinite(zoom) && zoom > 0 ? Math.Max(0.02, Math.Min(64, zoom)) : 1;
            return new ImageView { Zoom = zoom, X = (viewport.Width - image.Width * zoom) / 2,
                Y = (viewport.Height - image.Height * zoom) / 2 };
        }

        public static ImageView FitAxis(Size image, Size viewport, bool width)
        {
            return Center(image, viewport, width ? viewport.Width / image.Width : viewport.Height / image.Height);
        }

        public static ImageView Restore(Size image, Size viewport, ImageTabState state)
        {
            if (!state.HasCustomView || !IsFinite(state.Zoom) || state.Zoom <= 0
                || !IsFinite(state.OffsetX) || !IsFinite(state.OffsetY))
            {
                return Fit(image, viewport);
            }

            double x = state.OffsetX;
            double y = state.OffsetY;
            // Keep the same image point at the center when a tab's frame changes size.
            if (IsFinite(state.ViewportWidth) && state.ViewportWidth > 1)
                x += (viewport.Width - state.ViewportWidth) / 2;
            if (IsFinite(state.ViewportHeight) && state.ViewportHeight > 1)
                y += (viewport.Height - state.ViewportHeight) / 2;
            return Constrain(image, viewport, Math.Max(0.02, Math.Min(64, state.Zoom)), x, y);
        }

        public static ImageView Constrain(Size image, Size viewport, double zoom, double x, double y)
        {
            return new ImageView
            {
                Zoom = zoom,
                X = ConstrainAxis(image.Width * zoom, viewport.Width, x),
                Y = ConstrainAxis(image.Height * zoom, viewport.Height, y)
            };
        }

        private static double ConstrainAxis(double imageLength, double frameLength, double offset)
        {
            if (imageLength <= frameLength || !IsFinite(offset))
                return (frameLength - imageLength) / 2;
            return Math.Max(frameLength - imageLength, Math.Min(0, offset));
        }
    }
}
