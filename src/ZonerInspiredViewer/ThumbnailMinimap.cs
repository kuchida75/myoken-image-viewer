using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed class MinimapLayout
    {
        internal const int MaximumPreviews = 768;
        public int Count, Columns, SourceColumns, FirstIndex, LastIndex;
        public double Width, Height, CellWidth, RowHeight, ContentHeight, MapOffset;
        public double SourceRowHeight, SourceViewport, MaximumScroll, Progress, DragTravel;
        public Rect Viewport;

        public static MinimapLayout Create(int count, int sourceColumns, double sourceRowHeight,
            double sourceViewport, double sourceOffset, double width, double height)
        {
            var layout = new MinimapLayout { Count = Math.Max(0, count), SourceColumns = Math.Max(1, sourceColumns),
                SourceRowHeight = Math.Max(1, sourceRowHeight), SourceViewport = Math.Max(0, sourceViewport),
                Width = Math.Max(0, width), Height = Math.Max(0, height), LastIndex = -1 };
            layout.Columns = Math.Max(3, Math.Min(8, (int)(layout.Width / 24)));
            layout.CellWidth = layout.Width / layout.Columns;
            layout.RowHeight = Math.Max(18, Math.Max(layout.CellWidth * 0.72,
                layout.Height * layout.Columns / (MaximumPreviews - layout.Columns * 4)));
            if (layout.Count == 0 || width <= 1 || height <= 1) return layout;
            double sourceHeight = Math.Max(layout.SourceViewport,
                Math.Ceiling(count / (double)layout.SourceColumns) * layout.SourceRowHeight);
            layout.MaximumScroll = Math.Max(0, sourceHeight - layout.SourceViewport);
            layout.Progress = layout.MaximumScroll > 0 ? Clamp(sourceOffset / layout.MaximumScroll, 0, 1) : 0;
            layout.ContentHeight = Math.Ceiling(count / (double)layout.Columns) * layout.RowHeight;
            // The miniature document scrolls proportionally while its viewport band travels down the rail.
            layout.MapOffset = layout.Progress * Math.Max(0, layout.ContentHeight - layout.Height);
            double track = Math.Min(layout.Height, layout.ContentHeight);
            double thumbHeight = Math.Min(track, Math.Max(16, sourceHeight > 0
                ? layout.SourceViewport / sourceHeight * layout.ContentHeight : track));
            layout.DragTravel = Math.Max(0, track - thumbHeight);
            layout.Viewport = new Rect(1, layout.Progress * layout.DragTravel, Math.Max(0, width - 2), thumbHeight);
            layout.FirstIndex = Math.Max(0, ((int)Math.Floor(layout.MapOffset / layout.RowHeight) - 1) * layout.Columns);
            layout.LastIndex = Math.Min(count - 1,
                ((int)Math.Ceiling((layout.MapOffset + layout.Height) / layout.RowHeight) + 1) * layout.Columns - 1);
            return layout;
        }

        public Rect Bounds(int index)
        {
            return new Rect(index % Columns * CellWidth + 1, index / Columns * RowHeight - MapOffset + 1,
                Math.Max(0, CellWidth - 2), Math.Max(0, RowHeight - 2));
        }

        public int ItemAt(Point point)
        {
            if (Count == 0 || CellWidth <= 0 || point.X < 0 || point.X >= Width || point.Y < 0 || point.Y >= Height) return -1;
            int index = (int)Math.Floor((point.Y + MapOffset) / RowHeight) * Columns + (int)(point.X / CellWidth);
            return index >= 0 && index < Count ? index : -1;
        }

        public double CenterItemOffset(int index)
        {
            return Clamp((index / SourceColumns + 0.5) * SourceRowHeight - SourceViewport / 2, 0, MaximumScroll);
        }

        internal static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
    }

    internal sealed class ThumbnailMinimap : FrameworkElement, IDisposable
    {
        private sealed class Preview
        {
            public int Index;
            public ImageFileItem Item;
            public BitmapSource Bitmap;
            public CancellationTokenSource Request;
            public bool Finished;
        }

        public static readonly DependencyProperty BackgroundProperty = BrushProperty("Background");
        public static readonly DependencyProperty TileBrushProperty = BrushProperty("TileBrush");
        public static readonly DependencyProperty BorderBrushProperty = BrushProperty("BorderBrush");
        public static readonly DependencyProperty AccentBrushProperty = BrushProperty("AccentBrush");
        public static readonly DependencyProperty FolderBrushProperty = BrushProperty("FolderBrush");
        private readonly VirtualizedThumbnailGrid _source;
        private readonly ThumbnailCacheService _cache;
        private readonly Dictionary<int, Preview> _previews = new Dictionary<int, Preview>();
        private readonly Queue<Preview> _pending = new Queue<Preview>();
        private readonly DispatcherTimer _refreshTimer, _loadTimer;
        private MinimapLayout _layout;
        private int _activeLoads;
        private bool _disposed, _dragging;
        private double _lastDragY, _dragProgress;

        public ThumbnailMinimap(VirtualizedThumbnailGrid source, ThumbnailCacheService cache)
        {
            _source = source; _cache = cache;
            ClipToBounds = true; Cursor = Cursors.Hand; AllowDrop = true;
            AutomationProperties.SetName(this, "Thumbnail minimap (experimental)");
            ToolTip = "Thumbnail minimap (experimental, T)";
            ThemeManager.Bind(this, BackgroundProperty, ThemeKeys.PaneBackground);
            ThemeManager.Bind(this, TileBrushProperty, ThemeKeys.TileImageBackground);
            ThemeManager.Bind(this, BorderBrushProperty, ThemeKeys.Border);
            ThemeManager.Bind(this, AccentBrushProperty, ThemeKeys.ActiveThumbnailBorder);
            ThemeManager.Bind(this, FolderBrushProperty, "Folder.Fill");
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.LowQuality);
            _refreshTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _refreshTimer.Tick += delegate { _refreshTimer.Stop(); Refresh(); };
            _loadTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(65) };
            _loadTimer.Tick += delegate { _loadTimer.Stop(); PumpLoads(); };
            _source.ItemsChanged += SourceItemsChanged;
            _source.ScrollChanged += SourceScrollChanged;
            SizeChanged += delegate { EndDrag(); ScheduleRefresh(); };
            Loaded += delegate { ScheduleRefresh(); };
            Unloaded += delegate { Release(); };
            IsVisibleChanged += delegate { if (IsVisible) ScheduleRefresh(); else Release(); };
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { BeginDrag(e.GetPosition(this)); e.Handled = true; };
            MouseMove += delegate(object sender, MouseEventArgs e)
            {
                Point point = e.GetPosition(this);
                if (_dragging) { if (e.LeftButton == MouseButtonState.Pressed) DragTo(point); else EndDrag(); }
                else if (_layout != null)
                {
                    ImageFileItem item = _source.ItemAt(_layout.ItemAt(point));
                    ToolTip = item == null ? "Thumbnail minimap (experimental, T)" : item.Name + "\n" + item.Path;
                }
            };
            MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { EndDrag(); e.Handled = true; };
            LostMouseCapture += delegate { EndDrag(); };
            MouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                double distance = SystemParameters.WheelScrollLines < 0 ? _source.ViewportHeight : 16 * SystemParameters.WheelScrollLines;
                _source.ScrollToVerticalOffset(_source.VerticalOffset - e.Delta / 120.0 * distance);
                e.Handled = true;
            };
            PreviewDragOver += delegate(object sender, DragEventArgs e) { e.Effects = DragDropEffects.None; e.Handled = true; };
            PreviewDrop += delegate(object sender, DragEventArgs e) { e.Effects = DragDropEffects.None; e.Handled = true; };
        }

        private static DependencyProperty BrushProperty(string name)
        {
            return DependencyProperty.Register(name, typeof(Brush), typeof(ThumbnailMinimap),
                new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
        }

        private void SourceItemsChanged(object sender, EventArgs e)
        {
            EndDrag(); ClearPreviews(); _layout = null; InvalidateVisual(); ScheduleRefresh();
        }

        private void SourceScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0 || e.ExtentHeightChange != 0) EndDrag();
            ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            if (_disposed || !IsLoaded || !IsVisible) return;
            if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
        }

        private void Refresh()
        {
            if (_disposed || !IsVisible || !IsLoaded) return;
            _layout = MinimapLayout.Create(_source.ItemCount, _source.ColumnCount, _source.TileHeight,
                _source.ViewportHeight, _source.VerticalOffset, ActualWidth, ActualHeight);
            foreach (int index in _previews.Keys.Where(index => index < _layout.FirstIndex || index > _layout.LastIndex).ToArray())
            {
                Cancel(_previews[index]); _previews.Remove(index);
            }
            for (int i = _layout.FirstIndex; i <= _layout.LastIndex; i++)
            {
                if (_previews.ContainsKey(i)) continue;
                ImageFileItem item = _source.ItemAt(i);
                if (item != null) _previews.Add(i, new Preview { Index = i, Item = item, Finished = item.IsDirectory });
            }
            _pending.Clear();
            double center = (_source.VerticalOffset + _source.ViewportHeight / 2) / Math.Max(1, _source.TileHeight) * _source.ColumnCount;
            foreach (Preview preview in _previews.Values.Where(preview => !preview.Finished && preview.Request == null)
                .OrderBy(preview => Math.Abs(preview.Index - center))) _pending.Enqueue(preview);
            if (_pending.Count > 0 && !_loadTimer.IsEnabled) _loadTimer.Start();
            InvalidateVisual();
        }

        private void PumpLoads()
        {
            if (_disposed || !IsVisible || !IsLoaded || _loadTimer.IsEnabled) return;
            while (_activeLoads < 4 && _pending.Count > 0)
            {
                Preview preview = _pending.Dequeue(), current;
                if (preview.Finished || preview.Request != null || !_previews.TryGetValue(preview.Index, out current) || current != preview) continue;
                LoadPreview(preview);
            }
        }

        private async void LoadPreview(Preview preview)
        {
            var request = new CancellationTokenSource(); preview.Request = request; _activeLoads++;
            try
            {
                BitmapSource bitmap = await _cache.GetThumbnailAsync(preview.Item, 64, request.Token);
                Preview current;
                if (!_disposed && IsVisible && !request.IsCancellationRequested
                    && _previews.TryGetValue(preview.Index, out current) && current == preview) preview.Bitmap = bitmap;
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally
            {
                preview.Finished = true; preview.Request = null; request.Dispose(); _activeLoads--;
                if (!_disposed) { InvalidateVisual(); PumpLoads(); }
            }
        }

        private static void Cancel(Preview preview)
        {
            if (preview.Request != null) preview.Request.Cancel();
            preview.Bitmap = null;
        }

        private void ClearPreviews()
        {
            _loadTimer.Stop(); _pending.Clear();
            foreach (Preview preview in _previews.Values) Cancel(preview);
            _previews.Clear();
        }

        private void Release()
        {
            EndDrag(); _refreshTimer.Stop(); ClearPreviews(); _layout = null; InvalidateVisual();
        }

        internal bool BeginDrag(Point point)
        {
            if (_disposed || !IsVisible) return false;
            Refresh();
            if (_layout == null || _layout.MaximumScroll <= 0 || _layout.DragTravel <= 0) return false;
            int index = _layout.ItemAt(point);
            bool onViewport = _layout.Viewport.Contains(point);
            if (index < 0 && !onViewport) return false;
            _source.Focus();
            double target = onViewport ? _source.VerticalOffset : _layout.CenterItemOffset(index);
            _source.ScrollToVerticalOffset(target);
            _dragProgress = target / _layout.MaximumScroll; _lastDragY = point.Y;
            _dragging = CaptureMouse();
            ToolTip = null;
            return _dragging;
        }

        internal void DragTo(Point point)
        {
            if (!_dragging || _layout == null) return;
            _dragProgress = MinimapLayout.Clamp(_dragProgress + (point.Y - _lastDragY) / Math.Max(1, _layout.DragTravel), 0, 1);
            _lastDragY = point.Y;
            _source.ScrollToVerticalOffset(_dragProgress * _layout.MaximumScroll);
        }

        internal void EndDrag()
        {
            _dragging = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            dc.DrawRectangle((Brush)GetValue(BackgroundProperty), null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (_layout == null) return;
            Brush tile = (Brush)GetValue(TileBrushProperty), border = (Brush)GetValue(BorderBrushProperty);
            var edge = new Pen(border, 1);
            foreach (Preview preview in _previews.Values)
            {
                Rect rect = _layout.Bounds(preview.Index);
                dc.DrawRectangle(tile, null, rect);
                if (preview.Item.IsDirectory)
                {
                    Brush folder = (Brush)GetValue(FolderBrushProperty);
                    dc.DrawRectangle(folder, null, new Rect(rect.X + 2, rect.Y + 2, rect.Width * 0.4, 3));
                    dc.DrawRectangle(folder, edge, new Rect(rect.X + 2, rect.Y + 5, Math.Max(0, rect.Width - 4), Math.Max(0, rect.Height - 7)));
                }
                else if (preview.Bitmap != null)
                {
                    double scale = Math.Min(rect.Width / preview.Bitmap.PixelWidth, rect.Height / preview.Bitmap.PixelHeight);
                    double width = preview.Bitmap.PixelWidth * scale, height = preview.Bitmap.PixelHeight * scale;
                    dc.DrawImage(preview.Bitmap, new Rect(rect.X + (rect.Width - width) / 2, rect.Y + (rect.Height - height) / 2, width, height));
                }
                else if (preview.Finished)
                    dc.DrawLine(edge, new Point(rect.Left + 3, rect.Bottom - 3), new Point(rect.Right - 3, rect.Top + 3));
            }
            if (!_layout.Viewport.IsEmpty && _layout.Viewport.Height > 0)
            {
                Brush accent = (Brush)GetValue(AccentBrushProperty);
                dc.PushOpacity(0.15); dc.DrawRectangle(accent, null, _layout.Viewport); dc.Pop();
                dc.DrawRectangle(null, new Pen(accent, 2), _layout.Viewport);
            }
            dc.DrawLine(edge, new Point(0.5, 0), new Point(0.5, ActualHeight));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; Release();
            _source.ItemsChanged -= SourceItemsChanged;
            _source.ScrollChanged -= SourceScrollChanged;
        }
    }
}
