using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static readonly string Root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixture-data");
    private static string Landscape { get { return Path.Combine(Root, "images", "Landscape-300dpi.png"); } }
    private static string Portrait { get { return Path.Combine(Root, "images", "Portrait-72dpi.png"); } }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--storage-worker") return RunStorageWorker(args[1]);
            RunGeometryChecks();
            RunNaturalSortChecks();
            if (args.Contains("--natural-sort-only")) return 0;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            ThemeManager.Initialize(app, true);
            if (args.Contains("--tab-stacks-only")) { RunTabStackChecks(); RunTabWidthChecks(); RunReorderHeightChecks(); RunTreeEnhanceBackupChecks(); app.Shutdown(); return 0; }
            if (args.Length == 0) RunTabStackChecks();
            if (args.Contains("--batch-only")) { RunBatchChecks(); app.Shutdown(); return 0; }
            if (args.Contains("--branding-only"))
            {
                RunBrandingChecks(); RunPinAndInstanceChecks(); RunTreeEnhanceBackupChecks(); app.Shutdown(); return 0;
            }
            if (args.Contains("--workflow-only")) { RunWorkflowChecks(); RunToolbarLayoutChecks(); app.Shutdown(); return 0; }
            if (args.Contains("--workflow-compatibility-only"))
            {
                RunConfigurationChecks(); RunBackgroundThemeChecks(); RunKeyboardLayoutChecks(); RunFolderEnhanceHistoryChecks();
                RunThumbnailNavigationChecks(); RunSlideshowShortcutChecks(); RunMetadataOverlayChecks(); RunZoomControlsChecks();
                RunTreeEnhanceBackupChecks(); app.Shutdown(); return 0;
            }
            if (args.Contains("--gpu-image-device-only")) { RunGpuImageDeviceChecks(); app.Shutdown(); return 0; }
            if (args.Contains("--gpu-compatibility-only"))
            {
                RunSaveNavigationChecks(); RunOverwritePreferenceChecks(); RunEnhanceSaveChecks(); RunUiFormatChecks();
                RunConfigurationChecks(); RunZoomControlsChecks(); RunSuperResolutionChecks(); RunAutomaticUpscaleChecks();
                RunUpscaleAppearanceChecks(); app.Shutdown(); return 0;
            }
            if (args.Contains("--gpu-image-pipeline-only"))
            {
                RunGpuImageDeviceChecks(); RunGpuImageCacheChecks(); RunGpuImageViewerChecks();
                RunManualEnhanceChecks(); RunImageNavigatorChecks(); RunPrintChecks(); RunToolbarLayoutChecks();
                app.Shutdown(); return 0;
            }
            if (args.Contains("--zoom-controls-only")) { RunZoomControlsChecks(); RunZoomIndicatorChecks(); RunToolbarLayoutChecks(); app.Shutdown(); return 0; }
            if (args.Length == 0) RunZoomControlsChecks();
            if (args.Contains("--print-only")) { RunPrintChecks(); RunToolbarLayoutChecks(); app.Shutdown(); return 0; }
            if (args.Length == 0) RunPrintChecks();
            if (args.Contains("--upscale-status-only")) { RunUpscaleAppearanceChecks(); app.Shutdown(); return 0; }
            if (args.Contains("--upscale-models-only")) { RunUpscaleModelChecks(); RunToolbarLayoutChecks(); app.Shutdown(); return 0; }
            if (args.Contains("--upscale-only")) { RunSuperResolutionChecks(); RunAutomaticUpscaleChecks(); RunUpscaleAppearanceChecks(); RunToolbarLayoutChecks(); app.Shutdown(); return 0; }
            if (args.Length == 0) { RunSuperResolutionChecks(); RunAutomaticUpscaleChecks(); RunUpscaleModelChecks(); RunUpscaleAppearanceChecks(); }
            RunKeyboardLayoutChecks();
            if (args.Contains("--keyboard-layout-only")) { app.Shutdown(); return 0; }
            if (args.Contains("--keyboard-shortcuts-only")) { RunSlideshowShortcutChecks(); app.Shutdown(); return 0; }
            RunGpuProcessingChecks();
            if (args.Contains("--gpu-only")) { app.Shutdown(); return 0; }
            RunResetFilmstripChecks();
            if (args.Contains("--reset-filmstrip-only")) { app.Shutdown(); return 0; }
            if (args.Contains("--image-status-sort-only")) { RunMetadataOverlayChecks(); app.Shutdown(); return 0; }
            RunFolderEnhanceHistoryChecks();
            if (args.Contains("--folder-enhance-history-only")) { app.Shutdown(); return 0; }
            RunSaveNavigationChecks();
            if (args.Contains("--save-navigation-only")) { app.Shutdown(); return 0; }
            RunOverwritePreferenceChecks();
            if (args.Contains("--overwrite-setting-only")) { app.Shutdown(); return 0; }
            RunEnhanceSaveChecks();
            if (args.Contains("--enhance-save-only")) { app.Shutdown(); return 0; }
            RunManualEnhanceChecks();
            if (args.Contains("--manual-enhance-only")) { app.Shutdown(); return 0; }
            RunImageNavigatorChecks();
            if (args.Contains("--image-navigator-only")) { app.Shutdown(); return 0; }
            RunToolbarLayoutChecks();
            if (args.Contains("--toolbar-layout-only")) { app.Shutdown(); return 0; }
            RunSlideshowShortcutChecks();
            if (args.Contains("--slideshow-shortcuts-only")) { app.Shutdown(); return 0; }
            RunTabWidthChecks();
            if (args.Contains("--tab-width-only")) { app.Shutdown(); return 0; }
            RunBackgroundThemeChecks();
            if (args.Contains("--background-themes-only")) { app.Shutdown(); return 0; }
            RunMetadataOverlayChecks();
            if (args.Contains("--metadata-overlay-only")) { app.Shutdown(); return 0; }
            RunZoomIndicatorChecks();
            if (args.Contains("--zoom-indicator-only")) { app.Shutdown(); return 0; }
            RunUiFormatChecks();
            if (args.Contains("--ui-formats-only")) { app.Shutdown(); return 0; }
            RunImageToolsChecks();
            if (args.Contains("--image-tools-only")) { app.Shutdown(); return 0; }
            RunReorderHeightChecks();
            if (args.Contains("--reorder-height-only")) { app.Shutdown(); return 0; }
            RunConfigurationChecks();
            if (args.Contains("--configuration-only")) { app.Shutdown(); return 0; }
            RunSubjectSharpeningChecks();
            if (args.Contains("--subject-sharpening-only"))
            {
                app.Shutdown();
                return 0;
            }
            RunMinimapChecks();
            if (args.Contains("--minimap-only"))
            {
                app.Shutdown();
                return 0;
            }
            RunHelpSearchChecks();
            if (args.Contains("--help-search-only"))
            {
                app.Shutdown();
                return 0;
            }
            RunAdvancedEnhancementChecks();
            if (args.Contains("--advanced-enhance-only"))
            {
                app.Shutdown();
                Console.WriteLine("PASS: Advanced regional enhancement, local segmentation, skin-color protection, shader pixels and saved mode.");
                return 0;
            }
            RunThumbnailNavigationChecks();
            if (args.Contains("--thumbnail-navigation-only"))
            {
                app.Shutdown();
                Console.WriteLine("PASS: thumbnail keyboard navigation, active highlight, paging, return-to-browser focus and selection restore.");
                return 0;
            }
            RunTreeEnhanceBackupChecks();
            if (args.Contains("--tree-enhance-backup-only"))
            {
                app.Shutdown();
                Console.WriteLine("PASS: independent folder tree, sticky window-wide Quick Enhance, validated backup round-trip and missing-tab cleanup.");
                return 0;
            }
            RunCompactAndBoundaryChecks();
            if (args.Contains("--compact-navigation-only"))
            {
                app.Shutdown();
                Console.WriteLine("PASS: Home/End, circular image navigation and preloading, compact layout, focus guards and normal-mode startup.");
                return 0;
            }
            RunPinAndInstanceChecks();
            if (args.Contains("--pins-instances-only"))
            {
                app.Shutdown();
                Console.WriteLine("PASS: tab pinning/restore, independent instance sessions, shared-file stress and executable launch/exit/recovery.");
                return 0;
            }
            RunFavoriteAndDeleteChecks();
            if (args.Contains("--favorites-delete-only"))
            {
                app.Shutdown();
                Console.WriteLine("PASS: favorite aliases/descriptions, version metadata, recycle-and-advance, tab retention and session restore.");
                return 0;
            }
            if (args.Contains("--preferences-only"))
            {
                RunNavigationPreferenceChecks();
                app.Shutdown();
                Console.WriteLine("PASS: navigation preferences and restart.");
                return 0;
            }
            RunTransferAndSlideshowChecks();
            if (args.Contains("--transfers-only"))
            {
                app.Shutdown();
                Console.WriteLine("PASS: multi-selection, safe file transfers, Explorer data formats, folder counts, windowed slideshow and session restore.");
                return 0;
            }
            RunForwardAndExitChecks();
            if (args.Contains("--navigation-only"))
            {
                app.Shutdown();
                Console.WriteLine("PASS: folder Forward history, selected-folder entry, small-image fit and Escape navigation.");
                return 0;
            }
            RunEnhancementChecks();
            if (args.Contains("--enhancement-only"))
            {
                app.Shutdown();
                Console.WriteLine("PASS: enhancement analysis/rendering/cancellation, duplicate tabs, saved state and embedded icon.");
                return 0;
            }
            AppServices services = Prepare();
            MainWindow window = new MainWindow(services);
            window.Show();
            Wait(delegate { return Ready(window, Landscape); }, "initial image");
            AssertNear(window.Left, 0, "zero left restored");
            AssertNear(window.Top, 0, "zero top restored");
            CheckVisible(window);
            Invoke(window, "FitImageToView", true);
            Pump();
            CheckCentered(window);
            CheckRenderedPixels(window);

            Invoke(window, "OpenImageTab", Portrait, true);
            Wait(delegate { return Ready(window, Portrait); }, "portrait");
            CheckCentered(window);
            Invoke(window, "SetZoomOneToOne");
            var states = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
            double portraitZoom = states[Portrait].Zoom;
            Invoke(window, "ActivateImageTab", Landscape);
            Invoke(window, "ActivateImageTab", Portrait);
            Invoke(window, "ActivateImageTab", Landscape);
            Wait(delegate { return Ready(window, Landscape); }, "rapid tab switch");
            AssertNear(states[Portrait].Zoom, portraitZoom, "incoming view not overwritten");
            CheckCentered(window);

            window.Width = 1180;
            window.Height = 730;
            Pump();
            CheckCentered(window);
            Invoke(window, "SetMetadataPanelVisible", true);
            Pump();
            CheckCentered(window);
            Invoke(window, "SetMetadataPanelVisible", false);
            Pump();
            CheckCentered(window);

            states[Landscape].HasCustomView = true;
            states[Landscape].Zoom = 1.8;
            states[Landscape].OffsetX = 80000;
            states[Landscape].OffsetY = -90000;
            Invoke(window, "QueueImageView");
            Pump();
            CheckVisible(window);
            Invoke(window, "FitImageToView", true);

            StackPanel tabs = Field<StackPanel>(window, "_tabStrip");
            var tab = (Border)tabs.Children[0];
            var preview = (TabPreview)tab.ToolTip;
            preview.PlacementTarget = tab;
            preview.IsOpen = true;
            Wait(delegate { return FindPreviewImage(preview).Source != null; }, "hover preview");
            Assert(FindPreviewImage(preview).Source.Width > 0, "preview has bitmap");
            Render((FrameworkElement)preview.Content, "hover-preview.png");
            preview.IsOpen = false;
            Wait(delegate { return FindPreviewImage(preview).Source == null; }, "closed preview releases bitmap");
            int paletteCount = 0;
            var palette = new HashSet<int>();
            for (int i = 0; i < 50; i++) palette.Add(ThemeManager.TabColorIndex("image" + i));
            paletteCount = palette.Count;
            Assert(paletteCount == 6, "colored tab palette");
            Brush darkFill = tab.Background;
            ThemeManager.SetDarkTheme(false);
            Pump();
            Assert(!Object.ReferenceEquals(darkFill, tab.Background), "tab theme updates live");
            ThemeManager.SetDarkTheme(true);

            window.Left = 48;
            window.Top = 64;
            window.Width = 1240;
            window.Height = 780;
            Pump();
            SessionState normal = Capture(window);
            Invoke(window, "ToggleFullscreen");
            Pump();
            CheckCentered(window);
            Assert(!Capture(window).WindowMaximized, "fullscreen not persisted as maximized");
            AssertNear(Capture(window).WindowWidth, normal.WindowWidth, "fullscreen preserves normal width");
            Invoke(window, "ToggleFullscreen");
            Pump();
            AssertNear(window.Width, normal.WindowWidth, "exit fullscreen restores width");
            CheckCentered(window);
            window.WindowState = WindowState.Maximized;
            Pump();
            Invoke(window, "ToggleFullscreen");
            Pump();
            Invoke(window, "ToggleFullscreen");
            Pump();
            Assert(window.WindowState == WindowState.Maximized, "fullscreen returns to maximized");
            window.WindowState = WindowState.Minimized;
            Pump();
            SessionState minimized = Capture(window);
            Assert(minimized.WindowMaximized, "minimized remembers previous maximized state");
            AssertNear(minimized.WindowWidth, normal.WindowWidth, "maximized preserves restore width");
            window.Close();
            Pump();

            window = new MainWindow(services);
            window.Show();
            Wait(delegate { return Ready(window, Landscape); }, "restarted image");
            Assert(window.WindowState == WindowState.Maximized, "maximized restart");
            window.WindowState = WindowState.Normal;
            Pump();
            AssertNear(window.Left, normal.WindowLeft, "restart left");
            AssertNear(window.Top, normal.WindowTop, "restart top");
            AssertNear(window.Width, normal.WindowWidth, "restart width");
            AssertNear(window.Height, normal.WindowHeight, "restart height");
            CheckCentered(window);
            Render((FrameworkElement)window.Content, "viewer-regression.png");
            window.Close();
            Pump();
            RunBrowserChecks();
            RunFolderChecks();
            RunNavigationPreferenceChecks();
            RunTabRotationChecks();
            app.Shutdown();
            Console.WriteLine("PASS: rendering, DPI, placement, themes, wheel navigation/zoom, sessions, cache, virtualization, folder previews/watchers, favorites, sorting, pan, mouse Back, shadows, rotation and new browser tabs.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    internal static void ShowPreview()
    {
        var app = new Application();
        ThemeManager.Initialize(app, true);
        AppServices services = Prepare();
        SessionState state = services.Sessions.Load();
        state.Tabs[0].HasCustomView = false;
        state.Tabs.Add(new SessionTabDto { Path = Portrait });
        services.Sessions.Save(state);
        app.Run(new MainWindow(services));
    }

    private static void RunBrowserChecks()
    {
        AppServices services = Prepare();
        var window = new MainWindow(services);
        window.Show();
        Wait(delegate { return Ready(window, Landscape); }, "gesture fixture");
        var canvas = Field<Canvas>(window, "_imageCanvas");
        var scale = Field<ScaleTransform>(window, "_imageScale");
        Point center = new Point(canvas.ActualWidth / 2, canvas.ActualHeight / 2);
        double initialZoom = scale.ScaleX;
        canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
            { RoutedEvent = UIElement.MouseRightButtonDownEvent });
        Assert(Math.Abs(scale.ScaleX - initialZoom) < 0.00001, "right press does not reset view");
        Assert(!(bool)Invoke(window, "ApplyMouseWheelZoom", center, 120, false), "zoom requires right button");
        Assert(Math.Abs(scale.ScaleX - initialZoom) < 0.00001, "ordinary wheel preserves zoom");
        Assert((bool)Invoke(window, "ApplyMouseWheelZoom", center, 120, true), "right wheel handled");
        Assert(Math.Abs(scale.ScaleX - initialZoom * 1.15) < 0.00001, "right wheel zooms in");
        Invoke(window, "ApplyMouseWheelZoom", center, -120, true);
        Assert(Math.Abs(scale.ScaleX - initialZoom) < 0.00001, "right wheel zooms out");

        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "wheel navigation catalog");
        string wheelTabId = Field<string>(window, "_activeTabId");
        var wheelDown = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
            { RoutedEvent = UIElement.MouseWheelEvent };
        canvas.RaiseEvent(wheelDown);
        Wait(delegate { return Ready(window, Portrait); }, "wheel down shows next image");
        Assert(wheelDown.Handled && Field<string>(window, "_activeTabId") == wheelTabId
            && Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count == 1,
            "wheel navigation reuses current tab");
        var wheelUp = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
            { RoutedEvent = UIElement.MouseWheelEvent };
        canvas.RaiseEvent(wheelUp);
        Wait(delegate { return Ready(window, Landscape); }, "wheel up shows previous image");
        Assert(wheelUp.Handled && Field<string>(window, "_activeTabId") == wheelTabId,
            "previous image stays in same tab");

        Invoke(window, "OpenImageTab", Portrait, true);
        Wait(delegate { return Ready(window, Portrait); }, "second image tab");
        Invoke(window, "ActivateImageTab", Landscape);
        Wait(delegate { return Ready(window, Landscape); }, "return to first image");
        var states = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
        string id = Field<string>(window, "_activeTabId");
        var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
        typeof(MouseButtonEventArgs).GetProperty("ClickCount").SetValue(doubleClick, 2, null);
        canvas.RaiseEvent(doubleClick);
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "same-tab folder scan");
        Assert(states.Count == 2 && Field<string>(window, "_activeTabId") == id, "double click retains tab identity/count");
        Assert(states[id].IsBrowser && Field<Grid>(window, "_browserView").Visibility == Visibility.Visible,
            "double click displays browser");
        Assert(Field<Image>(window, "_mainImage").Source == null && !canvas.IsMouseCaptured, "browser releases image and pan");
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        Assert(grid.SelectedItem != null && grid.SelectedItem.Path == Landscape, "browser selects original image");

        Field<Slider>(window, "_thumbnailSizeSlider").Value = 192;
        Field<Slider>(window, "_thumbnailAspectSlider").Value = 0.5;
        Wait(delegate { return grid.ThumbnailSize == 192 && grid.ThumbnailAspectRatio == 0.5; }, "portrait thumbnail frames");
        var realized = Field<Dictionary<int, ThumbnailTile>>(grid, "_realized");
        AssertNear(realized.Values.First().Width, 212, "thumbnail frame width");
        AssertNear(realized.Values.First().Height, 452, "portrait frame height");
        Assert(Field<Image>(realized.Values.First(), "_image").Stretch == Stretch.Uniform, "thumbnail not distorted/cropped");
        var image = ImageFileItem.FromPath(Landscape);
        var firstThumb = services.Thumbnails.GetThumbnailAsync(image, 192, CancellationToken.None).Result;
        string cache = (string)Invoke(services.Thumbnails, "GetCachePath", image, 192);
        Assert(File.Exists(cache), "size-specific cache exists");
        DateTime cacheTime = File.GetLastWriteTimeUtc(cache);
        Field<Slider>(window, "_thumbnailAspectSlider").Value = 2;
        Wait(delegate { return grid.ThumbnailAspectRatio == 2; }, "wide thumbnail frames");
        AssertNear(Field<Dictionary<int, ThumbnailTile>>(grid, "_realized").Values.First().Height, 164, "wide frame height");
        var secondThumb = services.Thumbnails.GetThumbnailAsync(image, 192, CancellationToken.None).Result;
        Assert(firstThumb.PixelWidth == secondThumb.PixelWidth && File.GetLastWriteTimeUtc(cache) == cacheTime,
            "shape reuses uncropped size cache");
        Assert((string)Invoke(services.Thumbnails, "GetCachePath", image, 320) != cache, "larger sizes have separate cache");

        window.Width = 980;
        Invoke(window, "SetMetadataPanelVisible", true);
        Pump();
        FocusViewerSetting(window, Field<Slider>(window, "_thumbnailSizeSlider"));
        AssertInside(Field<Slider>(window, "_thumbnailSizeSlider"), Field<Grid>(window, "_configurationContent"));
        AssertInside(Field<Slider>(window, "_thumbnailAspectSlider"), Field<Grid>(window, "_configurationContent"));
        CloseViewerSettings(window);
        Render((FrameworkElement)window.Content, "browser-narrow.png");
        Invoke(window, "SetMetadataPanelVisible", false);
        window.Width = 1300;
        Pump();
        Render((FrameworkElement)window.Content, "browser-wide.png");

        var virtualItems = Enumerable.Repeat(image, 100000).ToList();
        grid.SetItems(virtualItems);
        grid.ScrollToEnd();
        Pump();
        Assert(Field<Dictionary<int, ThumbnailTile>>(grid, "_realized").Count < 300, "100k records stay virtualized");
        Invoke(window, "RefreshFolder");
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "restore real items");

        Invoke(window, "OpenBrowserImage", Portrait);
        Wait(delegate { return Ready(window, Portrait); }, "browser opens image in same tab");
        Assert(Field<string>(window, "_activeTabId") == id && states.Count == 2 && !states[id].IsBrowser,
            "image replaces browser without adding tab");
        Invoke(window, "BrowseActiveTab");
        Invoke(window, "OpenBrowserImage", Landscape);
        Wait(delegate { return Ready(window, Landscape); }, "same tab returns to landscape");
        Invoke(window, "RefreshFolder");
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "watcher-style refresh");
        Assert(Ready(window, Landscape) && !states[id].IsBrowser, "folder refresh preserves displayed image");

        string otherFolder = Path.Combine(Root, "other-folder");
        Directory.CreateDirectory(otherFolder);
        string otherImage = Path.Combine(otherFolder, "Other.png");
        File.Copy(Portrait, otherImage, true);
        for (int i = 0; i < 24; i++) File.Copy(Portrait, Path.Combine(otherFolder, "Other-" + i + ".png"), true);
        Invoke(window, "BrowseActiveTab");
        Invoke(window, "LoadFolder", Path.GetDirectoryName(Landscape));
        Invoke(window, "LoadFolder", otherFolder);
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "rapid folder switching");
        Assert(Field<List<ImageFileItem>>(window, "_allItems").All(item => Path.GetDirectoryName(item.Path) == otherFolder),
            "stale scan batches excluded");
        Field<TextBox>(window, "_searchBox").Text = "Other";
        Pump(); Pump(); Pump();
        grid.ScrollToVerticalOffset(350);
        grid.SelectItem(ImageFileItem.FromPath(otherImage));
        Pump();
        double scroll = grid.VerticalOffset;
        Assert(scroll > 0, "browser has scrollable fixture");
        Invoke(window, "ActivateImageTab", Portrait);
        Wait(delegate { return Ready(window, Portrait); }, "independent image tab");
        Assert(Field<TextBox>(window, "_searchBox").Text == "", "independent filter");
        Invoke(window, "ActivateImageTab", id);
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "browser tab reactivated");
        Assert(Field<string>(window, "_currentFolder") == otherFolder && Field<TextBox>(window, "_searchBox").Text == "Other",
            "browser remembers folder and filter");
        AssertNear(grid.VerticalOffset, scroll, "browser scroll retained");
        Assert(grid.SelectedItem != null && grid.SelectedItem.Path == otherImage, "browser selection retained");

        SessionState snapshot = Capture(window);
        snapshot.Tabs.Add(new SessionTabDto { Id = "missing-file", Path = Path.Combine(Root, "missing.png") });
        snapshot.Tabs.Add(new SessionTabDto { Id = "missing-folder", IsBrowser = true, FolderPath = Path.Combine(Root, "missing-folder") });
        for (int i = 0; i < 8; i++) snapshot.Tabs.Add(new SessionTabDto { Id = "extra-" + i, Path = Landscape });
        services.Sessions.SaveNamed("Browser regression", snapshot);
        SessionState named = services.Sessions.LoadNamed("Browser regression");
        Assert(named.Tabs.Count == 12 && named.Tabs[0].IsBrowser && named.ActiveTabId == id, "named snapshot retains all tab modes");
        Invoke(window, "ApplySessionState", named, false);
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "mixed named session");
        window.Close();
        Pump();
        window = new MainWindow(services);
        window.Show();
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "mixed restart");
        states = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
        Assert(states.Count == 12 && Field<List<string>>(window, "_tabOrder").SequenceEqual(named.Tabs.Select(tab => tab.Id)),
            "restart restores every tab in order including unavailable files");
        Assert(Field<string>(window, "_activeTabId") == id && states[id].IsBrowser, "restart restores active browser tab");
        grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        Assert(grid.ThumbnailSize == 192 && grid.ThumbnailAspectRatio == 2, "both sliders restored");
        AssertNear(grid.VerticalOffset, scroll, "restart restores browser scroll");
        Assert(grid.SelectedItem != null && grid.SelectedItem.Path == otherImage, "restart restores browser selection");
        Invoke(window, "ActivateImageTab", "missing-file");
        Wait(delegate { return Field<TextBlock>(window, "_imageError").Visibility == Visibility.Visible; }, "unavailable image placeholder");
        Assert(states.Count == 12, "missing image stays open");
        Invoke(window, "ActivateImageTab", "missing-folder");
        Pump();
        Assert(Field<TextBlock>(window, "_folderTitle").Text.StartsWith("Folder unavailable:"), "missing folder visible");
        Invoke(window, "ActivateImageTab", Portrait);
        Wait(delegate { return Ready(window, Portrait); }, "restored image works");
        Invoke(window, "ActivateImageTab", id);
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "return to browser");
        Invoke(window, "CloseTab", id);
        Assert(states.Count == 11 && !states.ContainsKey(id), "browser tab can close");
        window.Close();
        Pump();
    }

    private static void RunFolderChecks()
    {
        string gallery = Path.Combine(Root, "folder-browser");
        string album = Path.Combine(gallery, "Z-Album");
        string partial = Path.Combine(gallery, "M-Two Photos");
        string empty = Path.Combine(gallery, "A-No Direct Images");
        Directory.CreateDirectory(album);
        Directory.CreateDirectory(partial);
        Directory.CreateDirectory(empty);
        string nested = Path.Combine(empty, "Nested");
        Directory.CreateDirectory(nested);
        File.Copy(Landscape, Path.Combine(nested, "Nested.png"), true);
        File.WriteAllBytes(Path.Combine(album, "00-corrupt.jpg"), new byte[] { 1, 2, 3 });
        for (int i = 0; i < 4; i++) MakeImage(Path.Combine(album, "Photo-" + i + ".png"), 600 + i * 80, 400 + i * 120, 96);
        File.Copy(Landscape, Path.Combine(partial, "Landscape.png"), true);
        File.Copy(Portrait, Path.Combine(partial, "Portrait.png"), true);
        string first = Path.Combine(gallery, "01-Landscape.png");
        string second = Path.Combine(gallery, "02-Portrait.png");
        File.Copy(Landscape, first, true);
        File.Copy(Portrait, second, true);
        AppServices services = AppServices.Create(Path.Combine(Root, "folder-profile"));
        services.Sessions.Save(new SessionState { LastFolder = gallery, ThumbnailSizePixels = 192,
            ThumbnailAspectRatio = 1, WindowWidth = 1300, WindowHeight = 820 });
        var window = new MainWindow(services);
        window.Show();
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "mixed folder catalog");
        var items = Field<List<ImageFileItem>>(window, "_allItems");
        Assert(items.Count == 5 && items.Take(3).All(item => item.IsDirectory)
            && items.Skip(3).All(item => !item.IsDirectory), "folders sort ahead of images");
        Assert(items[0].Path == empty && items[1].Path == partial && items[2].Path == album, "folder name ordering");
        Wait(delegate { return FolderReady(window, album, 4); }, "four embedded folder previews");
        Wait(delegate { return FolderReady(window, partial, 2); }, "two-image folder leaves spare slots");
        Wait(delegate { return FolderReady(window, empty, 0); }, "preview does not recurse into child directories");
        FolderPreview albumPreview = Field<FolderPreview>(FolderTile(window, album), "_folderPreview");
        Assert(Field<Image[]>(albumPreview, "_images").All(preview => preview.Stretch == Stretch.Uniform),
            "folder thumbnails preserve image proportions");
        Assert(FolderTile(window, album).ContextMenu.Items.OfType<MenuItem>().Any(item => Object.Equals(item.Tag, "toggle-favorite")),
            "folder menu offers pin commands alongside file transfers");
        var cacheItem = ImageFileItem.FromPath(Path.Combine(album, "Photo-0.png"));
        string cachedPath = (string)Invoke(services.Thumbnails, "GetCachePath", cacheItem, 96);
        Assert(File.Exists(cachedPath), "folder previews use persistent size cache");
        Render((FrameworkElement)window.Content, "folder-browser-dark.png");
        ThemeManager.SetDarkTheme(false);
        Pump();
        Render((FrameworkElement)window.Content, "folder-browser-light.png");
        ThemeManager.SetDarkTheme(true);

        Field<Slider>(window, "_thumbnailSizeSlider").Value = 96;
        Field<Slider>(window, "_thumbnailAspectSlider").Value = 2;
        Wait(delegate { return Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid").ThumbnailSize == 96
            && FolderReady(window, album, 4); }, "small wide folder previews");
        albumPreview = Field<FolderPreview>(FolderTile(window, album), "_folderPreview");
        foreach (Image preview in Field<Image[]>(albumPreview, "_images")) AssertInside(preview, albumPreview);
        Render((FrameworkElement)window.Content, "folder-browser-small.png");
        Field<Slider>(window, "_thumbnailSizeSlider").Value = 192;
        Field<Slider>(window, "_thumbnailAspectSlider").Value = 0.5;
        Wait(delegate { return Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid").ThumbnailSize == 192
            && FolderReady(window, album, 4); }, "tall folder previews");
        Render((FrameworkElement)window.Content, "folder-browser-tall.png");
        Field<Slider>(window, "_thumbnailAspectSlider").Value = 1;
        Wait(delegate { return Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid").ThumbnailAspectRatio == 1
            && FolderReady(window, album, 4); }, "restore square previews");

        MakeImage(Path.Combine(album, "Photo-0.png"), 300, 1000, 96);
        Wait(delegate
        {
            ThumbnailTile tile = FolderTile(window, album);
            if (tile == null) return false;
            return Field<Image[]>(Field<FolderPreview>(tile, "_folderPreview"), "_images")
                .Any(preview => preview.Source is BitmapSource && ((BitmapSource)preview.Source).PixelHeight > ((BitmapSource)preview.Source).PixelWidth * 3
                    && ((BitmapSource)preview.Source).PixelHeight <= 96);
        }, "preview refreshes after image overwrite");
        File.Delete(Path.Combine(album, "Photo-0.png"));
        Wait(delegate { return FolderReady(window, album, 3); }, "preview refreshes after image deletion");
        MakeImage(Path.Combine(album, "Photo-0.png"), 600, 400, 96);
        Wait(delegate { return FolderReady(window, album, 4); }, "preview refreshes after image creation");

        string created = Path.Combine(gallery, "New-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(created);
        Wait(delegate { return items.Any(item => item.Path == created); }, "watcher discovers new folder");
        string renamed = created + "-renamed";
        Directory.Move(created, renamed);
        Wait(delegate { return items.Any(item => item.Path == renamed) && items.All(item => item.Path != created); }, "watcher follows folder rename");
        Directory.Delete(renamed);
        Wait(delegate { return items.All(item => item.Path != renamed); }, "watcher removes deleted folder");

        Invoke(window, "OpenFolderTab", gallery);
        Wait(delegate { return FolderReady(window, album, 4); }, "folder tab content");
        string tabId = Field<string>(window, "_activeTabId");
        var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
        typeof(MouseButtonEventArgs).GetProperty("ClickCount").SetValue(doubleClick, 2, null);
        FolderTile(window, album).RaiseEvent(doubleClick);
        Wait(delegate { return Field<string>(window, "_currentFolder") == album && !Field<bool>(window, "_folderScanPending"); },
            "folder tile double click");
        Assert(Field<string>(window, "_activeTabId") == tabId && Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count == 1,
            "double click opens folder in same tab");
        var header = (Border)Field<StackPanel>(window, "_tabStrip").Children[0];
        var tooltip = (TabPreview)header.ToolTip;
        tooltip.PlacementTarget = header;
        tooltip.IsOpen = true;
        var hoverFolder = Field<FolderPreview>(tooltip, "_folderPreview");
        Wait(delegate { return Field<Image[]>(hoverFolder, "_images").All(image => image.Source != null); },
            "folder tab hover shows four previews");
        Render((FrameworkElement)tooltip.Content, "folder-tab-preview.png");
        tooltip.IsOpen = false;
        Wait(delegate { return Field<FileSystemWatcher>(hoverFolder, "_watcher") == null; }, "folder hover releases watcher");
        Invoke(window, "NavigateUp");
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "parent folder navigation");
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        Assert(Field<string>(window, "_currentFolder") == gallery && grid.SelectedItem != null && grid.SelectedItem.Path == album,
            "parent folder restores child selection");
        Wait(delegate { return FolderReady(window, album, 4); }, "parent previews restored");
        albumPreview = Field<FolderPreview>(FolderTile(window, album), "_folderPreview");
        Invoke(window, "OpenBrowserImage", first);
        Wait(delegate { return Ready(window, first); }, "image inside mixed directory");
        Assert(Field<FileSystemWatcher>(albumPreview, "_watcher") == null
            && Field<Image[]>(albumPreview, "_images").All(preview => preview.Source == null), "hidden folder tiles release previews/watchers");
        Assert(Field<List<ImageFileItem>>(window, "_navigationImages").Count == 2, "image preload sequence excludes directories");
        var canvas = Field<Canvas>(window, "_imageCanvas");
        canvas.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent });
        Wait(delegate { return Ready(window, second); }, "wheel next skips folders");
        canvas.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = UIElement.MouseWheelEvent });
        Wait(delegate { return Ready(window, first); }, "wheel previous skips folders");
        Invoke(window, "BrowseActiveTab");
        Field<TextBox>(window, "_searchBox").Text = "Z-Album";
        Wait(delegate { return Field<List<ImageFileItem>>(window, "_filteredItems").Count == 1; }, "folder name search");
        Assert(Field<List<ImageFileItem>>(window, "_filteredItems")[0].IsDirectory, "search includes folders");
        Invoke(window, "OpenRelativeImage", 1);
        Assert(Field<Grid>(window, "_browserView").Visibility == Visibility.Visible, "image navigation ignores folder-only results");
        Field<TextBox>(window, "_searchBox").Text = "";
        Pump(); Pump(); Pump();
        Invoke(window, "OpenFolderTab", partial);
        string folderTabId = Field<string>(window, "_activeTabId");
        Assert(Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count == 2, "folder can open in another tab");
        window.Close();
        Pump();
        window = new MainWindow(services);
        window.Show();
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "folder session restart");
        Assert(Field<string>(window, "_activeTabId") == folderTabId && Field<string>(window, "_currentFolder") == partial,
            "folder-only tab restores on restart");
        Assert(Field<Dictionary<string, ImageTabState>>(window, "_tabs").Count == 2, "mixed browser tabs retained");
        window.Close();
        Pump();
    }

    private static void RunNavigationPreferenceChecks()
    {
        string root = Path.Combine(Root, "navigation-controls");
        string pinned = Path.Combine(root, "Pinned");
        string branch = Path.Combine(pinned, "Branch");
        string leaf = Path.Combine(branch, "Leaf");
        string other = Path.Combine(root, "Other");
        Directory.CreateDirectory(leaf);
        Directory.CreateDirectory(other);
        string[] names = { "image1.png", "image2.png", "image10.png" };
        int[] created = { 3, 1, 2 }, modified = { 2, 3, 1 };
        DateTime baseline = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < names.Length; i++)
        {
            string path = Path.Combine(pinned, names[i]);
            MakeImage(path, 1200 + i * 100, 700 + i * 80, 96);
            File.SetCreationTimeUtc(path, baseline.AddDays(created[i]));
            File.SetLastWriteTimeUtc(path, baseline.AddDays(modified[i]));
        }
        AppServices services = AppServices.Create(Path.Combine(Root, "navigation-profile"));
        services.Sessions.SaveBrowserPreferences(new BrowserPreferences());
        services.Sessions.Save(new SessionState { LastFolder = pinned, WindowWidth = 1300, WindowHeight = 820 });
        var window = new MainWindow(services);
        window.Show();
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "navigation fixture");
        var pane = Field<FolderNavigationPane>(window, "_folderNavigation");
        Field<ToggleButton>(window, "_favoriteButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert(pane.IsFavorite(pinned) && pane.IsFavorite(pinned + "\\"), "pin button and normalized favorite path");
        pane.ToggleFavorite(other);
        var favorites = Field<TreeViewItem>(pane, "_favoritesNode");
        Assert(favorites.Items.Count == 2, "two pinned shortcuts");
        var pinnedNode = (TreeViewItem)favorites.Items[0];
        pinnedNode.IsExpanded = true;
        Wait(delegate { return TreeChild(pinnedNode, "Branch") != null; }, "load favorite branch");
        TreeViewItem branchNode = TreeChild(pinnedNode, "Branch");
        branchNode.IsExpanded = true;
        Wait(delegate { return TreeChild(branchNode, "Leaf") != null; }, "load nested branch");
        pinnedNode.IsExpanded = false;
        Assert(branchNode.IsExpanded, "collapsed parent retains descendant expansion");
        pinnedNode.IsExpanded = true;
        TreeViewItem otherNode = (TreeViewItem)favorites.Items[1];
        otherNode.IsSelected = true;
        Wait(delegate { return Field<string>(window, "_currentFolder") == other && !Field<bool>(window, "_folderScanPending"); },
            "favorite shortcut navigation");
        pinnedNode.IsSelected = true;
        Wait(delegate { return Field<string>(window, "_currentFolder") == pinned && !Field<bool>(window, "_folderScanPending"); },
            "return through favorite");

        var box = Field<ComboBox>(window, "_sortFieldBox");
        var direction = Field<ToggleButton>(window, "_sortDirectionButton");
        AssertOrder(window, "image1.png", "image2.png", "image10.png");
        Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid").SelectItem(
            Field<List<ImageFileItem>>(window, "_allItems").First(item => item.Name == "image2.png"));
        box.SelectedIndex = (int)BrowserSortField.Number;
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "number sort");
        AssertOrder(window, "image1.png", "image2.png", "image10.png");
        Assert(Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid").SelectedItem.Name == "image2.png", "sorting preserves selection");
        direction.IsChecked = true;
        direction.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "descending sort");
        AssertOrder(window, "image10.png", "image2.png", "image1.png");
        Assert(Field<List<ImageFileItem>>(window, "_filteredItems")[0].IsDirectory, "folders remain first when descending");
        direction.IsChecked = false;
        direction.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        box.SelectedIndex = (int)BrowserSortField.Created;
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "creation date sort");
        AssertOrder(window, "image2.png", "image10.png", "image1.png");
        box.SelectedIndex = (int)BrowserSortField.Modified;
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "modified date sort");
        AssertOrder(window, "image10.png", "image1.png", "image2.png");
        box.SelectedIndex = (int)BrowserSortField.Size;
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "size sort");
        AssertOrder(window, names.OrderBy(name => new FileInfo(Path.Combine(pinned, name)).Length).ToArray());
        box.SelectedIndex = (int)BrowserSortField.Type;
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "type sort with name ties");
        AssertOrder(window, "image1.png", "image2.png", "image10.png");
        box.SelectedIndex = (int)BrowserSortField.Number;
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "home number sort");
        Field<TextBox>(window, "_searchBox").Text = "image1";
        Wait(delegate { return Field<List<ImageFileItem>>(window, "_filteredItems").Count == 2; }, "sorted filename search");
        AssertOrder(window, "image1.png", "image10.png");
        Field<TextBox>(window, "_searchBox").Text = "";
        Pump(); Pump(); Pump();
        window.Width = 980;
        Invoke(window, "SetMetadataPanelVisible", true);
        Pump();
        AssertInside(box, Field<Grid>(window, "_browserView"));
        AssertInside(direction, Field<Grid>(window, "_browserView"));
        Render((FrameworkElement)window.Content, "sort-favorites-narrow.png");
        window.Width = 1500;
        Invoke(window, "SetMetadataPanelVisible", false);
        Pump();
        Render((FrameworkElement)window.Content, "sort-favorites-dark.png");
        box.IsDropDownOpen = true;
        Pump();
        var popup = (Popup)box.Template.FindName("PART_Popup", box);
        Assert(popup.IsOpen && ((FrameworkElement)popup.Child).ActualWidth >= box.ActualWidth - 1, "sort popup has stable width");
        Render((FrameworkElement)popup.Child, "sort-dropdown-dark.png");
        box.IsDropDownOpen = false;
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = false;
        Pump();
        Render((FrameworkElement)window.Content, "sort-favorites-light.png");
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = true;

        Invoke(window, "OpenFolderTab", pinned);
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "independent sorted tab");
        string tabId = Field<string>(window, "_activeTabId");
        box.SelectedIndex = (int)BrowserSortField.Modified;
        direction.IsChecked = true;
        direction.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "tab modified descending sort");
        AssertOrder(window, "image2.png", "image1.png", "image10.png");
        Invoke(window, "ShowBrowser");
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "home sort returns");
        Assert(box.SelectedIndex == (int)BrowserSortField.Number && direction.IsChecked == false, "sort stored per tab");
        Invoke(window, "ActivateImageTab", tabId);
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "image tab order returns");
        string firstImage = Path.Combine(pinned, "image1.png");
        Invoke(window, "OpenBrowserImage", firstImage);
        Wait(delegate { return Ready(window, firstImage); }, "sorted viewer image");
        var canvas = Field<Canvas>(window, "_imageCanvas");
        canvas.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent });
        Wait(delegate { return Ready(window, Path.Combine(pinned, "image10.png")); }, "wheel follows metadata sort");
        Invoke(window, "OpenBrowserImage", firstImage);
        Wait(delegate { return Ready(window, firstImage); }, "pan fixture");
        Invoke(window, "FitImageToView", true);
        Assert(!(bool)Invoke(window, "BeginImagePan", new Point(100, 100)), "fitted image does not pan");
        Invoke(window, "ZoomFromCenter", 3.0);
        var translate = Field<TranslateTransform>(window, "_imageTranslate");
        double x = translate.X, y = translate.Y;
        Assert((bool)Invoke(window, "BeginImagePan", new Point(100, 100)), "oversized image captures drag");
        Invoke(window, "UpdateImagePan", new Point(130, 140), true);
        AssertNear(translate.X, x + 30, "left drag horizontal movement");
        AssertNear(translate.Y, y + 40, "left drag vertical movement");
        Invoke(window, "UpdateImagePan", new Point(20000, 20000), true);
        AssertNear(translate.X, 0, "pan clamps at left edge");
        AssertNear(translate.Y, 0, "pan clamps at top edge");
        Invoke(window, "UpdateImagePan", new Point(19990, 19980), true);
        AssertNear(translate.X, -10, "drag reverses immediately after clamp");
        AssertNear(translate.Y, -20, "vertical drag reverses immediately");
        Invoke(window, "UpdateImagePan", new Point(19900, 19900), false);
        Assert(!canvas.IsMouseCaptured && !Field<bool>(window, "_isPanning"), "button release ends drag");
        AssertNear(translate.X, -10, "released button cannot move image");
        Assert((bool)Invoke(window, "BeginImagePan", new Point(100, 100)), "second drag starts");
        canvas.ReleaseMouseCapture();
        Assert(!Field<bool>(window, "_isPanning"), "lost capture cancels drag");
        SessionState snapshot = Capture(window);
        services.Sessions.SaveNamed("Sorted workspace", snapshot);
        Assert(services.Sessions.LoadNamed("Sorted workspace").Tabs[0].SortDescending, "named workspace preserves sort direction");
        Invoke(window, "ApplySessionState", services.Sessions.LoadNamed("Sorted workspace"), false);
        Wait(delegate { return Ready(window, firstImage); }, "named sorted workspace restored");
        Assert(pane.IsFavorite(pinned) && pane.IsFavorite(other), "favorites survive named workspace changes");
        window.Close();
        Pump();

        window = new MainWindow(services);
        window.Show();
        try { Wait(delegate { return Ready(window, firstImage) && !Field<bool>(window, "_folderScanPending"); }, "preferences restart"); }
        catch
        {
            Console.WriteLine("Restart state: active=" + Field<string>(window, "_activeTabPath")
                + ", displayed=" + Field<string>(window, "_displayedImagePath")
                + ", scan=" + Field<bool>(window, "_folderScanPending")
                + ", image pending=" + Field<bool>(window, "_imageViewPending")
                + ", error=" + Field<TextBlock>(window, "_imageError").Text);
            throw;
        }
        pane = Field<FolderNavigationPane>(window, "_folderNavigation");
        favorites = Field<TreeViewItem>(pane, "_favoritesNode");
        Assert(favorites.Items.Count == 2 && pane.IsFavorite(pinned) && pane.IsFavorite(other), "favorites restored");
        pinnedNode = (TreeViewItem)favorites.Items[0];
        Wait(delegate { return TreeChild(pinnedNode, "Branch") != null; }, "expanded favorite restored");
        branchNode = TreeChild(pinnedNode, "Branch");
        Wait(delegate { return TreeChild(branchNode, "Leaf") != null; }, "nested expanded branch restored");
        Assert(pinnedNode.IsExpanded && branchNode.IsExpanded, "expansion states restored without navigation");
        Assert(Field<string>(window, "_currentFolder") == pinned && Field<string>(window, "_activeTabId") == tabId,
            "tree restoration does not change active tab");
        Assert(Field<ComboBox>(window, "_sortFieldBox").SelectedIndex == (int)BrowserSortField.Modified
            && Field<ToggleButton>(window, "_sortDirectionButton").IsChecked == true, "sort restored on restart");
        AssertNear(Field<TranslateTransform>(window, "_imageTranslate").X, -10, "pan offset restored");
        branchNode.IsExpanded = false;
        pane.ToggleFavorite(other);
        window.Close();
        Pump();
        BrowserPreferences saved = services.Sessions.LoadBrowserPreferences();
        Assert(saved.FavoriteFolders.SequenceEqual(new[] { pinned }), "unpin persisted");
        Assert(!saved.ExpandedNodes.Any(key => key.EndsWith("|" + branch, StringComparison.OrdinalIgnoreCase)), "collapsed branch persisted");
    }

    private static void RunTabRotationChecks()
    {
        string folder = Path.Combine(Root, "tab-rotation");
        string child = Path.Combine(folder, "Album");
        Directory.CreateDirectory(child);
        string imagePath = Path.Combine(folder, "Sample.png");
        MakeImage(imagePath, 1200, 700, 300);
        MakeImage(Path.Combine(child, "Preview.png"), 600, 1000, 96);
        for (int i = 0; i < 45; i++) MakeImage(Path.Combine(folder, "Image" + i + ".png"), 240, 180, 96);
        byte[] original = File.ReadAllBytes(imagePath);
        AppServices services = AppServices.Create(Path.Combine(Root, "tab-rotation-profile"));
        services.Sessions.Save(new SessionState { LastFolder = folder, WindowWidth = 1240, WindowHeight = 780 });
        var window = new MainWindow(services);
        window.Show();
        WaitScan(window);
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        var plus = Field<Button>(window, "_newTabButton");
        var strip = Field<StackPanel>(window, "_tabStrip");
        Assert(strip.Children.Count == 1 && strip.Children[0] == plus, "plus exists with no image tabs");
        Wait(delegate { return FolderReady(window, child, 1); }, "shadow folder preview");
        var tile = FolderTile(window, child);
        var shadow = Field<Border>(tile, "_previewFrame").Effect as System.Windows.Media.Effects.DropShadowEffect;
        Assert(shadow != null && shadow.IsFrozen && shadow.ShadowDepth > 0, "folder preview has reusable shadow");
        Assert(Field<Dictionary<int, ThumbnailTile>>(grid, "_realized").Values.Where(t => !t.Item.IsDirectory)
            .All(t => Field<Border>(t, "_previewFrame").Effect == shadow), "files share the same frozen preview shadow");
        Render((FrameworkElement)window.Content, "new-tab-shadows-dark.png");
        ThemeManager.SetDarkTheme(false);
        Pump();
        CheckPreviewShadow(tile);
        Render((FrameworkElement)window.Content, "new-tab-shadows-light.png");
        ThemeManager.SetDarkTheme(true);

        Invoke(window, "LoadFolder", child);
        WaitScan(window);
        var mouseBack = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.XButton1)
            { RoutedEvent = Mouse.PreviewMouseDownEvent };
        Field<Grid>(window, "_browserView").RaiseEvent(mouseBack);
        WaitScan(window);
        Assert(mouseBack.Handled && Field<string>(window, "_currentFolder") == folder
            && grid.SelectedItem.Path == child, "mouse Back opens parent and selects child");
        Invoke(window, "LoadFolder", child);
        WaitScan(window);
        PressKey(window, Key.Back);
        WaitScan(window);
        Assert(Field<string>(window, "_currentFolder") == folder, "keyboard Backspace still opens parent");
        Invoke(window, "LoadFolder", child);
        WaitScan(window);
        var search = Field<TextBox>(window, "_searchBox");
        search.Focus();
        PressKey(window, Key.Back);
        Assert(Field<string>(window, "_currentFolder") == child, "Backspace in search does not navigate");
        PressKey(window, Key.BrowserBack);
        WaitScan(window);
        Assert(Field<string>(window, "_currentFolder") == folder, "Browser Back key navigates even with search focus");
        Keyboard.ClearFocus();

        Field<ComboBox>(window, "_sortFieldBox").SelectedIndex = (int)BrowserSortField.Number;
        Field<ToggleButton>(window, "_sortDirectionButton").IsChecked = true;
        Field<ToggleButton>(window, "_sortDirectionButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        WaitScan(window);
        search.Text = "Image";
        Invoke(window, "ApplySearch");
        grid.ScrollToVerticalOffset(350);
        Pump();
        double scroll = grid.VerticalOffset;
        Assert(scroll > 0, "new tab clone fixture scrolls");
        plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitScan(window);
        var states = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
        string browserId = Field<string>(window, "_activeTabId");
        Assert(states[browserId].IsBrowser && states[browserId].FolderPath == folder
            && search.Text == "Image" && states[browserId].SortDescending
            && states[browserId].SortField == BrowserSortField.Number, "plus clones browser fields");
        AssertNear(grid.VerticalOffset, scroll, "plus clones browser scroll");
        Invoke(window, "OpenImageTab", imagePath, true);
        Wait(delegate { return Ready(window, imagePath); }, "rotation fixture");
        WaitScan(window);
        Keyboard.ClearFocus();
        BitmapSource source = (BitmapSource)Field<Image>(window, "_mainImage").Source;
        PressKey(window, Key.Oem4);
        Pump();
        Assert(states[imagePath].RotationQuarterTurns == 3, "left bracket turns counterclockwise");
        CheckCentered(window);
        CheckRotationPixels(window, 3);
        PressKey(window, Key.Oem6);
        Pump();
        Assert(states[imagePath].RotationQuarterTurns == 0, "right bracket undoes left rotation");
        for (int turn = 1; turn <= 4; turn++)
        {
            PressKey(window, Key.Oem6);
            Pump();
            CheckCentered(window);
            CheckRotationPixels(window, turn % 4);
        }
        Assert(states[imagePath].RotationQuarterTurns == 0, "four rotations wrap to original");
        PressKey(window, Key.Oem6);
        Pump();
        Assert(Object.ReferenceEquals(source, Field<Image>(window, "_mainImage").Source), "rotation reuses decoded bitmap");
        search.Focus();
        PressKey(window, Key.Oem6);
        Assert(states[imagePath].RotationQuarterTurns == 1, "brackets in search do not rotate");
        Keyboard.ClearFocus();
        window.Width = 980;
        window.Height = 640;
        Invoke(window, "SetMetadataPanelVisible", true);
        Pump();
        CheckCentered(window);
        Render((FrameworkElement)window.Content, "rotation-narrow.png");
        Invoke(window, "SetZoomOneToOne");
        var canvas = Field<Canvas>(window, "_imageCanvas");
        var translate = Field<TranslateTransform>(window, "_imageTranslate");
        Assert((bool)Invoke(window, "BeginImagePan", new Point(100, 100)), "rotated image can pan");
        Invoke(window, "UpdateImagePan", new Point(-9000, -9000), true);
        AssertNear(translate.X, canvas.ActualWidth - 700, "rotated horizontal pan limit uses swapped width");
        AssertNear(translate.Y, canvas.ActualHeight - 1200, "rotated vertical pan limit uses swapped height");
        Invoke(window, "EndImagePan");
        Invoke(window, "ApplyMouseWheelZoom", new Point(100, 100), 120, true);
        CheckVisible(window);
        SessionState saved = Capture(window);
        services.Sessions.SaveNamed("Rotated view", saved);
        Assert(services.Sessions.LoadNamed("Rotated view").Tabs.Single(t => t.Id == imagePath).RotationQuarterTurns == 1,
            "named sessions persist rotation");
        window.Close();
        Pump();
        window = new MainWindow(services);
        window.Show();
        Wait(delegate { return Ready(window, imagePath); }, "rotated session restart");
        states = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
        Assert(states[imagePath].RotationQuarterTurns == 1, "rotation restored");
        AssertNear(Field<TranslateTransform>(window, "_imageTranslate").Y,
            saved.Tabs.Single(t => t.Id == imagePath).OffsetY, "rotated pan restored");
        Invoke(window, "FitImageToView", true);
        Pump();
        CheckCentered(window);
        CheckRotationPixels(window, 1);
        plus = Field<Button>(window, "_newTabButton");
        plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitScan(window);
        string cloneId = Field<string>(window, "_activeTabId");
        Assert(states[cloneId].IsBrowser && states[cloneId].SearchText == "Image"
            && states[cloneId].SortField == BrowserSortField.Number && states[cloneId].SortDescending,
            "plus from restored image uses latest actual browser, not image defaults");
        Assert(states[cloneId] != states[browserId], "new tab state is independent");
        Invoke(window, "OpenBrowserImage", imagePath);
        Wait(delegate { return Ready(window, imagePath); }, "independent image tab");
        Assert(states[cloneId].RotationQuarterTurns == 0, "same file in new tab starts unrotated");
        Invoke(window, "RotateImage", -1);
        Assert(states[imagePath].RotationQuarterTurns == 1, "rotation remains independent per tab");
        Invoke(window, "OpenBrowserImage", Path.Combine(folder, "Image1.png"));
        Wait(delegate { return Ready(window, Path.Combine(folder, "Image1.png")); }, "next image resets rotation");
        Assert(states[cloneId].RotationQuarterTurns == 0, "rotation resets when navigating to another image");
        for (int i = 0; i < 8; i++) plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitScan(window);
        Pump();
        strip = Field<StackPanel>(window, "_tabStrip");
        var scroller = (ScrollViewer)strip.Parent;
        Assert(strip.Children[strip.Children.Count - 1] == plus, "plus follows final tab");
        Rect plusBounds = plus.TransformToAncestor(scroller).TransformBounds(new Rect(plus.RenderSize));
        Assert(plusBounds.Left >= 0 && plusBounds.Right <= scroller.ActualWidth + 1, "overflow scroll keeps plus reachable");
        PressKey(window, Key.Oem6);
        Assert(states[Field<string>(window, "_activeTabId")].RotationQuarterTurns == 0, "browser mode ignores rotation key");
        Render((FrameworkElement)window.Content, "new-tab-overflow.png");
        Assert(File.ReadAllBytes(imagePath).SequenceEqual(original), "rotation never changes source file");
        window.Close();
        Pump();
    }

    private static void PressKey(Window window, Key key)
    {
        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    private static void WaitScan(MainWindow window)
    {
        Wait(delegate { return !Field<bool>(window, "_folderScanPending"); }, "folder scan and sort");
    }

    private static void CheckRotationPixels(MainWindow window, int turns)
    {
        var canvas = Field<Canvas>(window, "_imageCanvas");
        var image = Field<Image>(window, "_mainImage");
        Rect bounds = image.TransformToAncestor(canvas).TransformBounds(new Rect(image.RenderSize));
        double ratio = turns % 2 == 0 ? 1200.0 / 700 : 700.0 / 1200;
        Assert(Math.Abs(bounds.Width / bounds.Height - ratio) < 0.001, "rotated image aspect ratio");
        var bitmap = new RenderTargetBitmap((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)(bounds.Left + bounds.Width * 0.25),
            (int)(bounds.Top + bounds.Height * 0.25), 1, 1), pixel, 4, 0);
        int[] red = { 95, 95, 165, 165 };
        int[] green = { 95, 165, 165, 95 };
        Assert(Math.Abs(pixel[2] - red[turns]) < 5 && Math.Abs(pixel[1] - green[turns]) < 5,
            "rendered pixels confirm rotation direction " + turns);
    }

    private static void CheckPreviewShadow(ThumbnailTile tile)
    {
        Border host = Field<Border>(tile, "_previewFrame");
        Rect bounds = host.TransformToAncestor(tile).TransformBounds(new Rect(host.RenderSize));
        int x = (int)(bounds.Left + bounds.Width / 2), y = (int)bounds.Bottom + 2;
        var withShadow = new RenderTargetBitmap((int)tile.ActualWidth, (int)tile.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        withShadow.Render(tile);
        var effect = host.Effect;
        host.Effect = null;
        Pump();
        var withoutShadow = new RenderTargetBitmap((int)tile.ActualWidth, (int)tile.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        withoutShadow.Render(tile);
        host.Effect = effect;
        Pump();
        var before = new byte[4];
        var after = new byte[4];
        withShadow.CopyPixels(new Int32Rect(x, y, 1, 1), before, 4, 0);
        withoutShadow.CopyPixels(new Int32Rect(x, y, 1, 1), after, 4, 0);
        Assert(before[0] < after[0] - 5, "shadow pixels are visible beneath preview: " + before[0] + " / " + after[0]);
    }

    private static TreeViewItem TreeChild(TreeViewItem parent, string label)
    {
        return parent.Items.OfType<TreeViewItem>().FirstOrDefault(item => Object.Equals(item.Header, label));
    }

    private static void AssertOrder(MainWindow window, params string[] names)
    {
        Assert(Field<List<ImageFileItem>>(window, "_navigationImages").Select(item => item.Name).SequenceEqual(names),
            "image sort order: " + String.Join(", ", names));
    }

    private static ThumbnailTile FolderTile(MainWindow window, string path)
    {
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        return Field<Dictionary<int, ThumbnailTile>>(grid, "_realized").Values.FirstOrDefault(tile => tile.Item.Path == path);
    }

    private static bool FolderReady(MainWindow window, string path, int count)
    {
        ThumbnailTile tile = FolderTile(window, path);
        if (tile == null) return false;
        FolderPreview preview = Field<FolderPreview>(tile, "_folderPreview");
        return preview != null && preview.IsLoaded && Field<CancellationTokenSource>(preview, "_request") == null
            && Field<Image[]>(preview, "_images").Count(image => image.Source != null) == count;
    }

    private static void AssertInside(FrameworkElement element, FrameworkElement frame)
    {
        Rect bounds = element.TransformToAncestor(frame).TransformBounds(new Rect(element.RenderSize));
        Assert(bounds.Left >= 0 && bounds.Right <= frame.ActualWidth + 1 && bounds.Bottom <= frame.ActualHeight + 1,
            "thumbnail control fits narrow browser");
    }

    private static AppServices Prepare()
    {
        Directory.CreateDirectory(Path.Combine(Root, "images"));
        MakeImage(Landscape, 1200, 700, 300);
        MakeImage(Portrait, 600, 1000, 72);
        AppServices services = AppServices.Create(Path.Combine(Root, "profile"));
        var state = new SessionState
        {
            LastFolder = Path.GetDirectoryName(Landscape), ActiveTabPath = Landscape,
            WindowWidth = 1300, WindowHeight = 820, WindowPositionSaved = true,
            WindowLeft = 0, WindowTop = 0
        };
        state.Tabs.Add(new SessionTabDto
        {
            Path = Landscape, Zoom = 0.5, HasCustomView = true,
            OffsetX = 99999, OffsetY = -99999, ViewportWidth = 1900, ViewportHeight = 1000
        });
        services.Sessions.Save(state);
        return services;
    }

    private static void RunGeometryChecks()
    {
        ImageView fit = ImageViewport.Fit(new Size(4000, 2000), new Size(1000, 800));
        AssertNear(fit.Zoom, 0.25, "fit scale");
        AssertNear(fit.X, 0, "fit x");
        AssertNear(fit.Y, 150, "fit y");
        ImageView constrained = ImageViewport.Constrain(new Size(1000, 800), new Size(500, 400), 2, 9999, -9999);
        AssertNear(constrained.X, 0, "pan right limit");
        AssertNear(constrained.Y, -1200, "pan bottom limit");
        var areas = new[] { new Rect(0, 0, 1920, 1040), new Rect(-1920, -1080, 1920, 1040) };
        Rect negative = new Rect(-1800, -1000, 1000, 700);
        Assert(WindowPlacement.Constrain(negative, areas) == negative, "negative monitor position preserved");
        Rect missing = WindowPlacement.Constrain(new Rect(9000, 9000, 1200, 800), areas);
        Assert(areas[0].Contains(missing), "missing monitor fallback");
    }

    private static void MakeImage(string path, int width, int height, double dpi)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * stride + x * 4;
            bool border = x < 12 || y < 12 || x > width - 13 || y > height - 13;
            bool cross = Math.Abs(x - width / 2) < 5 || Math.Abs(y - height / 2) < 5;
            pixels[index] = (byte)(border || cross ? 245 : 110);
            pixels[index + 1] = (byte)(border || cross ? 245 : (60 + y * 140 / height));
            pixels[index + 2] = (byte)(border || cross ? 245 : (60 + x * 140 / width));
            pixels[index + 3] = 255;
        }
        BitmapSource bitmap = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
    }

    private static bool Ready(MainWindow window, string path)
    {
        return Field<string>(window, "_displayedImagePath") == path
            && !Field<bool>(window, "_imageViewPending") && Field<Image>(window, "_mainImage").Source != null;
    }

    private static void CheckVisible(MainWindow window)
    {
        var canvas = Field<Canvas>(window, "_imageCanvas");
        var image = Field<Image>(window, "_mainImage");
        Rect bounds = image.TransformToAncestor(canvas).TransformBounds(new Rect(image.RenderSize));
        Assert(bounds.IntersectsWith(new Rect(canvas.RenderSize)), "image intersects frame");
        Assert(image.Visibility == Visibility.Visible, "image visible");
    }

    private static void CheckCentered(MainWindow window)
    {
        CheckVisible(window);
        var canvas = Field<Canvas>(window, "_imageCanvas");
        var image = Field<Image>(window, "_mainImage");
        Rect bounds = image.TransformToAncestor(canvas).TransformBounds(new Rect(image.RenderSize));
        AssertNear(bounds.Left + bounds.Width / 2, canvas.ActualWidth / 2, "image center x");
        AssertNear(bounds.Top + bounds.Height / 2, canvas.ActualHeight / 2, "image center y");
        Assert(bounds.Width <= canvas.ActualWidth + 1 && bounds.Height <= canvas.ActualHeight + 1, "fitted inside frame");
    }

    private static void CheckRenderedPixels(MainWindow window)
    {
        var canvas = Field<Canvas>(window, "_imageCanvas");
        var image = Field<Image>(window, "_mainImage");
        Rect bounds = image.TransformToAncestor(canvas).TransformBounds(new Rect(image.RenderSize));
        var bitmap = new RenderTargetBitmap((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        int stride = bitmap.PixelWidth * 4;
        byte[] bytes = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(bytes, stride, 0);
        int x = (int)(bounds.Left + bounds.Width * 0.2);
        int y = (int)(bounds.Top + bounds.Height * 0.2);
        int offset = y * stride + x * 4;
        Assert(bytes[offset] > 70 && bytes[offset + 1] > 70, "300-DPI image fills pixel frame");
    }

    private static Image FindPreviewImage(TabPreview preview)
    {
        return (Image)((Grid)((StackPanel)preview.Content).Children[0]).Children[0];
    }

    private static void Render(FrameworkElement element, string filename)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(Root, filename))) encoder.Save(stream);
    }

    private static SessionState Capture(MainWindow window) { return (SessionState)Invoke(window, "CaptureSessionState"); }
    private static object Invoke(object instance, string name, params object[] args)
    {
        return instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, args);
    }
    private static T Field<T>(object instance, string name)
    {
        return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    }
    private static void AssertNear(double value, double expected, string message)
    {
        Assert(Math.Abs(value - expected) < 1.5, message + ": expected " + expected + ", got " + value);
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private static void Wait(Func<bool> condition, string operation)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < limit) Pump();
        Assert(condition(), "Timed out: " + operation);
        Pump();
    }
}

internal static class ViewerPreviewEntry
{
    [STAThread]
    private static void Main() { ViewerRegressionTests.ShowPreview(); }
}
