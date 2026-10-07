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

        var destinationFolder = Path.Combine(root, "destination 日本");
        Directory.CreateDirectory(destinationFolder);
        var conflict = Path.Combine(destinationFolder, Path.GetFileName(c));
        File.Copy(source0, conflict, overwrite: true);
        var conflictBytes = File.ReadAllBytes(conflict);
        try { await MoveFileCoreAsync(c, destinationFolder); throw new InvalidOperationException("Move overwrote collision"); }
        catch (IOException) { }
        CheckUi(File.Exists(c) && File.ReadAllBytes(conflict).SequenceEqual(conflictBytes)
            && tab.Path == c, "move collision preserves source, destination and tab");
        File.Delete(conflict);
        CheckUi(await MoveFileCoreAsync(c, root) == c, "same-folder move is a no-op");
        try { await MoveFileCoreAsync(c, Path.Combine(root, "missing")); throw new InvalidOperationException("Missing folder accepted"); }
        catch (DirectoryNotFoundException) { }
        var moved = await MoveFileCoreAsync(c, destinationFolder);
        await Task.Delay(650); // Drain source-folder watcher notifications.
        CheckUi(!File.Exists(c) && File.Exists(moved) && tab.Path == moved
            && _tabItems.Contains(tab) && ReferenceEquals(tab.Content, viewer)
            && !_files.Contains(c), "cross-folder move keeps the existing tab after watcher drain");
        await WaitUiAsync(() => viewer.HasImage, "moved viewer decoded");
        CheckUi(Math.Abs(viewer.View.Zoom - zoom) < 1e-9, "move preserves zoom");
        SaveSession();
        CheckUi(_sessions!.Load().ImagePaths.Contains(moved)
            && !_sessions.Load().ImagePaths.Contains(c), "session saves moved path without stale source");
        await NavigateAsync(destinationFolder);
        CheckUi(_files.Contains(moved), "destination Browser lists moved image");
        try { await DesktopTrash.MoveAsync(moved, "/nonexistent/myoken-gio"); throw new InvalidOperationException("Missing gio accepted"); }
        catch (IOException) { }
        try { await DesktopTrash.MoveAsync(moved, "/usr/bin/false"); throw new InvalidOperationException("Failed gio accepted"); }
        catch (IOException) { }
        CheckUi(File.Exists(moved) && _tabItems.Contains(tab), "Trash failure has no permanent-delete fallback or tab mutation");
        await TrashFileCoreAsync(moved);
        await Task.Delay(650);
        CheckUi(!File.Exists(moved) && !_files.Contains(moved) && !_tabItems.Contains(tab),
            "real GIO Trash removes Browser entry and closes matching tab");
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")!;
        var trashFiles = Directory.GetFiles(Path.Combine(dataHome, "Trash", "files"));
        var trashInfo = Directory.GetFiles(Path.Combine(dataHome, "Trash", "info"));
        CheckUi(trashFiles.Any(f => File.ReadAllBytes(f).SequenceEqual(File.ReadAllBytes(source1)))
            && trashInfo.Any(f => File.ReadAllText(f).Contains("DeletionDate=")),
            "GIO retains recoverable image contents and trashinfo in isolated desktop Trash");
        SaveSession();
        CheckUi(!_sessions.Load().ImagePaths.Contains(moved), "session excludes trashed tab");
        await NavigateAsync(root);
        File.Copy(source0, c);
        tab = AddImageTab(c);
        File.Delete(c);
        await WaitUiAsync(() => !_tabItems.Contains(tab) && !_files.Any(p => StringComparer.Ordinal.Equals(p, c)),
            "external delete closes the matching tab and removes the Browser entry");

        await NavigateAsync(originalFolder);
        CheckUi(StringComparer.Ordinal.Equals(_folder, originalFolder) && _files.Length >= 3,
            "file-operation checks return to the original folder");
        Console.WriteLine("PASS: L004b move/Trash/failure/session plus L004a live create/rename/change/delete and in-place tab synchronization regressions");
    }
}
