using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunMinimapChecks()
    {
        string root = Path.Combine(Root, "browser-workspace-" + Guid.NewGuid().ToString("N"));
        string photos = Path.Combine(root, "Photos"); Directory.CreateDirectory(photos);
        string first = Path.Combine(photos, "Photo000.png"); MakeImage(first, 360, 240, 96);
        for (int i = 1; i < 120; i++) File.Copy(first, Path.Combine(photos, "Photo" + i.ToString("000") + ".png"));
        using (var services = AppServices.Create(Path.Combine(root, "Profile")))
        {
            services.Sessions.Save(new SessionState { LastFolder = photos, WindowWidth = 1240, WindowHeight = 820,
                ThumbnailMinimapVisible = true, ShortcutOverrides = new List<ShortcutOverride> {
                    new ShortcutOverride { Action = "minimap", Keys = ShortcutKeys(Key.T) },
                    new ShortcutOverride { Action = "metadata", Keys = ShortcutKeys(Key.F9) } } });
            var window = new MainWindow(services); window.Show(); WaitScan(window);
            try
            {
                var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
                var workspace = Field<Grid>(window, "_thumbnailWorkspace");
                Assert(grid.ItemCount == 120 && grid.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "browser retains normal scrolling");
                AssertNear(grid.ActualWidth, workspace.ActualWidth, "thumbnail browser fills width without a minimap rail or divider");
                Assert(!Capture(window).ThumbnailMinimapVisible, "old minimap preference cannot revive removed UI");
                var map = Field<ShortcutMap>(window, "_shortcuts");
                Assert(!map.Commands.Any(command => command.Id == "minimap") && map.Match(Key.T, ModifierKeys.None, true) == null, "minimap command and default T binding removed");
                Assert(map.Match(Key.F9, ModifierKeys.None, false).Id == "metadata", "other legacy shortcut assignments are retained");
                var menu = (ContextMenu)Invoke(window, "BuildViewOptionsMenu");
                Assert(!menu.Items.OfType<MenuItem>().Any(item => item.Header != null && item.Header.ToString().ToLowerInvariant().Contains("minimap")), "View menu has no browser minimap");
                grid.ScrollToVerticalOffset(grid.ScrollableHeight); Pump();
                Assert(grid.VerticalOffset > 0, "standard scrollbar reaches later images");
                Assert(grid.HandleNavigationKey(Key.Home, ModifierKeys.None), "Home still navigates thumbnails"); Pump();
                Assert(grid.SelectedItem.Path == first, "Home selects first image");
                Assert(grid.HandleNavigationKey(Key.End, ModifierKeys.None), "End still navigates thumbnails"); Pump();
                Assert(grid.SelectedItem.Name == "Photo119.png", "End selects last image");
                string backup = Path.Combine(root, "backup.json");
                ViewerBackupStore.Save(backup, (ViewerBackupDocument)Invoke(window, "CaptureBackupSnapshot"));
                Assert(!ViewerBackupStore.Load(backup).Workspace.ThumbnailMinimapVisible, "new backups do not restore the retired minimap");
                ThemeManager.SetDarkTheme(false); window.Width = 980; Pump();
                AssertNear(grid.ActualWidth, workspace.ActualWidth, "narrow/light browser has no reserved minimap width");
                Render((FrameworkElement)window.Content, "browser-without-minimap.png");
                Invoke(window, "OpenBrowserImage", first); Wait(delegate { return Ready(window, first); }, "image view after removing browser minimap");
                Invoke(window, "SetImageNavigator", true); Invoke(window, "ZoomFromCenter", 10.0); Pump();
                Assert(Field<System.Windows.Controls.Border>(window, "_imageNavigatorOverlay").IsVisible, "image-view pan overview remains available");
            }
            finally { window.Close(); Pump(); ThemeManager.SetDarkTheme(true); }
        }
        Console.WriteLine("PASS: browser minimap/rail/command removal, full thumbnail width and normal scrollbar/navigation, legacy preferences/shortcuts/backup compatibility, retained image pan overview.");
    }
}
