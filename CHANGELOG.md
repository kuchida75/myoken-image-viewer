# Changelog

## 0.2.167 - 2026-09-18

- Renamed the application and source project to Myoken Image Viewer, including window titles, Help, Windows file details, MyokenImageViewer.csproj and the MyokenImageViewer.exe build output.
- Updated current documentation and repository links for kuchida75/myoken-image-viewer. Historical release names, the existing shutter icon and Apache-2.0 licensing are retained.
- Kept established profile paths, environment overrides, instance/storage locks, backup identifiers and batch-backup folders unchanged. Existing tabs, tab stacks, sessions, favorites, settings and caches need no migration or reset.
- Updated branding and executable-launch regression checks for the new identity, including the executable assembly name.

## 0.2.166 - 2026-09-17

- Replaced the File toolbar's leading ellipsis with a document icon and added a small trailing dropdown chevron. File commands, tooltip and accessible name are unchanged.
- Added a focused label regression check and verified dark/light, wide/narrow toolbar layouts.

## 0.2.163-0.2.165 - 2026-09-17

- Added named, collapsible tab stacks for image and folder tabs, a virtualized tab-selection dialog, stack menus for switching/renaming/managing members, and individual/all-tab unstacking without closing tabs.
- Grouped tabs share an accent; pinned colors/protection, per-tab views and previews remain intact. Duplicate stays in its source stack. Stack headers drag as blocks; individual tabs can move into or out of groups.
- Automatic/named sessions and JSON backups preserve names, membership, order and collapsed state. Old profiles remain compatible. Backup import validates stack references and removes empty stacks after missing-image cleanup. Reset and recovery backups include stacks.
- Added generated-fixture stack UI, drag, persistence, missing-file, validation, reset and legacy compatibility checks, with dark/light screenshots and existing tab-width/reorder/backup regression coverage.

## 0.2.155-0.2.162 - 2026-09-17

- Added Batch rename, Batch resize and Batch convert format under File, the browser Batch button and thumbnail context menus, with dark/light native dialogs, background previews, per-item exclusions, progress/cancellation and persisted workspace presets.
- Rename includes protected extensions, numeric/letter sequences, original/folder/date/dimension tokens, find/replace, text removal, prefix/suffix, whitespace and case rules. Conflicts are previewed. Staging supports swaps/case-only changes; rollback and a durable recovery map protect against failures without claiming crash-atomic operation.
- Resize includes pixels, percentage, print dimensions/DPI, long/short edge, aspect and fit controls, enlarge/reduce restrictions and output-format selection. Conversion writes JPEG, PNG, WebP, AVIF, JPEG XL and still GIF; HEIC/HEIF remain decode-only inputs. Animated/multi-image exports are explicitly skipped.
- Added destination/suffix/subfolder, quality/lossless, metadata/date and conflict options. Originals are retained by default; explicit overwrite keeps backups. Encoded outputs are verified before publishing and stale inputs/late conflicts are refused. Batch export is bounded multithreaded CPU processing of on-disk originals, not a batch application of transient viewer adjustments.
- Updated tab/favorite/navigation remapping to handle name swaps without cascading. File watchers and targeted thumbnail invalidation now refresh pixels even after same-size/date overwrites. Batch rename pauses folder scans during staged changes.
- Added generated-fixture safety/codec/session tests, native dark/light/minimum-size screenshots and a 100k-row virtualized preview check. Build and recovery details are documented in README and Help.

## 0.2.153-0.2.154 - 2026-09-17

- Renamed the application to Zen Image Viewer in window titles, Help, startup errors and Windows executable metadata. Build output and packages now use ZenImageViewer; the source project is ZenImageViewer.csproj.
- Replaced the detailed multicolor metallic shutter with a bold teal/ivory shutter, preserving transparent edges and seven native icon sizes from 16 to 256 pixels. WPF now receives the full multi-size ICO as well as the executable's native Windows resource.
- Retained profile paths, instance/storage locks, internal data types and backup identifiers so existing tabs, sessions, favorites, settings and caches remain compatible without migration.
- Added branding/metadata, icon-size/palette/transparency, native resource and dark/light preview checks; exercised real renamed executable instances and saved-session recovery using isolated fixtures.

## 0.2.146-0.2.152 - 2026-09-17

- Reorganized the main toolbar into global Open folder/Sessions/File/View/Configure commands and a contextual row. Browsing shows file actions; image mode shows Back to folder, navigation, zoom, enhancement, AI upscale and print. Quick Enhance remains available across both views. Search stays on the right without forcing a separate row at the normal minimum width.
- Added clickable folder breadcrumbs with an overflow list of ancestors, an editable address (Ctrl+L), native Open folder (Ctrl+O), relative/environment-variable paths, inline validation and Escape cancellation. Existing custom bindings, including thumbnail selection modifiers, take precedence over new defaults.
- Added one-click filename-filter clearing, Escape-to-clear while typing, Show in Explorer and Copy full path under File, plus a direct Thumbnails entry to its size/shape settings. View groups panel/overlay visibility, compact/fullscreen/window-height options and performance details.
- Reworked Configure with a persistent category sidebar, headings and cross-category settings search. Search finds labels/commands, supports multiple terms and reports no results. Controls align consistently, focused settings remain visible on resize, and keyboard capture retains priority over search keys.
- Moved detailed CPU/cache/iGPU/dGPU telemetry into a collapsible Performance bar. GPU-image residency stays visible in its summary; expanded details are remembered in sessions/backups and hidden in compact mode. GPU rendering, file operations, image adjustments and original photos are unchanged by the layout work.
- Added native dark/light/narrow-screen workflow, address/focus/cancel, settings-search, shortcut-migration and persistence checks; updated existing layout tests for the intentional redesign.

## 0.2.138-0.2.145 - 2026-09-17

- Added an explicitly managed D3D11 resident-image texture cache shared with the WPF viewer through D3D9Ex/D3DImage. Existing overlays, zoom/pan/rotation, Quick/Advanced/Manual Enhance and editing/export workflows remain available. Non-JPEG decoders upload once; neighbor preloading also warms textures.
- Added bundled nvJPEG/CUDA hybrid JPEG decoding into a GPU buffer with GPU conversion to the shared display texture. CPU mirrors support current analysis/export workflows. ICC-enabled/unsupported JPEGs, animation, unsupported texture sizes and device failures retain CPU/WPF fallbacks. No system CUDA installation is required.
- Replaced the stub pressure monitor with live DXGI process-budget readings. The texture cache enforces a separate user allowance, reclaims inactive LRU entries, and hands displayed images back to WPF before releasing pinned surfaces under pressure. File revisions, equivalent-decode identity and generation checks protect reuse and obsolete work.
- Added Configure controls for image GPU, rendering/decoder toggles and VRAM allowance (default 4096 MB), persisted through sessions/backups/reset. Added renderer, residency, effective budget, cache-hit/upload/decode/eviction diagnostics, separate from system GPU counters and CPU cache limits.
- Added actual RTX/nvJPEG/shared-surface pixel checks, controlled pressure/lease-lifetime tests and integrated enhancement, navigation, print, theme and persistence checks. This is a D3D11/WPF implementation; it does not claim fully GPU-only decoding/editing, hard process-VRAM caps or tested GPU hot-unplug/TDR behavior.
- Fixed image navigation input being discarded while a post-save/file-watcher folder refresh is running; deferred input is canceled when leaving the view or switching tabs.

