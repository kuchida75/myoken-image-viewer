namespace Myoken.Linux;

internal enum ViewerDecodePriority
{
    Foreground,
    Background
}

// One-at-a-time viewer decode gate with foreground-first queueing. A native decode
// already holding the gate is not pre-empted, but queued background work never
// jumps ahead of a queued foreground request.
internal sealed class PriorityAsyncGate
{
    internal sealed class Lease : IDisposable
    {
        private PriorityAsyncGate? _owner;
        internal Lease(PriorityAsyncGate owner) => _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }

    private sealed class Waiter
    {
        public required TaskCompletionSource<Lease> Source;
        public CancellationTokenRegistration Registration;
    }

    private readonly object _sync = new();
    private readonly Queue<Waiter> _foreground = new();
    private readonly Queue<Waiter> _background = new();
    private bool _held;

    public ValueTask<Lease> EnterAsync(ViewerDecodePriority priority, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_held)
            {
                _held = true;
                return ValueTask.FromResult(new Lease(this));
            }

            var waiter = new Waiter
            {
                Source = new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously)
            };
            (priority == ViewerDecodePriority.Foreground ? _foreground : _background).Enqueue(waiter);
            if (token.CanBeCanceled)
            {
                waiter.Registration = token.Register(() => waiter.Source.TrySetCanceled(token));
            }
            return new ValueTask<Lease>(waiter.Source.Task);
        }
    }

    private void Release()
    {
        while (true)
        {
            Waiter? next;
            lock (_sync)
            {
                next = DequeueUsable(_foreground) ?? DequeueUsable(_background);
                if (next == null)
                {
                    _held = false;
                    return;
                }
            }

            if (next.Source.TrySetResult(new Lease(this)))
            {
                next.Registration.Dispose();
                return;
            }
            next.Registration.Dispose();
        }
    }

    private static Waiter? DequeueUsable(Queue<Waiter> queue)
    {
        while (queue.Count > 0)
        {
            var waiter = queue.Dequeue();
            if (!waiter.Source.Task.IsCompleted) return waiter;
            waiter.Registration.Dispose();
        }
        return null;
    }
}
