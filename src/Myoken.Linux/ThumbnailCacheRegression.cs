using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using ThumbnailLease = Myoken.Linux.LeasedLruCache<Myoken.Linux.ThumbnailKey, Avalonia.Media.Imaging.Bitmap>.Lease;

namespace Myoken.Linux;

internal sealed partial class MainWindow
{
    // Runs only behind Program's existing disposable-fixture/profile guard.
    private async Task RunThumbnailCacheChecksAsync()
    {
        var folder = _folder;
        var root = Path.GetFullPath(Path.Combine(folder, "..", "cache-fixtures"));
        Directory.CreateDirectory(root);
        var a = Path.Combine(root, "A.bmp"); var b = Path.Combine(root, "a.bmp");
        var c = Path.Combine(root, "日本 space.bmp");
        WriteCacheBmp(a, 40); WriteCacheBmp(b, 90); WriteCacheBmp(c, 160);
        using (var cache = new ThumbnailCache())
        {
            using var first = await cache.AcquireAsync(a, 256);
            using var second = await cache.AcquireAsync(a, 256);
            CheckUi(ReferenceEquals(first.Value, second.Value) && cache.Snapshot.DecodeAttempts == 1
                && cache.Snapshot.Memory.Hits == 1, "warm acquisition reuses bitmap without a second decode");
            using var lower = await cache.AcquireAsync(b, 256);
            using var smaller = await cache.AcquireAsync(a, 128);
            using var unicode = await cache.AcquireAsync(c, 256);
            CheckUi(cache.Snapshot.Memory.Entries == 4 && !ReferenceEquals(first.Value, lower.Value)
                && !ReferenceEquals(first.Value, smaller.Value), "cache distinguishes case, Unicode and requested thumbnail edge");
            var oldStamp = ThumbnailFileStamp.Read(a);
            WriteCacheBmp(a, 200);
            File.SetLastWriteTimeUtc(a, new DateTime(oldStamp.ModifiedUtcTicks, DateTimeKind.Utc).AddSeconds(2));
            using var changed = await cache.AcquireAsync(a, 256);
            CheckUi(!ReferenceEquals(first.Value, changed.Value) && CachePixel(first.Value) == 40 && CachePixel(changed.Value) == 200,
                "changed file produces new pixels while an already-displayed old lease stays valid");
            File.Delete(b);
            try { using var missing = await cache.AcquireAsync(b, 256); throw new Exception("Deleted image used cached pixels."); }
            catch (FileNotFoundException) { Console.WriteLine("PASS: deleted file is checked before a potential cache hit"); }
            var unchangedStamp = ThumbnailFileStamp.Read(a);
            WriteCacheBmp(a, 75);
            File.SetLastWriteTimeUtc(a, new DateTime(unchangedStamp.ModifiedUtcTicks, DateTimeKind.Utc));
            cache.Clear();
            using var forced = await cache.AcquireAsync(a, 256);
            CheckUi(CachePixel(forced.Value) == 75, "explicit invalidation reloads even same-size/same-mtime edits");
        }
        WriteCacheBmp(b, 90);
        using (var tiny = new ThumbnailCache(20_000))
        {
            using var shown = await tiny.AcquireAsync(a, 256);
            (await tiny.AcquireAsync(b, 256)).Dispose();
            (await tiny.AcquireAsync(c, 256)).Dispose();
            CheckUi(tiny.Snapshot.Memory.RetainedBytes <= 20_000 && tiny.Snapshot.Memory.Evictions > 0
                && CachePixel(shown.Value) == 75, "small real-bitmap cache evicts within budget without breaking displayed image");
        }
        var bad = Path.Combine(root, "broken.bmp"); File.WriteAllText(bad, "broken");
        using (var cache = new ThumbnailCache())
        {
            try { using var ignored = await cache.AcquireAsync(bad, 256); throw new Exception("Corrupt image loaded."); }
            catch (InvalidDataException) { CheckUi(cache.Snapshot.Memory.Entries == 0, "corrupt decode is not cached"); }
            WriteCacheBmp(bad, 120);
            using var recovered = await cache.AcquireAsync(bad, 256);
            CheckUi(CachePixel(recovered.Value) == 120, "failed path can recover on next request");
        }
        using (var concurrent = new ThumbnailCache())
        {
            var leases = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => concurrent.AcquireAsync(c, 256)));
            try
            {
                CheckUi(leases.All(l => ReferenceEquals(l.Value, leases[0].Value))
                    && concurrent.Snapshot.Memory.Entries == 1 && concurrent.Snapshot.DecodeAttempts <= 2,
                    "concurrent same-key requests retain one shared bitmap with at most two producers");
            }
            finally { foreach (var lease in leases) lease.Dispose(); }
        }

        // Hold a completed native decode before publication. Exercise cancellation,
        // clear, shutdown and metadata changes without depending on timing races.
        foreach (var action in new[] { "cancel", "clear", "shutdown", "change" })
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cache = new ThumbnailCache(decoder: async (path, edge, _) =>
            {
                var bitmap = await ImageDecoder.LoadAsync(path, edge);
                ready.SetResult(); await release.Task; return bitmap;
            });
            using var cancel = new CancellationTokenSource();
            var load = cache.AcquireAsync(c, 256, cancel.Token);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (action == "cancel") cancel.Cancel();
            if (action == "clear") cache.Clear();
            if (action == "shutdown") cache.Dispose();
            if (action == "change")
            {
                var stamp = ThumbnailFileStamp.Read(c);
                WriteCacheBmp(c, 190);
                File.SetLastWriteTimeUtc(c, new DateTime(stamp.ModifiedUtcTicks, DateTimeKind.Utc).AddSeconds(2));
            }
            release.SetResult();
            try { using var unwanted = await load; throw new Exception("Late " + action + " result was published."); }
            catch (OperationCanceledException) when (action != "change") { }
            catch (IOException) when (action == "change") { }
            CheckUi(cache.Snapshot.Memory.Entries == 0 && cache.Snapshot.Memory.LiveBytes == 0,
                "late result rejected after " + action + " without retained cache ownership");
        }

        // Check actual browser reuse, not only the cache's stand-alone API.
        await SelectForTestAsync(_browser);
        _thumbnails.ScrollToIndex(0);
        await WaitUiAsync(() => _thumbnails.FirstRealized == 0 && _thumbnails.LoadedCount > 0, "cache first viewport failed");
        await _thumbnails.WaitForLoadsAsync();
        _thumbnails.ScrollToIndex(_files.Length - 1);
        await WaitUiAsync(() => _thumbnails.EndRealized == _files.Length && _thumbnails.FirstRealized > 0, "cache last viewport failed");
        await _thumbnails.WaitForLoadsAsync();
        var before = _thumbnails.Cache.Snapshot;
        _thumbnails.ScrollToIndex(0);
        await WaitUiAsync(() => _thumbnails.FirstRealized == 0 && _thumbnails.LoadedCount > 0, "cache return viewport failed");
        await _thumbnails.WaitForLoadsAsync();
        var after = _thumbnails.Cache.Snapshot;
        CheckUi(after.Memory.Hits > before.Memory.Hits && after.DecodeAttempts == before.DecodeAttempts,
            $"scroll-away/back cache reuse: {after.Memory.Hits - before.Memory.Hits} hits, zero additional decodes");
        before = after;
        await NavigateAsync(Path.Combine(folder, "nested folder")); await NavigateAsync(folder);
        await WaitUiAsync(() => _thumbnails.LoadedCount > 0, "return folder cache failed");
        await _thumbnails.WaitForLoadsAsync(); after = _thumbnails.Cache.Snapshot;
        CheckUi(after.Memory.Hits > before.Memory.Hits && after.DecodeAttempts == before.DecodeAttempts,
            "returning to visited folder reuses thumbnails without new decodes");
        var displayed = _thumbnails.LoadedCount;
        _thumbnails.Cache.Clear();
        CheckUi(_thumbnails.Cache.Snapshot.Memory.RetainedBytes == 0
            && _thumbnails.Cache.Snapshot.Memory.LiveBytes > 0 && _thumbnails.LoadedCount == displayed,
            "evicting all cache entries leaves current browser leases displayable");
        CaptureForTest("cache-evicted-active-leases");
        before = _thumbnails.Cache.Snapshot;
        await RefreshAsync();
        await WaitUiAsync(() => _thumbnails.LoadedCount > 0, "Refresh did not repopulate thumbnails");
        await _thumbnails.WaitForLoadsAsync(); after = _thumbnails.Cache.Snapshot;
        CheckUi(after.DecodeAttempts > before.DecodeAttempts && after.Memory.RetainedBytes <= ThumbnailCache.DefaultBudget,
            "Refresh/F5 path clears thumbnails and decodes again within the retained-byte budget");
        Console.WriteLine("PASS: L002c2 real-bitmap cache, invalidation, late-result and browser reuse regressions");
    }
    private static byte CachePixel(Bitmap bitmap)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try { bitmap.CopyPixels(new PixelRect(0, 0, 1, 1), buffer, 4, 4); return Marshal.ReadByte(buffer); }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static void WriteCacheBmp(string path, byte shade)
    {
        const int width = 64, height = 32, stride = width * 3;
        using var stream = File.Create(path); using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4d42); writer.Write(54 + stride * height); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((ushort)1); writer.Write((ushort)24);
        writer.Write(0); writer.Write(stride * height); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        writer.Write(Enumerable.Repeat(shade, stride * height).ToArray());
    }
}
