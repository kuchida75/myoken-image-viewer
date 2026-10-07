using Avalonia.Media.Imaging;

namespace Myoken.Linux;

internal readonly record struct ThumbnailCacheSnapshot(LeaseCacheSnapshot Memory, long DecodeAttempts, long StaleResults);

// A browser-owned RAM cache. No persistent files, viewer previews or GPU settings.
// Two request slots bound metadata work and decoding. Concurrent misses may both
// decode; AddOrAcquire retains only one result. No unbounded in-flight key table.
internal sealed class ThumbnailCache : IDisposable
{
    public const long DefaultBudget = 128L * 1024 * 1024;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _requests = new(2);
    private readonly LeasedLruCache<ThumbnailKey, Bitmap> _memory;
    private readonly Func<string, int, CancellationToken, Task<Bitmap>> _decode;
    private long _generation, _decodeAttempts, _staleResults;
    private bool _disposed;

    public ThumbnailCache(long budgetBytes = DefaultBudget,
        Func<string, int, CancellationToken, Task<Bitmap>>? decoder = null)
    { _memory = new(budgetBytes); _decode = decoder ?? ImageDecoder.LoadAsync; }
    public ThumbnailCacheSnapshot Snapshot
    {
        get { lock (_sync) return new(_memory.Snapshot, _decodeAttempts, _staleResults); }
    }
    public async Task<LeasedLruCache<ThumbnailKey, Bitmap>.Lease> AcquireAsync(
        string path, int edge, CancellationToken token = default)
    {
        if (edge < 1 || edge > 1024) throw new ArgumentOutOfRangeException(nameof(edge));
        token.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        long generation;
        lock (_sync) { ObjectDisposedException.ThrowIf(_disposed, this); generation = _generation; }
        await _requests.WaitAsync(token).ConfigureAwait(false);
        Bitmap? owned = null;
        try
        {
            lock (_sync) CheckCurrent(generation, token);
            ThumbnailFileStamp stamp;
            try { stamp = await Task.Run(() => ThumbnailFileStamp.Read(fullPath), token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_sync) _memory.RemoveWhere(k => StringComparer.Ordinal.Equals(k.Path, fullPath));
                throw;
            }
            var key = new ThumbnailKey(fullPath, edge, stamp);
            lock (_sync)
            {
                CheckCurrent(generation, token);
                var hit = _memory.TryAcquire(key);
                if (hit != null) return hit;
                _memory.RemoveWhere(k => StringComparer.Ordinal.Equals(k.Path, fullPath) && k.Stamp != stamp);
                _decodeAttempts++;
            }
            owned = await _decode(fullPath, edge, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var after = await Task.Run(() => ThumbnailFileStamp.Read(fullPath), token).ConfigureAwait(false);
            lock (_sync)
            {
                CheckCurrent(generation, token);
                if (after != stamp)
                {
                    _staleResults++;
                    throw new IOException("Image changed while decoding; refresh to retry.");
                }
                if (owned.PixelSize.Width <= 0 || owned.PixelSize.Height <= 0
                    || owned.PixelSize.Width > edge || owned.PixelSize.Height > edge)
                    throw new InvalidDataException("Decoder returned an unexpected thumbnail size.");
                // Account pixel storage, conservative row alignment and a modest
                // key/bookkeeping allowance. Not allocator/GPU/decoder scratch RSS.
                var bits = Math.Max(32, owned.Format?.BitsPerPixel ?? 64);
                var row = (((long)owned.PixelSize.Width * bits + 7) / 8 + 63) / 64 * 64;
                var bytes = checked(row * owned.PixelSize.Height + 512L + fullPath.Length * 2L);
                var transferred = owned; owned = null;
                return _memory.AddOrAcquire(key, transferred, bytes);
            }
        }
        finally { owned?.Dispose(); _requests.Release(); }
    }
    private void CheckCurrent(long generation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed || generation != _generation)
            throw new OperationCanceledException("Thumbnail cache was cleared or closed.", token);
    }
    public void InvalidatePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_sync)
        {
            if (_disposed) return;
            _memory.RemoveWhere(k => StringComparer.Ordinal.Equals(k.Path, fullPath));
        }
    }
    public void Clear()
    {
        lock (_sync) { if (_disposed) return; _generation++; _memory.Clear(); }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; _generation++; _memory.Dispose();
            // Do not dispose the semaphore while late native decodes/waiters can
            // still reach finally/Release. The managed object is collected later.
        }
    }
}
