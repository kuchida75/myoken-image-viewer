using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void PressFocusedKey(Key key)
    {
        // Route from actual keyboard focus, not directly from MainWindow: lost focus is the regression.
        var target = Keyboard.FocusedElement as UIElement;
        Assert(target != null, "keyboard input has a focused target");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    private static void CheckNavigationAfterSave(MainWindow window, string first, string current, string last, string scenario)
    {
        Wait(delegate { return Ready(window, current); }, scenario + " reload"); WaitScan(window); Pump();
        var canvas = Field<Canvas>(window, "_imageCanvas");
        Console.WriteLine(scenario + ": active=" + window.IsActive + ", focus=" + Keyboard.FocusedElement);
        Assert(window.IsActive && canvas.IsKeyboardFocused, scenario + " returns keyboard focus to image without a click");
        string tab = Field<string>(window, "_activeTabId");
        PressFocusedKey(Key.Right); Wait(delegate { return Ready(window, last); }, scenario + " next");
        PressFocusedKey(Key.Left); Wait(delegate { return Ready(window, current); }, scenario + " previous");
        PressFocusedKey(Key.Left); Wait(delegate { return Ready(window, first); }, scenario + " first");
        PressFocusedKey(Key.Right); Wait(delegate { return Ready(window, current); }, scenario + " return");
        Assert(Field<string>(window, "_activeTabId") == tab, scenario + " navigates in the existing tab");
    }

    private static void RunSaveNavigationChecks()
    {
        string root = Path.Combine(Root, "save-navigation-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string first = Path.Combine(photos, "A.png"), current = Path.Combine(photos, "B.png"), last = Path.Combine(photos, "C.png");
        foreach (string path in new[] { first, current, last }) MakeImage(path, 640, 480, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 920 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            Invoke(window, "OpenImageTab", current, true); Wait(delegate { return Ready(window, current); }, "save navigation image");
            Invoke(window, "SetAutoSaveRotations", true, false);
            window.Activate(); Field<Canvas>(window, "_imageCanvas").Focus();
            PressFocusedKey(Key.Oem6); WaitBackup(Field<Task>(window, "_rotationSaveTask"));
            Assert(ReadToolImage(current).PixelWidth == 480, "keyboard rotation saved before navigation");
            CheckNavigationAfterSave(window, first, current, last, "Automatic rotation save");

            Invoke(window, "SetAutoSaveRotations", false, false);
            FocusViewerSetting(window, Field<Button>(window, "_rotateLeftButton"));
            Field<Button>(window, "_rotateLeftButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Field<Button>(window, "_saveRotationButton").Focus();
            Invoke(window, "BeginRotationSave", false); WaitBackup(Field<Task>(window, "_rotationSaveTask"));
            Assert(ReadToolImage(current).PixelWidth == 640, "explicit rotation saved before navigation");
            CheckNavigationAfterSave(window, first, current, last, "Configure rotation save");
            CloseViewerSettings(window);

            byte[] beforeFailure = File.ReadAllBytes(current);
            File.SetAttributes(current, FileAttributes.ReadOnly);
            try
            {
                Invoke(window, "SetAutoSaveRotations", true, false);
                PressFocusedKey(Key.Oem6); WaitBackup(Field<Task>(window, "_rotationSaveTask"));
                Assert(Field<TextBlock>(window, "_rotationStatus").Text.StartsWith("Not saved:")
                    && beforeFailure.SequenceEqual(File.ReadAllBytes(current)), "failed rotation leaves source untouched");
                CheckNavigationAfterSave(window, first, current, last, "Failed rotation save");
            }
            finally { File.SetAttributes(current, FileAttributes.Normal); Invoke(window, "SetAutoSaveRotations", false, false); }

            PressFocusedKey(Key.Oem6); Invoke(window, "SetManualEnhanceVisible", true); Pump();
            Field<CheckBox>(window, "_confirmEnhanceOverwriteCheckBox").IsChecked = false;
            var save = Field<Button>(window, "_manualSaveButton");
            Assert(save.IsEnabled && save.Focus(), "Enhance Save takes focus like a mouse click");
            save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var enhance = Field<Task<EnhancedSaveResult>>(window, "_enhanceSaveTask"); WaitBackup(enhance);
            Assert(enhance.Result != null && ReadToolImage(current).PixelWidth == 480, "Enhance saves rotation with progress window");
            CheckNavigationAfterSave(window, first, current, last, "Enhance rotation save");

            var slider = Field<List<Slider>>(window, "_manualSliders")[1]; slider.Value = 12; slider.Focus();
            Assert(!(bool)Invoke(window, "RunShortcut", Key.Right, ModifierKeys.None, false), "slider still keeps its own arrow input when deliberately focused");
            string readOnly = Path.Combine(root, "ReadOnly.png"); MakeImage(readOnly, 40, 30, 96);
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            try
            {
                enhance = (Task<EnhancedSaveResult>)Invoke(window, "SaveEnhancedImageAsync", readOnly, true, true); WaitBackup(enhance);
                Assert(enhance.Result == null && slider.Value == 12, "failed Enhance save retains adjustments");
                CheckNavigationAfterSave(window, first, current, last, "Failed Enhance save");
            }
            finally { File.SetAttributes(readOnly, FileAttributes.Normal); }

            slider.Value = 15; save.Focus();
            enhance = (Task<EnhancedSaveResult>)Invoke(window, "SaveEnhancedImageAsync", current, true, true);
            Window progress = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Saving image");
            PressKey(progress, Key.Escape); WaitBackup(enhance);
            Assert(enhance.Result == null && slider.Value == 15, "cancel retains unsaved adjustments");
            CheckNavigationAfterSave(window, first, current, last, "Canceled Enhance save");

            string output = Path.Combine(photos, "D.png");
            Field<Button>(window, "_manualSaveAsButton").Focus();
            enhance = (Task<EnhancedSaveResult>)Invoke(window, "SaveEnhancedImageAsync", output, false, true); WaitBackup(enhance);
            Assert(enhance.Result != null, "Save As completes");
            Wait(delegate { return Ready(window, output); }, "Save As reload"); WaitScan(window); Pump();
            Assert(Field<Canvas>(window, "_imageCanvas").IsKeyboardFocused, "Save As returns focus to the new image");
            PressFocusedKey(Key.Left); Wait(delegate { return Ready(window, last); }, "Save As previous");
            PressFocusedKey(Key.Right); Wait(delegate { return Ready(window, output); }, "Save As next");

            Invoke(window, "SetAutoSaveRotations", true, false);
            PressFocusedKey(Key.Oem6);
            var otherInput = new TextBox { Text = "Another test window" };
            var other = new Window { Title = "Save focus regression", Content = otherInput, Width = 320, Height = 150 };
            try
            {
                other.Show(); other.Activate(); otherInput.Focus();
                WaitBackup(Field<Task>(window, "_rotationSaveTask"));
                Wait(delegate { return Ready(window, output); }, "background rotation reload"); WaitScan(window); Pump();
                Assert(other.IsActive && otherInput.IsKeyboardFocused, "background save does not steal focus from another window");
                Assert(FocusManager.GetFocusedElement(window) == Field<Canvas>(window, "_imageCanvas"), "background save prepares image focus for reactivation");
            }
            finally { other.Close(); }
            window.Activate(); Pump();
            Assert(Field<Canvas>(window, "_imageCanvas").IsKeyboardFocused, "returning to viewer restores image focus");
            PressFocusedKey(Key.Left); Wait(delegate { return Ready(window, last); }, "navigation after returning from other window");
        }
        finally { window.Close(); Pump(); services.Dispose(); }
        Console.WriteLine("PASS: rotation and Enhance Save/Save As restore focused-key navigation without clicking, including failure/cancel; sliders and other active windows keep input.");
    }
}
