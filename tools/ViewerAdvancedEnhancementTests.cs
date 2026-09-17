using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunAdvancedEnhancementChecks()
    {
        string root = Path.Combine(Root, "ae-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        string photos = Path.Combine(root, "Photos");
        Directory.CreateDirectory(photos);
        string path = Path.Combine(photos, "Regions.png"), dark = Path.Combine(photos, "Dark.png");
        const int width = 384, height = 192;
        var pixels = new byte[width * height * 4];
        var mask = new SubjectMask { Width = width, Height = height, IsPerson = true, Values = new float[width * height], Description = "Test mask" };
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            byte value = (byte)(x < width / 2 ? 16 + y * 40 / height : 185 + y * 65 / height);
            pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = value;
            pixels[i * 4 + 3] = 255;
            mask.Values[i] = x < width / 2 ? 1 : 0;
        }
        BitmapSource source = AdvancedBitmap(width, height, pixels);
        SaveAdvancedBitmap(source, path);
        MakeEnhanceFixture(dark, "dark");
        var basic = QuickEnhance.Analyze(source, 8, CancellationToken.None);
        var regional = AdvancedQuickEnhance.AnalyzeRegions(source, basic, mask, 8, CancellationToken.None);
        Assert(regional.HasSubject && regional.HasPerson && regional.Mask.IsFrozen && regional.ToneTable.IsFrozen, "regional analysis has immutable subject and tone textures");
        Assert(regional.Subject.ExposureStops > regional.Background.ExposureStops + 0.3, "subject and background exposure are independently analyzed");
        Assert(regional.Subject.Shadows > regional.Background.Shadows && regional.Background.Highlights > regional.Subject.Highlights,
            "dark subject lifts shadows while bright background compresses highlights");
        foreach (RegionalTone tone in new[] { regional.Subject, regional.Background })
        {
            Assert(tone.Map(0) == 0 && tone.Map(1) == 1, "tone curves preserve black and white endpoints");
            double previous = 0;
            for (int i = 0; i <= 1024; i++)
            {
                double value = tone.Map(i / 1024.0);
                Assert(value >= previous && value <= 1 && !Double.IsNaN(value), "tone curves are bounded and monotonic");
                previous = value;
            }
        }
        byte[] rendered = RenderAdvanced(source, regional);
        int left = (height / 2 * width + width / 4) * 4, right = (height / 2 * width + width * 3 / 4) * 4;
        Assert(rendered[left] > pixels[left] + 8, "rendered subject shadows brighten");
        Assert(rendered[right] < pixels[right] - 8, "rendered background highlights darken");
        Assert(rendered[left] == rendered[left + 1] && rendered[left] == rendered[left + 2], "neutral colors remain neutral");
        var serial = AdvancedQuickEnhance.AnalyzeRegions(source, basic, mask, 1, CancellationToken.None);
        Assert(BitmapBytes(regional.Mask).SequenceEqual(BitmapBytes(serial.Mask)) && BitmapBytes(regional.ToneTable).SequenceEqual(BitmapBytes(serial.ToneTable)),
            "parallel region analysis is deterministic");
        CheckAdvancedSkinAndTransparency();
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel(); bool stopped = false;
            try { AdvancedQuickEnhance.AnalyzeRegions(source, basic, mask, 8, canceled.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Assert(stopped, "Advanced analysis honors cancellation");
        }
        using (var detector = new SubjectSegmentation(4, false))
        {
            var detection = Task.Run(delegate { return detector.Detect(source, CancellationToken.None); });
            WaitAdvancedTask(detection);
            Assert(detection.Result != null && detection.Result.Description != null, "bundled ONNX models execute on CPU");
            Console.WriteLine("PASS: CPU model execution: " + detection.Result.Description);
        }
        using (var detector = new SubjectSegmentation(4, false, Path.Combine(root, "MissingModels")))
        {
            var fallback = Task.Run(delegate { return AdvancedQuickEnhance.Analyze(source, 4, detector, CancellationToken.None); });
            WaitAdvancedTask(fallback);
            Assert(!fallback.Result.HasSubject && fallback.Result.DetectionSummary.Contains("unavailable"), "missing model degrades to labeled global adjustments");
            Assert(RenderAdvanced(source, fallback.Result).Any(v => v != 0), "model failure still produces a visible enhanced image");
        }
        string badModels = Path.Combine(root, "BadModels"); Directory.CreateDirectory(badModels);
        File.WriteAllBytes(Path.Combine(badModels, "humanseg.onnx"), new byte[] { 0, 1, 2, 3 });
        using (var detector = new SubjectSegmentation(4, false, badModels))
        {
            var fallback = Task.Run(delegate { return AdvancedQuickEnhance.Analyze(source, 4, detector, CancellationToken.None); });
            WaitAdvancedTask(fallback);
            Assert(!fallback.Result.HasSubject && fallback.Result.DetectionSummary.Contains("checksum mismatch"), "corrupt model is rejected before inference");
        }

        string portrait = Path.Combine(Root, "advanced-portraits", "astronaut.png");
        if (File.Exists(portrait))
        {
            var image = new BitmapImage(new Uri(portrait)); image.Freeze();
            using (var detector = new SubjectSegmentation(8))
            {
                var detection = Task.Run(delegate { return detector.Detect(image, CancellationToken.None); });
                WaitAdvancedTask(detection);
                Assert(detection.Result.Values != null && detection.Result.IsPerson, "real portrait is segmented as a person");
                Assert(detection.Result.Values.Max() > 0.9 && detection.Result.Values.Min() < 0.1, "portrait has distinct person/background confidence");
                Console.WriteLine("PASS: real portrait segmentation: " + detection.Result.Description);
            }
            File.Copy(portrait, Path.Combine(photos, "Portrait.png"));
        }

        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 780 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        var mode = Field<ComboBox>(window, "_enhanceMode");
        Assert(mode.SelectedIndex == 0 && !Capture(window).AdvancedQuickEnhance, "legacy configuration defaults to Basic");
        mode.SelectedIndex = 1;
        Assert(Capture(window).AdvancedQuickEnhance && Capture(window).QuickEnhanceEnabled == true, "Advanced can be enabled in browser mode");
        Assert(Field<Image>(window, "_mainImage").Effect == null, "Advanced does not affect thumbnail images");
        Invoke(window, "OpenBrowserImage", path); Wait(delegate { return Ready(window, path); }, "Advanced image opens");
        WaitAdvanced(window);
        var main = Field<Image>(window, "_mainImage");
        BitmapSource originalSource = (BitmapSource)main.Source;
        Assert(main.Effect is AdvancedEnhanceEffect, "Advanced effect applied in viewer");
        Assert(Field<string>(window, "_enhancementSummary").IndexOf("sharpening", StringComparison.OrdinalIgnoreCase) >= 0,
            "Advanced tooltip reports regional sharpening or the basic fallback");
        FocusViewerSetting(window, mode);
        Assert(mode.IsKeyboardFocusWithin, "mode dropdown takes keyboard focus");
        PressKey(window, Key.Right); PressKey(window, Key.End); PressKey(window, Key.Enter);
        Assert(Ready(window, path), "mode dropdown owns navigation keys without changing images");
        CloseViewerSettings(window);
        Field<Canvas>(window, "_imageCanvas").Focus();
        SetEnhance(window, false); Pump();
        byte[] originalView = RenderImagePixels(window);
        Assert(main.Effect == null && Field<SubjectSegmentation>(window, "_subjectSegmentation") == null, "off restores original and releases inference sessions");
        SetEnhance(window, true); WaitAdvanced(window);
        Assert(Object.ReferenceEquals(originalSource, main.Source), "Advanced reuses original decoded bitmap");
        SetEnhance(window, false); Pump();
        Assert(originalView.SequenceEqual(RenderImagePixels(window)), "Advanced off returns pixel-identical original view");
        SetEnhance(window, true);
        mode.SelectedIndex = 0; WaitEnhance(window);
        Assert(main.Effect is QuickEnhanceEffect && !Capture(window).AdvancedQuickEnhance, "Basic selection cancels stale Advanced work");
        mode.SelectedIndex = 1;
        Invoke(window, "OpenImageTab", dark, true); Wait(delegate { return Ready(window, dark); }, "tab switch during segmentation"); WaitAdvanced(window);
        Assert(main.Effect is AdvancedEnhanceEffect && Capture(window).AdvancedQuickEnhance, "Advanced follows all tabs in this window");
        Field<Canvas>(window, "_imageCanvas").Focus(); PressKey(window, Key.Enter); Pump();
        Assert(Capture(window).QuickEnhanceEnabled == true && Capture(window).AdvancedQuickEnhance && main.Effect == null, "browser return retains Advanced preference");
        Invoke(window, "OpenBrowserImage", path); Wait(delegate { return Ready(window, path); }, "Advanced browser reopen"); WaitAdvanced(window);
        var snapshot = Capture(window); services.Sessions.SaveNamed("Advanced", snapshot);
        Assert(services.Sessions.LoadNamed("Advanced").AdvancedQuickEnhance, "named sessions retain Advanced mode");
        string backup = Path.Combine(root, "backup.json");
        WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
        Assert(ViewerBackupStore.Load(backup).Workspace.AdvancedQuickEnhance, "backup exports Advanced mode");
        mode.SelectedIndex = 0; WaitEnhance(window);
        WaitBackup((Task)Invoke(window, "ImportBackupFileAsync", backup, false)); WaitScan(window); WaitAdvanced(window);
        Assert(Capture(window).AdvancedQuickEnhance, "backup import restores Advanced mode");

        if (File.Exists(Path.Combine(photos, "Portrait.png")))
        {
            Invoke(window, "OpenBrowserImage", Path.Combine(photos, "Portrait.png"));
            Wait(delegate { return Ready(window, Path.Combine(photos, "Portrait.png")); }, "portrait viewer"); WaitAdvanced(window);
            Render((FrameworkElement)window.Content, "advanced-portrait-dark.png");
            SetEnhance(window, false); Pump(); Render((FrameworkElement)window.Content, "advanced-portrait-original.png");
            SetEnhance(window, true); WaitAdvanced(window);
            window.Width = 980; window.Height = 640; Pump();
            CheckCentered(window);
            Render((FrameworkElement)window.Content, "advanced-portrait-narrow.png");
            Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = false; Pump();
            Render((FrameworkElement)window.Content, "advanced-portrait-light.png");
        }
        window.Close(); Pump();
        window = new MainWindow(services); window.Show(); WaitScan(window); WaitAdvanced(window);
        Assert(Capture(window).AdvancedQuickEnhance && Field<ComboBox>(window, "_enhanceMode").SelectedIndex == 1, "Advanced mode survives restart");
        SetEnhance(window, false); SetEnhance(window, true); window.Close(); Pump();
        Assert(File.ReadAllBytes(path).Length > 0 && BitmapBytes(new BitmapImage(new Uri(path))).SequenceEqual(pixels), "Advanced never writes original files");
    }

    private static void CheckAdvancedSkinAndTransparency()
    {
        Assert(AdvancedQuickEnhance.SkinProtection(0.72, 0.52, 0.41) > 0.8, "warm light complexion protected");
        Assert(AdvancedQuickEnhance.SkinProtection(0.24, 0.16, 0.12) > 0.8, "warm dark complexion protected without brightness classification");
        Assert(AdvancedQuickEnhance.SkinProtection(0.16, 0.24, 0.38) < 0.01, "blue colors not classified as skin-like hue");
        const int width = 256, height = 96;
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            double shade = 0.25 + y / (double)height * 0.7;
            pixels[i + 2] = (byte)((x < 128 ? 0.72 : 0.41) * shade * 255);
            pixels[i + 1] = (byte)(0.52 * shade * 255);
            pixels[i] = (byte)((x < 128 ? 0.41 : 0.72) * shade * 255);
            pixels[i + 3] = (byte)(x < 8 ? 0 : (x < 16 ? 128 : 255));
        }
        BitmapSource bitmap = AdvancedBitmap(width, height, pixels);
        var analysis = AdvancedQuickEnhance.AnalyzeRegions(bitmap, QuickEnhance.Analyze(bitmap, 4, CancellationToken.None),
            new SubjectMask { Description = "Global test" }, 4, CancellationToken.None);
        byte[] result = RenderAdvanced(bitmap, analysis);
        for (int i = 0; i < pixels.Length; i += 4) Assert(result[i + 3] == pixels[i + 3], "Advanced preserves transparency");
        int skin = (height / 2 * width + 64) * 4, blue = (height / 2 * width + 192) * 4;
        byte[] masks = BitmapBytes(analysis.Mask);
        Assert(masks[skin + 1] > masks[blue + 1] + 100 && masks[skin] < masks[blue], "skin mask reduces local vibrance relative to blue");
        double beforeHue = Hue(pixels[skin + 2], pixels[skin + 1], pixels[skin]);
        double afterHue = Hue(result[skin + 2], result[skin + 1], result[skin]);
        Assert(Math.Abs(beforeHue - afterHue) < 3, "skin hue remains stable after tone adjustment");
        var blank = AdvancedBitmap(1, 1, new byte[4]);
        var empty = AdvancedQuickEnhance.AnalyzeRegions(blank, new EnhancementAnalysis(), null, 1, CancellationToken.None);
        Assert(RenderAdvanced(blank, empty).All(v => v == 0), "fully transparent tiny image remains transparent");
    }

    private static double Hue(double r, double g, double b) { return 60 * (g - b) / Math.Max(1, r - b); }
    private static BitmapSource AdvancedBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); bitmap.Freeze(); return bitmap;
    }
    private static byte[] BitmapBytes(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(pixels, converted.PixelWidth * 4, 0); return pixels;
    }
    private static void SaveAdvancedBitmap(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
    }
    private static byte[] RenderAdvanced(BitmapSource bitmap, AdvancedEnhancementAnalysis analysis)
    {
        var image = new Image { Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Stretch = Stretch.Fill, Effect = new AdvancedEnhanceEffect(analysis, bitmap) };
        image.Measure(new Size(image.Width, image.Height)); image.Arrange(new Rect(0, 0, image.Width, image.Height)); image.UpdateLayout();
        var rendered = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32); rendered.Render(image);
        return BitmapBytes(rendered);
    }
    private static void WaitAdvanced(MainWindow window)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(90);
        while (Field<CancellationTokenSource>(window, "_enhancementRequest") != null && DateTime.UtcNow < limit) Pump();
        Assert(Field<Image>(window, "_mainImage").Effect is AdvancedEnhanceEffect, "Advanced effect applied: " + Field<string>(window, "_enhancementSummary")); Pump();
    }
    private static void WaitAdvancedTask(Task task)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(90);
        while (!task.IsCompleted && DateTime.UtcNow < limit) Pump();
        Assert(task.IsCompleted, "native segmentation completes within test timeout"); task.GetAwaiter().GetResult();
    }
}