## 0.2.135-0.2.137 - 2026-09-17

- Added top-toolbar icons for Zoom lock, Fit width, Fit height and original size (1:1), with descriptive, shortcut-aware tooltips and accessible names. Image-only controls disable in browser/loading states.
- Width/height fitting centers the image and preserves proportions, including rotated images. Explicit axis fitting may enlarge small images; ordinary fit-to-frame still caps at 100%.
- Zoom lock preserves the current level across image navigation, tabs and rotation. Manual zoom updates the locked level; AI-resolution replacement preserves apparent size. The highlighted toggle and level persist in automatic/named sessions and backups; older profiles/reset default to unlocked.
- Added rebindable commands (unassigned by default), native geometry/UI/navigation/AI-coordinate and session/backup regression coverage.

## 0.2.129-0.2.134 - 2026-09-17

- Added a printer toolbar command and rebindable Ctrl+P shortcut to open a dark/light print preview for the current image. Existing custom Ctrl+P assignments remain intact.
- Preview and output use full image pixels with current Quick Enhance, manual adjustments, rotation and applied AI upscale. Animation captures one frame; viewport zoom/crop, overlays and viewer decorations are excluded. Source files are unchanged.
- Added installed-printer selection, driver-supported paper sizes, portrait/landscape, centered fit or crop-to-fill, millimeter margins, copies and preview-only zoom. Printable-area and driver validation guard against unsupported page settings; transparent pixels print over white paper.
- Background rendering/capability reads and asynchronous Windows print submission support cancellation and errors. No-printer configurations retain preview-only mode. No job is submitted until Print is pressed, and no Windows printer defaults are changed.
- Added geometry, adjusted-image capture, one-page XPS output, UI/theme/zoom/layout, cancellation and shortcut regression checks. Initial scope excludes multi-image/contact-sheet printing and ICC soft-proofing.

## 0.2.128 - 2026-09-16

- Added bundled SwinIR-M Real-World x4 as a fifth AI upscale menu option and Configure choice, using the official medium real-world GAN checkpoint's EMA weights converted to checksum-verified ONNX. No separate Python installation or runtime download is needed.
- Reuses DirectML/CPU processing, pressure-aware fallback, fractional-scale slider, source-based rebuilds, status colors, cancellation, Enhance/export, and session/backup selection. Native 4x reconstruction is filtered to the existing 1x-3x scale choices and conservatively blended with source chroma/alpha.
- Added SwinIR checkpoint/export provenance and license, model-specific export selection, CPU/RTX/parity/tile checks and menu/switching/restore/export coverage. Existing default model and SUPIR's optional setup are unchanged.

## 0.2.127 - 2026-09-16

- Added theme-aware AI upscale status colors: amber for processing/cancellation, green for a completed upscale, blue for original-resolution bypass, red for errors, and neutral text when off. The footer factor and preview-dialog progress/status follow the same palette.
- The AI upscale split button now has a clear amber processing fill, green active fill or blue 1x fill, retained on hover/press. Turning it off or leaving the image clears the highlight; canceling a scale change preserves the previous preview's active color.
- Added focused native UI checks for state transitions, cancellation, errors, navigation, dark/light switching and readable text contrast. No changes to model inference or original-image saving.

## 0.2.124-0.2.126 - 2026-09-16

- Added a split AI upscale button: the main action/S uses the selected model; the attached arrow opens Basic, Real-ESRGAN, HAT / Real-HAT and SUPIR choices. Configure > Image tools also exposes the selection. The model persists in sessions/backups; older profiles default to Basic.
- Bundled genuine RealESRGAN_x4plus and fidelity-oriented Real_HAT_GAN_SRx4, exported to fixed 128px RGB ONNX tiles. Both use the selected DirectML GPU, original-based fractional scaling, overlapping tiles, source alpha/chroma preservation and existing subject/skin-detail safeguards. GPU pressure falls back to CPU with the same selected model.
- Switching models cancels obsolete work and restarts active previews from original pixels. Status and comparison windows identify the model. Missing models produce a clear error, never an undisclosed Basic substitution.
- Added optional SUPIR setup for a trusted local CUDA/Python/SUPIR installation. Its adapter uses SUPIR-v0F without LLaVA, runs offline, supports cancellation and exact fractional output, and keeps machine-specific executable paths out of backups. SUPIR checkpoints/runtime are not bundled or downloaded, and full SUPIR inference has not been validated in this release.
- Added model-menu, real CPU/RTX, PyTorch/ONNX parity, memory fallback, switching/cancellation, slider/export, persistence and dark/light/narrow layout checks.

## 0.2.123 - 2026-09-16

- Added an AI upscale factor slider to the bottom status bar while inline super-resolution is active, with supported steps from 1x to 3x and a visible numeric factor. The maximum respects the 32 MP output limit; 1x restores native pixels while keeping the slider available.
- Slider changes rebuild from original pixels after a short debounce, cancel obsolete inference and reject stale results. Previous pixels remain visible during processing, canceled changes restore the applied factor, and navigation/close discards pending work. Save is unavailable until the selected preview is ready.
- Focused slider arrow/Home/End keys no longer navigate images. Added native UI coverage for factor changes, pixel-for-pixel original-based reconstruction, cancellation, limits, export size and dark/light narrow-window footer layout.

## 0.2.121-0.2.122 - 2026-09-16

- Added the default image-only S shortcut and a labeled top-row AI upscale button. Both automatically choose a supported scale for small images relative to the current frame and Windows display scaling, up to 3x and the existing 8/32 MP limits. Images already large enough are left unchanged.
- Automatic results apply directly in the viewer, without a dialog or file writes; Enhance remains live. Press again to cancel processing or restore original pixels. Navigation, reload and close discard transient AI work/results, and stored zoom coordinates remain relative to the original image.
- Kept manual scale/comparison/export under Configure > Image tools > Super-resolution. Existing custom S assignments take precedence; the new action appears in the keyboard map, Help and shortcut editor.
- Toolbar commands now wrap when necessary so the labeled button and other commands remain accessible in narrow windows.
- Added automatic apply/restore, Save As, cancel/close, navigation, resize, session-coordinate and shortcut migration checks, alongside the existing real-model and responsive toolbar tests.

## 0.2.116-0.2.120 - 2026-09-16

