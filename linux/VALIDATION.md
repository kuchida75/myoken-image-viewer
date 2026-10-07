# Linux validation record

## L001 source preview - 18 September 2026

Tested application-source commit: `a5d94bb6faea7c774a7b0910dcad1af82a31b071`.

GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/35346126932

Runner: `ubuntu-24.04`. Job completed successfully at 12:43:57 UTC on 18 September 2026.

Confirmed by the completed job:

- Windows-baseline isolation check passed.
- The .NET 10 Linux application compiled successfully.
- The independent natural-sort and session regression runner passed, including case-distinct paths, Unicode numeric sorting, session round-trip, second-writer exclusion, corrupt-session preservation and newer-schema preservation.
- The application launched a real Avalonia window under Xvfb, scanned generated PNG fixtures, completed thumbnail-page loading, and successfully decoded the selected image-tab preview.

The initial source commit adds files only. No pre-existing file was changed or deleted relative to Windows v0.2.167 commit `ea785b3770383278f1a3bf67762d35014e8bda26`. Windows still has no reference to `Myoken.Core`.

The original validation record was a documentation-only follow-up to the tested source commit. A headless test is not a visual usability review and does not establish GNOME/NVIDIA compatibility or advanced-feature parity. There is no packaged Linux release recorded here.

## Native Ubuntu desktop smoke check - 4 October 2026

Evidence: screenshots supplied by the user in the Myoken Ubuntu/GNOME project conversation, plus the explicit report, "tabs do restore after relaunch". This is user-performed desktop validation, not a remote test executed by the assistant. Screenshots remain in the conversation; they are not uploaded to this repository.

The preceding terminal screenshot reports Ubuntu 24.04.5 LTS, x86-64, `ubuntu:GNOME`, and a Wayland desktop session. The project configuration uses the X11 backend via XWayland; this record does not establish native Wayland-backend support. The exact local source revision, SDK version and graphics-driver version were not supplied with the desktop result.

Confirmed at the scope of the supplied evidence:

- The Linux preview window launches and renders its controls on the user's Ubuntu desktop.
- A child image folder is shown after the earlier parent-folder view.
- A populated thumbnail grid renders real image content. The browser reports page 1 of 11; this does not verify that all pages were visited or that every file decodes successfully.
- Three image-tab headers are visible alongside the Browser tab.
- The user explicitly confirms that image tabs restore after relaunch.

Not independently established by this evidence: correct rendering inside every image tab, exact active-tab restoration, closed-tab exclusion, exact tab ordering, independent folder restoration, local regression-run results, paging responsiveness, memory use, GPU decoding or full GNOME integration. Do not infer these from the tab-restoration report.

### Usability observations for the next iteration

- With Browser selected, the screenshot's status bar still displays a previous image's fit-preview details. Investigate refreshing status when returning to Browser.
- Repetitive long filenames are truncated in both thumbnail captions and tab headers. Improve filename distinguishability and full-name discoverability.
- The screenshot shows the initial paged browser, not the planned continuous virtualised grid or persistent expandable folder tree.

This update changes validation documentation only. No application code or Windows files are changed, and no new build or automated test was run for this documentation update.

## Remaining checks before full L001 desktop acceptance

Continue using the separate native-Linux checkout and Linux scripts. Preserve Windows profiles and the Windows workspace.

- Confirm image-tab content, closed-tab exclusion, restored tab order and active selection, and independent folder restoration after a normal full restart.
- Exercise nested folders, spaces, Japanese/Unicode names, case-distinct paths and natural numeric ordering on the target filesystem.
- Exercise rapid folder/page changes while decoding, missing/unreadable files and corrupt images without a crash.
- Check fractional scaling, fullscreen, high-resolution display behaviour and desktop folder-picker integration with the actual graphics driver.
- Capture the local source revision and `bash linux/check.sh` result when completing the full acceptance record.

L001 status: initial source compilation and CI passed; user-confirmed native Ubuntu launch, thumbnail display, multiple image tabs and tab restoration now recorded. Full desktop acceptance remains partial. Do not mark advanced codecs, GPU decoding, large-folder performance, full-resolution zoom/pan, metadata parity or filesystem integration as implemented. Native Wayland remains a separate evaluation.

