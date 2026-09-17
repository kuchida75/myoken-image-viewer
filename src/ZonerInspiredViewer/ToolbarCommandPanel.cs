using System;
using System.Windows;
using System.Windows.Controls;

namespace ZonerInspiredViewer
{
    // Keeps the search group at the trailing edge, including when it needs a second row.
    internal sealed class ToolbarCommandPanel : Panel
    {
        private double _naturalCommandWidth;
        protected override Size MeasureOverride(Size availableSize)
        {
            if (InternalChildren.Count != 2) return new Size();
            foreach (UIElement child in InternalChildren) child.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            _naturalCommandWidth = InternalChildren[0].DesiredSize.Width;
            InternalChildren[0].Measure(new Size(availableSize.Width, Double.PositiveInfinity));
            Size commands = InternalChildren[0].DesiredSize, search = InternalChildren[1].DesiredSize;
            bool wrap = _naturalCommandWidth + search.Width > availableSize.Width;
            return new Size(Math.Min(availableSize.Width, _naturalCommandWidth + search.Width),
                wrap ? commands.Height + search.Height : Math.Max(commands.Height, search.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (InternalChildren.Count != 2) return finalSize;
            Size commands = InternalChildren[0].DesiredSize, search = InternalChildren[1].DesiredSize;
            bool wrap = _naturalCommandWidth + search.Width > finalSize.Width;
            InternalChildren[0].Arrange(new Rect(0, 0, Math.Min(_naturalCommandWidth, finalSize.Width), commands.Height));
            InternalChildren[1].Arrange(new Rect(Math.Max(0, finalSize.Width - search.Width), wrap ? commands.Height : 0,
                Math.Min(search.Width, finalSize.Width), search.Height));
            return finalSize;
        }
    }
}
