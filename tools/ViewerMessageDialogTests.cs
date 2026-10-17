using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void WithMessageDialog(Window owner, Action open, Action<MessageDialogWindow> respond)
    {
        bool seen = false; Exception failure = null; var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += delegate
        {
            var prompt = Application.Current.Windows.OfType<MessageDialogWindow>().FirstOrDefault(w => w.Owner == owner);
            if (prompt == null || !prompt.IsLoaded)
            { if (elapsed.Elapsed.TotalSeconds > 20) { failure = new Exception("Themed message dialog did not open"); timer.Stop(); if (prompt != null) prompt.Close(); else if (owner != null) owner.Close(); } return; }
            timer.Stop(); seen = true;
            try { respond(prompt); }
            catch (Exception error) { failure = error; prompt.Close(); }
        };
        timer.Start(); try { open(); } finally { timer.Stop(); }
        if (failure != null) throw failure;
        Assert(seen, "The action opens an app-themed message dialog");
    }

    private static void ClickMessage(MessageDialogWindow window, MessageBoxResult result)
    { Field<Dictionary<MessageBoxResult, Button>>(window, "_buttons")[result].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }

    private static void RunMessageDialogChecks()
    {
        var owner = new Window { Width = 700, Height = 500 }; ThemeManager.PrepareWindow(owner); owner.Show();
        try
        {
            foreach (bool dark in new[] { true, false })
            {
                ThemeManager.SetDarkTheme(dark);
                MessageBoxResult result = MessageBoxResult.None;
                WithMessageDialog(owner, delegate { result = ThemedMessageBox.Show(owner, "Unable to open the image.\n\nThe file may have been moved or removed.", "Open failed", MessageBoxButton.OK, MessageBoxImage.Error); }, prompt =>
                {
                    var color = ((SolidColorBrush)prompt.Background).Color;
                    Assert(dark ? color.R < 70 : color.R > 200, "Error dialog follows the current app theme");
                    var buttons = Field<Dictionary<MessageBoxResult, Button>>(prompt, "_buttons");
                    Assert(buttons[MessageBoxResult.OK].IsDefault, "OK-only message retains its default action");
                    Render(prompt, dark ? "message-error-dark.png" : "message-error-light.png");
                    ClickMessage(prompt, MessageBoxResult.OK);
                });
                Assert(result == MessageBoxResult.OK, "Error acknowledgment returns OK");
            }
            ThemeManager.SetDarkTheme(true);
            foreach (MessageBoxButton kind in new[] { MessageBoxButton.OK, MessageBoxButton.OKCancel, MessageBoxButton.YesNo, MessageBoxButton.YesNoCancel })
            {
                MessageBoxResult[] choices = kind == MessageBoxButton.OK ? new[] { MessageBoxResult.OK }
                    : kind == MessageBoxButton.OKCancel ? new[] { MessageBoxResult.OK, MessageBoxResult.Cancel }
                    : kind == MessageBoxButton.YesNo ? new[] { MessageBoxResult.Yes, MessageBoxResult.No }
                    : new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel };
                foreach (MessageBoxResult choice in choices)
                {
                    MessageBoxResult result = MessageBoxResult.None;
                    WithMessageDialog(owner, delegate { result = ThemedMessageBox.Show(owner, "Choose an action.", "Confirmation", kind); }, prompt =>
                    {
                        var buttons = Field<Dictionary<MessageBoxResult, Button>>(prompt, "_buttons");
                        Assert(buttons.Count == choices.Length && buttons[choices[0]].IsDefault, "Button set and implicit default are preserved");
                        ClickMessage(prompt, choice);
                    });
                    Assert(result == choice, "Each confirmation choice returns its exact result");
                }
                MessageBoxResult dismissed = MessageBoxResult.None;
                WithMessageDialog(owner, delegate { dismissed = ThemedMessageBox.Show(owner, "Dismiss without changes.", "Confirmation", kind); }, prompt =>
                    prompt.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(prompt), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }));
                Assert(dismissed == (kind == MessageBoxButton.OK ? MessageBoxResult.OK : kind == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel), "Escape never accepts a destructive action");
            }
            MessageBoxResult closed = MessageBoxResult.None;
            WithMessageDialog(owner, delegate { closed = ThemedMessageBox.Show(owner, "Overwrite the existing file?", "Save", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No); }, prompt =>
            {
                Assert(Field<Dictionary<MessageBoxResult, Button>>(prompt, "_buttons")[MessageBoxResult.No].IsDefault, "Explicit safe default is preserved");
                prompt.Close();
            });
            Assert(closed == MessageBoxResult.No, "Title-bar close declines a Yes/No prompt");
            WithMessageDialog(owner, delegate { ThemedMessageBox.Show(owner, String.Join("\n", Enumerable.Repeat("C:\\Photos\\A-very-long-filename-without-spaces-that-must-wrap-1234567890.png could not be read.", 80)), "File transfer incomplete", MessageBoxButton.OKCancel, MessageBoxImage.Warning); }, prompt =>
            {
                prompt.Width = 320; prompt.MaxHeight = 360; prompt.UpdateLayout();
                foreach (Button button in Field<Dictionary<MessageBoxResult, Button>>(prompt, "_buttons").Values) AssertInside(button, (FrameworkElement)prompt.Content);
                var text = StackVisuals<TextBox>(prompt).First();
                Assert(text.IsReadOnly && text.TextWrapping == TextWrapping.Wrap && prompt.ActualHeight <= 360.5, "Long paths stay selectable and bounded on a small screen");
                Render(prompt, "message-long-narrow-dark.png"); prompt.Close();
            });
            WithMessageDialog(null, delegate { ThemedMessageBox.Show("The viewer could not start or continue.", "Myoken Image Viewer", MessageBoxButton.OK, MessageBoxImage.Error); }, prompt =>
            { Assert(prompt.Owner == null && prompt.ShowInTaskbar, "Startup errors can be displayed without a main window"); ClickMessage(prompt, MessageBoxResult.OK); });
        }
        finally { owner.Close(); Pump(); ThemeManager.SetDarkTheme(true); }
        RunMessageOperationChecks();
        Console.WriteLine("PASS: themed dark/light errors, every confirmation result/default, Escape/title-close safety, narrow long messages, ownerless startup errors, actual Delete/folder recycle and Enhance overwrite cancel/accept, backups and unchanged opt-out preferences.");
    }

    private static void RunMessageOperationChecks()
    {
        string root = Path.Combine(Root, "message-operations-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"), folder = Path.Combine(photos, "Folder"); Directory.CreateDirectory(folder);
        string a = Path.Combine(photos, "01.png"), b = Path.Combine(photos, "02.png");
        MakeImage(a, 320, 240, 96); MakeImage(b, 320, 240, 96); MakeImage(Path.Combine(folder, "Nested.png"), 64, 64, 96);
        var services = AppServices.CreateInstance(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1200, WindowHeight = 800, ConfirmRecycle = true, ConfirmEnhanceOverwrite = true });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            Invoke(window, "OpenBrowserImage", a); Wait(delegate { return Ready(window, a); }, "Confirmation source opens");
            string tab = Field<string>(window, "_activeTabId");
            WithMessageDialog(window, delegate { Invoke(window, "DeletePath", a); }, prompt =>
            {
                Assert(prompt.Title == "Delete image" && Field<Dictionary<MessageBoxResult, Button>>(prompt, "_buttons")[MessageBoxResult.No].IsDefault, "Delete is themed and still defaults to No");
                Render(prompt, "message-delete-dark.png"); ClickMessage(prompt, MessageBoxResult.No);
            });
            Assert(File.Exists(a) && Ready(window, a), "Declining Delete leaves the displayed image untouched");
            WithMessageDialog(window, delegate { Invoke(window, "DeletePath", a); }, prompt => ClickMessage(prompt, MessageBoxResult.Yes));
            Wait(delegate { return Ready(window, b); }, "Confirmed Delete advances to next image"); WaitScan(window);
            Assert(!File.Exists(a) && Field<string>(window, "_activeTabId") == tab, "Confirmed Delete recycles only the generated image and retains its tab");
            Invoke(window, "SetManualEnhanceVisible", true); Field<List<Slider>>(window, "_manualSliders")[1].Value = 12;
            byte[] original = File.ReadAllBytes(b);
            WithMessageDialog(window, delegate { Invoke(window, "BeginEnhanceSave", false); }, prompt =>
            {
                Assert(prompt.Title == "Save adjusted image", "Enhance overwrite uses the shared themed prompt");
                Render(prompt, "message-enhance-overwrite-dark.png"); ClickMessage(prompt, MessageBoxResult.No);
            });
            Assert(File.ReadAllBytes(b).SequenceEqual(original) && Field<Task<EnhancedSaveResult>>(window, "_enhanceSaveTask") == null, "Declining overwrite starts no save and changes no bytes");
            WithMessageDialog(window, delegate { Invoke(window, "BeginEnhanceSave", false); }, prompt => ClickMessage(prompt, MessageBoxResult.Yes));
            var task = Field<Task<EnhancedSaveResult>>(window, "_enhanceSaveTask"); WaitBackup(task);
            Assert(task.Result != null && File.ReadAllBytes(task.Result.Backup).SequenceEqual(original), "Accepted overwrite retains the original backup");
            Wait(delegate { return Ready(window, b); }, "Confirmed enhancement reloads");
            Invoke(window, "BrowseActiveTab"); WaitScan(window);
            WithMessageDialog(window, delegate { Invoke(window, "DeleteBrowserItems", (object)new[] { folder }); }, prompt => ClickMessage(prompt, MessageBoxResult.No));
            Assert(Directory.Exists(folder), "Declining folder recycle preserves its contents");
            WithMessageDialog(window, delegate { Invoke(window, "DeleteBrowserItems", (object)new[] { folder }); }, prompt => ClickMessage(prompt, MessageBoxResult.Yes));
            WaitBackup(Field<Task>(window, "_recycleTask")); WaitScan(window);
            Assert(!Directory.Exists(folder) && File.Exists(b), "Accepted folder recycle affects only the selected generated folder");
            Assert(Capture(window).ConfirmRecycle && Capture(window).ConfirmEnhanceOverwrite == true, "Themed prompts do not change confirmation preferences");
        }
        finally { window.Close(); Pump(); services.Dispose(); }
    }
}
