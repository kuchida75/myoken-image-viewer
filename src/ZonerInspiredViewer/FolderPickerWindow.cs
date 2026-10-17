using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed class FolderPickerEntry
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public DateTime ModifiedUtc { get; set; }
        public bool IsDirectory { get; set; }
        public string Glyph { get { return IsDirectory ? "\uED25" : "\uE8A5"; } }
        public FolderPickerEntry() { IsDirectory = true; }
        public string ModifiedText { get { return ModifiedUtc.ToLocalTime().ToString("g"); } }
    }

    internal sealed partial class FolderPickerWindow : Window
    {
        private readonly FolderLocationBar _location;
        private readonly ListBox _folders, _places;
        private readonly Button _select, _up, _refresh, _newFolder;
        private readonly TextBlock _status;
        private string _current, _requestedPath;
        private CancellationTokenSource _request;
        private bool _closed;
        internal string SelectedFolder { get; private set; }
        internal Task PendingWork { get; private set; }
        private sealed class Listing { internal FolderPickerEntry[] Folders; internal int Skipped; internal bool Limited; }

        internal FolderPickerWindow(string initialFolder, ThumbnailCacheService thumbnailCache = null) : this(initialFolder, thumbnailCache, null) { }
        internal FolderPickerWindow(string initialFolder, ThumbnailCacheService thumbnailCache, Microsoft.Win32.FileDialog fileDialog)
        {
            _fileDialog = fileDialog;
            Title = fileDialog == null ? "Choose a folder to scan" : String.IsNullOrEmpty(fileDialog.Title) ? (fileDialog is Microsoft.Win32.SaveFileDialog ? "Save file" : "Open file") : fileDialog.Title;
            Width = 760; Height = 570; MinWidth = 560; MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; ThemeManager.PrepareWindow(this);
            Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(ItemStyles));
            var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
            ThemeManager.Bind(root, Panel.BackgroundProperty, ThemeKeys.WindowBackground);
            var address = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            address.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); address.ColumnDefinitions.Add(new ColumnDefinition());
            address.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); DockPanel.SetDock(address, Dock.Top); root.Children.Add(address);
            address.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _up = CommandPresentation.Button("\uE74A", "Parent folder", "Alt+Up", "Browse the parent of the current folder.", GoUp, false); address.Children.Add(_up);
            _location = new FolderLocationBar(Navigate); Grid.SetColumn(_location, 1); address.Children.Add(_location);
            _refresh = CommandPresentation.Button("\uE72C", "Refresh folders", "F5", "Reload folders at the current location.", delegate { Navigate(_current); }, false);
            _newFolder = CommandPresentation.Button("\uE8F4", "New folder", null, "Create a folder at the current location.", ChooseNewFolder, false);
            _newFolder.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(_newFolder, 2); address.Children.Add(_newFolder);
            _refresh.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(_refresh, 3); address.Children.Add(_refresh);
            var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
            _select = CommandPresentation.Button(fileDialog is Microsoft.Win32.SaveFileDialog ? "\uE74E" : "\uED25", fileDialog == null ? "Select folder" : fileDialog is Microsoft.Win32.SaveFileDialog ? "Save" : "Open", null,
                fileDialog == null ? "Use the selected folder, or the current location when no child folder is selected." : "Use the filename and selected file type.", Confirm, true);
            _select.IsEnabled = false; actions.Children.Add(_select);
            var cancel = CommandPresentation.Button("\uE711", "Cancel", "Esc", "Close without selecting a file or folder.", Close, true); cancel.Margin = new Thickness(0); actions.Children.Add(cancel);
            _status = new TextBlock { Text = "", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) };
            ThemeManager.Bind(_status, TextBlock.ForegroundProperty, ThemeKeys.MutedText); footer.Children.Add(_status);
            if (fileDialog != null) { var fileControls = BuildFileControls(); DockPanel.SetDock(fileControls, Dock.Bottom); root.Children.Add(fileControls); }
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); body.ColumnDefinitions.Add(new ColumnDefinition()); root.Children.Add(body);
            _places = List("Folder locations"); body.Children.Add(_places);
            _folders = List("Folders"); var folderViews = BuildViews(thumbnailCache); Grid.SetColumn(folderViews, 2); body.Children.Add(folderViews);
            _places.ItemsSource = Places();
            _places.SelectionChanged += delegate { var entry = _places.SelectedItem as FolderPickerEntry; if (entry != null) Navigate(entry.Path); };
            _folders.SelectionChanged += delegate
            { var entry = _folders.SelectedItem as FolderPickerEntry; if (entry != null) { _status.Text = entry.Name; _status.ToolTip = entry.Path; SelectFilename(entry.Path); } };
            _folders.MouseDoubleClick += delegate
            { var entry = _folders.SelectedItem as FolderPickerEntry; if (entry != null) ActivatePath(entry.Path); };
            _folders.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Enter) { var entry = _folders.SelectedItem as FolderPickerEntry; if (entry != null) ActivatePath(entry.Path); e.Handled = true; } };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { _location.BeginEdit(); e.Handled = true; }
                else if ((e.SystemKey == Key.Up || e.Key == Key.Up) && Keyboard.Modifiers == ModifierKeys.Alt) { GoUp(); e.Handled = true; }
                else if (e.Key == Key.F5) { Navigate(_current); e.Handled = true; }
            };
            string initial = Directory.Exists(initialFolder) ? initialFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (!Directory.Exists(initial)) initial = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Loaded += delegate { Navigate(initial); };
            if (fileDialog != null) { _viewMode.SelectedIndex = 1; Loaded += delegate { _fileName.Focus(); _fileName.SelectAll(); }; }
            Closed += delegate { _closed = true; if (_request != null) _request.Cancel(); _folders.ItemsSource = null; if (_thumbnailFolders != null) _thumbnailFolders.SetItems(new ImageFileItem[0]); };
        }
        private static ListBox List(string name)
        {
            var list = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch, ItemTemplate = EntryTemplate() };
            ThemeManager.Bind(list, Control.BackgroundProperty, ThemeKeys.PaneBackground); ThemeManager.Bind(list, Control.ForegroundProperty, ThemeKeys.Text);
            ThemeManager.Bind(list, Control.BorderBrushProperty, ThemeKeys.Border);
            VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(list, true); ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            AutomationProperties.SetName(list, name); return list;
        }
        private static DataTemplate EntryTemplate()
        {
            var template = new DataTemplate(typeof(FolderPickerEntry)); var row = new FrameworkElementFactory(typeof(DockPanel));
            row.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Path"));
            var icon = new FrameworkElementFactory(typeof(TextBlock)); icon.SetBinding(TextBlock.TextProperty, new Binding("Glyph"));
            icon.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets")); icon.SetValue(FrameworkElement.WidthProperty, 24.0);
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); row.AppendChild(icon);
            var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Name")); name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            row.AppendChild(name); template.VisualTree = row; return template;
        }
        private static FolderPickerEntry[] Places()
        {
            var entries = new List<FolderPickerEntry>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] names = { "Pictures", "Desktop", "Documents", "Downloads", "Home" };
            string[] paths = { Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.Combine(home, "Downloads"), home };
            for (int i = 0; i < names.Length; i++) if (Directory.Exists(paths[i])) entries.Add(new FolderPickerEntry { Name = names[i], Path = paths[i] });
            foreach (var drive in DriveInfo.GetDrives())
            {
                try { if (drive.DriveType != DriveType.Network && drive.IsReady) entries.Add(new FolderPickerEntry { Name = drive.Name, Path = drive.RootDirectory.FullName }); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return entries.ToArray();
        }
        internal bool Navigate(string path)
        {
            if (_closed || String.IsNullOrWhiteSpace(path)) return false;
            try
            {
                path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                if (!Path.IsPathRooted(path)) path = Path.Combine(_current ?? "", path);
                path = Path.GetFullPath(path);
                if (path.Length > Path.GetPathRoot(path).Length) path = path.TrimEnd('\\', '/');
                if (!Directory.Exists(path)) throw new DirectoryNotFoundException("This folder is unavailable.");
                FileTransferService.CheckAncestors(path);
                if (_request != null) _request.Cancel(); var request = new CancellationTokenSource(); _request = request;
                _select.IsEnabled = _folders.IsEnabled = _up.IsEnabled = _refresh.IsEnabled = _newFolder.IsEnabled = false; _status.Text = _fileDialog == null ? "Loading folders..." : "Loading files and folders...";
                string destination = path; _requestedPath = path;
                PendingWork = Dispatcher.InvokeAsync(new Func<Task>(delegate { return LoadAsync(destination, request); })).Task.Unwrap(); return true;
            }
            catch (Exception error) { _status.Text = error.Message; _status.ToolTip = error.Message; return false; }
        }
        private async Task LoadAsync(string path, CancellationTokenSource request)
        {
            Listing listing = null; string error = null;
            var filter = _fileDialog == null ? null : (FilePickerFilter)_fileType.SelectedItem;
            try
            {
                listing = await Task.Run(delegate
                {
                    var result = new Listing(); var entries = new List<FolderPickerEntry>(); FileTransferService.CheckAncestors(path);
                    int visited = 0;
                    foreach (string child in filter == null ? Directory.EnumerateDirectories(path) : Directory.EnumerateFileSystemEntries(path))
                    {
                        request.Token.ThrowIfCancellationRequested();
                        if (visited++ == 100000) { result.Limited = true; break; }
                        try
                        {
                            var attributes = File.GetAttributes(child);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { result.Skipped++; continue; }
                            bool directory = (attributes & FileAttributes.Directory) != 0;
                            if (!directory && filter != null && !filter.Matches(Path.GetFileName(child))) continue;
                            entries.Add(new FolderPickerEntry { Name = Path.GetFileName(child), Path = child, IsDirectory = directory,
                                ModifiedUtc = directory ? Directory.GetLastWriteTimeUtc(child) : File.GetLastWriteTimeUtc(child) });
                        }
                        catch (IOException) { result.Skipped++; } catch (UnauthorizedAccessException) { result.Skipped++; }
                    }
                    entries.Sort((a, b) => BrowserSort.CompareNames(a.Name, b.Name)); request.Token.ThrowIfCancellationRequested(); result.Folders = entries.ToArray(); return result;
                }, request.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception failure) { error = failure.Message; }
            finally
            {
                if (!_closed && _request == request)
                {
                    _request = null;
                    if (listing != null)
                    {
                        _current = path; _entries = listing.Folders; SortEntries(); _location.SetPath(path);
                        _status.Text = listing.Folders.Length + (_fileDialog == null ? " folders" : " items") + (listing.Limited ? " (limit reached)" : "") + (listing.Skipped > 0 ? "; " + listing.Skipped + " unavailable/linked" : "");
                        _status.ToolTip = path;
                    }
                    else if (error != null) { _status.Text = error; _status.ToolTip = error; }
                    _select.IsEnabled = _current != null; _folders.IsEnabled = _refresh.IsEnabled = true;
                    _up.IsEnabled = _current != null && Directory.GetParent(_current) != null;
                    _newFolder.IsEnabled = _current != null;
                }
                request.Dispose();
            }
        }
        private void GoUp() { if (_current != null) { var parent = Directory.GetParent(_current); if (parent != null) Navigate(parent.FullName); } }
        internal void Confirm()
        {
            if (_request != null) return;
            if (_fileDialog != null) { ConfirmFile(); return; }
            string path = SelectedPath() ?? _current;
            try
            {
                if (String.IsNullOrEmpty(path) || !Directory.Exists(path)) throw new DirectoryNotFoundException("This folder is unavailable.");
                FileTransferService.CheckAncestors(path); SelectedFolder = path; Close();
            }
            catch (Exception error) { _status.Text = error.Message; _status.ToolTip = error.Message; }
        }
        private const string ItemStyles = @"
<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Style TargetType=""ListBox""><Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ListBox""><Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}""><ScrollViewer Focusable=""False"" CanContentScroll=""True"" Padding=""{TemplateBinding Padding}""><ItemsPresenter/></ScrollViewer></Border></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType=""ListBoxItem"">
    <Setter Property=""Foreground"" Value=""{DynamicResource Theme.Text}""/><Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""HorizontalContentAlignment"" Value=""Stretch""/><Setter Property=""BorderBrush"" Value=""Transparent""/>
    <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ListBoxItem""><Border Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""1"" Padding=""8,7""><ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter>
    <Style.Triggers><Trigger Property=""IsSelected"" Value=""True""><Setter Property=""Background"" Value=""{DynamicResource Theme.SelectedBackground}""/><Setter Property=""BorderBrush"" Value=""{DynamicResource Theme.Accent}""/></Trigger><Trigger Property=""IsMouseOver"" Value=""True""><Setter Property=""Background"" Value=""{DynamicResource Theme.ControlHover}""/></Trigger></Style.Triggers>
  </Style>
</ResourceDictionary>";
    }
}
