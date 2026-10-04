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
