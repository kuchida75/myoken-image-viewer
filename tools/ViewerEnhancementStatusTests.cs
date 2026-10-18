using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void CheckEnhancementStatusBounds(MainWindow window)
    {
        Pump(); var view = Field<Grid>(window, "_imageView"); var badge = Field<Border>(window, "_enhancementStatusOverlay");
        Assert(badge.IsVisible && !badge.IsHitTestVisible && !badge.Focusable, "Enhancement status is visible and passive"); AssertInside(badge, view);
        var bounds = new Rect(badge.TranslatePoint(new Point(), view), badge.RenderSize);
        AssertNear(bounds.Left, 16, "Enhancement status stays at the image-frame top left"); AssertNear(bounds.Top, 16, "Enhancement status top inset");
        foreach (string field in new[] { "_zoomIndicator", "_manualEnhanceOverlay", "_imageMetadataOverlay", "_imageNavigatorOverlay", "_slideshowOverlay", "_filmstripOverlay" })
        {
            var other = Field<FrameworkElement>(window, field); if (!other.IsVisible) continue;
            var box = new Rect(other.TranslatePoint(new Point(), view), other.RenderSize);
            Assert(!bounds.IntersectsWith(box), "Enhancement status clears " + field);
        }
        Assert(((SolidColorBrush)badge.Background).Color.A > 0 && ((SolidColorBrush)badge.Background).Color.A < 255, "Badge background is translucent, not the text or image");
    }

    private static void RunEnhancementStatusChecks()
    {
        string root = Path.Combine(Root, "enhancement-status-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string a = Path.Combine(photos, "A.png"), b = Path.Combine(photos, "B.png"); MakeImage(a, 1500, 1000, 96); MakeImage(b, 1200, 900, 96);
        byte[] original = File.ReadAllBytes(a); var services = AppServices.CreateInstance(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1440, WindowHeight = 900, SlideshowSeconds = 30 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            var badge = Field<Border>(window, "_enhancementStatusOverlay"); var text = Field<TextBlock>(window, "_enhancementStatusText"); var manual = Field<TextBlock>(window, "_manualStatusText");
            Assert(!badge.IsVisible && Capture(window).EnhancementStatusVisible == true, "Legacy/new profiles default to status enabled, with no badge in the browser");
            Invoke(window, "OpenBrowserImage", a); Wait(delegate { return Ready(window, a); }, "Status fixture opens"); Pump();
            string firstTab = Field<string>(window, "_activeTabId"); Assert(text.Text == "Quick Enhance: Off" && !manual.IsVisible, "Off state is explicit with no unnecessary manual row");
            byte[] pixels = RenderImagePixels(window); Invoke(window, "SetEnhancementStatusVisible", false); Pump();
            Assert(!badge.IsVisible && pixels.SequenceEqual(RenderImagePixels(window)), "Hiding the badge does not alter image pixels, fit or pan"); Invoke(window, "SetEnhancementStatusVisible", true);
            SetEnhance(window, true); Assert(text.Text.Contains("Basic") && text.Text.Contains("processing"), "Quick Enhance processing is reported immediately");
            WaitEnhance(window); Assert(text.Text == "Quick Enhance: Basic (on)", "Applied Basic mode is reported");
            var mode = Field<ComboBox>(window, "_enhanceMode"); mode.SelectedIndex = 1; WaitAdvanced(window);
            Assert(text.Text == "Quick Enhance: Advanced (on)", "Applied Advanced mode is reported");
            Invoke(window, "SetManualEnhanceVisible", true); Assert(manual.Text.Contains("Neutral") && manual.IsVisible, "Opening untouched sliders reports neutral manual adjustments");
            Field<List<Slider>>(window, "_manualSliders")[1].Value = 15; Assert(manual.Text == "Manual adjustments: On", "Exposure changes update status immediately");
            Invoke(window, "SetManualEnhanceVisible", false); Assert(manual.IsVisible && manual.Text.EndsWith("On"), "Closing sliders does not claim their still-applied changes are off");
            var state = ((Dictionary<string, ImageTabState>)Field<Dictionary<string, ImageTabState>>(window, "_tabs"))[firstTab];
            state.SavedEnhanceRevision = Field<FileRevision>(window, "_displayedImageRevision"); Field<Image>(window, "_mainImage").Effect = null;
            Invoke(window, "UpdateEnhanceButton"); Assert(text.Text.Contains("saved") && !text.Text.Contains("(on)"), "Already-saved enhancement is not falsely labeled as an active effect");
            state.SavedEnhanceRevision = null; Invoke(window, "CancelEnhancement");
            typeof(MainWindow).GetField("_enhancementSummary", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, "Enhancement unavailable; test fixture");
            Invoke(window, "UpdateEnhanceButton"); Assert(text.Text.Contains("unavailable"), "Failure does not claim enhancement is active");
            SetEnhance(window, false); mode.SelectedIndex = 0; WaitEnhance(window);
            Invoke(window, "OpenImageTab", b, true); Wait(delegate { return Ready(window, b); }, "Switch status image"); WaitEnhance(window);
            Assert(text.Text.Contains("Basic") && !manual.IsVisible, "Window-wide automatic mode follows tabs but manual status is image-specific");
            Invoke(window, "ActivateImageTab", firstTab); Wait(delegate { return Ready(window, a); }, "Restore adjusted tab"); WaitEnhance(window);
            Assert(manual.Text.EndsWith("On") && manual.IsVisible, "Returning to an adjusted tab restores manual status");
            Invoke(window, "SetManualEnhanceVisible", true); Invoke(window, "SetImageMetadataOverlay", true); WaitOverlayMetadata(window, a);
            Invoke(window, "SetZoomOneToOne");
            foreach (bool dark in new[] { true, false })
            {
                ThemeManager.SetDarkTheme(dark); window.Width = 980; window.Height = 640; Pump(); CheckEnhancementStatusBounds(window); CheckManualLayout(window);
                Render((FrameworkElement)window.Content, dark ? "enhancement-status-dark.png" : "enhancement-status-light.png");
            }
            Invoke(window, "SetCompactMode", true); Pump(); CheckEnhancementStatusBounds(window); Invoke(window, "SetCompactMode", false);
            foreach (string position in MainWindow.FilmstripPositions)
            {
                Invoke(window, "SetFilmstripSettings", true, position); Invoke(window, "ShowFilmstrip"); Pump(); CheckEnhancementStatusBounds(window);
                Invoke(window, "HideFilmstrip");
            }
            Invoke(window, "ToggleFullscreen"); Pump(); CheckEnhancementStatusBounds(window); Invoke(window, "ToggleFullscreen");
            Invoke(window, "ResetManualAdjustments"); Invoke(window, "SetManualEnhanceVisible", false); Assert(!manual.IsVisible, "Reset removes the active-manual marker");
            Invoke(window, "BrowseActiveTab"); WaitScan(window); Assert(!badge.IsVisible, "Returning to thumbnails hides status");
            Invoke(window, "OpenBrowserImage", a); Wait(delegate { return Ready(window, a); }, "Status browser round trip"); WaitEnhance(window);
            var check = Field<CheckBox>(window, "_configureEnhancementStatus"); check.IsChecked = false; Assert(!badge.IsVisible && Capture(window).EnhancementStatusVisible == false, "Configuration opt-out applies immediately");
            var viewMenu = (ContextMenu)Invoke(window, "BuildViewOptionsMenu"); var item = viewMenu.Items.OfType<MenuItem>().Single(m => (string)m.Header == "Enhancement status");
            Assert(!item.IsChecked, "View menu reflects the same preference"); item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert(badge.IsVisible && check.IsChecked == true, "View toggles the same status setting");
            check.IsChecked = false; services.Sessions.SaveNamed("Status hidden", Capture(window));
            string backup = Path.Combine(root, "Settings.json"); ViewerBackupStore.Save(backup, ViewerBackupStore.Create(Capture(window), new BrowserPreferences(), services.Sessions.CaptureNamedSessions()));
            Assert(ViewerBackupStore.Load(backup).Workspace.EnhancementStatusVisible == false && services.Sessions.LoadNamed("Status hidden").EnhancementStatusVisible == false, "Backups and named sessions preserve visibility");
            services.Sessions.Save(Capture(window)); window.Close(); Pump(); window = new MainWindow(services); window.Show();
            Wait(delegate { return Ready(window, a); }, "Status preference restart"); WaitEnhance(window);
            Assert(!Field<Border>(window, "_enhancementStatusOverlay").IsVisible && Capture(window).EnhancementStatusVisible == false, "Automatic restart retains status opt-out");
            Assert(File.ReadAllBytes(a).SequenceEqual(original), "Status changes never write the original image");
        }
        finally { window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true); }
        Console.WriteLine("PASS: passive translucent enhancement status, Off/Basic/Advanced/processing/saved/failure states, live manual/neutral/reset state, tab/browser changes, pixel preservation, overlay/carousel bounds, dark/light/compact/fullscreen, View/Configure and automatic/named/backup persistence.");
    }
}
