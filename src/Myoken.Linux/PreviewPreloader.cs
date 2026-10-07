namespace Myoken.Linux;

internal readonly record struct PreviewPreloaderSnapshot(
    long Schedules, long Attempts, long Completed, long CacheHits, long CancelledRuns, long Failures);

// Low-priority, sequential warming of neighbouring open image tabs. It never holds
// a lease after a warm operation; the bounded PreviewCache owns any retained bitmap.
internal sealed class PreviewPreloader : IDisposable
{
    private readonly PreviewCache _cache;
    private readonly TimeSpan _idleDelay;
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task _current = Task.CompletedTask;
    private bool _disposed;
    private long _schedules, _attempts, _completed, _cacheHits, _cancelledRuns, _failures;

    public PreviewPreloader(PreviewCache cache, TimeSpan? idleDelay = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _idleDelay = idleDelay ?? TimeSpan.FromMilliseconds(150);
    }

    public PreviewPreloaderSnapshot Snapshot => new(
        Interlocked.Read(ref _schedules), Interlocked.Read(ref _attempts),
        Interlocked.Read(ref _completed), Interlocked.Read(ref _cacheHits),
        Interlocked.Read(ref _cancelledRuns), Interlocked.Read(ref _failures));

    internal Task CurrentTask { get { lock (_sync) return _current; } }

    public void Schedule(IReadOnlyList<string> orderedPaths, string selectedPath)
    {
        var targets = PreviewPreloadPolicy.Targets(orderedPaths, selectedPath);
        CancellationTokenSource? old;
        lock (_sync)
        {
            if (_disposed) return;
            old = _cts;
            _cts = targets.Length == 0 ? null : new CancellationTokenSource();
            Interlocked.Increment(ref _schedules);
            _current = _cts == null ? Task.CompletedTask : RunAsync(targets, _cts.Token);
        }
        if (old != null) { old.Cancel(); old.Dispose(); }
    }

    public void Cancel()
    {
        CancellationTokenSource? old;
        lock (_sync)
        {
            old = _cts; _cts = null; _current = Task.CompletedTask;
        }
        if (old != null) { old.Cancel(); old.Dispose(); }
    }

    private async Task RunAsync(string[] targets, CancellationToken token)
    {
        try
        {
            if (_idleDelay > TimeSpan.Zero) await Task.Delay(_idleDelay, token).ConfigureAwait(false);
            foreach (var path in targets)
            {
                token.ThrowIfCancellationRequested();
                Interlocked.Increment(ref _attempts);
                var hit = await _cache.PreloadAsync(path, token).ConfigureAwait(false);
                if (hit) Interlocked.Increment(ref _cacheHits);
                Interlocked.Increment(ref _completed);
            }
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _cancelledRuns);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // A missing/corrupt neighbour must not affect the selected image.
            Interlocked.Increment(ref _failures);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Cancel();
    }
}
