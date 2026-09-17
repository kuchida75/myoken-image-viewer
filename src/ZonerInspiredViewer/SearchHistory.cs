using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;

namespace ZonerInspiredViewer
{
    internal sealed class SearchHistory
    {
        private readonly List<string> _entries = new List<string>();
        internal int Generation { get; private set; }
        internal event Action Changed;
        internal List<string> Capture() { return new List<string>(_entries); }
        internal void Restore(IEnumerable<string> values)
        {
            Generation++;
            _entries.Clear();
            _entries.AddRange((values ?? new string[0]).Where(s => !String.IsNullOrWhiteSpace(s) && s.Length <= 4096)
                .Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
        }
        internal void Record(string text)
        {
            string value = (text ?? "").Trim();
            if (value.Length == 0 || value.Length > 4096 || (_entries.Count > 0 && _entries[0] == value)) return;
            _entries.RemoveAll(s => String.Equals(s, value, StringComparison.OrdinalIgnoreCase));
            _entries.Insert(0, value);
            if (_entries.Count > 5) _entries.RemoveRange(5, _entries.Count - 5);
            if (Changed != null) Changed();
        }
        internal void Clear() { Generation++; _entries.Clear(); if (Changed != null) Changed(); }
    }

    internal sealed class SearchHistoryInput
    {
        private static readonly ControlTemplate MenuTemplate = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ContextMenu'>
 <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Padding='2'>
  <ItemsPresenter/>
 </Border>
</ControlTemplate>");
        private static readonly ControlTemplate SeparatorTemplate = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Separator'>
 <Border Background='{DynamicResource Theme.Border}' Height='1' Margin='8,4'/>
</ControlTemplate>");
        private static readonly ControlTemplate ItemTemplate = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='MenuItem'>
 <Border x:Name='Row' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{TemplateBinding Background}' Padding='10,6'>
  <ContentPresenter ContentSource='Header' RecognizesAccessKey='False' VerticalAlignment='Center'/>
 </Border>
 <ControlTemplate.Triggers>
  <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Row' Property='Background' Value='{DynamicResource Theme.ControlHover}'/></Trigger>
  <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>");
        private readonly TextBox _input;
        private readonly SearchHistory _history;
        private readonly Action _search;
        private bool _pending;
        private int _editGeneration;
        internal readonly Button Button;

        internal SearchHistoryInput(TextBox input, SearchHistory history, Action search)
        {
            _input = input; _history = history; _search = search;
            Button = CommandPresentation.Button("\uE70D", "Search history", "Alt+Down", "Show the last five searches or clear history.", Show, false);
            Button.Width = 28; Button.Height = 30; Button.Margin = new Thickness(0, 0, 8, 0);
            input.TextChanged += delegate { if (input.IsKeyboardFocusWithin) { _pending = true; _editGeneration = _history.Generation; } };
            input.LostKeyboardFocus += delegate { Commit(); };
            input.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt) { Show(); e.Handled = true; }
                else if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
                { Commit(true); _search(); e.Handled = true; }
            };
        }

        internal void Commit(bool submitted = false)
        {
            if (submitted) { _pending = true; _editGeneration = _history.Generation; }
            if (!_pending) return;
            _pending = false;
            if (_editGeneration == _history.Generation) _history.Record(_input.Text);
        }
        internal void DiscardPending() { _pending = false; }
        internal ContextMenu BuildMenu()
        {
            var menu = new ContextMenu { MinWidth = 210, Template = MenuTemplate };
            var style = new Style(typeof(MenuItem), (Style)Application.Current.FindResource(typeof(MenuItem)));
            style.Setters.Add(new Setter(Control.TemplateProperty, ItemTemplate));
            menu.Resources[typeof(MenuItem)] = style;
            foreach (string text in _history.Capture())
            {
                string query = text;
                var item = new MenuItem { Header = new TextBlock { Text = query, MaxWidth = 320, TextTrimming = TextTrimming.CharacterEllipsis }, ToolTip = query };
                item.Click += delegate
                {
                    _input.Text = query; _pending = false; _history.Record(query); _search();
                    _input.Focus(); _input.SelectAll();
                };
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "No recent searches", IsEnabled = false });
            menu.Items.Add(new Separator { Template = SeparatorTemplate });
            var clear = new MenuItem { Header = "Clear history", IsEnabled = _history.Capture().Count > 0 };
            clear.Click += delegate { _pending = false; _history.Clear(); };
            menu.Items.Add(clear);
            return menu;
        }

        private void Show()
        {
            Commit();
            ContextMenu menu = BuildMenu(); menu.PlacementTarget = Button; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true;
        }
    }
}
