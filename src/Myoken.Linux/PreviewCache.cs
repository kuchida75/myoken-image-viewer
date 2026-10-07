using Avalonia.Media.Imaging;

namespace Myoken.Linux;

internal readonly record struct PreviewKey(string Path, int Edge, ThumbnailFileStamp Stamp);
internal readonly record struct PreviewCacheSnapshot(LeaseCacheSnapshot Memory, long DecodeAttempts, long StaleResults);

internal sealed class PreviewResource : IDisposable
{
    public ViewerImage Image { get; }
    public PreviewResource(ViewerImage image) => Image = image;
    public void Dispose() => Image.Bitmap.Dispose();
}

// Shared process-local cache for the normal initial viewer decode only. Explicit
// full-resolution upgrades are intentionally not retained here.
internal sealed class PreviewCache : IDisposable
{
    public const long DefaultBudget = 512L * 1024 * 1024;
    public const int MaximumEntries = 256;

    internal sealed class Lease : IDisposable
    {
        private LeasedLruCache<PreviewKey, PreviewResource>.Lease? _inner;
        internal Lease(LeasedLruCache<PreviewKey, PreviewResource>.Lease inner, bool fromCache)
        { _inner = inner; FromCache = fromCache; }
        public ViewerImage Image => _inner?.Value.Image ?? throw new ObjectDisposedException(nameof(Lease));
        public bool FromCache { get; }
        public void Dispose() => Interlocked.Exchange(ref _inner, null)?.Dispose();
    }

    private readonly object _sync = new();
    private readonly SemaphoreSlim _requests = new(2);
    private readonly LeasedLruCache<PreviewKey, PreviewResource> _memory;
    private readonly Func<string, CancellationToken, Task<ViewerImage>>? _decode;
    private long _generation, _decodeAttempts, _staleResults;
    private bool _disposed;

    public PreviewCache(long budgetBytes = DefaultBudget,
        Func<string, CancellationToken, Task<ViewerImage>>? decoder = null)
    {
        _memory = new(budgetBytes, MaximumEntries);
        _decode = decoder;
    }

    public PreviewCacheSnapshot Snapshot
    {
        get { lock (_sync) return new(_memory.Snapshot, _decodeAttempts, _staleResults); }
    }

    public Task<Lease> AcquireAsync(string path, CancellationToken token = default)
        => AcquireCoreAsync(path, ViewerDecodePriority.Foreground, token);

    public async Task<bool> PreloadAsync(string path, CancellationToken token = default)
    {
        using var lease = await AcquireCoreAsync(path, ViewerDecodePriority.Background, token).ConfigureAwait(false);
        return lease.FromCache;
    }

    private async Task<Lease> AcquireCoreAsync(string path, ViewerDecodePriority priority, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        long generation;
        lock (_sync) { ObjectDisposedException.ThrowIf(_disposed, this); generation = _generation; }

        await _requests.WaitAsync(token).ConfigureAwait(false);
        ViewerImage? owned = null;
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

            var key = new PreviewKey(fullPath, ViewerDecodePolicy.PreviewEdge, stamp);
            lock (_sync)
            {
                CheckCurrent(generation, token);
                var hit = _memory.TryAcquire(key);
                if (hit != null) return new Lease(hit, true);
                _memory.RemoveWhere(k => StringComparer.Ordinal.Equals(k.Path, fullPath) && k.Stamp != stamp);
                _decodeAttempts++;
            }

            owned = _decode != null
                ? await _decode(fullPath, token).ConfigureAwait(false)
                : await ViewerImageLoader.LoadAsync(fullPath, false, token, priority).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var after = await Task.Run(() => ThumbnailFileStamp.Read(fullPath), token).ConfigureAwait(false);

            lock (_sync)
            {
                CheckCurrent(generation, token);
                if (after != stamp)
                {
                    _staleResults++;
                    throw new IOException("Image changed while decoding; reopen or refresh to retry.");
                }

                var bitmap = owned.Bitmap;
                if (owned.Width <= 0 || owned.Height <= 0 || bitmap.PixelSize.Width <= 0 || bitmap.PixelSize.Height <= 0
                    || Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height) > ViewerDecodePolicy.PreviewEdge)
                    throw new InvalidDataException("Decoder returned an unexpected viewer preview.");

                var bits = Math.Max(32, bitmap.Format?.BitsPerPixel ?? 64);
                var row = (((long)bitmap.PixelSize.Width * bits + 7) / 8 + 63) / 64 * 64;
                var bytes = checked(row * bitmap.PixelSize.Height + 1024L + fullPath.Length * 2L);
                var resource = new PreviewResource(owned); owned = null;
                return new Lease(_memory.AddOrAcquire(key, resource, bytes), false);
            }
        }
        finally
        {
            owned?.Bitmap.Dispose();
            _requests.Release();
        }
    }

    private void CheckCurrent(long generation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed || generation != _generation)
            throw new OperationCanceledException("Preview cache was cleared or closed.", token);
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
            // Keep the semaphore alive for late native work that still reaches finally.
        }
    }
}
