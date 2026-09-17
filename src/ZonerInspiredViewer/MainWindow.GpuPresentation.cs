using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Image _gpuImage;
        private D3DImage _gpuSurface;
        private GpuImageLease _gpuImageLease;
        private BitmapSource _gpuImageSource;
        private CancellationTokenSource _gpuImageRequest;
        private DependencyPropertyDescriptor _gpuSourceObserver;
        private DateTime _gpuPresentationRetry;
        private Task _gpuPresentationTask;
        private CheckBox _imageGpuEnabledCheck, _imageGpuJpegCheck;
        private ComboBox _imageGpuCombo;
        private Slider _imageGpuBudget;
        private TextBlock _imageGpuBudgetText, _imageGpuDiagnostics;
        private bool _restoringImageGpu;
        private string _gpuPresentationError;

        private void BuildGpuPresentation()
        {
            _gpuImage = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            foreach (var property in new[] { FrameworkElement.WidthProperty, FrameworkElement.HeightProperty, UIElement.RenderTransformProperty, UIElement.EffectProperty })
                _gpuImage.SetBinding(property, new Binding(property.Name) { Source = _mainImage });
            RenderOptions.SetBitmapScalingMode(_gpuImage, BitmapScalingMode.HighQuality);
            _manualImageSurface.Children.Add(_gpuImage);
            _gpuSourceObserver = DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image));
            _gpuSourceObserver.AddValueChanged(_mainImage, GpuImageSourceChanged);
        }

        private void GpuImageSourceChanged(object sender, EventArgs args)
        {
            ReleaseGpuPresentation(); _gpuPresentationRetry = DateTime.MinValue;
            if (!_isClosed) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateGpuPresentation));
        }

        private void ReleaseGpuPresentation()
        {
            if (_gpuImageRequest != null) { _gpuImageRequest.Cancel(); _gpuImageRequest = null; }
            if (_gpuImage != null) { _gpuImage.Source = null; _gpuImage.Visibility = Visibility.Collapsed; }
            if (_mainImage != null) _mainImage.Opacity = 1;
            if (_gpuSurface != null)
            {
                try { _gpuSurface.Lock(); try { _gpuSurface.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero); } finally { _gpuSurface.Unlock(); } }
                catch (Exception error) { _gpuPresentationError = error.Message; }
                _gpuSurface = null;
            }
            if (_gpuImageLease != null) { _gpuImageLease.Dispose(); _gpuImageLease = null; }
            _gpuImageSource = null;
        }

        private void StopGpuPresentation()
        {
            _gpuSourceObserver.RemoveValueChanged(_mainImage, GpuImageSourceChanged);
            ReleaseGpuPresentation();
            _services.GpuImages.Configure(false, _services.GpuImages.AdapterKey, _services.GpuImages.LimitMb, _services.GpuImages.JpegEnabled);
        }

        private void UpdateGpuPresentation()
        {
            if (_gpuImage == null || _isClosed) return;
            var stats = _services.GpuImages.Statistics;
            if (_gpuImageLease != null && (stats.ReleaseActive || _gpuImageLease.Texture.Retired || !_services.GpuImages.Enabled))
            {
                ReleaseGpuPresentation(); _gpuPresentationRetry = DateTime.UtcNow.AddSeconds(3);
                _gpuPresentationError = "WPF fallback while GPU memory is reclaimed";
            }
            if (!_services.GpuImages.Enabled || !HasCurrentImage() || !_imageView.IsVisible
                || String.Equals(Path.GetExtension(_activeTabPath), ".gif", StringComparison.OrdinalIgnoreCase)) return;
            var source = _mainImage.Source as BitmapSource;
            if (source == null || Object.ReferenceEquals(_gpuImageSource, source) || _gpuImageRequest != null || DateTime.UtcNow < _gpuPresentationRetry) return;
            var request = new CancellationTokenSource(); _gpuImageRequest = request;
            _gpuPresentationTask = PresentGpuImageAsync(_activeTabPath, source, _displayedImageRevision, request);
        }

        private async Task PresentGpuImageAsync(string path, BitmapSource source, FileRevision revision, CancellationTokenSource request)
        {
            GpuImageLease lease = null; Exception failure = null;
            try { lease = await Task.Run(() => _services.GpuImages.Acquire(path, source, revision, request.Token)).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception error) { failure = error; }
            try
            {
                if (Dispatcher.HasShutdownStarted) return;
                await Dispatcher.InvokeAsync(new Action(delegate
                {
                    bool current = !_isClosed && _gpuImageRequest == request && !request.IsCancellationRequested && HasCurrentImage()
                        && _activeTabPath == path && Object.ReferenceEquals(source, _mainImage.Source) && revision.Matches(path) && _services.GpuImages.Enabled;
                    if (_gpuImageRequest == request) _gpuImageRequest = null;
                    if (!current) return;
                    if (lease == null)
                    {
                        _gpuPresentationError = failure == null ? _services.GpuImages.Statistics.Status : failure.Message;
                        _gpuPresentationRetry = DateTime.UtcNow.AddSeconds(3); return;
                    }
                    var surface = new D3DImage();
                    try
                    {
                        surface.Lock();
                        try
                        {
                            surface.SetBackBuffer(D3DResourceType.IDirect3DSurface9, lease.Texture.Surface, true);
                            surface.AddDirtyRect(new Int32Rect(0, 0, lease.Texture.Width, lease.Texture.Height));
                        }
                        finally { surface.Unlock(); }
                        if (!surface.IsFrontBufferAvailable) throw new InvalidOperationException("GPU front buffer is unavailable");
                        _gpuSurface = surface; _gpuImageLease = lease; lease = null; _gpuImageSource = source;
                        _gpuImage.Source = surface; _gpuImage.Visibility = Visibility.Visible; _mainImage.Opacity = 0; _gpuPresentationError = null;
                        surface.IsFrontBufferAvailableChanged += delegate
                        {
                            if (!surface.IsFrontBufferAvailable && !_isClosed)
                                Dispatcher.BeginInvoke(new Action(delegate
                                {
                                    if (_gpuSurface != surface) return;
                                    ReleaseGpuPresentation(); _gpuPresentationRetry = DateTime.UtcNow.AddSeconds(3);
                                }));
                        };
                    }
                    catch (Exception error)
                    {
                        if (_gpuSurface == surface) ReleaseGpuPresentation();
                        try { surface.Lock(); try { surface.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero); } finally { surface.Unlock(); } } catch { }
                        _gpuPresentationError = error.Message; _gpuPresentationRetry = DateTime.UtcNow.AddSeconds(3);
                    }
                }));
            }
            catch (OperationCanceledException) { }
            finally { if (lease != null) lease.Dispose(); request.Dispose(); }
        }

        private void BuildImageGpuConfiguration()
        {
            _imageGpuEnabledCheck = new CheckBox { Content = "GPU-resident image rendering" };
            _imageGpuJpegCheck = new CheckBox { Content = "NVIDIA GPU-assisted JPEG decoding" };
            _imageGpuCombo = new ComboBox { MinWidth = 190, MaxWidth = 370 };
            _imageGpuCombo.Items.Add(new GpuChoice { Key = "Auto", Label = "Automatic (prefer discrete GPU)" });
            foreach (var device in GpuHardware.Devices) _imageGpuCombo.Items.Add(new GpuChoice { Key = device.Key, Label = device.DisplayName });
            _imageGpuBudget = GpuLimitSlider(128, 24576, 128); _imageGpuBudgetText = ConfigurationValue(); _imageGpuDiagnostics = ConfigurationValue();
            _imageGpuEnabledCheck.ToolTip = "Keep decoded images in shared GPU textures. Unsupported sizes, animation, lost devices and memory pressure use the WPF fallback.";
            _imageGpuJpegCheck.ToolTip = "nvJPEG hybrid CPU/GPU decoding on NVIDIA adapters. ICC-enabled and unsupported JPEGs use the existing CPU decoder. No change to thumbnail decoding.";
            _imageGpuBudget.ToolTip = "Per-window resident texture allowance. Windows' live DXGI budget can reduce it. Driver, CUDA, WPF and AI working memory are additional, not a hard total-VRAM cap.";
            AutomationProperties.SetName(_imageGpuCombo, "Image rendering GPU"); AutomationProperties.SetName(_imageGpuBudget, "Image VRAM budget MB");
            AddConfigurationRow(2, "Image renderer", _imageGpuEnabledCheck);
            AddConfigurationRow(2, "Image GPU", _imageGpuCombo);
            AddConfigurationRow(2, "Image VRAM limit", ConfigurationInline(_imageGpuBudget, _imageGpuBudgetText));
            AddConfigurationRow(2, "JPEG decoder", _imageGpuJpegCheck);
            AddConfigurationRow(2, "Image GPU status", _imageGpuDiagnostics);
            _imageGpuEnabledCheck.Checked += delegate { ReadImageGpuSettings(); }; _imageGpuEnabledCheck.Unchecked += delegate { ReadImageGpuSettings(); };
            _imageGpuJpegCheck.Checked += delegate { ReadImageGpuSettings(); }; _imageGpuJpegCheck.Unchecked += delegate { ReadImageGpuSettings(); };
            _imageGpuCombo.SelectionChanged += delegate { ReadImageGpuSettings(); }; _imageGpuBudget.ValueChanged += delegate { ReadImageGpuSettings(); };
            RestoreImageGpuSettings(new SessionState());
        }

        private void ReadImageGpuSettings()
        {
            if (_restoringImageGpu || _imageGpuCombo.SelectedItem == null) return;
            bool jpegChanged = _services.GpuImages.JpegEnabled != (_imageGpuJpegCheck.IsChecked == true);
            ReleaseGpuPresentation(); _gpuPresentationRetry = DateTime.MinValue;
            _services.GpuImages.Configure(_imageGpuEnabledCheck.IsChecked == true, ((GpuChoice)_imageGpuCombo.SelectedItem).Key,
                (int)_imageGpuBudget.Value, _imageGpuJpegCheck.IsChecked == true);
            _imageGpuBudgetText.Text = ((int)_imageGpuBudget.Value) + " MB";
            if (jpegChanged) _services.VramCache.Clear();
            UpdateGpuPresentation(); ScheduleSessionSave();
        }

        private void RestoreImageGpuSettings(SessionState state)
        {
            if (_imageGpuCombo == null) return;
            _restoringImageGpu = true;
            try
            {
                string key = String.IsNullOrEmpty(state.ImageGpuKey) ? "Auto" : state.ImageGpuKey;
                var choice = _imageGpuCombo.Items.Cast<GpuChoice>().FirstOrDefault(c => c.Key == key);
                if (choice == null) { choice = new GpuChoice { Key = key, Label = "Unavailable GPU (WPF fallback)" }; _imageGpuCombo.Items.Add(choice); }
                _imageGpuCombo.SelectedItem = choice;
                _imageGpuEnabledCheck.IsChecked = state.GpuImagesEnabled ?? true; _imageGpuJpegCheck.IsChecked = state.GpuJpegEnabled ?? true;
                _imageGpuBudget.Value = state.ImageGpuBudgetMb >= 128 && state.ImageGpuBudgetMb <= 24576 ? state.ImageGpuBudgetMb : 4096;
            }
            finally { _restoringImageGpu = false; }
            ReadImageGpuSettings();
        }

        private void CaptureImageGpuSettings(SessionState state)
        {
            state.GpuImagesEnabled = _services.GpuImages.Enabled; state.GpuJpegEnabled = _services.GpuImages.JpegEnabled;
            state.ImageGpuKey = _services.GpuImages.AdapterKey; state.ImageGpuBudgetMb = _services.GpuImages.LimitMb;
        }

        private string ImageGpuSummary()
        {
            var stats = _services.GpuImages.Statistics;
            if (_imageGpuDiagnostics != null)
                _imageGpuDiagnostics.Text = (stats.Adapter ?? "WPF fallback") + "\nResident textures: " + ImageExtensions.FormatBytes(stats.Used)
                    + " / effective " + ImageExtensions.FormatBytes(stats.Target) + "\nWindows process budget: " + (stats.BudgetAvailable ? ImageExtensions.FormatBytes(stats.WindowsBudget) : "unavailable")
                    + "; process usage: " + (stats.BudgetAvailable ? ImageExtensions.FormatBytes(stats.ProcessUsage) : "unavailable") + "\nTextures: " + stats.Count + "; hits: " + stats.Hits
                    + "; uploads: " + stats.Uploads + "; GPU JPEGs: " + stats.JpegDecodes + "; evictions: " + stats.Evictions
                    + "\n" + (_gpuPresentationError ?? stats.Status);
            return "Image VRAM " + ImageExtensions.FormatBytes(stats.Used) + " / " + _services.GpuImages.LimitMb + " MB"
                + (HasCurrentImage() ? (_gpuImageLease == null ? " (WPF)" : " (GPU)") : "");
        }
    }
}
