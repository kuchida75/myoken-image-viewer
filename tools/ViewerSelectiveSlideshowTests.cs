using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using ZonerInspiredViewer;

internal static partial class ViewerRegressionTests
{
    private static void RunSelectiveSlideshowChecks()
    {
        string root = Path.Combine(Root, "selected-slideshow-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "Photos"), album = Path.Combine(folder, "Album");
        string nested = Path.Combine(album, "Nested"), empty = Path.Combine(folder, "Empty");
        Directory.CreateDirectory(nested); Directory.CreateDirectory(empty);
        string first = Path.Combine(folder, "A.png"), selected = Path.Combine(folder, "B.png");
        string ignored = Path.Combine(folder, "C.png"), one = Path.Combine(album, "Photo1.png");
        string two = Path.Combine(album, "Photo2.png"), ten = Path.Combine(album, "Photo10.png");
        string deep = Path.Combine(nested, "Deep.png");
        foreach (string path in new[] { first, selected, ignored, one, two, ten, deep }) MakeImage(path, 120, 80, 96);
        File.WriteAllText(Path.Combine(album, "notes.txt"), "Not an image");
        string backup = Path.Combine(album, ".viewer-enhance-backups");
        Directory.CreateDirectory(backup); MakeImage(Path.Combine(backup, "Backup.png"), 120, 80, 96);

        var source = new[] { ImageFileItem.FromDirectory(album), ImageFileItem.FromPath(selected) };
        string[] expected = { deep, one, two, ten, selected };
        var playlist = SelectedSlideshowPlaylist.Build(source, CancellationToken.None);
        Assert(playlist.Paths.SequenceEqual(expected), "selected folders recurse in natural order, then selected image");
        Assert(SelectedSlideshowPlaylist.Build(new[] { ImageFileItem.FromDirectory(empty) }, CancellationToken.None).Paths.Length == 0,
            "empty selected folder produces no slides");
        var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool cancellationObserved = false;
        try { SelectedSlideshowPlaylist.Build(source, cancelled.Token); }
        catch (OperationCanceledException) { cancellationObserved = true; }
        Assert(cancellationObserved, "playlist collection honors cancellation");

        var services = AppServices.Create(Path.Combine(root, "Profile"));
        services.Sessions.Save(new SessionState { LastFolder = folder, WindowWidth = 1200, WindowHeight = 760 });
        var window = new MainWindow(services);
        window.Show(); WaitScan(window);
        var grid = Field<VirtualizedThumbnailGrid>(window, "_thumbnailGrid");
        grid.RestoreSelection(new[] { album, selected }, album);
        grid.Focus(); PressKey(window, Key.F5);
        Wait(delegate { return Field<bool>(window, "_slideshowPlaying") && Ready(window, deep); }, "selected slideshow opens first nested image");
        Assert(Field<SlideshowSequence>(window, "_slideshowSequence").Count == expected.Length,
            "selected slideshow excludes unselected siblings and backup images");
        for (int i = 1; i < expected.Length; i++)
        {
            Invoke(window, "MoveSlideshow", 1);
            string path = expected[i];
            Wait(delegate { return Ready(window, path); }, "selected slideshow advances across folders");
        }
        Invoke(window, "ToggleSlideshowShuffle");
        Assert(Field<SlideshowSequence>(window, "_slideshowSequence").Count == expected.Length
            && expected.Contains(Field<SlideshowSequence>(window, "_slideshowSequence").Current),
            "shuffle preserves the selected playlist after folder changes");
        PressKey(window, Key.F5);
        Assert(!Field<bool>(window, "_slideshowPlaying"), "F5 stops selected slideshow");

        Invoke(window, "LoadFolder", folder); WaitScan(window);
        grid.SelectItem(null); PressKey(window, Key.F5);
        Wait(delegate { return Field<bool>(window, "_slideshowPlaying") && Ready(window, first); }, "F5 without selection plays current folder");
        Assert(Field<SlideshowSequence>(window, "_slideshowSequence").Count == 3, "default slideshow uses current folder only");
        PressKey(window, Key.F5);
        Invoke(window, "BrowseActiveTab"); WaitScan(window);
        grid.RestoreSelection(new[] { empty }, empty); grid.Focus(); PressKey(window, Key.F5);
        Wait(delegate { return Field<CancellationTokenSource>(window, "_slideshowSelectionCancellation") == null; }, "empty selection collected");
        Assert(!Field<bool>(window, "_slideshowPlaying")
            && Field<string>(window, "_transferStatus").Contains("No images found"), "empty selected folder leaves a useful status");
        window.Close(); Pump();
    }
}
