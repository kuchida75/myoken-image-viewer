using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Button _manualSaveButton, _manualSaveAsButton;
        private TextBlock _enhanceSaveStatus;
        private bool _enhancementSaving;
        private bool _confirmEnhanceOverwrite = true;
        private CheckBox _confirmEnhanceOverwriteCheckBox;
        private Task<EnhancedSaveResult> _enhanceSaveTask;

        private bool CanSaveEnhancement()
        {
            return !_isClosed && !_enhancementSaving && !_rotationSaving && !_backupBusy && !_fileTransferBusy
                && _enhancementRequest == null && !AutomaticUpscalePending && _imageView != null && _imageView.IsVisible && HasCurrentImage();
        }

        private void UpdateEnhanceSaveButtons()
        {
            UpdateUpscaleSizeControl();
            if (_manualSaveButton == null) return;
            bool ready = CanSaveEnhancement();
            if (_printButton != null) _printButton.IsEnabled = ready && _printPreview == null;
            if (_upscaleButton != null) _upscaleButton.IsEnabled = ready || AutomaticUpscalePending;
            if (_configureUpscaleButton != null) _configureUpscaleButton.IsEnabled = ready;
            bool canChooseModel = !_enhancementSaving && !_rotationSaving && !_backupBusy && !_fileTransferBusy;
            if (_upscaleModelButton != null) _upscaleModelButton.IsEnabled = canChooseModel;
            if (_configureUpscaleModel != null) _configureUpscaleModel.IsEnabled = canChooseModel;
            _manualSaveAsButton.IsEnabled = ready;
            ManualAdjustments value = CurrentTabState().ManualAdjustments;
            _manualSaveButton.IsEnabled = ready && (value != null && !value.IsNeutral || _mainImage.Effect != null || CurrentTabState().RotationQuarterTurns != 0);
        }

        private SaveFileDialog CreateEnhanceSaveDialog()
        {
            return new SaveFileDialog { Title = "Save adjusted image (full resolution, 8-bit)", Filter = EnhancedImageStore.Filter,
                DefaultExt = ".png", AddExtension = true, OverwritePrompt = _confirmEnhanceOverwrite, CheckPathExists = true,
                InitialDirectory = Path.GetDirectoryName(_activeTabPath), FileName = Path.GetFileNameWithoutExtension(_activeTabPath) + "-enhanced.png" };
        }

        private void BeginEnhanceSave(bool saveAs)
        {
            if (!CanSaveEnhancement()) return;
            string destination = _activeTabPath;
            if (saveAs)
            {
                var dialog = CreateEnhanceSaveDialog();
                if (dialog.ShowDialog(this) != true) return;
                destination = dialog.FileName;
            }
            if (!saveAs && _confirmEnhanceOverwrite
                && MessageBox.Show(this, "Overwrite " + Path.GetFileName(destination) + " with the adjusted image?\n\n"
                    + "Includes the manual sliders, visible Quick Enhance and rotation, at full pixel resolution. Output is 8-bit sRGB. "
                    + "JPEG / AVIF / HEIC are re-encoded at quality 95; GIF is palette-limited. JPEG transparency is flattened onto white.\n\n"
                    + "An original backup is kept in .viewer-enhance-backups beside the file. Animations cannot be overwritten.",
                    "Save adjusted image", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _enhanceSaveTask = SaveEnhancedImageAsync(destination, File.Exists(destination), true);
        }

        private async Task<EnhancedSaveResult> SaveEnhancedImageAsync(string destination, bool allowOverwrite, bool showProgress)
        {
            if (!CanSaveEnhancement()) throw new InvalidOperationException("Wait for the image and Quick Enhance to finish loading before saving.");
            destination = Path.GetFullPath(destination);
            string source = _activeTabPath;
            Effect quick = _mainImage.Effect == null ? null : (Effect)_mainImage.Effect.CloneCurrentValue();
            if (quick != null) quick.Freeze();
            var request = new EnhancedSaveRequest { SourcePath = source, Destination = destination, SourceRevision = _displayedImageRevision,
                DestinationRevision = FileRevision.Read(destination), AllowOverwrite = allowOverwrite, Pixels = (BitmapSource)_mainImage.Source,
                QuickEffect = quick, Manual = ManualAdjustments.Copy(CurrentTabState().ManualAdjustments), Rotation = CurrentTabState().RotationQuarterTurns };
            StopSlideshow(); EndImagePan(); _imageNavigator.EndDrag(); SaveWorkspaceState();
            if (_manualEnhanceOverlay.IsMouseCaptureWithin) System.Windows.Input.Mouse.Capture(null);
            if (_animationTimer != null) _animationTimer.Stop();
            _enhancementSaving = true; IsEnabled = false; _configurationContent.IsEnabled = false;
            var cancellation = new CancellationTokenSource();
            Window progressWindow = null; ProgressBar progressBar = null; bool finished = false;
            if (showProgress)
            {
                progressWindow = new Window { Title = "Saving image", Owner = this, Width = 390, Height = 170,
                    ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                ThemeManager.PrepareWindow(progressWindow);
                var panel = new StackPanel { Margin = new Thickness(18) };
                panel.Children.Add(new TextBlock { Text = Path.GetFileName(destination), TextTrimming = TextTrimming.CharacterEllipsis });
                progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 12, Margin = new Thickness(0, 14, 0, 14) };
                panel.Children.Add(progressBar);
                var cancel = CommandPresentation.Button("\uE711", "Cancel", null, "Cancel before the completed image replaces its destination.", cancellation.Cancel, true);
                cancel.HorizontalAlignment = HorizontalAlignment.Right; panel.Children.Add(cancel);
                progressWindow.Content = panel;
                progressWindow.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
                { if (!finished) { cancellation.Cancel(); e.Cancel = true; } };
                progressWindow.PreviewKeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
                { if (e.Key == System.Windows.Input.Key.Escape) { cancellation.Cancel(); e.Handled = true; } };
                progressWindow.Show();
            }
            EnhancedSaveResult result = null; Exception failure = null;
            try
            {
                result = await EnhancedImageStore.SaveAsync(request, cancellation.Token, value =>
                    Dispatcher.BeginInvoke(new Action(delegate { if (!finished && progressBar != null) progressBar.Value = value * 100; }))).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; }
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                try
                {
                    if (result != null)
                    {
                        _services.VramCache.Remove(result.Path);
                        if (!String.Equals(source, result.Path, StringComparison.OrdinalIgnoreCase)) OpenImageTab(result.Path, false);
                        foreach (ImageTabState tab in _tabs.Values.Where(t => String.Equals(t.Path, result.Path, StringComparison.OrdinalIgnoreCase)))
                        {
                            tab.ManualAdjustments = null; tab.RotationQuarterTurns = 0; tab.HasCustomView = false;
                            tab.SavedEnhanceRevision = result.Revision;
                        }
                        if (!String.Equals(source, result.Path, StringComparison.OrdinalIgnoreCase))
                            ActivateImageTab(_tabOrder.First(id => !_tabs[id].IsBrowser && String.Equals(_tabs[id].Path, result.Path, StringComparison.OrdinalIgnoreCase)));
                        else DisplayTab();
                        RefreshFolder(); ScheduleSessionSave();
                        _enhanceSaveStatus.Text = "Saved: " + Path.GetFileName(result.Path);
                        _enhanceSaveStatus.ToolTip = result.Path + (result.Backup == null ? "" : "\nOriginal backup: " + result.Backup);
                    }
                    else
                    {
                        _enhanceSaveStatus.Text = failure is OperationCanceledException ? "Save canceled; no file was replaced" : "Not saved: " + failure.Message;
                        _enhanceSaveStatus.ToolTip = failure.Message;
                        if (_animation != null && _animationTimer != null) _animationTimer.Start();
                    }
                    _enhanceSaveStatus.Visibility = Visibility.Visible;
                    _transferStatus = _enhanceSaveStatus.Text; UpdateBrowserFooter();
                }
                finally
                {
                    bool returnFocus = IsActive || (_configureWindow != null && _configureWindow.IsActive)
                        || (progressWindow != null && progressWindow.IsActive);
                    finished = true;
                    cancellation.Dispose(); _enhancementSaving = false; IsEnabled = true; _configurationContent.IsEnabled = true;
                    if (progressWindow != null) progressWindow.Close();
                    UpdateEnhanceSaveButtons(); UpdateImageToolsConfiguration(); LayoutImageOverlays();
                    RestoreImageFocusAfterSave(returnFocus);
                }
            }));
            return result;
        }

        private bool HasSavedEnhance(ImageTabState state)
        { return state.SavedEnhanceRevision.HasValue && state.SavedEnhanceRevision.Value.Equals(_displayedImageRevision); }
    }
}
