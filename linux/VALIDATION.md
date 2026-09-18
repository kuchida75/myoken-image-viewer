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

This record is a documentation-only follow-up to the tested source commit. A headless test is not a visual usability review and does not establish GNOME/NVIDIA compatibility or advanced-feature parity. There is no packaged Linux release yet.

## Still required before L001 desktop acceptance

Use a separate native-Linux checkout on the actual Ubuntu 24.04-or-newer GNOME system. Follow `linux/README.md` and run `bash linux/check.sh`, then `bash linux/run.sh`.

Manually verify folder navigation, thumbnail layout, opening/closing multiple image tabs, folder/tab/active-tab restoration after a full restart, rapid navigation while decoding, unreadable/corrupt files, fractional scaling, fullscreen, and GNOME Wayland through XWayland with the actual NVIDIA driver. Do not load or write Windows profiles. Native Wayland is a later opt-in evaluation, not part of the verified configuration.

L001 status: source implementation and initial CI passed; real GNOME desktop acceptance pending. Do not mark advanced codecs, GPU decoding, large-folder performance, full-resolution zoom/pan, metadata parity or filesystem integration as implemented.
