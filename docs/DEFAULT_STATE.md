# Default State And Recovery

The executable does not contain a personal session or favorites list. First
launch loads built-in defaults when no user profile exists. Copying the source
or executable never copies a developer's settings by itself.

The normal Windows profile is `%LOCALAPPDATA%\ZonerInspiredViewer`. The legacy
folder name is retained for compatibility. `ZONER_VIEWER_PROFILE` can select a
different local profile, useful for isolated tests. Do not set this variable
globally just to test a clean launch.

## New User Defaults

- Dark theme; normal workspace mode, with the folder tree available and the
  right metadata panel hidden.
- Teal crosshatch background at 100% intensity.
- No restored image/folder tabs, tab stacks, named sessions or favorites.
- No search history or custom shortcut bindings.
- Enhancements and slideshow off until enabled; no saved image adjustments.
- Square 128-pixel browser thumbnails; caches populate locally as needed.
- Default memory/GPU settings, with hardware-dependent fallback where needed.

The browser can open the current user's Pictures folder on first launch.
That is a local default resolved at runtime, not a developer path in the build.

## Reset An Existing Installation

Close other viewer instances, then use **Configure > Reset viewer**. Reset
clears settings, open tabs/stacks, saved sessions, favorites, search history and
custom shortcuts. Original photos and downloaded AI models are not changed.

The app creates a recovery backup before replacing settings. Import
`viewer-backup.json` through **Sessions > Import backup** to recover the primary
workspace, favorites and named sessions. Missing image tabs are omitted on
import. Raw `original-settings` files preserve extra instance state and files
that could not be interpreted; keep those for manual recovery with the app
closed. Backups are private data and must never be committed or distributed.

Thumbnail cache is retained unless **Also clear thumbnail cache** is selected.
Keeping a local cache does not change default settings. Search and thumbnail
caches, recovery backups, session files and per-machine configuration are not
part of this source repository. Reset is not a secure-erasure operation.

## Publication Checks

Use an isolated profile to test new-user behavior. Check that Git contains no
`session.json`, `browser.json`, instance/named-session files, recovery backups,
thumbnail cache or search index. Do not ship an empty template profile: allow
the app to create its own defaults, so personal paths cannot leak into a build.
