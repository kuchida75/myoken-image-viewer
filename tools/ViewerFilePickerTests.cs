using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void WithFilePicker(Window owner, Action open, Action<FolderPickerWindow> choose)
    {
        bool seen = false; Exception failure = null; var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += delegate
        {
            var picker = owner.OwnedWindows.OfType<FolderPickerWindow>().FirstOrDefault();
            if (picker == null || picker.PendingWork == null || !picker.PendingWork.IsCompleted)
            { if (elapsed.Elapsed.TotalSeconds > 20) { failure = new Exception("File chooser did not finish loading"); timer.Stop(); if (picker != null) picker.Close(); else owner.Close(); } return; }
            timer.Stop(); seen = true;
            try { choose(picker); }
            catch (Exception error) { failure = error; picker.Close(); }
        };
        timer.Start(); try { open(); } finally { timer.Stop(); }
        if (failure != null) throw failure; Assert(seen, "An owned themed chooser was displayed");
    }
    private static bool PickerChoice(Window owner, FileDialog options, Action<FolderPickerWindow> choose)
    {
        bool accepted = false; WithFilePicker(owner, delegate { accepted = ThemedFileDialogs.Show(options, owner); }, choose); return accepted;
    }
    private static void FileReplacementChoice(FolderPickerWindow picker, bool accept)
    {
        bool seen = false; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += delegate
        {
            var prompt = picker.OwnedWindows.OfType<FileReplaceWindow>().FirstOrDefault(); if (prompt == null) return;
            timer.Stop(); seen = true; Render(prompt, accept ? "file-replace-dark.png" : "file-replace-cancel-dark.png");
            if (accept) prompt.Accept(); else prompt.Close();
        };
        timer.Start(); try { picker.Confirm(); } finally { timer.Stop(); }
        Assert(seen, "Overwrite uses an app-themed confirmation window");
    }
    private static void RunFilePickerChecks()
    {
        string root = Path.Combine(Root, "file-pickers-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(Path.Combine(root, "Album2")); Directory.CreateDirectory(Path.Combine(root, "Album11"));
        string photo = Path.Combine(root, "Photo.png"), jpeg = Path.Combine(root, "Photo2.JPG"); MakeImage(photo, 320, 240, 96); File.WriteAllText(jpeg, "filter fixture");
        File.WriteAllText(Path.Combine(root, "Config.json"), "{}"); File.WriteAllText(Path.Combine(root, "notes"), "no extension");
        var filters = FilePickerFilter.Parse("Images|*.jpg;*.png|All files|*.*");
        Assert(filters[0].Matches("PHOTO.JPG") && filters[0].Matches("image.png") && !filters[0].Matches("image.png.exe") && filters[1].Matches("notes"), "File filters handle multiple patterns, case and extensionless files");
        var owner = new Window { Width = 600, Height = 400 }; ThemeManager.PrepareWindow(owner); owner.Show();
        try
        {
            foreach (bool dark in new[] { true, false })
            {
                ThemeManager.SetDarkTheme(dark);
                var options = new OpenFileDialog { Title = "Open canvas", InitialDirectory = root, Filter = "Images|*.png;*.jpg|JSON|*.json", CheckFileExists = true };
                bool accepted = PickerChoice(owner, options, picker =>
                {
                    var rows = Field<ListBox>(picker, "_folders"); var entries = rows.Items.Cast<FolderPickerEntry>().ToArray();
                    Assert(entries.Select(e => e.Name).SequenceEqual(new[] { "Album2", "Album11", "Photo.png", "Photo2.JPG" }), "File chooser lists folders first and applies the active filter naturally");
                    var color = ((SolidColorBrush)rows.Background).Color; Assert(dark ? color.R < 70 : color.R > 200, "Open files follows the app theme");
                    if (!dark) { picker.Width = 560; picker.Height = 400; } Pump();
                    foreach (var control in new FrameworkElement[] { Field<TextBox>(picker, "_fileName"), Field<ComboBox>(picker, "_fileType"), Field<Button>(picker, "_select"), Field<FolderLocationBar>(picker, "_location") }) AssertInside(control, (FrameworkElement)picker.Content);
                    Render(picker, dark ? "file-open-dark.png" : "file-open-light-narrow.png");
                    rows.SelectedItem = entries.Single(e => e.Path == photo); picker.Confirm();
                });
                Assert(accepted && options.FileName == photo, "Open returns the selected existing file");
            }
            ThemeManager.SetDarkTheme(true);
            foreach (string name in new[] { " leading.png", "100%USERPROFILE%.png", "\u00fc image.png" })
            {
                string special = Path.Combine(root, name); File.WriteAllText(special, "filename fixture");
                var exact = new OpenFileDialog { InitialDirectory = root, Filter = "PNG|*.png", CheckFileExists = true };
                Assert(PickerChoice(owner, exact, picker =>
                {
                    var rows = Field<ListBox>(picker, "_folders"); rows.SelectedItem = rows.Items.Cast<FolderPickerEntry>().Single(e => e.Path == special); picker.Confirm();
                }) && exact.FileName == special, "Selected Unicode, leading-space and percent-variable filenames retain their literal names");
            }
            var save = new SaveFileDialog { Title = "Save canvas", InitialDirectory = root, FileName = "Backup.zip", Filter = "ZIP|*.zip|JSON|*.json", DefaultExt = ".zip", AddExtension = true };
            Assert(PickerChoice(owner, save, picker =>
            {
                var type = Field<ComboBox>(picker, "_fileType"); type.SelectedIndex = 1; Wait(delegate { return picker.PendingWork.IsCompleted; }, "Filter changes reload entries");
                Assert(Field<TextBox>(picker, "_fileName").Text == "Backup.json", "Save type changes update the proposed extension");
                Field<TextBox>(picker, "_fileName").Text = "My backup"; Render(picker, "file-save-dark.png"); picker.Confirm();
            }) && save.FileName == Path.Combine(root, "My backup.json") && save.FilterIndex == 2 && !File.Exists(save.FileName), "Save returns the right extension/type without writing a file");
            var existing = new SaveFileDialog { InitialDirectory = root, FileName = "Config.json", Filter = "JSON|*.json", OverwritePrompt = true };
            byte[] unchanged = File.ReadAllBytes(Path.Combine(root, "Config.json"));
            Assert(PickerChoice(owner, existing, picker =>
            {
                FileReplacementChoice(picker, false); Assert(picker.IsVisible && picker.SelectedFile == null, "Cancel overwrite keeps the file chooser open");
                FileReplacementChoice(picker, true);
            }) && File.ReadAllBytes(existing.FileName).SequenceEqual(unchanged), "Overwrite consent does not itself modify existing data");
            existing.OverwritePrompt = false; Assert(PickerChoice(owner, existing, picker => picker.Confirm()), "Disabled overwrite prompts remain disabled");
            var missing = new OpenFileDialog { InitialDirectory = root, Filter = "All files|*.*", CheckFileExists = true };
            Assert(!PickerChoice(owner, missing, picker =>
            {
                var name = Field<TextBox>(picker, "_fileName"); name.Text = "Missing.png"; picker.Confirm();
                Assert(picker.IsVisible && picker.SelectedFile == null && Field<TextBlock>(picker, "_status").Text.Contains("does not exist"), "Open rejects missing files without closing");
                foreach (string invalid in new[] { "CON.png", "file:stream.png", "trailing. " }) { name.Text = invalid; picker.Confirm(); Assert(picker.SelectedFile == null, "Invalid/reserved names are refused"); }
                picker.Close();
            }), "Cancel returns no file");
            var folders = new FolderPickerWindow(root); folders.Show(); Wait(delegate { return folders.PendingWork != null && folders.PendingWork.IsCompleted; }, "New-folder chooser");
            Assert(!folders.CreateFolder("../outside") && folders.CreateFolder("Created"), "New folder rejects paths and creates only a valid child");
            Wait(delegate { return folders.PendingWork.IsCompleted; }, "Created folder loads"); folders.Confirm(); Assert(folders.SelectedFolder == Path.Combine(root, "Created"), "New folder can be used as the destination");
        }
        finally { owner.Close(); Pump(); ThemeManager.SetDarkTheme(true); }
        CheckThemedSaveAndMove(root, photo);
        CheckThemedCanvasDialogs(root, photo);
        Console.WriteLine("PASS: dark/light/narrow file dialogs, natural folders-first listing, filter/extension handling, missing/reserved names, overwrite cancel/accept/opt-out, no picker writes, new folders, actual Enhance Save As, Move and canvas Save/Open integration.");
    }
    private static void CheckThemedSaveAndMove(string root, string photo)
    {
        var services = AppServices.CreateInstance(Path.Combine(root, "Profile")); services.Sessions.Save(new SessionState { LastFolder = root, WindowWidth = 1200, WindowHeight = 800 });
        var window = new MainWindow(services); window.Show(); WaitScan(window);
        try
        {
            Invoke(window, "OpenBrowserImage", photo); Wait(delegate { return Ready(window, photo); }, "Save As source opens");
            string output = Path.Combine(root, "Enhanced.png"); byte[] original = File.ReadAllBytes(photo);
            WithFilePicker(window, delegate { Invoke(window, "BeginEnhanceSave", true); }, picker => { Field<TextBox>(picker, "_fileName").Text = output; picker.Confirm(); });
            WaitBackup(Field<Task<EnhancedSaveResult>>(window, "_enhanceSaveTask")); Wait(delegate { return Ready(window, output); }, "Themed Save As output opens");
            Assert(File.Exists(output) && File.ReadAllBytes(photo).SequenceEqual(original), "Themed Enhance Save As saves the copy and preserves the source");
            string move = Path.Combine(root, "MoveMe.png"), destination = Path.Combine(root, "Album11"); MakeImage(move, 64, 64, 96);
            WithFilePicker(window, delegate { Invoke(window, "ChooseMoveDestination", (object)new[] { move }); }, picker =>
            {
                var list = Field<ListBox>(picker, "_folders"); list.SelectedItem = list.Items.Cast<FolderPickerEntry>().Single(e => e.Path == destination); picker.Confirm();
            });
            Wait(delegate { return !Field<bool>(window, "_fileTransferBusy") && File.Exists(Path.Combine(destination, "MoveMe.png")); }, "Themed Move completes");
            Assert(!File.Exists(move), "Move uses the selected themed destination");
        }
        finally { window.Close(); Pump(); services.Dispose(); }
    }
    private static void CheckThemedCanvasDialogs(string root, string photo)
    {
        var review = new FaceScanResult(); review.Faces.Add(FaceFixture(FaceImages.Inspect(photo, CancellationToken.None), FaceSolid(128, 128, 80, 140, 180)));
        var window = new FacesWindow(root, 4, "Auto", 4096); window.Show(); window.SetReview(review); Field<TabControl>(window, "_tabs").SelectedIndex = 1; Pump();
        try
        {
            var fields = Field<object>(window, "_interactive"); Field<Slider>(fields, "WidthSlider").Value = 512; Wait(delegate { return LiveCanvasIdle(window); }, "Canvas dialog fixture");
            string file = Path.Combine(root, "Saved.myokenfaces");
            WithFilePicker(window, delegate { Invoke(window, "SaveCanvas"); }, picker => { Field<TextBox>(picker, "_fileName").Text = file; picker.Confirm(); });
            Wait(delegate { return window.PendingWork.IsCompleted; }, "Themed canvas save completes"); Assert(File.Exists(file) && window.CanvasPath == file, "Save canvas uses the themed picker");
            WithFilePicker(window, delegate { Invoke(window, "OpenCanvas"); }, picker => { Field<TextBox>(picker, "_fileName").Text = file; picker.Confirm(); });
            Wait(delegate { return window.PendingWork.IsCompleted; }, "Themed canvas open completes"); Assert(window.CanvasPath == file && Field<Image>(window, "_preview").Source != null, "Open canvas restores the map through the themed picker");
        }
        finally { window.Close(); Pump(); }
    }
}
