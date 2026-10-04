# Myoken Ubuntu/GNOME

Independent Linux development track in the existing Myoken repository.

- Development branch: `linux/ubuntu-gnome`; Linux feature branches may be used for CI before fast-forwarding this branch.
- Protected-by-workflow Windows baseline: **v0.2.167**, commit `ea785b3770383278f1a3bf67762d35014e8bda26`. This describes our isolation checks, not GitHub branch-protection settings.
- Target: Ubuntu 24.04 LTS and newer with GNOME, initially x64.
- Separate Linux UI: C# / .NET 10 / Avalonia 12.1.2. Linux app version: **0.1.0-alpha.2**, window label **L002a**.
- Portable core: .NET Standard 2.0. It is not yet referenced by the Windows .NET Framework 4.8/WPF application.

## Isolation contract

Do not edit `src/ZonerInspiredViewer`, Windows version/build journals, dependency manifests, or Windows build scripts for Linux milestones. Do not run `tools/build.ps1`: it reserves a Windows version even on compilation failure. Do not replace WPF, modify the default branch, merge to `main`, retag Windows releases or write Windows profiles. Natural ordering is portable, not a claim of exact `StrCmpLogicalW` equivalence.

Use a **separate checkout on the Linux filesystem**, never the Windows workspace or a shared writable profile. Native Ubuntu is the GNOME acceptance target; Ubuntu under WSL2/WSLg can be used for development, but is not GNOME/NVIDIA release validation. The current user workflow is native Ubuntu. GitHub source changes do not update a checkout on the user's computer.

## Build and run

Install prerequisites using the configured Ubuntu repositories, reviewing APT's proposal before confirming. No Ubuntu release upgrade, NVIDIA/CUDA package changes or Python environment changes are required by this source update.

```bash
sudo apt-get update
sudo apt-get install --no-install-recommends git dotnet-sdk-10.0 libx11-6 libice6 libsm6 libfontconfig1 libxrandr2 libxi6 libxcursor1 libgl1 xwayland
mkdir -p "$HOME/Projects"
git clone --branch linux/ubuntu-gnome --single-branch https://github.com/kuchida75/myoken-image-viewer.git "$HOME/Projects/Myoken-Ubuntu-GNOME"
cd "$HOME/Projects/Myoken-Ubuntu-GNOME"
bash linux/check.sh
bash linux/run.sh
# Optional explicit starting folder:
bash linux/run.sh "$HOME/Pictures"
```

Clone only when the separate checkout does not already exist. Do not delete or reset an existing working tree to make cloning succeed. For an existing clean Linux checkout, close the app normally, confirm the branch and run `git pull --ff-only origin linux/ubuntu-gnome`, then check and run. Never run the viewer with sudo. The initial NuGet restore needs network access.

## Current browser iteration: L002a

- Persistent, expandable **Home / File system** directory tree. Children are enumerated off the UI thread on expansion; navigating to a child retains its ancestors. Path entry and the folder picker reveal the selected path. Symbolic links are displayed as navigable leaves rather than recursively expanded.
- **Continuous thumbnail scrolling**, replacing the 72-image pages. A pixel-scrolling virtual canvas owns controls and decoded thumbnails only for the visible rows plus one overscan row on either side. Stale requests are cancelled; hidden/off-screen thumbnail bitmaps are released. The existing decoder allows at most two simultaneous decode operations.
- Columns reflow with browser width. Thumbnail captions wrap to two lines and full paths are available on hover. Tab labels retain the beginning and end of long filenames to distinguish screenshots.
- Arrow keys, Page Up/Down and Home/End navigate thumbnails; Enter/Space opens the focused image. Clicking still opens one image per tab. **Refresh / F5**, **Ctrl+L**, **Ctrl+W**, and **F11** are available.
- Returning to Browser refreshes the folder/image-count status instead of retaining the last image-preview text.
- Folder, ordered image tabs and active tab use the **unchanged session schema and location**. Fit-to-window image previews remain bounded to a requested 4096-pixel long edge. Only the active image tab is decoded.

Session location:

```text
${XDG_STATE_HOME:-$HOME/.local/state}/myoken-linux/session.json
```

A second instance cannot write the same session; corrupt/unreadable/newer-schema sessions are preserved and reported read-only. No Windows session import occurs. Default initial folder is `~/Pictures` when present, otherwise home.

### Scope limits

The thumbnail **controls** are virtualised; filenames are still fully enumerated and sorted off the UI thread before publication. The tree is lazy-loaded, **not virtualised**, and expanding a directory with a very large sibling set can still create many nodes. Expanded tree state and browser scroll position are retained during normal in-process navigation where applicable, not saved as new session settings. Links can be browsed, but deep link paths may leave the tree selection at the link ancestor. Refresh reloads the selected node's children and current file list.

There is no disk thumbnail cache, cross-viewport bitmap cache, incremental file-list publication, three-image preloading or measured real 100k-folder throughput yet. The synthetic 100k-entry UI check reuses generated fixture paths; it is a bounded-control/recycling check, not a unique-image performance benchmark.

Initial formats remain JPEG, PNG, WebP, BMP and the first frame of GIF where the decoder accepts them. Inputs above 120 million pixels are rejected. **Zoom/pan, HEIC/AVIF/JXL, video, metadata/orientation/colour-management parity, GPU decoding, filesystem watching, drag/drop and file operations remain unimplemented.**

## Display backend

This is GNOME-compatible, not GTK/libadwaita. The selected desktop backend is X11, using XWayland within a GNOME Wayland session. Native Wayland and hardware-specific performance require separate evaluation; do not infer them from CI or a WSLg launch.

## Checks and acceptance

`bash linux/check.sh` checks Windows isolation, builds the app, runs the original sorting/session regressions and the additional viewport/directory console checks.

`bash linux/smoke.sh` is an optional X11 integration suite requiring `xvfb` and Python 3. It generates disposable images/folders and isolated state, exercises scrolling, resize, tree navigation, malformed images and tabs, then starts a fresh process to check actual folder/tab/active-tab restoration and closed-tab exclusion. The restore process deliberately has no starting-folder override. `MYOKEN_TEST_OUTPUT` optionally selects a screenshot directory; CI saves screenshots containing generated fixtures only. These programmatic UI checks are not Windows Computer Use or a human GNOME usability test.

Source compilation, CI and desktop validation are separate states. See `linux/VALIDATION.md` for the user-confirmed L001 desktop result and `linux/L002A.md` for the new iteration's validation status. Do not infer L002a GNOME acceptance from the earlier L001 screenshot.

## Next work

Accept the L002a tree/continuous-grid change on the real desktop while retaining session checks. Then add zoom/pan and improve keyboard/tab ergonomics. Continue L002 performance work with incremental enumeration, byte-budgeted caches, preloading and real dataset measurements. Metadata/codecs and filesystem/packaging integration remain separate milestones; proven portable policies can later be adopted by Windows with its own regression review.

Reference documentation: Avalonia's ScrollViewer/TreeViewItem APIs and rendering documentation; Microsoft .NET Linux installation guidance. See `linux/AGENTS.md` for the development handoff. Existing repository licensing/notices remain in force. Packaging must include resolved transitive dependency notices before distribution.
