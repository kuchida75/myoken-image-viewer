using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Button _upscaleButton, _configureUpscaleButton;
        private double _upscaleScale = 1.6;
        private CancellationTokenSource _autoUpscaleRequest;
        private Task _autoUpscaleTask;
        private BitmapSource _autoUpscaleOriginal;
        private double _autoUpscaleFactor = 1;
        private string _autoUpscaleStatus;
        private UpscaleStatus _autoUpscaleStatusKind;

        private Button BuildUpscaleButton(bool automatic = false)
        {
            var button = CommandPresentation.Button("\uE740", automatic ? "AI upscale" : "AI upscale preview", automatic ? "S" : null,
                automatic ? "Automatically upscale small images for the current viewing frame. Press again to restore the original or cancel. No file is changed."
                : "Choose a manual AI scale, compare the result and save a separate copy. Includes current Enhance adjustments.",
                automatic ? (Action)ToggleAutomaticUpscale : ShowSuperResolution, true);
            button.IsEnabled = false; return button;
        }

        private void SetAutomaticUpscaleStatus(string message, UpscaleStatus state)
        {
            _autoUpscaleStatus = message; _autoUpscaleStatusKind = state; _transferStatus = message;
            UpdateAutomaticUpscaleAppearance(); UpdateBrowserFooter();
        }

        private void UpdateAutomaticUpscaleAppearance()
        {
            UpscaleStatus state = AutomaticUpscalePending ? UpscaleStatus.Working
                : _autoUpscaleOriginal == null ? UpscaleStatus.None
                : _autoUpscaleAppliedScale == 1 ? UpscaleStatus.Bypassed : UpscaleStatus.Ready;
            UpscalePresentation.ApplyButton(_upscaleButton, state);
            UpscalePresentation.ApplyButton(_upscaleModelButton, state);
            if (_upscaleSizeText != null)
            {
                ThemeManager.Bind(_upscaleSizeText, TextBlock.ForegroundProperty, UpscalePresentation.TextKey(state));
                _upscaleSizeText.FontWeight = FontWeights.SemiBold;
            }
            if (_upscaleSizeSlider != null)
                ThemeManager.Bind(_upscaleSizeSlider, Control.ForegroundProperty, UpscalePresentation.TextKey(state));
        }

        private void ClearAutomaticUpscale()
        {
            if (_upscaleSizeTimer != null) _upscaleSizeTimer.Stop();
            if (_autoUpscaleRequest != null) { _autoUpscaleRequest.Cancel(); _autoUpscaleRequest = null; }
            _autoUpscaleTask = null;
            _autoUpscaleOriginal = null; _autoUpscaleFactor = 1; _autoUpscaleAppliedScale = 1;
            if (_transferStatus == _autoUpscaleStatus) _transferStatus = null;
            _autoUpscaleStatus = null; _autoUpscaleStatusKind = UpscaleStatus.None;
            UpdateUpscaleSizeControl();
            UpdateBrowserFooter();
        }

        private void ToggleAutomaticUpscale()
        {
            if (AutomaticUpscalePending)
            {
                _upscaleSizeTimer.Stop();
                if (_autoUpscaleRequest != null) _autoUpscaleRequest.Cancel();
                else
                {
                    RestoreUpscaleSizeSelection();
                    SetAutomaticUpscaleStatus("AI upscale change canceled; current image unchanged", UpscaleStatus.Canceled);
                    if (_autoUpscaleOriginal == null) StartAnimation(_activeTabPath, _imageLoadVersion);
                    UpdateEnhanceSaveButtons();
                }
                return;
            }
            if (!CanSaveEnhancement()) return;
            if (_autoUpscaleOriginal != null)
            {
                BitmapSource original = _autoUpscaleOriginal; ClearAutomaticUpscale();
                SetAutomaticUpscalePixels(original);
                SetAutomaticUpscaleStatus("AI upscale off: original resolution restored", UpscaleStatus.None);
                StartAnimation(_activeTabPath, _imageLoadVersion); return;
            }
            var source = (BitmapSource)_mainImage.Source;
            var presentation = PresentationSource.FromVisual(this);
            Matrix dpi = presentation == null || presentation.CompositionTarget == null ? Matrix.Identity : presentation.CompositionTarget.TransformToDevice;
            double scale = SuperResolution.AutomaticScale(DisplayedImageSize(), new Size(_imageCanvas.ActualWidth * dpi.M11, _imageCanvas.ActualHeight * dpi.M22));
            if (scale <= 1)
            {
                SetAutomaticUpscaleStatus("AI upscale not needed: image already has enough pixels for this frame, or exceeds the 8 MP source limit", UpscaleStatus.Bypassed);
                return;
            }
            StartAutomaticUpscale(source, scale);
        }

        private void StartAutomaticUpscale(BitmapSource original, double scale)
        {
            _upscaleSizeTimer.Stop();
            StopSlideshow(); StopAnimation(); EndImagePan(); _imageNavigator.EndDrag();
            SyncUpscaleSize(scale, original);
            if (scale == 1)
            {
                _autoUpscaleOriginal = original; _autoUpscaleFactor = 1; _autoUpscaleAppliedScale = 1;
                SetAutomaticUpscalePixels(original);
                SetAutomaticUpscaleStatus("AI upscale 1x (view only) | Original resolution; AI bypassed", UpscaleStatus.Bypassed);
                _autoUpscaleTask = Task.FromResult(0); UpdateEnhanceSaveButtons(); return;
            }
            var cancellation = new CancellationTokenSource(); _autoUpscaleRequest = cancellation;
            SetAutomaticUpscaleStatus("AI upscale: preparing " + scale.ToString("0.##") + "x preview", UpscaleStatus.Working);
            UpdateEnhanceSaveButtons();
            _autoUpscaleTask = ApplyAutomaticUpscaleAsync(original, (BitmapSource)_mainImage.Source, scale, _activeTabPath, _imageLoadVersion, _displayedImageRevision, cancellation);
        }

        private async Task ApplyAutomaticUpscaleAsync(BitmapSource original, BitmapSource displayed, double scale, string path, int version, FileRevision revision, CancellationTokenSource cancellation)
        {
            SuperResolutionResult result = null; Exception failure = null;
            try
            {
                // Keep adjustments live in the main viewer; do not bake them into its replacement source.
                result = await SuperResolution.RunAsync(new SuperResolutionRequest { Source = original, Scale = scale, ModelId = _upscaleModelId, Supir = _supirConfig, Workers = _services.CpuWorkerCount,
                    Adapter = GpuHardware.SelectProcessing(_processingGpuKey, GpuHardware.Devices), SoftLimitMb = _processingGpuLimitMb }, cancellation.Token,
                    (p, stage) => Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (!_isClosed && _autoUpscaleRequest == cancellation)
                            SetAutomaticUpscaleStatus("AI upscale " + (p * 100).ToString("0") + "%: " + stage, UpscaleStatus.Working);
                    }))).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; }
            if (_isClosed || Dispatcher.HasShutdownStarted) { cancellation.Dispose(); return; }
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                bool current = !_isClosed && _autoUpscaleRequest == cancellation && version == _imageLoadVersion
                    && path == _activeTabPath && Object.ReferenceEquals(_mainImage.Source, displayed);
                if (_autoUpscaleRequest == cancellation) _autoUpscaleRequest = null;
                bool canceled = cancellation.IsCancellationRequested;
                cancellation.Dispose();
                if (!current) return;
                if (result != null && !canceled && revision.Matches(path))
                {
                    _autoUpscaleOriginal = original;
                    _autoUpscaleAppliedScale = scale;
                    _autoUpscaleFactor = result.Pixels.PixelWidth / (double)original.PixelWidth;
                    SetAutomaticUpscalePixels(result.Pixels);
                    SetAutomaticUpscaleStatus(UpscaleModels.Get(result.ModelId).Name + " " + scale.ToString("0.##") + "x (view only) | " + result.Backend + " | Enhance > Save As to keep a copy", UpscaleStatus.Ready);
                }
                else
                {
                    RestoreUpscaleSizeSelection();
                    SetAutomaticUpscaleStatus(canceled || failure is OperationCanceledException ? "AI upscale canceled; current image unchanged"
                        : failure != null ? "AI upscale unavailable: " + failure.Message : "AI upscale discarded: source image changed",
                        canceled || failure is OperationCanceledException ? UpscaleStatus.Canceled : UpscaleStatus.Error);
                    if (_autoUpscaleOriginal == null) StartAnimation(path, version);
                }
                UpdateEnhanceSaveButtons();
            }));
        }

        private void SetAutomaticUpscalePixels(BitmapSource pixels)
        {
            CancelEnhancement();
            _mainImage.Source = pixels; _mainImage.Width = pixels.PixelWidth; _mainImage.Height = pixels.PixelHeight;
            UpdateImageRotation(); FitImageAfterContentChange(); ApplyQuickEnhance(); UpdateManualEnhance();
            UpdateImageNavigator(); UpdateStatus();
        }

        private ImageTabState UpscaleViewState(ImageTabState state)
        {
            if (_autoUpscaleOriginal == null) return state;
            return new ImageTabState { HasCustomView = state.HasCustomView, Zoom = state.Zoom / _autoUpscaleFactor,
                OffsetX = state.OffsetX, OffsetY = state.OffsetY, ViewportWidth = state.ViewportWidth, ViewportHeight = state.ViewportHeight };
        }

        private void ShowSuperResolution()
        {
            if (!CanSaveEnhancement()) return;
            StopSlideshow(); EndImagePan(); _imageNavigator.EndDrag();
            Effect quick = _mainImage.Effect == null ? null : (Effect)_mainImage.Effect.CloneCurrentValue();
            if (quick != null) quick.Freeze();
            var dialog = new SuperResolutionWindow(new SuperResolutionRequest { Source = _autoUpscaleOriginal ?? (BitmapSource)_mainImage.Source,
                Manual = ManualAdjustments.Copy(CurrentTabState().ManualAdjustments), Rotation = CurrentTabState().RotationQuarterTurns,
                QuickEffect = quick, Scale = _upscaleScale, ModelId = _upscaleModelId, Supir = _supirConfig, Workers = _services.CpuWorkerCount,
                Adapter = GpuHardware.SelectProcessing(_processingGpuKey, GpuHardware.Devices), SoftLimitMb = _processingGpuLimitMb },
                _activeTabPath, _displayedImageRevision) { Owner = _configureWindow != null && _configureWindow.IsVisible ? _configureWindow : this };
            dialog.ShowDialog();
            _upscaleScale = dialog.SelectedScale; ScheduleSessionSave();
            if (dialog.SavedResult != null && !_isClosed)
            {
                var saved = dialog.SavedResult; _services.VramCache.Remove(saved.Path);
                OpenImageTab(saved.Path, false);
                foreach (ImageTabState tab in _tabs.Values.Where(t => String.Equals(t.Path, saved.Path, StringComparison.OrdinalIgnoreCase)))
                { tab.ManualAdjustments = null; tab.RotationQuarterTurns = 0; tab.HasCustomView = false; tab.SavedEnhanceRevision = saved.Revision; }
                ActivateImageTab(_tabOrder.First(id => !_tabs[id].IsBrowser && String.Equals(_tabs[id].Path, saved.Path, StringComparison.OrdinalIgnoreCase)));
                RefreshFolder(); ScheduleSessionSave();
            }
            RestoreImageFocusAfterSave(true);
        }
    }
}
