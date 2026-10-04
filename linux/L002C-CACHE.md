# L002c2 - bounded thumbnail RAM cache

Status: compilation, cache policy and real-bitmap/browser reuse checks, original browser/tab/viewer/sampling regressions and fresh-process session tests passed on the Ubuntu CI runner. Native Nova responsiveness/acceptance of this new cache remains pending.

Version: **0.1.0-alpha.5**. Window label: **L002c2**. This pass implements thumbnail caching only, not image-preview caching or preloading.

## Behaviour

The browser retains recently used decoded thumbnails under a **128 MiB accounted-storage budget** and an 8192-entry ceiling. Capacity pressure evicts least-recently-used entries. Visible tiles borrow explicit leases rather than disposing shared bitmaps. Evicted thumbnails remain valid while a tile still displays them and are destroyed after the last lease ends. Hidden browser controls release their leases; the cache may retain the image for reuse. Shutdown releases cache references and rejects late requests, while already-issued leases can finish safely.

The footer shows estimated retained thumbnail storage, cache hits and decode attempts. Its tooltip includes misses, entries, capacity evictions, outstanding leases, retired leased bytes, invalidations and changed-during-decode results. Counters are cumulative until application exit. They are not latency benchmarks. Pixel-format/row-alignment accounting and a bookkeeping allowance estimate storage; graphics copies, decoder scratch buffers, active retired leases and the rest of the application are not covered by the 128 MiB resident-cache budget. Values grow on demand; 128 MiB is not allocated at startup.

Keys use the exact case-sensitive absolute path, requested thumbnail edge, file length and UTC modification ticks. Metadata is re-read in bounded background work before each potential hit and again after a new decode. Observed changes invalidate old entries; deleted/inaccessible metadata cannot use an old hit. A changed-during-decode result is rejected rather than published. Metadata is not a content hash: same-size/same-mtime replacement may go undetected, and files can change after any metadata observation. **Refresh/F5 explicitly clears the cache and reloads the browser.** Already-visible images are not watched; revisit or Refresh after external changes.

At most two cache acquisition/decode requests run simultaneously; queued requests honour cancellation. An old generation cannot repopulate the cache after clearing or shutdown. Concurrent same-key misses can decode twice, but only one bitmap is retained; the duplicate result is disposed. No unbounded in-flight dictionary or failure cache is kept. Native decode and filesystem calls cannot be interrupted mid-call; cancelled late results are discarded.

## Completed automated validation - 4 October 2026

Tested application/test-source commit: **e3c9100f40819261b5ba2658064c61883f30c448**.

Passing GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37235861484

Job: **111534701509**. The completed job steps and full log were reviewed before promoting the source. Runner reported Ubuntu 24.04.5 LTS x64 and .NET SDK 10.0.401. Tests ran on GitHub Actions, not on Nova or an assistant-controlled user desktop. The assistant container had no .NET SDK and its attempted public checkout could not resolve github.com; no local-container compilation is claimed.

Commands executed by CI:

```bash
bash linux/check.sh
bash linux/smoke.sh
```

Confirmed by the completed log:

- Windows-baseline isolation and compilation passed. Zero errors; the one existing obsolete Bitmap.Save warning in the screenshot helper remains.
- Original core/session tests passed; 600 browser viewport combinations and directory checks, and 60 viewer geometry/scale combinations passed.
- Cache ownership tests passed: LRU order, shared resource reuse, duplicate disposal, idempotent release, active leases surviving eviction/clear/shutdown, oversized-entry bypass, byte and entry caps, case-sensitive keys, selective invalidation and late insertion after shutdown.
- **2000 parallel acquisitions/evictions** released every generated fake resource exactly once while respecting the retained-byte budget. This is an ownership/concurrency test, not a 2000-image performance benchmark.
- Real BMP thumbnails reused identical bitmap objects on hits without decoding again. Case/Unicode/size keys remained distinct. Changed files produced new pixel values while old displayed leases stayed readable. Deleted files could not use old cache hits; corrupt images were not cached and recovered after replacement. Explicit clearing handled a same-size/same-mtime edit.
- Concurrent same-key requests retained one shared bitmap with at most two producers. Controlled late-decode tests rejected results after cancellation, clear, shutdown and a detected file change, leaving no retained ownership.
- The actual browser's scroll-away/back test reported **25 hits and zero additional decodes**. Returning to the visited fixture folder also reused thumbnails without new decodes. These are measured fixture results, not Nova speed measurements.
- Clearing every resident entry left current browser leases displayable. Refresh forced new decoding within budget. Hiding Browser released every tile lease, leaving live accounted storage equal to the bounded cache-owned storage.
- Existing continuous scrolling, resize, tree navigation, stale-load rejection, 42-tab/single-row/menu/keyboard/middle-close checks and all Smooth/Pixels rendered-output assertions passed. Original-resolution loading, pointer-centred zoom, drag/capture/release, per-tab view/sampling retention and cancellation passed.
- A fresh process restored its folder without a startup override, exact ordered remaining tabs and the active image, excluded the closed tab and retained the fixed tab strip and correct Browser status.

The initial source candidate e8e3fbe also passed. The final tested follow-up strengthens the hidden-browser lease assertion and corrects its description: controls release leases, while cache-owned bitmaps may remain reusable. No original behavioural assertion was removed.

Eight generated-fixture screenshots were uploaded by CI, including the view while its visible cache entries had been evicted. No user images/session data were uploaded. Screenshot generation is not a human GNOME visual review. The inherited 100k-entry check still repeats fixture paths and is not real large-folder throughput. Cache byte counters are not allocator/process/GPU measurements.

## Isolation and handoff

The candidate diff against Windows v0.2.167 (ea785b3770383278f1a3bf67762d35014e8bda26) contains additions only; no pre-existing baseline file was modified/deleted. Relative to L002c1, changes are confined to the Linux thumbnail cache/browser integration, Linux tests/scripts/workflow, Linux app version and docs. Windows/WPF/main, portable core, package versions, image-viewer loading/zoom/pan/sampling and session schema are unchanged.

This final follow-up changes only this record, README.md and AGENTS.md; application/test source remains the passing e3c9100 revision. Historical linux/VALIDATION.md and the L002a/b/c1 records are retained, not overwritten. The user's preceding L002c1 "all good" report is recorded in linux/L002C-TABS.md and is not relabelled as cache acceptance.

The cache is per browser/process and writes no thumbnail files. No new system/driver/Python packages are required on a working checkout. Preview cache, preloading, disk cache, filesystem watching and real large-folder measurements remain separate passes.

## Nova acceptance

Close Myoken normally. Update the clean linux/ubuntu-gnome checkout with the guarded README block, then run bash linux/check.sh and bash linux/run.sh. Expect L002c2 in the title and a Thumb cache footer in Browser.

Use Pictures/Screenshots. Let the first view finish, scroll down, then return. Previously retained views should increase Hits without increasing Decodes; uncached regions and capacity-evicted entries still decode. Leave the folder and return to check reuse. Refresh/F5 deliberately clears thumbnails, so decoding after that is expected. Check tabs, zoom/pan, Smooth/Pixels and normal close/relaunch restore. Cache contents/counters reset on exit. Report responsiveness separately from counter reuse: thumbnail caching does not speed up the full filename scan or image-tab full-resolution loading.
