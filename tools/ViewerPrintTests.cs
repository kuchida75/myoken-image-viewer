using System;
using System.IO;
using System.Linq;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Xps.Packaging;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunPrintChecks()
    {
        Directory.CreateDirectory(Root);
        var paper = new Size(800, 1000); var imageable = new Rect(20, 30, 750, 940);
        var fit = ImagePrintLayout.Calculate(new Size(400, 200), paper, imageable, 0, false);
        Assert(fit.ContentArea == imageable && fit.ImageArea.Width == 750 && fit.ImageArea.Height == 375, "fit preserves aspect inside hardware margins");
        Assert(fit.ImageArea.X == 20 && Math.Abs(fit.ImageArea.Y - 312.5) < 0.001, "fit is centered in asymmetric printable area");
        var fill = ImagePrintLayout.Calculate(new Size(400, 200), paper, imageable, 10, true);
        Assert(fill.ImageArea.Width + 0.000001 >= fill.ContentArea.Width && fill.ImageArea.Height + 0.000001 >= fill.ContentArea.Height, "fill covers content area without distortion");
        Assert(Math.Abs(fill.ImageArea.Width / fill.ImageArea.Height - 2) < 0.000001, "fill preserves exact aspect ratio");
        var margins = ImagePrintLayout.Calculate(new Size(1, 4000), paper, imageable, 25.4, false);
        Assert(Math.Abs(margins.ContentArea.X - 96) < 0.001 && Math.Abs(margins.ContentArea.Y - 96) < 0.001
            && Math.Abs(margins.ContentArea.Width - 608) < 0.001 && Math.Abs(margins.ContentArea.Height - 808) < 0.001, "millimeter margins measured from paper edges");
        bool rejected = false;
        try { ImagePrintLayout.Calculate(new Size(100, 100), new Size(100, 100), new Rect(0, 0, 100, 100), 20, false); }
        catch (ArgumentException) { rejected = true; } Assert(rejected, "excessive margins rejected");
        rejected = false;
        try { ImagePrintLayout.Calculate(new Size(100, 100), paper, Rect.Empty, 0, false); }
        catch (ArgumentException) { rejected = true; } Assert(rejected, "empty printer area rejected");
        var map = new ShortcutMap(); string error;
        Assert(map.Match(Key.P, ModifierKeys.Control, false).Id == "print" && map.Match(Key.P, ModifierKeys.Control, true) == null
            && map.Match(Key.P, ModifierKeys.None, false).Id == "navigator", "Ctrl+P prints in image mode without changing plain P");
        Assert(map.Load(new System.Collections.Generic.List<ShortcutOverride> { new ShortcutOverride { Action = "metadata", Keys = new System.Collections.Generic.List<ShortcutGesture> { ShortcutMap.Gesture(Key.P, ModifierKeys.Control) } } }, out error)
            && map.Display("print") == "Unassigned" && map.Match(Key.P, ModifierKeys.Control, false).Id == "metadata", "existing custom Ctrl+P has priority");
        map.ResetAll(); Assert(map.Set("print", new System.Collections.Generic.List<ShortcutGesture> { ShortcutMap.Gesture(Key.F9, ModifierKeys.None) }, out error), "print shortcut rebinds");
        var restored = new ShortcutMap(); Assert(restored.Load(map.Export(), out error) && restored.Match(Key.F9, ModifierKeys.None, false).Id == "print", "print shortcut round-trips");
        CheckPrintPreviewAndOutput(); CheckPrintCapture(); CheckInstalledPrintSettings();
        Console.WriteLine("PASS: print geometry, margins, fit/fill, white paper/alpha, adjusted full-resolution snapshot, XPS output, preview zoom/themes/layout, validation, cancellation, Ctrl+P migration/rebinding and installed-printer settings. No jobs submitted to printers.");
    }

    private static void CheckPrintPreviewAndOutput()
    {
        var source = UpscaleFixture(205, 73); var manual = new ManualAdjustments(); manual.Values[4] = 20;
        var request = new ImagePrintRequest { Name = "Print test.png", Pixels = source, Manual = manual, Rotation = 1 };
        var window = new PrintPreviewWindow(request, false); window.Show();
        try
        {
            Wait(delegate { return window.PendingWork != null && window.PendingWork.IsCompleted; }, "print preview rendering"); window.PendingWork.GetAwaiter().GetResult();
            var pixels = Field<BitmapSource>(window, "_pixels");
            Assert(pixels.IsFrozen && pixels.PixelWidth == 73 && pixels.PixelHeight == 205, "rotation uses full source pixels");
            var expected = EnhancedImageRenderer.Render(source, null, manual, 1, CancellationToken.None, null);
            Assert(ToolPixels(expected).SequenceEqual(ToolPixels(pixels)), "preview bakes adjustments and rotation exactly once");
            Assert(!Field<Button>(window, "_print").IsEnabled && Field<TextBlock>(window, "_status").Text.Contains("Preview only"), "no-printer preview cannot submit a job");
            Invoke(window, "BeginPrint"); Assert(Field<System.Windows.Xps.XpsDocumentWriter>(window, "_writer") == null, "printing without a validated printer is blocked");
            Render(window, "print-preview-dark.png");
            ThemeManager.SetDarkTheme(false); Pump(); Render(window, "print-preview-light.png");
            ThemeManager.SetDarkTheme(true);
            Field<ComboBox>(window, "_orientation").SelectedIndex = 1; Pump();
            Assert(Field<ImagePrintLayout>(window, "_layout").PageSize.Width > Field<ImagePrintLayout>(window, "_layout").PageSize.Height, "landscape updates page geometry");
            Field<ComboBox>(window, "_fit").SelectedIndex = 1; Field<TextBox>(window, "_margin").Text = "0"; Pump();
            var layout = Field<ImagePrintLayout>(window, "_layout");
            Assert(layout.ImageArea.Height > layout.ContentArea.Height, "fill intentionally crops portrait on landscape sheet");
            Render(window, "print-preview-fill.png");
            Field<ComboBox>(window, "_zoom").SelectedIndex = 3; Pump();
            AssertNear(Field<Viewbox>(window, "_preview").ActualWidth, layout.PageSize.Width * 2, "200 percent preview zoom");
            Field<ComboBox>(window, "_zoom").SelectedIndex = 0;
            Field<TextBox>(window, "_copies").Text = "0"; Pump();
            Assert(!Field<Button>(window, "_print").IsEnabled && Field<Viewbox>(window, "_preview").Child == null, "invalid copies invalidate preview and printing");
            Field<TextBox>(window, "_copies").Text = "1"; Field<TextBox>(window, "_margin").Text = "NaN"; Pump();
            Assert(Field<Viewbox>(window, "_preview").Child == null, "NaN margin rejected");
            Field<TextBox>(window, "_margin").Text = "10"; Field<ComboBox>(window, "_fit").SelectedIndex = 0;
            window.Width = 640; window.Height = 520; Pump(); Render(window, "print-preview-narrow.png");
            foreach (string field in new[] { "_paper", "_orientation", "_fit", "_margin", "_copies", "_zoom", "_print", "_close", "_status", "_viewport" })
                AssertInside(Field<FrameworkElement>(window, field), (FrameworkElement)window.Content);
            layout = Field<ImagePrintLayout>(window, "_layout");
            CheckPrintXps(layout, pixels);
        }
        finally { window.Close(); ThemeManager.SetDarkTheme(true); Pump(); }
        var earlyClose = new PrintPreviewWindow(request, false); earlyClose.Show(); earlyClose.Close();
        Wait(delegate { return earlyClose.PendingWork.IsCompleted; }, "closing cancels pending print rendering");
        Assert(Field<BitmapSource>(earlyClose, "_pixels") == null, "closed preview does not retain a late render");
    }

    private static void CheckPrintXps(ImagePrintLayout layout, BitmapSource pixels)
    {
        string path = Path.Combine(Root, "print-output-" + Guid.NewGuid().ToString("N") + ".xps");
        using (var file = new XpsDocument(path, FileAccess.ReadWrite))
        {
            var writer = XpsDocument.CreateXpsDocumentWriter(file); bool completed = false; Exception failure = null;
            writer.WritingCompleted += delegate(object sender, System.Windows.Documents.Serialization.WritingCompletedEventArgs e)
            { completed = true; failure = e.Error; Assert(!e.Cancelled, "XPS export not canceled"); };
            writer.WriteAsync(layout.CreateDocument(pixels), new PrintTicket { CopyCount = 1 });
            Wait(delegate { return completed; }, "write real print document to XPS"); if (failure != null) throw failure;
        }
        using (var file = new XpsDocument(path, FileAccess.Read))
        {
            var paginator = file.GetFixedDocumentSequence().DocumentPaginator; paginator.ComputePageCount();
            Assert(paginator.PageCount == 1, "print document is one full-resolution image page");
            var page = paginator.GetPage(0); Assert(Math.Abs(page.Size.Width - layout.PageSize.Width) < 0.001
                && Math.Abs(page.Size.Height - layout.PageSize.Height) < 0.001, "XPS retains physical page size");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.Size.Width), (int)Math.Ceiling(page.Size.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(page.Visual); byte[] bytes = ToolPixels(bitmap);
            Assert(bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255, "paper and margins remain white in either theme");
            Assert(bytes.Where((v, i) => i % 4 != 3).Any(v => v < 220), "serialized print page contains nonblank image pixels");
        }
    }

    private static void CheckPrintCapture()
    {
        string folder = Path.Combine(Root, "print-capture-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Source.png"); MakeImage(path, 240, 160, 300); byte[] original = File.ReadAllBytes(path);
        var services = AppServices.CreateInstance(Path.Combine(folder, "Profile")); services.Sessions.Save(new SessionState { LastFolder = folder });
        var main = new MainWindow(services); main.Show(); WaitScan(main);
        try
        {
            Assert(!Field<Button>(main, "_printButton").IsEnabled, "print disabled in folder browser");
            Invoke(main, "OpenImageTab", path, true); Wait(delegate { return Ready(main, path); }, "print source"); WaitScan(main);
            Invoke(main, "ChangeManualAdjustment", 4, 15.0);
            var state = (ImageTabState)Invoke(main, "CurrentTabState"); state.RotationQuarterTurns = 1; Invoke(main, "UpdateImageRotation");
            Field<System.Windows.Controls.Primitives.ToggleButton>(main, "_enhanceButton").IsChecked = true; Invoke(main, "ToggleQuickEnhance");
            Wait(delegate { return (bool)Invoke(main, "CanSaveEnhancement"); }, "print waits for Quick Enhance");
            var request = (ImagePrintRequest)Invoke(main, "CapturePrintRequest");
            Assert(request.QuickEffect != null && request.QuickEffect.IsFrozen && request.Pixels.PixelWidth == 240 && request.Rotation == 1 && request.Manual.Values[4] == 15,
                "print captures full image, Quick Enhance, sliders and rotation");
            state.ManualAdjustments.Values[4] = 0; Assert(request.Manual.Values[4] == 15, "print snapshot does not share mutable adjustments");
            Field<Canvas>(main, "_imageCanvas").Focus();
            Exception callbackFailure = null; bool shown = false;
            main.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
            {
                var preview = Application.Current.Windows.OfType<PrintPreviewWindow>().SingleOrDefault();
                try { shown = preview != null; Assert(shown, "Ctrl+P opens print preview"); Assert(!Field<Button>(main, "_printButton").IsEnabled, "duplicate preview disabled"); }
                catch (Exception e) { callbackFailure = e; }
                finally { if (preview != null) preview.Close(); }
            }));
            Assert((bool)Invoke(main, "RunShortcut", Key.P, ModifierKeys.Control, false), "Ctrl+P command handled");
            if (callbackFailure != null) throw callbackFailure;
            Pump(); Assert(shown && Field<PrintPreviewWindow>(main, "_printPreview") == null && Field<Button>(main, "_printButton").IsEnabled, "closing restores current-image controls");
            Assert(original.SequenceEqual(File.ReadAllBytes(path)), "printing preview does not modify source bytes");
            Invoke(main, "BrowseActiveTab"); Assert(!Field<Button>(main, "_printButton").IsEnabled, "return to browser disables print");
        }
        finally { main.Close(); services.Dispose(); Pump(); }
    }

    private static void CheckInstalledPrintSettings()
    {
        var task = ImagePrinting.RunSta(ImagePrinting.Printers); Wait(delegate { return task.IsCompleted; }, "enumerate installed printers (read only)");
        var printers = task.GetAwaiter().GetResult();
        var printer = printers.FirstOrDefault(p => p.Name == "Microsoft Print to PDF");
        if (printer == null) { Console.WriteLine("SKIP: Microsoft Print to PDF not installed; no live driver settings check."); return; }
        var profileTask = ImagePrinting.RunSta(() => ImagePrinting.LoadProfile(printer.Name)); Wait(delegate { return profileTask.IsCompleted; }, "read PDF printer capabilities");
        var profile = profileTask.GetAwaiter().GetResult(); var paper = profile.Papers.FirstOrDefault(p => p.Kind == PageMediaSizeName.ISOA4) ?? profile.Papers[0];
        foreach (bool landscape in new[] { false, true })
        {
            var settingsTask = ImagePrinting.RunSta(() => ImagePrinting.Settings(printer.Name, paper, landscape, 1));
            Wait(delegate { return settingsTask.IsCompleted; }, "validate PDF printer page settings"); var settings = settingsTask.GetAwaiter().GetResult();
            Assert(settings.Ticket.Length > 0 && new Rect(settings.PageSize).Contains(settings.ImageableArea), "validated printable area stays inside paper");
            AssertNear(settings.PageSize.Width, landscape ? paper.Height : paper.Width, "validated printer orientation");
        }
        var window = new PrintPreviewWindow(new ImagePrintRequest { Name = "Printer settings test", Pixels = UpscaleFixture(49, 37) }, false);
        window.Show();
        try
        {
            Wait(delegate { return window.PendingWork.IsCompleted; }, "live printer preview pixels");
            var selector = Field<ComboBox>(window, "_printers"); selector.ItemsSource = printers; selector.SelectedItem = printer;
            Wait(delegate { return window.SettingsWork.IsCompleted; }, "live PDF selection"); window.SettingsWork.GetAwaiter().GetResult();
            Assert(Field<Button>(window, "_print").IsEnabled && Field<ImagePrintSettings>(window, "_settings") != null, "real printer validates and enables Print without submission");
            Render(window, "print-preview-printer-ready.png");
            Field<TextBox>(window, "_margin").Text = "invalid";
            Field<TextBox>(window, "_copies").Text = "0"; Field<TextBox>(window, "_copies").Text = "1";
            Wait(delegate { return window.SettingsWork.IsCompleted; }, "copy settings while margin invalid");
            Assert(!Field<Button>(window, "_print").IsEnabled, "invalid margin still blocks print");
            Field<TextBox>(window, "_margin").Text = "10";
            Assert(Field<Button>(window, "_print").IsEnabled, "correcting margin restores validated printer settings");
            var refresh = (Task)Invoke(window, "RefreshPrintersAsync");
            Wait(delegate { return refresh.IsCompleted; }, "refresh keeps selected printer"); refresh.GetAwaiter().GetResult();
            Assert(((ImagePrinterInfo)selector.SelectedItem).Name == printer.Name && Field<Button>(window, "_print").IsEnabled, "refresh preserves chosen printer and valid preview");
        }
        finally { window.Close(); Pump(); }
        Console.WriteLine("PASS: installed Microsoft Print to PDF paper, orientation and printable-area validation (no job submitted).");
    }
}
