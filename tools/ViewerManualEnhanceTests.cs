using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMagick;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static byte[] ManualPixels(FrameworkElement surface)
    {
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }

    private static void CheckManualLayout(MainWindow window)
    {
        window.UpdateLayout();
        var view = Field<Grid>(window, "_imageView"); var panel = Field<Border>(window, "_manualEnhanceOverlay");
        Assert(panel.IsVisible && panel.ActualHeight >= 80, "manual panel remains usable"); AssertInside(panel, view);
        Rect bounds = new Rect(panel.TranslatePoint(new Point(), view), panel.RenderSize);
        AssertNear(bounds.Left, 16, "manual panel left inset");
        foreach (string name in new[] { "_imageMetadataOverlay", "_imageNavigatorOverlay", "_slideshowOverlay" })
        {
            var other = Field<Border>(window, name); if (!other.IsVisible) continue;
            Rect box = new Rect(other.TranslatePoint(new Point(), view), other.RenderSize);
            Assert(!bounds.IntersectsWith(box), "manual panel clears " + name);
            if (name == "_imageMetadataOverlay") AssertNear(bounds.Bottom + 8, box.Top, "Enhance stacks immediately above Info");
        }
        var sliders = Field<List<Slider>>(window, "_manualSliders");
        var scroll = Field<ScrollViewer>(window, "_manualEnhanceScroll");
        foreach (int index in new[] { 0, 9 })
        {
            sliders[index].BringIntoView(); Pump();
            AssertInside(sliders[index], scroll);
            var track = (Track)sliders[index].Template.FindName("PART_Track", sliders[index]);
            Assert(track != null && track.Thumb.ActualWidth == 42, "stable numeric slider handle");
            var text = FindManualValue(track.Thumb);
            Assert(text != null && text.Text == sliders[index].Value.ToString("0"), "numeric value rendered inside slider");
            AssertInside(text, track.Thumb);
        }
        scroll.ScrollToTop(); Pump();
    }

    private static TextBlock FindManualValue(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            var found = child as TextBlock ?? FindManualValue(child); if (found != null) return found;
        }
        return null;
    }

    private static void RunManualEnhanceChecks()
    {
        CheckManualTransparency();
        var neutral = new ManualAdjustments();
        Assert(neutral.IsNeutral && ManualAdjustments.IsValid(neutral), "neutral adjustment defaults");
        for (int i = 0; i < 256; i++) Assert(Math.Abs(neutral.Tone(i / 255.0) - i / 255.0) < 0.00001, "neutral tone identity");
        foreach (int index in new[] { 1, 2, 3, 4, 5, 8, 9 })
        foreach (double extreme in new[] { -100.0, 100.0 })
        {
            var settings = new ManualAdjustments(); settings.Values[index] = extreme;
            double last = 0;
            for (int i = 0; i < 256; i++)
            { double next = settings.Tone(i / 255.0); Assert(ImageViewport.IsFinite(next) && next >= last && next <= 1, "bounded monotonic tone curve"); last = next; }
        }
        string root = Path.Combine(Root, "manual-enhance-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string photo = Path.Combine(photos, "A.png"), other = Path.Combine(photos, "B.png");
        MakeImage(photo, 2400, 1600, 300); MakeImage(other, 640, 480, 96); byte[] original = File.ReadAllBytes(photo);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1440, WindowHeight = 920, SlideshowSeconds = 30 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        var button = Field<ToggleButton>(window, "_manualEnhanceButton");
        Assert(!button.IsEnabled && !Field<Border>(window, "_manualEnhanceOverlay").IsVisible, "browser has no manual controls");
        Invoke(window, "OpenImageTab", photo, true); Wait(delegate { return Ready(window, photo); }, "manual image"); WaitScan(window);
        button.IsChecked = true; button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        CheckManualLayout(window);
        var sliders = Field<List<Slider>>(window, "_manualSliders"); var resets = Field<List<Button>>(window, "_manualResets");
        var surface = Field<Canvas>(window, "_manualImageSurface"); var canvas = Field<Canvas>(window, "_imageCanvas");
        Assert(sliders.Count == 10 && resets.Count == 10 && surface.Effect == null, "ten neutral sliders with zero-cost bypass");
        AssertNear(surface.ActualWidth, canvas.ActualWidth, "effect surface bounded to viewport width");
        AssertNear(surface.ActualHeight, canvas.ActualHeight, "effect surface bounded to viewport height");
        BitmapSource source = (BitmapSource)Field<Image>(window, "_mainImage").Source;
        byte[] before = ManualPixels(canvas);
        for (int i = 0; i < sliders.Count; i++)
        {
            Assert(sliders[i].Minimum == ManualAdjustments.Minimum(i) && sliders[i].Maximum == 100, "slider range " + i);
            sliders[i].Value = 70; Pump();
            Assert(surface.Effect is ManualEnhanceEffect && resets[i].IsEnabled, "live shader and row reset enabled " + i);
            byte[] after = ManualPixels(canvas);
            Assert(before.Where((b, p) => Math.Abs(b - after[p]) > 1).Take(25).Count() >= 25, "slider visibly changes rendered pixels " + ManualAdjustments.Names[i]);
            resets[i].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
            Assert(sliders[i].Value == 0 && surface.Effect == null && !resets[i].IsEnabled, "per-value reset bypasses shader " + i);
        }
        var dragTrack = (Track)sliders[0].Template.FindName("PART_Track", sliders[0]);
        dragTrack.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        dragTrack.Thumb.RaiseEvent(new DragDeltaEventArgs(24, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        dragTrack.Thumb.RaiseEvent(new DragCompletedEventArgs(24, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert(sliders[0].Value > 0 && surface.Effect != null, "native thumb drag updates value and shader");
        sliders[0].Value = 30; sliders[1].Value = 20; sliders[2].Value = 25; sliders[7].Value = 40; Pump();
        var effect = surface.Effect;
        for (int i = 0; i < 50; i++) sliders[4].Value = i;
        Assert(Object.ReferenceEquals(effect, surface.Effect) && Object.ReferenceEquals(source, Field<Image>(window, "_mainImage").Source), "rapid sliders reuse effect and decoded image");
        sliders[4].Value = 0;
        Invoke(window, "SetImageMetadataOverlay", true); WaitOverlayMetadata(window, photo); Pump();
        var info = Field<Border>(window, "_imageMetadataOverlay");
        Point infoAt = info.TranslatePoint(new Point(), Field<Grid>(window, "_imageView"));
        Invoke(window, "SetManualEnhanceVisible", false); Pump();
        Assert(surface.Effect == effect && !Field<Border>(window, "_manualEnhanceOverlay").IsVisible, "closing preserves adjustments");
        AssertNear(info.TranslatePoint(new Point(), Field<Grid>(window, "_imageView")).Y, infoAt.Y, "Info stays in normal position");
        Invoke(window, "SetManualEnhanceVisible", true); Pump(); CheckManualLayout(window);
        Render((FrameworkElement)window.Content, "manual-enhance-dark.png");
        sliders[0].Focus();
        foreach (Key key in new[] { Key.Right, Key.Left, Key.Home, Key.End, Key.Enter, Key.Delete, Key.PageDown })
            Assert(!(bool)Invoke(window, "RunShortcut", key, ModifierKeys.None, false), "manual slider keeps native key " + key);
        string tab = Field<string>(window, "_activeTabId");
        Invoke(window, "DuplicateTab", tab); Wait(delegate { return Ready(window, photo); }, "manual duplicate");
        string copyId = Field<string>(window, "_activeTabId"); sliders[1].Value = -40;
        Invoke(window, "ActivateImageTab", tab); Wait(delegate { return Ready(window, photo); }, "manual original tab");
        Assert(sliders[1].Value == 20, "duplicate values are independent");
        Invoke(window, "BrowseActiveTab"); WaitScan(window);
        Assert(surface.Effect == null && !Field<Border>(window, "_manualEnhanceOverlay").IsVisible, "browser clears rendering and overlay");
        Invoke(window, "OpenBrowserImage", photo); Wait(delegate { return Ready(window, photo); }, "same image manual restore");
        Assert(sliders[1].Value == 20, "browser round-trip retains same image adjustments");
        Invoke(window, "RotateImage", 1); Pump(); Assert(sliders[1].Value == 20 && surface.Effect != null, "rotation preserves manual controls");
        Invoke(window, "SetZoomOneToOne"); Pump();
        window.Width = 980; window.Height = 640; Pump(); CheckManualLayout(window);
        Render((FrameworkElement)window.Content, "manual-enhance-narrow.png");
        ThemeManager.SetDarkTheme(false); Pump(); CheckManualLayout(window);
        Render((FrameworkElement)window.Content, "manual-enhance-light.png");
        Invoke(window, "SetCompactMode", true); Pump(); CheckManualLayout(window);
        Render((FrameworkElement)window.Content, "manual-enhance-compact.png");
        Invoke(window, "SetCompactMode", false); ThemeManager.SetDarkTheme(true); Pump();
        Invoke(window, "ToggleFullscreen"); Pump(); CheckManualLayout(window);
        Render((FrameworkElement)window.Content, "manual-enhance-fullscreen.png");
        Invoke(window, "ToggleFullscreen"); Pump();
        Invoke(window, "StartSlideshow"); Wait(delegate { return Ready(window, photo); }, "manual slideshow");
        Invoke(window, "SetZoomOneToOne"); Pump(); CheckManualLayout(window);
        var timer = Field<DispatcherTimer>(window, "_slideshowTimer");
        sliders[1].Focus(); Assert(!timer.IsEnabled, "adjustment focus pauses slideshow advancement");
        Invoke(window, "ArmSlideshow"); Assert(!timer.IsEnabled, "timer cannot restart while adjusting");
        canvas.Focus(); Pump(); Assert(timer.IsEnabled, "leaving controls resumes timer");
        Invoke(window, "StopSlideshow");
        Field<ToggleButton>(window, "_enhanceButton").IsChecked = true; Invoke(window, "ToggleQuickEnhance");
        Wait(delegate { return Field<Image>(window, "_mainImage").Effect != null; }, "Quick and manual enhancement together");
        Assert(surface.Effect != null, "Quick Enhance coexists with manual shader");
        Invoke(window, "ResetManualAdjustments"); Assert(surface.Effect == null && Field<Image>(window, "_mainImage").Effect != null, "Reset all keeps Quick Enhance");
        Field<ToggleButton>(window, "_enhanceButton").IsChecked = false; Invoke(window, "ToggleQuickEnhance");
        sliders[0].Value = 33; sliders[1].Value = 22; sliders[9].Value = -17;
        services.Sessions.SaveNamed("Manual adjustments", Capture(window));
        string backup = Path.Combine(root, "manual-backup.json"); WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
        var saved = ViewerBackupStore.Load(backup);
        Assert(saved.Workspace.ManualEnhanceVisible && saved.Workspace.Tabs.Single(t => t.Id == tab).ManualAdjustments.Values[1] == 22,
            "panel and values in backup");
        foreach (double bad in new[] { Double.NaN, 101, -101 })
        {
            var invalid = ViewerBackupStore.Load(backup); invalid.Workspace.Tabs[0].ManualAdjustments.Values[0] = bad;
            bool rejected = false; try { ViewerBackupStore.Save(Path.Combine(root, "invalid.json"), invalid); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "invalid backup values rejected");
        }
        Invoke(window, "OpenBrowserImage", other); Wait(delegate { return Ready(window, other); }, "different image resets controls");
        Assert(sliders.All(s => s.Value == 0) && surface.Effect == null, "new image starts neutral");
        WaitBackup((Task<int>)Invoke(window, "ImportBackupFileAsync", backup, false));
        Wait(delegate { return Ready(window, photo); }, "manual backup restored");
        Assert(sliders[1].Value == 22 && services.Sessions.LoadNamed("Manual adjustments").Tabs.Single(t => t.Id == tab).ManualAdjustments.Values[9] == -17,
            "named session and imported adjustments");
        Assert(original.SequenceEqual(File.ReadAllBytes(photo)), "source file unchanged");
        window.Close(); Pump(); window = new MainWindow(services); window.Show();
        Wait(delegate { return Ready(window, photo); }, "manual automatic restore");
        Assert(Field<List<Slider>>(window, "_manualSliders")[1].Value == 22 && Field<Border>(window, "_manualEnhanceOverlay").IsVisible,
            "restart restores manual values and panel");
        string gif = Path.Combine(root, "Animated.gif");
        using (var frames = new MagickImageCollection())
        {
            foreach (var color in new[] { MagickColors.Crimson, MagickColors.Olive })
            { var frame = CodecFixture(color, 120, 80); frame.AnimationDelay = 1000; frame.AnimationIterations = 0; frames.Add(frame); }
            frames.Write(gif, MagickFormat.Gif);
        }
        Invoke(window, "OpenImageTab", gif, true);
        Wait(delegate { return Ready(window, gif) && Field<ImageAnimation>(window, "_animation") != null; }, "manual animated image");
        sliders = Field<List<Slider>>(window, "_manualSliders"); sliders[1].Value = 35;
        var gifSurface = Field<Canvas>(window, "_manualImageSurface"); byte[] firstFrame = ManualPixels(gifSurface);
        Invoke(window, "AdvanceAnimation"); Pump();
        Assert(!firstFrame.SequenceEqual(ManualPixels(gifSurface)) && sliders[1].Value == 35, "manual shader updates with GIF frames without resetting settings");
        string error; var keys = Field<ShortcutMap>(window, "_shortcuts");
        Assert(keys.Set("manualEnhance", ShortcutKeys(Key.F9), out error), "manual panel shortcut is rebindable");
        Field<Canvas>(window, "_imageCanvas").Focus(); PressKey(window, Key.F9);
        Assert(!Field<Border>(window, "_manualEnhanceOverlay").IsVisible && gifSurface.Effect != null, "custom key hides panel without undoing values");
        window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true);
        Console.WriteLine("PASS: live manual Enhance pixels, ten numeric/reset sliders, bounded rendering, Info stacking, focus, Quick Enhance composition, tab isolation and session/backup persistence.");
    }

    private static void CheckManualTransparency()
    {
        byte[] pixels = new byte[64 * 64 * 4];
        for (int y = 16; y < 48; y++)
        for (int x = 16; x < 48; x++)
        { int at = (y * 64 + x) * 4; pixels[at] = 60; pixels[at + 1] = 100; pixels[at + 2] = 160; pixels[at + 3] = 128; }
        var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 256); bitmap.Freeze();
        var surface = new Canvas { Width = 64, Height = 64, ClipToBounds = true };
        surface.Children.Add(new Image { Source = bitmap, Width = 64, Height = 64 });
        var adjustment = new ManualAdjustments(); adjustment.Values[4] = 100; adjustment.Values[5] = -100; adjustment.Values[7] = 100;
        var effect = new ManualEnhanceEffect(); effect.Apply(adjustment); surface.Effect = effect;
        surface.Measure(new Size(64, 64)); surface.Arrange(new Rect(0, 0, 64, 64)); surface.UpdateLayout();
        byte[] rendered = ManualPixels(surface);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            int at = (y * 64 + x) * 4; byte expected = x >= 16 && x < 48 && y >= 16 && y < 48 ? (byte)128 : (byte)0;
            Assert(rendered[at + 3] == expected, "manual shader preserves alpha, including empty backdrop");
            Assert(rendered[at] <= expected && rendered[at + 1] <= expected && rendered[at + 2] <= expected, "output stays premultiplied");
        }
    }
}