## L002a automated browser/session validation - 4 October 2026

Tested application/test-source commit: `2d80fe2ab408fd7464bfdb82e21e45feb48836ac`.

Passing GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37211980804

Job `111464851149` completed successfully on an Ubuntu 24.04.5 x64 runner using .NET SDK 10.0.401. Its completed logs were reviewed. `bash linux/check.sh` passed isolation, compilation, the original sorting/session checks and 600 viewport combinations plus directory/path/cancellation tests. Compilation had zero errors and two obsolete-API warnings (TextBox.Watermark and the screenshot helper's Bitmap.Save overload).

`bash linux/smoke.sh` passed the real-X11-window integration checks under Xvfb: continuous scrolling beyond the former page limit, final-image decoding, resize reflow, bounded tile realisation with a synthetic 100k path list, corrupt/missing images, stale-load cancellation, tree ancestor retention, rapid navigation, thumbnail activation, tab deduplication, hidden-browser resource release and image preview decoding. A second process restored the folder without a command-line override, exact tab order and active image, excluded the closed tab, and refreshed the Browser folder/count status.

The synthetic 100k test reused 120 generated PNG paths and reported 25-30 live tiles for its tested viewport. It is not a real 100k-unique-image throughput, GPU or memory benchmark. CI uploaded three generated-fixture screenshots; these were not a human GNOME visual review. No user images or session data were used.

The candidate diff against Windows v0.2.167 contained additions only: no pre-existing baseline file was changed/deleted. The follow-up recording these results changes only linux/L002A.md and this validation document; application/test sources remain at the passing revision. Windows UI/build/version files, dependency versions and the session schema are not changed by L002a.

L002a automated status: passed. The subsequent user-confirmed native-desktop visual check is recorded below; it does not replace remaining interactive acceptance checks. The earlier L001 user report is preserved above and is not relabelled as a new L002a restart test. See linux/L002A.md for automated coverage, limitations and safe update/acceptance steps.

## L002a native Ubuntu visual check - 4 October 2026

Evidence: the user's screenshot of a window titled "Myoken Ubuntu/GNOME - Linux preview L002a", accompanied by "looks ok". This continues the native Ubuntu/Nova workflow established earlier in the conversation. This is a user-performed visual check, not desktop control or a test executed remotely by the assistant. The screenshot remains in the conversation and has not been uploaded to the repository. The exact local commit ID and current driver/SDK versions were not included in this report.

Visible in the supplied screenshot:

- The expanded folder tree retains Pictures as the parent of the selected Screenshots directory, with other folders still accessible in the sidebar.
- The continuous thumbnail-browser layout is populated with real image thumbnails and two-line filename captions; the former page controls are absent.
- Four image-tab headers are visible alongside the selected Browser tab. Their labels expose distinguishing date/time suffixes.
- The Browser status bar reports 786 images and the current folder rather than a previous image's fit-preview dimensions.
- The user reports that the interface "looks ok".

Result: native desktop rendering and the initial visual layout of L002a are user-confirmed. A screenshot does not establish sustained scrolling smoothness, successful decoding of all 786 files, resize behaviour over time, memory/GPU measurements, or a new L002a session-restoration test. Those checks remain separate from the earlier L001 restart confirmation and the passing L002a automated cross-process session tests. Fractional scaling, GNOME dialog integration and native Wayland remain outside this evidence.

This record is a documentation-only update to linux/VALIDATION.md. No application code, dependencies, session schema, Windows source or Windows branch is changed by this update, and no new manual or automated test result is claimed. The next planned implementation area remains image-viewer zoom/pan and controls, retaining browser and session regressions.

## L002b automated viewer validation - 4 October 2026

Tested application/test-source commit: `755de9ca5b6ff35e3a355080e738861ea310aa1c`.

Passing GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37214121233

Completed job `111471054237` ran on Ubuntu 24.04.5 x64 with .NET SDK 10.0.401. The completed log was reviewed. `bash linux/check.sh` passed Windows isolation, compilation, original sorting/session regressions, browser policies (including 600 viewport combinations), and the new viewer geometry/decode policies (including 60 size/scale combinations). Compilation had zero errors and one obsolete Bitmap.Save warning in the screenshot helper.

`bash linux/smoke.sh` passed the retained X11 browser checks under Xvfb and the new image-viewer integration checks: a 6000x2000 original decoded beyond the 4096-preview limit at 100%; root-coordinate wheel input retained its source-point anchor; pointer capture, drag movement and release passed; inactive tabs released bitmaps while preserving transient zoom/pan; Fit reset the view; cancelled full decodes could not populate hidden tabs; corrupt images left actual-size controls disabled. An unarranged Fit viewport did not trigger a full decode.

The fresh-process regression still restored the folder without a startup-path override, restored ordered tabs and active image, excluded a closed tab, and refreshed Browser status. Zoom/pan values are deliberately not part of the persisted session schema; after application restart, image tabs start in Fit. Full-resolution viewing is guarded at 64 million source pixels, with preview-only handling above that limit and a 120-million-pixel input ceiling. These are not total process/GPU memory limits.

Four generated-fixture screenshots were uploaded by CI; no user data was included. These results establish neither human GNOME visual acceptance nor physical monitor-pixel parity under compositor scaling. The first candidate's synthetic wheel-root assumption and a subsequent test Point API compile mismatch were corrected before this passing run, retaining the affected assertions.

The follow-up documenting this run changes only linux/L002B.md and this file. Windows baseline files, main, shared core, dependency versions and session schema are outside the L002b change. See linux/L002B.md for controls, resource caveats, exact automated coverage and Nova update/test steps.

L002b status: compilation and automated checks passed; native Nova/GNOME viewer acceptance pending. Earlier L001/L002a user confirmations remain scoped to those versions.

## L002b user feedback and sampling correction - 4 October 2026

The user reports that dragging and returning to the same viewing position work correctly, but the enlarged image looks pixelated. The supplied L002b screenshot shows a 1020 x 475 image at 176.8% with full-resolution status. This is user-performed validation of dragging and in-process view restoration, not a new full restart or exhaustive viewer test. Screenshots remain only in the conversation.

Source inspection found that ImageSurface selected unfiltered nearest-neighbour rendering for every zoom >=100%, making fractional enlargement unnecessarily blocky for normal viewing. The correction defaults to smooth resampling, retains genuine full-source 1:1 rendering at 100%, and makes Pixels an explicit per-tab enlargement choice. Mode selection does not reset zoom/pan or re-decode the loaded bitmap. No image files are modified and no extra source detail or AI upscaling is claimed.

Tested source: f8994de75e0491a059855ef57d5e44243bbaae40. Passing run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37232234047 ; job 111524211122. Completed logs were reviewed. This ran on the Ubuntu 24.04.5 CI runner with .NET SDK 10.0.401, not Nova. bash linux/check.sh and bash linux/smoke.sh both passed, with the one existing obsolete Bitmap.Save warning and zero compilation errors.

Actual rendered checkerboard pixels at 176.8%, 200% and 800% distinguished Smooth (144 blended interior samples each) from Pixels (0 each). A 100% rendering matched all 256 original fixture pixels. Reduced-preview/full-source transition and minification checks passed. Button routing, unchanged geometry/bitmap identity, independent defaults and in-process mode retention passed. Original browser, pointer/drag, source-resolution, cancellation and fresh-process folder/tab/active-tab/closed-tab-exclusion tests passed as well. Six generated-fixture screenshots were uploaded; no user data was included.

The patch changes only Linux rendering/toolbar code, tests, Linux app version and documentation. Windows/main, portable core, dependencies, decoding guards and session schema are unchanged. The documentation follow-up preserves the tested application source. Version is 0.1.0-alpha.3.1 and the title remains L002b; the Smooth/Pixels button identifies the correction. See linux/L002B-SAMPLING.md for full scope and Nova comparison steps. Native appearance of the corrected sampling still requires user verification; neither these tests nor the preceding report establishes physical monitor-pixel parity or overall GPU performance.

## Sampling patch native follow-up - 4 October 2026

The user subsequently supplied a screenshot with Smooth selected at 189.3%, reporting "much better". This records user-confirmed improvement of the corrected L002b enlargement on Nova. It does not establish all zoom levels, physical display-pixel parity, performance, a new restart test or acceptance of later L002c code. The screenshot remains in the conversation and is not uploaded to the repository.

## L002c1 automated single-row tabs - 4 October 2026

Tested source: `dcd1149fe1df69933af882715d9399e05cb2a790`.

Passing run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37234437118 ; job `111530640003`. The completed log was reviewed. Runner reported Ubuntu 24.04.5 x64 and .NET SDK 10.0.401. `bash linux/check.sh` and `bash linux/smoke.sh` passed, with zero compilation errors and the one existing obsolete Bitmap.Save warning.

The new X11 tests opened 42 image tabs at an 820-logical-pixel window width, verified the fixed single-row geometry and pinned Browser/menu, revealed the selected header, and checked that inactive headers did not cause decoding. Header-wheel input scrolled without selection or image-zoom changes. The actual Open tabs popup contained all 43 documents, preserving full Unicode/underscore names and distinct parent folders. Menu selection, Ctrl+Tab/Ctrl+Shift+Tab including path focus and wraparound, visible-header middle press/release, release-outside cancellation, adjacent-close selection, inactive-close bitmap identity, Browser close protection and recovery after all images closed passed.

All prior core/session, 600 browser viewport combinations, 60 viewer geometry/scale combinations, actual rendered Smooth/Pixels/100% sampling, browser/tree, original-resolution decode, pointer/drag, in-process view/sampling retention and cancellation checks passed. A fresh process restored its folder without a startup override, ordered tabs and active image, excluded a closed tab and retained the single-row strip. Seven generated-fixture screenshots were uploaded; no user data was included. No human GNOME/popup visual review or performance benchmark is implied.

The initial candidate's helper/type name conflict, overstrict centred-header test assumption and empty programmatically opened menu were corrected with the relevant assertions retained. Failing candidates were not promoted. The candidate diff against the Windows v0.2.167 baseline contains additions only. Changes relative to the previous Linux build are confined to document tabs, MainWindow integration, Linux version, tests and documentation; image decoding/sampling/geometry, core, dependencies and session schema are unchanged. The final follow-up is documentation only. See linux/L002C-TABS.md for exact coverage, limits and Nova acceptance.

L002c1 status: implementation and automated validation passed; its own native Nova test remains pending. RAM/disk image caching and preloading are not part of this pass. Main and Windows profiles/releases are outside this work.


## L002c2 native thumbnail-cache counter check - 7 October 2026

The user supplied a native Nova screenshot of L002c2 browsing the Screenshots folder. Visible counters report 798 images, 17.1 MiB / 128 MiB estimated thumbnail-cache storage, 60 cache hits and 205 decodes. This is user-supplied desktop evidence that the cache is active and has reused thumbnails; it is not a controlled latency benchmark or total-memory measurement. The screenshot remains only in the conversation. See linux/L002C-CACHE.md for the automated cache validation and exact budget caveats.

## L002c3 automated viewer-preview cache validation - 7 October 2026

Tested application/test-source commit: `36634e086259357d59d11bf80698993e3495c805`.

Passing GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37618007337 ; job `112780954408`. The completed Ubuntu 24.04.5 job and log were reviewed. `bash linux/check.sh` and `bash linux/smoke.sh` passed. Compilation had zero errors; the existing obsolete Bitmap.Save warning in the screenshot helper remains.

The new preview-cache checks passed warm bitmap reuse without a second preview decode, case-distinct/Unicode path identity, changed/deleted/corrupt-file handling, budget eviction with active-lease safety, cancellation/clear/shutdown/change rejection of late results, and shared application-cache reuse across real image-tab switches. Switching back reused the same cached preview without another preview decode and preserved the tab's zoom state. Inactive image tabs released all preview leases while bounded cache ownership could remain. Clearing the cache released unleased preview storage.

The existing viewer regression also confirmed that explicit full-resolution upgrades are **not** retained in PreviewCache: switching away releases the full bitmap, switching back reacquires the cached initial preview, and full detail is decoded again only when the retained zoom demands it. Existing thumbnail-cache, browser, single-row tabs, rendered Smooth/Pixels output, pointer-centred zoom, drag/pan, cancellation and fresh-process ordered-tab/session restoration tests all passed.

L002c3 uses a 512 MiB accounted preview-storage budget with a 256-entry cap. This is not total process/GPU memory and does not include decoder scratch space, graphics copies, outstanding retired leases or uncached full-resolution upgrades. The cache is process-local and writes no files. Native Nova responsiveness and visual acceptance of L002c3 remain pending; no user screenshot has yet been relabelled as acceptance of this build.


## L002c4 automated low-priority preview preloading - 7 October 2026

Tested application/test-source commit: `e8134f2a0c1ed9b1c5f30bf00174f2454d46dfd8`.

Passing GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37619928355 ; job `112787314852`. The completed Ubuntu job and log were reviewed. `bash linux/check.sh` and `bash linux/smoke.sh` passed. The initial L002c4 candidate exposed test-lifetime mistakes in the new priority-gate regressions; those test defects were corrected before this passing source and no failing candidate was promoted.

The preload policy passed: for an interior selected image the targets are next, previous and second-next; targets do not wrap at list edges. The real-window test selected an image tab, waited for the idle preload pass, and confirmed that the previous one plus next two previews entered the bounded PreviewCache. Activating a warmed neighbour was a cache hit with **no new preview decode**.

Priority-gate tests confirmed that a queued selected-image foreground decode is released before an already-queued background preload, and a cancelled queued preload cannot block later foreground work. This is queue priority, not native-operation pre-emption: a decode that has already entered the native codec must return before the serial gate can serve the next request.

All inherited L002c2 thumbnail-cache and L002c3 preview-cache checks passed, including changed/deleted/corrupt files, eviction/lease safety, cancellation and cache clearing. Existing single-row tab/menu/keyboard/middle-close, Smooth/Pixels rendered output, source-resolution viewing, pointer-centred zoom, drag/pan, full-resolution non-caching and fresh-process folder/tab/session restoration regressions also passed.

L002c4 preloading is process-local, sequential and limited by the existing 512 MiB / 256-entry preview cache. It warms only normal previews of neighbouring **open tabs**; it does not scan ahead through unopened folder images, preload full-resolution bitmaps, add a disk cache or establish a Nova performance improvement. Native Nova responsiveness/acceptance remains pending.


## L003a automated EXIF orientation and basic metadata - 7 October 2026

Tested application/test-source commit: `b1a3b7166a27a3312189069610690bf914b5c4a5`.

Passing GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37629673905 ; job `112820339136`. The completed Ubuntu job and log were reviewed. `bash linux/check.sh` and `bash linux/smoke.sh` passed. Compilation had zero errors and retained the existing obsolete Bitmap.Save warning in the generated-screenshot helper.

Portable core checks passed EXIF orientation normalization/display naming and axis-swap policy. Real synthetic JPEG fixtures with EXIF Orientation values **1 through 8** then passed both oriented-dimension and four-corner mapping assertions, covering normal, both mirrors, 180°, transpose, 90° CW, transverse and 90° CCW. The thumbnail decoder published oriented dimensions before caching. The viewer used oriented source geometry, while the Info panel exposed display/encoded dimensions and orientation.

The portable MetadataExtractor-based reader correctly extracted the fixture's EXIF orientation, camera make **MYOKEN**, model **L003A** and software field. The lazy Info panel displayed the camera metadata and `Rotate 90° CW (6)` for the orientation-6 fixture. Metadata descriptions are bounded (256 displayed tags, 2048 characters per description); this is a read-only metadata surface, not an editor.

All inherited L002c2 thumbnail-cache, L002c3 preview-cache and L002c4 preload checks passed, as did single-row tabs, rendered Smooth/Pixels/100% assertions, source-resolution viewing, pointer-centred zoom, drag/pan, full-resolution non-caching and fresh-process folder/tab/session restoration.

Earlier L003a candidates were not promoted: CI first exposed a MetadataExtractor extension-method import mismatch, then a viewer-property name collision and Skia overload incompatibility. Those compile issues were corrected without removing the new orientation/metadata assertions. Metadata Refresh semantics were also tightened before the passing run so a visible Info panel can reread after invalidation instead of requiring the tab to be recreated.

The incremental diff from L002c4 changes only Myoken.Core portable orientation/metadata code plus Linux decode/UI/tests/version. A comparison against Windows v0.2.167 found no Windows baseline source/build/version files changed. Myoken.Core adds the **MetadataExtractor 2.9.3** dependency; Windows still does not reference the core project. Distribution packaging must account for third-party licence/notices.

**Colour management is not implemented.** A reported EXIF colour-space value does not imply ICC/profile conversion, and these tests do not establish colour accuracy. HEIC/AVIF/JXL, RAW orientation behaviour, native Wayland, hardware performance and Nova visual acceptance remain outside this automated result.


## L003b automated source-profile sRGB normalization - 7 October 2026

Tested application/test-source commit: `a486886ad88240448bcbd5e3664c142b8f735c1b`.

Passing GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37631683377 ; job `112827293000`. The completed Ubuntu job and full log were reviewed. `bash linux/check.sh` and `bash linux/smoke.sh` passed. Compilation had zero errors and retained the one existing obsolete Bitmap.Save warning in the generated-screenshot helper.

The L003b test generated a lossless PNG whose source SKColorSpace is recognised as **Display P3** and whose raw tagged RGB values are **220/90/40**. OrientedImageDecoder classified it as Display P3, selected the non-sRGB profile-aware path and decoded to an sRGB target. The resulting Avalonia pixels matched an independent Skia sRGB-target decode exactly within the test tolerance: **238/78/4**. The assertion also confirmed those numbers differ materially from the original tagged values, demonstrating that a transform actually occurred rather than only relabelling the data.

The thumbnail path produced the same sRGB-normalised pixel result before cache publication. A real ImageViewer tab exposed `ColorConvertedToSrgb`, and its Info panel reported **Source: Display P3**, **Working: sRGB**, **Source → sRGB: applied**, plus the explicit boundary **Display profile: not applied by Myoken yet**.

All L003a EXIF-orientation/metadata regressions remained passing, as did L002c thumbnail/preview cache and preload checks, single-row tabs, Smooth/Pixels/100% rendering, full-resolution viewing, pointer-centred zoom, drag/pan and fresh-process folder/tab/session restoration.

Relative to L003a, changes are Linux decode/UI/test/version files only. A comparison against the Windows v0.2.167 baseline found no Windows source/build/version/script changes. L003b adds no package dependency.

**Scope boundary:** L003b normalises Skia-recognised tagged source colour spaces into an sRGB working space. It does not apply the active monitor's ICC profile, calibrate physical display output, guarantee GNOME/XWayland/compositor colour behaviour, or implement HDR/tone mapping. The automated Display P3 fixture proves source conversion, not end-to-end display accuracy. Native Nova visual/performance acceptance remains pending.


## L003c automated HEIC / HEIF / AVIF decoding - 7 October 2026

Tested application/test-source commit: `e29f4c53c1fa13f012f939c5a322f0bd2b318a0a`.

Passing GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37634344732 ; job `112836484365`. The completed Ubuntu job and full log were reviewed. `bash linux/check.sh` and `bash linux/smoke.sh` passed. Compilation had zero errors and retained the single pre-existing obsolete Bitmap.Save warning in the generated-screenshot helper.

The test workflow generated a 64×48 four-quadrant PNG, then used Ubuntu's `heif-enc` test tool to produce a real HEVC-backed **HEIC** fixture and a real AV1-backed **AVIF** fixture. The application itself did not use those Ubuntu decoder libraries: Myoken's tested runtime path used **PhotoSauce.NativeCodecs.Libheif 1.23.5-preview1**, whose NuGet package carries its Linux native decoder dependencies. The Ubuntu heif encoder packages are CI fixture-generation dependencies only.

Both generated containers passed primary full-resolution decode at 64×48, expected red-quadrant pixel checks, and 32-pixel-long-edge thumbnail decoding through the common ImageDecoder path. Browser support policy includes `.heic`, `.heif` and `.avif`. Both decoder results reported the libheif/MagicScaler sRGB-normalization path.

Real ImageViewer tabs opened HEIC and AVIF. The HEIC viewer used the primary dimensions, both **HEIC and AVIF Info panels completed the existing MetadataExtractor read without a metadata error**, and both reported decoder-managed sRGB working output. Switching away and back to the HEIC tab produced an existing PreviewCache hit without another preview decode. Fresh-process session restoration and every inherited L003a/L003b/L002 regression remained passing.

The first L003c candidate was not promoted because compilation exposed only an ambiguous `PixelFormats` name between Avalonia and PhotoSauce. It was corrected by explicitly qualifying PhotoSauce's BGRA format; no codec assertion was removed. A later test-only follow-up strengthened coverage to require successful Info-panel metadata parsing for **both** formats; that is the tested revision recorded above.

Runtime scope is intentionally limited. L003c exposes the primary still image only. The current PhotoSauce libheif decoder publishes an 8-bit RGB pixel source, so HEIF alpha planes and >8-bit/HDR precision are not claimed. The libheif source decodes the primary image before MagicScaler downsizes it, meaning a thumbnail can still incur full native decode work/memory even though Myoken's retained thumbnail bitmap is bounded. Existing 120M input and 64M full-resolution guards apply to reported primary dimensions but are not total native decoder-memory guarantees.

A comparison against Windows v0.2.167 found no Windows source/build/version/script changes. Myoken.Core and the Linux session schema are unchanged. L003c adds the Linux-only PhotoSauce libheif package and modifies the Linux CI workflow solely to generate test fixtures.

Distribution must review the codec/native dependency licences and notices. HEVC/HEIC patent-licensing obligations may vary by jurisdiction/distribution. JPEG XL, HEIF sequences/auxiliary images, alpha/high-bit-depth preservation, HDR/tone mapping and native Nova performance remain outside this automated result.


## L003d automated JPEG XL decoding - 7 October 2026

Tested application/test-source commit: `6c2d57e900df134adaec108260b2973a9f1e02a7`.

Passing GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37645201794 ; job `112874047078`. The completed Ubuntu job and full log were reviewed. `bash linux/check.sh` and `bash linux/smoke.sh` passed. Compilation had zero errors and retained the one pre-existing obsolete Bitmap.Save warning in the generated-screenshot helper.

The CI workflow generated a 64×48 RGBA PNG with four colour quadrants and bottom-right alpha **64**, then encoded it losslessly to a genuine JPEG XL file with Ubuntu's `cjxl` test tool. Myoken's runtime decode used **PhotoSauce.NativeCodecs.Libjxl 0.12.0-preview1** and its bundled Linux libjxl, not Ubuntu's runtime decoder package; `libjxl-tools` exists in CI only to create the fixture.

The JXL fixture passed browser-extension recognition, full first-frame decode at 64×48, expected opaque red-quadrant pixels, and **exact first-frame alpha 64** in the decoded Avalonia bitmap. A 32-pixel-long-edge thumbnail passed through the common ImageDecoder path. A real ImageViewer tab used the expected visual dimensions and Info reported decoder-managed JPEG XL colour plus sRGB working output.

MetadataExtractor 2.9.3 does not expose JPEG XL container EXIF/XMP to the existing portable reader. L003d therefore makes this a controlled Info-panel limitation: JXL Info displays file/image/color data plus an explicit `JPEG XL EXIF/XMP detail extraction is not exposed in this panel yet` note, with no generic metadata parse error.

Switching Browser→JXL reused the existing PreviewCache with no additional preview decode. The new AdvancedCodecRegistry was also exercised by decoding HEIC **after** JPEG XL; the HEIC thumbnail still succeeded, proving libjxl registration does not replace libheif in PhotoSauce's global CodecManager.

All inherited HEIC/AVIF, Display P3→sRGB, EXIF orientation 1-8, thumbnail/preview cache, neighbour preload, single-row tabs, Smooth/Pixels/100%, zoom/pan and fresh-process session checks remained passing.

The first L003d candidate was not promoted because it referenced PhotoSauce's inaccessible internal `MultiFrameDecoderOptions`. Source inspection identified the plugin's public `JxlDecoderOptions`, and the passing revision explicitly uses `new JxlDecoderOptions(0..1)` to retain first-frame-only behavior. No JPEG XL assertion was removed.

Runtime limits: first frame only; no JXL animation playback/page navigation. The current PhotoSauce/libjxl pixel output is 8-bit, so alpha is preserved but >8-bit/HDR precision is not claimed. The decoder materializes a native full frame before downstream resize, so thumbnail requests can have transient native RAM/CPU cost beyond Myoken's retained cache size. Detailed JXL EXIF/XMP bridging remains future work. Myoken uses the plugin only for reading, even though the package also exposes encoding.

A comparison against Windows v0.2.167 found no Windows source/build/version/script changes. Myoken.Core and the Linux session schema are unchanged. L003d adds the Linux-only PhotoSauce libjxl package and CI-only `libjxl-tools` fixture generation. Native Nova acceptance and large real-world JXL performance remain pending.


## L004a automated live folder updates and rename/delete - 7 October 2026

Tested application/test-source commit: `86c4c5624eb50e9b11a704e0c53e318c79c7174d`.

Passing GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37647465253 ; job `112881872446`. The completed Ubuntu job and full log were reviewed. `bash linux/check.sh` and `bash linux/smoke.sh` passed. Compilation had zero errors and retained the single pre-existing obsolete Bitmap.Save warning in the generated-screenshot helper.

The integration test created a disposable watched folder on the Ubuntu runner and exercised the real Linux FileSystemWatcher path. A supported image created externally appeared without F5. An externally renamed open image updated the Browser path and **the same existing DocumentTab/ImageViewer object**; after reload the test confirmed the viewer identity was unchanged and its zoom value was preserved. Replacing the file contents at the same path invalidated PreviewCache, triggered a new decode and preserved zoom because the replacement retained the same dimensions. External deletion removed the Browser entry and closed the matching tab.

The same fixture exercised Myoken's operation cores. Same-folder rename moved the file on disk and immediately updated Browser state. The permanent-delete core removed only the disposable test image and Browser entry. The test then returned to the original generated folder before the inherited browser/viewer/session suite continued.

L004a adds targeted `InvalidatePath` operations to ThumbnailCache and PreviewCache, avoiding a global cache flush for ordinary changed/renamed/deleted files. VirtualThumbnailBrowser live updates preserve selection/scroll where possible. FolderWatcher uses a generation token across navigations and debounces events into 300 ms batches; watcher overflow requests a full folder rescan.

Open-tab rename reconciliation updates the mutable DocumentTab path, refreshes its existing header and rebinds the same ImageViewer. The ImageViewer's path and metadata source are mutable only through the controlled rebind method; decoded bitmap ownership is still released/reacquired according to the existing viewer/cache rules. Session schema/location are unchanged and subsequent normal saves serialize the new tab path.

User-facing delete is **permanent** in L004a. The dialog labels the action `Delete permanently`, displays the target path and explicitly states that GNOME Trash is bypassed and Myoken cannot undo it. Automated tests invoke only the core on disposable generated fixtures; they do not delete user data. Trash/recycle integration and move-to-another-folder are not part of L004a.

The first candidate was not promoted because Avalonia 12's TextBox did not expose the two-argument `Select` helper used by the rename dialog. The passing source uses `SelectionStart` / `SelectionEnd`; no watcher/file-operation assertion was removed.

Every inherited L003d/L003c/L003b/L003a/L002 check remained passing, including HEIC/AVIF/JXL, color/orientation/metadata, thumbnail/preview caches, neighbour preloading, single-row tabs, Smooth/Pixels/100%, pointer zoom/pan and fresh-process session restoration.

A comparison against Windows v0.2.167 found no Windows source/build/version/script changes. Myoken.Core, package versions and the session schema are unchanged. Native Nova UX/performance acceptance of watcher behavior and the new confirmation dialogs remains pending.


## L004b move and GNOME Trash — 2026-10-07

Application source `4df01c641f72bb426562f5cdf6ce0c5bf38383d9` passed the complete Ubuntu isolation/build/policy/headless smoke/fresh-process suite in push run 37649622246 and PR run 37649629785. The real GIO recoverable-content/trashinfo assertions, move watcher-drain identity/zoom checks and session assertions passed. Human GNOME acceptance remains pending.

Normal Delete uses confirmed GIO Trash without a permanent-delete fallback. Explicit permanent deletion remains separately labelled and confirmed. Move-to-folder refuses overwrites and updates the existing tab/viewer in place. Windows v0.2.167 isolation passed; main is unchanged. See linux/L004B-MOVE-TRASH.md.
