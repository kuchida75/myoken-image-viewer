using Avalonia.Media.Imaging;

namespace Myoken.Linux;

internal sealed partial class MainWindow
{
    private async Task RunPreviewCacheChecksAsync()
    {
        var folder = _folder;
        var root = Path.GetFullPath(Path.Combine(folder, "..", "preview-cache-fixtures"));
        Directory.CreateDirectory(root);
        var a = Path.Combine(root, "A.bmp");
        var b = Path.Combine(root, "a.bmp");
        var c = Path.Combine(root, "日本 preview.bmp");
        WriteCacheBmp(a, 35); WriteCacheBmp(b, 95); WriteCacheBmp(c, 170);

        using (var cache = new PreviewCache())
        {
            using var first = await cache.AcquireAsync(a);
            using var second = await cache.AcquireAsync(a);
            CheckUi(second.FromCache && ReferenceEquals(first.Image.Bitmap, second.Image.Bitmap)
                && cache.Snapshot.DecodeAttempts == 1 && cache.Snapshot.Memory.Hits == 1,
                "warm preview acquisition reuses the decoded bitmap");
            using var lower = await cache.AcquireAsync(b);
            using var unicode = await cache.AcquireAsync(c);
            CheckUi(cache.Snapshot.Memory.Entries == 3 && !ReferenceEquals(first.Image.Bitmap, lower.Image.Bitmap),
                "preview cache keeps case-distinct and Unicode paths separate");

            var oldStamp = ThumbnailFileStamp.Read(a);
            WriteCacheBmp(a, 210);
            File.SetLastWriteTimeUtc(a, new DateTime(oldStamp.ModifiedUtcTicks, DateTimeKind.Utc).AddSeconds(2));
            using var changed = await cache.AcquireAsync(a);
            CheckUi(!changed.FromCache && !ReferenceEquals(first.Image.Bitmap, changed.Image.Bitmap)
                && CachePixel(first.Image.Bitmap) == 35 && CachePixel(changed.Image.Bitmap) == 210,
                "changed preview reloads while an existing lease remains valid");

            File.Delete(b);
            try { using var missing = await cache.AcquireAsync(b); throw new Exception("Deleted preview used cached pixels."); }
            catch (FileNotFoundException) { Console.WriteLine("PASS: deleted preview cannot use an old cache hit"); }
        }

        WriteCacheBmp(b, 95);
        using (var tiny = new PreviewCache(20_000))
        {
            using var shown = await tiny.AcquireAsync(a);
            (await tiny.AcquireAsync(b)).Dispose();
            (await tiny.AcquireAsync(c)).Dispose();
            CheckUi(tiny.Snapshot.Memory.RetainedBytes <= 20_000 && tiny.Snapshot.Memory.Evictions > 0
                && CachePixel(shown.Image.Bitmap) == 210,
                "small preview cache evicts within budget without invalidating the displayed lease");
        }

        var bad = Path.Combine(root, "broken.bmp"); File.WriteAllText(bad, "broken");
        using (var cache = new PreviewCache())
        {
            try { using var ignored = await cache.AcquireAsync(bad); throw new Exception("Corrupt preview loaded."); }
            catch (InvalidDataException) { CheckUi(cache.Snapshot.Memory.Entries == 0, "corrupt preview is not cached"); }
            WriteCacheBmp(bad, 125);
            using var recovered = await cache.AcquireAsync(bad);
            CheckUi(CachePixel(recovered.Image.Bitmap) == 125, "failed preview path can recover on a later request");
        }

        foreach (var action in new[] { "cancel", "clear", "shutdown", "change" })
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cache = new PreviewCache(decoder: async (path, _) =>
            {
                var image = await ViewerImageLoader.LoadAsync(path, false, CancellationToken.None);
                ready.SetResult(); await release.Task; return image;
            });
            using var cancel = new CancellationTokenSource();
            var load = cache.AcquireAsync(c, cancel.Token);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (action == "cancel") cancel.Cancel();
            if (action == "clear") cache.Clear();
            if (action == "shutdown") cache.Dispose();
            if (action == "change")
            {
                var stamp = ThumbnailFileStamp.Read(c);
                WriteCacheBmp(c, 195);
                File.SetLastWriteTimeUtc(c, new DateTime(stamp.ModifiedUtcTicks, DateTimeKind.Utc).AddSeconds(2));
            }
            release.SetResult();
            try { using var unwanted = await load; throw new Exception("Late preview " + action + " result was published."); }
            catch (OperationCanceledException) when (action != "change") { }
            catch (IOException) when (action == "change") { }
            CheckUi(cache.Snapshot.Memory.Entries == 0 && cache.Snapshot.Memory.LiveBytes == 0,
                "late preview rejected after " + action + " without retained ownership");
        }

        // Exercise the shared application cache through real image tabs.
        _previewCache.Clear();
        var firstTab = AddImageTab(_files[0]);
        var secondTab = AddImageTab(_files[1]);
        await SelectForTestAsync(firstTab);
        var firstViewer = (ImageViewer)firstTab.Content!;
        await WaitUiAsync(() => firstViewer.HasImage, "initial preview did not load");
        var firstBitmap = firstViewer.Surface.Bitmap;
        firstViewer.ZoomBy(2);
        var zoom = firstViewer.View.Zoom;
        await SelectForTestAsync(secondTab);
        await WaitUiAsync(() => ((ImageViewer)secondTab.Content!).HasImage, "second preview did not load");
        var before = _previewCache.Snapshot;
        await SelectForTestAsync(firstTab);
        await WaitUiAsync(() => firstViewer.HasImage, "cached preview did not reactivate");
        var after = _previewCache.Snapshot;
        CheckUi(firstViewer.PreviewCacheHit && ReferenceEquals(firstBitmap, firstViewer.Surface.Bitmap)
            && after.Memory.Hits > before.Memory.Hits && after.DecodeAttempts == before.DecodeAttempts,
            "switching back reuses the shared preview without another preview decode");
        CheckUi(Math.Abs(firstViewer.View.Zoom - zoom) < 1e-9,
            "preview-cache reuse preserves the tab's zoom state");

        await SelectForTestAsync(_browser);
        CloseTab(firstTab); CloseTab(secondTab);
        CheckUi(_previewCache.Snapshot.Memory.Entries >= 2 && _previewCache.Snapshot.Memory.OutstandingLeases == 0,
            "inactive image tabs release preview leases while bounded cache ownership remains");
        _previewCache.Clear();
        CheckUi(_previewCache.Snapshot.Memory.RetainedBytes == 0 && _previewCache.Snapshot.Memory.LiveBytes == 0,
            "preview cache clear releases unleased preview storage");

        Console.WriteLine("PASS: L002c3 preview-cache reuse, invalidation, cancellation and ownership regressions");
    }
}
