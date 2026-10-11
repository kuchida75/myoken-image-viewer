using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
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
        private Button _rebuildThumbnailsButton, _cancelThumbnailRebuild, _rotateLeftButton, _rotateRightButton, _saveRotationButton, _cancelRotationSaveButton, _rotationBackupsButton;
        private CheckBox _autoSaveRotationCheckBox;
        private TextBlock _thumbnailRebuildStatus, _rotationStatus;
        private CancellationTokenSource _thumbnailRebuildRequest;
        private Task _thumbnailRebuildTask, _rotationSaveTask;
        private CancellationTokenSource _rotationSaveRequest;
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
            _rotateLeftButton = AdvancedSearchWindow.IconButton("\uE7AD", "Rotate left 90 degrees", delegate { RotateFromShortcut(-1); });
            _rotateLeftButton.Content = new TextBlock { Text = "\uE7AD", RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(-1, 1) };
            _rotateRightButton = AdvancedSearchWindow.IconButton("\uE7AD", "Rotate right 90 degrees", delegate { RotateFromShortcut(1); });
            _saveRotationButton = AdvancedSearchWindow.IconButton("\uE74E", "Save current rotation to file", delegate { BeginRotationSave(true); });
            _cancelRotationSaveButton = AdvancedSearchWindow.IconButton("\uE711", "Cancel batch rotation save", delegate
            { if (_rotationSaveRequest != null) _rotationSaveRequest.Cancel(); });
            _rotationBackupsButton = AdvancedSearchWindow.IconButton("\uE838", "Open original rotation backups", delegate
            {
                try { Process.Start(new ProcessStartInfo(ImageRotationService.BackupFolder(RotationTargetPath())) { UseShellExecute = true }); }
                catch (Exception error) { _rotationStatus.Text = error.Message; }
            });
            CommandPresentation.Describe(_rebuildThumbnailsButton, "Rebuild thumbnails", null, "Refresh cached thumbnails for this folder at the current size, including folder and minimap previews.");
            CommandPresentation.Describe(_cancelThumbnailRebuild, "Cancel rebuild", null, "Stop after the current image. Completed thumbnails are kept.");
            CommandPresentation.Describe(_rotateLeftButton, "Rotate left 90 degrees", "[", "Rotate all selected images or the displayed image counterclockwise. View-only unless automatic saving is enabled. Folders are skipped.");
            CommandPresentation.Describe(_rotateRightButton, "Rotate right 90 degrees", "]", "Rotate all selected images or the displayed image clockwise. View-only unless automatic saving is enabled. Folders are skipped.");
            CommandPresentation.Describe(_saveRotationButton, "Save rotation", null, "Save selected JPEG/PNG rotations with an original-file backup for each image. JPEG is re-encoded; Quick Enhance is not saved.");
            CommandPresentation.Describe(_cancelRotationSaveButton, "Cancel rotation save", null, "Stop after the current image. Rotations already saved keep their original-file backups.");
            CommandPresentation.Describe(_rotationBackupsButton, "Original backups", null, "Open the folder containing originals kept before rotation saves.");
            AddConfigurationRow(3, "Rotate image", ConfigurationInline(_rotateLeftButton, _rotateRightButton, _saveRotationButton, _cancelRotationSaveButton, _rotationBackupsButton));
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
            string browserPath = SelectedBrowserRotationPath();
            bool available = !_rotationSaving && !_backupBusy && !_fileTransferBusy;
            bool image = available && HasCurrentImage() && _imageView.IsVisible;
            bool browserImage = available && browserPath != null;
            _rotateLeftButton.IsEnabled = _rotateRightButton.IsEnabled = image || browserImage;
            _saveRotationButton.IsEnabled = image && CurrentTabState().RotationQuarterTurns != 0 && ImageRotationService.CanSave(_activeTabPath)
                || available && _browserView.Visibility == Visibility.Visible && SelectedBrowserRotationPaths()
                    .Any(path => BrowserRotationForPath(path) != 0 && ImageRotationService.CanSave(path));
            _cancelRotationSaveButton.IsEnabled = _rotationSaveRequest != null && !_rotationSaveRequest.IsCancellationRequested;
            _autoSaveRotationCheckBox.IsEnabled = !_rotationSaving;
            _autoSaveRotationCheckBox.IsChecked = _autoSaveRotations;
            _confirmEnhanceOverwriteCheckBox.IsChecked = _confirmEnhanceOverwrite;
            string target = RotationTargetPath();
            _rotationBackupsButton.IsEnabled = !String.IsNullOrEmpty(target) && Directory.Exists(ImageRotationService.BackupFolder(target));
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
            if (_browserView.Visibility == Visibility.Visible) { BeginBrowserRotationSave(confirm); return; }
            if (_rotationSaving || _backupBusy || _fileTransferBusy || !HasCurrentImage() || CurrentTabState().RotationQuarterTurns == 0) return;
            if (!ImageRotationService.CanSave(_activeTabPath))
            { SetRotationStatus("View-only rotation: saving supports JPEG / PNG", null); return; }
            if (confirm && !_autoSaveRotations && MessageBox.Show(_configureWindow ?? this,
                "Save the current rotation over " + Path.GetFileName(_activeTabPath) + "?\n\n"
                + "An original backup is kept beside the image in .viewer-rotation-backups. JPEG is re-encoded at quality 95. Quick Enhance is not saved.",
                "Save rotation", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _rotationSaveTask = SaveRotationAsync(_activeTabPath, CurrentTabState().RotationQuarterTurns, _displayedImageRevision, false);
        }

        private async Task SaveRotationAsync(string path, int turns, FileRevision revision, bool browser)
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
                        ClearBrowserRotationEverywhere(path);
                        SetRotationStatus("Rotation saved; original backup kept", backup);
                        if (!browser) DisplayTab();
                        RefreshFolder(); ScheduleSessionSave();
                    }
                }
                finally
                {
                    bool returnFocus = IsActive || (_configureWindow != null && _configureWindow.IsActive);
                    _rotationSaving = false; IsEnabled = true; _configurationContent.IsEnabled = true;
                    UpdateImageToolsConfiguration();
                    if (browser && !_isClosed)
                    {
                        FocusManager.SetFocusedElement(this, _thumbnailGrid);
                        if (returnFocus) { Activate(); _thumbnailGrid.Focus(); }
                    }
                    else RestoreImageFocusAfterSave(returnFocus);
                }
            }));
        }

        private string SelectedBrowserRotationPath()
        {
            return SelectedBrowserRotationPaths().FirstOrDefault();
        }

        private IEnumerable<string> SelectedBrowserRotationPaths()
        {
            if (_browserView == null || _browserView.Visibility != Visibility.Visible || _thumbnailGrid == null)
                return Enumerable.Empty<string>();
            return _thumbnailGrid.SelectedItems.Where(item => !item.IsDirectory && File.Exists(item.Path))
                .Select(item => item.Path);
        }

        private string RotationTargetPath()
        {
            return _browserView != null && _browserView.Visibility == Visibility.Visible ? SelectedBrowserRotationPath() : _activeTabPath;
        }

        private int BrowserRotationForPath(string path)
        {
            BrowserRotationState rotation;
            return path != null && CurrentTabState().BrowserRotations.TryGetValue(path, out rotation)
                && rotation.Revision.Matches(path) ? rotation.Turns : 0;
        }

        private void RememberBrowserRotation(ImageTabState state, string path, int turns, FileRevision revision)
        {
            if (String.IsNullOrEmpty(path)) return;
            turns = ImageViewport.NormalizeRotation(turns);
            if (turns == 0 || revision.Length < 0) state.BrowserRotations.Remove(path);
            else state.BrowserRotations[path] = new BrowserRotationState { Turns = turns, Revision = revision };
            if (_thumbnailGrid != null) _thumbnailGrid.RefreshRotations();
        }

        private void RotateBrowserImage(int quarterTurns)
        {
            if (_rotationSaving || _fileTransferBusy || _backupBusy) return;
            string[] paths = SelectedBrowserRotationPaths().ToArray();
            if (paths.Length == 0) return;
            int changed = 0;
            var state = CurrentTabState();
            foreach (string path in paths)
            {
                FileRevision revision = FileRevision.Read(path);
                if (revision.Length < 0) continue;
                int turns = ImageViewport.NormalizeRotation(BrowserRotationForPath(path) + quarterTurns);
                if (turns == 0) state.BrowserRotations.Remove(path);
                else state.BrowserRotations[path] = new BrowserRotationState { Turns = turns, Revision = revision };
                changed++;
            }
            if (changed == 0) { SetRotationStatus("Selected images are unavailable", null); return; }
            _thumbnailGrid.RefreshRotations();
            SetRotationStatus(changed == 1 ? "View-only thumbnail rotation" : changed + " thumbnails rotated (view-only)", null);
            UpdateImageToolsConfiguration(); ScheduleSessionSave();
            if (_autoSaveRotations) BeginBrowserRotationSave(false);
        }

        private void BeginBrowserRotationSave(bool confirm)
        {
            if (_rotationSaving || _backupBusy || _fileTransferBusy) return;
            var rotations = SelectedBrowserRotationPaths().Select(path =>
            {
                BrowserRotationState rotation;
                return CurrentTabState().BrowserRotations.TryGetValue(path, out rotation) && rotation.Turns != 0
                    ? new KeyValuePair<string, BrowserRotationState>(path, rotation) : new KeyValuePair<string, BrowserRotationState>();
            }).Where(pair => pair.Key != null).ToArray();
            if (rotations.Length == 0) return;
            var writable = rotations.Where(pair => ImageRotationService.CanSave(pair.Key)).ToArray();
            if (writable.Length == 0)
            { SetRotationStatus("View-only rotation: saving supports JPEG / PNG", null); return; }
            if (confirm && !_autoSaveRotations && MessageBox.Show(_configureWindow ?? this,
                "Save rotations over " + writable.Length + " selected JPEG/PNG " + (writable.Length == 1 ? "image" : "images") + "?\n\n"
                + "An original backup is kept beside each image in .viewer-rotation-backups. JPEG is re-encoded at quality 95.",
                "Save rotation", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (rotations.Length == 1)
                _rotationSaveTask = SaveRotationAsync(writable[0].Key, writable[0].Value.Turns, writable[0].Value.Revision, true);
            else _rotationSaveTask = SaveBrowserRotationsAsync(writable, rotations.Length - writable.Length);
        }

        private async Task SaveBrowserRotationsAsync(KeyValuePair<string, BrowserRotationState>[] rotations, int unsupported)
        {
            StopSlideshow(); EndImagePan(); SaveWorkspaceState();
            var request = new CancellationTokenSource();
            _rotationSaveRequest = request; _rotationSaving = true; IsEnabled = false;
            UpdateImageToolsConfiguration();
            var saved = new List<KeyValuePair<string, string>>();
            var errors = new List<string>();
            Exception unexpected = null;
            try
            {
                await Task.Run(delegate
                {
                    for (int index = 0; index < rotations.Length && !request.IsCancellationRequested; index++)
                    {
                        var entry = rotations[index];
                        int progress = index + 1;
                        Dispatcher.BeginInvoke(new Action(delegate
                        { if (!_isClosed) SetRotationStatus("Saving rotation " + progress + " / " + rotations.Length + "...", entry.Key); }));
                        try
                        {
                            string backup = ImageRotationService.Save(entry.Key, entry.Value.Turns, entry.Value.Revision, request.Token);
                            saved.Add(new KeyValuePair<string, string>(entry.Key, backup));
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception error) { errors.Add(Path.GetFileName(entry.Key) + ": " + error.Message); }
                    }
                }).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(new Action(delegate
                {
                    foreach (var entry in saved)
                    {
                        _services.VramCache.Remove(entry.Key);
                        foreach (ImageTabState tab in _tabs.Values)
                            if (String.Equals(tab.Path, entry.Key, StringComparison.OrdinalIgnoreCase))
                            { tab.RotationQuarterTurns = 0; tab.HasCustomView = false; }
                        ClearBrowserRotationEverywhere(entry.Key, false);
                    }
                    if (saved.Count > 0)
                    {
                        _thumbnailGrid.RefreshRotations();
                        RefreshFolder(); ScheduleSessionSave();
                    }
                    string status = saved.Count + " rotation" + (saved.Count == 1 ? "" : "s") + " saved";
                    if (request.IsCancellationRequested) status += "; canceled";
                    if (unsupported > 0) status += "; " + unsupported + " unsupported kept view-only";
                    if (errors.Count > 0) status += "; " + errors.Count + " failed";
                    SetRotationStatus(status, errors.Count > 0 ? String.Join(Environment.NewLine, errors) : null);
                }));
            }
            catch (Exception error) { unexpected = error; }
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                try
                {
                    if (unexpected != null) SetRotationStatus("Rotation save failed: " + unexpected.Message, unexpected.ToString());
                    bool returnFocus = IsActive || (_configureWindow != null && _configureWindow.IsActive);
                    _rotationSaveRequest = null; _rotationSaving = false; IsEnabled = true;
                    UpdateImageToolsConfiguration();
                    if (!_isClosed)
                    {
                        FocusManager.SetFocusedElement(this, _thumbnailGrid);
                        if (returnFocus) { Activate(); _thumbnailGrid.Focus(); }
                    }
                }
                finally { request.Dispose(); }
            }));
        }

        private void ClearBrowserRotationEverywhere(string path, bool refresh = true)
        {
            _homeBrowser.BrowserRotations.Remove(path);
            if (_latestBrowserView != null) _latestBrowserView.BrowserRotations.Remove(path);
            foreach (ImageTabState tab in _tabs.Values) tab.BrowserRotations.Remove(path);
            if (refresh) _thumbnailGrid.RefreshRotations();
        }

        private static List<BrowserRotationDto> CaptureBrowserRotations(ImageTabState state)
        {
            return state.BrowserRotations.Select(pair => new BrowserRotationDto
            { Path = pair.Key, Turns = pair.Value.Turns, Revision = pair.Value.Revision }).ToList();
        }

        private static Dictionary<string, BrowserRotationState> RestoreBrowserRotations(IEnumerable<BrowserRotationDto> rotations)
        {
            var restored = new Dictionary<string, BrowserRotationState>(StringComparer.OrdinalIgnoreCase);
            if (rotations == null) return restored;
            foreach (BrowserRotationDto rotation in rotations)
                if (rotation != null && !String.IsNullOrWhiteSpace(rotation.Path) && ImageExtensions.IsBrowsableImage(rotation.Path)
                    && rotation.Turns >= 1 && rotation.Turns <= 3 && rotation.Revision.Length >= 0)
                    restored[rotation.Path] = new BrowserRotationState { Turns = rotation.Turns, Revision = rotation.Revision };
            return restored;
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
