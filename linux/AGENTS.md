# Myoken Ubuntu/GNOME development context

Read linux/README.md and the current iteration record before work.

## Boundaries

- Work on linux/ubuntu-gnome or a Linux feature branch in a separate Linux-filesystem checkout.
- Preserve every Windows v0.2.167 file at ea785b3770383278f1a3bf67762d35014e8bda26, WPF, main and Windows profiles. Never run Windows build/version scripts or retag/publish releases for ordinary Linux work.
- Myoken.Core stays netstandard2.0 without UI/Skia/OS/GPU dependencies. Windows still does not reference it.
- Preserve case-sensitive Linux path identity and the existing session schema/location.

## Current iteration

L002c2 adds a browser-owned 128 MiB thumbnail RAM cache with an 8192-entry cap. LeasedLruCache owns retained entries; visible Tile objects own leases, never raw bitmap disposal. Eviction/clear removes cache ownership but must preserve displayed leases. Dispose late/cancelled decoded results. Metadata keys include ordinal full path, requested edge, length and UTC mtime; same-size/same-mtime changes require Refresh/F5. Metadata checks do not constitute filesystem watching or content hashing. Clearing/shutdown changes a generation, preventing old acquisitions from repopulating the cache. Two acquisition slots bound work; duplicate misses can decode twice but only one result is retained. Do not claim the retained-byte estimate is total process/GPU memory. The footer reports estimates and lifetime counters. See linux/L002C-CACHE.md for completed tests and remaining Nova acceptance.

L002c1: DocumentTab is transient identity/content, not a persisted DTO. DocumentTabs owns the single-row header controls, pinned Browser, horizontal scrolling, open-tabs popup and selection. MainWindow owns viewers, decoding activation, closing and unchanged session writes. Never reset the selected content or fire selection change when closing only an inactive tab. Header keyboard navigation is distinct from viewer pan keys. Browser is not closable; active-close fallback is right neighbour, then left, then Browser. The user reported this workflow "all good" on Nova; see linux/L002C-TABS.md.

L002b: ImageViewer owns active-tab decoding and transient per-tab geometry/sampling mode. ImageSurface draws/handles pointer input; ImageViewport holds pure geometry; ViewerDecodePolicy/ViewerImageLoader enforce output-size guards. Inactive image tabs retain geometry, not decoded viewer bitmaps. Viewer preview/full-resolution images are NOT in the new thumbnail cache. Source pixels, preview pixels, DIP units and render-target pixels differ. Zoom/pan and Smooth/Pixels choice are not persisted across restart. Routed pointer tests use presentation-root coordinates.

L002a folder tree is lazy, not virtualised; filenames are fully scanned/sorted. The thumbnail controls are virtualised and own visible leases; cached off-screen thumbnail bitmaps can now remain resident within budget. Do not infer 100k-folder throughput from repeated fixture paths. Thumbnail and serial viewer decode gates remain separate. Preview caching, preloading and disk caching are future passes.

## Checks and reporting

Run bash linux/check.sh and bash linux/smoke.sh where Xvfb is available. Retain core/session, browser, actual rendered-pixel sampling, viewer, tab-strip, cache ownership/concurrency/invalidation and fresh-process restoration assertions. Tests use disposable fixtures and isolated state, never user images/sessions. The restore process must not receive a startup-folder override. Rendering and popup/control event checks are not human GNOME or screen-reader acceptance.

Report exact source revision, tests actually completed, limitations and the next desktop check. Preserve historical validation documents; record current cache results in linux/L002C-CACHE.md. Do not relabel earlier screenshots as acceptance of a new build, assert measured speed from cache-hit counters, or advance linux/ubuntu-gnome over failing CI. A failed tool write is not a completed commit; verify every branch update.
