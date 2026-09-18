using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunPinAndInstanceChecks()
    {
        string root = Path.Combine(Root, "pi-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        string photos = Path.Combine(root, "Photos");
        Directory.CreateDirectory(photos);
        string image = Path.Combine(photos, "Image.png");
        MakeImage(image, 640, 420, 96);
        RunInstanceStoreChecks(root, photos);
        RunSharedPreferenceChecks(root, photos);
        RunConcurrentStorageChecks(root, image);
        RunExecutableInstanceChecks(root, photos);

        var services = AppServices.Create(Path.Combine(root, "PinProfile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 780 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        Invoke(window, "OpenLatestBrowserTab"); WaitScan(window);
        string first = Field<string>(window, "_activeTabId");
        Invoke(window, "OpenLatestBrowserTab"); WaitScan(window);
        string second = Field<string>(window, "_activeTabId");
        var tabs = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
        var order = Field<List<string>>(window, "_tabOrder");
        ClickPin(window, second);
        Assert(tabs[second].IsPinned && order[1] == second, "pin preserves browser tab position");
        ClickPin(window, first);
        Assert(Field<string>(window, "_activeTabId") == second, "pinning inactive tab keeps active tab");
        Assert(order.SequenceEqual(new[] { first, second }), "pinned order is stable");
        var header = TabHeader(window, second);
        Assert(!((DockPanel)header.Child).Children.OfType<Button>().Any(), "pinned tab hides close button");
        Assert(((DockPanel)header.Child).Children.OfType<TextBlock>().Any(text => text.Text == "\uE718"), "pin marker visible");
        Assert(!TabMenuItem(window, second, "close-tab").IsEnabled, "pinned context close disabled");
        Assert((string)TabMenuItem(window, second, "pin-tab").Header == "Unpin tab", "pinned context offers unpin");
        Invoke(window, "CloseTab", second);
        Assert(tabs.ContainsKey(second), "central close guard protects pinned tab including keyboard close");
        Invoke(window, "DuplicateTab", second); WaitScan(window);
        string duplicate = Field<string>(window, "_activeTabId");
        Assert(!tabs[duplicate].IsPinned && order.Last() == duplicate, "duplicate is unpinned beside source");
        Invoke(window, "ActivateImageTab", second); WaitScan(window);
        Invoke(window, "OpenBrowserImage", image); Wait(delegate { return Ready(window, image); }, "pinned image");
        Assert(Field<string>(window, "_activeTabId") == second && tabs[second].IsPinned, "same-tab image retains pin");
        PressKey(window, Key.Enter); WaitScan(window);
        Assert(tabs[second].IsBrowser && tabs[second].IsPinned, "return to browser retains pin");
        services.Sessions.SaveNamed("Pinned workspace", Capture(window));
        Render((FrameworkElement)window.Content, "pinned-tabs-dark.png");
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = false;
        window.Width = 980; window.Height = 640; Pump();
        Render((FrameworkElement)window.Content, "pinned-tabs-light-narrow.png");
        Field<CheckBox>(window, "_darkThemeCheckBox").IsChecked = true;
        window.Close(); Pump();
        window = new MainWindow(services); window.Show(); WaitScan(window);
        tabs = Field<Dictionary<string, ImageTabState>>(window, "_tabs");
        Assert(tabs[second].IsPinned && tabs[first].IsPinned && !tabs[duplicate].IsPinned, "automatic session restores pin states");
        Assert(Field<List<string>>(window, "_tabOrder").SequenceEqual(new[] { first, second, duplicate }), "automatic restore retains user tab order");
        ClickPin(window, second); Invoke(window, "CloseTab", second);
        Assert(!tabs.ContainsKey(second), "unpin permits normal close");
        Invoke(window, "ApplySessionState", services.Sessions.LoadNamed("Pinned workspace"), false); WaitScan(window);
        Assert(tabs[second].IsPinned, "named session restores pins");
        Assert(!new SessionTabDto().IsPinned, "legacy tabs default unpinned");
        window.Close(); Pump(); services.Dispose();
        Console.WriteLine("PASS: pin/unpin menus, ordering, duplicate/close behavior, dark/light rendering and saved pins.");
    }

    private static Border TabHeader(MainWindow window, string id)
    {
        return Field<StackPanel>(window, "_tabStrip").Children.OfType<Border>().Single(tab => Object.Equals(tab.Tag, id));
    }

    private static MenuItem TabMenuItem(MainWindow window, string id, string tag)
    {
        return TabHeader(window, id).ContextMenu.Items.OfType<MenuItem>().Single(item => Object.Equals(item.Tag, tag));
    }

    private static void ClickPin(MainWindow window, string id)
    {
        TabMenuItem(window, id, "pin-tab").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
    }

    private static SessionState InstanceState(string folder, string id, double width)
    {
        var state = new SessionState { LastFolder = folder, ActiveTabId = id, WindowWidth = width, WindowHeight = 700 };
        state.Tabs.Add(new SessionTabDto { Id = id, IsBrowser = true, IsPinned = true, FolderPath = folder });
        return state;
    }

    private static void RunInstanceStoreChecks(string root, string photos)
    {
        string profile = Path.Combine(root, "Slots");
        using (var first = AppServices.CreateInstance(profile))
        {
            Assert(first.InstanceNumber == 1, "first instance uses legacy session slot");
            first.Sessions.Save(InstanceState(photos, "first", 1100));
            using (var second = AppServices.CreateInstance(profile))
            {
                Assert(second.InstanceNumber == 2 && second.Sessions.Load().ActiveTabId == "first", "new instance starts from main session");
                second.Sessions.Save(InstanceState(photos, "second", 1200));
                Assert(first.Sessions.Load().ActiveTabId == "first", "second instance cannot overwrite first");
                first.Sessions.SaveNamed("Shared", InstanceState(photos, "named", 1000));
                Assert(second.Sessions.LoadNamed("Shared").ActiveTabId == "named", "named sessions shared between instances");
            }
            using (var reopened = AppServices.CreateInstance(profile))
            {
                var restored = reopened.Sessions.Load();
                Assert(reopened.InstanceNumber == 2 && restored.ActiveTabId == "second" && restored.Tabs[0].IsPinned
                    && restored.WindowWidth == 1200, "reused instance restores its own tabs/pins/placement");
            }
            Assert(first.Sessions.Load().WindowWidth == 1100, "other instance retains placement");
        }
        using (var reopened = AppServices.CreateInstance(profile))
            Assert(reopened.InstanceNumber == 1 && reopened.Sessions.Load().ActiveTabId == "first", "main slot reusable on normal exit");
    }

    private static void RunSharedPreferenceChecks(string root, string photos)
    {
        string profile = Path.Combine(root, "SharedPreferences"); Directory.CreateDirectory(profile);
        string session = Path.Combine(profile, "session.json");
        var seed = new SessionStore(session);
        seed.SaveBrowserPreferences(new BrowserPreferences { FavoriteFolders = new List<string> { photos } });
        var first = new SessionStore(session); var second = new SessionStore(session);
        var a = first.LoadBrowserPreferences(); var b = second.LoadBrowserPreferences();
        a.FavoriteDetails.Add(new FavoriteFolderDetails { Path = photos, Name = "First alias", Description = "Originals" });
        first.SaveBrowserPreferences(a);
        string added = Path.Combine(root, "Another favorite");
        b.FavoriteFolders.Add(added); b.ExpandedNodes.Add("branch-b"); second.SaveBrowserPreferences(b);
        var latest = seed.LoadBrowserPreferences();
        Assert(latest.FavoriteFolders.Contains(added) && latest.FavoriteDetails.Single().Name == "First alias", "stale window preserves remote alias and adds favorite");
        first.SaveBrowserPreferences(a);
        latest = seed.LoadBrowserPreferences();
        Assert(latest.FavoriteFolders.Contains(added) && latest.ExpandedNodes.Contains("branch-b"), "later autosave preserves unseen favorites and expansion");
        a.FavoriteFolders.Remove(photos); a.FavoriteDetails.Clear(); first.SaveBrowserPreferences(a);
        second.SaveBrowserPreferences(b);
        latest = seed.LoadBrowserPreferences();
        Assert(!latest.FavoriteFolders.Contains(photos) && latest.FavoriteFolders.Contains(added) && latest.FavoriteDetails.Count == 0,
            "stale window cannot resurrect removed favorite");
        a.FavoriteFolders.Add(photos); first.SaveBrowserPreferences(a);
        latest = seed.LoadBrowserPreferences();
        Assert(latest.FavoriteFolders.Contains(photos) && latest.FavoriteFolders.Contains(added), "explicit re-pin adds back favorite");
    }

    private static Process StartFixtureProcess(string exe, string arguments, string profile, bool captureOutput = false)
    {
        var start = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = captureOutput, RedirectStandardError = captureOutput
        };
        start.EnvironmentVariables["ZONER_VIEWER_PROFILE"] = profile;
        return Process.Start(start);
    }

    private static void CleanupFixtureProcesses(List<Process> processes)
    {
        // Only children created by this test against generated profiles are eligible for cleanup.
        foreach (Process process in processes)
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(3000)) { process.Kill(); process.WaitForExit(5000); }
            }
            process.Dispose();
        }
    }

    private static void RunExecutableInstanceChecks(string root, string photos)
    {
        string profile = Path.Combine(root, "ExecutableProfile"); Directory.CreateDirectory(profile);
        new SessionStore(Path.Combine(profile, "session.json")).Save(InstanceState(photos, "actual-app", 1100));
        string exe = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "MyokenImageViewer", "MyokenImageViewer.exe"));
        var processes = new List<Process>();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                Process process = StartFixtureProcess(exe, "", profile); processes.Add(process);
                Wait(delegate { process.Refresh(); return !process.HasExited && process.MainWindowHandle != IntPtr.Zero; }, "executable instance starts");
            }
            Wait(delegate { return processes.All(process => { process.Refresh(); return !process.HasExited && process.Responding; }); }, "three responsive app instances");
            Wait(delegate { return processes.Any(process => { process.Refresh(); return process.MainWindowTitle.EndsWith("Window 3"); }); }, "instance titles");
            Assert(processes.All(process => process.MainWindowTitle.StartsWith(BuildInfo.WindowTitle)), "all executable windows use Myoken Image Viewer branding");
            Process third = processes.Single(process => process.MainWindowTitle.EndsWith("Window 3"));
            // This intentional interruption verifies OS lease recovery, never a user's running viewer.
            third.Kill(); third.WaitForExit(5000);
            Process replacement = StartFixtureProcess(exe, "", profile); processes.Add(replacement);
            Wait(delegate { replacement.Refresh(); return !replacement.HasExited && replacement.MainWindowTitle.EndsWith("Window 3"); }, "restart after interrupted instance");
            foreach (Process process in processes.Where(item => item != third))
            {
                Assert(process.CloseMainWindow(), "app accepts normal close");
                Wait(delegate { return process.HasExited; }, "app exits normally");
                Assert(process.ExitCode == 0, "app instance exits without crash");
            }
            for (int number = 1; number <= 3; number++)
            {
                string path = number == 1 ? Path.Combine(profile, "session.json") : Path.Combine(profile, "instances", "session-" + number + ".json");
                var state = new SessionStore(path).Load();
                Assert(state.ActiveTabId == "actual-app" && state.Tabs.Single().IsPinned, "actual app saves its own session slot");
            }
            Console.WriteLine("PASS: three real app processes, normal exit and interrupted-instance relaunch.");
        }
        finally { CleanupFixtureProcesses(processes); }
    }

    private static void RunConcurrentStorageChecks(string root, string image)
    {
        string profile = Path.Combine(root, "ConcurrentProfile"); Directory.CreateDirectory(profile);
        File.Copy(image, Path.Combine(profile, "Source.png"));
        var processes = new List<Process>();
        try
        {
            for (int i = 0; i < 3; i++) processes.Add(StartFixtureProcess(typeof(ViewerRegressionTests).Assembly.Location,
                "--storage-worker \"" + profile + "\"", profile, true));
            Wait(delegate { return Directory.GetFiles(profile, "ready-*.txt").Length == 3; }, "concurrent storage workers ready");
            File.WriteAllText(Path.Combine(profile, "go.txt"), "go");
            Wait(delegate { return processes.All(process => process.HasExited); }, "concurrent storage writers");
            foreach (Process process in processes)
                Assert(process.ExitCode == 0, "shared storage worker failed: " + process.StandardOutput.ReadToEnd()
                    + process.StandardError.ReadToEnd());
            var store = new SessionStore(Path.Combine(profile, "session.json"));
            var preferences = store.LoadBrowserPreferences();
            Assert(preferences.FavoriteFolders.Count == 3 && preferences.FavoriteDetails.Count == 3, "cross-process favorite updates merged");
            Assert(store.LoadNamed("Concurrent").Tabs.Count == 1, "concurrent named-session writes remain readable");
            Assert(Directory.GetFiles(profile, "*.tmp", SearchOption.AllDirectories).Length == 0, "atomic writes leave no temporary files");
            using (var services = AppServices.CreateInstance(profile))
            {
                var item = ImageFileItem.FromPath(Path.Combine(profile, "Source.png"));
                Assert(services.Thumbnails.GetThumbnailAsync(item, 256, CancellationToken.None).Result.PixelWidth == 256,
                    "shared thumbnail cache readable after concurrent fills");
                string cached = Directory.GetFiles(Path.Combine(profile, "thumbs"), "*.jpg", SearchOption.AllDirectories).Single();
                File.WriteAllText(cached, "damaged test cache");
                Assert(services.Thumbnails.GetThumbnailAsync(item, 256, CancellationToken.None).Result.PixelWidth == 256,
                    "damaged shared cache rebuilt without retained file handle");
                using (var stream = new FileStream(cached, FileMode.Open, FileAccess.Read, FileShare.None))
                    Assert(stream.ReadByte() == 0xff && stream.ReadByte() == 0xd8, "repaired cache is JPEG and releases read handles");
            }
            Console.WriteLine("PASS: cross-process shared favorites, named sessions and thumbnail cache stress.");
        }
        finally { CleanupFixtureProcesses(processes); }
    }

    private static int RunStorageWorker(string profile)
    {
        using (var services = AppServices.CreateInstance(profile))
        {
            int number = services.InstanceNumber;
            var preferences = services.Sessions.LoadBrowserPreferences();
            string favorite = Path.Combine(profile, "Favorite-" + number);
            preferences.FavoriteFolders.Add(favorite);
            preferences.FavoriteDetails.Add(new FavoriteFolderDetails { Path = favorite, Name = "Window " + number });
            File.WriteAllText(Path.Combine(profile, "ready-" + number + ".txt"), "ready");
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!File.Exists(Path.Combine(profile, "go.txt")) && DateTime.UtcNow < deadline) Thread.Sleep(50);
            Assert(File.Exists(Path.Combine(profile, "go.txt")), "worker start barrier");
            var state = InstanceState(profile, "worker-" + number, 1000 + number);
            var item = ImageFileItem.FromPath(Path.Combine(profile, "Source.png"));
            for (int i = 0; i < 20; i++)
            {
                services.Sessions.SaveBrowserPreferences(preferences);
                services.Sessions.SaveNamed("Concurrent", state);
                services.Sessions.Save(state);
                Assert(services.Thumbnails.GetThumbnailAsync(item, 256, CancellationToken.None).Result.PixelWidth == 256, "worker thumbnail");
                Assert(services.Sessions.Load().ActiveTabId == state.ActiveTabId, "worker keeps independent session");
            }
        }
        return 0;
    }
}
