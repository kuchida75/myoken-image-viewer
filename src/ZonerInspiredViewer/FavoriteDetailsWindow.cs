using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace ZonerInspiredViewer
{
    internal sealed class FavoriteDetailsWindow : Window
    {
        private readonly TextBox _name;
        private readonly TextBox _description;

        internal FavoriteDetailsWindow(FavoriteFolderDetails details)
        {
            Title = "Rename favorite";
            Width = 540;
            Height = 370;
            MinWidth = 400;
            MinHeight = 340;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ThemeManager.PrepareWindow(this);
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var path = new TextBlock { Text = details.Path, TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = details.Path, Margin = new Thickness(0, 0, 0, 14) };
            ThemeManager.Bind(path, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            root.Children.Add(path);
            var namePanel = new StackPanel();
            namePanel.Children.Add(new TextBlock { Text = "Favorite name", Margin = new Thickness(0, 0, 0, 6) });
            _name = new TextBox { Text = details.Name ?? "", MaxLength = 200, Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(_name, "Favorite name");
            namePanel.Children.Add(_name);
            Grid.SetRow(namePanel, 1);
            root.Children.Add(namePanel);
            var label = new TextBlock { Text = "Description", Margin = new Thickness(0, 14, 0, 6) };
            Grid.SetRow(label, 2);
            root.Children.Add(label);
            _description = new TextBox { Text = details.Description ?? "", MaxLength = 2000, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6) };
            AutomationProperties.SetName(_description, "Favorite description");
            Grid.SetRow(_description, 3);
            root.Children.Add(_description);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0) };
            var reset = new Button { Content = "Reset name", MinWidth = 92, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            reset.Click += delegate { _name.Text = ""; };
            buttons.Children.Add(reset);
            var save = new Button { Content = "Save", MinWidth = 76, Height = 30, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            save.Click += delegate { DialogResult = true; };
            buttons.Children.Add(save);
            buttons.Children.Add(new Button { Content = "Cancel", MinWidth = 76, Height = 30, IsCancel = true });
            Grid.SetRow(buttons, 4);
            root.Children.Add(buttons);
            var frame = new Border { Padding = new Thickness(16), Child = root };
            ThemeManager.Bind(frame, Border.BackgroundProperty, ThemeKeys.WindowBackground);
            Content = frame;
            Loaded += delegate { _name.Focus(); _name.SelectAll(); };
        }

        public static FavoriteFolderDetails Edit(Window owner, FavoriteFolderDetails details)
        {
            var window = new FavoriteDetailsWindow(details) { Owner = owner };
            return window.ShowDialog() == true
                ? new FavoriteFolderDetails { Path = details.Path, Name = window._name.Text, Description = window._description.Text }
                : null;
        }
    }
}
