using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal static class WindowPlacement
    {
        public static Rect CurrentWorkArea(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return SystemParameters.WorkArea;
            HwndSource source = HwndSource.FromHwnd(handle);
            Matrix transform = source.CompositionTarget.TransformFromDevice;
            return new Rect(transform.Transform(new Point(info.Work.Left, info.Work.Top)),
                transform.Transform(new Point(info.Work.Right, info.Work.Bottom)));
        }

        internal static Rect VerticalBounds(Rect current, Rect workArea)
        {
            return new Rect(current.Left, workArea.Top, current.Width, workArea.Height);
        }

        public static Rect Restore(Window window, SessionState state)
        {
            var areas = new List<Rect>();
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
            MonitorCallback callback = delegate(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data)
            {
                var info = new MonitorInfo();
                info.Size = Marshal.SizeOf(typeof(MonitorInfo));
                if (GetMonitorInfo(monitor, ref info))
                {
                    Point topLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
                    Point bottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
                    Rect area = new Rect(topLeft, bottomRight);
                    if ((info.Flags & 1) != 0) areas.Insert(0, area);
                    else areas.Add(area);
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            if (areas.Count == 0) areas.Add(SystemParameters.WorkArea);

            double width = ImageViewport.IsFinite(state.WindowWidth) && state.WindowWidth > 400
                ? Math.Max(window.MinWidth, state.WindowWidth) : window.Width;
            double height = ImageViewport.IsFinite(state.WindowHeight) && state.WindowHeight > 300
                ? Math.Max(window.MinHeight, state.WindowHeight) : window.Height;
            bool positioned = state.WindowPositionSaved || state.WindowLeft != 0 || state.WindowTop != 0;
            double left = positioned && ImageViewport.IsFinite(state.WindowLeft)
                ? state.WindowLeft : areas[0].Left + (areas[0].Width - width) / 2;
            double top = positioned && ImageViewport.IsFinite(state.WindowTop)
                ? state.WindowTop : areas[0].Top + (areas[0].Height - height) / 2;
            Rect bounds = Constrain(new Rect(left, top, width, height), areas);
            window.MinWidth = Math.Min(window.MinWidth, bounds.Width);
            window.MinHeight = Math.Min(window.MinHeight, bounds.Height);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = bounds.Left;
            window.Top = bounds.Top;
            window.Width = bounds.Width;
            window.Height = bounds.Height;
            window.WindowState = state.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
            return bounds;
        }

        internal static Rect Constrain(Rect bounds, IList<Rect> workAreas)
        {
            Rect best = workAreas[0];
            double bestArea = 0;
            foreach (Rect area in workAreas)
            {
                Rect overlap = Rect.Intersect(bounds, area);
                double visible = overlap.IsEmpty ? 0 : overlap.Width * overlap.Height;
                if (visible > bestArea) { best = area; bestArea = visible; }
            }
            double width = Math.Min(bounds.Width, best.Width);
            double height = Math.Min(bounds.Height, best.Height);
            return new Rect(
                Math.Max(best.Left, Math.Min(bounds.Left, best.Right - width)),
                Math.Max(best.Top, Math.Min(bounds.Top, best.Bottom - height)), width, height);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }
        private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }
}