- Added local AI upscale from the toolbar and Configure > Image tools, with 1x original and 1.25x, 1.4x, 1.6x, 1.8x, 2x, 2.5x and 3x previews. Scale changes always rebuild from the original pixels; the selected factor persists in sessions and backups.
- Bundled a checksum-verified ONNX sub-pixel CNN model. Reflected-edge, overlapping tiles reconstruct luminance, with AI subject/background masks controlling detail strength and conservative skin-color protection. Source chroma and alpha are retained before the user's adjustments.
- Includes current Quick Enhance, all ten manual Enhance values and rotation once, at output resolution. Added original/result comparison, fit/actual-pixel viewing, progress, cancellation and Save As to a separate file; saved results open in a tab without reapplying baked adjustments.
- Uses the configured DirectML GPU and AI soft-memory limit, with between-tile DXGI pressure checks and CPU fallback. Source/output limits are 8/32 megapixels; color blending is multithreaded. No cloud uploads or runtime model downloads.
- Initial model is a conservative 3x luminance upscaler, area-filtered for fractional scales, not generative face restoration. Detail and subject/skin estimates can be wrong; no guarantee of recovering lost detail. Output follows the existing 8-bit sRGB export pipeline.
- Added real CPU/RTX parity, fractional-size, tile-join, alpha/chroma, skin/region weighting, model integrity, cancellation/close, adjustment/export, saved-result, session/backup and dark/light/narrow-toolbar checks. The compact toolbar entry has an AI upscale tooltip; Configure retains its full label.

## 0.2.115 - 2026-09-16

- Moved wheel-preview strips into a separate floating canvas above the image. Left/right strips no longer participate in workspace size measurement; image fit, zoom and pan remain independent of strip visibility, position and thumbnail size.
- Kept transparent backgrounds, click/wheel/hover behavior, edge placement and clearance for other overlays. The layer passes input through outside the thumbnails.
- Added regression checks for unchanged image frame, zoom, pan and rendered pixels in fit/zoom modes, all positions and both size extremes, plus side-overlay screenshots and narrow/compact/fullscreen layout coverage.

## 0.2.113-0.2.114 - 2026-09-16

- Added a visual keyboard to Configure > Shortcuts, using the active Windows input layout for language-specific key positions, punctuation and layout name. It updates on input-language changes and page activation without changing OS settings.
- Key tooltips show current bindings, modifiers, browser/image scopes and action descriptions. Assigned keys are highlighted; double-click or Enter/Space opens a key editor with recording, Save, Remove and conflict protection. Multiple bindings on the same key remain separately editable.
- Added Reset all keys with confirmation, retained the command-based editor, and synchronized visual changes with Help, sessions and backup restoration. Gesture labels now reflect the active layout instead of assuming US punctuation.
- Added isolated native-layout, tooltip/scope, double-click, cancellation/conflict, add/remove/reset, persistence and dark/light/narrow-layout checks. The diagram represents logical Windows keys, not physical keyboard/Fn/IME hardware geometry.
- Build 0.2.114 uses familiar arrow-cluster/numpad key shapes, omits untranslated extra scan-code placeholders, and refreshes an open key editor when the Windows layout changes.

## 0.2.112 - 2026-09-16

- Removed the wheel-preview carousel's visible background box, outer border and individual thumbnail backplates. Kept the selected-image outline, center shadow and graduated edge fading; added a small counter text shadow for readability over photos.
- Reduced thumbnail gaps from 6 to 2 px and removed inner image padding in all three strip positions. Updated fitting calculations while preserving size preferences, hover/click/wheel behavior and overlay separation.
- Added transparent-pixel, compact-spacing and interaction checks, plus loaded-thumbnail visual checks in light/compact mode.

## 0.2.111 - 2026-09-16

- Numbered VRAM readouts GPU 0, GPU 1, etc., matching the viewer's DXGI GPU selector and detailed memory readout.
- Highlighted dedicated-memory usage in bold cyan and shared-RAM usage in bold amber, with muted capacities and labels. Added theme-aware colors, tabular digits, accessible full-value names and explanatory tooltips; unavailable counters remain visibly unavailable and muted.
- Kept stable responsive adapter slots and added checks for numbering, changed counters, unavailable values, dark/light contrast and narrow-window layout.

## 0.2.110 - 2026-09-16

- Added Preview size slider to the bottom status bar in image mode: 96-256 px in 16 px steps, default 160 px, independent of browser thumbnails. Live layout updates use debounced, size-specific DPI-aware cache loads.
- Nearby wheel previews now form a tapered carousel: the current image is largest, fully opaque and shadowed; three neighbors on each side become progressively smaller and more translucent. All seven fit together at bottom center, left center or right center without covering other overlays.
- Preview size persists in automatic/named sessions and backups, validates on import, and resets to the new default. Slider arrow keys adjust size without advancing the photo; mouse release returns focus to image navigation.
- Added regression checks for fade/size symmetry, cache resolution, slider input, layout at all positions and window sizes, light/compact/fullscreen modes, persistence, backup validation and reset.

## 0.2.105-0.2.109 - 2026-09-16

- Added explicit AI enhancement GPU selection with stable saved hardware identities. Auto prefers the discrete adapter; DirectML uses its real DXGI index instead of assuming GPU 0. Missing adapters, device failures and insufficient DXGI/admission headroom fall back to CPU.
- Added real integrated-GPU thumbnail area resizing through a separate low-priority D3D11 compute device, with alpha-aware filtering and readback into the persistent cache. CPU decoding/indexing/cache encoding remain CPU tasks. Busy GPU workers fall back to CPU instead of blocking the worker pool.
- Performance now offers Auto/CPU/Integrated GPU thumbnail modes, three-sample end-to-end Auto comparisons, Recheck performance, an AI soft admission limit and a thumbnail texture working-set limit. Settings persist in sessions/backups and reset to defaults. Limits do not claim to cap WPF/ONNX/driver allocations.
- Toolbar and Configure show separate RTX/dGPU and iGPU dedicated/shared memory readings. iGPU detection uses the reported unified-memory architecture. Shared RAM is not counted as extra VRAM; missing readings stay unavailable.
- Added real GPU pixel/alpha/readback checks on RTX 5090 and AMD Radeon integrated graphics, selected-device DirectML inference, concurrent iGPU resize/RTX inference, memory limits, cache reuse, Auto selection, missing-device fallback, per-adapter telemetry, responsive layouts and settings/backup tests.
- Build 0.2.107 prevents deferred configuration cleanup from erasing newly gathered benchmark samples, excludes canceled comparisons from timings, keeps known adapters visible on telemetry failure and rejects stale status updates after changing mode.
- Build 0.2.108 keeps Enhance usable with the extra GPU status row in short windows by fitting readable side-by-side overlay columns where possible and reserving enough control height when stacking.
- Build 0.2.109 gives each adapter readout a stable responsive slot so changing usage values do not move neighboring readouts.

## 0.2.101-0.2.104 - 2026-09-16

- Added Configure > Reset viewer with explicit confirmation, optional thumbnail-cache clearing, and recovery backup before removing settings. Resets tabs/pins, named and inactive-window sessions, favorites/aliases/expanded trees, history, shortcuts, appearance, overlays, caching preferences and placement without changing photos. Other active instances block profile maintenance.
- Thumbnail cache stays by default; Performance > Clear cache clears all sizes independently. Cache maintenance coordinates with decoding/writing and rejects linked or out-of-root paths. Existing recovery/model/photo backups are retained.
- Wheel navigation now opens a translucent seven-image preview strip: three previous, highlighted current and three next images, wrapping in the selected sort/filter order. Click to navigate, hover to retain, otherwise auto-hide after two seconds. Positions: bottom center (default), left center or right center; persisted in sessions/backups. Right-button wheel zoom stays unchanged.
- Added generated-fixture reset/recovery/cache/multiple-instance and preview navigation/layout/persistence regressions. Build 0.2.101 caught a C# 5 async compatibility issue during compilation; corrected for 0.2.102.
- Build 0.2.103 reserves a temporary edge band for previews so small windows retain usable thumbnails alongside Enhance, Info and pan/slideshow controls.
- Build 0.2.104 verifies recovery by importing the reset backup, checks reset cancellation/defaults and concurrent cache writers, validates maintenance lock paths, and handles canceled preview dispatch during shutdown.

