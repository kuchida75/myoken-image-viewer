using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private static void RunResetFilmstripChecks()
    {
        string root = Path.Combine(Root, "reset-filmstrip-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        var paths = new List<string>();
        for (int i = 1; i <= 10; i++) { string path = Path.Combine(photos, "Image" + i + ".png"); MakeImage(path, 960, 640, 96); paths.Add(path); }
        string original = Convert.ToBase64String(File.ReadAllBytes(paths[0]));
        string profile = Path.Combine(root, "Profile");
        var services = AppServices.CreateInstance(profile);
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1500, WindowHeight = 920 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            Invoke(window, "OpenImageTab", paths[3], true); Wait(delegate { return Ready(window, paths[3]); }, "filmstrip image"); WaitScan(window);
            var overlay = Field<Border>(window, "_filmstripOverlay");
            var tiles = Field<Canvas>(window, "_filmstripTiles");
            var sizeSlider = Field<Slider>(window, "_filmstripSizeSlider");
            var canvas = Field<Canvas>(window, "_imageCanvas");
            Assert(!overlay.IsVisible && Field<bool>(window, "_filmstripEnabled"), "legacy sessions default wheel previews on but hidden until navigation");
            Assert(sizeSlider.IsVisible && sizeSlider.Value == MainWindow.DefaultFilmstripSize, "image footer has a larger default preview size");
            canvas.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
            Wait(delegate { return Ready(window, paths[4]) && tiles.Children.Cast<Border>().All(tile => tile.Child is Image && ((Image)tile.Child).Source != null); }, "wheel strip thumbnails");
            Assert(overlay.IsVisible && tiles.Children.Count == 7, "wheel advances image and shows seven previews");
            Assert(Field<List<string>>(window, "_filmstripPaths").SequenceEqual(paths.Skip(1).Take(7)), "three previous/current/three next in natural order");
            canvas.Focus();
            PressKey(window, Key.Left); Wait(delegate { return Ready(window, paths[3]) && overlay.IsVisible; }, "left arrow carousel");
            PressKey(window, Key.Up); Wait(delegate { return Ready(window, paths[2]) && overlay.IsVisible; }, "up arrow carousel");
            PressKey(window, Key.Right); Wait(delegate { return Ready(window, paths[3]) && overlay.IsVisible; }, "right arrow carousel");
            PressKey(window, Key.Down); Wait(delegate { return Ready(window, paths[4]) && overlay.IsVisible; }, "down arrow carousel");
            Assert(Field<List<string>>(window, "_filmstripPaths").SequenceEqual(paths.Skip(1).Take(7)),
                "arrow carousel recenters in the current folder order");
            Assert(((SolidColorBrush)((Border)tiles.Children[3]).BorderBrush).Color.A == 255, "current thumbnail has visible colored border");
            Assert(((SolidColorBrush)overlay.Background).Color.A == 0 && ((SolidColorBrush)overlay.BorderBrush).Color.A == 0
                && overlay.BorderThickness == new Thickness(0), "carousel backdrop and outer border are fully transparent");
            Assert(tiles.Children.Cast<Border>().All(t => ((SolidColorBrush)t.Background).Color.A == 0), "thumbnail backplates are transparent too");
            Assert(overlay.InputHitTest(new Point(1, 1)) != null, "transparent backdrop keeps hover and wheel interaction");
            CheckFilmstripLayout(window);
            Field<DispatcherTimer>(window, "_filmstripTimer").Stop();
            Render(window, "filmstrip-bottom.png");
            for (int i = 0; i < 3; i++)
            {
                var outer = (Border)tiles.Children[i]; var inner = (Border)tiles.Children[i + 1];
                Assert(outer.Opacity < inner.Opacity && outer.Width < inner.Width && outer.Height < inner.Height, "carousel grows more opaque and larger toward center");
                Assert(outer.Opacity == ((Border)tiles.Children[6 - i]).Opacity && outer.Width == ((Border)tiles.Children[6 - i]).Width, "carousel is symmetric");
            }
            sizeSlider.Value = 96; Pump();
            Wait(delegate { return tiles.Children.Cast<Border>().All(t => t.Child is Image && ((Image)t.Child).Source != null); }, "small cache previews");
            double smallWidth = ((Border)tiles.Children[3]).Width;
            sizeSlider.Value = 256;
            Wait(delegate { return !Field<DispatcherTimer>(window, "_filmstripSizeTimer").IsEnabled && tiles.Children.Cast<Border>().All(t => t.Child is Image && ((Image)t.Child).Source != null
                && ((System.Windows.Media.Imaging.BitmapSource)((Image)t.Child).Source).PixelWidth >= 256); }, "larger size-specific cached previews");
            Pump(); CheckFilmstripLayout(window);
            Assert(((Border)tiles.Children[3]).Width > smallWidth && Capture(window).WheelFilmstripSize == 256, "slider grows previews live without changing image");
            Assert(Ready(window, paths[4]), "resizing does not navigate");
            Field<DispatcherTimer>(window, "_filmstripTimer").Stop(); Render(window, "filmstrip-carousel-large.png");
            sizeSlider.Focus(); string beforeSizeKey = Field<string>(window, "_activeTabPath");
            double beforeSizeValue = sizeSlider.Value;
            PressControlKey(sizeSlider, Key.Left); Pump();
            Assert(Field<string>(window, "_activeTabPath") == beforeSizeKey && sizeSlider.Value < beforeSizeValue, "focused size slider keeps arrow keys without navigating images");
            canvas.Focus(); sizeSlider.Value = 256;
            Wait(delegate { return !Field<DispatcherTimer>(window, "_filmstripSizeTimer").IsEnabled; }, "slider debounce finishes");
            ((Border)tiles.Children[5]).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
            Wait(delegate { return Ready(window, paths[6]); }, "clicked neighboring image");
            Assert(Field<List<string>>(window, "_filmstripPaths")[3] == paths[6], "click recenters strip around new image");
            Invoke(window, "OpenNavigationImage", paths[0]);
            Invoke(window, "NavigateWithFilmstrip", -1); Wait(delegate { return Ready(window, paths[9]); }, "wheel wraps backward");
            Assert(Field<List<string>>(window, "_filmstripPaths")[4] == paths[0], "strip previews across end boundary");
            for (int i = 0; i < 18; i++) Invoke(window, "NavigateWithFilmstrip", 1);
            Wait(delegate { return Ready(window, paths[7]); }, "rapid wheel final image");
            Assert(Field<List<string>>(window, "_filmstripPaths")[3] == paths[7], "rapid navigation cancels stale preview updates");

            CheckFilmstripFloatingViewport(window);
            Invoke(window, "SetImageMetadataOverlay", true);
            Invoke(window, "SetManualEnhanceVisible", true);
            foreach (string position in MainWindow.FilmstripPositions)
            {
                Invoke(window, "SetFilmstripSettings", true, position); Invoke(window, "ShowFilmstrip"); Pump();
                Field<DispatcherTimer>(window, "_filmstripTimer").Stop(); CheckFilmstripLayout(window);
                Assert(position == "Bottom center" ? Canvas.GetLeft(tiles.Children[0]) < Canvas.GetLeft(tiles.Children[6])
                    : Canvas.GetTop(tiles.Children[0]) < Canvas.GetTop(tiles.Children[6]), "position controls strip orientation");
                Render(window, "filmstrip-" + position.Replace(' ', '-') + ".png");
            }
            window.Width = 850; window.Height = 720; Pump();
            foreach (string position in MainWindow.FilmstripPositions)
            {
                Invoke(window, "SetFilmstripSettings", true, position); Invoke(window, "ShowFilmstrip"); Pump(); CheckFilmstripLayout(window);
                Field<DispatcherTimer>(window, "_filmstripTimer").Stop();
                Render(window, "filmstrip-small-" + position.Replace(' ', '-') + ".png");
            }
            ThemeManager.SetDarkTheme(false); Invoke(window, "SetCompactMode", true); Pump();
            Invoke(window, "ShowFilmstrip");
            Wait(delegate { return tiles.Children.Count == 7 && tiles.Children.Cast<Border>().All(t => t.Child is Image && ((Image)t.Child).Source != null); }, "light carousel thumbnails");
            Field<DispatcherTimer>(window, "_filmstripTimer").Stop();
            CheckFilmstripLayout(window); Render(window, "filmstrip-light-compact.png");
            Invoke(window, "ToggleFullscreen"); Pump(); Invoke(window, "ShowFilmstrip"); CheckFilmstripLayout(window);
            Invoke(window, "ToggleFullscreen"); Invoke(window, "SetCompactMode", false); ThemeManager.SetDarkTheme(true); Pump();
            Invoke(window, "HideFilmstrip");
            Assert((bool)Invoke(window, "ApplyMouseWheelZoom", new Point(200, 120), 120, true), "right-wheel zoom remains available");
            Assert(!overlay.IsVisible, "right-wheel zoom does not open navigation previews");
            Invoke(window, "SetManualEnhanceVisible", false); Invoke(window, "SetImageMetadataOverlay", false);
            Invoke(window, "ShowFilmstrip");
            if (!overlay.IsMouseOver) Wait(delegate { return !overlay.IsVisible; }, "strip hides after two seconds");
            Invoke(window, "SetFilmstripSettings", false, "Left center"); Invoke(window, "NavigateWithFilmstrip", 1);
            Assert(!overlay.IsVisible, "disabling strip preserves normal navigation");
            canvas.Focus(); PressKey(window, Key.Left); Pump();
            Assert(!overlay.IsVisible, "disabled navigation previews stay hidden for arrow keys");
            Invoke(window, "SetFilmstripSettings", true, "Right center");
            SessionState state = (SessionState)Invoke(window, "CaptureSessionState");
            services.Sessions.Save(state); Assert(services.Sessions.Load().WheelFilmstripPosition == "Right center", "strip preferences persist");
            Assert(services.Sessions.Load().WheelFilmstripSize == 256, "preview size persists in automatic session");
            Invoke(window, "SetFilmstripSize", 96, false); Invoke(window, "ApplySessionState", state, false); WaitScan(window);
            Assert(sizeSlider.Value == 256, "session restore reapplies preview slider size");
            string backup = Path.Combine(root, "backup.json");
            WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
            Assert(ViewerBackupStore.Load(backup).Workspace.WheelFilmstripPosition == "Right center", "strip preference included in backup");
            Assert(ViewerBackupStore.Load(backup).Workspace.WheelFilmstripSize == 256, "preview size included in backup");
            var invalid = ViewerBackupStore.Load(backup); invalid.Workspace.WheelFilmstripSize = 100000;
            string invalidFile = Path.Combine(root, "invalid-size.json"); SessionStore.WriteObject(invalidFile, invalid, typeof(ViewerBackupDocument));
            ExpectBadBackup(invalidFile, "reject invalid preview size");
            Invoke(window, "ShowFilmstrip"); canvas.Focus(); PressControlKey(canvas, Key.Enter);
            Assert(!overlay.IsVisible && tiles.Children.Count == 0, "return to browser releases preview images");
            Assert(!sizeSlider.IsVisible, "preview slider hides in browser mode");

            var history = Field<SearchHistory>(window, "_searchHistory"); history.Record("remember me");
            var navigation = Field<FolderNavigationPane>(window, "_folderNavigation"); navigation.ToggleFavorite(photos);
            services.Sessions.SaveNamed("Before reset", (SessionState)Invoke(window, "CaptureSessionState"));
            string stale = Path.Combine(profile, "instances", "session-17.json");
            SessionStore.WriteObject(stale, state, typeof(SessionState));
            string corrupt = Path.Combine(profile, "sessions", "corrupt.json"); File.WriteAllText(corrupt, "invalid-json");
            string keep = Path.Combine(profile, "thumbs", "keep-marker.jpg"); File.WriteAllText(keep, "generated-cache-marker");
            using (var second = AppServices.CreateInstance(profile))
            {
                bool blocked = false;
                try { WaitBackup((Task)Invoke(window, "ResetViewerAsync", false)); }
                catch (IOException) { blocked = true; }
                Assert(blocked && services.Sessions.NamedSessionExists("Before reset") && File.Exists(keep), "second instance prevents reset without deleting state");
            }
            var reset = (Task<string>)Invoke(window, "ResetViewerAsync", false); WaitBackup(reset); Pump();
            Assert(File.Exists(reset.Result) && ViewerBackupStore.Load(reset.Result).SavedSessions.Any(s => s.Name == "Before reset"), "reset recovery preserves named sessions");
            Assert(File.Exists(Path.Combine(Path.GetDirectoryName(reset.Result), "original-settings", "instances", "session-17.json"))
                && File.Exists(Path.Combine(Path.GetDirectoryName(reset.Result), "original-settings", "sessions", "corrupt.json")), "recovery preserves stale and unreadable settings too");
            Assert(!File.Exists(stale) && !File.Exists(corrupt) && services.Sessions.ListNamedSessions().Count == 0, "reset removes all named and stale automatic sessions");
            SessionState resetState = (SessionState)Invoke(window, "CaptureSessionState");
            Assert(resetState.Tabs.Count == 0 && resetState.SearchHistory.Count == 0 && resetState.Theme == "Dark"
                && resetState.WheelFilmstripEnabled == true && resetState.WheelFilmstripPosition == "Bottom center"
                && resetState.WheelFilmstripSize == MainWindow.DefaultFilmstripSize
                && !resetState.ManualEnhanceVisible && !resetState.ImageMetadataOverlayVisible && resetState.VramBudgetMb == 1024, "reset clears tabs/history and restores defaults");
            Assert(services.Sessions.LoadBrowserPreferences().FavoriteFolders.Count == 0, "reset clears favorites");
            Assert(File.Exists(keep), "reset keeps thumbnail cache by default");
            WaitBackup((Task)Invoke(window, "ClearThumbnailCacheAsync")); Assert(!File.Exists(keep), "standalone cache clearing removes cached files");
            Assert(services.Sessions.Load().Tabs.Count == 0 && File.Exists(reset.Result), "cache clearing preserves current settings and recovery backup");
            WaitBackup((Task)Invoke(window, "ImportBackupFileAsync", reset.Result, false)); WaitScan(window);
            Assert(Capture(window).Tabs.Count > 0 && Capture(window).SearchHistory.Contains("remember me")
                && Capture(window).WheelFilmstripSize == 256
                && services.Sessions.NamedSessionExists("Before reset") && services.Sessions.LoadBrowserPreferences().FavoriteFolders.Contains(photos),
                "recovery import restores tabs, history, named sessions and favorites");
            File.WriteAllText(keep, "optional-reset-cache");
            WaitBackup((Task)Invoke(window, "ResetViewerAsync", true)); Assert(!File.Exists(keep), "optional reset clears cache");
            Assert(Convert.ToBase64String(File.ReadAllBytes(paths[0])) == original && paths.All(File.Exists), "reset/cache operations never modify original photos");
            Invoke(window, "ShowConfigure"); Pump(); Render(Field<Window>(window, "_configureWindow"), "reset-configure.png");
            var confirmationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            bool canceled = false;
            confirmationTimer.Tick += delegate
            {
                var dialog = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.Title == "Reset viewer");
                if (dialog == null) return;
                confirmationTimer.Stop();
                var content = (StackPanel)dialog.Content;
                Assert(!content.Children.OfType<CheckBox>().Single().IsChecked.Value, "reset confirmation defaults to keeping thumbnails");
                dialog.UpdateLayout(); Render(dialog, "reset-confirmation.png");
                canceled = true; dialog.DialogResult = false;
            };
            int beforeCancel = Directory.GetDirectories(Path.Combine(profile, "backups"), "before-reset-*").Length;
            confirmationTimer.Start(); Invoke(window, "ShowResetConfirmation");
            Assert(canceled && Directory.GetDirectories(Path.Combine(profile, "backups"), "before-reset-*").Length == beforeCancel,
                "canceling reset confirmation does not start maintenance");
            Field<Window>(window, "_configureWindow").Close();
        }
        finally { window.Close(); services.Dispose(); Pump(); }

        var cacheServices = AppServices.Create(Path.Combine(root, "Cache-test"));
        var item = ImageFileItem.FromPath(paths[0]);
        var jobs = Enumerable.Range(0, 16).Select(i => cacheServices.Thumbnails.GetThumbnailAsync(item, i % 2 == 0 ? 128 : 256, CancellationToken.None)).ToArray();
        Task.WaitAll(jobs);
        Assert(Directory.GetFiles(Path.Combine(cacheServices.AppDataRoot, "thumbs"), "*.jpg", SearchOption.AllDirectories).Length == 2, "two thumbnail sizes use separate cache entries");
        cacheServices.Thumbnails.Clear();
        Assert(Directory.GetFiles(Path.Combine(cacheServices.AppDataRoot, "thumbs"), "*", SearchOption.AllDirectories).Length == 0, "clearing removes all size buckets");
        cacheServices.Thumbnails.GetThumbnailAsync(item, 128, CancellationToken.None).Wait();
        Assert(Directory.GetFiles(Path.Combine(cacheServices.AppDataRoot, "thumbs"), "*.jpg", SearchOption.AllDirectories).Length == 1, "thumbnail regenerates after clearing");
        var inFlight = Enumerable.Range(0, 12).Select(i => cacheServices.Thumbnails.GetThumbnailAsync(item, 320 + i * 16, CancellationToken.None)).ToArray();
        var clearConcurrent = Task.Run(delegate { cacheServices.Thumbnails.Clear(); });
        Task.WaitAll(inFlight); clearConcurrent.Wait(); cacheServices.Thumbnails.Clear();
        Assert(Directory.GetFiles(Path.Combine(cacheServices.AppDataRoot, "thumbs"), "*", SearchOption.AllDirectories).Length == 0,
            "cache clearing serializes with in-flight writers and leaves no corrupt temporary files");
        bool outside = false; try { ProfileMaintenance.CheckPath(cacheServices.AppDataRoot, photos); } catch (IOException) { outside = true; }
        Assert(outside, "maintenance rejects paths outside its data root");
        cacheServices.Dispose();
        Console.WriteLine("PASS: profile reset/recovery, multi-instance guard, optional/all-size cache clearing, floating wheel previews, unchanged image viewport/zoom/pan/pixels, wraparound, rapid navigation, layouts and persistence.");
    }

    private static void CheckFilmstripFloatingViewport(MainWindow window)
    {
        var view = Field<Grid>(window, "_imageView");
        var canvas = Field<Canvas>(window, "_imageCanvas");
        var image = Field<Image>(window, "_mainImage");
        var strip = Field<Border>(window, "_filmstripOverlay");
        var scale = Field<ScaleTransform>(window, "_imageScale");
        var pan = Field<TranslateTransform>(window, "_imageTranslate");
        var positionControl = Field<ComboBox>(window, "_configureFilmstripPosition");
        foreach (bool zoomed in new[] { false, true })
        {
            Invoke(window, "HideFilmstrip");
            if (zoomed)
            {
                Invoke(window, "SetImageView", new ImageView { Zoom = 2, X = -180, Y = -95 });
                Invoke(window, "MarkActiveViewCustom");
            }
            else Invoke(window, "FitImageToView", true);
            Pump();
            Size frame = canvas.RenderSize;
            Rect imageBounds = image.TransformToAncestor(view).TransformBounds(new Rect(image.RenderSize));
            double zoom = scale.ScaleX; Point offset = new Point(pan.X, pan.Y);
            byte[] pixels = ManualPixels(canvas);
            foreach (string position in MainWindow.FilmstripPositions)
            {
                positionControl.SelectedItem = position;
                foreach (int size in new[] { 96, 256 })
                {
                    Invoke(window, "SetFilmstripSize", size, false); Invoke(window, "ShowFilmstrip"); Pump();
                    Field<DispatcherTimer>(window, "_filmstripTimer").Stop();
                    CheckFilmstripLayout(window);
                    Assert(canvas.RenderSize == frame && canvas.Margin == new Thickness(), "floating strip does not inset or resize image frame");
                    Assert(scale.ScaleX == zoom && pan.X == offset.X && pan.Y == offset.Y, "strip placement/size preserves zoom and pan");
                    Assert(image.TransformToAncestor(view).TransformBounds(new Rect(image.RenderSize)) == imageBounds,
                        "strip placement/size preserves rendered image bounds");
                    Assert(ManualPixels(canvas).SequenceEqual(pixels), "image pixels remain unchanged underneath floating strip");
                    var hit = view.InputHitTest(new Point(view.ActualWidth / 2, 12)) as DependencyObject;
                    while (hit != null && hit != canvas) hit = VisualTreeHelper.GetParent(hit);
                    Assert(hit == canvas, "floating layer passes image input through outside thumbnails");
                }
                if (zoomed && position != "Bottom center")
                {
                    var tiles = Field<Canvas>(window, "_filmstripTiles");
                    Wait(delegate { return tiles.Children.Count == 7 && tiles.Children.Cast<Border>().All(t => ((Image)t.Child).Source != null); }, "floating side thumbnails");
                    Rect bounds = new Rect(strip.TranslatePoint(new Point(), view), strip.RenderSize);
                    Assert(imageBounds.Contains(bounds), "side strip is drawn over zoomed image pixels");
                    Render(window, "filmstrip-floating-" + position.Replace(' ', '-') + ".png");
                }
                Invoke(window, "HideFilmstrip"); Pump();
                Assert(canvas.RenderSize == frame && scale.ScaleX == zoom && pan.X == offset.X && pan.Y == offset.Y,
                    "hiding strip preserves image frame, zoom and pan");
            }
        }
        Invoke(window, "FitImageToView", true); Pump();
    }

    private static void CheckFilmstripLayout(MainWindow window)
    {
        window.UpdateLayout(); Invoke(window, "LayoutImageOverlays"); window.UpdateLayout();
        var view = Field<Grid>(window, "_imageView"); var strip = Field<Border>(window, "_filmstripOverlay");
        Assert(strip.IsVisible && strip.ActualWidth > 50 && strip.ActualHeight > 30, "strip remains usable inside image frame");
        var layer = VisualTreeHelper.GetParent(strip) as Canvas;
        Assert(layer != null && layer.DesiredSize == new Size(), "floating strip contributes no size to workspace layout");
        Assert(layer.RenderSize == view.RenderSize && Field<Canvas>(window, "_imageCanvas").RenderSize == view.RenderSize,
            "overlay and image each retain the full viewer frame");
        Assert(Panel.GetZIndex(layer) > Panel.GetZIndex(Field<Canvas>(window, "_imageCanvas")), "strip renders above the image");
        AssertInside(strip, view);
        var tiles = Field<Canvas>(window, "_filmstripTiles");
        foreach (Border tile in tiles.Children) AssertInside(tile, strip);
        bool vertical = Field<string>(window, "_filmstripPosition") != "Bottom center";
        for (int i = 1; i < tiles.Children.Count; i++)
        {
            var previous = (Border)tiles.Children[i - 1]; var current = (Border)tiles.Children[i];
            double gap = vertical ? Canvas.GetTop(current) - Canvas.GetTop(previous) - previous.Height
                : Canvas.GetLeft(current) - Canvas.GetLeft(previous) - previous.Width;
            Assert(Math.Abs(gap - 2) < 0.01, "compact two-pixel carousel gaps without overlapping frames");
        }
        var pixels = new RenderTargetBitmap((int)strip.ActualWidth, (int)strip.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(strip), null, new Rect(strip.RenderSize));
        pixels.Render(visual);
        var corner = new byte[4];
        pixels.CopyPixels(new Int32Rect(1, 1, 1, 1), corner, 4, 0);
        Assert(corner[3] == 0, "rendered carousel corner has zero opacity, not a painted box");
        Rect box = new Rect(strip.TranslatePoint(new Point(), view), strip.RenderSize);
        foreach (string name in new[] { "_imageMetadataOverlay", "_manualEnhanceOverlay", "_imageNavigatorOverlay", "_slideshowOverlay" })
        {
            var other = Field<Border>(window, name);
            if (other.IsVisible) Assert(!box.IntersectsWith(new Rect(other.TranslatePoint(new Point(), view), other.RenderSize)), "strip clears " + name);
        }
    }
}
