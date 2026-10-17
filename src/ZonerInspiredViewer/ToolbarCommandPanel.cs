using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    // The tools scroll horizontally instead of consuming another image-view row.
    internal sealed class ToolbarCommandPanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            if (InternalChildren.Count != 2) return new Size();
            InternalChildren[1].Measure(new Size(availableSize.Width, Double.PositiveInfinity));
            double width = Double.IsInfinity(availableSize.Width) ? Double.PositiveInfinity : Math.Max(0, availableSize.Width - InternalChildren[1].DesiredSize.Width);
            InternalChildren[0].Measure(new Size(width, Double.PositiveInfinity));
            Size commands = InternalChildren[0].DesiredSize, search = InternalChildren[1].DesiredSize;
            return new Size(Math.Min(availableSize.Width, commands.Width + search.Width), Math.Max(commands.Height, search.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (InternalChildren.Count != 2) return finalSize;
            double search = Math.Min(finalSize.Width, InternalChildren[1].DesiredSize.Width), commands = Math.Max(0, finalSize.Width - search);
            InternalChildren[0].Arrange(new Rect(0, 0, commands, finalSize.Height));
            InternalChildren[1].Arrange(new Rect(commands, 0, search, finalSize.Height));
            return finalSize;
        }
    }

    internal sealed class ToolbarCommandStrip : Grid
    {
        internal readonly StackPanel Commands = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly ScrollViewer _viewport;
        private readonly Button _previous, _next;
        internal ScrollViewer Viewport { get { return _viewport; } }

        internal ToolbarCommandStrip()
        {
            ClipToBounds = true;
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _previous = Arrow("\uE76B", "Previous tools", -1);
            _next = Arrow("\uE76C", "More tools", 1);
            Children.Add(_previous); Grid.SetColumn(_next, 2);
            _viewport = new ScrollViewer { Content = Commands, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, Focusable = false,
                VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0), ClipToBounds = true };
            Grid.SetColumn(_viewport, 1); Children.Add(_viewport); Children.Add(_next);
            _viewport.ScrollChanged += delegate { UpdateArrows(); };
            SizeChanged += delegate { UpdateArrows(); };
            PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (Keyboard.Modifiers != ModifierKeys.Shift || _viewport.ScrollableWidth <= 0) return;
                _viewport.ScrollToHorizontalOffset(_viewport.HorizontalOffset - e.Delta); e.Handled = true;
            };
        }
        private Button Arrow(string glyph, string name, int direction)
        {
            var button = CommandPresentation.Button(glyph, name, null, "Scroll the toolbar to reveal additional commands.",
                delegate { Scroll(direction); }, false);
            button.Width = 24; button.MinWidth = 24; button.Height = 30; button.Margin = new Thickness(0, 6, 0, 6);
            button.Focusable = false;
            button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.Visibility = Visibility.Collapsed;
            return button;
        }
        internal void Scroll(int direction)
        { _viewport.ScrollToHorizontalOffset(_viewport.HorizontalOffset + direction * Math.Max(160, _viewport.ViewportWidth * .75)); }
        private void UpdateArrows()
        {
            if (_viewport == null) return;
            bool overflow = _viewport.ExtentWidth > ActualWidth + 1;
            _previous.Visibility = _next.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
            _previous.IsEnabled = _viewport.HorizontalOffset > .5;
            _next.IsEnabled = _viewport.HorizontalOffset + .5 < _viewport.ScrollableWidth;
        }
    }
}
