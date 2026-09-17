using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow
    {
        private Button _configureButton;
        private Window _configureWindow;
        private bool _configurationOwnerClosing;
        private Grid _configurationContent;
        private ScrollViewer _configurationScroll;
        private readonly List<StackPanel> _configurationPages = new List<StackPanel>();
        private readonly List<ToggleButton> _configurationNavigation = new List<ToggleButton>();
        private CheckBox _configureTree, _configureCompact, _configureEnhance, _configureImageMetadata;
        private TextBlock _systemVramText, _gpuDetailsText, _cacheUsageText, _pinnedColorText;
        private Border _pinnedColorPreview;
        private TextBox _configurationSearch;
        private StackPanel _configurationPageHost;
        private TextBlock _configurationNoResults;
        private int _selectedConfigurationPage;
        private readonly List<ConfigurationRow> _configurationRows = new List<ConfigurationRow>();
        private static readonly string[] ConfigurationNames = { "Appearance", "Images", "Performance", "Image tools", "Backgrounds", "Shortcuts" };
        private sealed class ConfigurationRow { internal int Page; internal Grid Row; internal string Search; }

        private void BuildConfigurationContent()
        {
            _configurationContent = new Grid();
            ThemeManager.Bind(_configurationContent, Panel.BackgroundProperty, ThemeKeys.WindowBackground);
            _configurationContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _configurationContent.RowDefinitions.Add(new RowDefinition());
            _configurationContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _configurationContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
            _configurationContent.ColumnDefinitions.Add(new ColumnDefinition());
            var header = new DockPanel { Margin = new Thickness(20, 16, 20, 14) };
            var searchGroup = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            searchGroup.Children.Add(new TextBlock { Text = "Find setting", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _configurationSearch = new TextBox { Width = 232, Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(_configurationSearch, "Find a setting");
            searchGroup.Children.Add(_configurationSearch);
            var clearSearch = CommandPresentation.Button("\uE711", "Clear settings search", null, "Return to the selected settings category.",
                delegate { _configurationSearch.Clear(); _configurationSearch.Focus(); }, false);
            clearSearch.IsEnabled = false;
            _configurationSearch.TextChanged += delegate { clearSearch.IsEnabled = !String.IsNullOrEmpty(_configurationSearch.Text); FilterConfiguration(); };
            clearSearch.Margin = new Thickness(4, 0, 0, 0); searchGroup.Children.Add(clearSearch);
            DockPanel.SetDock(searchGroup, Dock.Right); header.Children.Add(searchGroup);
            header.Children.Add(new TextBlock { Text = "Configure", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumnSpan(header, 2); _configurationContent.Children.Add(header);
            var navigation = new StackPanel { Margin = new Thickness(12, 4, 8, 12) };
            string[] names = ConfigurationNames;
            string[] icons = { "\uE790", "\uE8B9", "\uE9D9", "\uE70F", "\uE91B", "\uE765" };
            _configurationPageHost = new StackPanel();
            for (int i = 0; i < names.Length; i++)
            {
                int page = i;
                var label = CommandPresentation.Label(icons[i], names[i]); label.Width = 120;
                var button = new ToggleButton { Content = label, Height = 40, Margin = new Thickness(0, 0, 0, 4) };
                AutomationProperties.SetName(button, names[i] + " settings");
                button.Click += delegate { SelectConfigurationPage(page); };
                navigation.Children.Add(button); _configurationNavigation.Add(button);
                var settingsPage = new StackPanel { Margin = new Thickness(20, 4, 20, 20) };
                settingsPage.Children.Add(new TextBlock { Text = names[i], FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 20) });
                _configurationPages.Add(settingsPage); _configurationPageHost.Children.Add(settingsPage);
            }
            var navigationBand = new Border { Child = navigation, BorderThickness = new Thickness(0, 0, 1, 0) };
            ThemeManager.Bind(navigationBand, Border.BorderBrushProperty, ThemeKeys.Border);
            Grid.SetRow(navigationBand, 1); _configurationContent.Children.Add(navigationBand);
            _configurationNoResults = new TextBlock { Text = "No matching settings", Margin = new Thickness(20), Visibility = Visibility.Collapsed };
            _configurationPageHost.Children.Add(_configurationNoResults);
            _configurationScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _configurationPageHost };
            Grid.SetRow(_configurationScroll, 1); Grid.SetColumn(_configurationScroll, 1); _configurationContent.Children.Add(_configurationScroll);
            var close = new Button { Content = "Close", Width = 80, Height = 30, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16), IsCancel = true };
            close.Click += delegate { if (_configureWindow != null) _configureWindow.Close(); };
            var footer = new DockPanel();
            DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close);
            var reset = CommandPresentation.Button("\uE777", "Reset viewer...", null, "Reset all settings, tabs, saved sessions and favorites. Thumbnail cache clearing is optional.", ShowResetConfirmation, true);
            reset.HorizontalAlignment = HorizontalAlignment.Left; reset.Margin = new Thickness(16);
            footer.Children.Add(reset);
            Grid.SetRow(footer, 2); Grid.SetColumnSpan(footer, 2); _configurationContent.Children.Add(footer);

            _configureTree = new CheckBox { Content = "Folder tree" };
            _configureTree.Click += delegate { if ((_configureTree.IsChecked == true) != FolderTreeVisible) ToggleFolderTree(); };
            _configureCompact = new CheckBox { Content = "Compact mode" };
            _configureCompact.Click += delegate { SetCompactMode(_configureCompact.IsChecked == true); };
            _configureVerticalHeight = new CheckBox { Content = "Maximum available height" };
            _configureVerticalHeight.Click += delegate { SetVerticalHeight(_configureVerticalHeight.IsChecked == true); };
            _darkThemeCheckBox.Checked += delegate { ScheduleSessionSave(); };
            _darkThemeCheckBox.Unchecked += delegate { ScheduleSessionSave(); };
            _metadataToggle.Checked += delegate { ScheduleSessionSave(); };
            _metadataToggle.Unchecked += delegate { ScheduleSessionSave(); };
            _minimapButton.Margin = new Thickness(0);
            AddConfigurationRow(0, "Theme", _darkThemeCheckBox);
            AddConfigurationRow(0, "Navigation", _configureTree);
            AddConfigurationRow(0, "Workspace", _configureCompact);
            AddConfigurationRow(0, "Window height", _configureVerticalHeight);
            AddConfigurationRow(0, "Details", _metadataToggle);
            _configureImageMetadata = new CheckBox { Content = "Image metadata overlay" };
            CommandPresentation.Describe(_configureImageMetadata, "Image metadata overlay", "I", "Show or hide image details in the bottom-left corner.");
            _configureImageMetadata.Click += delegate { SetImageMetadataOverlay(_configureImageMetadata.IsChecked == true); };
            AddConfigurationRow(0, "Image overlay", _configureImageMetadata);
            _configureImageNavigator = new CheckBox { Content = "Pan navigator" };
            CommandPresentation.Describe(_configureImageNavigator, "Image navigator", "P", "Show a draggable image overview when the image extends beyond the frame.");
            _configureImageNavigator.Click += delegate { SetImageNavigator(_configureImageNavigator.IsChecked == true); };
            AddConfigurationRow(0, "Image navigation", _configureImageNavigator);
            AddConfigurationRow(0, "Thumbnail minimap", _minimapButton);
            var colors = new WrapPanel();
            foreach (string color in new[] { "#2F78A6", "#397C55", "#9A6530", "#A44364", "#7555A6", "#3D7E83" })
            {
                string chosen = color;
                var swatch = new Button { Width = 28, Height = 28, Padding = new Thickness(3),
                    Margin = new Thickness(0, 0, 5, 5), ToolTip = color };
                swatch.Content = new Border { Background = TabColorBrush(color), Width = 20, Height = 20 };
                AutomationProperties.SetName(swatch, "Pinned tab color " + color);
                swatch.Click += delegate { SetPinnedTabColor(chosen); };
                colors.Children.Add(swatch);
            }
            colors.Children.Add(AdvancedSearchWindow.IconButton("\uE790", "Choose pinned tab color", ChoosePinnedTabColor));
            AddConfigurationRow(0, "Pinned tabs", colors);
            _pinnedColorPreview = new Border { Width = 24, Height = 20, Margin = new Thickness(0, 0, 8, 0) };
            _pinnedColorText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            AddConfigurationRow(0, "Selected color", ConfigurationInline(_pinnedColorPreview, _pinnedColorText));
            BuildBackgroundConfiguration();
            BuildShortcutConfiguration();

            _configureEnhance = new CheckBox { Content = "Quick Enhance" };
            _configureEnhance.Click += delegate { _enhanceButton.IsChecked = _configureEnhance.IsChecked; ToggleQuickEnhance(); };
            AddConfigurationRow(1, "Thumbnail size", ConfigurationInline(_thumbnailSizeSlider, _thumbnailSizeText));
            AddConfigurationRow(1, "Thumbnail shape", ConfigurationInline(_thumbnailAspectSlider, _thumbnailAspectText));
            AddConfigurationRow(1, "Enhancement", _configureEnhance);
            AddConfigurationRow(1, "Enhance mode", _enhanceMode);
            AddConfigurationRow(1, "Color management", _iccCheckBox);
            BuildImageToolsConfiguration();
            _configureUpscaleButton = BuildUpscaleButton(); AddConfigurationRow(3, "Super-resolution", _configureUpscaleButton);
            BuildUpscaleModelConfiguration();
            _iccCheckBox.ToolTip = "Experimental preference; explicit ICC transforms are not implemented in this prototype.";
            _iccCheckBox.Checked += delegate { ScheduleSessionSave(); };
            _iccCheckBox.Unchecked += delegate { ScheduleSessionSave(); };
            _slideshowButton.HorizontalAlignment = HorizontalAlignment.Left;
            AddConfigurationRow(1, "Slideshow", _slideshowButton);
            AddConfigurationRow(1, "Slideshow seconds", _slideshowInterval);
            AddConfigurationRow(1, "Slideshow order", _slideshowShuffle);
            BuildFilmstripConfiguration();
            BuildImageGpuConfiguration();
            BuildGpuConfiguration();
            AddConfigurationRow(2, "Decoded cache limit", ConfigurationInline(_vramSlider, _vramText));
            _vramSlider.ToolTip = "Per-window decoded image cache limit. WPF and model GPU allocations are managed separately.";
            AutomationProperties.SetName(_vramSlider, "Decoded cache limit in MB");
            _cacheUsageText = ConfigurationValue();
            _systemVramText = ConfigurationValue();
            _gpuDetailsText = ConfigurationValue();
            AddConfigurationRow(2, "Decoded cache used", _cacheUsageText);
            AddConfigurationRow(2, "System VRAM", _systemVramText);
            AddConfigurationRow(2, "GPU adapters", _gpuDetailsText);
            AddConfigurationRow(2, "CPU workers", new TextBlock { Text = _services.CpuWorkerCount.ToString() });
            AddConfigurationRow(2, "Thumbnail cache", CommandPresentation.Button("\uE74D", "Clear cache...", null,
                "Clear all cached thumbnail sizes without changing photos or settings.", ClearThumbnailCacheClick, true));
            SetPinnedTabColor(null);
            SelectConfigurationPage(0);
        }

        private static TextBlock ConfigurationValue() { return new TextBlock { TextWrapping = TextWrapping.Wrap }; }

        private static void DetachConfigurationControl(UIElement control)
        {
            var parent = LogicalTreeHelper.GetParent(control) as Panel;
            if (parent != null) parent.Children.Remove(control);
        }

        private static StackPanel ConfigurationInline(params UIElement[] controls)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (UIElement control in controls) { DetachConfigurationControl(control); panel.Children.Add(control); }
            return panel;
        }

        private void AddConfigurationRow(int page, string label, UIElement control)
        {
            DetachConfigurationControl(control);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var title = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 12, 0) };
            var element = control as FrameworkElement;
            if (element != null && !Double.IsNaN(element.Width)) element.HorizontalAlignment = HorizontalAlignment.Left;
            if (control is ButtonBase) ((FrameworkElement)control).HorizontalAlignment = HorizontalAlignment.Left;
            if (control is CheckBox) ((CheckBox)control).Margin = new Thickness(0);
            if (control is ComboBox && Double.IsNaN(((ComboBox)control).Width)) ((ComboBox)control).MaxWidth = Double.PositiveInfinity;
            ThemeManager.Bind(title, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            row.Children.Add(title); Grid.SetColumn(control, 1); row.Children.Add(control);
            _configurationPages[page].Children.Add(row);
            string terms = label + " " + AutomationProperties.GetName(control);
            foreach (var child in ConfigurationDescendants(control).OfType<ContentControl>())
                if (child.Content is string) terms += " " + (string)child.Content;
            foreach (var child in ConfigurationDescendants(control).OfType<TextBlock>()) terms += " " + child.Text;
            _configurationRows.Add(new ConfigurationRow { Page = page, Row = row, Search = terms });
        }

        private static IEnumerable<DependencyObject> ConfigurationDescendants(DependencyObject root)
        {
            yield return root;
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                var element = child as DependencyObject;
                if (element != null) foreach (var descendant in ConfigurationDescendants(element)) yield return descendant;
            }
        }

        private void FilterConfiguration()
        {
            if (_configurationPageHost == null) return;
            string[] words = (_configurationSearch.Text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool searching = words.Length > 0, any = false;
            for (int page = 0; page < _configurationPages.Count; page++)
            {
                bool match = !searching && page == _selectedConfigurationPage;
                foreach (var entry in _configurationRows.Where(r => r.Page == page))
                {
                    bool visible = !searching || words.All(word => (ConfigurationNames[page] + " " + entry.Search).IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
                    entry.Row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    if (searching && visible) match = true;
                }
                _configurationPages[page].Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                _configurationNavigation[page].IsChecked = !searching && page == _selectedConfigurationPage;
                any |= match;
            }
            if (_keyboardShortcuts != null) _keyboardShortcuts.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
            _configurationNoResults.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            _configurationScroll.ScrollToTop();
        }

        private void SelectConfigurationPage(int page)
        {
            if (page < 0 || page >= _configurationPages.Count) return;
            _selectedConfigurationPage = page; _configurationSearch.Clear(); FilterConfiguration();
            if (page == 5 && _keyboardShortcuts != null) _keyboardShortcuts.RefreshLayout();
        }

        private void ShowConfigure()
        {
            if (_configureWindow != null) { _configureWindow.Activate(); return; }
            EndImagePan(); _thumbnailMinimap.EndDrag(); _imageNavigator.EndDrag();
            var window = new Window { Title = "Configure", Owner = this, Width = 900, Height = 680,
                MinWidth = 760, MinHeight = 440, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            ThemeManager.PrepareWindow(window);
            window.Content = _configurationContent;
            window.SizeChanged += delegate
            {
                window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(delegate
                {
                    var focused = Keyboard.FocusedElement as FrameworkElement;
                    if (focused != null && _configurationScroll.IsAncestorOf(focused)) focused.BringIntoView();
                }));
            };
            window.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (CaptureShortcut(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers, e.IsRepeat)) { e.Handled = true; return; }
                if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { _configurationSearch.Focus(); _configurationSearch.SelectAll(); e.Handled = true; return; }
                if (e.Key == Key.Escape && !String.IsNullOrEmpty(_configurationSearch.Text)) { _configurationSearch.Clear(); e.Handled = true; return; }
                if (e.Key == Key.Escape) { StopSlideshow(); window.Close(); e.Handled = true; }
            };
            window.Closed += delegate
            {
                CancelShortcutCapture();
                ApplyGpuSettings();
                ApplySlideshowInterval();
                _thumbnailSizeTimer.Stop(); ApplyThumbnailSize();
                window.Content = null; _configureWindow = null;
                ScheduleSessionSave();
                if (!_isClosed && !_configurationOwnerClosing) { Activate(); if (_browserView.IsVisible) _thumbnailGrid.Focus(); else _imageCanvas.Focus(); }
            };
            _configureWindow = window;
            UpdateConfigurationStatus();
            window.Show();
        }

        private void UpdateConfigurationStatus()
        {
            SystemGpuMemoryMonitor.Shared.Refresh();
            if (_systemVramText == null) return;
            var snapshot = SystemGpuMemoryMonitor.Shared.Snapshot;
            UpdateGpuDisplay(snapshot);
            _systemVramText.Text = snapshot.UsageText;
            _systemVramText.ToolTip = snapshot.Error ?? "Dedicated GPU memory across all processes; shared system RAM is excluded.";
            _gpuDetailsText.Text = snapshot.Details;
            _cacheUsageText.Text = ImageExtensions.FormatBytes(_services.VramCache.UsedBytes);
            _configureTree.IsChecked = FolderTreeVisible;
            _configureCompact.IsChecked = _isCompactMode;
            _configureEnhance.IsChecked = _quickEnhanceEnabled;
            _configureImageMetadata.IsChecked = _imageMetadataOverlayEnabled;
            _configureImageNavigator.IsChecked = _imageNavigatorEnabled;
            UpdateVerticalHeightControls();
            UpdateImageToolsConfiguration();
            if (_configureWindow != null && _configurationNavigation[5].IsChecked == true && _keyboardShortcuts != null)
                _keyboardShortcuts.RefreshLayout();
        }

        private void ChoosePinnedTabColor()
        {
            Color current = ((SolidColorBrush)TabColorBrush(_pinnedTabColor)).Color;
            using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true,
                Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B) })
                if (dialog.ShowDialog(new ColorDialogOwner(_configureWindow ?? this)) == System.Windows.Forms.DialogResult.OK)
                    SetPinnedTabColor(String.Format("#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B));
        }

        private sealed class ColorDialogOwner : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public ColorDialogOwner(Window window) { Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle; }
        }
    }
}
