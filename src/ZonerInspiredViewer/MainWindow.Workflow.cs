using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private WrapPanel _workflowCommands, _viewerNavigationCommands, _viewerZoomCommands, _viewerEditCommands;
        private Button _fileMenuButton, _viewMenuButton, _clearSearchButton, _fitImageButton;
        private ToggleButton _performanceToggle;
        private StackPanel _performanceDetails;
        private FrameworkElement _performanceBar;
        private TextBlock _performanceSummary;
        private FolderLocationBar _folderLocation;

        private static WrapPanel WorkflowGroup()
        { return new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 12, 2) }; }

        private void OpenFolderPicker()
        {
            var dialog = new FolderPickerWindow(_currentFolder, _services.Thumbnails) { Title = "Open a folder", Owner = this, Icon = Icon };
            dialog.ShowDialog(); if (dialog.SelectedFolder != null) NavigateFolderAddress(dialog.SelectedFolder);
        }

        private bool NavigateFolderAddress(string path)
        {
            if (_fileTransferBusy || _backupBusy || _rotationSaving || _enhancementSaving || !Directory.Exists(path)) return false;
            LoadFolder(path); return true;
        }

        private void FocusFolderAddress()
        {
            if (_isCompactMode) SetCompactMode(false);
            if (_imageView.Visibility == Visibility.Visible) BrowseActiveTab();
            _folderLocation.SetPath(_currentFolder); _folderLocation.BeginEdit();
        }

        private void ClearFolderSearch()
        { _searchBox.Clear(); _searchTimer.Stop(); ApplySearch(); _searchBox.Focus(); }

        private MenuItem WorkflowMenuItem(string name, string glyph, string command, Action action, bool enabled, string description = null)
        {
            var item = new MenuItem { Header = name, IsEnabled = enabled, Icon = CommandPresentation.Label(glyph, null) };
            if (command != null) { string keys = _shortcuts.Display(command); item.InputGestureText = keys == "Unassigned" ? "" : keys; }
            CommandPresentation.Describe(item, name, item.InputGestureText, description ?? name);
            item.Click += delegate { action(); }; return item;
        }

        private void OpenWorkflowMenu(Button owner, ContextMenu menu)
        { menu.PlacementTarget = owner; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; }

        private ContextMenu BuildFileActionsMenu()
        {
            var menu = new ContextMenu();
            string[] paths = SelectedFilePaths();
            bool ready = !_fileTransferBusy && !_backupBusy && !_rotationSaving && !_enhancementSaving;
            bool selected = paths.Length > 0 && ready;
            menu.Items.Add(WorkflowMenuItem("Copy", "\uE8C8", "copy", delegate { CopySelectedFiles(false); }, selected));
            menu.Items.Add(WorkflowMenuItem("Cut", "\uE8C6", "cut", delegate { CopySelectedFiles(true); }, selected));
            menu.Items.Add(WorkflowMenuItem("Paste into this folder", "\uE77F", "paste", delegate { PasteFiles(_currentFolder); }, ready && CanPasteFiles()));
            menu.Items.Add(new Separator());
            menu.Items.Add(WorkflowMenuItem("Rename...", "\uE8AC", "rename", RenameCurrent, selected && paths.Length == 1, "Rename the displayed image or one selected file or folder."));
            menu.Items.Add(BatchMenuItem());
            menu.Items.Add(WorkflowMenuItem("Move to folder...", "\uE8DE", null, MoveCurrent, selected, "Move selected files and folders to another folder. Existing items are never overwritten."));
            menu.Items.Add(WorkflowMenuItem("Move to Recycle Bin", "\uE74D", "delete", DeleteCurrent, selected, "Recycle selected files and folders. In viewer mode, advance to the next image without closing the tab."));
            menu.Items.Add(new Separator());
            menu.Items.Add(WorkflowMenuItem("Faces reference and canvas...", "\uE716", "faces", ShowFaces, ready));
            menu.Items.Add(new Separator());
            menu.Items.Add(WorkflowMenuItem("Show in Explorer", "\uED25", null, ShowCurrentInExplorer, !String.IsNullOrEmpty(WorkflowPath())));
            menu.Items.Add(WorkflowMenuItem("Copy full path", "\uE8C8", null, CopyCurrentPath, !String.IsNullOrEmpty(WorkflowPath())));
            return menu;
        }

        private string WorkflowPath()
        { return SelectedFilePaths().FirstOrDefault() ?? _currentFolder; }

        private void CopyCurrentPath()
        {
            try
            {
                string path = WorkflowPath(); if (String.IsNullOrEmpty(path)) return;
                Clipboard.SetText(path); _transferStatus = "Full path copied"; UpdateBrowserFooter();
            }
            catch (Exception error) { ThemedMessageBox.Show(this, error.Message, "Clipboard unavailable"); }
        }

        private void ShowCurrentInExplorer()
        {
            try
            {
                string path = WorkflowPath();
                if (String.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) return;
                string arguments = SelectedFilePaths().Length > 0 ? "/select,\"" + path + "\"" : "\"" + path + "\"";
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), arguments) { UseShellExecute = false });
            }
            catch (Exception error) { ThemedMessageBox.Show(this, error.Message, "Explorer unavailable"); }
        }

        private ContextMenu BuildViewOptionsMenu()
        {
            var menu = new ContextMenu();
            AddViewToggle(menu, "Folder tree", "\uE8B7", "tree", FolderTreeVisible, ToggleFolderTree);
            AddViewToggle(menu, "Metadata panel", "\uE946", null, _metadataPanel.Visibility == Visibility.Visible,
                delegate { _metadataToggle.IsChecked = _metadataToggle.IsChecked != true; });
            AddViewToggle(menu, "Thumbnail minimap", "\uE8A5", "minimap", _minimapVisible, delegate { SetThumbnailMinimap(!_minimapVisible, true); });
            menu.Items.Add(new Separator());
            AddViewToggle(menu, "Image information", "\uE946", "metadata", _imageMetadataOverlayEnabled, delegate { SetImageMetadataOverlay(!_imageMetadataOverlayEnabled); });
            AddViewToggle(menu, "Enhancement status", "\uE790", null, _enhancementStatusEnabled, delegate { SetEnhancementStatusVisible(!_enhancementStatusEnabled); });
            AddViewToggle(menu, "Pan navigator", "\uE9D9", "navigator", _imageNavigatorEnabled, delegate { SetImageNavigator(!_imageNavigatorEnabled); });
            menu.Items.Add(new Separator());
            AddViewToggle(menu, "Compact mode", "\uE8A0", "compact", _isCompactMode, delegate { SetCompactMode(!_isCompactMode); });
            AddViewToggle(menu, "Fullscreen", "\uE740", "fullscreen", _isFullscreen, ToggleFullscreen);
            AddViewToggle(menu, "Maximum window height", "\uE74A", null, _isVerticallyMaximized, delegate { SetVerticalHeight(!_isVerticallyMaximized); });
            AddViewToggle(menu, "Performance details", "\uE9D9", null, _performanceToggle.IsChecked == true,
                delegate { _performanceToggle.IsChecked = _performanceToggle.IsChecked != true; });
            return menu;
        }

        private void AddViewToggle(ContextMenu menu, string name, string glyph, string shortcut, bool value, Action action)
        {
            var item = WorkflowMenuItem(name, glyph, shortcut, action, true); item.IsCheckable = true; item.IsChecked = value; menu.Items.Add(item);
        }

        private FrameworkElement BuildPerformanceBar()
        {
            var band = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
            ThemeManager.Bind(band, Border.BackgroundProperty, ThemeKeys.ToolbarBackground);
            ThemeManager.Bind(band, Border.BorderBrushProperty, ThemeKeys.Border);
            var content = new StackPanel(); band.Child = content;
            var header = new DockPanel { Margin = new Thickness(8, 2, 8, 2) };
            _performanceToggle = new ToggleButton { Height = 26, Width = 122, Margin = new Thickness(0, 0, 14, 0) };
            CommandPresentation.Describe(_performanceToggle, "Performance details", null, "Show or hide CPU, image cache and system-wide GPU memory readings.");
            _performanceSummary = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            ThemeManager.Bind(_performanceSummary, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            header.Children.Add(_performanceToggle); header.Children.Add(_performanceSummary); content.Children.Add(header);
            _performanceDetails = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
            _performanceDetails.Children.Add(_statusText); _performanceDetails.Children.Add(_gpuStatusPanel); content.Children.Add(_performanceDetails);
            _performanceToggle.Checked += delegate { UpdatePerformanceDisclosure(); };
            _performanceToggle.Unchecked += delegate { UpdatePerformanceDisclosure(); };
            UpdatePerformanceDisclosure(); return band;
        }

        private void UpdatePerformanceDisclosure()
        {
            bool visible = _performanceToggle.IsChecked == true;
            _performanceToggle.Content = CommandPresentation.Label(visible ? "\uE70E" : "\uE70D", "Performance");
            _performanceDetails.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ScheduleSessionSave();
        }

        private void UpdateWorkflowCommands()
        {
            if (_viewerNavigationCommands == null || _imageView == null) return;
            bool viewing = _imageView.Visibility == Visibility.Visible;
            foreach (var group in new[] { _viewerNavigationCommands, _viewerZoomCommands, _viewerEditCommands })
                group.Visibility = viewing ? Visibility.Visible : Visibility.Collapsed;
            _fitImageButton.IsEnabled = HasCurrentImage();
            _clearSearchButton.IsEnabled = !String.IsNullOrEmpty(_searchBox.Text);
            if (_folderLocation != null) _folderLocation.SetPath(_currentFolder);
            if (_performanceSummary != null) _performanceSummary.Text = ImageGpuSummary();
        }
    }
}
