using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMagick;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static Point NavigatorCenter(Rect rect) { return new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2); }

    private static void CheckNavigatorGeometry()
    {
        var image = new Size(2000, 1000); var frame = new Size(500, 300);
        Rect bounds = ImageNavigator.FitBounds(image, new Size(200, 150));
        Assert(bounds == new Rect(0, 25, 200, 100), "navigator preserves aspect ratio and letterboxes");
        Rect visible = ImageNavigator.VisibleBounds(image, frame, 2, new Point(-1000, -500), bounds);
        Assert(visible == new Rect(50, 50, 25, 15), "navigator maps exact zoom and pan into thumbnail");
        var portrait = new Size(200, 2000); bounds = ImageNavigator.FitBounds(portrait, new Size(200, 150));
        visible = ImageNavigator.VisibleBounds(portrait, frame, 1, new Point(150, -500), bounds);
        AssertNear(visible.Width, bounds.Width, "a fitting axis spans the full overview");
        AssertNear(visible.Top, 37.5, "one-axis panning handles letterboxing");
        Assert(ImageNavigator.FitBounds(new Size(), frame).IsEmpty
            && ImageNavigator.VisibleBounds(image, frame, 0, new Point(), bounds).IsEmpty, "empty geometry is safe");
        var map = new ShortcutMap(); string error;
        Assert(map.Match(Key.P, ModifierKeys.None, false).Id == "navigator"
            && map.Match(Key.P, ModifierKeys.None, true) == null, "P is image-only");
        var old = new List<ShortcutOverride> { new ShortcutOverride { Action = "metadata", Keys = ShortcutKeys(Key.P) } };
        Assert(map.Load(old, out error) && map.Match(Key.P, ModifierKeys.None, false).Id == "metadata"
            && map.Display("navigator") == "Unassigned", "legacy custom P is preserved without resetting shortcuts");
        var copy = new ShortcutMap(); Assert(copy.Load(map.Export(), out error)
            && copy.Display("navigator") == "Unassigned", "legacy conflict migration round-trips");
        old.Add(new ShortcutOverride { Action = "navigator", Keys = ShortcutKeys(Key.P) });
        Assert(!map.Load(old, out error), "explicit duplicate P binding is rejected");
    }

    private static void CheckNavigatorLayout(MainWindow window)
    {
        window.UpdateLayout();
        var navigator = Field<ImageNavigator>(window, "_imageNavigator");
        var overlay = Field<Border>(window, "_imageNavigatorOverlay");
        var view = Field<Grid>(window, "_imageView");
        Assert(overlay.IsVisible && navigator.ActualWidth > 0 && navigator.ActualHeight > 0, "overflow navigator is visible");
        AssertInside(overlay, view); AssertInside(navigator, overlay);
        Point at = overlay.TranslatePoint(new Point(), view);
        AssertNear(at.X + overlay.ActualWidth, view.ActualWidth - 16, "navigator right inset");
        AssertNear(at.Y + overlay.ActualHeight, view.ActualHeight - overlay.Margin.Bottom, "navigator bottom inset");
        Rect box = new Rect(at, overlay.RenderSize);
        foreach (string name in new[] { "_imageMetadataOverlay", "_slideshowOverlay" })
        {
            var other = Field<Border>(window, name);
            if (!other.IsVisible) continue;
            AssertInside(other, view);
            Assert(!box.IntersectsWith(new Rect(other.TranslatePoint(new Point(), view), other.RenderSize)), "navigator clears " + name);
        }
        Rect image = navigator.ImageBounds, visible = navigator.ViewportBounds;
        Assert(new Rect(navigator.RenderSize).Contains(image), "entire thumbnail fits its own frame");
        Rect tolerant = image; tolerant.Inflate(0.01, 0.01);
        Assert(tolerant.Contains(visible), "visible-area rectangle stays inside image");
        Assert(Object.ReferenceEquals(navigator.Source, Field<Image>(window, "_mainImage").Source), "navigator shares loaded pixels without extra decode");
        Assert(((SolidColorBrush)overlay.Background).Color.A < 255, "navigator is translucent");
    }

    private static void CheckNavigatorPixels(MainWindow window, int rotation)
    {
        var navigator = Field<ImageNavigator>(window, "_imageNavigator");
        var preview = Field<Image>(navigator, "_preview");
        var bitmap = new RenderTargetBitmap((int)navigator.ActualWidth, (int)navigator.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render((Canvas)preview.Parent);
        byte[] pixels = ToolPixels(bitmap);
        Point sample = new Point(navigator.ImageBounds.Left + navigator.ImageBounds.Width * 0.2,
            navigator.ImageBounds.Top + navigator.ImageBounds.Height * 0.2);
        int index = ((int)sample.Y * bitmap.PixelWidth + (int)sample.X) * 4;
        double[] red = { 88, 88, 172, 172 }, green = { 88, 172, 172, 88 };
        Assert(Math.Abs(pixels[index + 2] - red[rotation]) < 8 && Math.Abs(pixels[index + 1] - green[rotation]) < 8
            && pixels[index + 3] == 255, "actual preview pixels preserve orientation and DPI at rotation " + rotation);
    }

    private static void DragNavigatorToEdges(MainWindow window)
    {
        var navigator = Field<ImageNavigator>(window, "_imageNavigator");
        var offset = Field<TranslateTransform>(window, "_imageTranslate");
        var scale = Field<ScaleTransform>(window, "_imageScale");
        var canvas = Field<Canvas>(window, "_imageCanvas");
        var image = (Size)Invoke(window, "DisplayedImageSize");
        double startX = offset.X, startY = offset.Y, zoom = scale.ScaleX;
        Point center = NavigatorCenter(navigator.ViewportBounds);
        Assert(navigator.BeginDrag(center) && navigator.IsMouseCaptured, "navigator captures rectangle drag");
        AssertNear(offset.X, startX, "grabbing inside does not jump x"); AssertNear(offset.Y, startY, "grabbing inside does not jump y");
        navigator.UpdateDrag(new Point(center.X + 3, center.Y + 2), true);
        double factor = image.Width / navigator.ImageBounds.Width * zoom;
        ImageView expected = ImageViewport.Constrain(image, canvas.RenderSize, zoom, startX - 3 * factor, startY - 2 * factor);
        AssertNear(offset.X, expected.X, "drag delta maps continuously x"); AssertNear(offset.Y, expected.Y, "drag delta maps continuously y");
        navigator.UpdateDrag(new Point(10000, 10000), true);
        expected = ImageViewport.Constrain(image, canvas.RenderSize, zoom, -10000000, -10000000);
        AssertNear(offset.X, expected.X, "drag clamps far edge x"); AssertNear(offset.Y, expected.Y, "drag clamps far edge y");
        navigator.UpdateDrag(new Point(-10000, -10000), true);
        expected = ImageViewport.Constrain(image, canvas.RenderSize, zoom, 10000000, 10000000);
        AssertNear(offset.X, expected.X, "drag reverses to near edge x"); AssertNear(offset.Y, expected.Y, "drag reverses to near edge y");
        navigator.UpdateDrag(new Point(), false);
        Assert(!navigator.IsDragging && !navigator.IsMouseCaptured, "button release ends capture");
        AssertNear(scale.ScaleX, zoom, "navigator preserves zoom");
        var tab = (ImageTabState)Invoke(window, "CurrentTabState");
        Assert(tab.HasCustomView && tab.OffsetX == offset.X && tab.OffsetY == offset.Y, "pan saves per-tab view");
    }

    private static void RunImageNavigatorChecks()
    {
        CheckNavigatorGeometry();
        string root = Path.Combine(Root, "navigator-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string large = Path.Combine(photos, "A.png"), tall = Path.Combine(photos, "B.png"), small = Path.Combine(photos, "C.png");
        MakeImage(large, 2400, 1600, 300); MakeImage(tall, 200, 2400, 96); MakeImage(small, 80, 40, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1440, WindowHeight = 820, SlideshowSeconds = 30 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        var navigator = Field<ImageNavigator>(window, "_imageNavigator");
        var overlay = Field<Border>(window, "_imageNavigatorOverlay");
        Assert(Field<bool>(window, "_imageNavigatorEnabled") && overlay.Visibility == Visibility.Collapsed, "legacy session defaults on but browser hides preview");
        Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid").Focus(); PressKey(window, Key.P);
        Assert(Field<bool>(window, "_imageNavigatorEnabled"), "P ignored in browser");
        Invoke(window, "OpenImageTab", large, true); Wait(delegate { return Ready(window, large); }, "navigator large image"); WaitScan(window);
        Assert(overlay.Visibility == Visibility.Collapsed && navigator.Source == null, "fitted large image needs no navigator");
        Invoke(window, "SetZoomOneToOne"); Pump(); CheckNavigatorLayout(window);
        for (int turn = 0; turn < 4; turn++)
        {
            CheckNavigatorPixels(window, turn); DragNavigatorToEdges(window);
            navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds));
            Invoke(window, "RotateImage", 1); Pump();
            Assert(!navigator.IsDragging && overlay.Visibility == Visibility.Collapsed, "rotation cancels drag and refits");
            Invoke(window, "SetZoomOneToOne"); Pump(); CheckNavigatorLayout(window);
        }
        var translate = Field<TranslateTransform>(window, "_imageTranslate");
        navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds)); navigator.UpdateDrag(new Point(-1000, -1000), true); navigator.EndDrag();
        Point destination = new Point(navigator.ImageBounds.Right - 1, navigator.ImageBounds.Bottom - 1);
        Assert(navigator.BeginDrag(destination) && translate.X < -500 && translate.Y < -500, "click outside rectangle centers another image region");
        navigator.EndDrag();
        Render((FrameworkElement)window.Content, "image-navigator-dark.png"); Render(navigator, "image-navigator-preview.png");
        navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds)); navigator.ReleaseMouseCapture();
        Assert(!navigator.IsDragging, "capture loss cancels navigator drag");
        navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds));
        Invoke(window, "RunShortcut", Key.P, ModifierKeys.None, false);
        Assert(!navigator.IsDragging && overlay.Visibility == Visibility.Collapsed && navigator.Source == null, "P during drag hides and releases shared source");
        Field<Canvas>(window, "_imageCanvas").Focus();
        Invoke(window, "RunShortcut", Key.P, ModifierKeys.None, true);
        Assert(!Field<bool>(window, "_imageNavigatorEnabled"), "held P does not repeatedly toggle");
        PressKey(window, Key.P); Pump(); CheckNavigatorLayout(window);
        Field<TextBox>(window, "_searchBox").Focus();
        Assert(!(bool)Invoke(window, "RunShortcut", Key.P, ModifierKeys.None, false), "typing P does not toggle navigator");
        Field<Canvas>(window, "_imageCanvas").Focus();
        navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds));
        Invoke(window, "ShowConfigure"); Pump(); Assert(!navigator.IsDragging, "opening settings releases capture");
        var toggle = Field<CheckBox>(window, "_configureImageNavigator");
        toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Assert(!Field<bool>(window, "_imageNavigatorEnabled"), "Configure toggles navigator");
        toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); CloseViewerSettings(window); Pump();

        Invoke(window, "SetImageMetadataOverlay", true); WaitOverlayMetadata(window, large);
        Invoke(window, "StartSlideshow"); Wait(delegate { return Ready(window, large); }, "navigator slideshow");
        Invoke(window, "SetZoomOneToOne"); Pump();
        var timer = Field<DispatcherTimer>(window, "_slideshowTimer");
        navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds));
        Assert(!timer.IsEnabled, "navigator drag suspends slideshow timer");
        Invoke(window, "ArmSlideshow"); Assert(!timer.IsEnabled, "asynchronous updates cannot restart timer during drag");
        navigator.EndDrag(); Assert(timer.IsEnabled, "release restarts slideshow interval");
        Invoke(window, "ToggleSlideshowPause"); navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds)); navigator.EndDrag();
        Assert(!timer.IsEnabled && Field<bool>(window, "_slideshowPaused"), "drag does not override manual pause");
        window.Width = 980; window.Height = 640; Pump();
        CheckNavigatorLayout(window); CheckSlideshowOverlayLayout(window); CheckMetadataOverlayBounds(window);
        Render((FrameworkElement)window.Content, "image-navigator-narrow-overlays.png");
        ThemeManager.SetDarkTheme(false); Pump(); CheckNavigatorLayout(window);
        Render((FrameworkElement)window.Content, "image-navigator-light.png");
        Invoke(window, "SetCompactMode", true); Pump(); CheckNavigatorLayout(window);
        Render((FrameworkElement)window.Content, "image-navigator-compact.png");
        Invoke(window, "ToggleFullscreen"); Pump(); Invoke(window, "ZoomFromCenter", 4.0); Pump(); CheckNavigatorLayout(window);
        Render((FrameworkElement)window.Content, "image-navigator-fullscreen.png");
        Invoke(window, "ToggleFullscreen"); Invoke(window, "SetCompactMode", false); ThemeManager.SetDarkTheme(true); Pump();
        navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds)); window.Width = 1100; Pump();
        Assert(!navigator.IsDragging, "resize releases drag before restoring view"); CheckNavigatorLayout(window);
        Invoke(window, "SetMetadataPanelVisible", true); Pump(); CheckNavigatorLayout(window); CheckMetadataOverlayBounds(window);
        Invoke(window, "SetMetadataPanelVisible", false); Pump();
        navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds)); PressKey(window, Key.Enter); Pump(); WaitScan(window);
        Assert(overlay.Visibility == Visibility.Collapsed && navigator.Source == null && !navigator.IsDragging, "return to browser clears overview/capture");
        Invoke(window, "OpenImageTab", tall, true); Wait(delegate { return Ready(window, tall); }, "one-axis navigator");
        Invoke(window, "SetZoomOneToOne"); Pump(); CheckNavigatorLayout(window); DragNavigatorToEdges(window);
        AssertNear(navigator.ViewportBounds.Width, navigator.ImageBounds.Width, "fitting horizontal axis remains fully visible");
        Invoke(window, "OpenImageTab", small, true); Wait(delegate { return Ready(window, small); }, "small image navigator");
        Assert(!overlay.IsVisible && navigator.Source == null, "unscaled small image hides navigator");
        Invoke(window, "ZoomFromCenter", 64.0); Pump(); CheckNavigatorLayout(window);
        Invoke(window, "FitImageToView", true); Assert(!overlay.IsVisible, "Fit hides navigator");

        string gif = Path.Combine(root, "navigator.gif");
        using (var animation = new MagickImageCollection())
        {
            foreach (var color in new[] { MagickColors.Red, MagickColors.Lime })
            {
                var frame = CodecFixture(color, 120, 80); frame.AnimationDelay = 1000; frame.AnimationIterations = 0; animation.Add(frame);
            }
            animation.Write(gif, MagickFormat.Gif);
        }
        Invoke(window, "OpenImageTab", gif, true);
        Wait(delegate { return Ready(window, gif) && Field<ImageAnimation>(window, "_animation") != null; }, "navigator animated GIF");
        Invoke(window, "ZoomFromCenter", 16.0); Pump();
        BitmapSource previous = navigator.Source; navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds));
        Invoke(window, "AdvanceAnimation");
        Assert(navigator.IsDragging && navigator.Source != previous && Object.ReferenceEquals(navigator.Source, Field<Image>(window, "_mainImage").Source),
            "GIF preview follows current frame without interrupting drag");
        navigator.EndDrag();
        string bad = Path.Combine(root, "bad.png"); File.WriteAllBytes(bad, new byte[] { 1, 2, 3 });
        Invoke(window, "OpenImageTab", bad, true);
        Assert(!overlay.IsVisible && navigator.Source == null, "image loading immediately clears previous preview");
        Wait(delegate { return Field<TextBlock>(window, "_imageError").IsVisible; }, "unreadable image placeholder");
        Assert(!overlay.IsVisible, "unavailable image has no stale overview");
        Invoke(window, "ActivateImageTab", large); Invoke(window, "OpenBrowserImage", large);
        Wait(delegate { return Ready(window, large); }, "navigator restore tab");
        Invoke(window, "SetZoomOneToOne"); Pump(); DragNavigatorToEdges(window);
        Invoke(window, "SetImageNavigator", false);
        var shortcuts = Field<ShortcutMap>(window, "_shortcuts"); string error;
        Assert(shortcuts.Set("navigator", ShortcutKeys(Key.O), out error), "navigator can be rebound");
        Field<Canvas>(window, "_imageCanvas").Focus();
        Assert(!(bool)Invoke(window, "RunShortcut", Key.P, ModifierKeys.None, false), "old P no longer acts after rebind");
        PressKey(window, Key.O); Assert(Field<bool>(window, "_imageNavigatorEnabled"), "custom key toggles navigator");
        PressKey(window, Key.O);
        SessionState state = Capture(window); services.Sessions.SaveNamed("Navigator off", state);
        string backup = Path.Combine(root, "navigator-backup.json");
        WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
        Assert(ViewerBackupStore.Load(backup).Workspace.ImageNavigatorEnabled == false
            && services.Sessions.LoadNamed("Navigator off").ImageNavigatorEnabled == false, "explicit off survives backup/named serialization");
        Invoke(window, "SetImageNavigator", true);
        WaitBackup((Task<int>)Invoke(window, "ImportBackupFileAsync", backup, false));
        Wait(delegate { return Ready(window, large); }, "navigator backup import");
        Assert(!Field<bool>(window, "_imageNavigatorEnabled"), "import applies navigator setting");
        window.Close(); Pump(); window = new MainWindow(services); window.Show();
        Wait(delegate { return Ready(window, large); }, "navigator session restart");
        Assert(!Field<bool>(window, "_imageNavigatorEnabled") && !Field<Border>(window, "_imageNavigatorOverlay").IsVisible
            && Field<ShortcutMap>(window, "_shortcuts").Display("navigator") == "O", "automatic session restores off and custom key");
        AssertNear(Field<TranslateTransform>(window, "_imageTranslate").X, state.Tabs.Single(t => t.Id == state.ActiveTabId).OffsetX, "drag position restored");
        Invoke(window, "SetImageNavigator", true); Pump(); CheckNavigatorLayout(window);
        navigator = Field<ImageNavigator>(window, "_imageNavigator");
        navigator.BeginDrag(NavigatorCenter(navigator.ViewportBounds)); window.Close(); Pump();
        Assert(navigator.Source == null && !navigator.IsDragging && !navigator.IsMouseCaptured, "closing releases navigator and its bitmap reference");
        services.Dispose(); ThemeManager.SetDarkTheme(true);
        Console.WriteLine("PASS: image pan navigator geometry, rotated/DPI pixels, captured drag/click/clamping, one-axis fit, keys, overlays, GIF, lifecycle and session/backup persistence.");
    }
}
