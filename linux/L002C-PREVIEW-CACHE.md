# L002c3 - bounded viewer preview RAM cache

Status: compilation, preview-cache ownership/invalidation/reuse checks, all inherited thumbnail/browser/tab/viewer/sampling regressions and fresh-process session restoration **passed** on Ubuntu CI. Native Nova acceptance remains pending.

Version: **0.1.0-alpha.6**. Window label: **L002c3**.

## Behaviour

A shared process-local PreviewCache now retains the normal initial viewer decode under a **512 MiB accounted-storage budget** and **256-entry** ceiling. Capacity pressure evicts least-recently-used unretained entries. An active ImageViewer owns a lease; suspending a tab releases that lease while the cache may keep the preview for a fast return. Reacquiring a cached preview preserves the tab's in-memory zoom/pan and Smooth/Pixels state. The status line adds **cache hit** when the initial image came from PreviewCache.

The cache stores only the normal initial viewer result. For small sources that result can already be source resolution. Explicit full-resolution upgrades requested when zoom exceeds preview detail or 100% is selected are **not cached**; they are disposed when the tab is hidden and decoded again later only if the retained view still needs them.

Keys use exact case-sensitive absolute path, ViewerDecodePolicy.PreviewEdge, file length and UTC modification ticks. Metadata is re-read before reuse and after new decoding. Detected changes invalidate old entries; deleted/inaccessible files cannot use stale cached previews. This is not a content hash or watcher, so same-size/same-mtime replacements can evade detection. **Refresh/F5 clears both preview and thumbnail caches.**

The 512 MiB figure is retained decoded-preview accounting, not total process RSS/VRAM. Active leases, retired leased entries, decoder scratch memory, graphics/compositor copies, full-resolution images and unrelated application state can add memory. The cache allocates on demand and writes no disk files.

## Completed automated validation - 7 October 2026

Tested source: `36634e086259357d59d11bf80698993e3495c805`.

Passing run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37618007337

Job: `112780954408`. Runner: Ubuntu 24.04.5 LTS x64. Commands executed:

```bash
bash linux/check.sh
bash linux/smoke.sh
```

Confirmed from the completed log:

- Windows-baseline isolation and compilation passed with zero errors; the pre-existing obsolete Bitmap.Save test-helper warning remains.
- Warm preview acquisition reused the same decoded bitmap without a second preview decode.
- Case-distinct and Unicode paths stayed distinct.
- Changed files produced new pixels while an old active lease remained readable; deleted files could not use stale hits; corrupt previews were not cached and recovered after replacement.
- A deliberately small preview cache evicted within budget without invalidating a displayed lease.
- Controlled late-decode tests rejected results after cancellation, clear, shutdown and detected file change without retained ownership.
- Through the real MainWindow/ImageViewer path, switching away and back reused the shared preview with no additional preview decode and preserved the tab's zoom.
- After the image tabs were inactive, outstanding preview leases dropped to zero while bounded cache ownership remained; explicit clear drained unleased preview storage.
- The existing 600 browser-layout combinations, thumbnail cache, 42-tab single-row workflow, rendered Smooth/Pixels tests, source-resolution viewing, pointer-centred zoom, drag/pan, cancellation and fresh-process folder/ordered-tab/active-tab/closed-tab-exclusion session checks passed.
- Explicit full-resolution viewer upgrades were verified not to be retained in PreviewCache.

These fixture results are not a Nova latency benchmark, process-memory benchmark, VRAM measurement, preloading test or native Wayland validation.

## Isolation

Relative to L002c2, implementation changes are confined to Linux PreviewCache/ImageViewer/MainWindow integration, Linux version and Linux regression code. Windows/WPF, main, Myoken.Core, package versions and the session schema/location are unchanged. No new system, NVIDIA/CUDA or Python packages are required.

## Nova acceptance

Close Myoken normally, update the clean `linux/ubuntu-gnome` checkout with the guarded README block, run `bash linux/check.sh`, then `bash linux/run.sh`. Expect **L002c3**.

Open two or more ordinary screenshot tabs. Switch A → B → A. On the return to A, the bottom image status should include **cache hit** if its initial preview remained resident. Zoom/pan A before switching and confirm the same view returns. For an image wider than 4096 pixels, zoom enough to trigger full detail, switch away and back: the initial preview may be a cache hit, while full detail should reload only if the retained zoom still requires it. Press Refresh/F5 only when deliberately testing invalidation because it clears both caches.

Report perceived tab-switch responsiveness separately from correctness. Preloading is intentionally not part of L002c3; it is the next isolated pass after native cache acceptance.
