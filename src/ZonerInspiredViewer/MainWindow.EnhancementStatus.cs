using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Border _enhancementStatusOverlay;
        private TextBlock _enhancementStatusText, _manualStatusText;
        private CheckBox _configureEnhancementStatus;
        private bool _enhancementStatusEnabled = true;

        private void BuildEnhancementStatusOverlay()
        {
            _enhancementStatusText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            _manualStatusText = new TextBlock { FontSize = 12, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
            var lines = new StackPanel(); lines.Children.Add(_enhancementStatusText); lines.Children.Add(_manualStatusText);
            _enhancementStatusOverlay = new Border { Width = 280, MaxWidth = 280, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromArgb(168, 15, 18, 21)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Child = lines, ClipToBounds = true,
                Visibility = Visibility.Collapsed, IsHitTestVisible = false, Focusable = false };
            Panel.SetZIndex(_enhancementStatusOverlay, 10); _imageView.Children.Add(_enhancementStatusOverlay);
            _enhancementStatusOverlay.SizeChanged += delegate { LayoutImageOverlays(); };
            AutomationProperties.SetName(_enhancementStatusOverlay, "Enhancement status");
        }

        private void SetEnhancementStatusVisible(bool enabled)
        {
            _enhancementStatusEnabled = enabled;
            if (_configureEnhancementStatus != null) _configureEnhancementStatus.IsChecked = enabled;
            UpdateEnhancementStatus(); ScheduleSessionSave();
        }

        private void HideEnhancementStatus()
        { if (_enhancementStatusOverlay != null) { _enhancementStatusOverlay.Visibility = Visibility.Collapsed; LayoutImageOverlays(); } }

        private void UpdateEnhancementStatus()
        {
            if (_enhancementStatusOverlay == null) return;
            if (_isClosed || !_enhancementStatusEnabled || _imageView.Visibility != Visibility.Visible || !HasCurrentImage())
            { HideEnhancementStatus(); return; }

            string text = "Quick Enhance: Off"; Brush color = Brushes.LightGray;
            if (_quickEnhanceEnabled)
            {
                string mode = _advancedQuickEnhance ? "Advanced" : "Basic";
                if (HasSavedEnhance(CurrentTabState())) { text = "Quick Enhance: " + mode + " (saved)"; color = Brushes.LightSkyBlue; }
                else if (_enhancementRequest != null) { text = "Quick Enhance: " + mode + " (processing)"; color = Brushes.Khaki; }
                else if (_mainImage.Effect != null) { text = "Quick Enhance: " + mode + " (on)"; color = Brushes.PaleGreen; }
                else { text = "Quick Enhance: " + mode + (_enhancementSummary == null ? " (not applied)" : " (unavailable)"); color = Brushes.LightSalmon; }
            }
            _enhancementStatusText.Text = text; _enhancementStatusText.Foreground = color;
            bool manual = _manualImageSurface.Effect != null;
            _manualStatusText.Text = manual ? "Manual adjustments: On" : "Manual adjustments: Neutral";
            _manualStatusText.Visibility = manual || _manualEnhanceVisible ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(_enhancementStatusOverlay, text + (_manualStatusText.Visibility == Visibility.Visible ? ". " + _manualStatusText.Text : ""));
            AutomationProperties.SetHelpText(_enhancementStatusOverlay, _enhancementSummary ?? "View-only enhancement status. Original files change only when explicitly saved.");
            _enhancementStatusOverlay.Visibility = Visibility.Visible;
            LayoutImageOverlays();
        }

        private double LayoutEnhancementStatus()
        {
            if (_enhancementStatusOverlay == null || _enhancementStatusOverlay.Visibility != Visibility.Visible) return 0;
            // Keep a stable top-right lane for the transient zoom factor, even while it is hidden.
            _enhancementStatusOverlay.MaxWidth = Math.Max(0, _imageView.ActualWidth - 32 - 120);
            _enhancementStatusOverlay.MaxHeight = Math.Max(0, Math.Min(78, (_imageView.ActualHeight - 32) * .3));
            return _enhancementStatusOverlay.ActualHeight + 8;
        }
    }
}
