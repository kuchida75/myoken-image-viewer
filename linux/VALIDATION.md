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
