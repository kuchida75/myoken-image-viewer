using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private sealed class GpuChoice
        {
            public string Key, Label;
            public override string ToString() { return Label; }
        }
        private string _processingGpuKey = "Auto", _thumbnailGpuMode = "Auto";
        private int _processingGpuLimitMb = 2048, _thumbnailGpuLimitMb = 128;
        private string _appliedProcessingGpu;
        private int _appliedProcessingLimit;
        private bool _restoringGpuConfiguration;
        private ComboBox _processingGpuCombo, _thumbnailGpuCombo;
        private Slider _processingGpuLimit, _thumbnailGpuLimit;
        private TextBlock _processingGpuLimitText, _thumbnailGpuLimitText, _gpuWorkStatus;
        private WrapPanel _gpuStatusPanel;
        private string _lastGpuSummary;
        private DispatcherTimer _gpuSettingsTimer;

        private void BuildGpuConfiguration()
        {
            _gpuSettingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _gpuSettingsTimer.Tick += delegate { _gpuSettingsTimer.Stop(); ApplyGpuSettings(); };
            _processingGpuCombo = new ComboBox { MinWidth = 190, MaxWidth = 370 };
            _processingGpuCombo.Items.Add(new GpuChoice { Key = "Auto", Label = "Automatic (prefer discrete GPU)" });
            _processingGpuCombo.Items.Add(new GpuChoice { Key = "CPU", Label = "CPU" });
            foreach (var device in GpuHardware.Devices)
                _processingGpuCombo.Items.Add(new GpuChoice { Key = device.Key, Label = device.DisplayName });
            _processingGpuCombo.ToolTip = "Select the DirectML adapter for AI subject analysis and super-resolution. WPF controls the display adapter independently. Missing devices or insufficient memory fall back to CPU.";
            _thumbnailGpuCombo = new ComboBox { ItemsSource = GpuThumbnailProcessor.Modes, MinWidth = 190 };
            _thumbnailGpuCombo.ToolTip = "Auto compares complete CPU and integrated-GPU resize paths on three uncached thumbnails per format/size class. Indexing, decoding and cache encoding remain on CPU. A busy GPU falls back to CPU.";
            _processingGpuLimit = GpuLimitSlider(256, 16384, 256);
            _thumbnailGpuLimit = GpuLimitSlider(16, 1024, 16);
            _processingGpuLimitText = ConfigurationValue(); _thumbnailGpuLimitText = ConfigurationValue();
            _processingGpuLimit.ToolTip = "Soft per-process GPU admission limit for AI sessions. Checks DXGI pressure before starting work, reserving 256 MB per new model. Not a hard cap on ONNX Runtime or WPF allocations.";
            _thumbnailGpuLimit.ToolTip = "Maximum estimated texture working set for the integrated-GPU thumbnail stage. One GPU resize runs at a time; other workers continue on CPU. Driver overhead is separate.";
            AutomationProperties.SetName(_processingGpuCombo, "AI enhancement GPU"); AutomationProperties.SetName(_thumbnailGpuCombo, "Background thumbnail processor");
            AutomationProperties.SetName(_processingGpuLimit, "AI GPU soft memory limit MB"); AutomationProperties.SetName(_thumbnailGpuLimit, "Thumbnail GPU working memory limit MB");
            AddConfigurationRow(2, "AI enhancement GPU", _processingGpuCombo);
            AddConfigurationRow(2, "AI GPU soft limit", ConfigurationInline(_processingGpuLimit, _processingGpuLimitText));
            AddConfigurationRow(2, "Thumbnail processing", _thumbnailGpuCombo);
            AddConfigurationRow(2, "Thumbnail GPU limit", ConfigurationInline(_thumbnailGpuLimit, _thumbnailGpuLimitText));
            AddConfigurationRow(2, "Auto comparison", CommandPresentation.Button("\uE72C", "Recheck performance", null,
                "Restart the automatic comparison on subsequent uncached thumbnails. Use Rebuild thumbnails to compare cached folders again.",
                delegate { ApplyGpuSettings(); _services.ThumbnailGpu.Configure(_thumbnailGpuMode, _thumbnailGpuLimitMb, true); }, true));
            _gpuWorkStatus = ConfigurationValue(); AddConfigurationRow(2, "Processing status", _gpuWorkStatus);
            _processingGpuCombo.SelectionChanged += delegate { ReadGpuConfiguration(); };
            _thumbnailGpuCombo.SelectionChanged += delegate { ReadGpuConfiguration(); };
            _processingGpuLimit.ValueChanged += delegate { ReadGpuConfiguration(); };
            _thumbnailGpuLimit.ValueChanged += delegate { ReadGpuConfiguration(); };
            RestoreGpuSettings(new SessionState());
        }

        private static Slider GpuLimitSlider(double min, double max, double step)
        { return new Slider { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true, Width = 160, Margin = new Thickness(0, 0, 10, 0) }; }

        private void RestoreGpuSettings(SessionState state)
        {
            _processingGpuKey = String.IsNullOrEmpty(state.ProcessingGpuKey) ? "Auto" : state.ProcessingGpuKey;
            _thumbnailGpuMode = Array.IndexOf(GpuThumbnailProcessor.Modes, state.ThumbnailGpuMode) >= 0 ? state.ThumbnailGpuMode : "Auto";
            _processingGpuLimitMb = state.ProcessingGpuLimitMb >= 256 && state.ProcessingGpuLimitMb <= 16384 ? state.ProcessingGpuLimitMb : 2048;
            _thumbnailGpuLimitMb = state.ThumbnailGpuLimitMb >= 16 && state.ThumbnailGpuLimitMb <= 1024 ? state.ThumbnailGpuLimitMb : 128;
            if (_processingGpuCombo != null)
            {
                _restoringGpuConfiguration = true;
                var choice = _processingGpuCombo.Items.Cast<GpuChoice>().FirstOrDefault(c => c.Key == _processingGpuKey);
                if (choice == null) { choice = new GpuChoice { Key = _processingGpuKey, Label = "Unavailable GPU (CPU fallback)" }; _processingGpuCombo.Items.Add(choice); }
                _processingGpuCombo.SelectedItem = choice; _thumbnailGpuCombo.SelectedItem = _thumbnailGpuMode;
                _processingGpuLimit.Value = _processingGpuLimitMb; _thumbnailGpuLimit.Value = _thumbnailGpuLimitMb;
                _restoringGpuConfiguration = false;
            }
            ApplyGpuSettings();
        }

        private void ReadGpuConfiguration()
        {
            if (_restoringGpuConfiguration || _processingGpuCombo.SelectedItem == null || _thumbnailGpuCombo.SelectedItem == null) return;
            _processingGpuKey = ((GpuChoice)_processingGpuCombo.SelectedItem).Key;
            _thumbnailGpuMode = (string)_thumbnailGpuCombo.SelectedItem;
            _processingGpuLimitMb = (int)_processingGpuLimit.Value; _thumbnailGpuLimitMb = (int)_thumbnailGpuLimit.Value;
            UpdateGpuLimitLabels(); _gpuSettingsTimer.Stop(); _gpuSettingsTimer.Start(); ScheduleSessionSave();
        }

        private void UpdateGpuLimitLabels()
        {
            if (_processingGpuLimitText == null) return;
            _processingGpuLimitText.Text = _processingGpuLimitMb + " MB"; _thumbnailGpuLimitText.Text = _thumbnailGpuLimitMb + " MB";
        }

        private void ApplyGpuSettings()
        {
            if (_gpuSettingsTimer != null) _gpuSettingsTimer.Stop();
            _services.ThumbnailGpu.Configure(_thumbnailGpuMode, _thumbnailGpuLimitMb);
            bool changed = _appliedProcessingGpu != _processingGpuKey || _appliedProcessingLimit != _processingGpuLimitMb;
            _appliedProcessingGpu = _processingGpuKey; _appliedProcessingLimit = _processingGpuLimitMb;
            if (changed)
            {
                CancelEnhancement(); ReleaseSubjectSegmentation();
                if (_restored && HasCurrentImage()) ApplyQuickEnhance();
            }
            UpdateGpuLimitLabels();
        }

        private SubjectSegmentation CreateSubjectDetector()
        {
            var adapter = GpuHardware.SelectProcessing(_processingGpuKey, GpuHardware.Devices);
            return new SubjectSegmentation(_services.CpuWorkerCount, adapter != null, null, adapter, _processingGpuLimitMb);
        }

        private void CaptureGpuSettings(SessionState state)
        {
            state.ProcessingGpuKey = _processingGpuKey; state.ProcessingGpuLimitMb = _processingGpuLimitMb;
            state.ThumbnailGpuMode = _thumbnailGpuMode; state.ThumbnailGpuLimitMb = _thumbnailGpuLimitMb;
        }

        private void UpdateGpuDisplay(SystemGpuMemorySnapshot snapshot)
        {
            if (_gpuStatusPanel != null && _lastGpuSummary != snapshot.AdapterSummary)
            {
                _lastGpuSummary = snapshot.AdapterSummary; _gpuStatusPanel.Children.Clear();
                foreach (var sample in snapshot.Adapters)
                {
                    var text = new TextBlock { Width = 430, Margin = new Thickness(0, 0, 22, 0), FontSize = 11,
                        TextWrapping = TextWrapping.Wrap, MaxWidth = Math.Max(100, _gpuStatusPanel.ActualWidth - 24),
                        ToolTip = sample.Adapter.DisplayName + "\nAdapter number used by the viewer's GPU selector.\nSystem-wide usage across all processes. Cyan: dedicated memory used. Amber: shared RAM used. Grey: dedicated capacity.\nShared memory is system RAM, not additional dedicated VRAM." };
                    text.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
                    text.Inlines.Add(GpuStatusRun("GPU " + sample.Adapter.Index, ThemeKeys.GpuDedicatedValue, true));
                    text.Inlines.Add(GpuStatusRun(" - " + sample.Adapter.ShortName, ThemeKeys.Text, true));
                    text.Inlines.Add(new Run("   VRAM "));
                    text.Inlines.Add(GpuStatusRun(sample.DedicatedText, sample.DedicatedUsed.HasValue ? ThemeKeys.GpuDedicatedValue : ThemeKeys.MutedText, sample.DedicatedUsed.HasValue));
                    if (sample.Adapter.CapacityBytes > 0) text.Inlines.Add(new Run(" / " + ImageExtensions.FormatBytes(sample.Adapter.CapacityBytes)));
                    text.Inlines.Add(new Run("   | shared "));
                    text.Inlines.Add(GpuStatusRun(sample.SharedText, sample.SharedUsed.HasValue ? ThemeKeys.GpuSharedValue : ThemeKeys.MutedText, sample.SharedUsed.HasValue));
                    AutomationProperties.SetName(text, sample.Summary);
                    ThemeManager.Bind(text, TextBlock.ForegroundProperty, ThemeKeys.MutedText); _gpuStatusPanel.Children.Add(text);
                }
            }
            if (_gpuWorkStatus != null)
            {
                var device = GpuHardware.SelectProcessing(_processingGpuKey, GpuHardware.Devices);
                _gpuWorkStatus.Text = "AI: " + (_subjectSegmentation == null ? (device == null ? "CPU" : device.Name + " (idle)") : _subjectSegmentation.Backend)
                    + "\nThumbnails: " + _services.ThumbnailGpu.Status + "\nGPU resizes: " + _services.ThumbnailGpu.GpuJobs;
            }
        }

        private static Run GpuStatusRun(string value, string color, bool emphasized)
        {
            var run = new Run(value) { FontWeight = emphasized ? FontWeights.SemiBold : FontWeights.Normal };
            run.SetResourceReference(TextElement.ForegroundProperty, color);
            return run;
        }
    }
}
