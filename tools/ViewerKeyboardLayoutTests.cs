using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    [DllImport("user32.dll", EntryPoint = "GetKeyboardLayout")] private static extern IntPtr TestKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern bool GetKeyboardState(byte[] state);

    private static void RunKeyboardLayoutChecks()
    {
        RunShortcutMapChecks();
        var layout = WindowsKeyboardLayout.Current;
        Assert(layout.Handle == TestKeyboardLayout(0) && layout.Id.Length == 8 && !String.IsNullOrEmpty(layout.Name), "keyboard detection uses the live UI-thread Windows layout");
        Console.WriteLine("Detected keyboard: " + layout.Name + " [" + layout.Id + "]");
        var before = new byte[256]; var after = new byte[256]; GetKeyboardState(before);
        foreach (uint scan in new uint[] { 0x10, 0x11, 0x1a, 0x1b, 0x1e, 0x27, 0x28, 0x29, 0x2b, 0x2c, 0x56 })
        {
            Key key = layout.KeyAt(scan);
            Assert(key != Key.None && !String.IsNullOrEmpty(layout.KeyName(key)), "OS maps character scan positions and OEM labels");
            Assert(ShortcutMap.Gesture(key, ModifierKeys.Control).ToString() == "Ctrl+" + layout.KeyName(key), "gesture text uses native labels instead of US punctuation");
            layout.Character(key, false); layout.Character(key, true);
        }
        GetKeyboardState(after); Assert(before.SequenceEqual(after), "layout label queries do not change keyboard toggle/pressed state");
        string root = Path.Combine(Root, "keyboard-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos); MakeImage(Path.Combine(photos, "Sample.png"), 400, 300, 96);
        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1300, WindowHeight = 850 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            Invoke(window, "ShowConfigure"); Invoke(window, "SelectConfigurationPage", 5); Pump();
            var config = Field<Window>(window, "_configureWindow");
            var view = Field<KeyboardShortcutView>(window, "_keyboardShortcuts");
            var map = Field<ShortcutMap>(window, "_shortcuts");
            Assert(view.KeyButtons.ContainsKey(Key.E) && view.KeyButtons.ContainsKey(Key.NumPad0) && view.KeyButtons.ContainsKey(Key.BrowserBack), "keyboard includes letters, numeric keypad and mouse/browser navigation keys");
            Assert(view.KeyDetails(Key.E).Contains("Enhance adjustments") && view.KeyDetails(Key.E).Contains("manual adjustment sliders"), "hover includes current binding and description");
            Assert(view.KeyDetails(Key.F).Contains("Ctrl+F") && view.KeyDetails(Key.F).Contains("Ctrl+Shift+F"), "hover lists modifier combinations separately");
            Assert(view.KeyDetails(Key.Escape).Contains("Reserved"), "Escape has an accurate reserved-action tooltip");
            var scope = Field<ComboBox>(view, "_scope"); scope.SelectedIndex = 1; Pump();
            Assert(view.KeyDetails(Key.Enter).Contains("Open selected item") && !view.KeyDetails(Key.Enter).Contains("Return to thumbnails"), "browser scope filters bindings");
            scope.SelectedIndex = 2; Pump();
            Assert(!view.KeyDetails(Key.Enter).Contains("Open selected item") && view.KeyDetails(Key.Enter).Contains("Return to thumbnails"), "image scope filters bindings");
            scope.SelectedIndex = 0;
            config.Height = 820; Pump(); Render(config, "keyboard-layout-dark.png");
            UseKeyboardEditor(window, Key.E, delegate(ShortcutKeyEditor editor)
            {
                Invoke(editor, "BeginRecording");
                Assert(editor.Capture(Key.K, ModifierKeys.None, false), "key editor records a replacement");
                Assert(map.Display("manualEnhance") == "E", "recording does not change live bindings before Save");
                Pump(); Render(editor, "keyboard-rebind.png"); Invoke(editor, "SaveBinding");
            });
            Assert(map.Display("manualEnhance") == "K" && view.KeyDetails(Key.K).Contains("Enhance adjustments")
                && !view.KeyDetails(Key.E).Contains("Enhance adjustments"), "double-click editor updates map and tooltips immediately");
            Assert(AutomationProperties.GetItemStatus(view.KeyButtons[Key.K][0]) == "Assigned", "assigned key highlighting updates");
            UseKeyboardEditor(window, Key.K, delegate(ShortcutKeyEditor editor)
            {
                Invoke(editor, "BeginRecording"); editor.Capture(Key.J, ModifierKeys.None, false); Invoke(editor, "SaveBinding");
                Assert(editor.IsVisible && Field<TextBlock>(editor, "_status").Text.Contains("conflicts") && map.Display("manualEnhance") == "K", "conflicting edits leave bindings intact");
                Invoke(editor, "BeginRecording"); editor.Capture(Key.Escape, ModifierKeys.None, false);
                Assert(editor.IsVisible && Field<TextBlock>(editor, "_status").Text.Contains("cancelled"), "Escape cancels recording without closing editor");
            });
            UseKeyboardEditor(window, Key.Enter, delegate(ShortcutKeyEditor editor)
            {
                var choices = Field<ComboBox>(editor, "_assignment");
                Assert(choices.Items.Count == 3, "Enter has separate browser, image and new-assignment choices");
            });
            UseKeyboardEditor(window, Key.F8, delegate(ShortcutKeyEditor editor)
            {
                Field<ComboBox>(editor, "_command").SelectedItem = map.Commands.Single(c => c.Id == "pauseSlideshow");
                Invoke(editor, "SaveBinding");
            });
            Assert(map.Display("pauseSlideshow") == "F8", "unassigned key can receive a selected command");
            UseKeyboardEditor(window, Key.F8, delegate(ShortcutKeyEditor editor) { Invoke(editor, "RemoveBinding"); });
            Assert(map.Display("pauseSlideshow") == "Unassigned", "remove affects only selected binding");
            var saved = Capture(window); services.Sessions.Save(saved);
            string backup = Path.Combine(root, "keys.json"); WaitBackup((System.Threading.Tasks.Task)Invoke(window, "ExportBackupFileAsync", backup));
            var copied = new ShortcutMap(); string error;
            Assert(copied.Load(ViewerBackupStore.Load(backup).Workspace.ShortcutOverrides, out error) && copied.Display("manualEnhance") == "K", "visual edits use normal backup persistence");
            Invoke(window, "ResetAllShortcuts", false);
            Assert(map.Export().Count == 0 && view.KeyDetails(Key.E).Contains("Enhance adjustments"), "reset restores default map and diagram");
            Invoke(window, "ApplySessionState", saved, false); WaitScan(window);
            Assert(map.Display("manualEnhance") == "K" && view.KeyDetails(Key.K).Contains("Enhance adjustments"), "restored shortcuts refresh visual keymap");
            ThemeManager.SetDarkTheme(false); Pump(); Render(config, "keyboard-layout-light.png");
            config.Width = 570; config.Height = 530; Pump();
            foreach (Button button in view.KeyButtons.Values.SelectMany(b => b)) AssertInside(button, view);
            Render(config, "keyboard-layout-narrow.png");
            var keysBefore = Capture(window).ShortcutOverrides.Count;
            view.RefreshLayout(); Assert(Capture(window).ShortcutOverrides.Count == keysBefore, "layout refresh does not rewrite bindings");
            config.Close(); ThemeManager.SetDarkTheme(true); Pump();
        }
        finally { window.Close(); services.Dispose(); Pump(); }
        Console.WriteLine("PASS: native keyboard layout/labels, key hover descriptions/scopes, double-click editing, capture/cancel/conflicts, add/remove/reset, themes/layout and persistence.");
    }

    private static void UseKeyboardEditor(MainWindow window, Key key, Action<ShortcutKeyEditor> action)
    {
        Exception failure = null; bool opened = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += delegate
        {
            timer.Stop(); opened = true; var editor = Field<ShortcutKeyEditor>(window, "_keyboardKeyEditor");
            try { Assert(editor != null && editor.IsVisible, "double-click opens key editor"); action(editor); }
            catch (Exception e) { failure = e; }
            finally { if (editor != null && editor.IsVisible) editor.Close(); }
        };
        timer.Start();
        Field<KeyboardShortcutView>(window, "_keyboardShortcuts").KeyButtons[key][0].RaiseEvent(
            new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
        timer.Stop(); Pump(); Assert(opened, "key double-click routed to modal editor"); if (failure != null) throw failure;
    }
}
