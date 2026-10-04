using Myoken.Linux;

static void Check(bool ok, string name)
{
    if (!ok) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}
using (var cache = new LeasedLruCache<string, Resource>(20, comparer: StringComparer.Ordinal))
{
    var a = new Resource(); var b = new Resource(); var c = new Resource();
    using var first = cache.AddOrAcquire("a", a, 10);
    cache.AddOrAcquire("b", b, 10).Dispose();
    using var hit = cache.TryAcquire("a");
    Check(hit != null && ReferenceEquals(hit.Value, a), "cache hit shares the original resource");
    cache.AddOrAcquire("c", c, 10).Dispose();
    Check(b.Disposals == 1 && a.Disposals == 0 && cache.Snapshot.Evictions == 1,
        "least recently used unpinned resource evicted first");
    var duplicate = new Resource();
    cache.AddOrAcquire("a", duplicate, 10).Dispose();
    Check(duplicate.Disposals == 1 && a.Disposals == 0, "concurrent duplicate result disposed without invalidating winner");
    cache.Clear();
    Check(cache.Snapshot.RetainedBytes == 0 && cache.Snapshot.LiveBytes == 10 && c.Disposals == 1,
        "clear removes cache references but preserves active leases");
    first.Dispose(); first.Dispose();
    Check(a.Disposals == 0, "idempotent lease release preserves another consumer");
    hit!.Dispose();
    Check(a.Disposals == 1 && cache.Snapshot.LiveBytes == 0 && cache.Snapshot.OutstandingLeases == 0,
        "last retired lease disposes resource exactly once");
}
using (var cache = new LeasedLruCache<int, Resource>(10))
{
    var a = new Resource(); var b = new Resource();
    var pinned = cache.AddOrAcquire(1, a, 10);
    cache.AddOrAcquire(2, b, 10).Dispose();
    Check(cache.Snapshot.RetainedBytes == 10 && cache.Snapshot.LiveBytes == 20 && pinned.Value == a,
        "capacity eviction cannot dispose a displayed resource; retired bytes are observable");
    pinned.Dispose();
    Check(a.Disposals == 1 && cache.Snapshot.LiveBytes == 10, "retired bytes freed on consumer release");
    var huge = new Resource();
    var uncached = cache.AddOrAcquire(3, huge, 11);
    Check(cache.Snapshot.Entries == 1 && cache.TryAcquire(3) == null && huge.Disposals == 0,
        "oversized resource bypasses retention without flushing resident cache");
    uncached.Dispose(); Check(huge.Disposals == 1, "uncached lease still owns its resource");
}
using (var cache = new LeasedLruCache<string, Resource>(100, 2, StringComparer.Ordinal))
{
    cache.AddOrAcquire("A", new(), 1).Dispose(); cache.AddOrAcquire("a", new(), 1).Dispose();
    Check(cache.Snapshot.Entries == 2, "case-distinct keys stay distinct");
    cache.AddOrAcquire("日本", new(), 1).Dispose();
    Check(cache.Snapshot.Entries == 2 && cache.Snapshot.Evictions == 1, "entry count also bounds tiny-resource bookkeeping");
    cache.RemoveWhere(k => k == "a");
    Check(cache.TryAcquire("a") == null && cache.Snapshot.Entries == 1, "selective invalidation removes only matching entries");
}
var shutdown = new LeasedLruCache<int, Resource>(10);
var activeResource = new Resource(); var activeLease = shutdown.AddOrAcquire(1, activeResource, 5);
shutdown.Dispose(); shutdown.Dispose();
Check(activeLease.Value == activeResource && activeResource.Disposals == 0, "active lease survives cache shutdown");
activeLease.Dispose(); Check(activeResource.Disposals == 1 && shutdown.Snapshot.LiveBytes == 0, "shutdown drains after last lease");
var rejected = new Resource();
try { shutdown.AddOrAcquire(2, rejected, 1); throw new Exception("Expected disposed cache rejection."); }
catch (ObjectDisposedException) { Check(rejected.Disposals == 1, "late insertion after shutdown disposes transferred resource"); }
using (var concurrent = new LeasedLruCache<int, Resource>(64, 64))
{
    var resources = new System.Collections.Concurrent.ConcurrentBag<Resource>();
    Parallel.For(0, 2000, i =>
    {
        var resource = new Resource(); resources.Add(resource);
        using var lease = concurrent.AddOrAcquire(i % 96, resource, 1);
        using var read = concurrent.TryAcquire(i % 96);
        if (lease.Value.Disposals != 0 || (read != null && read.Value.Disposals != 0))
            throw new Exception("Use-after-dispose in concurrent lease.");
        if (concurrent.Snapshot.RetainedBytes > 64) throw new Exception("Budget exceeded.");
    });
    concurrent.Clear();
    Check(resources.All(r => r.Disposals == 1) && concurrent.Snapshot.LiveBytes == 0,
        "2000 parallel acquisitions/evictions release every resource exactly once within budget");
}
var temp = Path.Combine(Path.GetTempPath(), "myoken-cache-tests-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(temp); var path = Path.Combine(temp, "日本 space.png");
    File.WriteAllText(path, "one"); var stamp = ThumbnailFileStamp.Read(path);
    File.WriteAllText(path, "two"); File.SetLastWriteTimeUtc(path, new DateTime(stamp.ModifiedUtcTicks, DateTimeKind.Utc).AddSeconds(2));
    Check(stamp != ThumbnailFileStamp.Read(path), "same-size edits with changed mtime invalidate identity");
    var key = new ThumbnailKey(path, 256, stamp);
    Check(key != new ThumbnailKey(path, 128, stamp) && key != new ThumbnailKey(path.ToUpperInvariant(), 256, stamp),
        "thumbnail identity includes requested size and ordinal path");
    File.Delete(path);
    try { ThumbnailFileStamp.Read(path); throw new Exception("Expected missing file."); }
    catch (FileNotFoundException) { Console.WriteLine("PASS: deleted files cannot produce valid cache metadata"); }
}
finally { Directory.Delete(temp, true); }
Console.WriteLine("All thumbnail cache ownership, budget, concurrency and metadata policy checks passed.");

sealed class Resource : IDisposable
{
    private int _disposals;
    public int Disposals => Volatile.Read(ref _disposals);
    public void Dispose()
    {
        if (Interlocked.Increment(ref _disposals) != 1) throw new InvalidOperationException("Double dispose.");
    }
}
