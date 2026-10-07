# Myoken Ubuntu/GNOME

Independent Linux development track in the existing Myoken repository.

- Development branch: **linux/ubuntu-gnome**; Linux feature branches are checked in CI before a fast-forward update.
- Windows baseline: **v0.2.167**, commit ea785b3770383278f1a3bf67762d35014e8bda26. Isolation checks are not a claim of GitHub branch-protection settings.
- Target: Ubuntu 24.04 LTS and newer with GNOME, initially x64.
- Linux UI: C# / .NET 10 / Avalonia 12.1.2. Current source version: **0.1.0-alpha.11**, window label **L003d**.
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

## L003d JPEG XL decoding

The Linux browser/viewer now recognises **.jxl** files through **PhotoSauce.NativeCodecs.Libjxl 0.12.0-preview1**. Its NuGet package carries libjxl native binaries for Linux glibc x64/arm64, so Nova does **not** need Ubuntu JPEG XL packages for runtime decoding. The CI workflow installs Ubuntu `libjxl-tools` only to generate a disposable real JXL fixture with `cjxl`; it is not a Myoken runtime dependency.

L003d also introduces one process-wide **AdvancedCodecRegistry** that registers both libheif and libjxl together. PhotoSauce's CodecManager is global, so this prevents opening/registering JPEG XL from replacing HEIC/AVIF support, or vice versa.

JPEG XL decoding deliberately selects **frame 0 only** using the public `JxlDecoderOptions(0..1)`. MagicScaler normalises the first frame's EXIF-style orientation, converts the decoder's ICC/profile output into Myoken's SDR **sRGB working space**, preserves first-frame alpha into BGRA32, and then feeds the existing thumbnail/viewer/cache paths. Existing 120-million-input-pixel and 64-million-full-resolution guards apply to the visual first-frame dimensions.

Important limits:

- first frame only; JPEG XL animation is not played or exposed as separate tabs/pages;
- PhotoSauce/libjxl currently requests **8-bit output**, so alpha is preserved but 10/12/16-bit or HDR precision is not;
- JPEG XL ICC/color data participates in the decoder-managed sRGB path, but detailed JXL EXIF/XMP boxes are **not yet bridged into Myoken's portable metadata panel**; Info states this explicitly rather than showing a parse error;
- the decoder materialises a full native frame before Myoken/MagicScaler thumbnail downscaling, so a tiny retained thumbnail can still have larger transient native CPU/RAM cost;
- Myoken uses the package for decoding only even though the plugin also has encoder support.

The real CI fixture is a lossless 64×48 RGBA JXL. Tests confirm full decode, requested-edge thumbnail, viewer tab, exact alpha value **64**, PreviewCache reuse and that HEIC still decodes after JPEG XL has used the shared codec registry. See **linux/L003D-JPEG-XL.md**.

## L003c HEIC / HEIF / AVIF decoding

The Linux browser/viewer now recognises **.heic, .heif and .avif** files. These formats use **PhotoSauce.NativeCodecs.Libheif 1.23.5-preview1** as a decode-only plugin. Its NuGet package carries the compatible native libheif decoder stack for Linux x64/arm64, so an already-working Nova checkout does **not** need Ubuntu libheif packages installed just to run Myoken. The CI workflow installs Ubuntu `heif-enc` and encoder plugins only to generate disposable HEIC/AVIF fixtures during testing; they are not application runtime dependencies.

HEIC/AVIF decoding selects the primary still image, lets libheif normalise container orientation, asks MagicScaler to convert exposed source profiles into the existing **sRGB working space**, converts the result to BGRA32 and then feeds the same Myoken thumbnail/viewer/cache paths used by other formats. Existing 120-million-input-pixel and 64-million-full-resolution guards still apply to the primary image dimensions. PreviewCache reuse and Info-panel metadata parsing are covered by integration tests.

Important decoder limits in this pass:

- primary still image only; auxiliary images/sequences are not exposed as separate Myoken pages/tabs;
- the current PhotoSauce libheif pixel source is **8-bit RGB**, so L003c does not claim preservation of HEIF alpha planes or >8-bit/HDR precision;
- thumbnail requests still cause libheif to decode the primary image before MagicScaler downsizes it, so large HEIC/AVIF files can have native decode CPU/RAM cost beyond the retained thumbnail-cache budget;
- output is normalised to SDR sRGB; HDR/tone mapping and monitor-profile output remain outside this milestone.

The codec package is decode-only. Distribution must carry the resolved PhotoSauce/libheif/native dependency notices, and HEVC/HEIC patent-licensing obligations can vary by jurisdiction and distribution model. JPEG XL is implemented separately in L003d.

See **linux/L003C-HEIC-AVIF.md** for exact automated validation and Nova acceptance.

## L003b source-profile-aware sRGB normalization

Skia-recognised tagged/embedded **non-sRGB source colour spaces** are now decoded into Myoken's sRGB working space before thumbnails, preview-cache entries or full viewer pixels are published. The source classifier labels sRGB, Display P3, Adobe RGB, Rec.2020, sRGB primaries with a non-sRGB transfer function, and other custom/ICC spaces. Untagged/unspecified sources continue to be treated as sRGB.

For the common **TopLeft + sRGB/untagged** case, Myoken keeps the existing Avalonia decode path. Non-sRGB tagged sources use Skia's profile-aware decode with an sRGB target. Images already requiring the L003a orientation transform share that sRGB Skia path. Resizing/orientation intermediates stay tagged sRGB before their bytes are copied into Avalonia, so the thumbnail and preview caches retain already-normalised pixels.

Image status adds **color→sRGB** when a source conversion occurred. **Info** now distinguishes Source colour, Working colour (**sRGB**), whether Source → sRGB was applied, and explicitly states that a display/monitor profile is **not applied by Myoken yet**. This avoids confusing the EXIF colour-space text with actual pixel conversion.

