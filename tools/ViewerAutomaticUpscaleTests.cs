using System;
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
    private static void RunAutomaticUpscaleChecks()
    {
        Assert(SuperResolution.AutomaticScale(new Size(400, 300), new Size(640, 480)) == 1.6, "auto chooses exact supported step");
        Assert(SuperResolution.AutomaticScale(new Size(800, 600), new Size(960, 720)) == 1.25, "auto rounds up to supported step");
        Assert(SuperResolution.AutomaticScale(new Size(1600, 1200), new Size(1000, 750)) == 1, "auto skips sufficient native pixels");
        Assert(SuperResolution.AutomaticScale(new Size(300, 400), new Size(480, 640)) == 1.6, "portrait/rotation-aware sizing");
        Assert(SuperResolution.AutomaticScale(new Size(200, 100), new Size(2000, 1000)) == 3, "auto caps at model maximum");
        Assert(SuperResolution.AutomaticScale(new Size(2000, 3000), new Size(8000, 12000)) == 2, "auto respects output pixel cap");
        Assert(SuperResolution.AutomaticScale(new Size(4000, 3000), new Size(8000, 6000)) == 1, "auto skips oversized sources");
        Assert(SuperResolution.AutomaticScale(new Size(1000, 1000), new Size(1050, 1050)) == 1, "ignore negligible enlargement");
        var map = new ShortcutMap(); string error;
        Assert(map.Match(Key.S, ModifierKeys.None, false).Id == "upscale" && map.Match(Key.S, ModifierKeys.None, true) == null, "S defaults to image-only upscale");
        Assert(map.Load(new System.Collections.Generic.List<ShortcutOverride> { new ShortcutOverride { Action = "metadata", Keys = ShortcutKeys(Key.S) } }, out error)
            && map.Match(Key.S, ModifierKeys.None, false).Id == "metadata" && map.Display("upscale") == "Unassigned", "legacy custom S has priority");
        map.ResetAll(); Assert(map.Set("upscale", ShortcutKeys(Key.U), out error), "AI shortcut can be rebound");
        var restored = new ShortcutMap(); Assert(restored.Load(map.Export(), out error) && restored.Match(Key.U, ModifierKeys.None, false).Id == "upscale"
            && restored.Match(Key.S, ModifierKeys.None, false) == null, "AI shortcut overrides round-trip");

        string folder = Path.Combine(Root, "auto-upscale-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Small.png"), large = Path.Combine(folder, "Large.png");
        MakeImage(path, 320, 200, 96); MakeImage(large, 2000, 1600, 96); byte[] original = File.ReadAllBytes(path);
        var services = AppServices.CreateInstance(Path.Combine(folder, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = folder, WindowWidth = 1440, WindowHeight = 900, ProcessingGpuKey = "CPU" });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            var scaleSlider = Field<Slider>(window, "_upscaleSizeSlider");
            Assert(!scaleSlider.IsVisible, "upscale slider is hidden in browser");
            Assert(!(bool)Invoke(window, "RunShortcut", Key.S, ModifierKeys.None, false), "S does not activate in browser");
            Invoke(window, "OpenImageTab", path, true); Wait(delegate { return Ready(window, path); }, "small source"); WaitScan(window);
            var button = Field<Button>(window, "_upscaleButton");
            var label = (StackPanel)button.Content;
            Assert(label.Children.OfType<TextBlock>().Any(t => t.Text == "AI upscale") && button.IsVisible, "top row has visible AI upscale label");
            Field<TextBox>(window, "_searchBox").Focus();
            Assert(!(bool)Invoke(window, "RunShortcut", Key.S, ModifierKeys.None, false), "typing S in search is untouched");
            var canvas = Field<Canvas>(window, "_imageCanvas"); canvas.Focus();
            Invoke(window, "ChangeManualAdjustment", 0, 17.0);
            BitmapSource native = (BitmapSource)Field<Image>(window, "_mainImage").Source;
            Assert((bool)Invoke(window, "RunShortcut", Key.S, ModifierKeys.None, false), "S starts automatic upscale");
            var pending = Field<Task>(window, "_autoUpscaleTask");
            Assert(pending != null && !Application.Current.Windows.OfType<SuperResolutionWindow>().Any(), "automatic action has no comparison dialog");
            var cancellation = Field<CancellationTokenSource>(window, "_autoUpscaleRequest");
            Invoke(window, "RunShortcut", Key.S, ModifierKeys.None, true);
            Assert(!cancellation.IsCancellationRequested, "held S does not cancel or enqueue jobs");
            Invoke(window, "RunShortcut", Key.S, ModifierKeys.None, false);
            Wait(delegate { return pending.IsCompleted; }, "S cancels active upscale"); pending.GetAwaiter().GetResult();
            Assert(Object.ReferenceEquals(Field<Image>(window, "_mainImage").Source, native), "cancel keeps original pixels");
            Assert(!scaleSlider.IsVisible, "canceled initial upscale hides slider");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pending = Field<Task>(window, "_autoUpscaleTask");
            Wait(delegate { return pending.IsCompleted && !Field<bool>(window, "_imageViewPending"); }, "automatic viewer result"); pending.GetAwaiter().GetResult();
            var pixels = (BitmapSource)Field<Image>(window, "_mainImage").Source;
            Assert(pixels.PixelWidth > native.PixelWidth && Field<BitmapSource>(window, "_autoUpscaleOriginal") == native, "button applies larger source directly in viewer");
            Assert(((ImageTabState)Invoke(window, "CurrentTabState")).ManualAdjustments.Values[0] == 17, "manual values stay live and unchanged");
            Assert(original.SequenceEqual(File.ReadAllBytes(path)), "automatic apply never writes original file");
            double factor = pixels.PixelWidth / (double)native.PixelWidth;
            Invoke(window, "ApplyMouseWheelZoom", new Point(300, 200), 240, true);
            double zoom = Field<ScaleTransform>(window, "_imageScale").ScaleX;
            var state = Capture(window).Tabs.Single(t => t.Path == path);
            Assert(Math.Abs(state.Zoom - zoom * factor) < 0.0001, "session zoom is stored in original-image coordinates");
            window.Width -= 60; Pump(); Wait(delegate { return !Field<bool>(window, "_imageViewPending"); }, "resize with upscale");
            Assert(Math.Abs(Field<ScaleTransform>(window, "_imageScale").ScaleX - zoom) < 0.0001, "window resize does not compound AI zoom");
            Render(window, "auto-upscale-viewer.png");
            CheckInlineUpscaleSlider(window, native, path);
            canvas.Focus(); Invoke(window, "RunShortcut", Key.S, ModifierKeys.None, false); Pump();
            Assert(Field<BitmapSource>(window, "_autoUpscaleOriginal") == null && Object.ReferenceEquals(Field<Image>(window, "_mainImage").Source, native), "S restores original view");
            Assert(!scaleSlider.IsVisible, "switching upscale off hides slider");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); pending = Field<Task>(window, "_autoUpscaleTask");
            Invoke(window, "OpenImageTab", large, true); Wait(delegate { return Ready(window, large) && pending.IsCompleted; }, "navigation cancels old AI result");
            Assert(Field<BitmapSource>(window, "_autoUpscaleOriginal") == null && ((BitmapSource)Field<Image>(window, "_mainImage").Source).PixelWidth == 2000, "stale completion cannot replace next image");
            Assert(!scaleSlider.IsVisible, "navigation hides slider");
            canvas.Focus(); Invoke(window, "RunShortcut", Key.S, ModifierKeys.None, false);
            Assert(Field<CancellationTokenSource>(window, "_autoUpscaleRequest") == null && Field<string>(window, "_transferStatus").Contains("not needed"), "large image is left unchanged with feedback");
            Invoke(window, "OpenImageTab", path, true); Wait(delegate { return Ready(window, path); }, "small image for export"); WaitScan(window);
            canvas.Focus(); Invoke(window, "RunShortcut", Key.S, ModifierKeys.None, false); pending = Field<Task>(window, "_autoUpscaleTask");
            Wait(delegate { return pending.IsCompleted; }, "AI preview before save"); pending.GetAwaiter().GetResult();
            SetInlineUpscaleSize(window, 1.6);
            int outputWidth = ((BitmapSource)Field<Image>(window, "_mainImage").Source).PixelWidth;
            string copy = Path.Combine(folder, "Upscaled-copy.png");
            var saving = (Task<EnhancedSaveResult>)Invoke(window, "SaveEnhancedImageAsync", copy, false, false);
            Wait(delegate { return saving.IsCompleted; }, "save inline AI preview"); saving.GetAwaiter().GetResult();
            Wait(delegate { return Ready(window, copy); }, "saved inline preview opens"); WaitScan(window);
            Assert(((BitmapSource)Field<Image>(window, "_mainImage").Source).PixelWidth == outputWidth
                && Field<BitmapSource>(window, "_autoUpscaleOriginal") == null && original.SequenceEqual(File.ReadAllBytes(path)), "Save As retains upscaled size without changing the source");
            Invoke(window, "OpenImageTab", path, true); Wait(delegate { return Ready(window, path); }, "small image for closing"); WaitScan(window);
            canvas.Focus(); Invoke(window, "RunShortcut", Key.S, ModifierKeys.None, false); pending = Field<Task>(window, "_autoUpscaleTask");
            window.Close(); Wait(delegate { return pending.IsCompleted; }, "close cancels automatic AI"); pending.GetAwaiter().GetResult();
            Assert(Field<CancellationTokenSource>(window, "_autoUpscaleRequest") == null, "closing releases automatic processing state");
        }
        finally { window.Close(); services.Dispose(); Pump(); }
        Console.WriteLine("PASS: automatic size/DPI policy, S migration/rebinding/focus/repeat, labeled button, inline apply/restore/cancel, footer scale/debounce/keyboard/1x/limits/layout, stale-result rejection and original-coordinate session zoom.");
    }

    private static void SetInlineUpscaleSize(MainWindow window, double scale)
    {
        Task before = Field<Task>(window, "_autoUpscaleTask");
        Field<Slider>(window, "_upscaleSizeSlider").Value = Array.IndexOf(SuperResolution.Scales, scale);
        Wait(delegate { return Field<Task>(window, "_autoUpscaleTask") != before
            && Field<Task>(window, "_autoUpscaleTask").IsCompleted && !Field<bool>(window, "_imageViewPending"); }, "slider " + scale + "x result");
        Field<Task>(window, "_autoUpscaleTask").GetAwaiter().GetResult();
        var original = Field<BitmapSource>(window, "_autoUpscaleOriginal");
        var displayed = (BitmapSource)Field<Image>(window, "_mainImage").Source;
        Assert(displayed.PixelWidth == (int)Math.Round(original.PixelWidth * scale)
            && displayed.PixelHeight == (int)Math.Round(original.PixelHeight * scale), "slider output has selected native-relative dimensions");
    }

    private static void CheckInlineUpscaleSlider(MainWindow window, BitmapSource native, string path)
    {
        var slider = Field<Slider>(window, "_upscaleSizeSlider");
        var group = Field<StackPanel>(window, "_upscaleSizeControls");
        var timer = Field<DispatcherTimer>(window, "_upscaleSizeTimer");
        var label = Field<TextBlock>(window, "_upscaleSizeText");
        Assert(slider.IsVisible && slider.Value == 7 && label.Text == "3x", "active footer shows automatic factor");
        Task before = Field<Task>(window, "_autoUpscaleTask");
        slider.Value = 1; slider.Value = 3; slider.Value = 2;
        Assert(timer.IsEnabled && Field<Task>(window, "_autoUpscaleTask") == before && label.Text == "1.4x", "rapid changes debounce with immediate factor feedback");
        Assert(!(bool)Invoke(window, "CanSaveEnhancement"), "cannot save an obsolete preview while slider is pending");
        Wait(delegate { return Field<Task>(window, "_autoUpscaleTask") != before && Field<Task>(window, "_autoUpscaleTask").IsCompleted
            && !Field<bool>(window, "_imageViewPending"); }, "debounced final choice");
        Field<Task>(window, "_autoUpscaleTask").GetAwaiter().GetResult();
        var pixels = (BitmapSource)Field<Image>(window, "_mainImage").Source;
        Assert(pixels.PixelWidth == 448 && pixels.PixelHeight == 280 && Field<BitmapSource>(window, "_autoUpscaleOriginal") == native, "regeneration starts from native 320x200, not previous 3x result");
        var fresh = SuperResolution.RunAsync(new SuperResolutionRequest { Source = native, Scale = 1.4, Workers = 4 }, CancellationToken.None, null);
        Wait(delegate { return fresh.IsCompleted; }, "fresh reference upscale");
        Assert(ToolPixels(pixels).SequenceEqual(ToolPixels(fresh.GetAwaiter().GetResult().Pixels)), "slider result matches a fresh original-based upscale pixel for pixel");

        slider.Focus(); PressControlKey(slider, Key.Left);
        Assert(slider.Value == 1 && Field<string>(window, "_activeTabPath") == path, "left arrow adjusts factor without image navigation");
        PressControlKey(slider, Key.Right); Assert(slider.Value == 2, "right arrow adjusts factor");
        PressControlKey(slider, Key.Home); Assert(slider.Value == 0 && Field<string>(window, "_activeTabPath") == path, "Home selects original, not first image");
        Wait(delegate { return !timer.IsEnabled && Object.ReferenceEquals(Field<Image>(window, "_mainImage").Source, native)
            && !Field<bool>(window, "_imageViewPending"); }, "1x restores native pixels");
        Assert(slider.IsVisible && label.Text == "1x" && Field<BitmapSource>(window, "_autoUpscaleOriginal") == native, "1x retains active slider without AI reconstruction");
        // Native test windows can lose activation to the desktop while inference is running.
        if (!window.IsActive) { window.Activate(); slider.Focus(); }
        Assert(slider.IsKeyboardFocusWithin, "1x refresh retains slider keyboard focus; focused=" + (Keyboard.FocusedElement == null ? "none" : Keyboard.FocusedElement.GetType().Name));
        PressControlKey(slider, Key.End); Assert(slider.Value == 7 && Field<string>(window, "_activeTabPath") == path,
            "End selects largest safe factor, not last image; value=" + slider.Value + ", max=" + slider.Maximum + ", focus=" + slider.IsKeyboardFocusWithin);
        before = Field<Task>(window, "_autoUpscaleTask");
        Wait(delegate { return Field<Task>(window, "_autoUpscaleTask") != before && Field<CancellationTokenSource>(window, "_autoUpscaleRequest") != null; }, "replacement starts");
        var superseded = Field<Task>(window, "_autoUpscaleTask");
        var cancellation = Field<CancellationTokenSource>(window, "_autoUpscaleRequest");
        slider.Value = 3;
        Assert(cancellation.IsCancellationRequested, "new slider choice cancels in-flight inference immediately");
        Wait(delegate { return superseded.IsCompleted && !timer.IsEnabled && Field<Task>(window, "_autoUpscaleTask") != superseded
            && Field<Task>(window, "_autoUpscaleTask").IsCompleted && !Field<bool>(window, "_imageViewPending"); }, "latest scale wins");
        Assert(((BitmapSource)Field<Image>(window, "_mainImage").Source).PixelWidth == 512 && label.Text == "1.6x", "stale result cannot overwrite latest factor");

        before = Field<Task>(window, "_autoUpscaleTask"); slider.Value = 5;
        Invoke(window, "ToggleAutomaticUpscale"); Pump();
        Assert(!timer.IsEnabled && Field<Task>(window, "_autoUpscaleTask") == before && label.Text == "1.6x", "cancel during debounce keeps applied scale and pixels");
        slider.Value = 6;
        Wait(delegate { return Field<CancellationTokenSource>(window, "_autoUpscaleRequest") != null; }, "scale change before cancel");
        var canceled = Field<Task>(window, "_autoUpscaleTask"); Invoke(window, "ToggleAutomaticUpscale");
        Wait(delegate { return canceled.IsCompleted; }, "cancel running scale change"); canceled.GetAwaiter().GetResult();
        Assert(slider.IsVisible && label.Text == "1.6x" && ((BitmapSource)Field<Image>(window, "_mainImage").Source).PixelWidth == 512,
            "cancel running rebuild retains previous result and factor");

        var limitFixture = BitmapSource.Create(2000, 3000, 96, 96, PixelFormats.Bgra32, null, new byte[2000 * 3000 * 4], 2000 * 4);
        Invoke(window, "SyncUpscaleSize", 1.6, limitFixture);
        Assert(slider.Maximum == Array.IndexOf(SuperResolution.Scales, 2.0), "slider excludes scales above 32 MP output cap");
        Invoke(window, "SyncUpscaleSize", 1.6, native);
        double oldWidth = window.Width; window.Width = 980; Pump();
        Wait(delegate { return !Field<bool>(window, "_imageViewPending"); }, "narrow slider layout");
        AssertInside(group, (FrameworkElement)window.Content); AssertInside(slider, group); AssertInside(label, group);
        Render(window, "upscale-slider-narrow-dark.png");
        ThemeManager.SetDarkTheme(false); Pump(); Render(window, "upscale-slider-narrow-light.png");
        ThemeManager.SetDarkTheme(true); window.Width = oldWidth; Pump();
        Wait(delegate { return !Field<bool>(window, "_imageViewPending"); }, "wide slider layout");
        Render(window, "upscale-slider-wide.png");
    }
}
