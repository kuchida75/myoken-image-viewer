namespace Myoken.Linux;

internal sealed partial class MainWindow
{
    private async Task RunFileOperationChecksAsync(string originalFolder)
    {
        var root = Path.GetFullPath(Path.Combine(originalFolder, "..", "fileops-fixtures"));
        Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file);

        var source0 = _files[0];
        var source1 = _files[1];
        var a = Path.Combine(root, "live-a.png");
        File.Copy(source0, a);
        await NavigateAsync(root);
        CheckUi(_files.SequenceEqual(new[] { a }), "file-operations fixture folder opens with watcher active");

        var b = Path.Combine(root, "live-b.png");
        File.Copy(source0, b);
        await WaitUiAsync(() => _files.Any(p => StringComparer.Ordinal.Equals(p, b)),
            "live watcher adds a newly created supported image without F5");

        var tab = AddImageTab(b);
        await SelectForTestAsync(tab);
        var viewer = (ImageViewer)tab.Content;
        await WaitUiAsync(() => viewer.HasImage, "live-watcher test tab decoded");
        viewer.ZoomBy(2);
        var zoom = viewer.View.Zoom;

        var c = Path.Combine(root, "live-c.png");
        File.Move(b, c);
        await WaitUiAsync(() => StringComparer.Ordinal.Equals(tab.Path, c)
            && _files.Any(p => StringComparer.Ordinal.Equals(p, c))
            && !_files.Any(p => StringComparer.Ordinal.Equals(p, b)),
            "external rename updates folder listing and the existing open tab path");
        await WaitUiAsync(() => viewer.HasImage, "renamed open tab reloaded");
        CheckUi(ReferenceEquals(tab.Content, viewer) && Math.Abs(viewer.View.Zoom - zoom) < 1e-9,
            "external rename preserves ImageViewer identity and zoom state");

        await Task.Delay(450);
        var decodeBefore = _previewCache.Snapshot.DecodeAttempts;
        File.Copy(source1, c, overwrite: true);
        File.SetLastWriteTimeUtc(c, DateTime.UtcNow.AddSeconds(2));
        await WaitUiAsync(() => _previewCache.Snapshot.DecodeAttempts > decodeBefore,
            "external content change invalidates preview cache and reloads an open tab");
        await WaitUiAsync(() => viewer.HasImage, "changed open tab recovered after live reload");
        CheckUi(Math.Abs(viewer.View.Zoom - zoom) < 1e-9,
            "same-size live content reload preserves zoom state");

        var renamed = await RenameFileCoreAsync(a, "renamed-by-myoken.png");
        CheckUi(File.Exists(renamed) && !File.Exists(a)
            && _files.Any(p => StringComparer.Ordinal.Equals(p, renamed)),
            "Myoken rename core moves the file and updates Browser immediately");

        await DeleteFileCoreAsync(renamed);
        CheckUi(!File.Exists(renamed) && !_files.Any(p => StringComparer.Ordinal.Equals(p, renamed)),
            "Myoken permanent-delete core removes the fixture and Browser entry");

        File.Delete(c);
        await WaitUiAsync(() => !_tabItems.Contains(tab) && !_files.Any(p => StringComparer.Ordinal.Equals(p, c)),
            "external delete closes the matching tab and removes the Browser entry");

        await NavigateAsync(originalFolder);
        CheckUi(StringComparer.Ordinal.Equals(_folder, originalFolder) && _files.Length >= 3,
            "file-operation checks return to the original folder");
        Console.WriteLine("PASS: L004a live create/rename/change/delete and in-place tab synchronization regressions");
    }
}
