using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static BitmapSource BackgroundPixels(ToolbarBackground background, int width, int height, int dpi)
    {
        background.Measure(new Size(width, height)); background.Arrange(new Rect(0, 0, width, height)); background.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width * dpi / 96, height * dpi / 96, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(background); bitmap.Freeze(); return bitmap;
    }

    private static double BackgroundLuminance(byte r, byte g, byte b)
    {
        double[] components = { r / 255.0, g / 255.0, b / 255.0 };
        for (int i = 0; i < 3; i++) components[i] = components[i] <= 0.04045 ? components[i] / 12.92 : Math.Pow((components[i] + 0.055) / 1.055, 2.4);
        return 0.2126 * components[0] + 0.7152 * components[1] + 0.0722 * components[2];
    }

    private static void CheckBackgroundRendering()
    {
        foreach (bool dark in new[] { true, false })
        {
            ThemeManager.SetDarkTheme(dark);
            var brush = (SolidColorBrush)Application.Current.Resources[ThemeKeys.ToolbarBackground];
            var foreground = ((SolidColorBrush)Application.Current.Resources[ThemeKeys.MutedText]).Color;
            var sample = new ToolbarBackground { BaseBrush = brush, HighContrast = false };
            Assert(sample.Pattern == "Crosshatch" && sample.ColorHex == "#6ABFB5" && sample.Intensity == 100,
                "new background uses Crosshatch, Teal and 100 percent intensity");
            sample.Configure("None", ToolbarBackground.DefaultColor, "Right edge", 100);
            byte[] plain = ToolPixels(BackgroundPixels(sample, 900, 108, 96));
            double foregroundLuminance = BackgroundLuminance(foreground.R, foreground.G, foreground.B);
            foreach (string pattern in ToolbarBackground.Patterns.Skip(1)) foreach (string fade in ToolbarBackground.Fades)
            {
                sample.Configure(pattern, dark ? "#FFFFFF" : "#000000", fade, 100);
                byte[] pixels = ToolPixels(BackgroundPixels(sample, 900, 108, 96));
                Assert(!pixels.SequenceEqual(plain), "rendered pattern/fade differs from plain background " + pattern + "/" + fade);
                double minimumContrast = Double.MaxValue;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    double luminance = BackgroundLuminance(pixels[i + 2], pixels[i + 1], pixels[i]);
                    minimumContrast = Math.Min(minimumContrast, (Math.Max(luminance, foregroundLuminance) + 0.05)
                        / (Math.Min(luminance, foregroundLuminance) + 0.05));
                    Assert(pixels[i + 3] == 255, "background remains opaque");
                }
                Assert(minimumContrast >= 4.5, "muted text contrast at maximum intensity " + pattern + "/" + fade + ": " + minimumContrast);
            }
            sample.Configure("Facets", "#FFFFFF", "Right edge", 0);
            Assert(ToolPixels(BackgroundPixels(sample, 900, 108, 96)).SequenceEqual(plain), "zero intensity is original toolbar");
            sample.Configure("Facets", "#FFFFFF", "Right edge", 100); sample.HighContrast = true;
            Assert(ToolPixels(BackgroundPixels(sample, 900, 108, 96)).SequenceEqual(plain), "high contrast suppresses decoration without changing settings");
            sample.HighContrast = false;
            Assert(!ToolPixels(BackgroundPixels(sample, 900, 108, 96)).SequenceEqual(plain), "leaving high contrast restores decoration");
            BitmapSource highDpi = BackgroundPixels(sample, 900, 108, 192);
            Assert(highDpi.PixelWidth == 1800 && highDpi.PixelHeight == 216 && ToolPixels(highDpi).Distinct().Count() > 3,
                "native geometry renders at 200 percent DPI without a raster asset");
            sample.Configure("invalid", "bad", "unknown", Double.NaN);
            Assert(sample.Pattern == "Crosshatch" && sample.ColorHex == ToolbarBackground.DefaultColor && sample.Fade == "Right edge"
                && sample.Intensity == ToolbarBackground.DefaultIntensity, "invalid ordinary session values normalize safely");

            var gallery = new StackPanel { Width = 1500, Background = brush };
            string[] palette = { "#B9C2CC", "#74A9E4", ToolbarBackground.DefaultColor, "#E5B663", "#D98BA4", "#93C890" };
            for (int i = 0; i < ToolbarBackground.Patterns.Length; i++)
            {
                var row = new Grid { Height = 80 };
                var background = new ToolbarBackground { HighContrast = false };
                background.Configure(ToolbarBackground.Patterns[i], palette[i], "Right edge", 100);
                row.Children.Add(background);
                var label = new TextBlock { Text = ToolbarBackground.Patterns[i], FontSize = 14, Margin = new Thickness(18), VerticalAlignment = VerticalAlignment.Center };
                ThemeManager.Bind(label, TextBlock.ForegroundProperty, ThemeKeys.Text); row.Children.Add(label); gallery.Children.Add(row);
            }
            gallery.Measure(new Size(1500, 480)); gallery.Arrange(new Rect(0, 0, 1500, 480)); gallery.UpdateLayout();
            Render(gallery, dark ? "background-theme-gallery-dark.png" : "background-theme-gallery-light.png");
        }
        var motifs = (Dictionary<string, Geometry>)typeof(ToolbarBackground).GetField("Motifs", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Assert(motifs.Count == 4 && motifs.Values.All(geometry => geometry.IsFrozen), "bounded shared frozen motif geometry");
        ThemeManager.SetDarkTheme(true);
    }

    private static void RunBackgroundThemeChecks()
    {
        Directory.CreateDirectory(Root); CheckBackgroundRendering();
        string root = Path.Combine(Root, "background-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string photo = Path.Combine(photos, "Sample.png"); MakeImage(photo, 2000, 1300, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1760, WindowHeight = 820 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        var background = Field<ToolbarBackground>(window, "_toolbarBackground");
        var toolbar = Field<FrameworkElement>(window, "_topToolbar");
        Assert(background.Pattern == "Crosshatch" && background.ColorHex == "#6ABFB5" && background.Intensity == 100 && background.Fade == "Right edge",
            "missing session preferences use Crosshatch, Teal and 100 percent intensity");
        Assert((string)Field<ComboBox>(window, "_backgroundPattern").SelectedItem == "Crosshatch"
            && Field<Slider>(window, "_backgroundIntensity").Value == 100
            && Field<Dictionary<string, ToggleButton>>(window, "_backgroundSwatches")["#6ABFB5"].IsChecked == true,
            "Configure reflects the new defaults");
        Assert(Capture(window).BackgroundPattern == "Crosshatch" && Capture(window).BackgroundIntensity == 100,
            "new defaults are captured in session settings");
        Render(toolbar, "background-default-toolbar-dark.png");
        ThemeManager.SetDarkTheme(false); Pump(); Render(toolbar, "background-default-toolbar-light.png");
        ThemeManager.SetDarkTheme(true); Pump();
        Assert(!background.IsHitTestVisible && !background.Focusable, "decoration never takes input");
        Invoke(window, "OpenImageTab", photo, true); Wait(delegate { return Ready(window, photo); }, "theme test image");
        var canvas = Field<Canvas>(window, "_imageCanvas");
        var image = Field<Image>(window, "_mainImage");
        var source = image.Source; Size frame = canvas.RenderSize; Size toolbarSize = toolbar.RenderSize;
        var originalView = Capture(window).Tabs.Single();
        Field<TextBox>(window, "_searchBox").Focus(); IInputElement focus = Keyboard.FocusedElement;
        Invoke(window, "SetBackgroundTheme", "Contours", ToolbarBackground.DefaultColor, "Right edge", 100.0); Pump();
        Assert(ReferenceEquals(source, image.Source) && canvas.RenderSize == frame && toolbar.RenderSize == toolbarSize
            && Keyboard.FocusedElement == focus, "background changes do not reload photos, alter layout or steal focus");
        AssertNear(Capture(window).Tabs.Single().Zoom, originalView.Zoom, "background preserves zoom");
        Render((FrameworkElement)window.Content, "background-themed-viewer-dark.png");
        Render(toolbar, "background-themed-toolbar-dark.png");
        Point buttonAt = Field<Button>(window, "_configureButton").TranslatePoint(new Point(10, 10), toolbar);
        Assert(toolbar.InputHitTest(buttonAt) != background, "toolbar commands stay above the background");
        ThemeManager.SetDarkTheme(false); Pump();
        Assert(((SolidColorBrush)background.BaseBrush).Color == ((SolidColorBrush)Application.Current.Resources[ThemeKeys.ToolbarBackground]).Color,
            "background follows light theme immediately");
        Render((FrameworkElement)window.Content, "background-themed-viewer-light.png");
        ThemeManager.SetDarkTheme(true); window.Width = 980; window.Height = 640; Pump();
        AssertInside(Field<Button>(window, "_configureButton"), toolbar);
        Render((FrameworkElement)window.Content, "background-themed-viewer-narrow.png");
        canvas.Focus(); PressKey(window, Key.U); Pump();
        Assert(!background.IsVisible, "compact hides entire themed toolbar");
        PressKey(window, Key.U); Pump(); Assert(background.IsVisible && background.Pattern == "Contours", "normal restores chosen background");
        PressKey(window, Key.F11); Pump();
        AssertInside(background, (FrameworkElement)window.Content);
        Render((FrameworkElement)window.Content, "background-themed-viewer-ultrawide.png");
        PressKey(window, Key.F11); Pump();

        Invoke(window, "ShowConfigure"); Invoke(window, "SelectConfigurationPage", 4); Pump();
        var configure = Field<Window>(window, "_configureWindow");
        var pattern = Field<ComboBox>(window, "_backgroundPattern");
        var fade = Field<ComboBox>(window, "_backgroundFade");
        var intensity = Field<Slider>(window, "_backgroundIntensity");
        var preview = Field<ToolbarBackground>(window, "_backgroundPreview");
        Assert(pattern.Items.Count == 6 && fade.Items.Count == 3, "all original pattern/fade choices available");
        pattern.SelectedItem = "Ribbons"; fade.SelectedItem = "Upper right"; intensity.Value = 85;
        var colors = Field<Dictionary<string, ToggleButton>>(window, "_backgroundSwatches");
        colors["#D98BA4"].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
        Assert(background.Pattern == "Ribbons" && background.ColorHex == "#D98BA4" && background.Intensity == 85 && background.Fade == "Upper right",
            "settings immediately change toolbar");
        Assert(preview.Pattern == background.Pattern && preview.ColorHex == background.ColorHex && preview.Fade == background.Fade
            && preview.Intensity == background.Intensity && colors.Values.Count(button => button.IsChecked == true) == 1, "preview and exclusive selected swatch stay in sync");
        pattern.Focus(); PressKey(window, Key.U); Assert(!Field<bool>(window, "_isCompactMode"), "Configure retains keyboard input");
        Render((FrameworkElement)configure.Content, "background-configure-dark.png");
        ThemeManager.SetDarkTheme(false); Pump(); Render((FrameworkElement)configure.Content, "background-configure-light.png");
        configure.Width = 570; configure.Height = 360; Pump();
        foreach (var button in Field<List<ToggleButton>>(window, "_configurationNavigation"))
        {
            AssertInside(button, (FrameworkElement)configure.Content);
            string caption = ((StackPanel)button.Content).Children.OfType<TextBlock>().Last().Text;
            var text = new FormattedText(caption, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), button.FontSize, Brushes.Black,
                VisualTreeHelper.GetDpi(button).PixelsPerDip);
            Assert(text.Width + 32 < button.ActualWidth, "configuration navigation labels and icons fit at minimum width");
        }
        Field<ScrollViewer>(window, "_configurationScroll").ScrollToBottom(); Pump();
        var page = Field<List<StackPanel>>(window, "_configurationPages")[4];
        var reset = ((Grid)page.Children[page.Children.Count - 1]).Children.OfType<Button>().Single();
        AssertInside(reset, (FrameworkElement)configure.Content); reset.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert(background.Pattern == "Crosshatch" && intensity.IsEnabled && fade.IsEnabled && background.ColorHex == "#6ABFB5"
            && intensity.Value == 100 && background.Intensity == 100 && background.Fade == "Right edge"
            && preview.Pattern == "Crosshatch" && preview.Intensity == 100,
            "reset restores Crosshatch, Teal, right-edge fade and full intensity in both toolbar and preview");
        pattern.SelectedItem = "None";
        Assert(background.Pattern == "None" && !intensity.IsEnabled && !fade.IsEnabled, "None still disables decoration controls");
        pattern.SelectedItem = "Facets"; intensity.Value = 70; fade.SelectedItem = "Left edge";
        CloseViewerSettings(window); ThemeManager.SetDarkTheme(true);
        Invoke(window, "SetBackgroundTheme", "Facets", "#C58ADE", "Left edge", 70.0);
        Assert(Capture(window).BackgroundColor == "#C58ADE", "custom RGB color captured");
        services.Sessions.SaveNamed("Background theme", Capture(window));
        string backup = Path.Combine(root, "backup.json");
        WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
        Assert(ViewerBackupStore.Load(backup).Workspace.BackgroundPattern == "Facets"
            && services.Sessions.LoadNamed("Background theme").BackgroundIntensity == 70, "named session and backup retain settings");
        Invoke(window, "SetBackgroundTheme", "None", ToolbarBackground.DefaultColor, "Right edge", 65.0);
        WaitBackup((Task<int>)Invoke(window, "ImportBackupFileAsync", backup, false));
        Wait(delegate { return Ready(window, photo); }, "background backup restore");
        Assert(background.Pattern == "Facets" && background.ColorHex == "#C58ADE" && background.Fade == "Left edge" && background.Intensity == 70,
            "backup restores all background settings");
        foreach (int bad in Enumerable.Range(0, 4))
        {
            var document = ViewerBackupStore.Load(backup);
            if (bad == 0) document.Workspace.BackgroundPattern = "invalid";
            if (bad == 1) document.Workspace.BackgroundColor = "#NOTRGB";
            if (bad == 2) document.Workspace.BackgroundFade = "unknown";
            if (bad == 3) document.Workspace.BackgroundIntensity = Double.NaN;
            bool rejected = false;
            try { ViewerBackupStore.Save(Path.Combine(root, "bad.json"), document); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "invalid backup background rejected " + bad);
        }
        using (var otherServices = AppServices.Create(Path.Combine(root, "OtherProfile")))
        {
            otherServices.Sessions.Save(new SessionState { LastFolder = photos });
            var other = new MainWindow(otherServices); other.Show(); WaitScan(other);
            Assert(Field<ToolbarBackground>(other, "_toolbarBackground").Pattern == "Crosshatch" && background.Pattern == "Facets", "background preference is isolated per window");
            Invoke(other, "SetBackgroundTheme", "None", "#74A9E4", "Left edge", 0.0);
            otherServices.Sessions.SaveNamed("Plain background", Capture(other));
            string plainBackup = Path.Combine(root, "plain-backup.json");
            WaitBackup((Task)Invoke(other, "ExportBackupFileAsync", plainBackup));
            other.Close(); Pump();
            other = new MainWindow(otherServices); other.Show(); WaitScan(other);
            var plainBackground = Field<ToolbarBackground>(other, "_toolbarBackground");
            Assert(plainBackground.Pattern == "None" && plainBackground.ColorHex == "#74A9E4" && plainBackground.Fade == "Left edge"
                && plainBackground.Intensity == 0, "explicit saved None, zero intensity and custom preferences survive upgrade/restart");
            Invoke(other, "SetBackgroundTheme", "Crosshatch", ToolbarBackground.DefaultColor, "Right edge", 100.0);
            WaitBackup((Task<int>)Invoke(other, "ImportBackupFileAsync", plainBackup, false)); WaitScan(other);
            Assert(plainBackground.Pattern == "None" && plainBackground.Intensity == 0
                && otherServices.Sessions.LoadNamed("Plain background").BackgroundPattern == "None", "backup and named sessions preserve explicit plain background");
            other.Close(); Pump();
        }
        window.Close(); Pump(); window = new MainWindow(services); window.Show(); Wait(delegate { return Ready(window, photo); }, "background session restart");
        background = Field<ToolbarBackground>(window, "_toolbarBackground");
        Assert(background.Pattern == "Facets" && background.ColorHex == "#C58ADE" && background.Fade == "Left edge" && background.Intensity == 70,
            "automatic session restores background");
        window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true);
        Console.WriteLine("PASS: original toolbar patterns/colors/fades, contrast/pixel checks, passive DPI-aware geometry, live Configure preview, narrow/light/dark/compact/ultrawide layout and session/backup isolation.");
    }
}
