using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void PumpZoomFor(int milliseconds)
    {
        var time = Stopwatch.StartNew();
        while (time.ElapsedMilliseconds < milliseconds) Pump();
    }

    private static void CheckZoomIndicatorBounds(MainWindow window)
    {
        var overlay = Field<Border>(window, "_zoomIndicator");
        var view = Field<Grid>(window, "_imageView");
        window.UpdateLayout();
        AssertInside(overlay, view);
        Point corner = overlay.TranslatePoint(new Point(), view);
        AssertNear(corner.X + overlay.ActualWidth, view.ActualWidth - 16, "zoom indicator right inset");
        AssertNear(corner.Y, 16, "zoom indicator top inset");
        var hit = view.InputHitTest(new Point(corner.X + 12, corner.Y + 12));
        Assert(hit != overlay && hit != Field<TextBlock>(window, "_zoomIndicatorText"), "overlay does not capture image gestures");
    }

    private static void RunZoomIndicatorChecks()
    {
        string root = Path.Combine(Root, "zoom-indicator-" + Guid.NewGuid().ToString("N").Substring(0, 10));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string large = Path.Combine(photos, "Large.png"), small = Path.Combine(photos, "Small.png");
        MakeImage(large, 2000, 1400, 96); MakeImage(small, 240, 160, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 780 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        var indicator = Field<Border>(window, "_zoomIndicator");
        var label = Field<TextBlock>(window, "_zoomIndicatorText");
        var timer = Field<DispatcherTimer>(window, "_zoomIndicatorTimer");
        Assert(indicator.Visibility == Visibility.Collapsed && !timer.IsEnabled, "browser has no zoom overlay");
        Assert(!indicator.IsHitTestVisible && !indicator.Focusable, "zoom overlay is passive");
        Assert(((SolidColorBrush)indicator.Background).Color.A > 0 && ((SolidColorBrush)indicator.Background).Color.A < 255,
            "zoom background is translucent");
        Assert(timer.Interval == TimeSpan.FromSeconds(1), "zoom indicator uses one-second timeout");

        Invoke(window, "OpenImageTab", small, true); Wait(delegate { return Ready(window, small); }, "small image at 1x");
        Assert(indicator.Visibility == Visibility.Collapsed, "1x small image remains unobstructed");
        var canvas = Field<Canvas>(window, "_imageCanvas"); canvas.Focus();
        IInputElement focus = Keyboard.FocusedElement;
        Size frameSize = canvas.RenderSize;
        Assert((bool)Invoke(window, "ApplyMouseWheelZoom", new Point(180, 140), 120, true), "right-button wheel zoom accepted");
        window.UpdateLayout();
        Assert(indicator.Visibility == Visibility.Visible && label.Text == "1.15x", "wheel zoom displays magnification");
        Assert(Keyboard.FocusedElement == focus && canvas.RenderSize == frameSize, "zoom overlay does not move layout or focus");
        CheckZoomIndicatorBounds(window);
        PumpZoomFor(550);
        Assert(indicator.Visibility == Visibility.Visible, "indicator does not disappear before one second");
        Invoke(window, "ApplyMouseWheelZoom", new Point(180, 140), 120, true);
        PumpZoomFor(550);
        Assert(indicator.Visibility == Visibility.Visible, "subsequent zoom restarts the timeout");
        Wait(delegate { return indicator.Visibility == Visibility.Collapsed; }, "indicator auto hides after final zoom");
        Assert(!timer.IsEnabled, "expired zoom timer stops");
        Invoke(window, "SetZoomOneToOne");
        Assert(indicator.Visibility == Visibility.Collapsed && !timer.IsEnabled, "1:1 clears overlay immediately");

        PressKey(window, Key.OemMinus);
        Assert(indicator.Visibility == Visibility.Visible && label.Text == "0.87x", "keyboard reduction displayed");
        Invoke(window, "SetZoomOneToOne");
        PressKey(window, Key.OemPlus);
        Assert(label.Text == "1.15x" && indicator.Visibility == Visibility.Visible, "keyboard magnification displayed");
        Invoke(window, "SetZoomOneToOne");
        Invoke(window, "OpenImageTab", large, true); Wait(delegate { return Ready(window, large); }, "large fitted image");
        Assert(Field<ScaleTransform>(window, "_imageScale").ScaleX < 1 && indicator.Visibility == Visibility.Visible, "fit reduction displayed on image entry");
        CheckZoomIndicatorBounds(window);
        Render((FrameworkElement)window.Content, "zoom-indicator-fit-dark.png");
        Invoke(window, "SetZoomOneToOne");
        Invoke(window, "ZoomFromCenter", 2.0);
        Assert(label.Text == "2x" && indicator.Visibility == Visibility.Visible, "exact magnification factor");
        Pump();
        Render((FrameworkElement)window.Content, "zoom-indicator-2x-dark.png");
        var start = new Point(200, 160);
        Assert((bool)Invoke(window, "BeginImagePan", start), "zoomed image can pan");
        var panTime = Stopwatch.StartNew();
        while (panTime.ElapsedMilliseconds < 1400)
        {
            Invoke(window, "UpdateImagePan", new Point(200 + panTime.ElapsedMilliseconds / 20, 160), true); Pump();
        }
        Invoke(window, "EndImagePan");
        Assert(indicator.Visibility == Visibility.Collapsed, "panning does not extend zoom timeout");

        Invoke(window, "SetZoomOneToOne"); PressKey(window, Key.D0);
        Assert(indicator.Visibility == Visibility.Visible && Field<ScaleTransform>(window, "_imageScale").ScaleX < 1, "Fit shortcut displays new factor");
        ThemeManager.SetDarkTheme(false); window.Width = 980; window.Height = 640; Pump();
        Invoke(window, "SetZoomOneToOne"); Invoke(window, "ZoomFromCenter", 1.25);
        CheckZoomIndicatorBounds(window); Render((FrameworkElement)window.Content, "zoom-indicator-light-narrow.png");
        ThemeManager.SetDarkTheme(true); PressKey(window, Key.H); Pump();
        Invoke(window, "ZoomFromCenter", 1.2);
        CheckZoomIndicatorBounds(window); Render((FrameworkElement)window.Content, "zoom-indicator-compact.png");
        PressKey(window, Key.F11); Pump(); Invoke(window, "ZoomFromCenter", 1.1);
        CheckZoomIndicatorBounds(window); Render((FrameworkElement)window.Content, "zoom-indicator-fullscreen.png");
        PressKey(window, Key.Escape); Pump();
        Assert(indicator.Visibility == Visibility.Collapsed && !timer.IsEnabled, "leaving image/fullscreen clears zoom feedback");
        PressKey(window, Key.H); Pump();

        Invoke(window, "OpenImageTab", large, true); Wait(delegate { return Ready(window, large); }, "restore fitted image");
        Invoke(window, "SetZoomOneToOne"); Invoke(window, "ZoomFromCenter", 1.5);
        string tab = Field<string>(window, "_activeTabId");
        Invoke(window, "OpenImageTab", small, true); Wait(delegate { return Ready(window, small); }, "switch to 1x image");
        Assert(indicator.Visibility == Visibility.Collapsed, "previous image factor cannot leak into 1x tab");
        Invoke(window, "ActivateImageTab", tab); Wait(delegate { return Ready(window, large); }, "restore saved zoom tab");
        Assert(indicator.Visibility == Visibility.Visible && label.Text == "1.5x", "restored tab announces saved zoom");
        Invoke(window, "SetImageView", new ImageView { Zoom = 0.9999, X = 0, Y = 0 });
        Assert(label.Text == "0.9999x", "near-1x zoom does not round to a misleading 1x");
        window.Close(); Pump();
        Assert(indicator.Visibility == Visibility.Collapsed && !timer.IsEnabled, "closing releases timer");
        Console.WriteLine("PASS: transient zoom factor, one-second reset/expiry, wheel/keyboard/Fit, passive overlay, pan, tab lifecycle and dark/light/compact/fullscreen geometry.");
    }
}
