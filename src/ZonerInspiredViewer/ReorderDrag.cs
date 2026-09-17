using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed class ReorderItem
    {
        public string Key;
        public FrameworkElement Element;
    }

    internal static class ReorderList
    {
        public static bool Move(IList<string> list, string key, int insertionSlot)
        {
            if (insertionSlot < 0 || insertionSlot > list.Count) return false;
            int before = -1;
            for (int i = 0; i < list.Count; i++) if (String.Equals(list[i], key, StringComparison.OrdinalIgnoreCase)) { before = i; break; }
            if (before < 0) return false;
            int after = insertionSlot > before ? insertionSlot - 1 : insertionSlot;
            if (before == after) return false;
            list.RemoveAt(before); list.Insert(after, key); return true;
        }
    }

    internal sealed class ReorderDrag : IDisposable
    {
        private readonly FrameworkElement _host;
        private readonly bool _horizontal;
        private readonly Func<IList<ReorderItem>> _items;
        private readonly Func<string, int, bool> _move;
        private readonly Func<ScrollViewer> _scroll;
        private readonly Func<Point, bool> _accept;
        private readonly string _format = "Viewer.InternalOrder." + Guid.NewGuid().ToString("N");
        private readonly DispatcherTimer _timer;
        private FrameworkElement _pressed;
        private string _pressedKey, _dragKey, _token;
        private Point _start, _position;
        private Action _click;
        private AdornerLayer _layer;
        private InsertionAdorner _marker;
        private bool _disposed, _capturing;
        internal Func<DependencyObject, IDataObject, DragDropEffects, DragDropEffects> RunDrag = DragDrop.DoDragDrop;

        public ReorderDrag(FrameworkElement host, bool horizontal, Func<IList<ReorderItem>> items,
            Func<string, int, bool> move, Func<ScrollViewer> scroll, Func<Point, bool> accept = null)
        {
            _host = host; _horizontal = horizontal; _items = items; _move = move; _scroll = scroll; _accept = accept;
            _host.AllowDrop = true;
            _host.DragOver += DragOver;
            _host.Drop += Drop;
            _host.DragLeave += DragLeave;
            _host.QueryContinueDrag += QueryContinueDrag;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _timer.Tick += delegate { ScrollEdge(); };
        }

        public void Bind(FrameworkElement handle, string key, Action click)
        {
            handle.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (_disposed || IsButton(e.OriginalSource as DependencyObject, handle)) return;
                BeginPress(handle, key, e.GetPosition(_host), click); e.Handled = true;
            };
            handle.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!_capturing && _pressed == handle && MovePointer(e.GetPosition(_host), e.LeftButton == MouseButtonState.Pressed)) e.Handled = true;
            };
            handle.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (_pressed != handle) return;
                Point point = e.GetPosition(handle);
                EndPress(new Rect(handle.RenderSize).Contains(point)); e.Handled = true;
            };
            handle.LostMouseCapture += delegate { if (_pressed == handle) EndPress(false); };
            handle.Unloaded += delegate { if (_pressed == handle) EndPress(false); };
        }

        internal void BeginPress(FrameworkElement handle, string key, Point position, Action click)
        {
            EndPress(false);
            if (_disposed) return;
            _pressed = handle; _pressedKey = key; _start = position; _click = click;
            // Capture can synchronously raise a pointer move before the press has finished.
            _capturing = true;
            try { if (!handle.CaptureMouse()) EndPress(false); }
            finally { _capturing = false; }
        }

        internal bool MovePointer(Point position, bool leftDown)
        {
            if (_pressed == null) return false;
            if (!leftDown) { EndPress(false); return false; }
            if (Math.Abs(position.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(position.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance) return false;
            string key = _pressedKey;
            EndPress(false);
            if (_disposed || !_items().Any(item => item.Key == key)) return false;
            _dragKey = key; _token = Guid.NewGuid().ToString("N");
            var data = new DataObject(); data.SetData(_format, _token, false);
            try { RunDrag(_host, data, DragDropEffects.Move); }
            finally
            {
                _dragKey = null; _token = null; ClearMarker();
                if (_disposed) _host.QueryContinueDrag -= QueryContinueDrag;
            }
            return true;
        }

        internal void EndPress(bool clicked)
        {
            FrameworkElement handle = _pressed; Action action = _click;
            _pressed = null; _pressedKey = null; _click = null;
            if (handle != null && handle.IsMouseCaptured) handle.ReleaseMouseCapture();
            if (clicked && !_disposed && action != null) action();
        }

        private static bool IsButton(DependencyObject element, DependencyObject stop)
        {
            while (element != null && element != stop)
            {
                if (element is ButtonBase) return true;
                element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            }
            return false;
        }

        private bool Owns(IDataObject data)
        {
            return !_disposed && _token != null && data.GetDataPresent(_format, false)
                && String.Equals(data.GetData(_format, false) as string, _token, StringComparison.Ordinal);
        }

        private void DragOver(object sender, DragEventArgs e)
        {
            if (!Owns(e.Data)) return;
            e.Effects = Preview(e.Data, e.GetPosition(_host)) >= 0 ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void Drop(object sender, DragEventArgs e)
        {
            if (!Owns(e.Data)) return;
            e.Effects = Commit(e.Data, e.GetPosition(_host)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void DragLeave(object sender, DragEventArgs e) { if (Owns(e.Data)) ClearMarker(); }
        private void QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
        {
            if (_disposed || e.EscapePressed) { e.Action = DragAction.Cancel; ClearMarker(); e.Handled = true; }
        }

        internal int Preview(IDataObject data, Point position)
        {
            if (!Owns(data)) return -1;
            _position = position;
            int slot = FindSlot(position, true);
            if (slot < 0) ClearMarker(); else _timer.Start();
            return slot;
        }

        internal bool Commit(IDataObject data, Point position)
        {
            if (!Owns(data)) return false;
            int slot = FindSlot(position, false);
            ClearMarker();
            return slot >= 0 && _move(_dragKey, slot);
        }

        private int FindSlot(Point position, bool mark)
        {
            if (_disposed || !new Rect(_host.RenderSize).Contains(position) || (_accept != null && !_accept(position))) return -1;
            IList<ReorderItem> items = _items();
            if (!items.Any(item => item.Key == _dragKey)) return -1;
            Rect anchor = Rect.Empty;
            int slot = 0;
            bool after = false;
            for (int i = 0; i < items.Count; i++)
            {
                FrameworkElement element = items[i].Element;
                if (!element.IsVisible || element.ActualHeight <= 0 || element.ActualWidth <= 0) continue;
                Rect bounds = element.TransformToAncestor(_host).TransformBounds(new Rect(element.RenderSize));
                anchor = bounds; slot = i;
                if ((_horizontal ? position.X < bounds.X + bounds.Width / 2 : position.Y < bounds.Y + bounds.Height / 2)) { after = false; break; }
                slot = i + 1; after = true;
            }
            if (anchor.IsEmpty) return -1;
            if (mark) ShowMarker(anchor, after);
            return slot;
        }

        internal void ScrollEdge()
        {
            if (_token == null || _disposed) { ClearMarker(); return; }
            ScrollViewer scroller = _scroll == null ? null : _scroll();
            if (scroller == null) return;
            double position = _horizontal ? _position.X : _position.Y;
            double extent = _horizontal ? _host.ActualWidth : _host.ActualHeight;
            int direction = position < 28 ? -1 : position > extent - 28 ? 1 : 0;
            if (direction == 0) return;
            if (_horizontal) scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + direction * 20);
            else scroller.ScrollToVerticalOffset(scroller.VerticalOffset + direction * (scroller.CanContentScroll ? 1 : 20));
            _host.UpdateLayout(); if (FindSlot(_position, true) < 0) ClearMarker();
        }

        private void ShowMarker(Rect bounds, bool after)
        {
            if (_marker == null)
            {
                _layer = AdornerLayer.GetAdornerLayer(_host);
                if (_layer == null) return;
                _marker = new InsertionAdorner(_host); _layer.Add(_marker);
            }
            _marker.Line = _horizontal
                ? new Rect(Math.Max(1, Math.Min(_host.ActualWidth - 3, after ? bounds.Right : bounds.Left)) - 1, Math.Max(0, bounds.Top), 3, bounds.Height)
                : new Rect(Math.Max(0, bounds.Left), Math.Max(1, Math.Min(_host.ActualHeight - 3, after ? bounds.Bottom : bounds.Top)) - 1, bounds.Width, 3);
            _marker.InvalidateVisual();
        }

        private void ClearMarker()
        {
            _timer.Stop();
            if (_marker != null) { _layer.Remove(_marker); _marker = null; _layer = null; }
        }

        public void Dispose()
        {
            _disposed = true; EndPress(false); ClearMarker();
            _host.DragOver -= DragOver; _host.Drop -= Drop; _host.DragLeave -= DragLeave;
            // Keep the cancellation handler for a currently running native drag until its source is released.
            if (_token == null) _host.QueryContinueDrag -= QueryContinueDrag;
        }

        private sealed class InsertionAdorner : Adorner
        {
            public Rect Line;
            public InsertionAdorner(UIElement adorned) : base(adorned) { IsHitTestVisible = false; }
            protected override void OnRender(DrawingContext context)
            {
                context.PushClip(new RectangleGeometry(new Rect(AdornedElement.RenderSize)));
                context.DrawRectangle(Application.Current.TryFindResource(ThemeKeys.Accent) as Brush ?? Brushes.DeepSkyBlue, null, Line);
                context.Pop();
            }
        }

        internal static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            var found = root as ScrollViewer;
            if (found != null) return found;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }
    }
}
