using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed class KeyboardShortcutView : StackPanel
    {
        private readonly ShortcutMap _map;
        private readonly TextBlock _layoutName;
        private readonly ComboBox _scope;
        private readonly StackPanel _keys;
        private readonly WrapPanel _extraKeys = new WrapPanel();
        private WindowsKeyboardLayout _layout;
        internal readonly Dictionary<Key, List<Button>> KeyButtons = new Dictionary<Key, List<Button>>();
        internal event Action<Key> EditRequested;
        internal event Action LayoutChanged;
        internal int Scope { get { return _scope.SelectedIndex == 1 ? 1 : _scope.SelectedIndex == 2 ? 2 : 3; } }

        internal KeyboardShortcutView(ShortcutMap map, Action reset)
        {
            _map = map; Margin = new Thickness(0, 0, 0, 20);
            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var resetButton = CommandPresentation.Button("\uE777", "Reset all keys", null, "Restore the default keyboard bindings for this window.", reset, true);
            DockPanel.SetDock(resetButton, Dock.Right); toolbar.Children.Add(resetButton);
            _scope = new ComboBox { ItemsSource = new[] { "All views", "Browser", "Image" }, SelectedIndex = 0, Width = 108,
                Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(_scope, "Keyboard binding view");
            DockPanel.SetDock(_scope, Dock.Left); toolbar.Children.Add(_scope);
            var label = new TextBlock { Text = "Keyboard", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            ThemeManager.Bind(label, TextBlock.ForegroundProperty, ThemeKeys.Text); toolbar.Children.Add(label); Children.Add(toolbar);
            _layoutName = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            ThemeManager.Bind(_layoutName, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            AutomationProperties.SetName(_layoutName, "Detected Windows keyboard layout"); Children.Add(_layoutName);
            _keys = new StackPanel(); Children.Add(_keys);
            _scope.SelectionChanged += delegate { RefreshBindings(); };
            Loaded += delegate { InputLanguageManager.Current.InputLanguageChanged += InputLanguageChanged; RefreshLayout(); };
            Unloaded += delegate { InputLanguageManager.Current.InputLanguageChanged -= InputLanguageChanged; };
            RefreshLayout();
        }

        private void InputLanguageChanged(object sender, InputLanguageEventArgs e)
        { Dispatcher.BeginInvoke(new Action(RefreshLayout)); }

        internal void RefreshLayout()
        {
            var layout = WindowsKeyboardLayout.Current;
            if (_layout == layout) return;
            _layout = layout; _layoutName.Text = layout.Name + "  (" + layout.Id + ")";
            _layoutName.ToolTip = "Active Windows input layout. Logical key positions include ISO and numeric keys; physical keyboard shapes can differ.";
            BuildKeys(); RefreshBindings();
            if (LayoutChanged != null) LayoutChanged();
        }

        private sealed class Cap
        {
            internal Key Key; internal double Width; internal string Label;
            internal Cap(Key key, double width = 1, string label = null) { Key = key; Width = width; Label = label; }
        }

        private Cap Scan(uint code, double width = 1) { return new Cap(_layout.KeyAt(code), width); }

        private void BuildKeys()
        {
            _keys.Children.Clear(); KeyButtons.Clear(); _extraKeys.Children.Clear();
            var function = new List<Cap> { new Cap(Key.Escape, 1.3, "Esc") };
            for (int i = 0; i < 12; i++) function.Add(new Cap((Key)((int)Key.F1 + i)));
            _keys.Children.Add(Row(function));
            var row = new List<Cap> { Scan(0x29) };
            for (uint scan = 0x02; scan <= 0x0d; scan++) row.Add(Scan(scan));
            row.Add(new Cap(Key.Back, 2, "Back")); _keys.Children.Add(Row(row));
            row = new List<Cap> { new Cap(Key.Tab, 1.5) };
            for (uint scan = 0x10; scan <= 0x1b; scan++) row.Add(Scan(scan));
            row.Add(Scan(0x2b, 1.5)); _keys.Children.Add(Row(row));
            row = new List<Cap> { new Cap(Key.Capital, 1.75, "Caps") };
            for (uint scan = 0x1e; scan <= 0x28; scan++) row.Add(Scan(scan));
            row.Add(new Cap(Key.Enter, 2.25)); _keys.Children.Add(Row(row));
            row = new List<Cap> { new Cap(Key.LeftShift, 1.25), Scan(0x56) };
            for (uint scan = 0x2c; scan <= 0x35; scan++) row.Add(Scan(scan));
            row.Add(new Cap(Key.RightShift, 2.75)); _keys.Children.Add(Row(row));
            _keys.Children.Add(Row(new[] { new Cap(Key.LeftCtrl, 1.25), new Cap(Key.LWin, 1.25, "Win"), new Cap(Key.LeftAlt, 1.25),
                new Cap(Key.Space, 6.25), new Cap(Key.RightAlt, 1.25), new Cap(Key.RWin, 1.25, "Win"), new Cap(Key.Apps, 1.25), new Cap(Key.RightCtrl, 1.25) }));
            var groups = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
            var navigation = new StackPanel { Width = 192, Margin = new Thickness(0, 0, 14, 8) };
            navigation.Children.Add(Row(new[] { new Cap(Key.Snapshot, 1, "Print"), new Cap(Key.Scroll, 1, "Scroll"), new Cap(Key.Pause) }));
            navigation.Children.Add(Row(new[] { new Cap(Key.Insert, 1, "Ins"), new Cap(Key.Home), new Cap(Key.PageUp, 1, "PgUp") }));
            navigation.Children.Add(Row(new[] { new Cap(Key.Delete, 1, "Del"), new Cap(Key.End), new Cap(Key.PageDown, 1, "PgDn") }));
            var arrows = KeyGrid(3, 2);
            PlaceKey(arrows, new Cap(Key.Up, 1, "\u2191"), 0, 1);
            PlaceKey(arrows, new Cap(Key.Left, 1, "\u2190"), 1, 0);
            PlaceKey(arrows, new Cap(Key.Down, 1, "\u2193"), 1, 1);
            PlaceKey(arrows, new Cap(Key.Right, 1, "\u2192"), 1, 2); navigation.Children.Add(arrows);
            groups.Children.Add(navigation);
            var numpad = KeyGrid(4, 5); numpad.Width = 208; numpad.Margin = new Thickness(0, 0, 0, 8);
            PlaceKey(numpad, new Cap(Key.NumLock, 1, "Num"), 0, 0); PlaceKey(numpad, new Cap(Key.Divide, 1, "/"), 0, 1);
            PlaceKey(numpad, new Cap(Key.Multiply, 1, "*"), 0, 2); PlaceKey(numpad, new Cap(Key.Subtract, 1, "-"), 0, 3);
            for (int rowIndex = 1; rowIndex <= 3; rowIndex++) for (int col = 0; col < 3; col++)
            {
                int number = (3 - rowIndex) * 3 + col + 1;
                PlaceKey(numpad, new Cap((Key)((int)Key.NumPad0 + number), 1, number.ToString()), rowIndex, col);
            }
            PlaceKey(numpad, new Cap(Key.Add, 1, "+"), 1, 3, 2);
            PlaceKey(numpad, new Cap(Key.Enter), 3, 3, 2);
            PlaceKey(numpad, new Cap(Key.NumPad0, 1, "0"), 4, 0, 1, 2);
            PlaceKey(numpad, new Cap(Key.Decimal, 1, "Dec"), 4, 2); groups.Children.Add(numpad);
            _keys.Children.Add(groups);
            foreach (uint scan in new uint[] { 0x73, 0x7d })
            {
                Key key = _layout.KeyAt(scan);
                if (key != Key.None && !KeyButtons.ContainsKey(key) && !String.IsNullOrWhiteSpace(_layout.Character(key, false))) AddExtra(key);
            }
            AddExtra(Key.BrowserBack); AddExtra(Key.BrowserForward);
            foreach (Key key in _map.Commands.SelectMany(c => _map.Keys(c.Id)).Select(g => (Key)g.KeyCode).Distinct())
                if (!KeyButtons.ContainsKey(key)) AddExtra(key);
            _keys.Children.Add(_extraKeys);
        }

        private void AddExtra(Key key)
        {
            Button button = KeyButton(new Cap(key)); button.MinWidth = 92; button.Padding = new Thickness(8, 0, 8, 0);
            _extraKeys.Children.Add(button);
        }

        private Grid Row(IEnumerable<Cap> caps)
        {
            var row = new Grid();
            foreach (var cap in caps)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cap.Width, GridUnitType.Star) });
                Button key = KeyButton(cap); Grid.SetColumn(key, row.ColumnDefinitions.Count - 1); row.Children.Add(key);
            }
            return row;
        }

        private static Grid KeyGrid(int columns, int rows)
        {
            var grid = new Grid();
            for (int i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });
            return grid;
        }

        private void PlaceKey(Grid grid, Cap cap, int row, int column, int rows = 1, int columns = 1)
        {
            Button button = KeyButton(cap); button.Height = Double.NaN;
            Grid.SetRow(button, row); Grid.SetColumn(button, column); Grid.SetRowSpan(button, rows); Grid.SetColumnSpan(button, columns);
            grid.Children.Add(button);
        }

        private Button KeyButton(Cap cap)
        {
            var button = new Button { Content = new TextBlock { Text = cap.Label ?? _layout.KeyName(cap.Key), FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center },
                Height = 32, Padding = new Thickness(1, 0, 1, 0), Margin = new Thickness(1, 1, 2, 2), Tag = cap.Key };
            if (!KeyButtons.ContainsKey(cap.Key)) KeyButtons[cap.Key] = new List<Button>();
            KeyButtons[cap.Key].Add(button);
            button.MouseDoubleClick += delegate(object sender, MouseButtonEventArgs e)
            { if (e.ChangedButton == MouseButton.Left) { e.Handled = true; RequestEdit(cap.Key); } };
            button.KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Enter || e.Key == Key.Space) { e.Handled = true; RequestEdit(cap.Key); } };
            ToolTipService.SetInitialShowDelay(button, 250); ToolTipService.SetShowDuration(button, 30000);
            AutomationProperties.SetName(button, _layout.KeyName(cap.Key));
            return button;
        }

        private void RequestEdit(Key key) { if (EditRequested != null) EditRequested(key); }

        internal string KeyDetails(Key key)
        {
            string reserved = Reserved(key);
            if (reserved != null) return _layout.KeyName(key) + "\n" + reserved;
            var entries = _map.Commands.Where(c => (c.Scope & Scope) != 0)
                .SelectMany(c => _map.Keys(c.Id).Where(g => g.KeyCode == (int)key).Select(g => g + " | " + c + "\n" + c.Description)).ToList();
            string extra = key >= Key.NumPad0 && key <= Key.NumPad9 || key == Key.Decimal ? "\nNumeric keypad bindings assume Num Lock is on." : "";
            return _layout.KeyName(key) + "\n\n" + (entries.Count == 0 ? "Unassigned in this view." : String.Join("\n\n", entries)) + extra;
        }

        internal static string Reserved(Key key)
        {
            if (key == Key.Escape) return "Reserved: cancel, stop slideshow, leave fullscreen and return to thumbnails.";
            if (ShortcutMap.IsModifier(key)) return "Modifier key: combines with another key; cannot be assigned alone.";
            if (key == Key.LWin || key == Key.RWin) return "Reserved for Windows shortcuts.";
            if (key == Key.None) return "No key mapping is available from this input layout.";
            return null;
        }

        internal void RefreshBindings()
        {
            if (_layout == null) return;
            var missing = _map.Commands.SelectMany(c => _map.Keys(c.Id)).Select(g => (Key)g.KeyCode).Distinct().Where(k => !KeyButtons.ContainsKey(k)).ToArray();
            foreach (Key key in missing) AddExtra(key);
            foreach (var pair in KeyButtons)
            {
                bool assigned = _map.Commands.Any(c => (c.Scope & Scope) != 0 && _map.Keys(c.Id).Any(g => g.KeyCode == (int)pair.Key));
                string details = KeyDetails(pair.Key);
                foreach (Button button in pair.Value)
                {
                    ThemeManager.Bind(button, Control.BackgroundProperty, assigned ? ThemeKeys.SelectedBackground : ThemeKeys.ControlBackground);
                    ThemeManager.Bind(button, Control.BorderBrushProperty, assigned ? ThemeKeys.Accent : ThemeKeys.Border);
                    ThemeManager.Bind(button, Control.ForegroundProperty, Reserved(pair.Key) == null ? ThemeKeys.Text : ThemeKeys.MutedText);
                    button.ToolTip = new ToolTip { Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 }, Padding = new Thickness(10) };
                    ThemeManager.Bind((ToolTip)button.ToolTip, Control.BackgroundProperty, ThemeKeys.ControlBackground);
                    ThemeManager.Bind((ToolTip)button.ToolTip, Control.ForegroundProperty, ThemeKeys.Text);
                    ThemeManager.Bind((ToolTip)button.ToolTip, Control.BorderBrushProperty, ThemeKeys.Border);
                    AutomationProperties.SetHelpText(button, details);
                    AutomationProperties.SetItemStatus(button, assigned ? "Assigned" : Reserved(pair.Key) == null ? "Unassigned" : "Reserved");
                }
            }
        }
    }
}
