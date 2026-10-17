using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed partial class FolderPickerWindow
    {
        private ComboBox _viewMode;
        private VirtualizedThumbnailGrid _thumbnailFolders;
        private Grid _detailsHeader;
        private FolderPickerEntry[] _entries = new FolderPickerEntry[0];
        private bool _sortModified, _sortDescending;
        private FrameworkElement BuildViews(ThumbnailCacheService cache)
        {
            var panel = new DockPanel(); var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new TextBlock { Text = "View", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _viewMode = new ComboBox { Width = 145, Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            _viewMode.Items.Add(new ComboBoxItem { Content = "List" }); _viewMode.Items.Add(new ComboBoxItem { Content = "Details" });
            _viewMode.Items.Add(new ComboBoxItem { Content = "Thumbnails", IsEnabled = cache != null }); _viewMode.SelectedIndex = 0;
            AutomationProperties.SetName(_viewMode, "Folder chooser view"); row.Children.Add(_viewMode); DockPanel.SetDock(row, Dock.Top); panel.Children.Add(row);
            _detailsHeader = new Grid { Visibility = Visibility.Collapsed }; _detailsHeader.ColumnDefinitions.Add(new ColumnDefinition()); _detailsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(144) });
            var name = new Button { Content = "Name", HorizontalContentAlignment = HorizontalAlignment.Left, Height = 28 };
            name.Click += delegate { _sortDescending = !_sortModified && !_sortDescending; _sortModified = false; SortEntries(); }; _detailsHeader.Children.Add(name);
            var modified = new Button { Content = "Modified", HorizontalContentAlignment = HorizontalAlignment.Left, Height = 28 };
            modified.Click += delegate { _sortDescending = _sortModified ? !_sortDescending : true; _sortModified = true; SortEntries(); }; Grid.SetColumn(modified, 1); _detailsHeader.Children.Add(modified);
            DockPanel.SetDock(_detailsHeader, Dock.Top); panel.Children.Add(_detailsHeader);
            var views = new Grid(); views.Children.Add(_folders); panel.Children.Add(views);
            if (cache != null)
            {
                _thumbnailFolders = new VirtualizedThumbnailGrid(cache) { Visibility = Visibility.Collapsed, ContextMenu = null };
                _thumbnailFolders.ItemMenuFactory = delegate(ImageFileItem item)
                {
                    var menu = new ContextMenu(); var open = new MenuItem { Header = item.IsDirectory ? "Open folder" : "Select file" }; open.Click += delegate { ActivatePath(item.Path); }; menu.Items.Add(open); return menu;
                };
                _thumbnailFolders.ItemActivated += delegate(object sender, ThumbnailActivatedEventArgs e) { ActivatePath(e.Item.Path); };
                _thumbnailFolders.ItemSelected += delegate(object sender, ThumbnailActivatedEventArgs e) { if (e.Item != null) { _status.Text = e.Item.Name; _status.ToolTip = e.Item.Path; SelectFilename(e.Item.Path); } };
                _thumbnailFolders.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.Key == Key.Enter) { if (_thumbnailFolders.SelectedItem != null) ActivatePath(_thumbnailFolders.SelectedItem.Path); e.Handled = true; }
                    else e.Handled = _thumbnailFolders.HandleNavigationKey(e.Key, Keyboard.Modifiers);
                };
                views.Children.Add(_thumbnailFolders);
            }
            _viewMode.SelectionChanged += delegate { SwitchView(); };
            return panel;
        }
        private string SelectedPath()
        {
            if (_thumbnailFolders != null && _thumbnailFolders.Visibility == Visibility.Visible) return _thumbnailFolders.SelectedItem == null ? null : _thumbnailFolders.SelectedItem.Path;
            var entry = _folders.SelectedItem as FolderPickerEntry; return entry == null ? null : entry.Path;
        }
        private void SwitchView()
        {
            string selected = SelectedPath(); bool thumbnails = _viewMode.SelectedIndex == 2 && _thumbnailFolders != null, details = _viewMode.SelectedIndex == 1;
            _folders.Visibility = thumbnails ? Visibility.Collapsed : Visibility.Visible; _folders.ItemTemplate = details ? DetailsTemplate() : EntryTemplate();
            _detailsHeader.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
            if (_thumbnailFolders != null)
            {
                _thumbnailFolders.Visibility = thumbnails ? Visibility.Visible : Visibility.Collapsed;
                if (thumbnails) _thumbnailFolders.SelectItem(_entries.Where(e => e.Path == selected).Select(ToImageItem).FirstOrDefault());
            }
            if (!thumbnails) _folders.SelectedItem = _entries.FirstOrDefault(e => e.Path == selected);
        }
        private void SortEntries()
        {
            string selected = SelectedPath(); var ordered = _entries.ToArray();
            Array.Sort(ordered, (a, b) => { if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1; int c = _sortModified ? a.ModifiedUtc.CompareTo(b.ModifiedUtc) : 0; if (c == 0) c = BrowserSort.CompareNames(a.Name, b.Name); return _sortDescending ? -c : c; });
            _entries = ordered; _folders.ItemsSource = ordered; _folders.SelectedItem = ordered.FirstOrDefault(e => e.Path == selected);
            if (_thumbnailFolders != null) { _thumbnailFolders.SetItems(ordered.Select(ToImageItem).ToArray()); _thumbnailFolders.SelectItem(ordered.Where(e => e.Path == selected).Select(ToImageItem).FirstOrDefault()); }
        }
        private static ImageFileItem ToImageItem(FolderPickerEntry entry)
        { return new ImageFileItem { Path = entry.Path, Name = entry.Name, IsDirectory = entry.IsDirectory, Extension = entry.IsDirectory ? "" : System.IO.Path.GetExtension(entry.Path).ToLowerInvariant(), LastWriteUtc = entry.ModifiedUtc }; }
        private static DataTemplate DetailsTemplate()
        {
            var template = new DataTemplate(typeof(FolderPickerEntry)); var row = new FrameworkElementFactory(typeof(DockPanel)); row.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Path"));
            var date = new FrameworkElementFactory(typeof(TextBlock)); date.SetValue(DockPanel.DockProperty, Dock.Right); date.SetValue(FrameworkElement.WidthProperty, 136.0); date.SetBinding(TextBlock.TextProperty, new Binding("ModifiedText")); row.AppendChild(date);
            var icon = new FrameworkElementFactory(typeof(TextBlock)); icon.SetBinding(TextBlock.TextProperty, new Binding("Glyph")); icon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets")); icon.SetValue(FrameworkElement.WidthProperty, 24.0); row.AppendChild(icon);
            var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Name")); name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); row.AppendChild(name);
            template.VisualTree = row; return template;
        }
    }
}
