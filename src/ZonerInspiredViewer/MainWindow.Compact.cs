using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private UIElement _topToolbar;
        private ColumnDefinition _folderColumn;
        private ColumnDefinition _folderSplitterColumn;
        private GridSplitter _leftSplitter;
        private Button _folderTreeToggle;
        private bool _isCompactMode;
        private bool _folderTreeHidden;
        private bool _compactTreeVisible;
        private GridLength _normalFolderWidth = new GridLength(260);

        private void SetCompactMode(bool compact)
        {
            if (_isCompactMode == compact) return;
            EndImagePan();
            SaveActiveViewState();
            RememberFolderWidth();
            if (compact && (_topToolbar.IsKeyboardFocusWithin || _folderNavigation.IsKeyboardFocusWithin))
            {
                Keyboard.ClearFocus();
                if (_imageView.IsVisible) _imageCanvas.Focus();
            }
            // Compact is a window-only preference, deliberately absent from session DTOs.
            _isCompactMode = compact;
            _compactTreeVisible = false;
            _topToolbar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            if (_performanceBar != null) _performanceBar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            ApplyFolderLayout();
        }

        private bool FolderTreeVisible { get { return _isCompactMode ? _compactTreeVisible : !_folderTreeHidden; } }

        private void ToggleFolderTree()
        {
            EndImagePan();
            SaveActiveViewState();
            RememberFolderWidth();
            if (_isCompactMode) _compactTreeVisible = !_compactTreeVisible;
            else _folderTreeHidden = !_folderTreeHidden;
            ApplyFolderLayout();
            ScheduleSessionSave();
        }

        private void RememberFolderWidth()
        {
            if (_folderNavigation.Visibility == Visibility.Visible && _folderColumn.ActualWidth >= 100)
                _normalFolderWidth = new GridLength(_folderColumn.ActualWidth);
        }

        private double FolderPaneWidth()
        {
            RememberFolderWidth();
            return _normalFolderWidth.Value;
        }

        private void RestoreFolderLayout(SessionState state)
        {
            _folderTreeHidden = state.FolderTreeHidden;
            _normalFolderWidth = new GridLength(ImageViewport.IsFinite(state.FolderPaneWidth)
                && state.FolderPaneWidth >= 100 ? System.Math.Min(800, state.FolderPaneWidth) : 260);
            ApplyFolderLayout();
        }

        private void ApplyFolderLayout()
        {
            bool visible = FolderTreeVisible;
            if (!visible && _folderNavigation.IsKeyboardFocusWithin)
            {
                Keyboard.ClearFocus();
                if (_imageView.IsVisible) _imageCanvas.Focus();
            }
            _folderNavigation.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _leftSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _folderTreeToggle.Visibility = !_isCompactMode || visible ? Visibility.Visible : Visibility.Collapsed;
            _folderColumn.Width = visible ? _normalFolderWidth : new GridLength(0);
            _folderSplitterColumn.Width = new GridLength(!_isCompactMode || visible ? 24 : 0);
            UpdateFolderTreeToggle();
            QueueImageView();
        }

        private void UpdateFolderTreeToggle()
        {
            _folderTreeToggle.Content = FolderTreeVisible ? "\uE76B" : "\uE76C";
            string label = FolderTreeVisible ? "Hide folder tree" : "Show folder tree";
            CommandPresentation.Describe(_folderTreeToggle, label, "J", "Toggle the folder navigation panel without changing the current folder.");
            AutomationProperties.SetName(_folderTreeToggle, label);
        }
    }
}
