# Myoken Ubuntu/GNOME

Independent Linux development track in the existing Myoken repository.

- Development branch: **linux/ubuntu-gnome**; Linux feature branches are checked in CI before a fast-forward update.
- Windows baseline: **v0.2.167**, commit ea785b3770383278f1a3bf67762d35014e8bda26. Isolation checks are not a claim of GitHub branch-protection settings.
- Target: Ubuntu 24.04 LTS and newer with GNOME, initially x64.
- Linux UI: C# / .NET 10 / Avalonia 12.1.2. Current source version: **0.1.0-alpha.6**, window label **L002c3**.
- Portable core: .NET Standard 2.0, not yet referenced by Windows .NET Framework 4.8/WPF.

## Isolation contract

Use a separate checkout on the Linux filesystem. Do not modify src/ZonerInspiredViewer, Windows version/build journals, Windows dependency manifests or scripts. Never run tools/build.ps1 for Linux work: it reserves a Windows version even on failure. Do not modify main, merge to main, retag releases, replace WPF or read/write Windows profiles. Native Ubuntu is the GNOME acceptance target. WSL2 can be used for development, but the current workflow is native Ubuntu and WSLg is not GNOME/NVIDIA release validation.

GitHub changes do not update the user's checkout automatically. Linux features do not imply Windows feature parity. Existing repository licensing and third-party notices remain in force; distribution must include resolved transitive dependency notices.

## Existing checkout: update and launch

Close Myoken normally first. This block stops on a wrong branch, local changes, failed pull or failed checks; it never resets, discards or stashes work.

```bash
(
    set -e
    cd "$HOME/Projects/Myoken-Ubuntu-GNOME"
    if [ "$(git branch --show-current)" != "linux/ubuntu-gnome" ]; then
        printf 'Stop: expected linux/ubuntu-gnome.\n' >&2
        exit 1
    fi
    if [ -n "$(git status --porcelain)" ]; then
        printf 'Stop: local changes need review.\n' >&2
        git status --short
        exit 1
    fi
    git pull --ff-only origin linux/ubuntu-gnome
    git log -1 --format='%h %s'
    bash linux/check.sh
    bash linux/run.sh
)
```

No new APT packages, Ubuntu release upgrade, NVIDIA/CUDA changes or Python changes are required to update an already-working checkout. Run the viewer as your normal user, never sudo. The first NuGet restore requires network access. For a new machine, install git, dotnet-sdk-10.0, libx11-6, libice6, libsm6, libfontconfig1, libxrandr2, libxi6, libxcursor1, libgl1 and xwayland from configured Ubuntu feeds after reviewing APT's proposal, then clone linux/ubuntu-gnome into a new, separate directory. Do not delete an existing checkout to make cloning succeed.

## L002c3 viewer preview RAM cache

Image tabs now share a process-local cache for the normal initial viewer decode. The default retained-storage budget is **512 MiB**, allocated on demand, with LRU eviction and a 256-entry cap. Inactive tabs release their preview leases; recently used previews can remain cached and be reacquired without another preview decode. The cache stores only the normal initial preview result (which can equal source resolution for small images). Explicit full-resolution upgrades requested for zoom/100% are **not retained** and are still released when the tab becomes inactive.

Preview reuse preserves each tab's existing in-process zoom/pan and Smooth/Pixels state. When a cached preview is actually reused, the image status includes **cache hit**. File length/UTC mtime and exact case-sensitive path are checked before reuse and after a new decode. **Refresh/F5 clears both thumbnail and preview caches.** This is not filesystem watching or content hashing; same-size/same-mtime replacements still require Refresh.

The 512 MiB budget is accounted decoded-preview storage, not total application/GPU memory. Active leases, decoder scratch buffers, graphics copies, full-resolution upgrades and the rest of the process can use additional memory. Preview caching is per process and writes no files. See **linux/L002C-PREVIEW-CACHE.md** for validation and Nova acceptance.

## L002c2 thumbnail RAM cache

Browser reuses recently decoded thumbnails when scrolling back or revisiting folders. The default retained-storage budget is **128 MiB**, allocated on demand, with least-recently-used eviction and an additional 8192-entry cap. Visible controls lease shared thumbnails, so eviction cannot destroy an image still on screen. Cache ownership may retain off-screen bitmaps after the controls release their leases. Image-viewer previews/full-resolution images are not cached by this pass.

The Browser footer shows **Thumb cache** estimated storage, **Hits** and **Decodes**. Hover for misses, entries, evictions and outstanding leases. Hits with no new decodes demonstrate reuse, not a measured latency improvement. The budget uses pixel-format/row-alignment accounting and bookkeeping allowances; it is not total process/GPU memory. Displayed leases surviving eviction, decoder scratch buffers and graphics copies can use additional memory.

Each acquisition checks current file length/UTC mtime and the exact case-sensitive path/thumbnail size; a new decode rechecks metadata before caching. Deleted or observably changed files cannot use the old key. Same-size/same-mtime replacements are not reliably detected by metadata. **Refresh/F5 clears the thumbnail cache and reloads the browser**, including that case. There is no live file watcher. Cache contents and counters reset on exit. The cache writes no disk files and changes no original images.

