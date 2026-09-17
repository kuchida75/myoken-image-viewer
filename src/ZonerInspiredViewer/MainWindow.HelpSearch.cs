using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private HelpWindow _helpWindow;
        private AdvancedSearchWindow _advancedSearchWindow;
        private Button _helpButton, _advancedSearchButton;

        private void ShowHelp()
        {
            if (_helpWindow != null) { _helpWindow.Activate(); return; }
            _helpWindow = new HelpWindow { Owner = this, Icon = Icon };
            _helpWindow.RefreshShortcuts(_shortcuts);
            _helpWindow.Closed += delegate { _helpWindow = null; };
            _helpWindow.Show();
        }

        private void ShowAdvancedSearch()
        {
            if (String.IsNullOrWhiteSpace(_currentFolder)) return;
            if (_advancedSearchWindow != null)
            {
                if (String.Equals(_advancedSearchWindow.RootFolder, RecursiveSearchIndex.CanonicalPath(_currentFolder), StringComparison.OrdinalIgnoreCase))
                { _advancedSearchWindow.Activate(); return; }
                _advancedSearchWindow.Close();
            }
            _advancedSearchWindow = new AdvancedSearchWindow(_services, _currentFolder, _searchBox.Text,
                _thumbnailSizePixels, _thumbnailAspectRatio, delegate(ImageFileItem item, bool parent)
                {
                    if (_isClosed) return;
                    StopSlideshow();
                    if (parent) OpenFolderTab(Path.GetDirectoryName(item.Path));
                    else if (item.IsDirectory) OpenFolderTab(item.Path);
                    else OpenImageTab(item.Path, true);
                    Activate();
                }, _searchHistory) { Owner = this, Icon = Icon };
            _advancedSearchWindow.Closed += delegate { _advancedSearchWindow = null; };
            _advancedSearchWindow.Show();
        }
    }
}
