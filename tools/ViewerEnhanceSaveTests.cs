using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ImageMagick;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void ToggleOverwritePreference(CheckBox checkBox)
    {
        var peer = new CheckBoxAutomationPeer(checkBox);
        var toggle = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
        // Exercise WPF's accessibility toggle, which changes IsChecked without raising Click.
        toggle.Toggle();
    }

    private static void RunOverwritePreferenceChecks()
    {
        string root = Path.Combine(Root, "overwrite-setting-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string photo = Path.Combine(photos, "Example.png"); MakeImage(photo, 640, 480, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 920 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            Invoke(window, "OpenImageTab", photo, true); Wait(delegate { return Ready(window, photo); }, "overwrite preference image");
            var confirm = Field<CheckBox>(window, "_confirmEnhanceOverwriteCheckBox");
            FocusViewerSetting(window, confirm); confirm.BringIntoView(); Pump();
            ToggleOverwritePreference(confirm);
            Assert(confirm.IsChecked == false, "native toggle clears the visible checkbox");
            Assert(!Field<bool>(window, "_confirmEnhanceOverwrite"), "native unchecked checkbox immediately updates Save confirmation policy");
            Assert(!((Microsoft.Win32.SaveFileDialog)Invoke(window, "CreateEnhanceSaveDialog")).OverwritePrompt, "native toggle updates Save As policy");
            Invoke(window, "UpdateConfigurationStatus"); Pump();
            Assert(confirm.IsChecked == false && Capture(window).ConfirmEnhanceOverwrite == false, "status refresh cannot undo native checkbox toggle");
            Render((FrameworkElement)Field<Window>(window, "_configureWindow").Content, "overwrite-setting-off-dark.png");
            CloseViewerSettings(window);
            Wait(delegate { return services.Sessions.Load().ConfirmEnhanceOverwrite == false; }, "overwrite opt-out reaches automatic session");
            Invoke(window, "SetManualEnhanceVisible", true);
            Field<List<Slider>>(window, "_manualSliders")[1].Value = 12;
            byte[] original = File.ReadAllBytes(photo);
            Field<Button>(window, "_manualSaveButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var save = Field<Task<EnhancedSaveResult>>(window, "_enhanceSaveTask"); WaitBackup(save);
            Assert(save.Result != null && original.SequenceEqual(File.ReadAllBytes(save.Result.Backup)), "Save skips confirmation after native toggle and retains original backup");
            Wait(delegate { return Ready(window, photo); }, "native opt-out save reload");
            window.Close(); Pump(); window = new MainWindow(services); window.Show();
            Wait(delegate { return Ready(window, photo); }, "native toggle automatic restore");
            confirm = Field<CheckBox>(window, "_confirmEnhanceOverwriteCheckBox");
            Assert(confirm.IsChecked == false && !Field<bool>(window, "_confirmEnhanceOverwrite"), "restart retains native opt-out");
            FocusViewerSetting(window, confirm); confirm.BringIntoView(); Pump();
            ToggleOverwritePreference(confirm);
            Assert(confirm.IsChecked == true && Field<bool>(window, "_confirmEnhanceOverwrite"), "native toggle can re-enable confirmations");
            confirm.IsChecked = false;
            Assert(!Field<bool>(window, "_confirmEnhanceOverwrite"), "direct checked-state update stays synchronized without Click");
            Invoke(window, "UpdateConfigurationStatus");
            Assert(confirm.IsChecked == false, "refresh preserves direct checkbox change");
            confirm.IsChecked = null;
            Assert(Field<bool>(window, "_confirmEnhanceOverwrite"), "indeterminate state conservatively asks for confirmation");
            confirm.IsChecked = false;
            ThemeManager.SetDarkTheme(false); Pump();
            Render((FrameworkElement)Field<Window>(window, "_configureWindow").Content, "overwrite-setting-off-light.png");
        }
        finally { window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true); }
        Console.WriteLine("PASS: native overwrite checkbox toggles, immediate Save/Save As policy, refresh, actual backed-up save and automatic restart.");
    }

    private static EnhancedSaveRequest SaveFixture(string source, string target, ManualAdjustments manual, int turns = 0)
    {
        return new EnhancedSaveRequest { SourcePath = source, Destination = target, SourceRevision = FileRevision.Read(source),
            DestinationRevision = FileRevision.Read(target), AllowOverwrite = File.Exists(target), Pixels = ReadToolImage(source), Manual = manual, Rotation = turns };
    }

    private static EnhancedSaveResult RunFixtureSave(EnhancedSaveRequest request, CancellationToken token)
    { var task = EnhancedImageStore.SaveAsync(request, token, null); WaitBackup(task); return task.GetAwaiter().GetResult(); }

    private static void AssertPixelsNear(BitmapSource first, BitmapSource second, double tolerance, string message)
    {
        Assert(first.PixelWidth == second.PixelWidth && first.PixelHeight == second.PixelHeight, message + " dimensions");
        byte[] a = ToolPixels(first), b = ToolPixels(second);
        double error = a.Select((v, i) => Math.Abs(v - b[i])).Average();
        Assert(error <= tolerance, message + ": average error " + error + "; middle pixel "
            + String.Join(",", a.Skip(a.Length / 2).Take(4)) + " / " + String.Join(",", b.Skip(b.Length / 2).Take(4)));
    }

    private static void RunEnhanceSaveChecks()
    {
        string root = Path.Combine(Root, "enhance-save-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string photo = Path.Combine(photos, "A.png"); MakeImage(photo, 1200, 800, 300);
        var values = new ManualAdjustments(); values.Values[0] = 20; values.Values[1] = 18; values.Values[7] = 30; values.Values[9] = 25;
        BitmapSource original = ReadToolImage(photo);
        var quick = new QuickEnhanceEffect(new EnhancementAnalysis { ExposureStops = 0.2, Sharpness = 0.3, NoiseThreshold = 0.004 }, original); quick.Freeze();
        BitmapSource whole = EnhancedImageRenderer.Render(original, quick, values, 0, CancellationToken.None, null, 1600);
        BitmapSource tiles = EnhancedImageRenderer.Render(original, quick, values, 0, CancellationToken.None, null, 192);
        AssertPixelsNear(whole, tiles, 1.0, "full image versus guarded tiles with Quick Enhance");
        byte[] fullPixels = ToolPixels(whole), tilePixels = ToolPixels(tiles);
        var joins = new List<int>();
        for (int y = 0; y < whole.PixelHeight; y++)
        for (int x = 0; x < whole.PixelWidth; x++)
            if (x % 192 < 2 || y % 192 < 2)
                for (int c = 0; c < 3; c++) { int at = (y * whole.PixelWidth + x) * 4 + c; joins.Add(Math.Abs(fullPixels[at] - tilePixels[at])); }
        Assert(joins.Average() < 1.5, "tile joins remain within WPF 8-bit sampling precision");
        var subject = new SubjectMask { Width = 64, Height = 48, IsPerson = true, Values = new float[64 * 48], Description = "Save regions" };
        for (int y = 0; y < subject.Height; y++)
        for (int x = 0; x < subject.Width; x++) subject.Values[y * subject.Width + x] = x < 32 ? 1 : 0;
        var regional = AdvancedQuickEnhance.AnalyzeRegions(original, QuickEnhance.Analyze(original, 4, CancellationToken.None), subject, 4, CancellationToken.None);
        var advanced = new AdvancedEnhanceEffect(regional, original); advanced.Freeze();
        BitmapSource advancedWhole = EnhancedImageRenderer.Render(original, advanced, values, 0, CancellationToken.None, null, 1600);
        BitmapSource advancedTiles = EnhancedImageRenderer.Render(original, advanced, values, 0, CancellationToken.None, null, 192);
        AssertPixelsNear(advancedWhole, advancedTiles, 1.0, "Advanced subject mask and manual effects retain global coordinates across tiles");
        AssertPixelsNear(original, EnhancedImageRenderer.Render(original, null, null, 0, CancellationToken.None, null, 192), 0,
            "neutral full-resolution rendering preserves opaque pixels despite print DPI");
        BitmapSource rotated = EnhancedImageRenderer.Render(original, quick, values, 1, CancellationToken.None, null, 192);
        Assert(rotated.PixelWidth == 800 && rotated.PixelHeight == 1200, "save rotation uses full pixel resolution");
        byte[] input = File.ReadAllBytes(photo);
        var request = SaveFixture(photo, photo, values); request.QuickEffect = quick;
        EnhancedSaveResult saved = RunFixtureSave(request, CancellationToken.None);
        Assert(saved.Path == photo && File.Exists(saved.Backup) && input.SequenceEqual(File.ReadAllBytes(saved.Backup)), "overwrite keeps byte-identical original backup");
        Assert(!input.SequenceEqual(File.ReadAllBytes(photo)), "overwrite commits adjusted pixels");
        AssertPixelsNear(whole, ReadToolImage(photo), 1.0, "saved PNG matches rendered full-resolution image");
        Assert(!Directory.GetFiles(photos, ".viewer-enhance-*.tmp").Any(), "successful save cleans staging file");
        string alphaSource = Path.Combine(root, "Alpha.png"), alphaTarget = Path.Combine(root, "Alpha-saved.png");
        var alphaPixels = new byte[96 * 64 * 4];
        for (int i = 0; i < 96 * 64; i++)
        { alphaPixels[i * 4] = 80; alphaPixels[i * 4 + 1] = 100; alphaPixels[i * 4 + 2] = 140; alphaPixels[i * 4 + 3] = (byte)(i % 96 < 32 ? 0 : i % 96 < 64 ? 128 : 255); }
        SaveAdvancedBitmap(AdvancedBitmap(96, 64, alphaPixels), alphaSource);
        var alphaRequest = SaveFixture(alphaSource, alphaTarget, values);
        BitmapSource alphaExpected = EnhancedImageRenderer.Render(alphaRequest.Pixels, null, values, 0, CancellationToken.None, null);
        RunFixtureSave(alphaRequest, CancellationToken.None);
        byte[] alphaSaved = ToolPixels(ReadToolImage(alphaTarget));
        Assert(alphaSaved[3] == 0 && alphaSaved[48 * 4 + 3] == 128 && alphaSaved[80 * 4 + 3] == 255, "PNG save preserves transparent, translucent and opaque alpha");
        AssertPixelsNear(alphaExpected, ReadToolImage(alphaTarget), 1.0, "PNG transparency and adjusted colors match rendered pixels");
        alphaRequest.Destination = Path.Combine(root, "Alpha.jpg");
        RunFixtureSave(alphaRequest, CancellationToken.None);
        byte[] white = ToolPixels(ReadToolImage(alphaRequest.Destination));
        Assert(white[0] > 250 && white[1] > 250 && white[2] > 250 && white[3] == 255, "JPEG flattens transparent regions onto white");
        foreach (string extension in new[] { ".png", ".jpg", ".webp", ".avif", ".jxl" })
        {
            string output = Path.Combine(root, "Export" + extension);
            var convert = SaveFixture(photo, output, new ManualAdjustments());
            RunFixtureSave(convert, CancellationToken.None);
            var registry = new DecoderRegistry(delegate { return false; });
            BitmapSource decoded = registry.Decode(output, 0, CancellationToken.None);
            Assert(decoded.PixelWidth == 1200 && decoded.PixelHeight == 800, "export dimensions " + extension);
            if (extension == ".png" || extension == ".webp" || extension == ".jxl") AssertPixelsNear(ReadToolImage(photo), decoded, 1.0, "lossless export " + extension);
        }
        string jpeg = Path.Combine(root, "Metadata.jpg");
        using (var image = CodecFixture(MagickColors.Crimson, 80, 60))
        {
            var exif = new ExifProfile(); exif.SetValue(ExifTag.Make, "Save test camera"); exif.SetValue(ExifTag.Orientation, (ushort)6);
            image.SetProfile(exif); image.Write(jpeg, MagickFormat.Jpeg);
        }
        RunFixtureSave(SaveFixture(jpeg, jpeg, values, 1), CancellationToken.None);
        using (var image = new MagickImage(jpeg))
        {
            var exif = image.GetExifProfile();
            Assert(image.Width == 60 && image.Height == 80 && exif.GetValue(ExifTag.Make).Value == "Save test camera"
                && exif.GetValue(ExifTag.Orientation).Value == 1 && image.GetColorProfile() != null, "metadata retained, orientation normalized, sRGB embedded");
        }
        string collision = Path.Combine(root, "Collision.png"); MakeImage(collision, 80, 60, 96);
        byte[] existing = File.ReadAllBytes(collision);
        request = SaveFixture(photo, collision, values); request.AllowOverwrite = false;
        bool refused = false; try { RunFixtureSave(request, CancellationToken.None); } catch (IOException) { refused = true; }
        Assert(refused && existing.SequenceEqual(File.ReadAllBytes(collision)), "unconfirmed overwrite refused");
        request = SaveFixture(photo, collision, values); request.SourceRevision.Length++;
        refused = false; try { RunFixtureSave(request, CancellationToken.None); } catch (IOException) { refused = true; }
        Assert(refused && existing.SequenceEqual(File.ReadAllBytes(collision)), "stale source refused");
        request = SaveFixture(photo, collision, values); request.DestinationRevision.Modified--;
        refused = false; try { RunFixtureSave(request, CancellationToken.None); } catch (IOException) { refused = true; }
        Assert(refused && existing.SequenceEqual(File.ReadAllBytes(collision)), "stale destination refused");
        File.SetAttributes(collision, FileAttributes.ReadOnly);
        try
        {
            refused = false; try { RunFixtureSave(SaveFixture(photo, collision, values), CancellationToken.None); } catch (IOException) { refused = true; }
            Assert(refused && existing.SequenceEqual(File.ReadAllBytes(collision)), "read-only destination unchanged");
        }
        finally { File.SetAttributes(collision, FileAttributes.Normal); }
        var cancellation = new CancellationTokenSource(); request = SaveFixture(photo, collision, values);
        var canceled = EnhancedImageStore.SaveAsync(request, cancellation.Token, p => { if (p > 0.1) cancellation.Cancel(); });
        refused = false; try { WaitBackup(canceled); canceled.GetAwaiter().GetResult(); } catch (OperationCanceledException) { refused = true; }
        Assert(refused && existing.SequenceEqual(File.ReadAllBytes(collision)) && !Directory.GetFiles(root, ".viewer-enhance-*.tmp").Any(), "cancel leaves original and cleans staging");
        cancellation.Dispose();
        request = SaveFixture(photo, collision, values);
        var conflict = EnhancedImageStore.SaveAsync(request, CancellationToken.None, p => { if (p > 0 && p < 0.8) File.SetLastWriteTimeUtc(collision, DateTime.UtcNow.AddMinutes(1)); });
        refused = false; try { WaitBackup(conflict); conflict.GetAwaiter().GetResult(); } catch (IOException) { refused = true; }
        Assert(refused && existing.SequenceEqual(File.ReadAllBytes(collision)), "concurrent target edit prevents replacement");
        request = SaveFixture(photo, collision, values);
        EnhancedSaveResult replaced = RunFixtureSave(request, CancellationToken.None);
        Assert(existing.SequenceEqual(File.ReadAllBytes(replaced.Backup)), "Save As replacement backs up the existing destination");
        string gif = Path.Combine(root, "Animation.gif");
        using (var frames = new MagickImageCollection())
        {
            foreach (var color in new[] { MagickColors.Red, MagickColors.Blue })
            { var frame = CodecFixture(color, 80, 60); frame.AnimationDelay = 20; frames.Add(frame); }
            frames.Write(gif, MagickFormat.Gif);
        }
        request = SaveFixture(photo, gif, values); request.SourcePath = gif; request.SourceRevision = FileRevision.Read(gif);
        request.Pixels = new ModernImageDecoder(delegate { return false; }).Decode(gif, 0, CancellationToken.None);
        byte[] animation = File.ReadAllBytes(gif);
        refused = false; try { RunFixtureSave(request, CancellationToken.None); } catch (NotSupportedException) { refused = true; }
        Assert(refused && animation.SequenceEqual(File.ReadAllBytes(gif)), "animated overwrite cannot flatten original");
        request.Destination = Path.Combine(root, "Frame.png"); request.DestinationRevision = FileRevision.Read(request.Destination); request.AllowOverwrite = false;
        RunFixtureSave(request, CancellationToken.None); Assert(File.Exists(request.Destination), "animation exports still frame through Save As");

        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 920 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        Invoke(window, "OpenImageTab", photo, true); Wait(delegate { return Ready(window, photo); }, "save panel image");
        Invoke(window, "SetManualEnhanceVisible", true); Pump();
        var save = Field<Button>(window, "_manualSaveButton"); var saveAs = Field<Button>(window, "_manualSaveAsButton");
        Assert(!save.IsEnabled && saveAs.IsEnabled, "neutral image permits Save As but no redundant overwrite");
        var confirm = Field<CheckBox>(window, "_confirmEnhanceOverwriteCheckBox");
        Assert(confirm.IsChecked == true && Capture(window).ConfirmEnhanceOverwrite == true
            && ((Microsoft.Win32.SaveFileDialog)Invoke(window, "CreateEnhanceSaveDialog")).OverwritePrompt, "older session defaults to confirming Save and Save As");
        FocusViewerSetting(window, confirm); confirm.BringIntoView(); Pump();
        var configure = Field<Window>(window, "_configureWindow");
        AssertInside(confirm, (FrameworkElement)configure.Content);
        Render((FrameworkElement)configure.Content, "enhance-save-confirm-dark.png");
        ThemeManager.SetDarkTheme(false); configure.Width = 570; configure.Height = 360; confirm.BringIntoView(); Pump();
        AssertInside(confirm, (FrameworkElement)configure.Content);
        Render((FrameworkElement)configure.Content, "enhance-save-confirm-light-narrow.png");
        ThemeManager.SetDarkTheme(true);
        confirm.IsChecked = false;
        Assert(Capture(window).ConfirmEnhanceOverwrite == false
            && !((Microsoft.Win32.SaveFileDialog)Invoke(window, "CreateEnhanceSaveDialog")).OverwritePrompt, "Configure opt-out disables Save As replacement prompt");
        services.Sessions.SaveNamed("No overwrite prompt", Capture(window));
        Assert(services.Sessions.LoadNamed("No overwrite prompt").ConfirmEnhanceOverwrite == false, "named session remembers opt-out");
        CloseViewerSettings(window);
        Field<List<Slider>>(window, "_manualSliders")[1].Value = 15;
        Field<ToggleButton>(window, "_enhanceButton").IsChecked = true; Invoke(window, "ToggleQuickEnhance");
        Wait(delegate { return Field<Image>(window, "_mainImage").Effect != null && save.IsEnabled; }, "Save waits for Quick Enhance");
        string sourceId = Field<string>(window, "_activeTabId");
        Invoke(window, "SetZoomOneToOne"); Invoke(window, "ZoomFromCenter", 2.0); Pump();
        Render((FrameworkElement)window.Content, "enhance-save-dark.png");
        string outputFile = Path.Combine(photos, "Saved.png"); input = File.ReadAllBytes(photo);
        var uiSave = (Task<EnhancedSaveResult>)Invoke(window, "SaveEnhancedImageAsync", outputFile, false, true);
        WaitBackup(uiSave); Assert(uiSave.Result != null, "Save As UI completed");
        Wait(delegate { return Ready(window, outputFile); }, "saved image opened");
        Assert(input.SequenceEqual(File.ReadAllBytes(photo)), "Save As leaves source bytes unchanged");
        Assert(Field<string>(window, "_activeTabId") != sourceId && ((BitmapSource)Field<Image>(window, "_mainImage").Source).PixelWidth == 1200,
            "Save As opens full image in new tab, not a viewport crop");
        Assert(Field<List<Slider>>(window, "_manualSliders").All(s => s.Value == 0) && Field<Image>(window, "_mainImage").Effect == null
            && Field<bool>(window, "_quickEnhanceEnabled"), "saved output is not enhanced twice and global Quick Enhance remains enabled");
        string backup = Path.Combine(root, "saved-state.json"); WaitBackup((Task)Invoke(window, "ExportBackupFileAsync", backup));
        Assert(ViewerBackupStore.Load(backup).Workspace.Tabs.Any(t => t.SavedEnhanceRevision.HasValue), "baked revision survives JSON backup");
        Assert(ViewerBackupStore.Load(backup).Workspace.ConfirmEnhanceOverwrite == false, "backup retains overwrite preference");
        Invoke(window, "ActivateImageTab", sourceId); Wait(delegate { return Ready(window, photo); }, "source tab remains adjusted");
        Assert(Field<List<Slider>>(window, "_manualSliders")[1].Value == 15, "source tab keeps independent values after Save As");
        Wait(delegate { return Field<Image>(window, "_mainImage").Effect != null; }, "source Quick Enhance restored");
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        uiSave = Field<Task<EnhancedSaveResult>>(window, "_enhanceSaveTask"); WaitBackup(uiSave);
        Assert(uiSave.Result != null && File.Exists(uiSave.Result.Backup), "Save button overwrites without confirmation and still keeps backup");
        Wait(delegate { return Ready(window, photo); }, "overwritten image reloaded");
        Assert(Field<List<Slider>>(window, "_manualSliders").All(s => s.Value == 0) && Field<Image>(window, "_mainImage").Effect == null, "overwrite resets baked controls");
        Field<List<Slider>>(window, "_manualSliders")[1].Value = 12;
        string unwritable = Path.Combine(root, "ReadOnly.png"); MakeImage(unwritable, 40, 30, 96);
        File.SetAttributes(unwritable, FileAttributes.ReadOnly);
        try
        {
            uiSave = (Task<EnhancedSaveResult>)Invoke(window, "SaveEnhancedImageAsync", unwritable, true, true); WaitBackup(uiSave);
            Assert(uiSave.Result == null && window.IsEnabled && !Field<bool>(window, "_enhancementSaving")
                && Field<List<Slider>>(window, "_manualSliders")[1].Value == 12 && save.IsEnabled,
                "failed save restores UI and preserves unsaved values");
        }
        finally { File.SetAttributes(unwritable, FileAttributes.Normal); }
        Field<List<Slider>>(window, "_manualSliders")[1].Value = 0;
        window.Width = 980; window.Height = 640; Pump(); CheckManualLayout(window);
        AssertInside(save, Field<Border>(window, "_manualEnhanceOverlay")); AssertInside(saveAs, Field<Border>(window, "_manualEnhanceOverlay"));
        ThemeManager.SetDarkTheme(false); Pump(); Render((FrameworkElement)window.Content, "enhance-save-light-narrow.png");
        window.Close(); Pump(); window = new MainWindow(services); window.Show(); Wait(delegate { return Ready(window, photo); }, "saved file session restore");
        Assert(Field<Image>(window, "_mainImage").Effect == null && ((ImageTabState)Invoke(window, "CurrentTabState")).SavedEnhanceRevision.HasValue,
            "restart preserves baked-image protection");
        confirm = Field<CheckBox>(window, "_confirmEnhanceOverwriteCheckBox");
        Assert(Capture(window).ConfirmEnhanceOverwrite == false && confirm.IsChecked == false, "restart restores overwrite preference and checkbox");
        confirm.IsChecked = true;
        Assert(Capture(window).ConfirmEnhanceOverwrite == true
            && ((Microsoft.Win32.SaveFileDialog)Invoke(window, "CreateEnhanceSaveDialog")).OverwritePrompt, "confirmation can be re-enabled");
        Invoke(window, "ApplySessionState", services.Sessions.LoadNamed("No overwrite prompt"), false);
        Wait(delegate { return Ready(window, photo); }, "named overwrite preference");
        Assert(Capture(window).ConfirmEnhanceOverwrite == false && confirm.IsChecked == false, "named session applies opt-out");
        var legacy = Capture(window); legacy.ConfirmEnhanceOverwrite = null;
        Invoke(window, "ApplySessionState", legacy, false); Wait(delegate { return Ready(window, photo); }, "legacy overwrite preference");
        Assert(Capture(window).ConfirmEnhanceOverwrite == true && confirm.IsChecked == true, "applying older session restores safe confirmation default");
        WaitBackup((Task)Invoke(window, "ImportBackupFileAsync", backup, false));
        Wait(delegate { return Ready(window, outputFile); }, "backup overwrite preference");
        Assert(Capture(window).ConfirmEnhanceOverwrite == false && confirm.IsChecked == false, "backup import applies opt-out");
        Invoke(window, "ToggleQuickEnhance");
        Assert(!((ImageTabState)Invoke(window, "CurrentTabState")).SavedEnhanceRevision.HasValue, "explicit Quick Enhance toggle allows fresh processing");
        window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true);
        Console.WriteLine("PASS: full-resolution Enhance Save/Save As, optional overwrite confirmation, preference restore, guarded shader tiles, formats, backups, metadata, cancellation/conflicts, animation safety and no double application after restore.");
    }
}
