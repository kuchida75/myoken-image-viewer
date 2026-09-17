using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZonerInspiredViewer
{
    internal sealed partial class MainWindow : Window
    {
        private readonly AppServices _services;
        private readonly List<ImageFileItem> _allItems;
        private readonly List<ImageFileItem> _filteredItems;
        private readonly List<ImageFileItem> _navigationImages;
        private readonly Dictionary<string, ImageTabState> _tabs;
        private readonly List<string> _tabOrder;
        private readonly HashSet<string> _preloading;

        private FolderNavigationPane _folderNavigation;
        private VirtualizedThumbnailGrid _thumbnailGrid;
        private MetadataPanel _metadataPanel;
        private TextBox _searchBox;
        private readonly SearchHistory _searchHistory = new SearchHistory();
        private SearchHistoryInput _searchHistoryInput;
        private TextBlock _statusText;
        private TextBlock _folderTitle;
        private TextBlock _vramText;
        private Slider _vramSlider;
        private Slider _thumbnailSizeSlider;
        private TextBlock _thumbnailSizeText;
        private Slider _thumbnailAspectSlider;
        private TextBlock _thumbnailAspectText;
        private CheckBox _iccCheckBox;
        private CheckBox _darkThemeCheckBox;
        private CheckBox _metadataToggle;
        private StackPanel _tabStrip;
        private Button _browserTabButton;
        private Button _newTabButton;
        private ReorderDrag _tabReorder;
        private Button _browserUpButton;
        private ToggleButton _favoriteButton;
        private ComboBox _sortFieldBox;
        private ToggleButton _sortDirectionButton;
        private bool _syncingSortControls;
        private int _sortVersion;
        private bool _isScanningFolder;
        private BrowserSortField? _appliedSortField;
        private bool _appliedSortDescending;
        private Button _sessionsButton;
        private Button _renameButton;
        private Button _moveButton;
        private Button _deleteButton;
        private ToggleButton _enhanceButton;
        private bool _quickEnhanceEnabled;
        private bool _advancedQuickEnhance;
        private bool _syncEnhanceMode;
        private ComboBox _enhanceMode;
        private SubjectSegmentation _subjectSegmentation;
        private CancellationTokenSource _enhancementRequest;
        private string _enhancementSummary;
        private bool _isClosed;
        private Grid _browserView;
        private Grid _imageView;
        private Canvas _imageCanvas;
        private Image _mainImage;
        private ScaleTransform _imageScale;
        private MatrixTransform _imageRotation;
        private TranslateTransform _imageTranslate;
        private GridSplitter _rightSplitter;
        private ColumnDefinition _metadataSplitterColumn;
        private ColumnDefinition _metadataColumn;

        private CancellationTokenSource _scanCancellation;
        private FileSystemWatcher _watcher;
        private string _watcherDirtyImagePath;
        private DispatcherTimer _rescanTimer;
        private DispatcherTimer _searchTimer;
        private DispatcherTimer _statusTimer;
        private DispatcherTimer _thumbnailSizeTimer;
        private DispatcherTimer _placementSaveTimer;

        private string _currentFolder;
        private string _activeTabPath;
        private string _activeTabId;
        private readonly ImageTabState _homeBrowser = new ImageTabState { IsBrowser = true };
        private ImageTabState _latestBrowserView;
        private bool _folderScanPending;
        private int _deferredRelativeNavigation;
        private string _deferredNavigationTab, _deferredNavigationFolder, _deferredNavigationPath;
        private TextBlock _imageError;
        private string _displayedImagePath;
        private bool _imageViewPending;
        private string _activeNamedSessionName;
        private int _imageLoadVersion;
        private int _metadataLoadVersion;
        private bool _restored;
        private bool _isFullscreen;
        private WindowStyle _savedWindowStyle;
        private ResizeMode _savedResizeMode;
        private WindowState _savedWindowState;
        private bool _isPanning;
        private Point _panStart;
        private int _thumbnailSizePixels;
        private double _thumbnailAspectRatio = 1;
        private double _metadataPanelWidth;
        private Rect _normalWindowBounds;
        private WindowState _lastWindowState = WindowState.Normal;

        public MainWindow(AppServices services)
        {
            _services = services;
            _allItems = new List<ImageFileItem>(4096);
            _filteredItems = new List<ImageFileItem>(4096);
            _navigationImages = new List<ImageFileItem>(4096);
            _tabs = new Dictionary<string, ImageTabState>(StringComparer.OrdinalIgnoreCase);
            _tabOrder = new List<string>();
            _preloading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _thumbnailSizePixels = VirtualizedThumbnailGrid.DefaultThumbnailSize;
            _metadataPanelWidth = 330;

            _thumbnailSizeTimer = new DispatcherTimer();
            _thumbnailSizeTimer.Interval = TimeSpan.FromMilliseconds(240);
            _thumbnailSizeTimer.Tick += delegate
            {
                _thumbnailSizeTimer.Stop();
                ApplyThumbnailSize();
            };

            Title = BuildInfo.WindowTitle;
            using (var iconStream = typeof(MainWindow).Assembly.GetManifestResourceStream("Viewer.AppIcon.ico"))
            {
                var icons = new IconBitmapDecoder(iconStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Icon = icons.Frames.OrderByDescending(frame => frame.PixelWidth).First();
                Icon.Freeze();
            }
            Width = 1500;
            Height = 920;
            MinWidth = 980;
            MinHeight = 620;
            AllowDrop = true;
            ThemeManager.PrepareWindow(this);

            SessionState startupSession = _services.Sessions.Load();
            SourceInitialized += delegate
            {
                _normalWindowBounds = WindowPlacement.Restore(this, startupSession);
                _lastWindowState = startupSession.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
                InitializeVerticalSizing();
            };

            BuildInterface();
            BuildConfigurationContent();

            _rescanTimer = new DispatcherTimer();
            _rescanTimer.Interval = TimeSpan.FromMilliseconds(700);
            _rescanTimer.Tick += delegate
            {
                _rescanTimer.Stop();
                if (_batchWindow != null) return;
                if (!String.IsNullOrWhiteSpace(_currentFolder) && Directory.Exists(_currentFolder))
                {
                    bool imageChanged = _activeTabPath != null && String.Equals(_activeTabPath, _watcherDirtyImagePath, StringComparison.OrdinalIgnoreCase);
                    _watcherDirtyImagePath = null;
                    RefreshFolder();
                    if (!_rotationSaving && !_enhancementSaving && HasCurrentImage() && (imageChanged || !_displayedImageRevision.Matches(_activeTabPath)))
                    { if (imageChanged) _services.VramCache.Remove(_activeTabPath); LoadImage(_activeTabPath); LoadMetadata(_activeTabPath); }
                }
            };

            _searchTimer = new DispatcherTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(220);
            _searchTimer.Tick += delegate
            {
                _searchTimer.Stop();
                ApplySearch();
            };

            _statusTimer = new DispatcherTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(1);
            _statusTimer.Tick += delegate { UpdateStatus(); };
            _statusTimer.Start();

            _placementSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _placementSaveTimer.Tick += delegate { _placementSaveTimer.Stop(); SaveSession(); };
            LocationChanged += delegate { RememberWindowPlacement(); };
            SizeChanged += delegate { RememberWindowPlacement(); };
            StateChanged += delegate { RememberWindowPlacement(); };

            PreviewKeyDown += MainWindowPreviewKeyDown;
            PreviewMouseDown += MainWindowPreviewMouseDown;
            Deactivated += delegate { EndImagePan(); _thumbnailMinimap.EndDrag(); _imageNavigator.EndDrag(); };
            Drop += MainWindowDrop;
            DragOver += MainWindowDragOver;
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (_fileTransferBusy || _backupBusy || _rotationSaving || _enhancementSaving)
                {
                    e.Cancel = true;
                    MessageBox.Show(this, "Please wait for the current operation to finish.", "Operation in progress");
                    return;
                }
                _configurationOwnerClosing = true;
                if (_configureWindow != null) _configureWindow.Close();
                _searchHistoryInput.Commit();
                if (_advancedSearchWindow != null) _advancedSearchWindow.CommitSearchHistory();
                _placementSaveTimer.Stop(); SaveSession();
            };
            Closed += delegate
            {
                _isClosed = true;
                StopGpuPresentation();
                ClearAutomaticUpscale();
                if (_gpuSettingsTimer != null) _gpuSettingsTimer.Stop();
                _services.ThumbnailGpu.Configure("CPU", 128);
                HideFilmstrip();
                HideManualEnhance();
                HideImageNavigator();
                ResetZoomIndicator();
                _imageOverlayMetadata = null;
                UpdateImageMetadataOverlay();
                StopAnimation();
                if (_thumbnailRebuildRequest != null) _thumbnailRebuildRequest.Cancel();
                _imageLoadVersion++;
                _metadataLoadVersion++;
                StopSlideshow();
                CancelEnhancement();
                ReleaseSubjectSegmentation();
                _placementSaveTimer.Stop();
                _rescanTimer.Stop();
                _searchTimer.Stop();
                _statusTimer.Stop();
                _thumbnailSizeTimer.Stop();
                if (_scanCancellation != null) _scanCancellation.Cancel();
                if (_watcher != null) { _watcher.Dispose(); _watcher = null; }
                _folderNavigation.Dispose();
                _thumbnailMinimap.Dispose();
                _tabReorder.Dispose();
                if (_heightSource != null) _heightSource.RemoveHook(VerticalHeightMessage);
                if (_configureWindow != null) _configureWindow.Close();
                _sortVersion++;
            };
            Loaded += delegate
            {
                if (!_restored)
                {
                    ApplySessionState(startupSession, true);
                    _restored = true;
                    RememberWindowPlacement();
                }
            };
        }

        private void BuildInterface()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            _topToolbar = BuildToolbar();
            Grid.SetRow(_topToolbar, 0);
            root.Children.Add(_topToolbar);

            var body = new Grid();
            ThemeManager.Bind(body, Panel.BackgroundProperty, ThemeKeys.PaneBackground);
            _folderColumn = new ColumnDefinition { Width = new GridLength(260) };
            _folderSplitterColumn = new ColumnDefinition { Width = new GridLength(24) };
            body.ColumnDefinitions.Add(_folderColumn);
            body.ColumnDefinitions.Add(_folderSplitterColumn);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _metadataSplitterColumn = new ColumnDefinition { Width = new GridLength(4) };
            body.ColumnDefinitions.Add(_metadataSplitterColumn);
            _metadataColumn = new ColumnDefinition { Width = new GridLength(_metadataPanelWidth) };
            body.ColumnDefinitions.Add(_metadataColumn);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var left = BuildFolderPane();
            Grid.SetColumn(left, 0);
            body.Children.Add(left);

            _leftSplitter = new GridSplitter();
            _leftSplitter.HorizontalAlignment = HorizontalAlignment.Left;
            _leftSplitter.Width = 4;
            _leftSplitter.ResizeDirection = GridResizeDirection.Columns;
            _leftSplitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
            _leftSplitter.DragCompleted += delegate { RememberFolderWidth(); ScheduleSessionSave(); };
            ThemeManager.Bind(_leftSplitter, Panel.BackgroundProperty, ThemeKeys.Splitter);
            Grid.SetColumn(_leftSplitter, 1);
            body.Children.Add(_leftSplitter);
            _folderTreeToggle = new Button
            {
                Width = 20, Height = 48, Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12
            };
            _folderTreeToggle.Click += delegate { ToggleFolderTree(); };
            Grid.SetColumn(_folderTreeToggle, 1);
            body.Children.Add(_folderTreeToggle);
            UpdateFolderTreeToggle();

            var center = BuildCenterPane();
            Grid.SetColumn(center, 2);
            body.Children.Add(center);

            _rightSplitter = new GridSplitter();
            _rightSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            ThemeManager.Bind(_rightSplitter, Panel.BackgroundProperty, ThemeKeys.Splitter);
            Grid.SetColumn(_rightSplitter, 3);
            body.Children.Add(_rightSplitter);

            _metadataPanel = new MetadataPanel();
            Grid.SetColumn(_metadataPanel, 4);
            body.Children.Add(_metadataPanel);
            SetMetadataPanelVisible(false);
            _performanceBar = BuildPerformanceBar(); Grid.SetRow(_performanceBar, 2); root.Children.Add(_performanceBar);
        }

        private UIElement BuildToolbar()
        {
            var toolbar = new StackPanel();
            toolbar.Background = Brushes.Transparent;
            var commands = new ToolbarCommandPanel();

            var left = new WrapPanel();
            left.Orientation = Orientation.Horizontal;
            left.Margin = new Thickness(8, 6, 8, 6);
            left.MinHeight = 32;
            commands.Children.Add(left);
            _workflowCommands = new WrapPanel { Margin = new Thickness(8, 0, 8, 6) };
            _viewerNavigationCommands = WorkflowGroup(); _viewerZoomCommands = WorkflowGroup();
            _viewerEditCommands = WorkflowGroup(); _browserFileCommands = WorkflowGroup();
            _workflowCommands.Children.Add(_viewerNavigationCommands); _workflowCommands.Children.Add(_viewerZoomCommands);
            _workflowCommands.Children.Add(_browserFileCommands);

            left.Children.Add(CommandPresentation.Button("\uED25", "Open folder", "Ctrl+O", "Choose a folder to browse in this tab.", OpenFolderPicker, true));
            _sessionsButton = CommandPresentation.Button("\uE8F1", "Sessions", null, "Save or open a workspace session, or import/export a backup.", delegate { SessionsButtonClick(_sessionsButton, new RoutedEventArgs()); }, true);
            AutomationProperties.SetName(_sessionsButton, "Sessions");
            left.Children.Add(_sessionsButton);
            _viewerNavigationCommands.Children.Add(CommandPresentation.Button("\uE72B", "Back to folder", "Enter / Esc", "Return to thumbnails in this tab.", BrowseActiveTab, true));
            _viewerNavigationCommands.Children.Add(CommandPresentation.Button("\uE76B", "Previous image", "Left / Page Up", "Show the previous image in viewer mode.", delegate { OpenRelativeImage(-1); }, false));
            _viewerNavigationCommands.Children.Add(CommandPresentation.Button("\uE76C", "Next image", "Right / Page Down", "Show the next image in viewer mode.", delegate { OpenRelativeImage(1); }, false));
            _fitImageButton = CommandPresentation.Button("\uE9A6", "Fit image", "0", "Fit the image inside the frame without enlarging small images.", delegate { FitImageToView(true); }, false);
            _viewerZoomCommands.Children.Add(_fitImageButton);
            AddZoomControls(_viewerZoomCommands);
            _viewerZoomCommands.Children.Add(BuildVerticalHeightButton());
            _viewerZoomCommands.Children.Add(CommandPresentation.Button("\uE740", "Fullscreen", "F11", "Toggle fullscreen viewing.", ToggleFullscreen, false));
            var enhanceLabel = new StackPanel { Orientation = Orientation.Horizontal };
            enhanceLabel.Children.Add(new TextBlock { Text = "\uE790", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Margin = new Thickness(0, 0, 6, 0) });
            enhanceLabel.Children.Add(new TextBlock { Text = "Quick Enhance", VerticalAlignment = VerticalAlignment.Center });
            _enhanceButton = new ToggleButton
            {
                Content = enhanceLabel, Width = 132, Height = 30, Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(6, 2, 6, 2), IsEnabled = false, ToolTip = "Quick Enhance"
            };
            AutomationProperties.SetName(_enhanceButton, "Quick Enhance");
            _enhanceButton.Click += delegate { ToggleQuickEnhance(); };
            var quickGroup = WorkflowGroup(); quickGroup.Children.Add(_enhanceButton); _workflowCommands.Children.Add(quickGroup);
            _viewerEditCommands.Children.Add(BuildManualEnhanceButton());
            _viewerEditCommands.Children.Add(BuildUpscaleControl());
            _viewerEditCommands.Children.Add(BuildPrintButton());
            _workflowCommands.Children.Add(_viewerEditCommands);
            _enhanceMode = new ComboBox { Width = 100, Height = 30, Margin = new Thickness(0, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Quick Enhance mode" };
            _enhanceMode.Items.Add(new ComboBoxItem { Content = "Basic" });
            _enhanceMode.Items.Add(new ComboBoxItem { Content = "Advanced" });
            _enhanceMode.SelectedIndex = 0;
            AutomationProperties.SetName(_enhanceMode, "Quick Enhance mode");
            _enhanceMode.SelectionChanged += delegate
            {
                if (_syncEnhanceMode) return;
                _advancedQuickEnhance = _enhanceMode.SelectedIndex == 1;
                _enhanceButton.IsChecked = true;
                ToggleQuickEnhance();
            };
            _renameButton = CommandPresentation.Button("\uE8AC", "Rename", "F2", "Rename the displayed image or one selected file or folder.", RenameCurrent, true);
            _moveButton = CommandPresentation.Button("\uE8DE", "Move", null, "Move selected files and folders to another folder. Existing items are never overwritten.", MoveCurrent, true);
            _deleteButton = CommandPresentation.Button("\uE74D", "Recycle", "Delete", "Send selected files and folders to the Recycle Bin. In viewer mode, advance to the next image without closing the tab.", DeleteCurrent, true);
            _browserFileCommands.Children.Add(_renameButton);
            _batchButton = CommandPresentation.Button("\uE8F1", "Batch", null, "Rename, resize or convert the selected items with a preview.", delegate { OpenWorkflowMenu(_batchButton, BuildBatchMenu()); }, true);
            _browserFileCommands.Children.Add(_batchButton);
            _browserFileCommands.Children.Add(_moveButton);
            _browserFileCommands.Children.Add(_deleteButton);
            _fileMenuButton = CommandPresentation.Button("\uE8A5", "File", null, "Copy, cut, paste, rename, move, recycle, show in Explorer or copy the full path.",
                delegate { OpenWorkflowMenu(_fileMenuButton, BuildFileActionsMenu()); }, true);
            ((StackPanel)_fileMenuButton.Content).Children.Add(new TextBlock
            {
                Text = "\uE70D", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10,
                Width = 12, Margin = new Thickness(6, 0, 0, 0),
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            left.Children.Add(_fileMenuButton);
            _viewMenuButton = CommandPresentation.Button("\uE8A7", "View", null, "Panels, image overlays, fullscreen and performance details.",
                delegate { OpenWorkflowMenu(_viewMenuButton, BuildViewOptionsMenu()); }, true);
            left.Children.Add(_viewMenuButton);
            _configureButton = CommandPresentation.Button("\uE713", "Configure", "Ctrl+,", "Appearance, thumbnails, enhancement, slideshow, performance and image tools.", ShowConfigure, true);
            AutomationProperties.SetName(_configureButton, "Configure viewer");
            left.Children.Add(_configureButton);
            _helpButton = CommandPresentation.Button("\uE897", "Help", "F1", "Keyboard shortcuts, supported formats and version history.", ShowHelp, false);
            left.Children.Add(_helpButton);

            var right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.Margin = new Thickness(8, 6, 10, 6);
            right.Height = 32;
            commands.Children.Add(right);

            var searchLabel = new TextBlock();
            searchLabel.Text = "Search";
            searchLabel.VerticalAlignment = VerticalAlignment.Center;
            searchLabel.Margin = new Thickness(0, 0, 6, 0);
            ThemeManager.Bind(searchLabel, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            right.Children.Add(searchLabel);

            _searchBox = new TextBox();
            _searchBox.Width = 180;
            CommandPresentation.Describe(_searchBox, "Search this folder", "Ctrl+F", "Filter filenames in the current folder. Use Advanced search to include subfolders.");
            _searchBox.VerticalContentAlignment = VerticalAlignment.Center;
            _searchBox.Margin = new Thickness(0, 0, 2, 0);
            _searchBox.TextChanged += delegate
            {
                if (_clearSearchButton != null) _clearSearchButton.IsEnabled = !String.IsNullOrEmpty(_searchBox.Text);
                _searchTimer.Stop();
                _searchTimer.Start();
            };
            right.Children.Add(_searchBox);
            _clearSearchButton = CommandPresentation.Button("\uE711", "Clear search", null, "Remove the current filename filter.", ClearFolderSearch, false);
            _clearSearchButton.IsEnabled = false; _clearSearchButton.Margin = new Thickness(0, 0, 2, 0); right.Children.Add(_clearSearchButton);
            _searchHistoryInput = new SearchHistoryInput(_searchBox, _searchHistory, delegate { _searchTimer.Stop(); ApplySearch(); });
            _searchHistory.Changed += ScheduleSessionSave;
            right.Children.Add(_searchHistoryInput.Button);
            _advancedSearchButton = CommandPresentation.Button("\uE721", "Advanced search", "Ctrl+Shift+F", "Search the current folder and every subfolder.", ShowAdvancedSearch, false);
            right.Children.Add(_advancedSearchButton);

            _iccCheckBox = new CheckBox();
            _iccCheckBox.Content = "Low-priority ICC (experimental)";
            _iccCheckBox.VerticalAlignment = VerticalAlignment.Center;
            _iccCheckBox.Margin = new Thickness(0, 0, 12, 0);
            _iccCheckBox.Checked += delegate { _services.LowPriorityIccEnabled = true; };
            _iccCheckBox.Unchecked += delegate { _services.LowPriorityIccEnabled = false; };

            _darkThemeCheckBox = new CheckBox();
            _darkThemeCheckBox.Content = "Dark theme";
            _darkThemeCheckBox.VerticalAlignment = VerticalAlignment.Center;
            _darkThemeCheckBox.Margin = new Thickness(0, 0, 12, 0);
            _darkThemeCheckBox.IsChecked = ThemeManager.IsDarkTheme;
            _darkThemeCheckBox.Checked += delegate { ThemeManager.SetDarkTheme(true); };
            _darkThemeCheckBox.Unchecked += delegate { ThemeManager.SetDarkTheme(false); };
            _darkThemeCheckBox.ToolTip = "Use dark theme";


            _vramSlider = new Slider();
            _vramSlider.Minimum = 128;
            _vramSlider.Maximum = 24576;
            _vramSlider.Value = 1024;
            _vramSlider.TickFrequency = 1024;
            _vramSlider.IsSnapToTickEnabled = false;
            _vramSlider.Width = 150;
            _vramSlider.VerticalAlignment = VerticalAlignment.Center;
            _vramSlider.ValueChanged += delegate
            {
                int mb = (int)Math.Round(_vramSlider.Value / 128.0) * 128;
                _services.VramCache.SetBudgetMb(mb);
                UpdateStatus();
                ScheduleSessionSave();
            };

            _vramText = new TextBlock();
            _vramText.Width = 92;
            _vramText.VerticalAlignment = VerticalAlignment.Center;
            ThemeManager.Bind(_vramText, TextBlock.ForegroundProperty, ThemeKeys.MutedText);

            _statusText = new TextBlock();
            _gpuStatusPanel = new WrapPanel { Margin = new Thickness(8, 0, 8, 4) };
            _gpuStatusPanel.SizeChanged += delegate
            {
                foreach (TextBlock text in _gpuStatusPanel.Children) text.MaxWidth = Math.Max(100, _gpuStatusPanel.ActualWidth - 24);
            };
            _statusText.VerticalAlignment = VerticalAlignment.Center;
            _statusText.Margin = new Thickness(8, 0, 8, 0);
            _statusText.TextTrimming = TextTrimming.CharacterEllipsis;
            _statusText.TextWrapping = TextWrapping.NoWrap;
            _statusText.Height = 20;
            ThemeManager.Bind(_statusText, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            toolbar.Children.Add(commands);
            toolbar.Children.Add(_workflowCommands);

            var surface = new Grid { ClipToBounds = true };
            _toolbarBackground = new ToolbarBackground();
            surface.Children.Add(_toolbarBackground); surface.Children.Add(toolbar);
            return surface;
        }

        private Button ToolbarButton(string text, RoutedEventHandler handler)
        {
            var button = new Button();
            button.Content = text;
            button.Margin = new Thickness(0, 0, 6, 0);
            button.Padding = new Thickness(10, 3, 10, 3);
            button.MinWidth = 50;
            button.Click += handler;
            return button;
        }

        private void SessionsButtonClick(object sender, RoutedEventArgs e)
        {
            ContextMenu menu = BuildSessionsMenu();
            menu.PlacementTarget = _sessionsButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private ContextMenu BuildSessionsMenu()
        {
            var menu = new ContextMenu();
            ThemeManager.Bind(menu, Control.BackgroundProperty, ThemeKeys.ControlBackground);
            ThemeManager.Bind(menu, Control.ForegroundProperty, ThemeKeys.Text);
            ThemeManager.Bind(menu, Control.BorderBrushProperty, ThemeKeys.Border);

            var save = new MenuItem();
            save.Header = "Save current session...";
            save.Click += delegate { SaveNamedSession(); };
            menu.Items.Add(save);
            menu.Items.Add(new Separator());

            IList<NamedSessionInfo> sessions = _services.Sessions.ListNamedSessions();
            var open = new MenuItem();
            open.Header = "Open saved session";
            var delete = new MenuItem();
            delete.Header = "Delete saved session";

            if (sessions.Count == 0)
            {
                var empty = new MenuItem();
                empty.Header = "No saved sessions";
                empty.IsEnabled = false;
                open.Items.Add(empty);
                delete.IsEnabled = false;
            }
            else
            {
                for (int i = 0; i < sessions.Count; i++)
                {
                    string sessionName = sessions[i].Name;

                    var openItem = new MenuItem();
                    openItem.Header = sessionName;
                    openItem.IsCheckable = true;
                    openItem.IsChecked = String.Equals(
                        sessionName,
                        _activeNamedSessionName,
                        StringComparison.OrdinalIgnoreCase);
                    openItem.Click += delegate { LoadNamedSession(sessionName); };
                    open.Items.Add(openItem);

                    var deleteItem = new MenuItem();
                    deleteItem.Header = sessionName;
                    deleteItem.Click += delegate { DeleteNamedSession(sessionName); };
                    delete.Items.Add(deleteItem);
                }
            }

            menu.Items.Add(open);
            menu.Items.Add(delete);
            menu.Items.Add(new Separator());
            var export = new MenuItem { Header = "Export backup...", Tag = "export-backup", IsEnabled = !_backupBusy && !_fileTransferBusy };
            export.Click += ExportBackupClick;
            menu.Items.Add(export);
            var import = new MenuItem { Header = "Import backup...", Tag = "import-backup", IsEnabled = !_backupBusy && !_fileTransferBusy };
            import.Click += ImportBackupClick;
            menu.Items.Add(import);
            return menu;
        }

        private void SaveNamedSession()
        {
            string name = TextPromptWindow.Prompt(
                this,
                "Save session",
                "Session name",
                _activeNamedSessionName ?? "");
            if (name == null)
            {
                return;
            }

            name = name.Trim();
            if (name.Length == 0 || name.Length > 80)
            {
                MessageBox.Show(
                    this,
                    "Enter a session name between 1 and 80 characters.",
                    "Save session",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                if (_services.Sessions.NamedSessionExists(name))
                {
                    MessageBoxResult overwrite = MessageBox.Show(
                        this,
                        "Replace the saved session '" + name + "'?",
                        "Save session",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (overwrite != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                SessionState state = CaptureSessionState();
                state.ActiveNamedSessionName = name;
                _services.Sessions.SaveNamed(name, state);
                _activeNamedSessionName = name;
                UpdateWindowTitle();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Unable to save session",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void LoadNamedSession(string name)
        {
            try
            {
                SessionState state = _services.Sessions.LoadNamed(name);
                if (state == null)
                {
                    throw new InvalidDataException("The saved session could not be read.");
                }

                ApplySessionState(state, false);
                _activeNamedSessionName = name;
                UpdateWindowTitle();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Unable to open session",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void DeleteNamedSession(string name)
        {
            MessageBoxResult result = MessageBox.Show(
                this,
                "Delete the saved session '" + name + "'?",
                "Delete session",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _services.Sessions.DeleteNamed(name);
                if (String.Equals(name, _activeNamedSessionName, StringComparison.OrdinalIgnoreCase))
                {
                    _activeNamedSessionName = null;
                    UpdateWindowTitle();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Unable to delete session",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void UpdateWindowTitle()
        {
            Title = String.IsNullOrWhiteSpace(_activeNamedSessionName)
                ? BuildInfo.WindowTitle
                : BuildInfo.WindowTitle + " - " + _activeNamedSessionName;
            if (_services.InstanceNumber > 1) Title += " - Window " + _services.InstanceNumber;
        }

        private UIElement BuildFolderPane(BrowserPreferences preferences = null)
        {
            _folderNavigation = new FolderNavigationPane(preferences ?? _services.Sessions.LoadBrowserPreferences());
            _folderNavigation.FolderRequested += delegate(object sender, FolderNavigationEventArgs e)
            {
                if (e.NewTab) OpenFolderTab(e.Path);
                else LoadFolder(e.Path);
            };
            _folderNavigation.PreferencesChanged += delegate { UpdateFavoriteButton(); ScheduleSessionSave(); };
            return _folderNavigation;
        }

        private UIElement BuildCenterPane()
        {
            var center = new Grid();
            center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            UIElement footer = BuildBrowserFooter();
            Grid.SetRow(footer, 2);
            center.Children.Add(footer);

            var tabBar = new DockPanel();
            tabBar.MinHeight = 46;
            ThemeManager.Bind(tabBar, Panel.BackgroundProperty, ThemeKeys.TabBarBackground);
            tabBar.AllowDrop = true;
            tabBar.Drop += MainWindowDrop;
            tabBar.DragOver += MainWindowDragOver;

            _browserTabButton = new Button();
            _browserTabButton.Content = "Browser";
            _browserTabButton.Margin = new Thickness(6, 5, 4, 5);
            _browserTabButton.Padding = new Thickness(12, 2, 12, 2);
            _browserTabButton.Click += delegate { ShowBrowser(); };
            _browserTabButton.ContextMenu = BuildTabContextMenu(null);
            DockPanel.SetDock(_browserTabButton, Dock.Left);
            tabBar.Children.Add(_browserTabButton);

            _metadataToggle = new CheckBox();
            _metadataToggle.Content = "Metadata";
            _metadataToggle.VerticalAlignment = VerticalAlignment.Center;
            _metadataToggle.Margin = new Thickness(10, 0, 12, 0);
            _metadataToggle.ToolTip = "Show metadata panel";
            AutomationProperties.SetName(_metadataToggle, "Show metadata panel");
            _metadataToggle.Checked += delegate { SetMetadataPanelVisible(true); };
            _metadataToggle.Unchecked += delegate { SetMetadataPanelVisible(false); };
            DockPanel.SetDock(_metadataToggle, Dock.Right);

            var scroller = new ScrollViewer();
            _tabScroller = scroller;
            scroller.SizeChanged += delegate { UpdateTabWidths(); RevealActiveTab(); };
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _tabStrip = new StackPanel();
            _tabStrip.Orientation = Orientation.Horizontal;
            _newTabButton = new Button
            {
                Content = "+", Width = 32, Height = 32, FontSize = 20,
                Padding = new Thickness(0), Margin = new Thickness(2, 5, 6, 5),
                ToolTip = "New browser tab"
            };
            AutomationProperties.SetName(_newTabButton, "New browser tab");
            CommandPresentation.Describe(_newTabButton, "New browser tab", "Ctrl+T", "Open a new tab using the latest folder browser view.");
            _newTabButton.Click += delegate { OpenLatestBrowserTab(); };
            scroller.Content = _tabStrip;
            _tabReorder = new ReorderDrag(scroller, true,
                TabReorderItems, ReorderVisibleTab, delegate { return scroller; });
            tabBar.Children.Add(scroller);

            Grid.SetRow(tabBar, 0);
            center.Children.Add(tabBar);

            var workspace = new Grid();
            Grid.SetRow(workspace, 1);
            center.Children.Add(workspace);

            _browserView = new Grid();
            _browserView.AllowDrop = true;
            _browserView.DragOver += BrowserDragOver;
            _browserView.Drop += BrowserDrop;
            _browserView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _browserView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            ThemeManager.Bind(_browserView, Panel.BackgroundProperty, ThemeKeys.WorkspaceBackground);

            var browserHeader = new Grid();
            browserHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            browserHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            browserHeader.Margin = new Thickness(12, 10, 12, 8);

            var thumbnailControls = new WrapPanel();
            thumbnailControls.Orientation = Orientation.Horizontal;
            thumbnailControls.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(thumbnailControls, 1);
            thumbnailControls.Margin = new Thickness(0, 8, 0, 0);
            var sizeControls = new StackPanel { Orientation = Orientation.Horizontal };

            var thumbnailLabel = new TextBlock();
            thumbnailLabel.Text = "Size";
            thumbnailLabel.VerticalAlignment = VerticalAlignment.Center;
            thumbnailLabel.Margin = new Thickness(0, 0, 6, 0);
            ThemeManager.Bind(thumbnailLabel, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            sizeControls.Children.Add(thumbnailLabel);

            _thumbnailSizeSlider = new Slider();
            _thumbnailSizeSlider.Minimum = VirtualizedThumbnailGrid.MinimumThumbnailSize;
            _thumbnailSizeSlider.Maximum = VirtualizedThumbnailGrid.MaximumThumbnailSize;
            _thumbnailSizeSlider.TickFrequency = VirtualizedThumbnailGrid.ThumbnailSizeStep;
            _thumbnailSizeSlider.IsSnapToTickEnabled = true;
            _thumbnailSizeSlider.Value = _thumbnailSizePixels;
            _thumbnailSizeSlider.Width = 140;
            _thumbnailSizeSlider.VerticalAlignment = VerticalAlignment.Center;
            _thumbnailSizeSlider.ToolTip = "Thumbnail size";
            AutomationProperties.SetName(_thumbnailSizeSlider, "Thumbnail size");
            _thumbnailSizeSlider.ValueChanged += ThumbnailSizeSliderValueChanged;
            sizeControls.Children.Add(_thumbnailSizeSlider);

            _thumbnailSizeText = new TextBlock();
            _thumbnailSizeText.Width = 52;
            _thumbnailSizeText.Margin = new Thickness(7, 0, 0, 0);
            _thumbnailSizeText.VerticalAlignment = VerticalAlignment.Center;
            _thumbnailSizeText.Text = _thumbnailSizePixels.ToString() + " px";
            ThemeManager.Bind(_thumbnailSizeText, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            sizeControls.Children.Add(_thumbnailSizeText);

            var aspectControls = new StackPanel { Orientation = Orientation.Horizontal };
            var aspectLabel = new TextBlock { Text = "Shape", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 6, 0) };
            ThemeManager.Bind(aspectLabel, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            aspectControls.Children.Add(aspectLabel);
            _thumbnailAspectSlider = new Slider { Minimum = 0.5, Maximum = 2, TickFrequency = 0.25,
                IsSnapToTickEnabled = true, Value = 1, Width = 120, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Thumbnail frame aspect ratio" };
            AutomationProperties.SetName(_thumbnailAspectSlider, "Thumbnail aspect ratio");
            _thumbnailAspectText = new TextBlock { Text = "1:1", Width = 50,
                Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            ThemeManager.Bind(_thumbnailAspectText, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            _thumbnailAspectSlider.ValueChanged += delegate
            {
                _thumbnailAspectRatio = VirtualizedThumbnailGrid.NormalizeAspectRatio(_thumbnailAspectSlider.Value);
                _thumbnailAspectText.Text = AspectRatioLabel(_thumbnailAspectRatio);
                _thumbnailSizeTimer.Stop();
                _thumbnailSizeTimer.Start();
            };
            aspectControls.Children.Add(_thumbnailAspectSlider);
            aspectControls.Children.Add(_thumbnailAspectText);
            var sortControls = new StackPanel { Orientation = Orientation.Horizontal };
            var sortLabel = new TextBlock { Text = "Sort", Margin = new Thickness(12, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center };
            ThemeManager.Bind(sortLabel, TextBlock.ForegroundProperty, ThemeKeys.MutedText);
            sortControls.Children.Add(sortLabel);
            _sortFieldBox = new ComboBox { Width = 154, Height = 28, VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Sort by" };
            AutomationProperties.SetName(_sortFieldBox, "Sort by");
            string[] sortLabels = { "Name", "Number in name", "Date created", "Date modified", "File size", "File type" };
            for (int i = 0; i < sortLabels.Length; i++)
                _sortFieldBox.Items.Add(new ComboBoxItem { Content = sortLabels[i], Tag = (BrowserSortField)i });
            _sortFieldBox.SelectedIndex = 0;
            _sortFieldBox.SelectionChanged += delegate { SortSelectionChanged(); };
            sortControls.Children.Add(_sortFieldBox);
            _sortDirectionButton = new ToggleButton { Content = "\u2191", Width = 28, Height = 28,
                FontSize = 18, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Ascending order" };
            AutomationProperties.SetName(_sortDirectionButton, "Descending sort order");
            _sortDirectionButton.Click += delegate { SortSelectionChanged(); };
            sortControls.Children.Add(_sortDirectionButton);
            thumbnailControls.Children.Add(sortControls);
            var thumbnailSettings = CommandPresentation.Button("\uE8A9", "Thumbnails", null, "Change thumbnail size and shape in Configure.", delegate
            {
                ShowConfigure(); SelectConfigurationPage(1); _thumbnailSizeSlider.Focus(); _thumbnailSizeSlider.BringIntoView();
            }, true);
            thumbnailSettings.Margin = new Thickness(12, 0, 0, 0); thumbnailControls.Children.Add(thumbnailSettings);
            BuildMinimapButton();
            browserHeader.Children.Add(thumbnailControls);

            _folderTitle = new TextBlock();
            _folderTitle.FontSize = 16;
            _folderTitle.FontWeight = FontWeights.SemiBold;
            _folderTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            ThemeManager.Bind(_folderTitle, TextBlock.ForegroundProperty, ThemeKeys.Text);
            var locationBar = new DockPanel();
            _browserUpButton = new Button { Content = new TextBlock { Text = "\u2191", FontSize = 18 },
                Width = 28, Height = 28, Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(0), ToolTip = "Parent folder", IsEnabled = false };
            AutomationProperties.SetName(_browserUpButton, "Parent folder");
            _browserUpButton.Click += delegate { NavigateUp(); };
            DockPanel.SetDock(_browserUpButton, Dock.Left);
            locationBar.Children.Add(_browserUpButton);
            _favoriteButton = new ToggleButton { Content = "\u2606", FontSize = 18, Width = 28, Height = 28,
                Margin = new Thickness(8, 0, 0, 0), ToolTip = "Pin folder to favorites" };
            AutomationProperties.SetName(_favoriteButton, "Pin folder to favorites");
            _favoriteButton.Click += delegate { _folderNavigation.ToggleFavorite(_currentFolder); UpdateFavoriteButton(); };
            DockPanel.SetDock(_favoriteButton, Dock.Right);
            locationBar.Children.Add(_favoriteButton);
            _folderTitle.VerticalAlignment = VerticalAlignment.Center;
            _folderLocation = new FolderLocationBar(NavigateFolderAddress);
            locationBar.Children.Add(_folderLocation);
            browserHeader.Children.Add(locationBar);
            Grid.SetRow(browserHeader, 0);
            _browserView.Children.Add(browserHeader);

            _thumbnailGrid = new VirtualizedThumbnailGrid(_services.Thumbnails);
            _thumbnailGrid.IsFavoriteFolder = path => _folderNavigation.IsFavorite(path);
            _thumbnailGrid.ItemActivated += delegate(object sender, ThumbnailActivatedEventArgs e)
            {
                ActivateBrowserItem(e.Item);
            };
            _thumbnailGrid.ItemSelected += delegate(object sender, ThumbnailActivatedEventArgs e)
            {
                if (e.Item == null) { _metadataLoadVersion++; _metadataPanel.ShowMessage(""); }
                else if (e.Item.IsDirectory)
                {
                    _metadataLoadVersion++;
                    _metadataPanel.ShowMessage("Folder: " + e.Item.Path);
                }
                else LoadMetadata(e.Item.Path);
                UpdateStatus();
                ScheduleSessionSave();
            };
            _thumbnailGrid.ScrollChanged += delegate { if (!_folderScanPending) ScheduleSessionSave(); };
            _thumbnailGrid.CommandRequested += ThumbnailCommandRequested;
            _thumbnailGrid.CanPaste = CanPasteFiles;
            _thumbnailGrid.DragRequested += StartBrowserDrag;
            Grid thumbnailWorkspace = BuildThumbnailWorkspace();
            Grid.SetRow(thumbnailWorkspace, 1);
            _browserView.Children.Add(thumbnailWorkspace);
            workspace.Children.Add(_browserView);

            _imageView = new Grid();
            ThemeManager.Bind(_imageView, Panel.BackgroundProperty, ThemeKeys.ImageBackground);
            _imageView.Visibility = Visibility.Collapsed;
            _imageView.ClipToBounds = true;

            _imageCanvas = new Canvas();
            _imageCanvas.Focusable = true;
            ThemeManager.Bind(_imageCanvas, Panel.BackgroundProperty, ThemeKeys.ImageBackground);
            _imageCanvas.ClipToBounds = true;
            _imageCanvas.MouseWheel += ImageCanvasMouseWheel;
            _imageCanvas.MouseLeftButtonDown += ImageCanvasMouseLeftButtonDown;
            _imageCanvas.MouseMove += ImageCanvasMouseMove;
            _imageCanvas.MouseLeftButtonUp += ImageCanvasMouseLeftButtonUp;
            _imageCanvas.LostMouseCapture += delegate { EndImagePan(); };
            _imageCanvas.MouseRightButtonDown += delegate(object sender, MouseButtonEventArgs e) { e.Handled = true; };
            _imageCanvas.SizeChanged += delegate
            {
                _manualImageSurface.Width = _imageCanvas.ActualWidth;
                _manualImageSurface.Height = _imageCanvas.ActualHeight;
                QueueImageView();
            };

            _mainImage = new Image();
            // Match the bitmap's pixel aspect, independent of embedded print DPI.
            _mainImage.Stretch = Stretch.Fill;
            _mainImage.RenderTransformOrigin = new Point(0, 0);
            RenderOptions.SetBitmapScalingMode(_mainImage, BitmapScalingMode.HighQuality);
            _imageScale = new ScaleTransform(1, 1);
            _imageRotation = new MatrixTransform();
            _imageTranslate = new TranslateTransform(0, 0);
            var transform = new TransformGroup();
            transform.Children.Add(_imageRotation);
            transform.Children.Add(_imageScale);
            transform.Children.Add(_imageTranslate);
            _mainImage.RenderTransform = transform;
            Canvas.SetLeft(_mainImage, 0);
            Canvas.SetTop(_mainImage, 0);
            // Apply manual adjustments to the clipped image surface, never to the backdrop or overlays.
            _manualImageSurface = new Canvas { ClipToBounds = true };
            _manualImageSurface.Children.Add(_mainImage);
            BuildGpuPresentation();
            _imageCanvas.Children.Add(_manualImageSurface);
            _imageView.Children.Add(_imageCanvas);
            _imageError = new TextBlock { Text = "Image unavailable", TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            ThemeManager.Bind(_imageError, TextBlock.ForegroundProperty, ThemeKeys.Text);
            _imageView.Children.Add(_imageError);
            BuildZoomIndicator();
            BuildImageMetadataOverlay();
            BuildSlideshowOverlay();
            BuildImageNavigator();
            BuildManualEnhanceOverlay();
            BuildFilmstrip();
            workspace.Children.Add(_imageView);

            return center;
        }

        private void SetMetadataPanelVisible(bool visible)
        {
            if (_metadataColumn == null || _metadataSplitterColumn == null
                || _rightSplitter == null || _metadataPanel == null)
            {
                return;
            }

            if (!visible && _metadataColumn.ActualWidth >= 180)
            {
                _metadataPanelWidth = _metadataColumn.ActualWidth;
            }

            _metadataColumn.Width = visible
                ? new GridLength(Math.Max(220, _metadataPanelWidth))
                : new GridLength(0);
            _metadataSplitterColumn.Width = visible ? new GridLength(4) : new GridLength(0);
            _rightSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _metadataPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (_metadataToggle != null && _metadataToggle.IsChecked != visible)
            {
                _metadataToggle.IsChecked = visible;
            }
        }

        private void ThumbnailSizeSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _thumbnailSizePixels = VirtualizedThumbnailGrid.NormalizeThumbnailSize(e.NewValue);
            if (_thumbnailSizeText != null)
            {
                _thumbnailSizeText.Text = _thumbnailSizePixels.ToString() + " px";
            }

            _thumbnailSizeTimer.Stop();
            _thumbnailSizeTimer.Start();
        }

        private void ApplyThumbnailSize()
        {
            if (_thumbnailGrid != null)
            {
                _thumbnailGrid.SetThumbnailLayout(_thumbnailSizePixels, _thumbnailAspectRatio);
                ScheduleSessionSave();
            }
        }

        private static string AspectRatioLabel(double ratio)
        {
            int width = (int)Math.Round(ratio * 4), height = 4;
            for (int divisor = 4; divisor > 1; divisor--)
                if (width % divisor == 0 && height % divisor == 0) { width /= divisor; height /= divisor; }
            return width + ":" + height;
        }

        private void UpdateFavoriteButton()
        {
            if (_favoriteButton == null) return;
            bool pinned = _folderNavigation.IsFavorite(_currentFolder);
            _favoriteButton.IsChecked = pinned;
            _favoriteButton.Content = pinned ? "\u2605" : "\u2606";
            _favoriteButton.IsEnabled = !String.IsNullOrWhiteSpace(_currentFolder);
            _favoriteButton.ToolTip = pinned ? "Unpin folder from favorites" : "Pin folder to favorites";
        }

        private void LoadFolder(string folder)
        {
            NavigateFolder(folder, false);
        }

        private void NavigateFolder(string folder, bool keepForwardHistory)
        {
            if (!Directory.Exists(folder)) return;
            SaveWorkspaceState();
            ImageTabState state = CurrentTabState();
            if (!keepForwardHistory) state.ForwardFolders.Clear();
            state.IsBrowser = true;
            state.FolderPath = folder;
            state.SearchText = "";
            state.BrowserScrollOffset = 0;
            state.SelectedPath = null;
            state.SelectedPaths.Clear();
            DisplayTab();
        }

        private string ParentFolder()
        {
            try
            {
                DirectoryInfo parent = String.IsNullOrEmpty(_currentFolder) ? null : Directory.GetParent(_currentFolder);
                return parent == null ? null : parent.FullName;
            }
            catch (Exception) { return null; }
        }

        private void NavigateUp()
        {
            string parent = ParentFolder();
            if (parent == null || !Directory.Exists(parent)) return;
            string child = _currentFolder;
            CurrentTabState().ForwardFolders.Add(child);
            NavigateFolder(parent, true);
            CurrentTabState().SelectedPath = child;
            if (!_folderScanPending) RestoreBrowserPosition();
        }

        private bool IsImmediateSubfolder(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder) || String.IsNullOrWhiteSpace(_currentFolder)) return false;
            try
            {
                DirectoryInfo parent = Directory.GetParent(Path.GetFullPath(folder));
                return parent != null && String.Equals(parent.FullName.TrimEnd('\\'),
                    Path.GetFullPath(_currentFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private void NavigateDown()
        {
            List<string> history = CurrentTabState().ForwardFolders;
            while (history.Count > 0)
            {
                string child = history[history.Count - 1];
                history.RemoveAt(history.Count - 1);
                if (IsImmediateSubfolder(child) && Directory.Exists(child))
                {
                    NavigateFolder(child, true);
                    return;
                }
            }
            ImageFileItem selected = _thumbnailGrid.SelectedItem;
            if (selected != null && selected.IsDirectory && IsImmediateSubfolder(selected.Path)) LoadFolder(selected.Path);
            ScheduleSessionSave();
        }

        private void ActivateBrowserItem(ImageFileItem item)
        {
            if (item == null) return;
            if (item.IsDirectory) LoadFolder(item.Path);
            else OpenBrowserImage(item.Path);
        }

        private void OpenFolderTab(string folder)
        {
            if (!Directory.Exists(folder)) return;
            SaveWorkspaceState();
            string id = Guid.NewGuid().ToString("N");
            _tabs[id] = new ImageTabState { IsBrowser = true, FolderPath = folder, LastAccessUtc = DateTime.UtcNow };
            _tabOrder.Add(id);
            _activeTabId = id;
            DisplayTab();
        }

        private static ImageTabState CopyBrowserView(ImageTabState state)
        {
            return new ImageTabState
            {
                IsBrowser = true, FolderPath = state.FolderPath, SearchText = state.SearchText,
                SelectedPath = state.SelectedPath, BrowserScrollOffset = state.BrowserScrollOffset,
                SelectedPaths = new List<string>(state.SelectedPaths),
                SortField = state.SortField, SortDescending = state.SortDescending,
                ForwardFolders = new List<string>(state.ForwardFolders)
            };
        }

        private void OpenLatestBrowserTab()
        {
            SaveWorkspaceState();
            string id = Guid.NewGuid().ToString("N");
            _tabs[id] = CopyBrowserView(_latestBrowserView ?? _homeBrowser);
            _tabOrder.Add(id);
            _activeTabId = id;
            DisplayTab();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { _newTabButton.BringIntoView(); }));
        }

        private ContextMenu BuildTabContextMenu(string id)
        {
            var menu = new ContextMenu();
            ThemeManager.Bind(menu, Control.BackgroundProperty, ThemeKeys.ControlBackground);
            ThemeManager.Bind(menu, Control.ForegroundProperty, ThemeKeys.Text);
            ThemeManager.Bind(menu, Control.BorderBrushProperty, ThemeKeys.Border);
            var duplicate = new MenuItem { Header = "Duplicate tab" };
            duplicate.Click += delegate { DuplicateTab(id); };
            menu.Items.Add(duplicate);
            if (id != null)
            {
                var pin = new MenuItem { Tag = "pin-tab" };
                pin.Click += delegate { ToggleTabPin(id); };
                menu.Items.Add(pin);
                AddTabStackCommands(menu, id);
                menu.Items.Add(new Separator());
                var close = new MenuItem { Header = "Close tab", Tag = "close-tab" };
                close.Click += delegate { CloseTab(id); };
                menu.Items.Add(close);
                Action update = delegate
                {
                    ImageTabState state;
                    bool exists = _tabs.TryGetValue(id, out state);
                    pin.Header = exists && state.IsPinned ? "Unpin tab" : "Pin tab";
                    pin.IsEnabled = exists;
                    close.IsEnabled = exists && !state.IsPinned;
                };
                update();
                menu.Opened += delegate { update(); };
            }
            return menu;
        }

        private void DuplicateTab(string id)
        {
            SaveWorkspaceState();
            ImageTabState source = _homeBrowser;
            if (id != null && !_tabs.TryGetValue(id, out source)) return;
            string copyId = Guid.NewGuid().ToString("N");
            _tabs[copyId] = source.Copy();
            _tabs[copyId].IsPinned = false;
            _tabOrder.Insert(id == null ? 0 : _tabOrder.IndexOf(id) + 1, copyId);
            _activeTabId = copyId;
            DisplayTab();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                Border header = _tabStrip.Children.OfType<Border>().FirstOrDefault(tab => Object.Equals(tab.Tag, copyId));
                if (header != null) header.BringIntoView();
            }));
        }

        private ImageTabState CurrentTabState()
        {
            ImageTabState state;
            return _activeTabId != null && _tabs.TryGetValue(_activeTabId, out state) ? state : _homeBrowser;
        }

        private void SaveWorkspaceState()
        {
            SaveActiveViewState();
            ImageTabState state = CurrentTabState();
            if (_activeTabId != null || _browserView.Visibility == Visibility.Visible)
                state.SearchText = _searchBox.Text;
            if (state.IsBrowser && _browserView.Visibility == Visibility.Visible)
            {
                if (!_folderScanPending)
                {
                    state.BrowserScrollOffset = _thumbnailGrid.VerticalOffset;
                    state.SelectedPath = _thumbnailGrid.SelectedItem == null ? null : _thumbnailGrid.SelectedItem.Path;
                    state.SelectedPaths = new List<string>(_thumbnailGrid.SelectedPaths);
                }
                _latestBrowserView = CopyBrowserView(state);
            }
        }

        private void RefreshFolder()
        {
            SaveWorkspaceState();
            ScanFolder(_currentFolder);
        }

        private void RestoreBrowserPosition()
        {
            ImageTabState state = CurrentTabState();
            _thumbnailGrid.RestoreSelection(state.SelectedPaths, state.SelectedPath);
            _thumbnailGrid.ScrollToVerticalOffset(Math.Max(0, state.BrowserScrollOffset));
            RevealReturnedThumbnail();
            UpdateBrowserFooter();
        }

        private void ScanFolder(string folder)
        {
            _sortVersion++;
            _appliedSortField = null;
            _isScanningFolder = true;
            if (_scanCancellation != null)
            {
                _scanCancellation.Cancel();
            }

            _currentFolder = folder;
            UpdateFavoriteButton();
            _browserUpButton.IsEnabled = ParentFolder() != null;
            _folderTitle.Text = folder ?? "Folders";
            _allItems.Clear();
            _filteredItems.Clear();
            _navigationImages.Clear();
            _folderSummary = null;
            _imageOrdinals.Clear();
            _thumbnailGrid.SetItems(_filteredItems);
            _thumbnailGrid.SelectItem(null);
            InstallWatcher(folder);

            _scanCancellation = new CancellationTokenSource();
            CancellationToken token = _scanCancellation.Token;
            _folderScanPending = true;
            if (!Directory.Exists(folder))
            {
                _folderScanPending = false;
                _isScanningFolder = false;
                _folderTitle.Text = "Folder unavailable: " + folder;
                return;
            }
            // Queue batches explicitly so they precede completion even without a UI synchronization context.
            Action<List<ImageFileItem>> progress = delegate(List<ImageFileItem> batch)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (!token.IsCancellationRequested) AddScannedBatch(batch);
                }));
            };

            _services.Catalog.ScanFolderAsync(folder, progress, token).ContinueWith(
                delegate(Task<FolderScanSummary> scan)
                {
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(
                            delegate
                            {
                                if (scan.IsCanceled || token.IsCancellationRequested)
                                {
                                    return;
                                }

                                _isScanningFolder = false;
                                if (scan.Status == TaskStatus.RanToCompletion) _folderSummary = scan.Result;
                                SortItems();
                                if (scan.Exception != null)
                                {
                                    _metadataPanel.ShowMessage("Unable to finish scanning this folder");
                                }

                                UpdateStatus();
                            }));
                },
                TaskScheduler.Default);
        }

        private void AddScannedBatch(List<ImageFileItem> batch)
        {
            string search = (_searchBox.Text ?? "").Trim();
            _allItems.AddRange(batch);
            if (String.IsNullOrWhiteSpace(search))
            {
                _filteredItems.AddRange(batch);
            }
            else
            {
                string needle = search.ToLowerInvariant();
                for (int i = 0; i < batch.Count; i++)
                {
                    if (batch[i].Name.ToLowerInvariant().Contains(needle))
                    {
                        _filteredItems.Add(batch[i]);
                    }
                }
            }

            _thumbnailGrid.RefreshTiles();
            foreach (ImageFileItem item in batch)
                if (!item.IsDirectory && (String.IsNullOrWhiteSpace(search)
                    || item.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)) _navigationImages.Add(item);
            UpdateStatus();
        }

        private void SyncSortControls()
        {
            ImageTabState state = CurrentTabState();
            _syncingSortControls = true;
            _sortFieldBox.SelectedIndex = (int)BrowserSort.Normalize(state.SortField);
            _sortDirectionButton.IsChecked = state.SortDescending;
            _sortDirectionButton.Content = state.SortDescending ? "\u2193" : "\u2191";
            _sortDirectionButton.ToolTip = state.SortDescending ? "Descending order" : "Ascending order";
            _syncingSortControls = false;
        }

        private void SortSelectionChanged()
        {
            if (_syncingSortControls || _sortDirectionButton == null) return;
            SaveWorkspaceState();
            ImageTabState state = CurrentTabState();
            state.SortField = BrowserSort.Normalize((BrowserSortField)_sortFieldBox.SelectedIndex);
            state.SortDescending = _sortDirectionButton.IsChecked == true;
            SyncSortControls();
            SortItems();
            ScheduleSessionSave();
        }

        private async void SortItems()
        {
            if (_isScanningFolder) return;
            int version = ++_sortVersion;
            var field = BrowserSort.Normalize(CurrentTabState().SortField);
            bool descending = CurrentTabState().SortDescending;
            ImageFileItem[] snapshot = _allItems.ToArray();
            _folderScanPending = true;
            bool failed = false;
            try
            {
                await Task.Run(delegate { Array.Sort(snapshot, new BrowserSort(field, descending)); }).ConfigureAwait(false);
            }
            catch (Exception)
            {
                failed = true;
            }
            if (failed)
            {
                if (!Dispatcher.HasShutdownStarted)
                    await Dispatcher.InvokeAsync(new Action(delegate
                    {
                        if (version != _sortVersion) return;
                        _folderScanPending = false;
                        _metadataPanel.ShowMessage("Unable to sort this folder");
                    }));
                return;
            }
            if (Dispatcher.HasShutdownStarted) return;
            await Dispatcher.InvokeAsync(new Action(delegate
            {
                if (version != _sortVersion) return;
                _allItems.Clear();
                _allItems.AddRange(snapshot);
                RebuildImageOrdinals();
                _appliedSortField = field;
                _appliedSortDescending = descending;
                ApplySearch();
                _folderScanPending = false;
                RestoreBrowserPosition();
                FlushDeferredImageNavigation();
                if (_activeTabPath != null) PreloadAround(_activeTabPath);
            }));
        }

        private void ApplySearch()
        {
            string search = (_searchBox.Text ?? "").Trim();
            _filteredItems.Clear();
            if (String.IsNullOrWhiteSpace(search))
            {
                _filteredItems.AddRange(_allItems);
            }
            else
            {
                string needle = search.ToLowerInvariant();
                for (int i = 0; i < _allItems.Count; i++)
                {
                    if (_allItems[i].Name.ToLowerInvariant().Contains(needle))
                    {
                        _filteredItems.Add(_allItems[i]);
                    }
                }
            }

            _thumbnailGrid.SetItems(_filteredItems);
            _navigationImages.Clear();
            _navigationImages.AddRange(_filteredItems.Where(item => !item.IsDirectory));
            ScheduleSessionSave();
            UpdateStatus();
        }

        private void InstallWatcher(string folder)
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            try
            {
                _watcher = new FileSystemWatcher(folder);
                _watcher.IncludeSubdirectories = false;
                _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
                _watcher.Created += WatcherChanged;
                _watcher.Deleted += WatcherChanged;
                _watcher.Changed += WatcherChanged;
                _watcher.Renamed += WatcherRenamed;
                _watcher.EnableRaisingEvents = true;
            }
            catch
            {
            }
        }

        private void WatcherChanged(object sender, FileSystemEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!Object.ReferenceEquals(sender, _watcher)) return;
                if (String.Equals(e.FullPath, _activeTabPath, StringComparison.OrdinalIgnoreCase)) _watcherDirtyImagePath = e.FullPath;
                _rescanTimer.Stop();
                _rescanTimer.Start();
            }));
        }

        private void WatcherRenamed(object sender, RenamedEventArgs e)
        {
            WatcherChanged(sender, e);
        }

        private void OpenImageTab(string path, bool activate)
        {
            if (!File.Exists(path) || !ImageExtensions.IsBrowsableImage(path))
            {
                return;
            }

            string id = _tabOrder.FirstOrDefault(key => !_tabs[key].IsBrowser
                && String.Equals(_tabs[key].Path, path, StringComparison.OrdinalIgnoreCase));
            if (id == null)
            {
                id = _tabs.ContainsKey(path) ? Guid.NewGuid().ToString("N") : path;
                var state = new ImageTabState
                {
                    Path = path,
                    FolderPath = Path.GetDirectoryName(path),
                    SelectedPath = path,
                    Zoom = 1,
                    OffsetX = 0,
                    OffsetY = 0,
                    HasCustomView = false,
                    LastAccessUtc = DateTime.UtcNow
                };
                _tabs[id] = state;
                _tabOrder.Add(id);
            }

            if (activate)
            {
                ActivateImageTab(id);
            }
            else
            {
                RefreshTabStrip();
            }
            ScheduleSessionSave();
        }

        private void OpenBrowserImage(string path)
        {
            if (_activeTabId == null)
            {
                OpenImageTab(path, true);
                return;
            }
            if (!File.Exists(path) || !ImageExtensions.IsBrowsableImage(path)) return;
            SaveWorkspaceState();
            ImageTabState state = CurrentTabState();
            if (!String.Equals(state.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                state.ManualAdjustments = null;
                state.SavedEnhanceRevision = null;
                state.HasCustomView = false;
                state.Zoom = 1;
                state.RotationQuarterTurns = 0;
                state.ViewportWidth = state.ViewportHeight = 0;
            }
            state.Path = path;
            state.SelectedPath = path;
            state.FolderPath = Path.GetDirectoryName(path);
            state.IsBrowser = false;
            DisplayTab();
        }

        private void BrowseActiveTab()
        {
            if (_activeTabId == null) return;
            SaveWorkspaceState();
            ImageTabState state = CurrentTabState();
            state.IsBrowser = true;
            state.FolderPath = Path.GetDirectoryName(state.Path);
            state.SelectedPath = state.Path;
            state.SelectedPaths = new List<string> { state.Path };
            _browserFocusTabId = _activeTabId;
            DisplayTab();
        }

        private void ActivateImageTab(string id)
        {
            if (!_tabs.ContainsKey(id)) return;
            SaveWorkspaceState();
            _activeTabId = id;
            DisplayTab();
        }

        private void DisplayTab()
        {
            _deferredRelativeNavigation = 0;
            if (_folderLocation != null) _folderLocation.CancelEdit();
            ClearAutomaticUpscale();
            HideFilmstrip();
            HideManualEnhance();
            HideImageNavigator();
            ResetZoomIndicator();
            _imageOverlayMetadata = null;
            _imageMetadataOverlay.Visibility = Visibility.Collapsed;
            StopAnimation();
            if (!_slideshowAdvancing) StopSlideshow();
            CancelEnhancement();
            _metadataLoadVersion++;
            EndImagePan();
            ImageTabState state = CurrentTabState();
            SyncSortControls();
            if (!state.IsBrowser || _activeTabId != _browserFocusTabId) _browserFocusTabId = null;
            state.QuickEnhanceEnabled = _quickEnhanceEnabled;
            state.LastAccessUtc = DateTime.UtcNow;
            _activeTabPath = state.IsBrowser ? null : state.Path;
            _displayedImagePath = null;
            _imageLoadVersion++;
            _mainImage.Source = null;
            _imageError.Visibility = Visibility.Collapsed;
            _browserView.Visibility = state.IsBrowser ? Visibility.Visible : Visibility.Collapsed;
            _imageView.Visibility = state.IsBrowser ? Visibility.Collapsed : Visibility.Visible;
            _searchHistoryInput.Commit();
            _searchBox.Text = state.SearchText ?? "";
            _searchHistoryInput.DiscardPending();
            _searchTimer.Stop();
            if (state.IsBrowser) _latestBrowserView = CopyBrowserView(state);
            if (!String.Equals(_currentFolder, state.FolderPath, StringComparison.OrdinalIgnoreCase)
                || _scanCancellation == null)
                ScanFolder(state.FolderPath);
            else
            {
                if (_appliedSortField != state.SortField || _appliedSortDescending != state.SortDescending) SortItems();
                else ApplySearch();
                if (!_folderScanPending) RestoreBrowserPosition();
            }
            RefreshTabStrip();
            RevealActiveTab();
            if (!state.IsBrowser)
            {
                LoadMetadata(state.Path);
                LoadImage(state.Path);
                PreloadAround(state.Path);
            }
            UpdateStatus();
            ScheduleSessionSave();
        }

        private void ShowBrowser()
        {
            SaveWorkspaceState();
            _activeTabId = null;
            DisplayTab();
        }

        private void RefreshTabStrip()
        {
            if (_tabStrip == null)
            {
                return;
            }

            NormalizeTabOrder();
            _tabStrip.Children.Clear();
            if (String.IsNullOrWhiteSpace(_activeTabId))
            {
                ThemeManager.Bind(_browserTabButton, Control.BackgroundProperty, ThemeKeys.SelectedBackground);
            }
            else
            {
                _browserTabButton.ClearValue(Control.BackgroundProperty);
            }

            var shownStacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _tabOrder.Count; i++)
            {
                string path = _tabOrder[i];
                if (!_tabs.ContainsKey(path))
                {
                    continue;
                }

                TabStackDto stack = FindTabStack(_tabs[path].StackId);
                if (stack != null)
                {
                    if (shownStacks.Add(stack.Id)) _tabStrip.Children.Add(CreateStackHeader(stack));
                    if (stack.IsCollapsed) continue;
                }
                _tabStrip.Children.Add(CreateImageTab(path));
            }
            _tabStrip.Children.Add(_newTabButton);
            MeasureTabWidth();
            UpdateTabWidths();
        }

        private UIElement CreateImageTab(string id)
        {
            ImageTabState state = _tabs[id];
            string path = state.Path;
            string label = TabLabel(state);
            bool active = String.Equals(id, _activeTabId, StringComparison.OrdinalIgnoreCase);
            int color = ThemeManager.TabColorIndex(state.StackId ?? id);
            var border = new Border();
            border.Margin = new Thickness(0, 4, 4, 4);
            border.Padding = new Thickness(10, 2, 4, 2);
            border.BorderThickness = new Thickness(1, active ? 4 : 2, 1, 1);
            border.CornerRadius = new CornerRadius(3, 3, 0, 0);
            border.Height = 34;
            border.Width = 208;
            string fill = state.IsPinned ? "Tab.Pinned.Fill" : "Tab.Fill." + color;
            string activeFill = state.IsPinned ? "Tab.Pinned.Active" : "Tab.Active." + color;
            ThemeManager.Bind(border, Border.BorderBrushProperty, state.IsPinned ? "Tab.Pinned.Accent" : "Tab.Accent." + color);
            ThemeManager.Bind(border, Border.BackgroundProperty, active ? activeFill : fill);
            border.Tag = id;
            border.ContextMenu = BuildTabContextMenu(id);
            border.MouseEnter += delegate
            {
                ThemeManager.Bind(border, Border.BackgroundProperty, activeFill);
            };
            border.MouseLeave += delegate
            {
                ThemeManager.Bind(border, Border.BackgroundProperty, active ? activeFill : fill);
            };
            var preview = new TabPreview(state.IsBrowser ? state.FolderPath : path, _services.Thumbnails, state.IsBrowser);
            border.ToolTip = preview;
            border.Unloaded += delegate { preview.IsOpen = false; };
            ToolTipService.SetInitialShowDelay(border, 350);
            ToolTipService.SetBetweenShowDelay(border, 100);
            ToolTipService.SetShowDuration(border, 30000);

            var dock = new DockPanel();
            if (state.IsPinned)
            {
                var pin = new TextBlock
                {
                    Text = "\uE718", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Width = 22, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Pinned tab"
                };
                AutomationProperties.SetName(pin, "Pinned tab");
                ThemeManager.Bind(pin, TextBlock.ForegroundProperty, "Tab.Pinned.Text");
                DockPanel.SetDock(pin, Dock.Left);
                dock.Children.Add(pin);
            }
            else
            {
                var close = new Button();
                close.Content = new TextBlock { Text = "\uE711", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10 };
                CommandPresentation.Describe(close, "Close tab", active ? "Ctrl+W" : null, "Close this tab. Pinned tabs must be unpinned before closing.");
                AutomationProperties.SetName(close, "Close " + label);
                close.Width = 22;
                close.Height = 22;
                close.Margin = new Thickness(8, 0, 0, 0);
                close.Padding = new Thickness(0);
                close.Click += delegate(object sender, RoutedEventArgs e)
                {
                    e.Handled = true;
                    CloseTab(id);
                };
                DockPanel.SetDock(close, Dock.Right);
                dock.Children.Add(close);
            }

            var text = new TextBlock();
            text.Text = label;
            text.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            ThemeManager.Bind(text, TextBlock.ForegroundProperty, state.IsPinned ? "Tab.Pinned.Text" : ThemeKeys.Text);
            dock.Children.Add(text);

            border.Child = dock;
            _tabReorder.Bind(border, id, delegate { ActivateImageTab(id); });
            return border;
        }

        private void CloseTab(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return;
            }

            ImageTabState closing;
            if (!_tabs.TryGetValue(path, out closing) || closing.IsPinned) return;
            SaveWorkspaceState();
            _tabs.Remove(path);
            _tabOrder.Remove(path);
            if (closing.Path != null) _services.VramCache.Remove(closing.Path);

            if (String.Equals(_activeTabId, path, StringComparison.OrdinalIgnoreCase))
            {
                _activeTabId = null;
                _activeTabPath = null;
                _mainImage.Source = null;
                _browserView.Visibility = Visibility.Collapsed;
                if (_tabOrder.Count > 0)
                {
                    ActivateImageTab(_tabOrder[Math.Max(0, _tabOrder.Count - 1)]);
                }
                else
                {
                    ShowBrowser();
                }
            }
            else
            {
                RefreshTabStrip();
            }
            ScheduleSessionSave();
        }

        private void LoadImage(string path)
        {
            HideManualEnhance();
            HideImageNavigator();
            ResetZoomIndicator();
            _imageMetadataOverlay.Visibility = Visibility.Collapsed;
            StopAnimation();
            CancelEnhancement();
            FileRevision revision = FileRevision.Read(path);
            int version = ++_imageLoadVersion;
            _displayedImagePath = null;
            _mainImage.Source = null;
            _mainImage.Visibility = Visibility.Hidden;
            _imageViewPending = true;
            UpdateImageStatusBar(null);
            _imageError.Visibility = Visibility.Collapsed;
            BitmapSource cached;
            if (_services.VramCache.TryGet(path, out cached))
            {
                _displayedImageRevision = revision;
                DisplayImage(path, cached, version);
                return;
            }

            _statusText.Text = "Loading " + Path.GetFileName(path);
            Task.Factory.StartNew(
                delegate
                {
                    return _services.Decoders.Decode(path, 0, CancellationToken.None);
                },
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default).ContinueWith(
                    delegate(Task<BitmapSource> task)
                    {
                        Dispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new Action(
                                delegate
                                {
                                    if (version != _imageLoadVersion)
                                    {
                                        return;
                                    }

                                    if (task.Status == TaskStatus.RanToCompletion)
                                    {
                                        if (!revision.Matches(path)) { LoadImage(path); return; }
                                        _displayedImageRevision = revision;
                                        _services.VramCache.Add(path, task.Result, revision);
                                        DisplayImage(path, task.Result, version);
                                    }
                                    else
                                    {
                                        _mainImage.Source = null;
                                        _imageError.Text = "Image unavailable: " + Path.GetFileName(path);
                                        _imageError.ToolTip = task.Exception == null ? "Image loading was canceled." : task.Exception.GetBaseException().Message;
                                        _imageError.Visibility = Visibility.Visible;
                                        _metadataPanel.ShowMessage("Unable to decode " + Path.GetFileName(path));
                                        ArmSlideshow();
                                    }

                                    UpdateStatus();
                                }));
                    },
                    TaskScheduler.Default);
        }

        private void DisplayImage(string path, BitmapSource bitmap, int version)
        {
            if (version != _imageLoadVersion || !String.Equals(path, _activeTabPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ClearAutomaticUpscale();
            _mainImage.Source = bitmap;
            _mainImage.Width = bitmap.PixelWidth;
            _mainImage.Height = bitmap.PixelHeight;
            UpdateImageRotation();
            _displayedImagePath = path;
            QueueImageView();
            ApplyQuickEnhance();
            StartAnimation(path, version);
            UpdateStatus();
        }

        private void QueueImageView()
        {
            if (_mainImage == null || _mainImage.Source == null || _displayedImagePath != _activeTabPath)
                return;
            int version = _imageLoadVersion;
            _imageViewPending = true;
            HideImageNavigator();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                if (version != _imageLoadVersion || _displayedImagePath != _activeTabPath)
                    return;
                BitmapSource bitmap = _mainImage.Source as BitmapSource;
                ImageTabState state;
                if (bitmap == null || _imageCanvas.ActualWidth <= 1 || _imageCanvas.ActualHeight <= 1
                    || _activeTabId == null || !_tabs.TryGetValue(_activeTabId, out state)) return;
                ImageView view = RestoreZoomView(state);
                SetImageView(view);
                _imageViewPending = false;
                _mainImage.Visibility = Visibility.Visible;
                UpdateZoomIndicator();
                UpdateImageMetadataOverlay();
                UpdateImageNavigator();
                UpdateManualEnhance();
                SaveActiveViewState();
                UpdateEnhanceButton();
                ArmSlideshow();
            }));
        }

        private void ToggleQuickEnhance()
        {
            foreach (ImageTabState tab in _tabs.Values) tab.SavedEnhanceRevision = null;
            _quickEnhanceEnabled = _enhanceButton.IsChecked == true;
            _homeBrowser.QuickEnhanceEnabled = _quickEnhanceEnabled;
            foreach (ImageTabState tab in _tabs.Values) tab.QuickEnhanceEnabled = _quickEnhanceEnabled;
            ApplyQuickEnhance();
            if (!_quickEnhanceEnabled || !_advancedQuickEnhance) ReleaseSubjectSegmentation();
            ScheduleSessionSave();
        }

        private void ReleaseSubjectSegmentation()
        {
            if (_subjectSegmentation != null) _subjectSegmentation.Dispose();
            _subjectSegmentation = null;
        }

        private void CancelEnhancement()
        {
            if (_enhancementRequest != null) _enhancementRequest.Cancel();
            _enhancementRequest = null;
            _enhancementSummary = null;
            if (_mainImage != null) _mainImage.Effect = null;
        }

        private async void ApplyQuickEnhance()
        {
            CancelEnhancement();
            ImageTabState state = CurrentTabState();
            var bitmap = _mainImage.Source as BitmapSource;
            if (!_quickEnhanceEnabled || state.IsBrowser || bitmap == null || HasSavedEnhance(state)) { UpdateEnhanceButton(); return; }
            var request = new CancellationTokenSource();
            _enhancementRequest = request;
            int version = _imageLoadVersion;
            UpdateEnhanceButton();
            EnhancementAnalysis result = null;
            AdvancedEnhancementAnalysis advancedResult = null;
            bool advanced = _advancedQuickEnhance;
            if (advanced && _subjectSegmentation == null) _subjectSegmentation = CreateSubjectDetector();
            SubjectSegmentation detector = _subjectSegmentation;
            Exception failure = null;
            try
            {
                if (advanced)
                    advancedResult = await Task.Run(delegate { return AdvancedQuickEnhance.Analyze(bitmap, _services.CpuWorkerCount, detector, request.Token); }).ConfigureAwait(false);
                else result = await Task.Run(delegate { return QuickEnhance.Analyze(bitmap, _services.CpuWorkerCount, request.Token); }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { failure = ex; }
            if (Dispatcher.HasShutdownStarted) { request.Dispose(); return; }
            try
            {
                await Dispatcher.InvokeAsync(new Action(delegate
                {
                    bool current = !_isClosed && _enhancementRequest == request && !request.IsCancellationRequested
                        && version == _imageLoadVersion && CurrentTabState() == state && _quickEnhanceEnabled
                        && advanced == _advancedQuickEnhance
                        && Object.ReferenceEquals(_mainImage.Source, bitmap);
                    if (_enhancementRequest == request) _enhancementRequest = null;
                    if (!current) return;
                    if (result != null || advancedResult != null)
                    {
                        try
                        {
                            if (advancedResult != null)
                            {
                                _mainImage.Effect = new AdvancedEnhanceEffect(advancedResult, bitmap);
                                _enhancementSummary = "Advanced: " + advancedResult.DetectionSummary
                                    + ". " + advancedResult.Sharpness.Summary
                                    + ". Adaptive shadows, highlights and vibrance; skin-color protection. Click to show original.";
                            }
                            else
                            {
                                _mainImage.Effect = new QuickEnhanceEffect(result, bitmap);
                                _enhancementSummary = String.Format("Exposure {0:+0.00;-0.00;0.00} stops; sharpening {1:0}%. Click to show original.",
                                    result.ExposureStops, result.Sharpness * 100);
                            }
                        }
                        catch (Exception ex) { failure = ex; }
                    }
                    if (failure != null)
                    {
                        _mainImage.Effect = null;
                        _enhancementSummary = "Enhancement unavailable; original image displayed. " + failure.Message;
                        ScheduleSessionSave();
                    }
                    UpdateEnhanceButton();
                }));
            }
            catch (OperationCanceledException) { }
            finally { request.Dispose(); }
        }

        private void UpdateEnhanceButton()
        {
            UpdateGpuPresentation();
            UpdateZoomControls();
            if (_enhanceButton == null) return;
            _enhanceButton.IsEnabled = !_isClosed;
            _enhanceButton.IsChecked = _quickEnhanceEnabled;
            _syncEnhanceMode = true;
            _enhanceMode.SelectedIndex = _advancedQuickEnhance ? 1 : 0;
            _syncEnhanceMode = false;
            string enhanceDescription = _enhancementRequest != null ? (_advancedQuickEnhance
                ? "Analyzing subject softness, background, noise and tone. Click to cancel." : "Analyzing exposure and sharpness. Click to cancel.")
                : (_enhancementSummary ?? (_quickEnhanceEnabled
                    ? "Quick Enhance is on for images in this window. Click to turn off. Originals are unchanged."
                    : "Quick Enhance: automatic exposure and sharpening for images in this window. Originals are unchanged."));
            CommandPresentation.Describe(_enhanceButton, _advancedQuickEnhance ? "Advanced Quick Enhance" : "Quick Enhance", null, enhanceDescription);
            if (_quickEnhanceEnabled && HasCurrentImage() && HasSavedEnhance(CurrentTabState()))
                CommandPresentation.Describe(_enhanceButton, "Quick Enhance", null, "This saved image already contains its enhancement. Toggle Quick Enhance off and on to apply a fresh enhancement.");
            UpdateEnhanceSaveButtons();
        }

        private void SetImageView(ImageView view)
        {
            _imageScale.ScaleX = view.Zoom;
            _imageScale.ScaleY = view.Zoom;
            _imageTranslate.X = view.X;
            _imageTranslate.Y = view.Y;
            UpdateZoomIndicator();
            UpdateImageNavigator();
        }

        private bool HasCurrentImage()
        {
            return !_imageViewPending && _mainImage.Source != null && !String.IsNullOrEmpty(_activeTabPath)
                && String.Equals(_displayedImagePath, _activeTabPath, StringComparison.OrdinalIgnoreCase);
        }

        private Size DisplayedImageSize()
        {
            var bitmap = (BitmapSource)_mainImage.Source;
            return ImageViewport.RotatedSize(new Size(bitmap.PixelWidth, bitmap.PixelHeight),
                CurrentTabState().RotationQuarterTurns);
        }

        private void UpdateImageRotation()
        {
            var bitmap = (BitmapSource)_mainImage.Source;
            _imageRotation.Matrix = ImageViewport.RotationMatrix(new Size(bitmap.PixelWidth, bitmap.PixelHeight),
                CurrentTabState().RotationQuarterTurns);
        }

        private void RotateImage(int quarterTurns)
        {
            if (_rotationSaving || _fileTransferBusy || _backupBusy || !HasCurrentImage() || _imageView.Visibility != Visibility.Visible) return;
            EndImagePan();
            ImageTabState state = CurrentTabState();
            _imageNavigator.EndDrag();
            state.RotationQuarterTurns = ImageViewport.NormalizeRotation(state.RotationQuarterTurns + quarterTurns);
            UpdateImageRotation();
            FitImageAfterContentChange();
            ScheduleSessionSave();
            _rotationStatus.Text = "View-only rotation";
            _rotationStatus.ToolTip = null;
            UpdateImageToolsConfiguration();
            if (_autoSaveRotations) BeginRotationSave(false);
        }

        private void ConstrainImageView()
        {
            BitmapSource bitmap = _mainImage.Source as BitmapSource;
            if (bitmap == null || _imageCanvas.ActualWidth <= 1 || _imageCanvas.ActualHeight <= 1) return;
            SetImageView(ImageViewport.Constrain(DisplayedImageSize(),
                _imageCanvas.RenderSize, _imageScale.ScaleX, _imageTranslate.X, _imageTranslate.Y));
        }

        private void LoadMetadata(string path)
        {
            int version = ++_metadataLoadVersion;
            FileRevision revision = FileRevision.Read(path);
            _imageOverlayMetadata = null;
            UpdateImageMetadataOverlay();
            _metadataPanel.ShowMessage("Loading metadata");
            Task.Factory.StartNew(
                delegate
                {
                    return _services.Decoders.ReadMetadata(path);
                },
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default).ContinueWith(
                    delegate(Task<ImageMetadata> task)
                    {
                        Dispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new Action(
                                delegate
                                {
                                    if (_isClosed || version != _metadataLoadVersion) return;
                                    if (!revision.Equals(FileRevision.Read(path)))
                                    {
                                        LoadMetadata(path);
                                        return;
                                    }
                                    if (task.Status == TaskStatus.RanToCompletion)
                                    {
                                        _metadataPanel.ShowMetadata(task.Result);
                                        _imageOverlayMetadata = task.Result;
                                        _imageOverlayMetadataRevision = revision;
                                    }
                                    else
                                    {
                                        _metadataPanel.ShowMessage("Metadata unavailable");
                                    }
                                    UpdateImageMetadataOverlay();
                                }));
                    },
                    TaskScheduler.Default);
        }

        private void FitImageToView(bool markCustom)
        {
            BitmapSource bitmap = _mainImage.Source as BitmapSource;
            if (bitmap == null || _imageCanvas.ActualWidth <= 1 || _imageCanvas.ActualHeight <= 1)
            {
                return;
            }

            SetImageView(ImageViewport.Fit(DisplayedImageSize(), _imageCanvas.RenderSize));

            if (markCustom && !String.IsNullOrWhiteSpace(_activeTabPath))
            {
                ImageTabState state;
                if (_activeTabId != null && _tabs.TryGetValue(_activeTabId, out state))
                {
                    state.HasCustomView = false;
                }
            }
            RememberLockedZoom();
            SaveActiveViewState();
            ScheduleSessionSave();
        }

        private void SetZoomOneToOne()
        {
            BitmapSource bitmap = _mainImage.Source as BitmapSource;
            if (!HasCurrentImage() || bitmap == null)
            {
                return;
            }

            EndImagePan();
            _imageNavigator.EndDrag();
            SetImageView(ImageViewport.Center(DisplayedImageSize(), _imageCanvas.RenderSize, 1));
            MarkActiveViewCustom();
        }

        private void ImageCanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_imageView.Visibility != Visibility.Visible || String.IsNullOrEmpty(_activeTabPath) || e.Delta == 0)
                return;

            if (e.RightButton == MouseButtonState.Pressed)
            {
                e.Handled = ApplyMouseWheelZoom(e.GetPosition(_imageCanvas), e.Delta, true);
                return;
            }

            NavigateWithFilmstrip(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        }

        private bool ApplyMouseWheelZoom(Point point, int delta, bool rightPressed)
        {
            if (!rightPressed || delta == 0 || !HasCurrentImage()) return false;
            double oldZoom = _imageScale.ScaleX;
            double factor = Math.Pow(1.15, delta / 120.0);
            double newZoom = Math.Max(0.02, Math.Min(64, oldZoom * factor));
            double ratio = newZoom / oldZoom;

            _imageTranslate.X = point.X - ((point.X - _imageTranslate.X) * ratio);
            _imageTranslate.Y = point.Y - ((point.Y - _imageTranslate.Y) * ratio);
            _imageScale.ScaleX = newZoom;
            _imageScale.ScaleY = newZoom;
            MarkActiveViewCustom();
            return true;
        }

        private void ImageCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _imageCanvas.Focus();
            if (e.ClickCount >= 2 && _activeTabId != null)
            {
                BrowseActiveTab();
                e.Handled = true;
                return;
            }

            e.Handled = BeginImagePan(e.GetPosition(_imageCanvas));
        }

        private void ImageCanvasMouseMove(object sender, MouseEventArgs e)
        {
            UpdateImagePan(e.GetPosition(_imageCanvas), e.LeftButton == MouseButtonState.Pressed);
        }

        private void ImageCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndImagePan();
        }

        private bool BeginImagePan(Point point)
        {
            var bitmap = _mainImage.Source as BitmapSource;
            if (!HasCurrentImage() || bitmap == null) return false;
            Size size = DisplayedImageSize();
            if (size.Width * _imageScale.ScaleX <= _imageCanvas.ActualWidth + 0.5
                && size.Height * _imageScale.ScaleY <= _imageCanvas.ActualHeight + 0.5) return false;
            _panStart = point;
            _isPanning = _imageCanvas.CaptureMouse();
            if (_isPanning) Cursor = Cursors.Hand;
            return _isPanning;
        }

        private void UpdateImagePan(Point point, bool leftButtonPressed)
        {
            if (!_isPanning) return;
            if (!leftButtonPressed || !HasCurrentImage()) { EndImagePan(); return; }
            _imageTranslate.X += point.X - _panStart.X;
            _imageTranslate.Y += point.Y - _panStart.Y;
            _panStart = point;
            MarkActiveViewCustom();
        }

        private void EndImagePan()
        {
            _isPanning = false;
            if (_imageCanvas != null && _imageCanvas.IsMouseCaptured) _imageCanvas.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }

        private void MarkActiveViewCustom()
        {
            if (!HasCurrentImage())
            {
                return;
            }

            ImageTabState state;
            if (_activeTabId != null && _tabs.TryGetValue(_activeTabId, out state))
            {
                ConstrainImageView();
                state.HasCustomView = true;
                RememberLockedZoom();
                SaveActiveViewState();
                ScheduleSessionSave();
            }
        }

        private void SaveActiveViewState()
        {
            if (!HasCurrentImage())
            {
                return;
            }

            ImageTabState state;
            if (_activeTabId != null && _tabs.TryGetValue(_activeTabId, out state))
            {
                state.Zoom = _imageScale.ScaleX * _autoUpscaleFactor;
                state.OffsetX = _imageTranslate.X;
                state.OffsetY = _imageTranslate.Y;
                state.ViewportWidth = _imageCanvas.ActualWidth;
                state.ViewportHeight = _imageCanvas.ActualHeight;
            }
        }

        private void PreloadAround(string path)
        {
            int budget = _services.VramCache.BudgetMb;
            int radius = 1;
            if (budget >= 6144)
            {
                radius = 6;
            }
            else if (budget >= 3072)
            {
                radius = 3;
            }
            else if (budget >= 1536)
            {
                radius = 2;
            }

            if (_slideshowPlaying && _slideshowSequence != null)
            {
                foreach (string nearby in _slideshowSequence.Nearby(radius).Distinct(StringComparer.OrdinalIgnoreCase))
                    if (File.Exists(nearby)) PreloadImage(nearby);
                return;
            }
            int index = FindFilteredIndex(path);
            if (index < 0) return;

            for (int offset = -radius; offset <= radius; offset++)
            {
                int target = WrapImageIndex(index + offset, _navigationImages.Count);
                string nearby = _navigationImages[target].Path;
                if (File.Exists(nearby)) PreloadImage(nearby);
            }
        }

        private void PreloadImage(string path)
        {
            BitmapSource existing;
            if (_services.VramCache.TryGet(path, out existing))
            {
                return;
            }

            if (_preloading.Contains(path))
            {
                return;
            }

            _preloading.Add(path);
            int cacheGeneration = _cacheGeneration;
            FileRevision revision = FileRevision.Read(path);
            Task.Factory.StartNew(
                delegate
                {
                    return _services.Decoders.Decode(path, 0, CancellationToken.None);
                },
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default).ContinueWith(
                    delegate(Task<BitmapSource> task)
                    {
                        Dispatcher.BeginInvoke(
                            DispatcherPriority.Background,
                            new Action(
                                delegate
                                {
                                    if (cacheGeneration == _cacheGeneration) _preloading.Remove(path);
                                    if (!_isClosed && cacheGeneration == _cacheGeneration && task.Status == TaskStatus.RanToCompletion && File.Exists(path))
                                    {
                                        _services.VramCache.Add(path, task.Result, revision);
                                        UpdateStatus();
                                    }
                                }));
                    },
                    TaskScheduler.Default);
        }

        private void OpenRelativeImage(int delta)
        {
            if (delta == 0) return;
            if (_folderScanPending)
            {
                if (_imageView.Visibility != Visibility.Visible || String.IsNullOrEmpty(_activeTabPath)) return;
                if (_deferredNavigationTab != _activeTabId || _deferredNavigationFolder != _currentFolder || _deferredNavigationPath != _activeTabPath)
                    _deferredRelativeNavigation = 0;
                _deferredNavigationTab = _activeTabId; _deferredNavigationFolder = _currentFolder; _deferredNavigationPath = _activeTabPath;
                _deferredRelativeNavigation = Math.Max(-100000, Math.Min(100000, _deferredRelativeNavigation + delta));
                return;
            }
            if (_navigationImages.Count == 0) return;
            bool wrap = _imageView.Visibility == Visibility.Visible;
            int step = delta > 0 ? 1 : -1;
            string reference = _activeTabPath;
            if (String.IsNullOrWhiteSpace(reference) && _thumbnailGrid.SelectedItem != null)
            {
                reference = _thumbnailGrid.SelectedItem.Path;
            }

            int index = FindFilteredIndex(reference);
            if (index < 0)
            {
                index = delta > 0 ? 0 : _navigationImages.Count - 1;
            }
            else
            {
                index = wrap ? WrapImageIndex(index + delta, _navigationImages.Count)
                    : Math.Max(0, Math.Min(_navigationImages.Count - 1, index + delta));
            }

            for (int attempt = 0; attempt < _navigationImages.Count; attempt++)
            {
                string path = _navigationImages[index].Path;
                if (File.Exists(path))
                {
                    OpenNavigationImage(path);
                    return;
                }
                index += step;
                if (wrap) index = WrapImageIndex(index, _navigationImages.Count);
                else if (index < 0 || index >= _navigationImages.Count) break;
            }
        }

        private void FlushDeferredImageNavigation()
        {
            int delta = _deferredRelativeNavigation; _deferredRelativeNavigation = 0;
            if (delta != 0 && !_isClosed && _imageView.Visibility == Visibility.Visible && _deferredNavigationTab == _activeTabId
                && _deferredNavigationFolder == _currentFolder && _deferredNavigationPath == _activeTabPath) OpenRelativeImage(delta);
        }

        private static int WrapImageIndex(int index, int count)
        {
            return ((index % count) + count) % count;
        }

        private void OpenBoundaryImage(bool last)
        {
            if (_folderScanPending) return;
            int step = last ? -1 : 1;
            for (int index = last ? _navigationImages.Count - 1 : 0;
                index >= 0 && index < _navigationImages.Count; index += step)
            {
                string path = _navigationImages[index].Path;
                if (!File.Exists(path)) continue;
                OpenNavigationImage(path);
                return;
            }
        }

        private void OpenNavigationImage(string path)
        {
            StopSlideshow();
            if (_imageView.Visibility == Visibility.Visible
                && String.Equals(path, _activeTabPath, StringComparison.OrdinalIgnoreCase)) return;
            OpenBrowserImage(path);
        }

        private int FindFilteredIndex(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return -1;
            }

            for (int i = 0; i < _navigationImages.Count; i++)
            {
                if (String.Equals(_navigationImages[i].Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private ImageFileItem FindItemByPath(string path)
        {
            for (int i = 0; i < _allItems.Count; i++)
            {
                if (String.Equals(_allItems[i].Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    return _allItems[i];
                }
            }

            return null;
        }

        private void ThumbnailCommandRequested(object sender, ThumbnailCommandEventArgs e)
        {
            if (e.Command == "batch-rename") { ShowBatch(BatchKind.Rename); return; }
            if (e.Command == "batch-resize") { ShowBatch(BatchKind.Resize); return; }
            if (e.Command == "batch-convert") { ShowBatch(BatchKind.Convert); return; }
            if (e.Command == "copy" || e.Command == "cut") { CopySelectedFiles(e.Command == "cut"); return; }
            if (e.Command == "paste") { PasteFiles(e.Item != null && e.Item.IsDirectory ? e.Item.Path : _currentFolder); return; }
            if (e.Item == null || _fileTransferBusy) return;
            if (e.Command == "toggle-favorite" && e.Item.IsDirectory)
            {
                _folderNavigation.ToggleFavorite(e.Item.Path);
            }
            else if (e.Command == "open")
            {
                ActivateBrowserItem(e.Item);
            }
            else if (e.Command == "open-new-tab")
            {
                if (e.Item.IsDirectory) OpenFolderTab(e.Item.Path);
                else OpenImageTab(e.Item.Path, true);
            }
            else if (e.Command == "rename")
            {
                RenamePath(e.Item.Path);
            }
            else if (e.Command == "move")
            {
                MoveCurrent();
            }
            else if (e.Command == "delete")
            {
                DeleteCurrent();
            }
        }

        private void RenameCurrent()
        {
            string path = CurrentCommandPath();
            if (path != null)
            {
                RenamePath(path);
            }
        }

        private void MoveCurrent()
        {
            ChooseMoveDestination(SelectedFilePaths());
        }

        private void DeleteCurrent()
        {
            if (_browserView.IsVisible) { DeleteBrowserItems(SelectedFilePaths()); return; }
            string path = CurrentCommandPath();
            if (path != null)
            {
                DeletePath(path);
            }
        }

        private string CurrentCommandPath()
        {
            if (_fileTransferBusy || _rotationSaving || _enhancementSaving || _backupBusy) return null;
            if (_browserView.IsVisible) return _thumbnailGrid.SelectedCount == 1 ? _thumbnailGrid.SelectedItem.Path : null;
            if (!String.IsNullOrWhiteSpace(_activeTabPath))
            {
                return _activeTabPath;
            }

            return null;
        }

        private void RenamePath(string path)
        {
            if (_fileTransferBusy || (!File.Exists(path) && !Directory.Exists(path)))
            {
                return;
            }

            string currentName = Path.GetFileName(path);
            string newName = TextPromptWindow.Prompt(this, "Rename", Directory.Exists(path) ? "New folder name" : "New filename", currentName);
            if (String.IsNullOrWhiteSpace(newName) || String.Equals(currentName, newName, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                RenameConfirmedItem(path, newName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Rename failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MovePath(string path)
        {
            ChooseMoveDestination(new[] { path });
        }

        private void DeletePath(string path)
        {
            if (Directory.Exists(path)) { DeleteBrowserItems(new[] { path }); return; }
            if (!File.Exists(path))
            {
                return;
            }

            StopSlideshow();
            MessageBoxResult result = MessageBox.Show(
                this,
                "Send " + Path.GetFileName(path) + " to the Recycle Bin?",
                "Delete image",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                RecycleConfirmedImage(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ReplacePathReferences(string oldPath, string newPath)
        {
            SaveWorkspaceState();
            foreach (ImageTabState state in _tabs.Values)
            {
                if (String.Equals(state.Path, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    state.Path = newPath;
                    if (!state.IsBrowser) state.FolderPath = Path.GetDirectoryName(newPath);
                }
                if (String.Equals(state.SelectedPath, oldPath, StringComparison.OrdinalIgnoreCase)) state.SelectedPath = newPath;
            }

            _services.VramCache.Remove(oldPath);
            if (String.Equals(_activeTabPath, oldPath, StringComparison.OrdinalIgnoreCase)) DisplayTab();
            RefreshTabStrip();
            ScheduleSessionSave();
        }

        private void MainWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.XButton1) NavigateUp();
            else if (e.ChangedButton == MouseButton.XButton2) NavigateDown();
            else return;
            e.Handled = true;
        }


        private void ZoomFromCenter(double factor)
        {
            if (!HasCurrentImage())
            {
                return;
            }

            Point center = new Point(_imageCanvas.ActualWidth / 2.0, _imageCanvas.ActualHeight / 2.0);
            double oldZoom = _imageScale.ScaleX;
            double newZoom = Math.Max(0.02, Math.Min(64, oldZoom * factor));
            double ratio = newZoom / oldZoom;
            _imageTranslate.X = center.X - ((center.X - _imageTranslate.X) * ratio);
            _imageTranslate.Y = center.Y - ((center.Y - _imageTranslate.Y) * ratio);
            _imageScale.ScaleX = newZoom;
            _imageScale.ScaleY = newZoom;
            MarkActiveViewCustom();
        }

        private void CycleTab()
        {
            if (_tabOrder.Count == 0)
            {
                ShowBrowser();
                return;
            }

            int index = _tabOrder.IndexOf(_activeTabId);
            index = (index + 1) % _tabOrder.Count;
            ActivateImageTab(_tabOrder[index]);
        }

        private void ToggleFullscreen()
        {
            StopSlideshow();
            if (!_isFullscreen)
            {
                RememberWindowPlacement();
                _savedWindowStyle = WindowStyle;
                _savedResizeMode = ResizeMode;
                _savedWindowState = WindowState;
                _isFullscreen = true;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = _savedWindowStyle;
                ResizeMode = _savedResizeMode;
                WindowState = WindowState.Normal;
                Left = _normalWindowBounds.Left;
                Top = _normalWindowBounds.Top;
                Width = _normalWindowBounds.Width;
                Height = _normalWindowBounds.Height;
                WindowState = _savedWindowState;
                _isFullscreen = false;
                RememberWindowPlacement();
            }
            UpdateVerticalHeightControls();
        }

        private void RememberWindowPlacement()
        {
            if (_isClosed || !_restored || _isFullscreen || WindowState == WindowState.Minimized) return;
            ObserveVerticalHeight();
            _lastWindowState = WindowState;
            Rect bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if (!bounds.IsEmpty && ImageViewport.IsFinite(bounds.Left) && ImageViewport.IsFinite(bounds.Top)
                && bounds.Width > 400 && bounds.Height > 300)
                _normalWindowBounds = bounds;
            _placementSaveTimer.Stop();
            _placementSaveTimer.Start();
        }

        private void MainWindowDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void MainWindowDrop(object sender, DragEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null)
            {
                return;
            }

            bool activated = false;
            for (int i = 0; i < paths.Length; i++)
            {
                if (Directory.Exists(paths[i]))
                {
                    LoadFolder(paths[i]);
                }
                else if (File.Exists(paths[i]) && ImageExtensions.IsBrowsableImage(paths[i]))
                {
                    OpenImageTab(paths[i], !activated);
                    activated = true;
                }
            }
        }

        private void RestoreSession()
        {
            ApplySessionState(_services.Sessions.Load(), true);
        }

        private void ApplySessionState(SessionState state, bool restoreWindowPlacement)
        {
            if (state == null)
            {
                state = new SessionState();
            }

            SaveWorkspaceState();
            _browserView.Visibility = Visibility.Collapsed;
            StopSlideshow();
            _slideshowSeconds = state.SlideshowSeconds >= 0.5 && state.SlideshowSeconds <= 3600 ? state.SlideshowSeconds : 3;
            _slideshowInterval.Text = _slideshowSeconds.ToString(System.Globalization.CultureInfo.CurrentCulture);
            _slideshowShuffle.IsChecked = state.SlideshowShuffle;
            RestoreShortcuts(state);
            _tabs.Clear();
            _tabOrder.Clear();
            _activeTabPath = null;
            _activeTabId = null;
            _currentFolder = null;
            if (_scanCancellation != null) _scanCancellation.Cancel();
            _imageLoadVersion++;
            _mainImage.Source = null;

            if (restoreWindowPlacement)
            {
                _activeNamedSessionName = null;
                if (!String.IsNullOrWhiteSpace(state.ActiveNamedSessionName)
                    && _services.Sessions.NamedSessionExists(state.ActiveNamedSessionName))
                {
                    _activeNamedSessionName = state.ActiveNamedSessionName;
                }
            }

            _services.LowPriorityIccEnabled = state.LowPriorityIccEnabled;
            _performanceToggle.IsChecked = state.PerformanceDetailsVisible;
            _batchPresets = (state.BatchPresets ?? new List<BatchPreset>()).Where(BatchPreset.IsValid).Take(32).Select(p => p.Copy()).ToList();
            RestoreGpuSettings(state);
            RestoreImageGpuSettings(state);
            RestoreFolderLayout(state);
            SessionTabDto legacyEnhanced = (state.Tabs ?? new List<SessionTabDto>()).FirstOrDefault(tab => tab != null
                && String.Equals(tab.Id ?? tab.Path, state.ActiveTabId ?? state.ActiveTabPath, StringComparison.OrdinalIgnoreCase));
            _quickEnhanceEnabled = state.QuickEnhanceEnabled ?? (legacyEnhanced != null && legacyEnhanced.QuickEnhanceEnabled);
            _advancedQuickEnhance = state.AdvancedQuickEnhance;
            _autoSaveRotations = state.AutoSaveRotations;
            _confirmEnhanceOverwrite = state.ConfirmEnhanceOverwrite ?? true;
            if (!_quickEnhanceEnabled || !_advancedQuickEnhance) ReleaseSubjectSegmentation();
            _homeBrowser.QuickEnhanceEnabled = _quickEnhanceEnabled;
            _iccCheckBox.IsChecked = state.LowPriorityIccEnabled;
            bool darkTheme = String.IsNullOrWhiteSpace(state.Theme)
                || String.Equals(state.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
            _darkThemeCheckBox.IsChecked = darkTheme;
            ThemeManager.SetDarkTheme(darkTheme);
            SetBackgroundTheme(state.BackgroundPattern, state.BackgroundColor, state.BackgroundFade,
                state.BackgroundIntensity ?? ToolbarBackground.DefaultIntensity);
            SetPinnedTabColor(state.PinnedTabColor);
            _thumbnailSizeTimer.Stop();
            _thumbnailSizePixels = VirtualizedThumbnailGrid.NormalizeThumbnailSize(
                state.ThumbnailSizePixels > 0
                    ? state.ThumbnailSizePixels
                    : VirtualizedThumbnailGrid.DefaultThumbnailSize);
            _thumbnailSizeSlider.Value = _thumbnailSizePixels;
            _thumbnailSizeText.Text = _thumbnailSizePixels.ToString() + " px";
            _thumbnailAspectRatio = VirtualizedThumbnailGrid.NormalizeAspectRatio(state.ThumbnailAspectRatio);
            _thumbnailAspectSlider.Value = _thumbnailAspectRatio;
            _thumbnailAspectText.Text = AspectRatioLabel(_thumbnailAspectRatio);
            _thumbnailSizeTimer.Stop();
            ApplyThumbnailSize();
            SetThumbnailMinimap(state.ThumbnailMinimapVisible, false);
            _metadataPanelWidth = state.MetadataPanelWidth >= 180
                ? state.MetadataPanelWidth
                : 330;
            SetMetadataPanelVisible(state.MetadataPanelVisible);
            SetImageMetadataOverlay(state.ImageMetadataOverlayVisible);
            SetImageNavigator(state.ImageNavigatorEnabled ?? true);
            RestoreZoomSettings(state);
            SetFilmstripSettings(state.WheelFilmstripEnabled ?? true, state.WheelFilmstripPosition);
            _upscaleScale = Array.IndexOf(SuperResolution.Scales, state.SuperResolutionScale) >= 0 ? state.SuperResolutionScale : 1.6;
            SetUpscaleModel(UpscaleModels.IsKnown(state.SuperResolutionModel) ? state.SuperResolutionModel : UpscaleModels.Basic, false);
            SetFilmstripSize(state.WheelFilmstripSize, false);
            _manualEnhanceVisible = state.ManualEnhanceVisible;
            _searchHistory.Restore(state.SearchHistory);
            if (state.VramBudgetMb > 0)
            {
                _vramSlider.Value = state.VramBudgetMb;
                _services.VramCache.SetBudgetMb(state.VramBudgetMb);
            }

            string folder = state.LastFolder;
            if (String.IsNullOrWhiteSpace(folder))
            {
                folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }

            _homeBrowser.FolderPath = folder;
            _homeBrowser.SearchText = state.SearchText ?? "";
            _homeBrowser.BrowserScrollOffset = state.BrowserScrollOffset;
            _homeBrowser.SelectedPath = state.BrowserSelectedPath;
            _homeBrowser.SelectedPaths = new List<string>(state.BrowserSelectedPaths ?? new List<string>());
            _homeBrowser.SortField = BrowserSort.Normalize(state.SortField);
            _homeBrowser.SortDescending = state.SortDescending;
            _homeBrowser.ForwardFolders = new List<string>(state.ForwardFolders ?? new List<string>());
            SessionTabDto latest = state.LatestBrowserView;
            _latestBrowserView = latest == null ? CopyBrowserView(_homeBrowser) : new ImageTabState
            {
                IsBrowser = true, FolderPath = latest.FolderPath, SearchText = latest.SearchText ?? "",
                SelectedPath = latest.SelectedPath, BrowserScrollOffset = latest.BrowserScrollOffset,
                SelectedPaths = new List<string>(latest.SelectedPaths ?? new List<string>()),
                SortField = BrowserSort.Normalize(latest.SortField), SortDescending = latest.SortDescending,
                ForwardFolders = new List<string>(latest.ForwardFolders ?? new List<string>())
            };

            if (state.Tabs != null)
            {
                for (int i = 0; i < state.Tabs.Count; i++)
                {
                    SessionTabDto dto = state.Tabs[i];
                    if (dto != null && ((!String.IsNullOrWhiteSpace(dto.Path) && ImageExtensions.IsBrowsableImage(dto.Path))
                        || (dto.IsBrowser && !String.IsNullOrWhiteSpace(dto.FolderPath))))
                    {
                        string id = String.IsNullOrWhiteSpace(dto.Id) ? dto.Path : dto.Id;
                        if (id == null || _tabs.ContainsKey(id)) id = Guid.NewGuid().ToString("N");

                        var tab = new ImageTabState
                        {
                            Path = dto.Path,
                            IsBrowser = dto.IsBrowser,
                            IsPinned = dto.IsPinned,
                            StackId = dto.StackId,
                            FolderPath = dto.FolderPath ?? Path.GetDirectoryName(dto.Path),
                            SearchText = dto.SearchText ?? "",
                            SelectedPath = dto.SelectedPath ?? dto.Path,
                            SelectedPaths = new List<string>(dto.SelectedPaths ?? new List<string>()),
                            BrowserScrollOffset = dto.BrowserScrollOffset,
                            SortField = BrowserSort.Normalize(dto.SortField),
                            SortDescending = dto.SortDescending,
                            ForwardFolders = new List<string>(dto.ForwardFolders ?? new List<string>()),
                            Zoom = dto.Zoom <= 0 ? 1 : dto.Zoom,
                            RotationQuarterTurns = ImageViewport.NormalizeRotation(dto.RotationQuarterTurns),
                            QuickEnhanceEnabled = _quickEnhanceEnabled,
                            ManualAdjustments = ManualAdjustments.Copy(dto.ManualAdjustments),
                            SavedEnhanceRevision = dto.SavedEnhanceRevision,
                            OffsetX = dto.OffsetX,
                            OffsetY = dto.OffsetY,
                            HasCustomView = dto.HasCustomView,
                            ViewportWidth = dto.ViewportWidth,
                            ViewportHeight = dto.ViewportHeight,
                            LastAccessUtc = DateTime.UtcNow
                        };
                        _tabs[id] = tab;
                        _tabOrder.Add(id);
                    }
                }
            }

            RestoreTabStacks(state);
            string activeId = state.ActiveTabId ?? state.ActiveTabPath;
            _activeTabId = activeId != null && _tabs.ContainsKey(activeId) ? activeId : null;
            DisplayTab();

            if (restoreWindowPlacement)
            {
                UpdateWindowTitle();
            }
        }

        private void SaveSession()
        {
            if (_isClosed || _backupBusy) return;
            _services.Sessions.Save(CaptureSessionState());
            _services.Sessions.SaveBrowserPreferences(_folderNavigation.CapturePreferences());
        }

        private void ScheduleSessionSave()
        {
            if (_isClosed || _backupBusy || !_restored || _placementSaveTimer == null) return;
            _placementSaveTimer.Stop();
            _placementSaveTimer.Start();
        }

        private SessionState CaptureSessionState()
        {
            SaveWorkspaceState();
            NormalizeTabOrder();

            if (_metadataPanel != null && _metadataPanel.Visibility == Visibility.Visible
                && _metadataColumn.ActualWidth >= 180)
            {
                _metadataPanelWidth = _metadataColumn.ActualWidth;
            }

            var state = new SessionState();
            state.TabStacks = _tabStacks.Select(stack => stack.Copy()).ToList();
            CaptureGpuSettings(state);
            state.FolderTreeHidden = _folderTreeHidden;
            state.FolderPaneWidth = FolderPaneWidth();
            state.QuickEnhanceEnabled = _quickEnhanceEnabled;
            state.AdvancedQuickEnhance = _advancedQuickEnhance;
            state.SlideshowSeconds = _slideshowSeconds;
            state.SlideshowShuffle = _slideshowShuffle.IsChecked == true;
            ImageTabState latest = _latestBrowserView ?? _homeBrowser;
            state.LatestBrowserView = new SessionTabDto
            {
                IsBrowser = true, FolderPath = latest.FolderPath, SearchText = latest.SearchText,
                SelectedPath = latest.SelectedPath, BrowserScrollOffset = latest.BrowserScrollOffset,
                SelectedPaths = new List<string>(latest.SelectedPaths),
                SortField = latest.SortField, SortDescending = latest.SortDescending,
                ForwardFolders = new List<string>(latest.ForwardFolders)
            };
            state.LastFolder = _homeBrowser.FolderPath;
            state.SearchText = _homeBrowser.SearchText;
            state.BrowserScrollOffset = _homeBrowser.BrowserScrollOffset;
            state.BrowserSelectedPath = _homeBrowser.SelectedPath;
            state.BrowserSelectedPaths = new List<string>(_homeBrowser.SelectedPaths);
            state.SortField = _homeBrowser.SortField;
            state.SortDescending = _homeBrowser.SortDescending;
            state.ForwardFolders = new List<string>(_homeBrowser.ForwardFolders);
            state.ActiveTabPath = _activeTabPath;
            state.ActiveTabId = _activeTabId;
            state.VramBudgetMb = _services.VramCache.BudgetMb;
            state.PerformanceDetailsVisible = _performanceToggle.IsChecked == true;
            state.BatchPresets = _batchPresets.Select(p => p.Copy()).ToList();
            CaptureImageGpuSettings(state);
            state.LowPriorityIccEnabled = _services.LowPriorityIccEnabled;
            state.Theme = ThemeManager.IsDarkTheme ? "Dark" : "Light";
            state.ThumbnailSizePixels = _thumbnailSizePixels;
            state.ThumbnailAspectRatio = _thumbnailAspectRatio;
            state.ThumbnailMinimapVisible = _minimapVisible;
            state.PinnedTabColor = _pinnedTabColor;
            state.AutoSaveRotations = _autoSaveRotations;
            state.ConfirmEnhanceOverwrite = _confirmEnhanceOverwrite;
            state.MetadataPanelVisible = _metadataPanel != null
                && _metadataPanel.Visibility == Visibility.Visible;
            state.MetadataPanelWidth = _metadataPanelWidth;
            state.ImageMetadataOverlayVisible = _imageMetadataOverlayEnabled;
            state.ImageNavigatorEnabled = _imageNavigatorEnabled;
            state.ZoomLocked = _zoomLocked;
            state.LockedZoom = _lockedZoom;
            state.WheelFilmstripEnabled = _filmstripEnabled;
            state.WheelFilmstripPosition = _filmstripPosition;
            state.WheelFilmstripSize = _filmstripSizePixels;
            state.SuperResolutionScale = _upscaleScale;
            state.SuperResolutionModel = _upscaleModelId;
            state.ManualEnhanceVisible = _manualEnhanceVisible;
            state.SearchHistory = _searchHistory.Capture();
            state.ShortcutOverrides = _shortcuts.Export();
            state.BackgroundPattern = _toolbarBackground.Pattern;
            state.BackgroundColor = _toolbarBackground.ColorHex;
            state.BackgroundFade = _toolbarBackground.Fade;
            state.BackgroundIntensity = _toolbarBackground.Intensity;
            state.ActiveNamedSessionName = _activeNamedSessionName;
            state.WindowMaximized = (_isFullscreen ? _savedWindowState : _lastWindowState) == WindowState.Maximized;
            Rect bounds = _normalWindowBounds;
            if (bounds.Width > 400 && bounds.Height > 300)
            {
                state.WindowLeft = bounds.Left;
                state.WindowTop = bounds.Top;
                state.WindowWidth = bounds.Width;
                state.WindowHeight = bounds.Height;
                state.WindowPositionSaved = true;
            }

            for (int i = 0; i < _tabOrder.Count; i++)
            {
                ImageTabState tab;
                if (_tabs.TryGetValue(_tabOrder[i], out tab))
                {
                    state.Tabs.Add(new SessionTabDto
                    {
                        Id = _tabOrder[i],
                        Path = tab.Path,
                        IsBrowser = tab.IsBrowser,
                        IsPinned = tab.IsPinned,
                        StackId = tab.StackId,
                        FolderPath = tab.FolderPath,
                        SearchText = tab.SearchText,
                        SelectedPath = tab.SelectedPath,
                        SelectedPaths = new List<string>(tab.SelectedPaths),
                        BrowserScrollOffset = tab.BrowserScrollOffset,
                        SortField = tab.SortField,
                        SortDescending = tab.SortDescending,
                        ForwardFolders = new List<string>(tab.ForwardFolders),
                        Zoom = tab.Zoom,
                        RotationQuarterTurns = tab.RotationQuarterTurns,
                        QuickEnhanceEnabled = _quickEnhanceEnabled,
                        ManualAdjustments = ManualAdjustments.Copy(tab.ManualAdjustments),
                        SavedEnhanceRevision = tab.SavedEnhanceRevision,
                        OffsetX = tab.OffsetX,
                        OffsetY = tab.OffsetY,
                        HasCustomView = tab.HasCustomView,
                        ViewportWidth = tab.ViewportWidth,
                        ViewportHeight = tab.ViewportHeight
                    });
                }
            }

            return state;
        }

        private void UpdateStatus()
        {
            if (_statusText == null)
            {
                return;
            }

            long used = _services.VramCache.UsedBytes;
            UpdateManualEnhance();
            UpdateImageNavigator();
            UpdateBrowserFooter();
            UpdateEnhanceButton();
            bool hasFileSelection = CurrentCommandPath() != null;
            UpdateWorkflowCommands();
            _renameButton.IsEnabled = hasFileSelection;
            bool hasItems = !_fileTransferBusy && !_rotationSaving && !_enhancementSaving && !_backupBusy
                && (_browserView.IsVisible ? _thumbnailGrid.SelectedCount > 0 : hasFileSelection);
            _moveButton.IsEnabled = hasItems;
            _deleteButton.IsEnabled = hasItems;
            int budget = _services.VramCache.BudgetMb;
            _vramText.Text = budget.ToString() + " MB";
            _statusText.Text = _filteredItems.Count.ToString() + " shown / "
                + _allItems.Count.ToString() + " items"
                + "    CPU workers " + _services.CpuWorkerCount.ToString()
                + "    decoded cache " + ImageExtensions.FormatBytes(used)
                + " / " + budget.ToString() + " MB"
                + "    " + ImageGpuSummary()
                + "    " + SystemGpuMemoryMonitor.Shared.Snapshot.Summary;
            UpdateConfigurationStatus();
        }
    }
}
