# Myoken Ubuntu/GNOME

Independent Linux development track in the existing Myoken repository.

- Branch: `linux/ubuntu-gnome`.
- Windows baseline: **v0.2.167**, commit `ea785b3770383278f1a3bf67762d35014e8bda26`.
- Target: Ubuntu 24.04 LTS and newer, GNOME, initially x64.
- Linux UI: separate C# / .NET 10 / Avalonia 12.1.2 application.
- Portable core: .NET Standard 2.0, so a future Windows .NET Framework 4.8 integration does not require replacing WPF.

## Isolation contract

Do not edit `src/ZonerInspiredViewer`, Windows version/build journals, dependency manifests, or Windows build scripts for Linux milestones. Do not run `tools/build.ps1`: it reserves a Windows version number even when compilation fails. Do not replace the WPF interface or merge this branch to `main` without a separate review. New core code is **not yet referenced by Windows**. Natural ordering is portable, not a claim of exact `StrCmpLogicalW` equivalence.

Use a separate native-Linux checkout, not the Windows workspace, a shared writable profile, or a checkout on a Windows-mounted drive. Source creation through GitHub does not create a directory on your computer.

```bash
sudo apt-get update
sudo apt-get install -y git dotnet-sdk-10.0 libx11-6 libice6 libsm6 libfontconfig1 libxrandr2 libxi6 libxcursor1 libgl1 xwayland
mkdir -p "$HOME/Projects"
git clone --branch linux/ubuntu-gnome --single-branch https://github.com/kuchida75/myoken-image-viewer.git "$HOME/Projects/Myoken-Ubuntu-GNOME"
cd "$HOME/Projects/Myoken-Ubuntu-GNOME"
bash linux/check.sh
bash linux/run.sh
# An explicit starting folder is also supported:
bash linux/run.sh "$HOME/Pictures"
```

The clone command deliberately fails rather than reusing an existing nonempty destination. Run the viewer as your normal desktop user, never with sudo. Review your configured Ubuntu package sources before installing packages. First restore requires NuGet network access.

## Initial implementation: L001

The source provides folder navigation (path entry, folder picker, Home and Up), a paged thumbnail browser, natural filename ordering, one image per tab, tab closing, fit-to-window previews, and session restore of folder, all valid stored image tabs, and active tab. Only the selected image tab is decoded. Thumbnails are limited to 72 visible items and two simultaneous decode operations. Folder enumeration runs off the UI thread and can be cancelled. Session files are replaced atomically; a second instance cannot write the same session; malformed or newer-version sessions are not overwritten.

Initial formats are JPEG, PNG, WebP, BMP and the first frame of GIF where the bundled decoder accepts them. HEIC, AVIF, JXL, video, EXIF orientation/colour-management parity, full-resolution zoom/pan, GPU decoding, filesystem watching, disk caching, drag/drop and file operations are **not implemented**. Preview decoding is bounded to a requested 4096-pixel long edge; individual inputs above 120 million pixels are rejected. Paged thumbnails are an interim memory measure, not a virtualised 100,000-file performance claim.

Linux state is separate from every Windows profile:

```text
${XDG_STATE_HOME:-$HOME/.local/state}/myoken-linux/session.json
```

A corrupt/unreadable session or a session from a newer schema makes persistence read-only for that run; the status bar reports this. No Windows session import occurs. On first launch, the default folder is `~/Pictures` when present, otherwise home.

## GNOME and display backend

This is a GNOME-compatible desktop app, **not a GTK/libadwaita application**. The initial Avalonia desktop backend is X11; in a GNOME Wayland session it runs through XWayland. Do not advertise native Wayland validation: upstream's opt-in native Wayland backend remains experimental. Keep native Wayland evaluation as a separate milestone. Neither GPU acceleration parity nor NVIDIA-driver compatibility is established by a headless launch test.

## Testing and acceptance

`bash linux/check.sh` verifies Windows-baseline files are unchanged, builds the Linux project, and runs the independent console regression checks. The Linux-only GitHub Actions workflow also launches the UI under Xvfb with generated PNG fixtures and isolated state. A successful CI result does not replace GNOME testing.

The initial source was authored from a environment without a .NET SDK or an accessible GNOME desktop. Treat build/runtime status as **unverified until the corresponding checks actually succeed**. Do not call L001 accepted merely because the source or branch exists.

Manual L001 acceptance on Ubuntu 24.04 GNOME:

1. Browse nested folders, spaces and Japanese/Unicode filenames; distinguish `A.png` from `a.png`; check `image2` before `image10`.
2. Open several images in separate tabs, close one, select another, quit, relaunch, and verify folder, remaining tabs and active tab restore.
3. Change folders/pages rapidly while thumbnails load; exercise unreadable/missing files and a corrupt image without a crash.
4. Test GNOME Wayland/XWayland on the actual NVIDIA system, fractional scaling, fullscreen and a high-resolution monitor.
5. Compare `git diff ea785b3770383278f1a3bf67762d35014e8bda26 -- src/ZonerInspiredViewer version.json tools` and verify no Windows changes.

## Next milestones

- L001 acceptance: build and real GNOME browser/viewer/session testing; fix regressions before advancing.
- L002: virtualised folder tree/grid, incremental enumeration, byte-budgeted memory/disk caches, three-image preloading and measured 100k-folder behaviour.
- L003: metadata/orientation/colour management, HEIC/AVIF/JXL and decoder fallback tests.
- L004: filesystem notifications, rename/move/Trash, desktop integration, packaging and native Wayland evaluation.
- L005: selective Windows adoption of proven shared sorting/session/metadata/cache policies, each gated by existing Windows regressions. Keep Windows-specific WPF, DirectX/CUDA and filesystem integration separate.

## Reference documentation checked 18 September 2026

- https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install
- https://docs.avaloniaui.net/docs/supported-platforms
- https://docs.avaloniaui.net/docs/platform-specific-guides/linux
- https://www.nuget.org/packages/Avalonia.Desktop/12.1.2

See `linux/AGENTS.md` for the development handoff. Existing repository licensing and notices remain in force; Avalonia and SkiaSharp add their respective upstream dependency licences to the Linux dependency graph. Packaging must carry the resolved transitive notices before distribution.