See **linux/L002C-CACHE.md** for completed cache/CI checks, resource caveats and the Nova acceptance procedure. Initial scanning/sorting and first-time decodes are still required; this is not a speedup to full image-tab decoding or a large-folder throughput claim.

## L002c1 document tabs

Tabs occupy one fixed-height row. Browser is pinned on the left, image headers scroll horizontally, and Open tabs remains on the right. The wheel over the strip scrolls headers without zooming/selecting an image. Selecting an image or resizing reveals its header. Open tabs lists full filenames with parent folders, preserving Unicode and underscores.

Ctrl+Tab / Ctrl+Shift+Tab cycle the existing order including Browser, even with path focus. Ctrl+W, the close button or middle-click closes an image; middle release outside cancels. Browser is not closable. Active close selects right neighbour, otherwise left neighbour or Browser. Closing an inactive tab does not reload the active image. Header/menu controls are not virtualised; thousands-of-tabs and screen-reader acceptance are not claimed. The user reported this workflow "all good" on Nova; see linux/L002C-TABS.md.

## Browser inherited from L002a

Home/File system tree expansion loads one level asynchronously and retains ancestors. Links are navigable leaves, not recursively expanded. The continuous thumbnail grid realises only visible rows plus overscan. Columns reflow with width; two-line captions and full-path tooltips distinguish files. Off-screen controls now release cache leases rather than disposing shared bitmaps directly. Returning to Browser restores folder/count status.

Browser keys: arrows, Page Up/Down, Home/End, Enter/Space. Window keys: Refresh/F5, Ctrl+L, Ctrl+W, Ctrl+Tab/Ctrl+Shift+Tab and F11. One image per exact case-sensitive Linux path remains the tab identity rule.

## Image viewing inherited from L002b

Image tabs provide pointer-centred wheel zoom, left/middle drag pan, Fit, source-resolution 100%, minus/plus and a zoom indicator. Double-click toggles Fit/100%. With viewer focus: 0 = Fit, 1 = 100%, +/- = zoom, arrows = pan. With header focus, arrows instead navigate tabs. Fit does not enlarge small images.

Smooth is the default for scaled viewing; Smooth/Pixels opts into hard pixel edges when enlarged. Full-source 100% is unfiltered 1:1 in either mode; downsizing stays filtered. Changing modes edits no source image and preserves zoom/pan. The user reported Smooth was "much better" on Nova. See linux/L002B-SAMPLING.md for rendered-pixel tests.

100% maps source pixels to application render-target pixels, not guaranteed physical monitor pixels after XWayland/compositor scaling. The initial 4096-edge preview upgrades from the original when more detail is needed. Preview/loading/error states remain explicit. Full decode is guarded at 64 million source pixels; larger accepted images stay preview-only with 100% disabled. The input ceiling is 120 million pixels. These guards are not total process/GPU memory limits. See linux/L002B.md.

Each open tab keeps zoom/pan and sampling while switching, but releases inactive viewer bitmaps. These view settings are not saved across full restart; restored tabs start in Fit/Smooth. Folder, ordered tabs and active selection retain the unchanged session format/location:

```text
${XDG_STATE_HOME:-$HOME/.local/state}/myoken-linux/session.json
```

A second writer is excluded; corrupt, unreadable or newer-schema sessions are preserved and reported read-only. No Windows session import occurs.

## Scope limits and next work

Initial formats remain JPEG, PNG, WebP, BMP and the first GIF frame. Advanced codecs, video, metadata/EXIF orientation/colour-management parity, GPU decoding, filesystem watching, file operations and drag/drop are future work. Filenames are fully enumerated/sorted before publication; the directory tree is lazy, not virtualised. Thumbnail caching does not change these facts.

Next pass: low-priority preview preloading. The preview cache itself is now implemented; disk caching, incremental file publication and measured real 100k-image throughput remain unimplemented. Disk caching, incremental file publication and measured real 100k-image throughput remain unimplemented. A repeated-path synthetic allocation test is not a real large-folder performance benchmark. Do not retain full-resolution images for every tab.

This is GNOME-compatible, not GTK/libadwaita. The selected backend is X11 through XWayland in a GNOME Wayland session. Native Wayland, fractional scaling and hardware performance need separate validation.

## Tests and acceptance

bash linux/check.sh checks Windows isolation, compilation and original core/session/browser/viewer policies plus cache ownership/concurrency/metadata policies. bash linux/smoke.sh additionally needs Xvfb and Python 3, uses disposable generated fixtures/state, checks actual cache reuse/pixels/invalidation/cancellation, tab-strip inputs, rendered sampling, viewer behaviour and fresh-process session restoration without a starting-folder override. MYOKEN_TEST_OUTPUT optionally captures fixture screenshots; this is not a human GNOME visual review.

Source authoring, compilation, CI and user desktop validation are separate. Historical linux/VALIDATION.md and L002a/b/c1 records remain intact. Thumbnail-cache validation is in linux/L002C-CACHE.md; viewer-preview-cache validation and its pending Nova check are in linux/L002C-PREVIEW-CACHE.md. Read linux/AGENTS.md before development.
