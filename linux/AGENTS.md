# Myoken Ubuntu/GNOME development context

Read linux/README.md before Linux work. Continue independently from Windows.

## Non-negotiable boundaries

- Work on linux/ubuntu-gnome or a Linux feature branch in a separate Linux-filesystem checkout.
- Windows baseline is v0.2.167 at ea785b3770383278f1a3bf67762d35014e8bda26. Preserve WPF and every existing Windows source/build/version file.
- Never run Windows build/version-reservation scripts for Linux tasks; do not modify main, merge to main, retag releases or publish binaries without a separate request.
- Do not read/write Windows profiles. Use case-sensitive ordinal Linux path identity.
- Core targets netstandard2.0 for future .NET Framework 4.8 compatibility. Do not add Avalonia/WPF/SkiaSharp/Win32/DirectX/CUDA/platform-I/O dependencies to it.

## Current iteration

L002a adds a lazy FolderTree and a VirtualThumbnailBrowser pixel-scrolling canvas. It is not a new decoder or session migration. The tree itself is not virtualised, and directory file lists are still fully scanned/sorted off the UI thread. Off-screen/hidden thumbnail controls and bitmaps are disposed; the decoder's two-operation gate remains in force. Do not claim 100k-folder throughput from the synthetic repeated-path UI check.

Keep changes scoped to src/Myoken.Linux, portable policies when warranted, Linux tests/scripts/docs and the Linux CI workflow. Windows still does not reference Myoken.Core.

Run bash linux/check.sh, then bash linux/smoke.sh where Xvfb is available. Original core/session tests must keep passing. UI checks use generated fixtures and guarded, isolated state; do not bypass the isolation requirement. The second process must restore its folder from session rather than a command-line override. Manual GNOME acceptance is separate and still required for new UI changes.

Each handoff should report exact source revision, checks actually completed, files affected, limitations and the next concrete desktop acceptance step. Maintain linux/VALIDATION.md and linux/L002A.md without converting unverified behaviour into a passed test. Preserve the user's confirmed L001 tab-restore result while checking the new iteration separately.
