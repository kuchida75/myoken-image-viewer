namespace Myoken.Linux;

internal readonly record struct LeaseCacheSnapshot(long BudgetBytes, long RetainedBytes, long LiveBytes,
    int Entries, int OutstandingLeases, long Hits, long Misses, long Evictions, long Invalidations);

// Resident entries own one cache reference; callers own explicit leases. Removing
// an entry never destroys a resource that a control is still displaying. The
// budget covers retained entries, not outstanding retired leases or process RSS.
// All access is locked. Dispose implementations must be nonthrowing.
internal sealed class LeasedLruCache<TKey, TValue> : IDisposable
    where TKey : notnull where TValue : class, IDisposable
{
    internal sealed class Entry
    {
        public required TKey Key;
        public required TValue Value;
        public long Bytes;
        public int References;
        public LinkedListNode<Entry>? Node;
    }
    internal sealed class Lease : IDisposable
    {
        private readonly LeasedLruCache<TKey, TValue> _owner;
        private Entry? _entry;
        internal Lease(LeasedLruCache<TKey, TValue> owner, Entry entry) { _owner = owner; _entry = entry; }
        public TValue Value => _entry?.Value ?? throw new ObjectDisposedException(nameof(Lease));
        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry != null) _owner.Release(entry);
        }
    }
    private readonly object _sync = new();
    private readonly Dictionary<TKey, Entry> _entries;
    private readonly LinkedList<Entry> _lru = new();
    private readonly long _budget;
    private readonly int _maxEntries;
    private long _retainedBytes, _liveBytes, _hits, _misses, _evictions, _invalidations;
    private int _leases;
    private bool _disposed;

    public LeasedLruCache(long budgetBytes, int maxEntries = 8192, IEqualityComparer<TKey>? comparer = null)
    {
        if (budgetBytes < 0) throw new ArgumentOutOfRangeException(nameof(budgetBytes));
        if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        _budget = budgetBytes; _maxEntries = maxEntries; _entries = new(comparer);
    }
    public LeaseCacheSnapshot Snapshot
    {
        get { lock (_sync) return new(_budget, _retainedBytes, _liveBytes, _entries.Count,
            _leases, _hits, _misses, _evictions, _invalidations); }
    }
    public Lease? TryAcquire(TKey key)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(key, out var entry)) { _misses++; return null; }
            _hits++; Touch(entry); return Pin(entry);
        }
    }
    // Takes ownership of value even for duplicate keys, oversized values or a
    // disposed cache. Duplicate producers share the winner and dispose the loser.
    public Lease AddOrAcquire(TKey key, TValue value, long bytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_sync)
        {
            if (bytes <= 0) { value.Dispose(); throw new ArgumentOutOfRangeException(nameof(bytes)); }
            if (_disposed) { value.Dispose(); throw new ObjectDisposedException(GetType().Name); }
            if (_entries.TryGetValue(key, out var existing))
            { value.Dispose(); Touch(existing); return Pin(existing); }
            var entry = new Entry { Key = key, Value = value, Bytes = bytes };
            _liveBytes = checked(_liveBytes + bytes);
            var lease = Pin(entry);
            if (bytes > _budget) return lease; // Uncached, valid until lease release.
            while (_lru.Last != null && (_retainedBytes > _budget - bytes || _entries.Count >= _maxEntries))
            { _evictions++; Remove(_lru.Last.Value); }
            entry.Node = _lru.AddFirst(entry); _entries.Add(key, entry); _retainedBytes += bytes;
            return lease;
        }
    }
    public void RemoveWhere(Func<TKey, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        lock (_sync)
        {
            if (_disposed) return;
            foreach (var entry in _entries.Values.Where(e => predicate(e.Key)).ToArray())
            { _invalidations++; Remove(entry); }
        }
    }
    public void Clear()
    {
        lock (_sync)
        {
            while (_lru.Last != null) { _invalidations++; Remove(_lru.Last.Value); }
        }
    }
    private Lease Pin(Entry entry) { entry.References++; _leases++; return new Lease(this, entry); }
    private void Touch(Entry entry)
    {
        if (entry.Node == null) return;
        _lru.Remove(entry.Node); _lru.AddFirst(entry.Node);
    }
    private void Remove(Entry entry)
    {
        _entries.Remove(entry.Key); _lru.Remove(entry.Node!); entry.Node = null;
        _retainedBytes -= entry.Bytes;
        if (entry.References == 0) Destroy(entry);
    }
    private void Release(Entry entry)
    {
        lock (_sync)
        {
            entry.References--; _leases--;
            if (entry.References == 0 && entry.Node == null) Destroy(entry);
        }
    }
    private void Destroy(Entry entry) { _liveBytes -= entry.Bytes; entry.Value.Dispose(); }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; Clear(); // Leases remain usable and can release after shutdown.
        }
    }
}
