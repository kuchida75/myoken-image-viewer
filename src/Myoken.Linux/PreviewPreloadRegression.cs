namespace Myoken.Linux;

internal sealed partial class MainWindow
{
    private async Task RunPreviewPreloadChecksAsync()
    {
        var targets = PreviewPreloadPolicy.Targets(
            new[] { "/tmp/a", "/tmp/b", "/tmp/c", "/tmp/d", "/tmp/e" }, "/tmp/c");
        CheckUi(targets.SequenceEqual(new[] { "/tmp/d", "/tmp/b", "/tmp/e" }),
            "preload policy selects next, previous and second-next without wrapping");
        CheckUi(PreviewPreloadPolicy.Targets(new[] { "/tmp/a", "/tmp/b" }, "/tmp/a")
            .SequenceEqual(new[] { "/tmp/b" }), "preload policy respects list edges");

        _preloader.Cancel();
        _previewCache.Clear();
        var tabs = _files.Take(4).Select(AddImageTab).ToArray();
        _preloadTestEnabled = true;
        try
        {
            await SelectForTestAsync(tabs[1]);
            await _preloader.CurrentTask.WaitAsync(TimeSpan.FromSeconds(15));
            var warm = _preloader.Snapshot;
            var cache = _previewCache.Snapshot;
            CheckUi(warm.Completed >= 3 && cache.Memory.Entries >= 4,
                "selected tab warms previous one and next two previews into the bounded cache");

            var before = _previewCache.Snapshot;
            _preloadTestEnabled = false; // Keep this activation deterministic.
            await SelectForTestAsync(tabs[2]);
            var viewer = (ImageViewer)tabs[2].Content;
            var after = _previewCache.Snapshot;
            CheckUi(viewer.PreviewCacheHit && after.DecodeAttempts == before.DecodeAttempts,
                "activating a preloaded neighbour is a cache hit with no new preview decode");

            using var gate = new PriorityAsyncGate();
            using var held = await gate.EnterAsync(ViewerDecodePriority.Foreground);
            var background = gate.EnterAsync(ViewerDecodePriority.Background).AsTask();
            var foreground = gate.EnterAsync(ViewerDecodePriority.Foreground).AsTask();
            held.Dispose();
            var first = await Task.WhenAny(background, foreground).WaitAsync(TimeSpan.FromSeconds(5));
            CheckUi(ReferenceEquals(first, foreground), "queued selected-image decode outranks queued background preload");
            using var foregroundLease = await foreground;
            foregroundLease.Dispose();
            using var backgroundLease = await background.WaitAsync(TimeSpan.FromSeconds(5));

            using var blocker = await gate.EnterAsync(ViewerDecodePriority.Foreground);
            using var cancelled = new CancellationTokenSource();
            var stale = gate.EnterAsync(ViewerDecodePriority.Background, cancelled.Token).AsTask();
            cancelled.Cancel(); blocker.Dispose();
            try { await stale; throw new Exception("Cancelled background preload acquired decode gate."); }
            catch (OperationCanceledException) { Console.WriteLine("PASS: cancelled queued preload does not block later foreground work"); }
            using var finalForeground = await gate.EnterAsync(ViewerDecodePriority.Foreground).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            _preloadTestEnabled = false;
            _preloader.Cancel();
            await SelectForTestAsync(_browser);
            foreach (var tab in tabs) CloseTab(tab);
            _previewCache.Clear();
        }

        Console.WriteLine("PASS: L002c4 neighbour preloading and foreground-priority regression checks");
    }
}
