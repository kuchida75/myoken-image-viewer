using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed class TextPromptWindow : Window
    {
        private readonly TextBox _textBox;
        private string _result;

        private TextPromptWindow(string title, string label, string initialValue)
        {
            Title = title;
            Width = 460;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ThemeManager.PrepareWindow(this);

            var root = new Grid();
            root.Margin = new Thickness(14);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock();
            labelBlock.Text = label;
            labelBlock.Margin = new Thickness(0, 0, 0, 8);
            ThemeManager.Bind(labelBlock, TextBlock.ForegroundProperty, ThemeKeys.Text);
            Grid.SetRow(labelBlock, 0);
            root.Children.Add(labelBlock);

            _textBox = new TextBox();
            _textBox.Text = initialValue ?? "";
            _textBox.MinWidth = 400;
            _textBox.SelectAll();
            _textBox.KeyDown += TextBoxKeyDown;
            Grid.SetRow(_textBox, 1);
            root.Children.Add(_textBox);

            var buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 14, 0, 0);

            var ok = new Button();
            ok.Content = "OK";
            ok.Width = 74;
            ok.Margin = new Thickness(0, 0, 8, 0);
            ok.Click += delegate { Accept(); };
            buttons.Children.Add(ok);

            var cancel = new Button();
            cancel.Content = "Cancel";
            cancel.Width = 74;
            cancel.Click += delegate { DialogResult = false; Close(); };
            buttons.Children.Add(cancel);

            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Content = root;
            Loaded += delegate { _textBox.Focus(); };
        }

        public static string Prompt(Window owner, string title, string label, string initialValue)
        {
            var window = new TextPromptWindow(title, label, initialValue);
            window.Owner = owner;
            bool? result = window.ShowDialog();
            return result == true ? window._result : null;
        }

        private void TextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
        }

        private void Accept()
        {
            _result = _textBox.Text;
            DialogResult = true;
            Close();
        }
    }
}