## 0.2.95-0.2.100 - 2026-09-16

- E now toggles the manual Enhance panel in image mode. Opening by keyboard focuses the first slider after layout; Left/Right adjust focused sliders by one without navigating images. Existing custom E assignments take precedence, and Enhance remains rebindable.
- Folder thumbnails now offer Rename, Move and Delete alongside Copy/Cut/Paste. F2 and toolbar Rename handle one selected file or folder; Move and Recycle support mixed multi-selection. Folder contents are included, collisions never overwrite, recycling is confirmed and runs in the background, and drive roots/junctions are protected.
- Folder rename/move updates descendant open tabs, selected paths, favorites (preserving aliases/descriptions), forward history and expanded tree paths. Recycling retains affected tabs at the nearest surviving parent and removes unavailable favorite shortcuts.
- Added a search-history dropdown to toolbar and Advanced search: five distinct recent queries, newest first, recall and Clear history. Enter or leaving an edited search field records the query; clearing history leaves the current filter unchanged. History is shared within a window and persists in automatic/named sessions and JSON backups.
- Added focused slider-key tests, shortcut migration, search-history lifecycle/layout/backup tests and generated-fixture folder rename/move/recycle safety checks. Build 0.2.96 settles Enhance focus after layout; build 0.2.97 exercises folder selection through the normal selection notification path.
- Build 0.2.98 records pending searches before tab/view transitions and passes selected paths as one array in the mixed-recycle regression.
- Build 0.2.99 gives the history dropdown compact theme-aware rows without unused icon gutters.
- Build 0.2.100 removes the native menu/separator gutter and indexes deleted paths so large mixed selections do not require quadratic browser-state cleanup; adds a 100k-path regression.

## 0.2.93-0.2.94 - 2026-09-16

- Fixed arrow-key navigation stopping after a rotated image is saved. Rotation saves now return keyboard focus to the image after re-enabling the window, including automatic bracket-key saves and saves from Configure.
- Enhance Save / Save As also return focus after their progress window closes, including failure or cancellation. Completing a background save does not activate the viewer over another window; deliberately focused sliders still retain their native arrow-key controls.
- Build 0.2.93 reproduced missing keyboard focus after automatic rotation saving. Added focused-input regressions for next/previous navigation without clicking, explicit rotation saving, Enhance rotation saving, failure and cancellation.

## 0.2.92 - 2026-09-16

- Added a bottom image status bar showing the full path, file type, filename, modified date/time, original resolution, bit depth and file size, independently of the Info overlay and metadata panel. Long paths/names have full-text hover tooltips; detail fields wrap in narrower windows.
- Reuses loaded pixels and revision-checked metadata without extra decoding. Footer details follow navigation, file changes/renames and saves, and clear in browser/loading/error states.
- Name sorting now uses natural numeric order (xxxx1, xxxx2, xxxx11), as does folder ordering and the name tie-break for other metadata sorts. Number in name remains compatible. Added 100k unpadded-name, descending, multiple-number, metadata/footer lifecycle and layout regressions.

## 0.2.88-0.2.91 - 2026-09-16

- Fixed Ask before overwriting becoming unchecked while Enhance Save still asked for confirmation. The setting now follows Checked/Unchecked/Indeterminate changes immediately instead of depending on a separate Click event. Backups and file-safety checks are unchanged.
- Added a regression for changing Ask before overwriting through WPF's native accessibility toggle, including immediate Save/Save As policy, status refresh, original backup and restart checks.
- Builds 0.2.89-0.2.90 add the .NET Framework accessibility references to the test harness and correct their compiler argument grouping.
- Build 0.2.90 reproduced the stale confirmation policy; build 0.2.91 fixes the state-change handler and removes synthetic Click events from preference tests.

## 0.2.87 - 2026-09-12

- Added Configure > Image tools > Enhance saving > Ask before overwriting. Disable it to skip Enhance Save and Save As replacement confirmations; automatic backups, changed-file/read-only checks and Windows permissions remain in effect. Save As no longer asks twice when choosing the current file.
- The per-window choice persists in automatic/named sessions and JSON backups. Older sessions default to asking. Updated Help and added save-button, preference, restore and configuration-layout regressions.

## 0.2.82-0.2.86 - 2026-09-11

- Enhance now includes Save (confirmed overwrite with an original backup) and Save As (another image tab). Writes the full-resolution visible adjustment stack and rotation, independent of zoom/pan and overlays, using guarded rendering tiles on a cancelable worker.
- Staged output verification, revision/content checks, shared file gates and atomic replacement protect existing files. Save As supports PNG/JPEG/WebP/AVIF/JPEG XL; overwrite uses the original supported encoder, with animation/multi-image and read-only protection. Output is 8-bit sRGB; common EXIF/IPTC are retained where available, and stale orientation/dimensions/thumbnails are normalized.
- Successful saves refresh image/catalog state and reset baked adjustments, preventing Quick Enhance from being applied twice after save, session restore or backup import. Save As retains the original tab's unsaved values. Added rendered-pixel/format, backup, stale/read-only/cancel/conflict, animation and UI lifecycle checks.
- Build 0.2.83 checks tile joins explicitly while allowing normal sub-level 8-bit WPF shader sampling differences between render target sizes.
- Build 0.2.84 adds Advanced subject-mask tile checks, transparent PNG/white-background JPEG checks, neutral pixel preservation, Save As destination backups and UI recovery after a failed save.
- Build 0.2.85 makes Advanced Enhance effects cloneable for background saving and per-tile mask mapping.
- Build 0.2.86 isolates alpha encoding round-trip checks from render target sampling differences.

## 0.2.77-0.2.81 - 2026-09-11

- Added an Enhance toolbar button and translucent lower-left manual controls for vibrance, exposure, brilliance, contrast, brightness, black point, saturation, sharpness, highlights and shadows. Numeric values are embedded in slider handles; each row resets independently, with Reset all and Close in the header.
- Live non-destructive adjustments use a viewport-bounded shader after optional Quick Enhance, without re-decoding images or changing source files. Values default to 0; Sharpness is 0-100, others -100 to +100. Exposure covers +/-3 stops; vibrance includes muted-color and warm-skin protection.
- Enhance stacks above Info without moving it, scrolls in constrained frames, protects slider keyboard input and suspends slideshow advancement during adjustment focus. Visibility is saved per window; independent tab values survive duplication, automatic/named sessions and validated JSON backups. A different image in the same tab starts neutral.
- Build 0.2.78 reduces shader instructions for the existing WPF shader-model-2 rendering path and adds focused adjustment regressions.
- Build 0.2.79 completes shader-model-2 instruction reduction while retaining bounded output and alpha-aware sharpening.
- Build 0.2.80 extends verification to native thumb dragging, transparent pixels, animated frames, fullscreen placement and shortcut rebinding.
- Build 0.2.81 reads premultiplied render pixels directly in the transparency regression, without the shared helper's straight-alpha conversion.

