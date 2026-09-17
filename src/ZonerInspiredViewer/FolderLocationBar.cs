using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed class FolderLocationBar : Grid
    {
        private readonly Grid _crumbs = new Grid();
        private readonly Grid _entry = new Grid();
        private readonly TextBox _address;
        private readonly TextBlock _error;
        private readonly Func<string, bool> _navigate;
        private string _path;
        private bool _narrow;
        internal bool IsEditing { get { return _entry.Visibility == Visibility.Visible; } }
        internal TextBox Address { get { return _address; } }

        internal FolderLocationBar(Func<string, bool> navigate)
        {
            _navigate = navigate; MinWidth = 120;
            Children.Add(_crumbs); Children.Add(_entry);
            _entry.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _entry.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _address = new TextBox { MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(_address, "Folder address");
            CommandPresentation.Describe(_address, "Folder address", "Ctrl+L", "Enter a folder path. Enter opens it; Escape cancels.");
            _error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            ThemeManager.Bind(_error, TextBlock.ForegroundProperty, ThemeKeys.UpscaleErrorText);
            AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
            _entry.Children.Add(_address); Grid.SetRow(_error, 1); _entry.Children.Add(_error);
            _entry.Visibility = Visibility.Collapsed;
            _address.TextChanged += delegate { _error.Visibility = Visibility.Collapsed; };
            _address.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
                else if (e.Key == Key.Escape) { CancelEdit(); e.Handled = true; }
            };
            SizeChanged += delegate
            {
                bool narrow = ActualWidth < 440;
                if (_narrow != narrow) { _narrow = narrow; Rebuild(); }
            };
        }

        internal void SetPath(string path)
        {
            if (String.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) return;
            _path = path; CancelEdit(); Rebuild();
        }

        internal void BeginEdit()
        {
            _address.Text = _path ?? ""; _error.Visibility = Visibility.Collapsed;
            _crumbs.Visibility = Visibility.Collapsed; _entry.Visibility = Visibility.Visible;
            _address.Focus(); _address.SelectAll();
        }

        internal void CancelEdit()
        { _entry.Visibility = Visibility.Collapsed; _crumbs.Visibility = Visibility.Visible; _error.Visibility = Visibility.Collapsed; }

        internal bool Commit()
        {
            try
            {
                string path = Environment.ExpandEnvironmentVariables(_address.Text.Trim().Trim('"'));
                if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Enter a folder path.");
                if (!Path.IsPathRooted(path)) path = Path.Combine(_path ?? "", path);
                path = Path.GetFullPath(path);
                if (!Directory.Exists(path)) throw new DirectoryNotFoundException("This folder is unavailable.");
                if (!_navigate(path)) throw new IOException("This folder could not be opened.");
                CancelEdit(); return true;
            }
            catch (Exception error)
            {
                _error.Text = error.Message; _error.Visibility = Visibility.Visible;
                _address.Focus(); return false;
            }
        }

        private void Add(UIElement control, bool fill)
        {
            _crumbs.ColumnDefinitions.Add(new ColumnDefinition { Width = fill ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetColumn(control, _crumbs.ColumnDefinitions.Count - 1); _crumbs.Children.Add(control);
        }

        private Button Crumb(DirectoryInfo folder, bool current)
        {
            string path = folder.FullName;
            var button = new Button { Height = 32, Padding = new Thickness(7, 0, 7, 0), Margin = new Thickness(0, 0, 2, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Content = new TextBlock { Text = folder.Parent == null ? path : folder.Name, TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal } };
            if (!current) button.MaxWidth = 136;
            CommandPresentation.Describe(button, path, null, current ? "Edit this folder's address." : "Open this parent folder.");
            button.Click += delegate { if (current) BeginEdit(); else _navigate(path); };
            return button;
        }

        private void Rebuild()
        {
            _crumbs.Children.Clear(); _crumbs.ColumnDefinitions.Clear();
            var ancestors = new List<DirectoryInfo>();
            try { for (var d = String.IsNullOrEmpty(_path) ? null : new DirectoryInfo(_path); d != null; d = d.Parent) ancestors.Add(d); }
            catch (Exception) { }
            ancestors.Reverse();
            if (ancestors.Count == 0) { Add(new TextBlock { Text = "Folder", VerticalAlignment = VerticalAlignment.Center }, true); }
            else
            {
                if (ancestors.Count > 1) Add(Crumb(ancestors[0], false), false);
                int tail = _narrow ? 1 : 2;
                int hidden = Math.Max(0, ancestors.Count - 1 - tail);
                if (hidden > 0)
                {
                    Button more = null;
                    more = CommandPresentation.Button("\uE712", "Parent folders", null, "Open a parent folder from the full path.", delegate
                    {
                        var menu = new ContextMenu { PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                        foreach (var ancestor in ancestors.Skip(1).Take(hidden))
                        {
                            string path = ancestor.FullName;
                            var item = new MenuItem { Header = ancestor.Name, ToolTip = path };
                            item.Click += delegate { _navigate(path); }; menu.Items.Add(item);
                        }
                        menu.IsOpen = true;
                    }, false);
                    Add(more, false);
                }
                for (int i = Math.Max(ancestors.Count == 1 ? 0 : 1, ancestors.Count - tail); i < ancestors.Count; i++)
                {
                    if (i > 0) Add(new TextBlock { Text = "\uE76C", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 9,
                        Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center }, false);
                    Add(Crumb(ancestors[i], i == ancestors.Count - 1), i == ancestors.Count - 1);
                }
            }
            var edit = CommandPresentation.Button("\uE70F", "Folder address", "Ctrl+L", "Enter or paste a folder path.", BeginEdit, false);
            edit.Margin = new Thickness(4, 0, 0, 0); Add(edit, false);
        }
    }
}
