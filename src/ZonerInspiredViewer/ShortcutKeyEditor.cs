using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZonerInspiredViewer
{
    internal sealed class ShortcutKeyEditor : Window
    {
        private sealed class Assignment
        {
            internal ShortcutCommand Command;
            internal ShortcutGesture Gesture;
            public override string ToString() { return Command == null ? "New assignment" : Gesture + " | " + Command; }
        }

        private readonly ShortcutMap _map;
        private readonly Key _key;
        private readonly ComboBox _assignment, _command;
        private readonly TextBox _gestureText;
        private readonly TextBlock _description, _status;
        private readonly Button _remove;
        private ShortcutGesture _gesture;
        private bool _recording;
        internal event Action BindingsChanged;

        internal ShortcutKeyEditor(ShortcutMap map, Key key, int scope, ShortcutCommand preferred)
        {
            _map = map; _key = key;
            Title = "Rebind key - " + WindowsKeyboardLayout.Current.KeyName(key);
            Width = 550; MinWidth = 450; SizeToContent = SizeToContent.Height; MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 40);
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            ThemeManager.PrepareWindow(this);
            var body = new StackPanel { Margin = new Thickness(20) };
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var assignments = map.Commands.Where(c => (c.Scope & scope) != 0).SelectMany(c => map.Keys(c.Id)
                .Where(g => g.KeyCode == (int)key).Select(g => new Assignment { Command = c, Gesture = g })).ToList();
            assignments.Add(new Assignment());
            _assignment = new ComboBox { ItemsSource = assignments, MinHeight = 32 };
            AddField(body, "Current binding", _assignment);
            _command = new ComboBox { ItemsSource = map.Commands.OrderBy(c => c.Name).ToList(), MinHeight = 32, SelectedItem = preferred ?? map.Commands[0] };
            AddField(body, "Command", _command);
            _description = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
            ThemeManager.Bind(_description, TextBlock.ForegroundProperty, ThemeKeys.MutedText); body.Children.Add(_description);
            _gestureText = new TextBox { IsReadOnly = true, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
            AddField(body, "New keys", _gestureText);
            var record = CommandPresentation.Button("\uE765", "Record keys", null, "Capture one key combination. Escape cancels recording.", BeginRecording, true);
            record.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(record);
            _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) }; body.Children.Add(_status);
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            _remove = CommandPresentation.Button("\uE74D", "Remove binding", null, "Unassign only the selected binding.", RemoveBinding, true);
            actions.Children.Add(_remove);
            actions.Children.Add(CommandPresentation.Button("\uE74E", "Save", null, "Apply this binding; conflicting assignments remain unchanged.", SaveBinding, true));
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 76, Height = 32, Margin = new Thickness(6, 0, 0, 0) };
            cancel.Click += delegate { Close(); }; actions.Children.Add(cancel); body.Children.Add(actions);
            _assignment.SelectionChanged += delegate { SelectAssignment(); };
            _command.SelectionChanged += delegate { _description.Text = ((ShortcutCommand)_command.SelectedItem).Description; };
            _gestureText.LostKeyboardFocus += delegate { StopRecording(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (Capture(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers, e.IsRepeat)) e.Handled = true;
            };
            _assignment.SelectedIndex = 0;
        }

        private static void AddField(Panel body, string name, FrameworkElement control)
        {
            var label = new TextBlock { Text = name, Margin = new Thickness(0, 0, 0, 5) };
            ThemeManager.Bind(label, TextBlock.ForegroundProperty, ThemeKeys.MutedText); body.Children.Add(label);
            control.Margin = new Thickness(0, 0, 0, 12); AutomationProperties.SetName(control, name); body.Children.Add(control);
        }

        private void SelectAssignment()
        {
            var selected = (Assignment)_assignment.SelectedItem;
            if (selected == null) return;
            _recording = false; _status.Text = "";
            if (selected.Command != null) _command.SelectedItem = selected.Command;
            _command.IsEnabled = selected.Command == null; _remove.IsEnabled = selected.Command != null;
            _gesture = selected.Gesture == null ? ShortcutMap.Gesture(_key, ModifierKeys.None) : selected.Gesture.Copy();
            _gestureText.Text = _gesture.ToString(); _description.Text = ((ShortcutCommand)_command.SelectedItem).Description;
        }

        private void BeginRecording()
        { _recording = true; _gestureText.Text = "Press a key combination"; _status.Text = ""; _gestureText.Focus(); }
        private void StopRecording() { _recording = false; if (_gesture != null) _gestureText.Text = _gesture.ToString(); }

        internal void RefreshLayout()
        {
            Title = "Rebind key - " + WindowsKeyboardLayout.Current.KeyName(_key);
            _assignment.Items.Refresh();
            if (!_recording && _gesture != null) _gestureText.Text = _gesture.ToString();
        }

        internal bool Capture(Key key, ModifierKeys modifiers, bool repeat)
        {
            if (!_recording) return false;
            if (key == Key.Escape) { StopRecording(); _status.Text = "Recording cancelled."; return true; }
            if (repeat || ShortcutMap.IsModifier(key)) return true;
            _gesture = ShortcutMap.Gesture(key, modifiers); StopRecording(); return true;
        }

        private void SaveBinding() { Apply(false); }
        private void RemoveBinding() { Apply(true); }
        private void Apply(bool remove)
        {
            if (_recording) { _status.Text = "Finish recording before saving."; return; }
            var assignment = (Assignment)_assignment.SelectedItem;
            var command = (ShortcutCommand)_command.SelectedItem;
            List<ShortcutGesture> keys = _map.Keys(command.Id);
            if (assignment.Command != null)
            {
                int index = keys.FindIndex(g => g.KeyCode == assignment.Gesture.KeyCode && g.Modifiers == assignment.Gesture.Modifiers);
                if (index < 0) { _status.Text = "This binding changed. Close and reopen the key editor."; return; }
                if (remove) keys.RemoveAt(index); else keys[index] = _gesture.Copy();
            }
            else if (!remove) keys.Add(_gesture.Copy());
            else return;
            string error;
            if (!_map.Set(command.Id, keys, out error)) { _status.Text = error; return; }
            if (BindingsChanged != null) BindingsChanged(); Close();
        }
    }
}