## 0.2.73-0.2.76 - 2026-09-11

- Added a bottom-right image pan navigator when either displayed dimension exceeds the frame. Drag the outlined visible region or click another area to pan, with rotated geometry, edge clamping and centered smaller axes.
- P and Configure > Appearance > Pan navigator toggle the window-wide setting, enabled by default and saved in automatic/named sessions and JSON backups. Existing custom P assignments are preserved; the new command is rebindable and documented in Help.
- Reuses loaded bitmap pixels, including active GIF frames, without extra decoding or cache entries. The navigator preview remains unenhanced. Fit, loading, missing images and browser mode hide it; changing geometry, losing capture/focus or closing releases its drag.
- Navigator and slideshow controls stack without overlap; narrow views also lift metadata above them. Dragging suspends slideshow advancement until release and preserves manual pause. Added geometry, pixels, input, layout, lifecycle and persistence regressions.
- Build 0.2.74 adapts navigator height to leave room for complete metadata rows in short/narrow frames; metadata sits beside the navigator when space allows.
- Build 0.2.75 makes fullscreen checks independent of monitor size and checks the optional right-side metadata panel with the navigator.
- Build 0.2.76 corrects the regression's browser-to-image reopening step before checking session restore; viewer behavior is unchanged.

## 0.2.72 - 2026-09-10

