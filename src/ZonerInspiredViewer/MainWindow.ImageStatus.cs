using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private TextBlock _imageStatusPath;
        private readonly TextBlock[] _imageStatusValues = new TextBlock[6];
        private static readonly string[] ImageStatusLabels = { "Size: ", "Type: ", "Name: ", "Modified: ", "Resolution: ", "Depth: " };

        private void BuildImageStatusBar(StackPanel footer, WrapPanel controls)
        {
            _imageStatusPath = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 0, 0, 4), Visibility = Visibility.Collapsed };
            AutomationProperties.SetName(_imageStatusPath, "Image file path");
            ThemeManager.Bind(_imageStatusPath, TextBlock.ForegroundProperty, ThemeKeys.Text);
            footer.Children.Insert(0, _imageStatusPath);
            for (int i = 0; i < _imageStatusValues.Length; i++)
            {
                var value = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 14, 4), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
                ThemeManager.Bind(value, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
                AutomationProperties.SetName(value, "Image " + ImageStatusLabels[i].TrimEnd(':', ' ').ToLowerInvariant());
                _imageStatusValues[i] = value; controls.Children.Add(value);
            }
            BuildFilmstripSizeControl(controls);
            BuildUpscaleSizeControl(controls);
            controls.SizeChanged += delegate
            {
                for (int i = 0; i < _imageStatusValues.Length; i++)
                    _imageStatusValues[i].MaxWidth = Math.Max(1, Math.Min(i == 2 ? 240 : Double.MaxValue, controls.ActualWidth - 14));
            };
        }

        private void UpdateImageStatusBar(string[] details)
        {
            if (_imageStatusPath == null) return;
            bool visible = details != null;
            _filmstripSizeControls.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            UpdateUpscaleSizeControl();
            _imageStatusPath.Text = visible ? _displayedImagePath : "";
            _imageStatusPath.ToolTip = visible ? _displayedImagePath : null;
            _imageStatusPath.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < _imageStatusValues.Length; i++)
            {
                TextBlock value = _imageStatusValues[i];
                value.Text = visible ? ImageStatusLabels[i] + details[i] : "";
                value.ToolTip = visible ? value.Text : null;
                value.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
