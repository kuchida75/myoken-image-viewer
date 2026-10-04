# L002c2 - bounded thumbnail RAM cache

Status: source candidate; compilation and automated/native validation are pending. Do not advance linux/ubuntu-gnome over failed checks.

Version: 0.1.0-alpha.5. Window label: L002c2. This pass implements thumbnail caching only, not image-preview caching or preloading.

## Behaviour

The browser retains recently used decoded thumbnails under a 128 MiB accounted-storage budget and an 8192-entry ceiling. Capacity pressure evicts least-recently-used entries. Visible tiles borrow explicit leases rather than disposing shared bitmaps. Evicted thumbnails remain valid while a tile still displays them and are destroyed after the last lease ends. Hidden browser controls release their leases; the cache may retain the image for reuse. Shutdown releases cache references and rejects late requests, while already-issued leases can finish safely.

The footer shows estimated retained thumbnail storage, cache hits and decode attempts. Its tooltip includes misses, entries, capacity evictions, outstanding leases, retired leased bytes, invalidations and changed-during-decode results. Counters are cumulative until application exit. They are not latency benchmarks. Pixel-format/row-alignment accounting and a bookkeeping allowance estimate storage; graphics copies, decoder scratch buffers, active retired leases and the rest of the application are not covered by the 128 MiB resident-cache budget.

Keys use the exact case-sensitive absolute path, requested thumbnail edge, file length and UTC modification ticks. Metadata is re-read in bounded background work before each potential hit and again after a new decode. Observed changes invalidate old entries; deleted/inaccessible metadata cannot use an old hit. A changed-during-decode result is rejected rather than published. Metadata is not a content hash: same-size/same-mtime replacement may go undetected, and files can change after any metadata observation. Refresh/F5 explicitly clears the cache and reloads the browser. Already-visible images are not watched; revisit or Refresh after external changes.

At most two cache acquisition/decode requests run simultaneously; queued requests honour cancellation. An old generation cannot repopulate the cache after clearing or shutdown. Concurrent same-key misses can decode twice, but only one bitmap is retained; the duplicate result is disposed. No unbounded in-flight dictionary or failure cache is kept. Native decode and filesystem calls cannot be interrupted mid-call; cancelled late results are discarded.

## Isolation and next work

Windows/WPF/main, portable core, package versions, image-viewer decoding/zoom/pan/sampling and persisted session schema remain unchanged. This cache is per browser/process and writes no thumbnail files. The existing source-image decoding limits remain. No new system/driver/Python packages are required on a working checkout. Preview cache, preloading, disk cache, filesystem watching and real large-folder performance measurements remain separate passes.

Run bash linux/check.sh and bash linux/smoke.sh on generated fixtures, retaining original tab/viewer/sampling/session checks. New tests cover ownership, byte/count bounds, parallel leases, metadata, real bitmap reuse, case/Unicode/size distinctions, changed/deleted/corrupt files, explicit invalidation, cancellation, clearing/shutdown during decoding and scroll/folder return reuse. Passing synthetic/CI tests do not establish user-dataset speed or GNOME acceptance.

## Nova acceptance after passing CI

Close Myoken normally. Update the clean linux/ubuntu-gnome checkout with the guarded README block, then run bash linux/check.sh and bash linux/run.sh. Expect L002c2 in the title and a Thumb cache footer in Browser.

Use Pictures/Screenshots. Let the first view finish, scroll down, then return. Previously retained views should increase Hits without increasing Decodes; uncached regions still decode. Leave the folder and return to check reuse. Refresh/F5 deliberately clears thumbnails, so decoding after that is expected. Check that tabs, zoom/pan, Smooth/Pixels and ordinary close/relaunch restore still work. Cache contents/counters reset on exit. Report responsiveness separately from counter reuse; a thumbnail cache does not speed up the complete filename scan or image-tab full-resolution loading.
