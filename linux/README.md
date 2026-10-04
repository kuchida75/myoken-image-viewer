# Myoken Ubuntu/GNOME

Independent Linux development track in the existing Myoken repository.

- Development branch: `linux/ubuntu-gnome`; Linux feature branches are checked in CI before a fast-forward update.
- Windows baseline: **v0.2.167**, commit `ea785b3770383278f1a3bf67762d35014e8bda26`. Isolation checks are not a claim of GitHub branch-protection settings.
- Target: Ubuntu 24.04 LTS and newer with GNOME, initially x64.
- Linux UI: C# / .NET 10 / Avalonia 12.1.2. Current source version: **0.1.0-alpha.3.1**, window label **L002b**; the sampling patch adds a **Smooth/Pixels** button.
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
    bash linux/check.sh
    bash linux/run.sh
)
```

No new APT packages, Ubuntu release upgrade, NVIDIA/CUDA changes or Python changes are required to update an already-working Linux checkout. Run the viewer as your normal user, never sudo. The first NuGet restore requires network access. For a new machine, install git, dotnet-sdk-10.0, libx11-6, libice6, libsm6, libfontconfig1, libxrandr2, libxi6, libxcursor1, libgl1 and xwayland from the configured Ubuntu feeds after reviewing APT's proposal, then clone linux/ubuntu-gnome into a new, separate directory. Do not delete an existing checkout to make cloning succeed.

## Browser inherited from L002a

The expandable Home/File system folder tree loads one level asynchronously and retains ancestors. Symbolic links are navigable leaves rather than recursively expanded. Continuous thumbnail scrolling realises controls/bitmaps for visible rows plus one overscan row each side, cancelling and releasing off-screen work. Columns reflow with width. Captions wrap to two lines, full paths appear on hover and tab labels expose distinguishing suffixes. Returning to Browser restores folder/count status.

Browser keys: arrows, Page Up/Down, Home/End, Enter/Space. Window keys: Refresh/F5, Ctrl+L, Ctrl+W and F11. One image per exact case-sensitive Linux path remains the tab identity rule.

## L002b image viewing

Image tabs provide pointer-centred wheel zoom, left/middle drag pan with edge bounds, Fit and true source-resolution 100% controls, minus/plus buttons and a zoom indicator. Double-click toggles Fit/100%. With viewer focus: 0 = Fit, 1 = 100%, +/- = zoom, arrows = pan. Fit does not enlarge a small image.

The sampling patch defaults to **Smooth** for scaled viewing. Click the Smooth/Pixels button to opt into hard pixel edges when enlarged. Full-source 100% remains unfiltered 1:1 in either mode; downsizing stays filtered. The mode is per tab, retained while switching tabs and reset to Smooth after restart. Switching modes changes neither source files nor zoom/pan. See linux/L002B-SAMPLING.md for the diagnosis, rendered-pixel tests and Nova comparison steps. Smoothing does not add source detail or AI upscale the image.

100% means one source pixel per application render-target pixel, accounting for Avalonia RenderScaling. It is not a promise of monitor-pixel parity after additional XWayland/compositor scaling. The initial 4096-edge preview is upgraded from the original file when the view needs more detail. Preview/loading/error status remains explicit. Full decode is guarded at 64 million source pixels; larger accepted images stay preview-only with 100% disabled. The existing 120-million-pixel input ceiling remains. See linux/L002B.md for resource limits and exact validation status.

Each tab keeps zoom/pan during in-process tab switching, while releasing its hidden bitmap. Zoom/pan is not saved across a full application restart yet; restored tabs start in Fit. Existing folder, ordered tabs and active-tab restoration use the unchanged session format and location:

```text
${XDG_STATE_HOME:-$HOME/.local/state}/myoken-linux/session.json
```

A second writer is excluded; corrupt, unreadable or newer-schema sessions are preserved and reported read-only. No Windows session import occurs.

## Scope limits and display backend

JPEG, PNG, WebP, BMP and the first GIF frame remain the initial formats. Advanced codecs, video, metadata/EXIF orientation/colour-management parity, GPU decoding, filesystem watching, file operations and drag/drop remain future work. Filenames are still fully enumerated/sorted before publication. The directory tree is lazy, not virtualised. Disk caching, cross-viewport bitmap caching, three-image preloading, incremental file publication and measured real 100k-image throughput are not implemented. A synthetic repeated-path control-allocation test is not a real large-folder performance benchmark.

This is GNOME-compatible, not GTK/libadwaita. The selected backend is X11, through XWayland in a GNOME Wayland session. Native Wayland, fractional scaling and hardware performance require separate validation. Full-resolution decode limits are output-size guards, not bounds on total process/GPU memory.

## Tests and acceptance

`bash linux/check.sh` checks Windows isolation, compilation and core/session/browser/viewer policy regressions. `bash linux/smoke.sh` additionally needs Xvfb and Python 3, creates disposable fixtures/state, exercises real X11 windows including viewer input, checks actual rendered sampling pixels, and checks folder/tab/active-tab restoration in a fresh process without a starting-folder override. `MYOKEN_TEST_OUTPUT` optionally captures generated-fixture screenshots; these are not a human visual review.

Keep source authoring, compilation, CI and user desktop validation distinct. linux/VALIDATION.md preserves user and CI results; linux/L002A.md, linux/L002B.md and linux/L002B-SAMPLING.md record iteration-specific scope and checks. The sampling fix requires its own Nova comparison. Read linux/AGENTS.md before development.
