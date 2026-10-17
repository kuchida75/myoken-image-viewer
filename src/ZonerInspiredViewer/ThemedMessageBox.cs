using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal static class ThemedMessageBox
    {
        internal static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
        { return Show(null, message, caption, buttons, image, defaultResult); }

        internal static MessageBoxResult Show(Window owner, string message, string caption, MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
        {
            var window = new MessageDialogWindow(owner, message, caption, buttons, image, defaultResult);
            window.ShowDialog();
            return window.Result;
        }
    }

    internal sealed class MessageDialogWindow : Window
    {
        private readonly Dictionary<MessageBoxResult, Button> _buttons = new Dictionary<MessageBoxResult, Button>();
        private readonly MessageBoxResult _dismissResult;
        internal MessageBoxResult Result { get; private set; }

        internal MessageDialogWindow(Window owner, string message, string caption, MessageBoxButton buttons,
            MessageBoxImage image, MessageBoxResult defaultResult)
        {
            MessageBoxResult[] choices;
            switch (buttons)
            {
                case MessageBoxButton.OK: choices = new[] { MessageBoxResult.OK }; break;
                case MessageBoxButton.OKCancel: choices = new[] { MessageBoxResult.OK, MessageBoxResult.Cancel }; break;
                case MessageBoxButton.YesNo: choices = new[] { MessageBoxResult.Yes, MessageBoxResult.No }; break;
                case MessageBoxButton.YesNoCancel: choices = new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel }; break;
                default: throw new ArgumentOutOfRangeException("buttons");
            }
            if (Array.IndexOf(choices, defaultResult) < 0) defaultResult = choices[0];
            _dismissResult = Array.IndexOf(choices, MessageBoxResult.Cancel) >= 0 ? MessageBoxResult.Cancel
                : buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;
            Result = _dismissResult;

            if (owner != null && owner.IsVisible) { Owner = owner; Icon = owner.Icon; }
            Title = caption ?? BuildInfo.AppName;
            ShowInTaskbar = Owner == null;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            UseLayoutRounding = true;
            var screen = System.Windows.Forms.Screen.FromHandle(Owner == null ? IntPtr.Zero : new WindowInteropHelper(Owner).Handle);
            var source = Owner == null ? null : PresentationSource.FromVisual(Owner);
            var scale = source == null || source.CompositionTarget == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
            MaxWidth = Math.Max(240, screen.WorkingArea.Width * scale.M11 - 32);
            MaxHeight = Math.Max(200, screen.WorkingArea.Height * scale.M22 - 48);
            Width = Math.Min(520, MaxWidth);
            ThemeManager.PrepareWindow(this);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var body = new Grid { Margin = new Thickness(22, 22, 22, 18) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(body);
            if (image != MessageBoxImage.None)
            {
                var symbol = new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 26,
                    Text = image == MessageBoxImage.Warning ? "\uE7BA" : image == MessageBoxImage.Error ? "\uEA39"
                        : image == MessageBoxImage.Question ? "\uE897" : "\uE946",
                    Margin = new Thickness(0, 1, 16, 0), VerticalAlignment = VerticalAlignment.Top };
                ThemeManager.Bind(symbol, TextBlock.ForegroundProperty, image == MessageBoxImage.Error ? ThemeKeys.UpscaleErrorText
                    : image == MessageBoxImage.Warning ? ThemeKeys.UpscaleWorkingText : ThemeKeys.Accent);
                AutomationProperties.SetName(symbol, image.ToString());
                body.Children.Add(symbol);
            }
            var text = new TextBox { Text = message ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0), Padding = new Thickness(0), Background = Brushes.Transparent,
                FontSize = 14, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Max(80, MaxHeight - 128) };
            ThemeManager.Bind(text, Control.ForegroundProperty, ThemeKeys.Text);
            AutomationProperties.SetName(text, "Message");
            Grid.SetColumn(text, 1); body.Children.Add(text);

            var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 12, 16, 12) };
            ThemeManager.Bind(footer, Border.BackgroundProperty, ThemeKeys.ToolbarBackground);
            ThemeManager.Bind(footer, Border.BorderBrushProperty, ThemeKeys.Border);
            Grid.SetRow(footer, 1); root.Children.Add(footer);
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; footer.Child = actions;
            foreach (MessageBoxResult choice in choices)
            {
                MessageBoxResult result = choice;
                var button = new Button { Content = choice.ToString(), MinWidth = 80, MinHeight = 32, Margin = new Thickness(6, 0, 0, 0),
                    IsDefault = choice == defaultResult, IsCancel = choice == _dismissResult };
                AutomationProperties.SetName(button, choice.ToString());
                button.Click += delegate { Complete(result); };
                _buttons.Add(choice, button); actions.Children.Add(button);
            }
            Button initial = _buttons[defaultResult];
            Loaded += delegate { initial.Focus(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Escape) { e.Handled = true; Complete(_dismissResult); } };
        }

        internal void Complete(MessageBoxResult result)
        {
            if (!_buttons.ContainsKey(result)) throw new ArgumentOutOfRangeException("result");
            Result = result;
            Close();
        }
    }
}