The passing fixture encoded raw Display P3 values **220/90/40** and the managed path produced the same sRGB reference as Skia: **238/78/4**. The thumbnail path matched the same reference. See **linux/L003B-COLOR-MANAGEMENT.md** for exact validation.

**This is source-profile normalisation, not end-to-end monitor calibration.** GNOME/XWayland/compositor/display-profile behaviour is still outside Myoken's own output pipeline, and HDR/tone mapping is also not implemented. No new package is introduced by L003b; the existing SkiaSharp backend performs the conversion.

## L003a EXIF orientation and basic metadata

Current JPEG/PNG/WebP/BMP/GIF decoding now normalises the decoder's encoded orientation before a thumbnail or viewer preview enters either RAM cache. EXIF/encoded orientations **1-8** are supported, including mirrored/transposed cases. The normal TopLeft path keeps the established Avalonia decode path; images requiring an orientation transform use the Linux Skia path before publication. Viewer geometry and 100%/zoom calculations use the visually oriented dimensions while **Info** reports both display and encoded dimensions.

Image tabs add an **Info** button; with viewer focus, **I** toggles the same side panel. The panel reads metadata lazily and shows file information, orientation, common camera/EXIF fields (camera make/model, date taken, exposure, aperture, ISO, focal length, software, reported colour-space tag and description) plus a bounded metadata-detail list. **Refresh/F5** invalidates thumbnail/preview caches and metadata; an already-visible Info panel rereads metadata automatically.

Portable metadata DTOs, orientation policy and the metadata reader live in Myoken.Core (netstandard2.0). The reader uses **MetadataExtractor 2.9.3**; UI and pixel transforms remain Linux-specific, so Windows WPF is still untouched and does not yet reference Myoken.Core. Distribution packaging must include the resolved third-party licence/notices.

**L003a adds metadata/orientation; L003b adds source-profile-to-sRGB pixel conversion; L003c adds HEIC/HEIF/AVIF; L003d adds first-frame JPEG XL.** Monitor/display-profile output, HDR/tone mapping and full end-to-end colour-management parity remain separate work. See **linux/L003A-ORIENTATION-METADATA.md** for exact automated coverage and Nova acceptance.

## L002c4 low-priority neighbour preloading

After the selected image preview is ready, Myoken waits briefly (150 ms) and warms up to three neighbouring **open image tabs** into the existing bounded PreviewCache: next image first, previous image second, then the second-next image. The target list does not wrap. Preloading stores only normal initial previews; it never requests or retains full-resolution upgrades.

Every selection change, Browser switch, Refresh and shutdown cancels the old preload schedule. Pending viewer decodes use a foreground-first gate so a selected-image request is dequeued before pending background preload work. A native decode that has already started cannot be forcibly pre-empted; its cancellation result is discarded safely. Preloading is sequential and uses the same bounded 512 MiB / 256-entry preview cache, so it does not create a second unbounded image store.

There is no new user setting in this pass. A successfully warmed neighbour should appear as a **cache hit** when selected. See **linux/L002C-PRELOAD.md** for completed CI coverage and Nova acceptance.

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

Current formats are JPEG, PNG, WebP, BMP, first-frame GIF, primary-image HEIC/HEIF/AVIF and first-frame JPEG XL. EXIF/encoded orientation, basic read-only metadata and source-profile-to-sRGB normalisation are implemented. Video, monitor-profile/HDR colour-management parity, GPU decoding, filesystem watching, file operations and drag/drop are future work. Filenames are fully enumerated/sorted before publication; the directory tree is lazy, not virtualised. Thumbnail caching does not change these facts.

Neighbour preview preloading is implemented in L002c4, EXIF orientation/basic metadata in L003a, source-profile normalisation in L003b, HEIC/HEIF/AVIF in L003c and first-frame JPEG XL in L003d. The originally targeted advanced still-image codecs are now present at baseline decode level; monitor-profile/HDR remains a separate colour-pipeline milestone. Disk caching, incremental file publication and measured real 100k-image throughput remain unimplemented. A repeated-path synthetic allocation test is not a real large-folder performance benchmark. Do not retain full-resolution images for every tab.

This is GNOME-compatible, not GTK/libadwaita. The selected backend is X11 through XWayland in a GNOME Wayland session. Native Wayland, fractional scaling and hardware performance need separate validation.

## Tests and acceptance

bash linux/check.sh checks Windows isolation, compilation and core/session/browser/viewer/cache policies including portable orientation rules. bash linux/smoke.sh additionally needs Xvfb and Python 3, uses disposable generated fixtures/state, checks cache reuse/pixels/invalidation/cancellation, tab-strip inputs, rendered sampling, all eight synthetic EXIF orientation transforms, the Info metadata panel, a tagged Display P3→sRGB pixel reference, real generated HEIC/AVIF behavior, a real lossless-alpha JPEG XL decode/thumbnail/cache/registry path, viewer behavior and fresh-process session restoration without a starting-folder override. MYOKEN_TEST_OUTPUT optionally captures fixture screenshots; this is not a human GNOME visual review.

Source authoring, compilation, CI and user desktop validation are separate. Historical linux/VALIDATION.md and L002a/b/c1 records remain intact. Thumbnail/preview/preload validation remains in the L002c records. Orientation/metadata validation is in linux/L003A-ORIENTATION-METADATA.md and source-profile colour validation in linux/L003B-COLOR-MANAGEMENT.md. HEIC/AVIF validation is in linux/L003C-HEIC-AVIF.md. Current JPEG XL validation and its pending Nova check are in linux/L003D-JPEG-XL.md. Read linux/AGENTS.md before development.
