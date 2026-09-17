using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private string _browserFocusTabId;

        private static bool IsThumbnailNavigationKey(Key key)
        {
            return key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down
                || key == Key.Home || key == Key.End || key == Key.PageUp || key == Key.PageDown;
        }

        private bool CanNavigateThumbnails()
        {
            return !_folderNavigation.IsKeyboardFocusWithin && !_metadataPanel.IsKeyboardFocusWithin
                && !_sortFieldBox.IsKeyboardFocusWithin && !_thumbnailSizeSlider.IsKeyboardFocusWithin
                && !_enhanceMode.IsKeyboardFocusWithin
                && !_thumbnailAspectSlider.IsKeyboardFocusWithin && !_vramSlider.IsKeyboardFocusWithin;
        }

        private void RevealReturnedThumbnail()
        {
            if (_browserFocusTabId == null || _activeTabId != _browserFocusTabId || !CurrentTabState().IsBrowser) return;
            string id = _browserFocusTabId;
            _browserFocusTabId = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                if (_isClosed || _activeTabId != id || !CurrentTabState().IsBrowser || _folderScanPending) return;
                if (_folderLocation == null || !_folderLocation.IsEditing) _thumbnailGrid.Focus();
                _thumbnailGrid.BringSelectionIntoView();
            }));
        }
    }
}
