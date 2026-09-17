using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static Color UpscaleColor(Brush brush) { return ((SolidColorBrush)brush).Color; }
    private static Color UpscaleThemeColor(string key) { return UpscaleColor((Brush)Application.Current.Resources[key]); }

    private static void CheckUpscaleAppearance(MainWindow window, UpscaleStatus buttonState, UpscaleStatus statusState)
    {
        string backgroundKey = buttonState == UpscaleStatus.Working ? ThemeKeys.UpscaleWorkingBackground
            : buttonState == UpscaleStatus.Ready ? ThemeKeys.UpscaleReadyBackground
            : buttonState == UpscaleStatus.Bypassed ? ThemeKeys.UpscaleBypassedBackground : ThemeKeys.ControlBackground;
        foreach (string name in new[] { "_upscaleButton", "_upscaleModelButton" })
        {
            var button = Field<Button>(window, name); button.ApplyTemplate();
            Assert(UpscaleColor(button.Background) == UpscaleThemeColor(backgroundKey), name + " has " + buttonState + " fill");
            var frame = (Border)button.Template.FindName("Frame", button);
            Assert(UpscaleColor(frame.Background) == UpscaleColor(button.Background), "rendered button preserves state fill");
            Assert(button.Template.Triggers.OfType<Trigger>().Where(t => t.Property == UIElement.IsMouseOverProperty || t.Property == System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty)
                .SelectMany(t => t.Setters.OfType<Setter>()).All(s => s.Property != Border.BackgroundProperty), "hover/press cannot mask enabled color");
        }
        Assert(UpscaleColor(Field<TextBlock>(window, "_transferText").Foreground) == UpscaleThemeColor(UpscalePresentation.TextKey(statusState)), "status color matches " + statusState);
        Assert(Field<UpscaleStatus>(window, "_autoUpscaleStatusKind") == statusState, "status has explicit semantic state");
        if (Field<Slider>(window, "_upscaleSizeSlider").IsVisible)
            Assert(UpscaleColor(Field<TextBlock>(window, "_upscaleSizeText").Foreground) == UpscaleThemeColor(UpscalePresentation.TextKey(buttonState)), "factor color matches applied/pending preview");
    }

    private static void CheckUpscalePaletteContrast()
    {
        foreach (bool dark in new[] { true, false })
        {
            ThemeManager.SetDarkTheme(dark);
            string[] texts = { ThemeKeys.UpscaleWorkingText, ThemeKeys.UpscaleReadyText, ThemeKeys.UpscaleBypassedText, ThemeKeys.UpscaleErrorText };
            Assert(texts.Select(UpscaleThemeColor).Distinct().Count() == 4, "four distinct status colors");
            foreach (string key in texts) CheckUpscaleContrast(UpscaleThemeColor(key), UpscaleThemeColor(ThemeKeys.ToolbarBackground));
            foreach (string key in new[] { ThemeKeys.UpscaleWorkingBackground, ThemeKeys.UpscaleReadyBackground, ThemeKeys.UpscaleBypassedBackground })
                CheckUpscaleContrast(UpscaleThemeColor(ThemeKeys.Text), UpscaleThemeColor(key));
        }
        ThemeManager.SetDarkTheme(true);
    }

    private static void CheckUpscaleContrast(Color foreground, Color background)
    {
        double a = BackgroundLuminance(foreground.R, foreground.G, foreground.B), b = BackgroundLuminance(background.R, background.G, background.B);
        double ratio = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        Assert(ratio >= 4.5, "upscale text contrast >= 4.5:1, actual " + ratio);
    }

    private static void RunUpscaleAppearanceChecks()
    {
        CheckUpscalePaletteContrast();
        string folder = Path.Combine(Root, "upscale-colors-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Preview.png"); MakeImage(path, 320, 200, 96);
        var services = AppServices.CreateInstance(Path.Combine(folder, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = folder, WindowWidth = 1200, WindowHeight = 850, ProcessingGpuKey = "CPU" });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            Invoke(window, "OpenImageTab", path, true); Wait(delegate { return Ready(window, path); }, "appearance source"); WaitScan(window);
            var native = (BitmapSource)Field<Image>(window, "_mainImage").Source;
            CheckUpscaleAppearance(window, UpscaleStatus.None, UpscaleStatus.None);
            Invoke(window, "StartAutomaticUpscale", native, 1.6);
            CheckUpscaleAppearance(window, UpscaleStatus.Working, UpscaleStatus.Working);
            window.UpdateLayout(); Render(window, "upscale-colors-working-dark.png");
            ThemeManager.SetDarkTheme(false); CheckUpscaleAppearance(window, UpscaleStatus.Working, UpscaleStatus.Working);
            window.UpdateLayout(); Render(window, "upscale-colors-working-light.png"); ThemeManager.SetDarkTheme(true);
            var canceled = Field<Task>(window, "_autoUpscaleTask"); Invoke(window, "ToggleAutomaticUpscale"); WaitModelTask(canceled);
            CheckUpscaleAppearance(window, UpscaleStatus.None, UpscaleStatus.Canceled);
            Invoke(window, "StartAutomaticUpscale", native, 1.6); WaitModelTask(Field<Task>(window, "_autoUpscaleTask"));
            Wait(delegate { return !Field<bool>(window, "_imageViewPending"); }, "applied appearance layout");
            CheckUpscaleAppearance(window, UpscaleStatus.Ready, UpscaleStatus.Ready);
            Render(window, "upscale-colors-ready-dark.png");
            ThemeManager.SetDarkTheme(false); Pump(); CheckUpscaleAppearance(window, UpscaleStatus.Ready, UpscaleStatus.Ready);
            Render(window, "upscale-colors-ready-light.png"); ThemeManager.SetDarkTheme(true);
            Field<Slider>(window, "_upscaleSizeSlider").Value = 4;
            CheckUpscaleAppearance(window, UpscaleStatus.Working, UpscaleStatus.Working);
            Invoke(window, "ToggleAutomaticUpscale");
            CheckUpscaleAppearance(window, UpscaleStatus.Ready, UpscaleStatus.Canceled);
            SetInlineUpscaleSize(window, 1); CheckUpscaleAppearance(window, UpscaleStatus.Bypassed, UpscaleStatus.Bypassed);
            Render(window, "upscale-colors-bypass.png");
            Invoke(window, "ToggleAutomaticUpscale"); CheckUpscaleAppearance(window, UpscaleStatus.None, UpscaleStatus.None);
            Invoke(window, "SetUpscaleModel", UpscaleModels.Supir, false);
            Invoke(window, "StartAutomaticUpscale", native, 1.6); WaitModelTask(Field<Task>(window, "_autoUpscaleTask"));
            CheckUpscaleAppearance(window, UpscaleStatus.None, UpscaleStatus.Error);
            Render(window, "upscale-colors-error.png");
            ThemeManager.SetDarkTheme(false); Pump(); CheckUpscaleAppearance(window, UpscaleStatus.None, UpscaleStatus.Error);
            Render(window, "upscale-colors-error-light.png"); ThemeManager.SetDarkTheme(true);
            // An unrelated operation must not inherit the last AI error color.
            typeof(MainWindow).GetField("_transferStatus", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, "Other operation completed");
            Invoke(window, "UpdateBrowserFooter");
            Assert(UpscaleColor(Field<TextBlock>(window, "_transferText").Foreground) == UpscaleThemeColor(ThemeKeys.MutedText), "unrelated footer status restores neutral color");
            Invoke(window, "SetUpscaleModel", UpscaleModels.Basic, false);
            Invoke(window, "StartAutomaticUpscale", native, 1.6);
            var obsolete = Field<Task>(window, "_autoUpscaleTask"); Invoke(window, "BrowseActiveTab"); WaitModelTask(obsolete);
            CheckUpscaleAppearance(window, UpscaleStatus.None, UpscaleStatus.None);
            Assert(!Field<Button>(window, "_upscaleButton").IsEnabled, "leaving viewer clears active color and disables upscale");
        }
        finally { window.Close(); services.Dispose(); ThemeManager.SetDarkTheme(true); Pump(); }
        Console.WriteLine("PASS: upscale state colors, split-button fills, hover/press preservation, live dark/light themes, contrast, cancellation, 1x bypass, errors, unrelated status and stale/navigation reset.");
    }
}
