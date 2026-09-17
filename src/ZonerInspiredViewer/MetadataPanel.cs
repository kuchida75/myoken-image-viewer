using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed class MetadataPanel : ScrollViewer
    {
        private readonly StackPanel _stack;

        public MetadataPanel()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            ThemeManager.Bind(this, Control.BackgroundProperty, ThemeKeys.PaneBackground);
            _stack = new StackPanel();
            _stack.Margin = new Thickness(14);
            Content = _stack;
            ShowMessage("Select an image");
        }

        public void ShowMessage(string message)
        {
            _stack.Children.Clear();
            _stack.Children.Add(Header("Metadata"));
            var text = new TextBlock();
            text.Text = message;
            text.Margin = new Thickness(0, 10, 0, 0);
            ThemeManager.Bind(text, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            text.TextWrapping = TextWrapping.Wrap;
            _stack.Children.Add(text);
        }

        public void ShowMetadata(ImageMetadata metadata)
        {
            _stack.Children.Clear();
            _stack.Children.Add(Header("Metadata"));

            if (metadata == null)
            {
                ShowMessage("No metadata available");
                return;
            }

            AddPathBlock(metadata.Path);
            for (int i = 0; i < metadata.Rows.Count; i++)
            {
                AddRow(metadata.Rows[i].Name, metadata.Rows[i].Value);
            }
        }

        private void AddPathBlock(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return;
            }

            AddRow("File", Path.GetFileName(path));
            AddRow("Folder", Path.GetDirectoryName(path));
        }

        private void AddRow(string label, string value)
        {
            var border = new Border();
            border.BorderThickness = new Thickness(0, 0, 0, 1);
            ThemeManager.Bind(border, Border.BorderBrushProperty, ThemeKeys.Border);
            border.Padding = new Thickness(0, 8, 0, 8);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new TextBlock();
            left.Text = label;
            left.FontSize = 12;
            ThemeManager.Bind(left, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            left.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var right = new TextBlock();
            right.Text = value;
            right.FontSize = 12;
            ThemeManager.Bind(right, TextBlock.ForegroundProperty, ThemeKeys.Text);
            right.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);

            border.Child = grid;
            _stack.Children.Add(border);
        }

        private static TextBlock Header(string text)
        {
            var header = new TextBlock();
            header.Text = text;
            header.FontSize = 15;
            header.FontWeight = FontWeights.SemiBold;
            ThemeManager.Bind(header, TextBlock.ForegroundProperty, ThemeKeys.Text);
            return header;
        }
    }
}
