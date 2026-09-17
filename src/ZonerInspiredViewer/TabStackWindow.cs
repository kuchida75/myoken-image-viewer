using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;

namespace ZonerInspiredViewer
{
    internal sealed class StackTabChoice
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Path { get; set; }
        public bool Selected { get; set; }
    }

    internal sealed class TabStackWindow : Window
    {
        private readonly TextBox _name;
        private readonly TextBlock _error;
        private readonly List<StackTabChoice> _choices;
        public string StackName { get { return _name.Text.Trim(); } }
        public string[] SelectedIds { get { return _choices.Where(item => item.Selected).Select(item => item.Id).ToArray(); } }

        public TabStackWindow(string name, List<StackTabChoice> choices, bool creating)
        {
            Title = creating ? "New tab stack" : "Manage tab stack";
            Width = 550; Height = 480; MinWidth = 400; MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ThemeManager.PrepareWindow(this);
            _choices = choices;
            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new TextBlock { Text = "Stack name", Margin = new Thickness(0, 0, 0, 6) });
            _name = new TextBox { Text = name, MaxLength = 80, Margin = new Thickness(0, 0, 0, 16) };
            AutomationProperties.SetName(_name, "Stack name");
            Grid.SetRow(_name, 1); root.Children.Add(_name);
            var title = new TextBlock { Text = "Tabs", Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(title, 2); root.Children.Add(title);
            var list = new ListBox { ItemsSource = choices, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ThemeManager.Bind(list, Control.BackgroundProperty, ThemeKeys.PaneBackground);
            ThemeManager.Bind(list, Control.ForegroundProperty, ThemeKeys.Text);
            ThemeManager.Bind(list, Control.BorderBrushProperty, ThemeKeys.Border);
            AutomationProperties.SetName(list, "Tabs in stack");
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            var check = new FrameworkElementFactory(typeof(CheckBox));
            check.SetValue(Control.TemplateProperty, XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type CheckBox}'>
 <Grid Background='Transparent'>
  <Grid.ColumnDefinitions><ColumnDefinition Width='22'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
  <Border x:Name='Box' Width='16' Height='16' HorizontalAlignment='Left' VerticalAlignment='Center'
   Background='{DynamicResource Theme.ControlBackground}' BorderBrush='{DynamicResource Theme.SubtleText}' BorderThickness='1' CornerRadius='2'>
   <TextBlock x:Name='Tick' Text='&#xE73E;' FontFamily='Segoe MDL2 Assets' FontSize='11' Visibility='Collapsed'
    Foreground='{DynamicResource Theme.AccentText}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
  </Border>
  <ContentPresenter Grid.Column='1' VerticalAlignment='Center'/>
 </Grid>
 <ControlTemplate.Triggers>
  <Trigger Property='IsChecked' Value='True'>
   <Setter TargetName='Box' Property='Background' Value='{DynamicResource Theme.Accent}'/>
   <Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Theme.Accent}'/>
   <Setter TargetName='Tick' Property='Visibility' Value='Visible'/>
  </Trigger>
  <Trigger Property='IsKeyboardFocused' Value='True'>
   <Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Theme.Text}'/>
  </Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>"));
            check.SetBinding(ToggleButton.IsCheckedProperty, new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            check.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Path"));
            check.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 6, 4, 6));
            var label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetBinding(TextBlock.TextProperty, new Binding("Label"));
            label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            check.AppendChild(label);
            list.ItemTemplate = new DataTemplate { VisualTree = check };
            Grid.SetRow(list, 3); root.Children.Add(list);
            _error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            ThemeManager.Bind(_error, TextBlock.ForegroundProperty, ThemeKeys.Accent);
            Grid.SetRow(_error, 4); root.Children.Add(_error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var save = new Button { Content = creating ? "Create stack" : "Save", MinWidth = 100, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            save.Click += delegate
            {
                if (!TabStacks.ValidName(StackName)) { _error.Text = "Enter a stack name of 1-80 characters without control characters."; return; }
                if (SelectedIds.Length == 0) { _error.Text = "Select at least one tab."; return; }
                DialogResult = true;
            };
            buttons.Children.Add(save);
            buttons.Children.Add(new Button { Content = "Cancel", MinWidth = 80, IsCancel = true });
            Grid.SetRow(buttons, 5); root.Children.Add(buttons);
            Content = root;
            Loaded += delegate { _name.Focus(); _name.SelectAll(); };
        }
    }
}
