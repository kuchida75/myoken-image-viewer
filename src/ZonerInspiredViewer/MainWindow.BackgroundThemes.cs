using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private ToolbarBackground _toolbarBackground, _backgroundPreview;
        private ComboBox _backgroundPattern, _backgroundFade;
        private Slider _backgroundIntensity;
        private TextBlock _backgroundIntensityText, _backgroundColorText, _backgroundPreviewTitle;
        private readonly Dictionary<string, ToggleButton> _backgroundSwatches = new Dictionary<string, ToggleButton>();
        private bool _syncBackgroundControls;

        private void BuildBackgroundConfiguration()
        {
            var preview = new Grid { Height = 82, Margin = new Thickness(0, 0, 0, 18), ClipToBounds = true };
            _backgroundPreview = new ToolbarBackground(); preview.Children.Add(_backgroundPreview);
            _backgroundPreviewTitle = new TextBlock { Margin = new Thickness(16), FontSize = 15, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center };
            ThemeManager.Bind(_backgroundPreviewTitle, TextBlock.ForegroundProperty, ThemeKeys.Text);
            preview.Children.Add(_backgroundPreviewTitle);
            AutomationProperties.SetName(preview, "Toolbar background preview");
            _configurationPages[4].Children.Add(preview);
            _backgroundPattern = BackgroundOptions(ToolbarBackground.Patterns, "Toolbar background pattern");
            _backgroundFade = BackgroundOptions(ToolbarBackground.Fades, "Background fade direction");
            _backgroundIntensity = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true,
                Width = 180, ToolTip = "Background intensity (0-100%)" };
            AutomationProperties.SetName(_backgroundIntensity, "Background intensity percent");
            _backgroundIntensityText = new TextBlock { Width = 48, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _backgroundPattern.SelectionChanged += delegate { ReadBackgroundControls(); };
            _backgroundFade.SelectionChanged += delegate { ReadBackgroundControls(); };
            _backgroundIntensity.ValueChanged += delegate { ReadBackgroundControls(); };
            AddConfigurationRow(4, "Pattern", _backgroundPattern);
            AddConfigurationRow(4, "Fade", _backgroundFade);
            var colors = new WrapPanel();
            string[] names = { "Graphite", "Teal", "Ocean", "Rose", "Amber", "Emerald" };
            string[] codes = { "#B9C2CC", ToolbarBackground.DefaultColor, "#74A9E4", "#D98BA4", "#E5B663", "#93C890" };
            for (int i = 0; i < codes.Length; i++)
            {
                string code = codes[i];
                var button = new ToggleButton { Width = 32, Height = 32, Margin = new Thickness(0, 0, 6, 6),
                    ToolTip = names[i] + " (" + code + ")" };
                var sample = new Border { Width = 22, Height = 22, Background = TabColorBrush(code) };
                var mark = new TextBlock { Text = "\uE73E", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12,
                    Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                sample.Child = mark;
                button.Checked += delegate { mark.Visibility = Visibility.Visible; };
                button.Unchecked += delegate { mark.Visibility = Visibility.Collapsed; };
                mark.Visibility = Visibility.Collapsed; button.Content = sample;
                AutomationProperties.SetName(button, names[i] + " background color");
                button.Click += delegate { SetBackgroundTheme(_toolbarBackground.Pattern, code, _toolbarBackground.Fade, _toolbarBackground.Intensity); };
                colors.Children.Add(button); _backgroundSwatches.Add(code, button);
            }
            colors.Children.Add(AdvancedSearchWindow.IconButton("\uE790", "Choose custom background color", ChooseBackgroundColor));
            _backgroundColorText = new TextBlock { Margin = new Thickness(0, 2, 0, 0) };
            var palette = new StackPanel(); palette.Children.Add(colors); palette.Children.Add(_backgroundColorText);
            AddConfigurationRow(4, "Color", palette);
            AddConfigurationRow(4, "Intensity", ConfigurationInline(_backgroundIntensity, _backgroundIntensityText));
            var reset = CommandPresentation.Button("\uE777", "Reset background", null, "Restore Crosshatch, Teal, right-edge fade and 100% intensity.",
                delegate { SetBackgroundTheme(ToolbarBackground.DefaultPattern, ToolbarBackground.DefaultColor, "Right edge", ToolbarBackground.DefaultIntensity); }, true);
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            AddConfigurationRow(4, "", reset);
            SetBackgroundTheme(_toolbarBackground.Pattern, _toolbarBackground.ColorHex, _toolbarBackground.Fade, _toolbarBackground.Intensity);
        }

        private static ComboBox BackgroundOptions(string[] choices, string name)
        {
            var box = new ComboBox { Width = 220, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = name };
            foreach (string choice in choices) box.Items.Add(choice);
            AutomationProperties.SetName(box, name); return box;
        }

        private void ReadBackgroundControls()
        {
            if (_syncBackgroundControls || _backgroundPattern.SelectedItem == null || _backgroundFade.SelectedItem == null) return;
            SetBackgroundTheme((string)_backgroundPattern.SelectedItem, _toolbarBackground.ColorHex,
                (string)_backgroundFade.SelectedItem, _backgroundIntensity.Value);
        }

        private void SetBackgroundTheme(string pattern, string color, string fade, double intensity)
        {
            _toolbarBackground.Configure(pattern, color, fade, intensity);
            if (_backgroundPreview != null)
            {
                _syncBackgroundControls = true;
                try
                {
                    _backgroundPreview.Configure(_toolbarBackground.Pattern, _toolbarBackground.ColorHex, _toolbarBackground.Fade, _toolbarBackground.Intensity);
                    _backgroundPreviewTitle.Text = _toolbarBackground.Pattern == "None" ? "Plain toolbar" : _toolbarBackground.Pattern;
                    _backgroundPattern.SelectedItem = _toolbarBackground.Pattern;
                    _backgroundFade.SelectedItem = _toolbarBackground.Fade;
                    _backgroundIntensity.Value = _toolbarBackground.Intensity;
                    _backgroundIntensityText.Text = _toolbarBackground.Intensity.ToString("0") + "%";
                    _backgroundColorText.Text = _toolbarBackground.ColorHex;
                    foreach (var pair in _backgroundSwatches) pair.Value.IsChecked = pair.Key == _toolbarBackground.ColorHex;
                    _backgroundFade.IsEnabled = _backgroundIntensity.IsEnabled = _toolbarBackground.Pattern != "None";
                }
                finally { _syncBackgroundControls = false; }
            }
            ScheduleSessionSave();
        }

        private void ChooseBackgroundColor()
        {
            Color current = ((SolidColorBrush)TabColorBrush(_toolbarBackground.ColorHex)).Color;
            using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true,
                Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B) })
                if (dialog.ShowDialog(new ColorDialogOwner(_configureWindow ?? this)) == System.Windows.Forms.DialogResult.OK)
                    SetBackgroundTheme(_toolbarBackground.Pattern, String.Format("#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B),
                        _toolbarBackground.Fade, _toolbarBackground.Intensity);
        }
    }
}
