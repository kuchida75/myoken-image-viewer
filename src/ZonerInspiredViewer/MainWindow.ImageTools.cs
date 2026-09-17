using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Button _rebuildThumbnailsButton, _cancelThumbnailRebuild, _rotateLeftButton, _rotateRightButton, _saveRotationButton, _rotationBackupsButton;
        private CheckBox _autoSaveRotationCheckBox;
        private TextBlock _thumbnailRebuildStatus, _rotationStatus;
        private CancellationTokenSource _thumbnailRebuildRequest;
        private Task _thumbnailRebuildTask, _rotationSaveTask;
        private bool _autoSaveRotations, _rotationSaving;
        private FileRevision _displayedImageRevision;

        private void BuildImageToolsConfiguration()
        {
            _rebuildThumbnailsButton = AdvancedSearchWindow.IconButton("\uE72C", "Rebuild thumbnails for current folder", StartThumbnailRebuild);
            _cancelThumbnailRebuild = AdvancedSearchWindow.IconButton("\uE711", "Cancel thumbnail rebuild", delegate
            { if (_thumbnailRebuildRequest != null) _thumbnailRebuildRequest.Cancel(); });
            _thumbnailRebuildStatus = ConfigurationValue();
            AddConfigurationRow(3, "Rebuild thumbnails", ConfigurationInline(_rebuildThumbnailsButton, _cancelThumbnailRebuild));
            AddConfigurationRow(3, "Rebuild status", _thumbnailRebuildStatus);
            _rotateLeftButton = AdvancedSearchWindow.IconButton("\uE7AD", "Rotate left 90 degrees", delegate { RotateImage(-1); });
            _rotateLeftButton.Content = new TextBlock { Text = "\uE7AD", RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(-1, 1) };
            _rotateRightButton = AdvancedSearchWindow.IconButton("\uE7AD", "Rotate right 90 degrees", delegate { RotateImage(1); });
            _saveRotationButton = AdvancedSearchWindow.IconButton("\uE74E", "Save current rotation to file", delegate { BeginRotationSave(true); });
            _rotationBackupsButton = AdvancedSearchWindow.IconButton("\uE838", "Open original rotation backups", delegate
            {
                try { Process.Start(new ProcessStartInfo(ImageRotationService.BackupFolder(_activeTabPath)) { UseShellExecute = true }); }
                catch (Exception error) { _rotationStatus.Text = error.Message; }
            });
            CommandPresentation.Describe(_rebuildThumbnailsButton, "Rebuild thumbnails", null, "Refresh cached thumbnails for this folder at the current size, including folder and minimap previews.");
            CommandPresentation.Describe(_cancelThumbnailRebuild, "Cancel rebuild", null, "Stop after the current image. Completed thumbnails are kept.");
            CommandPresentation.Describe(_rotateLeftButton, "Rotate left 90 degrees", "[", "Rotate the displayed image counterclockwise. View-only unless automatic saving is enabled.");
            CommandPresentation.Describe(_rotateRightButton, "Rotate right 90 degrees", "]", "Rotate the displayed image clockwise. View-only unless automatic saving is enabled.");
            CommandPresentation.Describe(_saveRotationButton, "Save rotation", null, "Save this JPEG/PNG rotation with an original-file backup. JPEG is re-encoded; Quick Enhance is not saved.");
            CommandPresentation.Describe(_rotationBackupsButton, "Original backups", null, "Open the folder containing originals kept before rotation saves.");
            AddConfigurationRow(3, "Rotate image", ConfigurationInline(_rotateLeftButton, _rotateRightButton, _saveRotationButton, _rotationBackupsButton));
            _autoSaveRotationCheckBox = new CheckBox { Content = new TextBlock { Text = "Automatically save rotations (JPEG / PNG)", TextWrapping = TextWrapping.Wrap } };
            _autoSaveRotationCheckBox.Click += delegate { SetAutoSaveRotations(_autoSaveRotationCheckBox.IsChecked == true, true); };
            AddConfigurationRow(3, "Rotation saving", _autoSaveRotationCheckBox);
            _rotationStatus = ConfigurationValue();
            AddConfigurationRow(3, "Rotation status", _rotationStatus);
            _confirmEnhanceOverwriteCheckBox = new CheckBox { Content = new TextBlock { Text = "Ask before overwriting", TextWrapping = TextWrapping.Wrap } };
            CommandPresentation.Describe(_confirmEnhanceOverwriteCheckBox, "Ask before overwriting enhanced images", null,
                "Confirm replacements for Enhance Save and Save As. Turn off to save without asking. Original backups and changed-file checks remain enabled; Windows access permissions still apply. Output is 8-bit sRGB.");
            RoutedEventHandler confirmationChanged = delegate
            {
                bool confirm = _confirmEnhanceOverwriteCheckBox.IsChecked != false;
                if (_confirmEnhanceOverwrite == confirm) return;
                _confirmEnhanceOverwrite = confirm;
                ScheduleSessionSave();
            };
            _confirmEnhanceOverwriteCheckBox.Checked += confirmationChanged;
            _confirmEnhanceOverwriteCheckBox.Unchecked += confirmationChanged;
            _confirmEnhanceOverwriteCheckBox.Indeterminate += confirmationChanged;
            AddConfigurationRow(3, "Enhance saving", _confirmEnhanceOverwriteCheckBox);
            UpdateImageToolsConfiguration();
        }

        private void UpdateImageToolsConfiguration()
        {
            if (_rebuildThumbnailsButton == null) return;
            _rebuildThumbnailsButton.IsEnabled = !_rotationSaving && !_backupBusy && _thumbnailRebuildRequest == null && Directory.Exists(_currentFolder);
            _cancelThumbnailRebuild.IsEnabled = _thumbnailRebuildRequest != null && !_thumbnailRebuildRequest.IsCancellationRequested;
            bool image = !_rotationSaving && !_backupBusy && !_fileTransferBusy && HasCurrentImage() && _imageView.IsVisible;
            _rotateLeftButton.IsEnabled = _rotateRightButton.IsEnabled = image;
            _saveRotationButton.IsEnabled = image && CurrentTabState().RotationQuarterTurns != 0 && ImageRotationService.CanSave(_activeTabPath);
            _autoSaveRotationCheckBox.IsEnabled = !_rotationSaving;
            _autoSaveRotationCheckBox.IsChecked = _autoSaveRotations;
            _confirmEnhanceOverwriteCheckBox.IsChecked = _confirmEnhanceOverwrite;
            _rotationBackupsButton.IsEnabled = !String.IsNullOrEmpty(_activeTabPath) && Directory.Exists(ImageRotationService.BackupFolder(_activeTabPath));
        }

        private void StartThumbnailRebuild()
        {
            if (_thumbnailRebuildRequest != null || _rotationSaving || _backupBusy || !Directory.Exists(_currentFolder)) return;
            ApplyThumbnailSize();
            _thumbnailRebuildTask = RebuildThumbnailsAsync(_currentFolder, _thumbnailSizePixels);
        }

        private async Task RebuildThumbnailsAsync(string folder, int size)
        {
            var request = new CancellationTokenSource(); _thumbnailRebuildRequest = request;
            _thumbnailRebuildStatus.Text = "Rebuilding: " + folder;
            _thumbnailRebuildStatus.ToolTip = folder;
            UpdateImageToolsConfiguration();
            string status = null, details = folder;
            try
            {
                ThumbnailRebuildProgress result = await _services.Thumbnails.RebuildFolderAsync(folder, size,
                    value => Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (!_isClosed && _thumbnailRebuildRequest == request)
                        { _thumbnailRebuildStatus.Text = value.ToString(); _thumbnailRebuildStatus.ToolTip = value.LastError ?? folder; }
                    })), request.Token).ConfigureAwait(false);
                status = result.ToString(); details = result.LastError ?? folder;
            }
            catch (OperationCanceledException) { status = "Rebuild canceled; completed thumbnails kept"; }
            catch (Exception error) { status = "Rebuild failed: " + error.Message; }
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                _thumbnailRebuildRequest = null; request.Dispose();
                if (!_isClosed)
                {
                    _thumbnailRebuildStatus.Text = status; _thumbnailRebuildStatus.ToolTip = details;
                    if (String.Equals(folder, _currentFolder, StringComparison.OrdinalIgnoreCase)) _thumbnailGrid.SetItems(_filteredItems);
                    RefreshTabStrip(); UpdateImageToolsConfiguration();
                }
            }));
        }

        private bool SetAutoSaveRotations(bool enabled, bool confirm)
        {
            if (_rotationSaving) return false;
            if (enabled && !_autoSaveRotations && confirm && MessageBox.Show(_configureWindow ?? this,
                "Automatically overwrite JPEG/PNG files after rotation?\n\nAn original-file backup is kept for every save in .viewer-rotation-backups beside the image. "
                + "Backups consume disk space. JPEG files are re-encoded at quality 95; PNG pixel data is lossless. "
                + "Animations and other formats remain view-only. Quick Enhance is never saved.\n\n"
                + "This applies to the rotation buttons and [ / ] keys in this window. Windows access restrictions still apply.",
                "Enable automatic rotation saving", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            { _autoSaveRotationCheckBox.IsChecked = false; return false; }
            _autoSaveRotations = enabled;
            UpdateImageToolsConfiguration(); ScheduleSessionSave(); return true;
        }

        private void BeginRotationSave(bool confirm)
        {
            if (_rotationSaving || _backupBusy || _fileTransferBusy || !HasCurrentImage() || CurrentTabState().RotationQuarterTurns == 0) return;
            if (!ImageRotationService.CanSave(_activeTabPath))
            { SetRotationStatus("View-only rotation: saving supports JPEG / PNG", null); return; }
            if (confirm && !_autoSaveRotations && MessageBox.Show(_configureWindow ?? this,
                "Save the current rotation over " + Path.GetFileName(_activeTabPath) + "?\n\n"
                + "An original backup is kept beside the image in .viewer-rotation-backups. JPEG is re-encoded at quality 95. Quick Enhance is not saved.",
                "Save rotation", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _rotationSaveTask = SaveRotationAsync(_activeTabPath, CurrentTabState().RotationQuarterTurns, _displayedImageRevision);
        }

        private async Task SaveRotationAsync(string path, int turns, FileRevision revision)
        {
            StopSlideshow(); EndImagePan(); SaveWorkspaceState();
            _rotationSaving = true;
            IsEnabled = false;
            _configurationContent.IsEnabled = false;
            SetRotationStatus("Saving rotation...", null);
            string backup = null;
            Exception failure = null;
            try
            {
                backup = await Task.Run(delegate { return ImageRotationService.Save(path, turns, revision, CancellationToken.None); }).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; }
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                try
                {
                    if (failure != null) SetRotationStatus("Not saved: " + failure.Message, failure.ToString());
                    else
                    {
                        _services.VramCache.Remove(path);
                        foreach (ImageTabState tab in _tabs.Values)
                            if (String.Equals(tab.Path, path, StringComparison.OrdinalIgnoreCase))
                            { tab.RotationQuarterTurns = 0; tab.HasCustomView = false; }
                        SetRotationStatus("Rotation saved; original backup kept", backup);
                        DisplayTab(); RefreshFolder(); ScheduleSessionSave();
                    }
                }
                finally
                {
                    bool returnFocus = IsActive || (_configureWindow != null && _configureWindow.IsActive);
                    _rotationSaving = false; IsEnabled = true; _configurationContent.IsEnabled = true;
                    UpdateImageToolsConfiguration();
                    RestoreImageFocusAfterSave(returnFocus);
                }
            }));
        }

        private void RestoreImageFocusAfterSave(bool activate)
        {
            if (_isClosed || !IsEnabled || !_imageView.IsVisible) return;
            // Disabling the window clears keyboard focus; save buttons/sliders must not reclaim it.
            FocusManager.SetFocusedElement(this, _imageCanvas);
            if (activate) { Activate(); _imageCanvas.Focus(); }
        }

        private void SetRotationStatus(string status, string details)
        {
            _rotationStatus.Text = status; _rotationStatus.ToolTip = details;
            _transferStatus = status; UpdateBrowserFooter();
        }
    }
}
