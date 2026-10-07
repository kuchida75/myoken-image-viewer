# L004b — Move to folder and GNOME Trash

Version 0.1.0-alpha.13, window label L004b. Linux-only milestone based on L004a.

Move to folder… opens a local folder picker and moves the selected Browser image or active image tab without overwriting an existing file. Same-folder moves are no-ops. The existing DocumentTab/ImageViewer is rebound in place, retaining transient zoom/pan/sampling state. Source and destination cache paths are invalidated; the current folder is reconciled immediately, and delayed source watcher events cannot close the rebound tab. Cross-filesystem moves use .NET File.Move; failures are reported rather than claimed as success.

Move to Trash… and the Delete key confirm and invoke `gio trash -- <absolute path>` using separate process arguments. GIO supplies GNOME/freedesktop mount-aware Trash policy. Restore is available through desktop Trash, not inside Myoken. A missing GIO executable or failed trash operation reports an error and never falls back to permanent deletion. Ubuntu supplies gio through libglib2.0-bin.

Delete permanently… remains a separate explicitly labelled toolbar action with the irreversible-delete confirmation. No permanent-delete keyboard shortcut is added. Cancellation performs no mutation. Session schema/location and ordinal Linux identity remain unchanged.

## Automated validation

Local linux/check.sh passed compilation, Windows baseline isolation and core/session/browser/viewer/cache checks. Local linux/smoke.sh could not run because Xvfb is unavailable; CI supplies the headless display, codecs and GIO.

The existing real watcher create/rename/change/delete tests remain. Added integration checks cover destination collision preservation, same-folder no-op, missing destination, cross-folder move with delayed watcher events, viewer identity/zoom, moved session path, destination listing, missing/failed GIO without deletion fallback, real GIO recoverable contents/trashinfo in disposable XDG_DATA_HOME, and removal of trashed tabs from saved sessions. Existing explicit permanent deletion and fresh-process restoration remain covered.

Human GNOME validation is pending: folder picker cancellation, moves between mounts, Trash restoration, unavailable/unsupported Trash errors, minimum-window toolbar layout, and permanent-delete confirmation. Automated headless checks do not establish native GNOME acceptance.


## CI result — 2026-10-07

Application source `4df01c641f72bb426562f5cdf6ce0c5bf38383d9` passed the complete Ubuntu isolation/build/policy/headless smoke/fresh-process suite in push run 37649622246 and PR run 37649629785. The real GIO recoverable-content/trashinfo assertions, move watcher-drain identity/zoom checks and session assertions passed. Human GNOME acceptance remains pending.
