using System.Windows;
using System.Windows.Controls;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Grid _thumbnailWorkspace;

        private Grid BuildThumbnailWorkspace()
        {
            _thumbnailWorkspace = new Grid();
            _thumbnailWorkspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _thumbnailWorkspace.Children.Add(_thumbnailGrid);
            return _thumbnailWorkspace;
        }
    }
}
