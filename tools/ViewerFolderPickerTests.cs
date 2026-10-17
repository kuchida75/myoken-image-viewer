using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunFolderPickerChecks()
    {
        string root = Path.Combine(Root, "folder-picker-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string album2 = Path.Combine(root, "Album2"), album11 = Path.Combine(root, "Album11");
        Directory.CreateDirectory(album2); Directory.CreateDirectory(album11); Directory.CreateDirectory(Path.Combine(album2, "Nested"));
        File.WriteAllText(Path.Combine(root, "Not a folder.txt"), "fixture");
        var window = new FolderPickerWindow(root); window.Show();
        Wait(delegate { return window.PendingWork != null && window.PendingWork.IsCompleted; }, "Themed folder picker loads");
        var folders = Field<ListBox>(window, "_folders");
        Assert(folders.Items.Cast<FolderPickerEntry>().Select(e => e.Name).SequenceEqual(new[] { "Album2", "Album11" }), "Chooser lists only folders in natural order");
        Assert(window.SelectedFolder == null && Field<Button>(window, "_select").IsEnabled, "Browsing alone never commits a folder selection");
        foreach (bool dark in new[] { true, false })
        {
            ThemeManager.SetDarkTheme(dark); window.Width = dark ? 760 : 560; window.Height = dark ? 570 : 400; Pump();
            var background = (SolidColorBrush)folders.Background; var expected = (SolidColorBrush)Application.Current.Resources[ThemeKeys.PaneBackground];
            Assert(background.Color == expected.Color && (dark ? background.Color.R < 70 : background.Color.R > 200), "Folder list follows the app's dark/light theme");
            AssertInside(Field<Button>(window, "_select"), (FrameworkElement)window.Content);
            AssertInside(Field<FolderLocationBar>(window, "_location"), (FrameworkElement)window.Content);
            Render(window, dark ? "folder-picker-dark.png" : "folder-picker-light-narrow.png");
        }
        ThemeManager.SetDarkTheme(true);
        var location = Field<FolderLocationBar>(window, "_location"); location.BeginEdit(); location.Address.Text = "Album2";
        Assert(location.Commit(), "Relative folder addresses navigate"); Wait(delegate { return window.PendingWork.IsCompleted; }, "Child folder loads");
        Assert(folders.Items.Cast<FolderPickerEntry>().Single().Name == "Nested", "Address navigation loads the correct children");
        Assert(window.Navigate(album2 + Path.DirectorySeparatorChar), "Trailing separator is accepted"); Wait(delegate { return window.PendingWork.IsCompleted; }, "Trailing separator loads");
        Invoke(window, "GoUp"); Wait(delegate { return window.PendingWork.IsCompleted; }, "Parent folder loads");
        Assert(folders.Items.Count == 2, "Parent navigation is correct after a trailing separator");
        Assert(!window.Navigate(Path.Combine(root, "Missing")) && window.SelectedFolder == null, "Invalid path cannot commit a selection");
        folders.SelectedItem = folders.Items.Cast<FolderPickerEntry>().Single(e => e.Path == album11); window.Confirm();
        Assert(!window.IsVisible && window.SelectedFolder == album11 && Directory.Exists(album11), "Select folder commits the selected existing child without modifying it");
        var canceled = new FolderPickerWindow(root); canceled.Show(); Pump(); canceled.Close();
        Wait(delegate { return canceled.PendingWork == null || canceled.PendingWork.IsCompleted; }, "Closed picker cancels its pending enumeration");
        Assert(canceled.SelectedFolder == null, "Cancel/close returns no folder selection");

        var faces = new FacesWindow(root, 4, "Auto", 4096); faces.Show(); Pump();
        Exception modalError = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += delegate
        {
            var picker = faces.OwnedWindows.OfType<FolderPickerWindow>().SingleOrDefault();
            if (picker == null || picker.PendingWork == null || !picker.PendingWork.IsCompleted) return;
            timer.Stop();
            try
            {
                Assert(picker.Owner == faces && picker.Title == "Choose a folder to scan", "Faces opens the themed owned chooser");
                var items = Field<ListBox>(picker, "_folders"); items.SelectedItem = items.Items.Cast<FolderPickerEntry>().Single(e => e.Path == album2); picker.Confirm();
            }
            catch (Exception error) { modalError = error; picker.Close(); }
        };
        timer.Start();
        try { Invoke(faces, "ChooseFolder"); } finally { timer.Stop(); }
        if (modalError != null) throw modalError;
        Assert(Field<TextBox>(faces, "_folder").Text == album2, "Confirmed picker selection updates the Faces scan folder"); faces.Close();
        MakeImage(Path.Combine(album2, "Preview.png"), 320, 240, 96);
        var services = AppServices.CreateInstance(Path.Combine(Root, "picker-view-profile-" + Guid.NewGuid().ToString("N")));
        var views = new FolderPickerWindow(root, services.Thumbnails); views.Show();
        try
        {
            Wait(delegate { return views.PendingWork != null && views.PendingWork.IsCompleted; }, "Folder view chooser loads");
            var mode = Field<ComboBox>(views, "_viewMode"); var rows = Field<ListBox>(views, "_folders");
            rows.SelectedItem = rows.Items.Cast<FolderPickerEntry>().Single(e => e.Path == album2);
            mode.SelectedIndex = 1; Pump();
            Assert(Field<Grid>(views, "_detailsHeader").IsVisible && rows.ItemTemplate != null && rows.Items.Cast<FolderPickerEntry>().All(e => e.ModifiedUtc > DateTime.MinValue), "Details view displays folder names and modified dates");
            Render(views, "folder-picker-details.png");
            mode.SelectedIndex = 2; PumpZoomFor(350);
            var grid = Field<VirtualizedThumbnailGrid>(views, "_thumbnailFolders");
            Assert(grid.IsVisible && grid.ItemCount == 2 && grid.SelectedItem.Path == album2, "Thumbnail view retains folder selection and virtualizes folder previews");
            Render(views, "folder-picker-thumbnails.png");
            mode.SelectedIndex = 0; Pump(); Assert(((FolderPickerEntry)rows.SelectedItem).Path == album2, "List view retains the selected folder after thumbnail mode");
            mode.SelectedIndex = 2; Pump(); views.Confirm(); Assert(views.SelectedFolder == album2, "Thumbnail-mode selection confirms the correct folder");
        }
        finally { views.Close(); Pump(); services.Dispose(); }
        CheckNormalOpenFolder(root, album2);
        Console.WriteLine("PASS: themed Faces/main Open folder chooser, view modes, natural ordering, address/parent navigation, validation, confirm/cancel, normal browsing integration, async close and dark/light/narrow layouts.");
    }
    private static void CheckNormalOpenFolder(string root, string album)
    {
        var services = AppServices.CreateInstance(Path.Combine(Root, "open-picker-profile-" + Guid.NewGuid().ToString("N")));
        services.Sessions.Save(new SessionState { LastFolder = root, WindowWidth = 1200, WindowHeight = 800 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            foreach (bool dark in new[] { true, false })
            {
                ThemeManager.SetDarkTheme(dark); bool seen = false; Exception failure = null; var elapsed = System.Diagnostics.Stopwatch.StartNew();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
                timer.Tick += delegate
                {
                    var picker = window.OwnedWindows.OfType<FolderPickerWindow>().SingleOrDefault();
                    if (picker == null || picker.PendingWork == null || !picker.PendingWork.IsCompleted)
                    { if (elapsed.Elapsed.TotalSeconds > 20) { failure = new Exception("Main Open folder chooser did not load"); timer.Stop(); if (picker != null) picker.Close(); } return; }
                    timer.Stop(); seen = true;
                    try
                    {
                        Assert(picker.Owner == window && picker.Title == "Open a folder", "Main Open folder uses the owned, correctly titled themed chooser");
                        var list = Field<ListBox>(picker, "_folders"); var color = ((SolidColorBrush)list.Background).Color;
                        Assert(dark ? color.R < 70 : color.R > 200, "Normal Open follows the app's current dark/light theme");
                        Assert(((ComboBoxItem)Field<ComboBox>(picker, "_viewMode").Items[2]).IsEnabled, "Normal Open includes cached thumbnail view");
                        if (!dark) { picker.Width = 560; picker.Height = 400; } Pump();
                        AssertInside(Field<Button>(picker, "_select"), (FrameworkElement)picker.Content);
                        Render(picker, dark ? "main-open-folder-dark.png" : "main-open-folder-light-narrow.png");
                        if (dark) { list.SelectedItem = list.Items.Cast<FolderPickerEntry>().Single(e => e.Path == album); picker.Confirm(); }
                        else picker.Close();
                    }
                    catch (Exception error) { failure = error; picker.Close(); }
                };
                timer.Start(); try { Invoke(window, "OpenFolderPicker"); } finally { timer.Stop(); }
                if (failure != null) throw failure; Assert(seen, "Normal Open chooser was observed"); WaitScan(window);
                Assert(Field<string>(window, "_currentFolder") == album, dark ? "Confirm navigates the main browser to the selected folder" : "Cancel preserves the current browser folder");
            }
        }
        finally { window.Close(); Pump(); services.Dispose(); ThemeManager.SetDarkTheme(true); }
    }
}
