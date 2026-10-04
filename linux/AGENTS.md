# Myoken Ubuntu/GNOME development context

Read linux/README.md and the current iteration record before work.

## Boundaries

- Work on linux/ubuntu-gnome or a Linux feature branch in a separate Linux-filesystem checkout.
- Preserve every Windows v0.2.167 file at ea785b3770383278f1a3bf67762d35014e8bda26, WPF, main and Windows profiles. Never run Windows build/version scripts or retag/publish releases for ordinary Linux work.
- Myoken.Core stays netstandard2.0 without UI/Skia/OS/GPU dependencies. Windows still does not reference it.
- Preserve case-sensitive Linux path identity and the existing session schema/location.

## Current iteration

L002b: ImageViewer owns active-tab decoding and transient per-tab view state; ImageSurface handles drawing/pointer input; ImageViewport holds pure geometry; ViewerDecodePolicy/ViewerImageLoader enforce source dimensions and output-size guards. Hidden tabs retain geometry, not bitmaps. Source pixels, reduced-preview pixels, DIP units and render-target pixels are distinct; do not equate a stretched preview with full resolution. Zoom/pan is not persisted across restart. Input event positions in tests must use presentation-root coordinates, just like real pointer events.

L002a tree/continuous thumbnail code remains the browser baseline. The tree is lazy, not virtualised; directory lists are fully scanned/sorted. Do not infer real 100k-folder throughput from repeated fixture paths. Thumbnail and serial viewer decode gates are separate. Output pixel guards do not establish a process-memory limit.

Run bash linux/check.sh, then bash linux/smoke.sh where Xvfb is available. Keep original core/session, browser and fresh-process restoration assertions. Tests must use disposable fixtures and isolated state; no user images/sessions in CI. The restore process must not receive a startup-folder override.

Report exact source revision, tests actually completed, known limitations and next desktop acceptance step. Maintain validation history in linux/VALIDATION.md and iteration records without turning user screenshots or earlier-version tests into full acceptance of a new build. Do not change main or advance the shared Linux branch over failing CI.
