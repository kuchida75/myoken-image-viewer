using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void AssertZoom(MainWindow window, double expected, string message)
    {
        double actual = Field<ScaleTransform>(window, "_imageScale").ScaleX;
        Assert(Math.Abs(actual - expected) < 0.000001, message + ": expected " + expected + ", got " + actual);
        Assert(Math.Abs(Field<ScaleTransform>(window, "_imageScale").ScaleY - actual) < 0.000001, "zoom preserves proportions");
    }

    private static void ClickZoom(MainWindow window, string name)
    {
        var button = Field<ButtonBase>(window, name);
        Assert(button.IsEnabled, name + " enabled for loaded image");
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Pump();
    }

    private static void CheckZoomCentered(MainWindow window)
    {
        CheckVisible(window);
        var canvas = Field<Canvas>(window, "_imageCanvas");
        var image = Field<Image>(window, "_mainImage");
        Rect bounds = image.TransformToAncestor(canvas).TransformBounds(new Rect(image.RenderSize));
        AssertNear(bounds.Left + bounds.Width / 2, canvas.ActualWidth / 2, "zoomed image center x");
        AssertNear(bounds.Top + bounds.Height / 2, canvas.ActualHeight / 2, "zoomed image center y");
    }

    private static void RunZoomControlsChecks()
    {
        foreach (int rotation in new[] { 0, 1, 2, 3 })
        {
            Size image = ImageViewport.RotatedSize(new Size(2000, 1200), rotation), frame = new Size(800, 600);
            ImageView width = ImageViewport.FitAxis(image, frame, true), height = ImageViewport.FitAxis(image, frame, false);
            Assert(Math.Abs(width.Zoom * image.Width - frame.Width) < 0.000001, "rotated width fits exactly");
            Assert(Math.Abs(height.Zoom * image.Height - frame.Height) < 0.000001, "rotated height fits exactly");
            AssertNear(width.Y, (frame.Height - image.Height * width.Zoom) / 2, "width vertically centered");
            AssertNear(height.X, (frame.Width - image.Width * height.Zoom) / 2, "height horizontally centered");
        }
        Assert(ImageViewport.Fit(new Size(100, 80), new Size(800, 600)).Zoom == 1, "default fit still does not enlarge small images");
        Assert(ImageViewport.FitAxis(new Size(100, 80), new Size(800, 600), true).Zoom == 8, "explicit fit width can enlarge small images");
        Assert(ImageViewport.Center(new Size(10, 10), new Size(800, 600), 1000).Zoom == 64, "explicit zoom has upper limit");

        string root = Path.Combine(Root, "zoom-controls-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"), profile = Path.Combine(root, "Profile"); Directory.CreateDirectory(photos);
        string large = Path.Combine(photos, "A-large.png"), portrait = Path.Combine(photos, "B-portrait.png"), small = Path.Combine(photos, "C-small.png");
        MakeImage(large, 2000, 1200, 300); MakeImage(portrait, 1000, 1800, 72); MakeImage(small, 240, 160, 96);
        var services = AppServices.CreateInstance(profile);
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1600, WindowHeight = 820 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            var toggle = Field<ToggleButton>(window, "_zoomLockButton");
            Assert(toggle.IsChecked == false && toggle.IsEnabled, "zoom lock defaults off and is available in browser");
            foreach (string field in new[] { "_fitWidthButton", "_fitHeightButton", "_actualSizeButton" })
            {
                var button = Field<Button>(window, field);
                Assert(!button.IsEnabled, "image zoom command disabled in browser");
                Assert(button.ToolTip is ToolTip && !String.IsNullOrEmpty(AutomationProperties.GetName(button)), "zoom command has accessible tooltip");
            }
            Invoke(window, "OpenImageTab", large, true); Wait(delegate { return Ready(window, large); }, "large zoom fixture");
            var canvas = Field<Canvas>(window, "_imageCanvas");
            canvas.Focus(); IInputElement focus = Keyboard.FocusedElement;
            ClickZoom(window, "_fitWidthButton"); AssertZoom(window, canvas.ActualWidth / 2000, "fit width uses pixel dimensions, not DPI metadata"); CheckZoomCentered(window);
            Assert(Keyboard.FocusedElement == focus, "zoom toolbar leaves image keyboard focus intact");
            ClickZoom(window, "_fitHeightButton"); AssertZoom(window, canvas.ActualHeight / 1200, "fit height"); CheckZoomCentered(window);
            ClickZoom(window, "_actualSizeButton"); AssertZoom(window, 1, "original size"); CheckZoomCentered(window);
            Invoke(window, "ZoomFromCenter", 1.25); ClickZoom(window, "_zoomLockButton");
            Assert(toggle.IsChecked == true && Capture(window).ZoomLocked, "lock is highlighted and captured");
            Invoke(window, "OpenRelativeImage", 1); Wait(delegate { return Ready(window, portrait); }, "locked next image"); AssertZoom(window, 1.25, "next keeps zoom"); CheckZoomCentered(window);
            Invoke(window, "OpenRelativeImage", 1); Wait(delegate { return Ready(window, small); }, "locked small image"); AssertZoom(window, 1.25, "lock can enlarge next small image");
            Invoke(window, "OpenRelativeImage", 1); Wait(delegate { return Ready(window, large); }, "locked circular navigation"); AssertZoom(window, 1.25, "wrap keeps zoom");
            Invoke(window, "OpenImageTab", portrait, true); Wait(delegate { return Ready(window, portrait); }, "locked other tab"); AssertZoom(window, 1.25, "new tab inherits zoom");
            Invoke(window, "ApplyMouseWheelZoom", new Point(180, 150), 120, true); AssertZoom(window, 1.4375, "wheel adjusts locked zoom");
            Assert(Math.Abs(Capture(window).LockedZoom - 1.4375) < 0.000001, "locked level follows manual zoom");
            Invoke(window, "RotateImage", 1); AssertZoom(window, 1.4375, "rotation keeps locked zoom"); CheckZoomCentered(window);
            ClickZoom(window, "_fitWidthButton"); AssertZoom(window, canvas.ActualWidth / 1800, "fit width respects rotation");
            ClickZoom(window, "_fitHeightButton"); AssertZoom(window, canvas.ActualHeight / 1000, "fit height respects rotation");
            ClickZoom(window, "_actualSizeButton");
            Assert((bool)Invoke(window, "BeginImagePan", new Point(300, 250)), "oversized image supports drag");
            Invoke(window, "UpdateImagePan", new Point(350, 290), true); Invoke(window, "EndImagePan");
            SessionState beforeResize = Capture(window); window.Width = 1440; Pump(); AssertZoom(window, 1, "resize keeps locked zoom");
            var pan = Field<TranslateTransform>(window, "_imageTranslate"); double x = pan.X;
            Invoke(window, "QueueImageView"); Pump(); AssertNear(pan.X, x, "layout refresh retains pan");
            Assert(beforeResize.ZoomLocked, "pan retains lock");
            Render((FrameworkElement)window.Content, "zoom-controls-dark.png");
            ThemeManager.SetDarkTheme(false); window.Width = 980; Pump();
            Assert(toggle.IsChecked == true, "lock survives theme change"); CheckSearchAlignment(window, false);
            Render((FrameworkElement)window.Content, "zoom-controls-light-narrow.png"); ThemeManager.SetDarkTheme(true);

            // Simulate the existing AI result callback, without running a model for this UI test.
            var original = (BitmapSource)Field<Image>(window, "_mainImage").Source;
            string upscaled = Path.Combine(root, "upscaled.png"); MakeImage(upscaled, 2000, 3600, 96);
            var pixels = services.Decoders.Decode(upscaled, 0, System.Threading.CancellationToken.None);
            typeof(MainWindow).GetField("_autoUpscaleOriginal", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, original);
            typeof(MainWindow).GetField("_autoUpscaleFactor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, 2.0);
            Invoke(window, "SetAutomaticUpscalePixels", pixels); AssertZoom(window, 0.5, "AI replacement keeps apparent size");
            Assert(Math.Abs(Capture(window).LockedZoom - 1) < 0.000001, "AI lock remains source-relative");
            Invoke(window, "ClearAutomaticUpscale"); Invoke(window, "SetAutomaticUpscalePixels", original); AssertZoom(window, 1, "original restoration keeps size");
            var map = Field<ShortcutMap>(window, "_shortcuts"); string error;
            Assert(map.Set("fitWidth", ShortcutKeys(Key.W), out error), "fit width rebindable");
            Assert(map.Set("fitHeight", ShortcutKeys(Key.F6), out error), "fit height rebindable");
            Assert(map.Set("zoomLock", ShortcutKeys(Key.L), out error), "zoom lock rebindable");
            Invoke(window, "ShortcutsChanged"); canvas.Focus();
            Invoke(window, "RunShortcut", Key.W, ModifierKeys.None, false); AssertZoom(window, canvas.ActualWidth / 1800, "width shortcut dispatches");
            Invoke(window, "RunShortcut", Key.F6, ModifierKeys.None, false); AssertZoom(window, canvas.ActualHeight / 1000, "height shortcut dispatches");
            Invoke(window, "RunShortcut", Key.L, ModifierKeys.None, false); Assert(!Capture(window).ZoomLocked, "unlock shortcut dispatches");
            Invoke(window, "OpenBrowserImage", small); Wait(delegate { return Ready(window, small); }, "unlocked next image"); AssertZoom(window, 1, "unlocked small image defaults to original size");
            ClickZoom(window, "_fitWidthButton"); AssertZoom(window, canvas.ActualWidth / 240, "explicit width enlarges small image");
            ClickZoom(window, "_actualSizeButton");
            Invoke(window, "ZoomFromCenter", 1.5); ClickZoom(window, "_zoomLockButton");
            Invoke(window, "BrowseActiveTab"); WaitScan(window);
            Assert(!Field<Button>(window, "_fitWidthButton").IsEnabled && toggle.IsChecked == true, "browser disables image commands but retains lock");
            Invoke(window, "OpenBrowserImage", large); Wait(delegate { return Ready(window, large); }, "return from browser"); AssertZoom(window, 1.5, "browser round-trip preserves zoom");

            services.Sessions.SaveNamed("Locked zoom", Capture(window));
            string backup = Path.Combine(root, "backup.json");
            var document = (ViewerBackupDocument)Invoke(window, "CaptureBackupSnapshot");
            document.SavedSessions = new System.Collections.Generic.List<NamedSessionDocument>(services.Sessions.CaptureNamedSessions());
            ViewerBackupStore.Save(backup, document);
            ViewerBackupDocument saved = ViewerBackupStore.Load(backup);
            Assert(saved.Workspace.ZoomLocked && Math.Abs(saved.Workspace.LockedZoom - 1.5) < 0.000001, "backup retains lock and level");
            Assert(saved.SavedSessions[0].State.ZoomLocked, "named session retains lock");
            foreach (double bad in new[] { -1.0, Double.NaN, Double.PositiveInfinity, 1000 })
            {
                saved.Workspace.LockedZoom = bad; bool rejected = false;
                try { ViewerBackupStore.Save(Path.Combine(root, "invalid.json"), saved); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "invalid locked zoom rejected");
            }
            Invoke(window, "RestoreZoomSettings", new SessionState()); Assert(toggle.IsChecked == false, "old sessions and reset default unlocked");
            Invoke(window, "RestoreZoomSettings", new SessionState { ZoomLocked = true, LockedZoom = Double.NaN });
            Assert(Field<double>(window, "_lockedZoom") == 1, "malformed local level falls back safely");
            Invoke(window, "RestoreZoomSettings", ViewerBackupStore.Load(backup).Workspace);
        }
        finally { window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true); }

        var reopenedServices = AppServices.CreateInstance(profile);
        var reopened = new MainWindow(reopenedServices); reopened.Show();
        try
        {
            Wait(delegate { return Ready(reopened, large); }, "restore locked workspace");
            Assert(Capture(reopened).ZoomLocked && Field<ToggleButton>(reopened, "_zoomLockButton").IsChecked == true, "lock restored on restart");
            AssertZoom(reopened, 1.5, "zoom restored on restart");
            Invoke(reopened, "OpenRelativeImage", 1); Wait(delegate { return Ready(reopened, portrait); }, "navigate restored lock"); AssertZoom(reopened, 1.5, "restored lock works on next image");
        }
        finally { reopened.Close(); Pump(); reopenedServices.Dispose(); }
        Console.WriteLine("PASS: zoom toolbar icons, fit width/height, rotations/DPI, centered 1:1, lock across navigation/tabs/resize/AI, pan, shortcuts, themes, session/backup and legacy settings.");
    }
}
