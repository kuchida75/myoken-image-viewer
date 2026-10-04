# Myoken Ubuntu/GNOME development context

Read linux/README.md and the current iteration record before work.

## Boundaries

- Work on linux/ubuntu-gnome or a Linux feature branch in a separate Linux-filesystem checkout.
- Preserve every Windows v0.2.167 file at ea785b3770383278f1a3bf67762d35014e8bda26, WPF, main and Windows profiles. Never run Windows build/version scripts or retag/publish releases for ordinary Linux work.
- Myoken.Core stays netstandard2.0 without UI/Skia/OS/GPU dependencies. Windows still does not reference it.
- Preserve case-sensitive Linux path identity and the existing session schema/location.

## Current iteration

L002c1: DocumentTab is transient identity/content, not a persisted DTO. DocumentTabs owns the single-row header controls, pinned Browser, horizontal scrolling, open-tabs popup and selection. MainWindow owns viewers, decoding activation, closing and the unchanged session writes. Never reset the selected content or fire a selection change when only closing an inactive tab. Header keyboard navigation is distinct from viewer pan keys. Browser is not closable; active-close fallback is right neighbour, then left, then Browser. See linux/L002C-TABS.md.

L002b: ImageViewer owns active-tab decoding and transient per-tab geometry/sampling mode. ImageSurface handles drawing/pointer input; ImageViewport holds pure geometry; ViewerDecodePolicy/ViewerImageLoader enforce source dimensions and output-size guards. Hidden tabs retain geometry, not bitmaps. Source pixels, preview pixels, DIP units and render-target pixels are distinct. Zoom/pan and Smooth/Pixels selection are not persisted across restart. Routed pointer tests use presentation-root coordinates, just like real pointer events.

L002a tree/continuous thumbnail code remains the browser baseline. The tree is lazy, not virtualised; directory lists are fully scanned/sorted. Do not infer real 100k-folder throughput from repeated fixture paths. Thumbnail and serial viewer decode gates are separate. Output pixel guards are not process-memory limits. Reusable thumbnail/preview caches and preloading are still future passes, not part of L002c1.

Run bash linux/check.sh, then bash linux/smoke.sh where Xvfb is available. Retain core/session, browser, rendered-pixel sampling, viewer, tab-strip and fresh-process restoration assertions. Tests use disposable fixtures and isolated state, never user images/sessions. The restore process must not receive a startup-folder override. Rendering and popup/control event checks are not human GNOME or screen-reader acceptance.

Report exact source revision, tests actually completed, limitations and the next desktop check. Maintain validation history in linux/VALIDATION.md and iteration records without relabelling earlier screenshots or checks as acceptance of new builds. Do not advance the shared Linux branch over failing CI. A failed tool write is not a completed commit; verify every branch update.
