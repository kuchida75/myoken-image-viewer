using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void CheckFacesOpening(MainWindow owner, Action open, string expectedFolder, bool autoScan = false)
    {
        bool seen = false; Exception failure = null; var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += delegate
        {
            var dialog = owner.OwnedWindows.OfType<FacesWindow>().FirstOrDefault();
            if (dialog == null)
            {
                if (clock.Elapsed.TotalSeconds > 15) { failure = new Exception("Faces did not open"); timer.Stop(); owner.Close(); }
                return;
            }
            timer.Stop(); seen = true;
            try
            {
                Assert(Field<TextBox>(dialog, "_folder").Text == expectedFolder, "Faces uses the correct context folder");
                Assert((autoScan ? dialog.PendingWork != null && !dialog.PendingWork.IsCompleted : dialog.PendingWork == null)
                    && Field<ListBox>(dialog, "_review").Items.Count == 0, "F6 starts scanning automatically; manual entry waits for Scan faces");
            }
            catch (Exception error) { failure = error; }
            finally { dialog.Close(); }
        };
        if (autoScan) FaceImages.WorkGate.Wait();
        timer.Start(); try { open(); } finally { timer.Stop(); if (autoScan) FaceImages.WorkGate.Release(); }
        if (failure != null) throw failure;
        Assert(seen && Field<FacesWindow>(owner, "_facesWindow") == null && !Field<bool>(owner, "_fileTransferBusy"), "Faces closes and releases the modal file-operation guard");
        Pump();
    }

    private static void RunFacesAccessChecks()
    {
        string error; var map = new ShortcutMap();
        Assert(map.Load(null, out error) && map.Match(Key.F6, ModifierKeys.None, true).Id == "faces"
            && map.Match(Key.F6, ModifierKeys.None, false).Id == "faces", "F6 opens Faces from both views");
        Assert(map.Load(new List<ShortcutOverride> { new ShortcutOverride { Action = "help", Keys = ShortcutKeys(Key.F6) } }, out error)
            && map.Display("faces") == "Unassigned" && map.Match(Key.F6, ModifierKeys.None, true).Id == "help", "Existing custom F6 assignments take priority over the new default");
        map.ResetAll(); Assert(map.Set("faces", ShortcutKeys(Key.F7), out error), "Faces can be rebound");
        var restored = new ShortcutMap(); Assert(restored.Load(map.Export(), out error) && restored.Display("faces") == "F7", "Faces binding survives export/import");
        string root = Path.Combine(Root, "faces-access-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string photos = Path.Combine(root, "Photos"), child = Path.Combine(photos, "Child"); Directory.CreateDirectory(child);
        string image = Path.Combine(photos, "Photo.png"); MakeImage(image, 640, 480, 96);
        var services = AppServices.CreateInstance(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1440, WindowHeight = 900 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid"); grid.Focus();
            CheckFacesOpening(window, delegate { Assert((bool)Invoke(window, "RunShortcut", Key.F6, ModifierKeys.None, false), "F6 is handled in browser"); }, photos, true);
            Assert((bool)Invoke(window, "RunShortcut", Key.F6, ModifierKeys.None, true) && Field<FacesWindow>(window, "_facesWindow") == null, "Held F6 does not reopen Faces");
            grid.SelectItem(grid.ItemAt(0)); grid.Focus();
            Assert(grid.SelectedItem.Path == child, "Folder fixture is selected in the browser");
            CheckFacesOpening(window, delegate { Invoke(window, "RunShortcut", Key.F6, ModifierKeys.None, false); }, child, true);
            grid.SelectItem(grid.ItemAt(1)); grid.Focus();
            CheckFacesOpening(window, delegate { Invoke(window, "RunShortcut", Key.F6, ModifierKeys.None, false); }, photos, true);
            grid.SelectItem(null);
            var tiles = Field<Dictionary<int, ThumbnailTile>>(grid, "_realized");
            foreach (string path in new[] { image, child })
            {
                var tile = tiles.Values.Single(t => t.Item.Path == path); Invoke(grid, "UpdateMenu", tile.ContextMenu);
                var menu = tile.ContextMenu.Items.OfType<MenuItem>().Single(m => (string)m.Tag == "faces");
                Assert(menu.IsEnabled && menu.Visibility == Visibility.Visible && menu.InputGestureText == "F6", "Image/folder menus expose Faces with its shortcut");
                CheckFacesOpening(window, delegate { menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }, path == child ? child : photos);
            }
            Invoke(grid, "UpdateMenu", grid.ContextMenu);
            var background = grid.ContextMenu.Items.OfType<MenuItem>().Single(m => (string)m.Tag == "faces");
            CheckFacesOpening(window, delegate { background.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }, photos);
            var bindings = Field<ShortcutMap>(window, "_shortcuts"); Assert(bindings.Set("faces", ShortcutKeys(Key.F7), out error), "Window Faces shortcut can change");
            Invoke(grid, "UpdateMenu", grid.ContextMenu); Assert(background.InputGestureText == "F7", "Context menu reflects current binding");
            Field<TextBox>(window, "_searchBox").Focus();
            CheckFacesOpening(window, delegate { Invoke(window, "RunShortcut", Key.F7, ModifierKeys.None, false); }, photos, true);
            Assert(bindings.Set("faces", ShortcutKeys(Key.K), out error), "Faces can use an ordinary key");
            Field<TextBox>(window, "_searchBox").Focus();
            Assert(!(bool)Invoke(window, "RunShortcut", Key.K, ModifierKeys.None, false), "Typing in search never opens Faces");
            bindings.ResetAll(); Invoke(window, "OpenImageTab", image, true); WaitGpuImage(window, image);
            Field<Canvas>(window, "_imageCanvas").Focus();
            CheckFacesOpening(window, delegate { Invoke(window, "RunShortcut", Key.F6, ModifierKeys.None, false); }, photos, true);
            Assert(Field<Grid>(window, "_imageView").IsVisible, "Closing Faces retains image view");
        }
        finally { window.Close(); Pump(); services.Dispose(); }
        Console.WriteLine("PASS: Faces browser/image shortcuts, automatic selected-folder/image-parent paths, rebind round-trip and legacy conflicts, repeat/text guards, thumbnail/folder/background menus and modal cleanup.");
    }
}