- The default toolbar background is now Crosshatch with the Teal preset (#6ABFB5), 100% intensity and the existing right-edge fade. New windows without saved background preferences and Reset background use the same defaults.
- Saved background choices remain unchanged, including explicit None and zero intensity. Dark/light rendering and high-contrast suppression are preserved. Updated Help, README and default/reset/session/backup regression checks.

## 0.2.71 - 2026-09-10

- Inactive tabs keep their shared narrowest-title width; the active image/folder tab uses its own normal measured width (96-280 DIP), including pinned tabs and overflowing rows. Available space is calculated after reserving the active width. Activation/resize reveals the active header; reorder keeps its existing scroll behavior.
- Search and Advanced search move to the toolbar's right edge. At narrower widths, both move to a right-aligned second row without overlapping file/viewer commands. Keyboard focus, filtering, compact mode and background themes are preserved.
- Updated Help and README, with focused coverage for active/inactive sizing, pinning, reorder, overflow, restore and wide/narrow dark/light toolbar layout.

## 0.2.67-0.2.70 - 2026-09-10

- Windowed slideshows now keep translucent controls at the image area's bottom-right while playing or paused: previous, pause/resume, next, shuffle, stop, position/count and editable seconds per image. Manual skips preserve pause, interval editing suspends the timer, and Stop keeps the image open. Narrow layouts move the metadata overlay above playback controls.
- Configure > Shortcuts adds per-window keyboard rebinding for viewer/browser commands, multiple aliases, removal, per-command/all-default reset, context-aware conflict detection and reserved-key protection. Escape always remains available to cancel/exit. Default F5 starts/stops slideshow; pause/resume can be assigned a key.
- Custom keys replace their old assignments, respect text/native-control input, preserve Ctrl/Shift thumbnail selection and survive automatic/named sessions and validated JSON backups. Help lists current assignments and button tooltips resolve current shortcuts. Older sessions use defaults; playback never auto-starts on restore.
- Added focused sequence, overlay, timer/input, dark/light/narrow/compact layout, shortcut capture/routing/conflict, isolation and persistence checks.
- Build 0.2.68 tightens text-entry guards, refreshes configuration hints for remapped keys, avoids rebuilding unchanged playback icons and checks current Help bindings as table cells.
- Build 0.2.69 adds readable names for punctuation/paging keys and updates the metadata-toggle test for structured shortcut tooltips.
- Build 0.2.70 preserves dedicated Browser Back/Forward key navigation while a text field has focus; normal text/editing keys remain protected.

## 0.2.66 - 2026-09-10

- Equal-width tabs now use the narrowest measured open image/folder title instead of the widest. Wider titles no longer enlarge every tab; opening, closing or renaming the narrowest tab recalculates the shared width.
- Preserves the 96-280 DIP bounds, available-space fitting, horizontal overflow, pin/close controls, long-title ellipsis, hover previews and fixed Browser/+ commands. Restored tabs use the same sizing rule.
- Updated Help and regression coverage for narrowest-title sizing, lifecycle, mixed folder/image tabs, overflow and session restore.

## 0.2.64-0.2.65 - 2026-09-10

- Configure > Backgrounds adds a live preview, original Color fade/Contours/Facets/Ribbons/Crosshatch toolbar backgrounds, six color presets plus a custom RGB picker, three fading directions and an intensity slider. None/Reset retains the original plain toolbar.
- Subtle line art fades across the top toolbar/status band, follows dark/light mode, is suppressed in high contrast and hides with compact mode. Passive, frozen, bounded geometry preserves controls, image rendering, thumbnail caching and native window chrome without animation or background jobs.
- All four background preferences survive automatic/named sessions and validated JSON backups, independently per window. Older sessions default to no decoration. Help/build instructions describe the scope and controls.
- Focused pattern/pixel/contrast, preview/input, layout/theme, settings/reset, invalid-backup and persistence tests, with dark/light/narrow/ultrawide screenshots.
- Build 0.2.65 updates the test's text measurement to the current DPI-aware overload and captures a toolbar-only preview; background behavior is unchanged.

## 0.2.62-0.2.63 - 2026-09-10

- I toggles a persistent, translucent bottom-left image metadata overlay with six rows: automatically scaled file size, file type, filename, local modified date/time, pixel resolution and explicitly labeled bit depth.
- Passive overlay stays anchored during zoom/pan/rotation, works in dark/light/compact/fullscreen layouts and wraps long filenames. It remains enabled across tabs/images, hides in thumbnail mode and saves its preference with automatic/named sessions and JSON backups. Configure > Appearance offers the same toggle; the separate right-side metadata panel is unchanged.
- Metadata refreshes asynchronously with image navigation and filesystem changes. File-revision checks prevent late results from describing a replaced image. JPEG/PNG use native WIC bits/pixel; modern formats retain header depth in bits/channel before display conversion, with GIF palette depth labeled separately.
- Focused keyboard, live-file update, metadata precision, layout, lifecycle, session/backup and input pass-through regression checks.
- Build 0.2.63 resolves a metadata type-name collision in the regression harness; 0.2.62 compiled the viewer but not the tests.

## 0.2.60-0.2.61 - 2026-09-10

### Added
- Translucent zoom-factor indicator in the image area's top-right corner, visible for one second after a zoom change above/below 1x. Subsequent zoom changes restart the timer; returning to 1x hides it immediately.
- Covers right-button wheel zoom, keyboard zoom, Fit and restored image views. The overlay does not affect layout, image transforms, focus or mouse input; it also works in compact/fullscreen viewing. Panning and animation frames do not restart it. Loading another image, returning to thumbnails and closing the window clear the old indicator.
- Focused timing, zoom-input, lifecycle, placement, mouse-pass-through and dark/light/fullscreen rendering checks.
- Build 0.2.61 corrects the test's saved 1x-tab setup and lets updated overlay text render before taking the 2x screenshot.

## 0.2.51-0.2.59 - 2026-09-09

### Added
- Compact Windows-native command icons, retained labels for less-obvious actions, descriptive hover tips and actual keyboard shortcuts, including rotation controls. Accessibility names and disabled-button tips aid discovery.
- Structured, selectable native Help: shortcut tables, section headings, readable lists, formatted release notes and per-build history, plus a supported-formats page. Dark/light themes remain live.
- Bundled Magick.NET 14.16.0 x64 decoders for WebP, AVIF, JPEG XL and HEIC/HEIF; JPEG/PNG retain Windows decoding. GIF is cataloged, thumbnailed and animated in the active image tab. Modern-format EXIF and optional ICC conversion are available.
- Bounded parallel codec work, raster-only security policy, detached display pixels, cancellation checks and active-tab GIF lifecycle. Playback honors timing/disposal/loop counts and preserves pan/zoom/rotation.
- Build 0.2.52 fixes initial codec API/compiler integration and adds real-format, animation, session and Help/icon regression coverage.
- Build 0.2.53 bundles the codec's required configuration resources alongside the restrictive raster policy.
- Build 0.2.54 generates test colors through real PNG data, keeping pseudo-image coders disabled; adds discoverable new/close-tab controls.
- Build 0.2.55 avoids the native AVIF writer's lossless-fixture limitation, preserves GIF logical canvas geometry, and clarifies tree/minimap/window-height tooltips and decoder errors.
- Build 0.2.56 requests all animation frames explicitly and tests offset GIF logical-canvas display and metadata.
- Build 0.2.57 fixes native Help table column sizing after screenshot inspection, adds readable-column geometry checks at narrow/wide sizes and uses an open-folder icon for browser/backup actions.
- Build 0.2.58 aligns single-image action tips with existing capabilities, documents bundled codec files and sampled-frame enhancement, and expands GIF disposal/finite-loop and modern-format metadata/alpha checks.
- Build 0.2.59 loads profiles for codecs that omit EXIF in header-only reads, verified with a real WebP camera-metadata fixture.

### Limitations
- Modern formats display at 8-bit precision; no HDR output or RAW. GIF animation is limited to 512 frames and one quarter of the decoded cache budget (32-256 MB), separately allocated; oversized animations show their first frame. Other animated formats show their first frame. Native decode cannot be interrupted mid-call. Modern image decode is CPU-based, followed by existing GPU rendering. Rotation writing remains JPEG/PNG only.

## 0.2.46-0.2.50 - 2026-09-09

### Added
- Configure > Image tools: cancellable current-folder thumbnail rebuild with bounded parallel work, current-size/minimap/tab cache regeneration, bounded immediate-child-folder preview retries, progress and failure reporting. Visible thumbnails/minimap/hover previews refresh without losing browser selection or scroll.
- Automatic fallback when a codec rejects scaled thumbnail decoding but can decode the full image. At most two full-resolution fallback jobs run at once; returned thumbnails are detached and bounded on their long edge.
- Rotate left/right 90-degree buttons, explicit Save rotation and original-backup access. View-only remains the default. An explicitly enabled automatic-save checkbox applies to buttons and bracket shortcuts and persists in sessions/backups.
- Single-frame JPEG/PNG rotation writing with verified staging output, original-file backups, read-only/link/concurrent-change protection and serialization across viewer windows. PNG pixels/alpha are lossless; JPEG is re-encoded at quality 95. WIC metadata/color contexts are carried forward; JPEG orientation/dimensions and stale EXIF thumbnails are normalized.
- Decoded-cache file revisions reject stale entries and in-flight results after a file replacement; watched active images reload after external changes. Matching tabs reset view rotations after a successful save.
- Focused image-tool regressions and dark/light/small settings screenshots. Build 0.2.46 compiled the app; 0.2.47 corrects a metadata type name in the test build.
- Build 0.2.48 explicitly dispatches background completion to the UI thread and also shows rotation save/failure status in the main footer when Configure is closed.
- Build 0.2.49 refuses mismatched JPEG/PNG file containers and adds animation-marker, cache-write-failure and bounded-child-preview checks; inaccessible child folders report a failure without aborting the rebuild.
- The overwritten-portrait preview regression now checks the new long-edge bound and changed aspect ratio instead of expecting an oversized portrait thumbnail.
- Build 0.2.50 keeps routine view-only rotation status inside Configure so it does not add a footer row or change saved pan geometry. Explicit saving/error feedback remains visible in the main viewer.

### Limitations
- Saving other formats and animated/multi-frame images is not implemented. JPEG saving is not a lossless coefficient transform and vendor-specific metadata preservation is not guaranteed. Quick Enhance remains display-only. Backups consume space and are not automatically deleted. Windows access restrictions are never bypassed.

## 0.2.40-0.2.45 - 2026-09-09

### Added
- Vertical-arrow toolbar toggle and Configure > Appearance control fill the current monitor's available height, preserving width/horizontal position; a second press restores previous height/top. Uses Windows monitor work area for shown/auto-hidden taskbars and refreshes for display, work-area and DPI changes. Does not change taskbar settings. Normal bounds remain session-persistent.
- Drag/drop tab ordering with insertion markers, edge scrolling, click/drag threshold and Escape cancellation. Pinned tabs may occupy any position while retaining chosen color and close protection. Duplicates appear next to their source; Browser and + stay fixed. Automatic/named sessions and backups preserve order.
- Drag/drop favorite shortcut ordering, retaining alias/description, selection and expanded child branches without moving directories. Shared preference merge preserves another instance's order on idle autosave and combines deliberate reorders with concurrent additions/removals and alias edits.
- Slideshow start/stop moved into Configure > Images alongside timing and shuffle. Starting closes Configure to expose windowed playback; reopening allows Stop, and Escape in Configure stops playback.
- Focused regressions for insertion geometry, cancellation, private ordering data, edge scrolling, favorite preference merges, restart/backup order, live height/restore/fullscreen behavior and Configure slideshow actions.
- Build 0.2.41 corrects the regression harness's creation of a WPF native drag-cancellation event; 0.2.40 compiled the app but did not finish the test build.
- Builds 0.2.42-0.2.43 improve capture diagnostics and guard synchronous pointer-event re-entry while beginning a drag candidate.
- Builds 0.2.44-0.2.45 verify routed drops and actual insertion-marker pixels and include ordering/height smoke-check instructions; marker captures account for the adorner layer's offset.

### Limitations
- Reordering is within one window/list; it does not move tabs between windows. Height mode's pre-toggle restore anchor is temporary, although resulting normal window bounds are saved. Native physical drag gestures and changing the real taskbar setting require manual smoke checks; the regression harness exercises the application drag callbacks and live work-area geometry without moving the user's pointer or changing Windows settings.

## 0.2.34-0.2.39 - 2026-09-09

### Added
- Configure button and Ctrl+, open one modeless settings window per viewer. Appearance, Images and Performance collect theme, panels, minimap, thumbnail size/shape, enhancement mode, ICC, slideshow options and cache budget. Search/sort and playback/enhancement commands stay in the workspace.
- Minimap divider chevron hides/shows the map, preserves its current region and remains available while the map is collapsed.
- Consistent user-selected pinned-tab color, preset swatches and a native custom color picker. Pin icons and names automatically choose contrasting text. Color survives automatic/named sessions and validated JSON backups.
- Equal-width image/folder tabs measured from the widest title, shrinking to fit available space within a 96-280 DIP range. Long titles are elided with existing hover previews; horizontal overflow remains available for many tabs. The Browser shortcut and + command keep fixed sizes.
- Background-polled system-wide dedicated GPU memory usage from Windows adapter counters, plus DXGI adapter capacity and per-adapter details. Usage is distinct from the per-window decoded cache budget; unavailable readings are labeled rather than fabricated. No process-counter summation or claim of explicit VRAM eviction.
- Regression checks for configuration controls, live changes, pending-edit save on close, color/backup validation, pin contrast, shared tab sizing, minimap arrows and multi-adapter telemetry aggregation/failure/throttling.
- Builds 0.2.35-0.2.38 refine DPI-aware measurement and regression diagnostics/resource-lifecycle checks. Build 0.2.39 aligns settings controls, uses a minimap checkbox and captures the dialog's actual theme background.

### Limitations
- The cache slider still limits decoded bitmaps, not WPF/ONNX allocations. Explicit DXGI pressure eviction and unified VRAM accounting remain unimplemented. Telemetry depends on Windows GPU counters/drivers; localized or unavailable counters report unavailable. Shared system RAM is not included in VRAM.

## 0.2.32-0.2.33 - 2026-09-09

### Added
- Advanced Quick Enhance automatically estimates subject softness and adjusts subject sharpening separately from the background. No new toggle or session setting; it follows the existing window-wide Advanced mode.
- Bounded native-resolution analysis: at most forty 128x128 crops, including mask-guided samples for small/off-center subjects, analyzed with up to eight workers. Sharp backgrounds cannot drive the subject's focus estimate; flat areas and silhouette boundaries do not count as subject edge evidence.
- Region-specific noise thresholds, evidence/confidence gating, restrained background sharpening and the existing skin-color/alpha protection and luminance halo limit. No confident mask retains the Basic sharpening fallback.
- GPU regional sharpening via the existing subject mask and tone lookup texture, plus tooltip diagnostics. Originals, EXIF, thumbnails, sessions and on/off restoration remain non-destructive.
- Regression coverage for sharp/soft/noisy subjects, native-scale sampling, flat/tiny/missing-mask cases, deterministic analysis, cancellation, actual shader edge contrast, skin hue, alpha and halo bounds.

### Limitations
- This is a conservative edge/noise heuristic, not a calibrated focus score or deblurring/reconstruction model. It can improve perceived edge contrast but cannot restore detail lost to severe defocus, motion blur or clipping. Native patches are bounded samples, not a per-pixel full-resolution blur map.

## 0.2.28-0.2.31 - 2026-09-09

### Added
- Experimental thumbnail minimap: T in browser mode, or the minimap icon beside Sort, replaces the narrow right scrollbar with a wider miniature image rail. Drag its highlighted viewport band to scrub through the folder; click a miniature to scroll to its row without opening it. The mouse wheel still scrolls the main browser.
- Moving, virtualized miniature document for large folders: every item keeps its sorted/filtered position, without sampling the directory or squeezing thousands of images into unreadable pixels. Directory markers maintain alignment with folder tiles.
- Persistent 64-pixel thumbnails, no more than four concurrent minimap loads and 768 retained preview slots. Off-screen loads are canceled; hidden maps release previews. Sort/filter/folder/watcher changes invalidate stale miniatures.
- Responsive rail width, current-position preservation when toggling, dark/light styling, safe pointer capture/release, text/control-focus guards and saved window-wide visibility in automatic/named sessions and JSON backups. Older configurations start with the minimap off.
- Help shortcut/interaction reference and focused minimap geometry, pointer, rendering, lifecycle and large-folder tests.
- Build 0.2.29 separates the geometry test scope for compatibility with the Windows C# compiler.
- Build 0.2.30 normalizes the rail's right-column offset in isolated pixel captures; full-window screenshots also verify its actual placement.
- Build 0.2.31 verifies completed image previews after 100k-item scrubbing and excludes the colored viewport band from image-pixel assertions.

## 0.2.26-0.2.27 - 2026-09-09

### Added
- Help button and F1 window with current executable version, mode-specific keyboard/mouse shortcuts, individually selectable build notes/outcomes and release changelogs. Help text is embedded in the executable and supports dark/light themes.
- Advanced Search button beside the folder filter, plus Ctrl+Shift+F. Searches image and folder names recursively beneath the current folder in a separate, resizable results window; the existing per-tab filter is unchanged.
- Persistent per-root search snapshots, immediate cached queries, debounced background reconciliation with up to eight below-normal directory workers, parallel matching, cancellable work and live recursive FileSystemWatcher refresh.
- Virtualized result thumbnails with size/aspect sliders, metadata sort/direction, containing-folder captions, full-path tooltips, keyboard navigation and Open / Open containing folder commands.
- Build 0.2.27 refines version-dropdown labels and themed search/help rendering, coalesces watcher callbacks, handles moves across the excluded cache boundary, and adds live-scan cancellation/cached-query and junction-cycle checks.

### Safety and limits
- Index files use validated structured records and atomic, cross-instance-serialized replacement. Corrupt caches rebuild; canceled scans retain the previous completed cache. Missing results cannot open; inaccessible entries and links are counted. Nested junctions/symlinks and the app profile/cache are excluded.
- Initial cold scans enumerate the subtree before publishing results. Cached matches remain available during subsequent full background reconciliation; file-event bursts schedule a trailing refresh without repeatedly canceling scans. Search is filename substring matching, not EXIF/full-text/AI search. Scope is fixed when opened; no tab stacks or search-window session persistence.
- Indexes are limited to two million entries per root with an explicit error rather than silent truncation. No 100k-file disk benchmark or USN/Windows Search integration is claimed.

## 0.2.18-0.2.25 - 2026-09-09

### Added
- Basic/Advanced mode dropdown beside Quick Enhance. Advanced includes exposure and sharpening analysis, with separate subject/background shadow, highlight, exposure and vibrance adjustments. Mode and enablement persist window-wide across tabs, sessions and JSON backups.
- Local PPHumanSeg person and U-2-NetP foreground models using ONNX Runtime DirectML with CPU fallback. Bundled runtime/model files, checksums, licenses and a pinned dependency preparation script; no photo uploads or runtime model downloads.
- Edge-aware feathered subject mask, conservative skin-colored-area protection, hue-preserving tone changes and gamut compression. Low-confidence or unavailable detection falls back to gentler global adjustments and reports it in the tooltip.
- Advanced regression checks for actual inference, regional shader pixels, skin-hue preservation, alpha, deterministic parallel analysis, cancellation, corrupt/missing model fallback, mode focus and persistence.

### Changed
- The on/off button continues to restore the untouched original immediately. Advanced sessions are reused while enabled and released asynchronously on Off, Basic mode or window close. Superseded native jobs are serialized to limit overlapping GPU allocations.
- Runtime packages now include DLLs and Models/Licenses folders that must remain beside the EXE. Explicit DXGI/unified GPU memory budgeting remains a prototype limitation, including inference working memory.
- Builds 0.2.18-0.2.22 refine the Advanced effect to fit WPF's PS 2.0 shader limits. Build 0.2.23 adds regional/portrait tests; 0.2.24 hardens cancellation, model failure handling, mode-dropdown focus, runtime telemetry settings and documentation.
- Build 0.2.25 makes the regression return focus to the image after verifying native dropdown navigation, matching the viewer's focus guards.

## 0.2.14-0.2.17 - 2026-09-09

### Added
- Thumbnail-mode arrow navigation: Left/Right select adjacent tiles, Up/Down move by the current visual row, and Shift extends/contracts a range. Folders can be selected without entering them.
- Home/End select the first/last image thumbnail under the current sort/filter; Page Up/Page Down scroll the browser by one viewport while preserving selection.
- Distinct blue active-thumbnail highlight and emphasized filename in dark/light themes, separate from other selected items' teal highlight.
- Focused regression checks for thumbnail navigation, row edges, paging, input-focus guards, resizing/reflow, sort/filter, 100k-record virtualization and selection/session restore.
- Builds 0.2.15-0.2.17 refine viewport diagnostics and wait for debounced slider/search changes before testing reflow and synthetic virtualization fixtures.

### Fixed
- Returning from an image with Enter/Esc now focuses and reveals its thumbnail. Subsequent arrows stay in thumbnail view instead of opening the next image.
- Image-only next/previous shortcuts no longer activate images from thumbnail view. Folder-tree, dropdown, slider and text-field navigation retains native key handling.

## 0.2.12-0.2.13 - 2026-09-08 to 2026-09-09

### Added
- J-key and a chevron button to hide/show the folder tree separately from the toolbar. Hidden normal-mode trees retain a reveal arrow. Tree visibility and divider width are saved; compact mode can temporarily show the tree with J and restores the normal preference on exit.
- Sessions menu Export backup / Import backup commands using versioned JSON. Backups include current-window tabs/pins/order, viewer settings, favorites/aliases/expansion and named sessions. Tab stacks are deferred.
- Import validation and confirmation, a pre-import recovery snapshot, automatic removal of missing image tabs, and fallback to a surviving tab or home browser. Existing unrelated named sessions are retained.
- Regression coverage for tree controls, global enhancement, backup round trips/recovery, malformed or unsupported backups, missing paths, folder-only/all-missing tabs and size/numeric/path limits.

### Changed
- Quick Enhance is available in browser mode and stays enabled across all tabs and image navigation within the same window until explicitly toggled off or a saved configuration is restored. Each displayed image gets fresh analysis. Source files/thumbnails remain unchanged; one failed enhancement no longer disables the global preference.
- Automatic/named sessions and backups preserve the window-wide enhancement setting. Older per-tab sessions migrate from their active tab's setting.
- Build 0.2.13 gives the chevron rail a theme-matched background and explicitly keeps splitter resizing between the tree and workspace, with additional import-validation tests.

## 0.2.10-0.2.11 - 2026-09-08

### Added
- Home and End jump to the first/last image in the current folder's sorted and filtered sequence without replacing the tab.
- H toggles compact mode: hides the top toolbar/status area and left folder tree/splitter, retaining tabs and the main workspace. Normal mode restores the folder-pane width. Every new app instance starts in normal mode, regardless of how its previous session was closed.
- Focused regression coverage for endpoint navigation, wraparound, edge preloading, compact dark/light layouts, zoom/pan/fullscreen interaction, keyboard focus and normal-mode restart.

### Changed
- Next/previous image navigation wraps at both ends, including the wheel and keyboard/toolbar controls. Adaptive neighbor preloading now wraps between the same folder's first and last images. Single-image sequences avoid unnecessary reloads; empty or all-missing sequences leave the current view intact.
- Image navigation skips files deleted before the watcher refresh and waits for folder scans/sorts to finish. Home/End preserve text editing, and clicking an image returns keyboard focus to the viewer (refined in build 0.2.11).
- Compact mode preserves image centering and manual zoom, ends active dragging, and does not alter outer window placement. H ignores key repeat and text entry. Ctrl+F returns to normal mode before focusing the search field.

## 0.2.7-0.2.9 - 2026-09-08

### Added
- Pin tab / Unpin tab in image and browser tab context menus. Pinned tabs stay at the left, display a pin marker, and require unpinning before closing. Automatic and named sessions retain pin state and order. Duplicates are unpinned.
- Independent automatic sessions for concurrent app instances, with numbered window titles and reusable session slots. Existing main-session files remain compatible; new slots initially use the main saved workspace.
- Exclusive instance leases that Windows releases after normal or unexpected exits, plus an optional `ZONER_VIEWER_PROFILE` override for isolated profiles.
- Cross-process regression tests for simultaneous real app launches, normal exit, interrupted-instance recovery, shared storage, tab pins and session restore.

### Changed
- Shared favorites and expanded-tree preferences merge edits from each window instead of overwriting an entire stale snapshot. Alias changes and remote additions/removals survive unrelated autosaves.
- Named-session and preference writes use cross-process locks with atomic replacement. Thumbnail reads eagerly load from explicitly shared streams and release file handles even after corrupt-cache failures.
- Updated build/run documentation for multiple instances, per-instance memory budgets and shared-setting behavior. Build 0.2.8 corrects legacy-length test paths and adds child-process diagnostics; 0.2.9 improves theme screenshots and validates repaired cache files.

## 0.2 Series - 2026-09-08

### Added
- Favorite folder aliases (up to 200 characters) and descriptions (up to 2,000 characters), edited from the tree's Rename favorite menu. Descriptions appear below the alias and in a scrollable hover preview. Reset name returns to the real folder's label; no filesystem names change.
- Incrementing `0.2.BUILD` versions on every build invocation, with matching executable metadata and window title. Failed attempts reserve their version as well, so numbers are not reused.
- A dated build journal recording the version, result and build note. Release packages include this changelog, the journal and VERSION.txt.

### Changed
- Recycling the displayed image keeps the same tab and advances in the current browser order. At the end it selects the previous available image; when none remain it returns to the folder browser in the same tab.
- Other tabs showing a recycled image return to their browser instead of closing. Unrelated tabs and browser selections are retained.
- A delete confirmation stops slideshow playback so the image being confirmed cannot change underneath it.

### Compatibility
- Existing favorites migrate without changes to their paths or expansion keys. Aliases/descriptions are application-wide and survive session changes and restarts.
- Earlier releases were unversioned. New numbering starts at 0.2.1; BUILD_HISTORY.md tracks individual iterations.

## Earlier Unversioned Prototype

- Native dark/light three-pane viewer, image/browser tabs, sessions and window placement.
- Virtualized resizable thumbnails, folder mosaics, favorites, sorting, search and filesystem watchers.
- Zoom, pan, rotation, keyboard/mouse navigation, tab previews and non-destructive Quick Enhance.
- Multi-selection, Copy/Cut/Paste, copy-default Explorer drag/drop, conflict prompts and windowed timed/shuffled slideshows.
- Safer image stream handling and session-save retries. Explicit GPU residency, DXGI pressure monitoring and additional native codecs remain prototype integration points described in README.md.
