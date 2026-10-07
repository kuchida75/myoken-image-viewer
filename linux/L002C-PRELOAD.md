# L002c4 - low-priority neighbour preview preloading

Status: source compilation, preload-policy/priority checks, real-window cache warming, inherited thumbnail/preview-cache suites, viewer/tab/rendering regressions and fresh-process session restoration **passed** on Ubuntu CI. Native Nova acceptance remains pending.

Version: **0.1.0-alpha.7**. Window label: **L002c4**.

## Behaviour

After the currently selected image preview is ready, Myoken waits **150 ms** before starting background work. It considers only open image tabs and warms at most three neighbours into PreviewCache:

1. next image tab,
2. previous image tab,
3. second-next image tab.

Targets do not wrap around the tab list. Browser is never a preload target. Every selected-tab change, switch to Browser, Refresh/F5 and shutdown cancels the previous schedule. The scheduler is sequential; it does not fan out three simultaneous image decodes.

A warmed operation acquires the normal PreviewCache entry and immediately releases its lease. The existing cache is therefore still the only retained owner and remains bounded to **512 MiB accounted preview storage / 256 entries**. Preloading never requests ViewerImageLoader's full-resolution path. If a tab later needs full detail because of zoom/100%, that foreground decode remains uncached as in L002c3.

ViewerImageLoader now uses a foreground/background priority gate. If foreground and background work are both queued, foreground is served first. This cannot pre-empt a native decode already running; cancellation prevents stale publication and the running native call must return before the gate moves on. The short idle delay reduces avoidable work during rapid tab switching.

## Completed automated validation - 7 October 2026

Tested source: **e8134f2a0c1ed9b1c5f30bf00174f2454d46dfd8**.

Passing run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37619928355

Job: **112787314852**. The completed Ubuntu log was reviewed. Commands:

```bash
bash linux/check.sh
bash linux/smoke.sh
```

Confirmed:

- preload target policy chooses next, previous and second-next without wrapping;
- the viewer decode gate releases queued foreground work before queued preload work;
- cancelled queued background work does not block a later foreground request;
- the real MainWindow path warmed all three available neighbours after an interior selection;
- selecting a warmed neighbour produced a PreviewCache hit with no additional preview decode;
- L002c2 thumbnail reuse/invalidation, L002c3 preview cache reuse/invalidation/ownership, single-row tabs, rendered sampling, zoom/pan, full-resolution non-caching and fresh-process session restore all retained their passing regressions.

The first candidate failed to compile because a test incorrectly treated PriorityAsyncGate itself as disposable. A quick follow-up then exposed a malformed test scope/lease lifetime. Both were test-only defects and were corrected before the passing revision above; the affected priority assertions were retained.

## Limits

This pass preloads only neighbours among **already-open image tabs**. It does not automatically open or preload images adjacent in the current folder that do not yet have tabs. It does not measure speed on Nova, change the 512 MiB accounting caveats, cache full-resolution upgrades, create disk files, watch the filesystem or add codecs.

A running native decode cannot be forcibly interrupted. Foreground priority applies when choosing among queued decode requests. Cache eviction may also remove a warmed preview before it is selected; in that case a normal foreground decode occurs.

## Nova acceptance

Close Myoken, update the clean `linux/ubuntu-gnome` checkout, run `bash linux/check.sh`, then `bash linux/run.sh`. Expect **L002c4**.

Open at least four ordinary screenshot tabs. Select the second tab and pause briefly. Then select the third, first and fourth tabs. Warmed tabs should generally show **cache hit** in their image status and appear promptly. Rapidly switch several tabs to check that stale preloads do not make the selected image lag badly or change the active selection.

Also recheck a large >4096-pixel image at zoom/100%: background preloading must not retain its full-resolution upgrade. Refresh/F5 intentionally clears both thumbnail and preview caches and cancels preloading.

Report perceived responsiveness separately from correctness; CI proves queueing/cache behaviour with fixtures, not a Nova latency benchmark.
