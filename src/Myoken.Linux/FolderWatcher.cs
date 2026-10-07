namespace Myoken.Linux;

internal enum FolderChangeKind { Created, Changed, Deleted, Renamed }

internal readonly record struct FolderChange(FolderChangeKind Kind, string Path, string? OldPath = null);
internal sealed record FolderChangeBatch(string Folder, IReadOnlyList<FolderChange> Changes, bool Overflowed);

// FileSystemWatcher callback threads are collapsed into a short debounced batch.
// A generation prevents events from a disposed/previous directory watcher from
// reaching the current folder after navigation.
internal sealed class FolderWatcher : IDisposable
{
    private readonly object _sync = new();
    private readonly Timer _timer;
    private FileSystemWatcher? _watcher;
    private readonly List<FolderChange> _pending = new();
    private string _folder = string.Empty;
    private long _generation;
    private bool _overflowed, _disposed;
    public event Action<FolderChangeBatch>? BatchReady;
    public string? Warning { get; private set; }

    public FolderWatcher() => _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);

    public void Watch(string folder)
    {
        folder = Path.GetFullPath(folder);
        FileSystemWatcher? old;
        long generation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            old = _watcher; _watcher = null;
            _generation++; generation = _generation;
            _folder = folder; _pending.Clear(); _overflowed = false; Warning = null;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        old?.Dispose();

        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size
                    | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                Filter = "*",
                EnableRaisingEvents = false
            };
            watcher.Created += (_, e) => Queue(generation, new FolderChange(FolderChangeKind.Created, e.FullPath));
            watcher.Changed += (_, e) => Queue(generation, new FolderChange(FolderChangeKind.Changed, e.FullPath));
            watcher.Deleted += (_, e) => Queue(generation, new FolderChange(FolderChangeKind.Deleted, e.FullPath));
            watcher.Renamed += (_, e) => Queue(generation, new FolderChange(FolderChangeKind.Renamed, e.FullPath, e.OldFullPath));
            watcher.Error += (_, _) => QueueOverflow(generation);

            lock (_sync)
            {
                if (_disposed || generation != _generation) { watcher.Dispose(); return; }
                _watcher = watcher; watcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            lock (_sync)
            {
                if (generation == _generation)
                    Warning = "Live folder updates unavailable: " + ex.Message;
            }
        }
    }

    private void Queue(long generation, FolderChange change)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation) return;
            _pending.Add(change);
            _timer.Change(300, Timeout.Infinite);
        }
    }

    private void QueueOverflow(long generation)
    {
        lock (_sync)
        {
            if (_disposed || generation != _generation) return;
            _overflowed = true;
            _timer.Change(100, Timeout.Infinite);
        }
    }

    private void Flush()
    {
        FolderChangeBatch? batch = null;
        lock (_sync)
        {
            if (_disposed || (_pending.Count == 0 && !_overflowed)) return;
            batch = new FolderChangeBatch(_folder, _pending.ToArray(), _overflowed);
            _pending.Clear(); _overflowed = false;
        }
        try { BatchReady?.Invoke(batch); }
        catch { /* UI subscriber owns reporting; never kill the watcher timer. */ }
    }

    public void Dispose()
    {
        FileSystemWatcher? watcher;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; _generation++;
            watcher = _watcher; _watcher = null; _pending.Clear();
        }
        watcher?.Dispose(); _timer.Dispose();
    }
}
