using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private const string DefaultPinnedTabColor = "#2F78A6";
        private string _pinnedTabColor = DefaultPinnedTabColor;
        private ScrollViewer _tabScroller;
        private double _idealTabWidth = 96;
        private double _activeTabWidth = 96;

        internal static bool IsValidTabColor(string color)
        {
            uint value;
            return color != null && color.Length == 7 && color[0] == '#'
                && UInt32.TryParse(color.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        private static Brush TabColorBrush(string color)
        {
            uint rgb = UInt32.Parse(color.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            brush.Freeze(); return brush;
        }

        private void SetPinnedTabColor(string color)
        {
            _pinnedTabColor = IsValidTabColor(color) ? color.ToUpperInvariant() : DefaultPinnedTabColor;
            var brush = (SolidColorBrush)TabColorBrush(_pinnedTabColor);
            Color c = brush.Color;
            double light = (LinearColor(c.R) * 0.2126 + LinearColor(c.G) * 0.7152 + LinearColor(c.B) * 0.0722);
            Brush text = light > 0.179 ? Brushes.Black : Brushes.White;
            Resources["Tab.Pinned.Fill"] = brush;
            Resources["Tab.Pinned.Active"] = brush;
            Resources["Tab.Pinned.Accent"] = text;
            Resources["Tab.Pinned.Text"] = text;
            if (_pinnedColorPreview != null) { _pinnedColorPreview.Background = brush; _pinnedColorText.Text = _pinnedTabColor; }
            ScheduleSessionSave();
        }

        private static double LinearColor(byte value)
        {
            double c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        private static string TabLabel(ImageTabState state)
        {
            return state.IsBrowser ? "Folder: " + Path.GetFileName((state.FolderPath ?? "").TrimEnd('\\'))
                : Path.GetFileName(state.Path);
        }

        private void MeasureTabWidth()
        {
            double narrowest = Double.PositiveInfinity;
            _activeTabWidth = 96;
            foreach (string id in _tabOrder)
            {
                ImageTabState state;
                if (!_tabs.TryGetValue(id, out state)) continue;
                var text = new FormattedText(TabLabel(state) ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), FontSize, Brushes.Black,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                double normalWidth = Math.Max(96, Math.Min(280, Math.Ceiling(text.WidthIncludingTrailingWhitespace + 50)));
                narrowest = Math.Min(narrowest, normalWidth);
                if (String.Equals(id, _activeTabId, StringComparison.OrdinalIgnoreCase)) _activeTabWidth = normalWidth;
            }
            _idealTabWidth = Double.IsPositiveInfinity(narrowest) ? 96 : Math.Max(96, Math.Min(280, Math.Ceiling(narrowest)));
        }

        private void UpdateTabWidths()
        {
            if (_tabStrip == null || _tabScroller == null) return;
            var tabs = _tabStrip.Children.OfType<Border>().Where(tab => tab.Tag is string).ToArray();
            Border active = tabs.FirstOrDefault(tab => String.Equals((string)tab.Tag, _activeTabId, StringComparison.OrdinalIgnoreCase));
            int inactiveCount = tabs.Length - (active == null ? 0 : 1);
            double width = _idealTabWidth;
            if (inactiveCount > 0 && _tabScroller.ActualWidth > 0)
            {
                double available = _tabScroller.ActualWidth - _newTabButton.Width - _newTabButton.Margin.Left - _newTabButton.Margin.Right
                    - _tabStrip.Children.OfType<Border>().Where(tab => tab.Tag is TabStackDto).Sum(tab => tab.Width + tab.Margin.Left + tab.Margin.Right)
                    - tabs.Sum(tab => tab.Margin.Left + tab.Margin.Right) - (active == null ? 0 : _activeTabWidth);
                width = Math.Max(96, Math.Min(width, Math.Floor(available / inactiveCount)));
            }
            foreach (Border tab in tabs) tab.Width = tab == active ? _activeTabWidth : width;
        }

        private void RevealActiveTab()
        {
            string id = _activeTabId;
            if (id == null) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                if (_isClosed || !String.Equals(id, _activeTabId, StringComparison.OrdinalIgnoreCase)) return;
                Border tab = _tabStrip.Children.OfType<Border>().FirstOrDefault(item => Object.Equals(item.Tag, id));
                ImageTabState state;
                if (tab == null && _tabs.TryGetValue(id, out state) && state.StackId != null)
                    tab = _tabStrip.Children.OfType<Border>().FirstOrDefault(item => item.Tag is TabStackDto
                        && String.Equals(((TabStackDto)item.Tag).Id, state.StackId, StringComparison.OrdinalIgnoreCase));
                if (tab != null) tab.BringIntoView();
            }));
        }
    }
}
