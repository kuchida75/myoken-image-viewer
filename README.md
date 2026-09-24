# Myoken Image Viewer

Myoken is a Windows-native photo browser and image viewer built with C# and WPF. The current source version is **0.2.170**. It targets Windows 10/11 x64 with .NET Framework 4.8.

## Highlights

- Browse folders and images in a three-pane workspace with a virtualized thumbnail grid, adjustable thumbnail size and shape, natural filename sorting, favorites and recursive filename search.
- Keep one image per tab, group tabs into named stacks, and restore tabs, stacks, sessions and window settings. Export or import a JSON workspace backup.
- Navigate with the keyboard, mouse wheel or nearby-image carousel; zoom, pan, rotate, step through neighboring images and run a windowed slideshow.
- Copy, move, rename and recycle files or folders; drag files to and from Windows Explorer. Batch rename selections, or resize and convert selected images.
- Open JPEG, PNG, WebP, AVIF, JPEG XL, HEIC/HEIF and GIF. Animated GIF playback is supported; other animated formats show their primary frame.
- Apply non-destructive Quick Enhance and manual adjustments, with optional AI super-resolution models. Explicit save commands write edited output.
- Use GPU-resident image rendering and a tunable VRAM cache where supported, with CPU/WPF fallbacks and separate iGPU/dGPU memory reporting.

The default workspace is dark, normal mode, with the folder tree visible and the right metadata panel hidden. No tabs, sessions, favorites, thumbnails or personal settings are bundled with the source or executable.

## Build And Run

This repository contains **source, not a prebuilt executable**. A fresh clone must restore the pinned codec and enhancement dependencies before building. Follow the [Windows build guide](docs/BUILDING.md); its build script produces `artifacts\MyokenImageViewer\MyokenImageViewer.exe` and advances the build number each time it runs. Optional NVIDIA JPEG acceleration has a separate restore step.

There is no public binary release yet. Bundled codecs, runtimes and AI weights require a [third-party distribution review](THIRD_PARTY_NOTICES.md) before a full executable package is published.

## Everyday Keys

| Action | Default |
| --- | --- |
| Previous / next image, with nearby previews | Left or Up / Right or Down; mouse wheel |
| Return from image to thumbnails | Enter or Esc |
| Fit image to width / height | W / H |
| Toggle compact mode / folder tree | U / J |
| Zoom at the pointer | Hold right mouse button and scroll |
| Pan a zoomed image | Drag with left mouse button |
| Fullscreen / Help | F11 / F1 |

Shortcuts can be rebound under **Configure > Shortcuts**. The [detailed feature reference](docs/FEATURE_REFERENCE.md) covers tabs, file operations, enhancements, GPU behavior and more.

## Data And Limits

Settings, sessions, thumbnail/search caches and recovery backups live in the local Windows profile, not in Git. **Configure > Reset viewer** restores defaults; select **Also clear thumbnail cache** to clear thumbnails too. See [default state and recovery](docs/DEFAULT_STATE.md).

The app is Windows-only today. RAW decoding and full HDR output are not implemented; ICC handling is optional and limited. SUPIR requires a separate local setup and is experimental. Review the [feature reference](docs/FEATURE_REFERENCE.md) for format and workflow limitations.

The project was previously named Zen Image Viewer. Older GitHub commit titles and the `v0.2.166` tag retain that history. The `src/ZonerInspiredViewer` source path and existing profile/backup identifiers remain unchanged for compatibility; the app and executable are named Myoken Image Viewer.

Original project code and documentation are licensed under [Apache License 2.0](LICENSE). Third-party components retain their own terms; see [NOTICE](NOTICE), [third-party notices](THIRD_PARTY_NOTICES.md), the [changelog](CHANGELOG.md) and the [source publication checklist](docs/PUBLISHING.md).
