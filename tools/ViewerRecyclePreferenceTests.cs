using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunRecyclePreferenceChecks()
    {
        string root = Path.Combine(Root, "recycle-preference-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"), child = Path.Combine(photos, "Folder to recycle"); Directory.CreateDirectory(child);
        string a = Path.Combine(photos, "01.png"), b = Path.Combine(photos, "02.png"), nested = Path.Combine(child, "Nested.png");
        MakeImage(a, 320, 240, 96); MakeImage(b, 320, 240, 96); MakeImage(nested, 80, 80, 96);
        var services = AppServices.CreateInstance(Path.Combine(root, "Profile")); services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1200, WindowHeight = 800 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            var check = Field<CheckBox>(window, "_confirmRecycleCheckBox");
            Assert(!Field<bool>(window, "_confirmRecycle") && check.IsChecked == false && !Capture(window).ConfirmRecycle, "New and legacy sessions default to recycling without app confirmation");
            Invoke(window, "OpenBrowserImage", a); Wait(delegate { return Ready(window, a); }, "Recycle fixture opens");
            string tab = Field<string>(window, "_activeTabId"); Invoke(window, "DeletePath", a);
            Wait(delegate { return Ready(window, b); }, "Unconfirmed recycle advances"); WaitScan(window);
            Assert(!File.Exists(a) && File.Exists(b) && Field<string>(window, "_activeTabId") == tab, "Default Delete recycles only the displayed fixture and advances in the same tab without prompting");
            Invoke(window, "BrowseActiveTab"); WaitScan(window); Invoke(window, "DeleteBrowserItems", (object)new[] { child });
            WaitBackup(Field<Task>(window, "_recycleTask")); WaitScan(window);
            Assert(!Directory.Exists(child) && File.Exists(b), "Browser folders use the same default no-confirmation Recycle Bin path");
            check.IsChecked = true; Assert(Field<bool>(window, "_confirmRecycle") && Capture(window).ConfirmRecycle, "Enabling the configuration checkbox applies immediately");
            services.Sessions.SaveNamed("Confirm recycle", Capture(window));
            string backup = Path.Combine(root, "Settings.json"); ViewerBackupStore.Save(backup, ViewerBackupStore.Create(Capture(window), new BrowserPreferences(), services.Sessions.CaptureNamedSessions()));
            Assert(ViewerBackupStore.Load(backup).Workspace.ConfirmRecycle && services.Sessions.LoadNamed("Confirm recycle").ConfirmRecycle, "Confirmation preference survives JSON backup and named sessions");
            Invoke(window, "ApplySessionState", new SessionState { LastFolder = photos }, false); WaitScan(window);
            Assert(!Field<bool>(window, "_confirmRecycle") && check.IsChecked == false, "Reset/older settings restore the new no-confirmation default");
            Invoke(window, "ApplySessionState", services.Sessions.LoadNamed("Confirm recycle"), false); WaitScan(window);
            Assert(Field<bool>(window, "_confirmRecycle") && check.IsChecked == true, "Named restore synchronizes confirmation and its checkbox");
            services.Sessions.Save(Capture(window)); window.Close(); Pump(); window = new MainWindow(services); window.Show(); WaitScan(window);
            Assert(Field<bool>(window, "_confirmRecycle") && Field<CheckBox>(window, "_confirmRecycleCheckBox").IsChecked == true, "Automatic session restart retains an enabled confirmation preference");
            Invoke(window, "ShowConfigure"); Pump(); Field<TextBox>(window, "_configurationSearch").Text = "Recycle Bin"; Pump();
            Render(Field<Window>(window, "_configureWindow"), "recycle-confirmation-settings-dark.png"); CloseViewerSettings(window);
        }
        finally { window.Close(); Pump(); services.Dispose(); }
        Console.WriteLine("PASS: default no-prompt viewer/folder recycling, same-tab advancement, live confirmation setting, legacy/reset defaults, automatic/named sessions, JSON backup and searchable configuration. Only generated fixtures recycled; enabled confirmation dialog clicks not automated.");
    }
}
