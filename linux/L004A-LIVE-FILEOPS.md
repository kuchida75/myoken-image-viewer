# L004a - live folder updates + rename/delete

Status: compilation, real Linux FileSystemWatcher create/rename/change/delete checks, same-folder rename/permanent-delete cores and every inherited Linux regression **passed** on Ubuntu CI. Native Nova acceptance remains pending.

Version: **0.1.0-alpha.12**. Window label: **L004a**.

## Live current-folder watching

The current Browser folder is watched with `FileSystemWatcher` using filename, size, last-write and creation-time notifications. Events are debounced for 300 ms and emitted as a batch. Each watcher instance carries a generation; events from a folder that was just navigated away from are ignored.

A normal batch can contain create, change, delete and rename records. MainWindow reconciles open image tabs first, then rescans the current folder and updates the virtual thumbnail list. A watcher overflow still triggers a complete current-folder rescan.

Live rescans do **not** automatically select Browser, so an image being viewed remains active. VirtualThumbnailBrowser preserves its previous vertical offset and selected path/index when possible.

## Open-tab synchronization

Rename:

- exact old path is matched with ordinal/case-sensitive comparison;
- ThumbnailCache and PreviewCache entries for old/new paths are invalidated;
- the existing DocumentTab path is mutated;
- the existing tab header/tooltips are refreshed;
- the same ImageViewer is rebound to the new path;
- if decoded dimensions remain identical, ImageViewport retains its zoom/pan state and Smooth/Pixels remains on the existing ImageSurface;
- duplicate target-path tabs are prevented.

Content change:

- only the changed path is invalidated in thumbnail + preview caches;
- an open viewer for that path reloads;
- same-dimension replacements retain its transient view geometry.

Delete:

- affected cache entries are invalidated;
- any exact matching open tab is closed;
- Browser removes the path after its live rescan.

Create:

- supported files appear on the next debounced live rescan without F5.

## User operations

Toolbar buttons:

```text
Rename…
Delete…
```

Keyboard:

```text
F2      Rename…
Delete  Delete…
```

Shortcuts do not fire while editing the folder-path TextBox.

When an image tab is active, that tab's image is the operation target. When Browser is active, the selected thumbnail is the target.

### Rename

L004a rename is **same-directory only**. The dialog accepts a filename, not a path. The result must still have a supported image extension. An existing destination file is never overwritten. Case-only renames use Linux's ordinal path identity and are allowed when supported by the filesystem.

An open tab is updated in place rather than being closed/recreated. The new path is what later session saves persist; the JSON schema itself is unchanged.

### Delete

**Delete is permanent in L004a.**

The confirmation dialog says:

```text
Delete permanently
This bypasses the desktop Trash and cannot be undone by Myoken.
```

The operation uses `File.Delete` only after confirmation. There is currently no undo/recycle layer.

Trash integration is intentionally reserved for the next file-operation pass, along with moving an image to another folder.

## Cache behavior

ThumbnailCache and PreviewCache now expose per-path invalidation. Invalidation drops cache ownership for matching keys while existing leases remain safe under the established LeasedLruCache rules.

A changed file that is already decoding is still protected by the existing before/after file-stamp checks; changed-during-decode results are rejected where the observed stamp differs. F5 remains the manual fallback for same-size/same-mtime replacements or any external change that the operating-system watcher does not report.

## Completed automated validation - 7 October 2026

Tested source: **86c4c5624eb50e9b11a704e0c53e318c79c7174d**.

Passing run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37647465253

Job: **112881872446**.

A disposable folder was created inside the isolated Xvfb test root. Tests confirmed:

- initial folder navigation starts a live watcher;
- external file creation becomes visible without Refresh/F5;
- external rename updates Browser and an already-open tab;
- the renamed tab retains the same ImageViewer object and zoom;
- external same-dimension content replacement triggers another preview decode and preserves zoom;
- Myoken same-folder rename moves the fixture and updates Browser immediately;
- Myoken permanent delete removes the fixture and Browser entry;
- external deletion removes the file entry and closes the matching tab;
- navigation returns cleanly to the original fixture folder;
- every inherited codec/color/orientation/cache/preload/tab/viewer/session regression still passes.

The first candidate failed only at compile time because of a rename-dialog TextBox selection API mismatch. It was corrected using Avalonia's `SelectionStart` and `SelectionEnd` properties before the passing run.

## Isolation

Relative to L003d, changes are confined to Myoken.Linux UI/file-watcher/cache integration/tests/version. There are no new NuGet packages or system dependencies. Windows/WPF, `main`, Myoken.Core and the Linux session schema remain unchanged.

## Nova acceptance

Close Myoken, update the clean `linux/ubuntu-gnome` checkout, run `bash linux/check.sh`, then `bash linux/run.sh`. Expect **L004a**.

Recommended native checks use a disposable copy of an image, not an irreplaceable original:

1. Open a test folder in Browser and copy a new image into it from Files/Terminal. It should appear automatically.
2. Open that copy in a Myoken tab, zoom/pan it, then rename it externally. The tab header/path should update and the view should remain.
3. Overwrite the test copy with another same-size image; Myoken should reload it after the watcher debounce.
4. Rename a disposable image using **Rename…** or **F2**.
5. Test **Delete…** only on a disposable copy. Confirm the dialog clearly says **Delete permanently** and remember that this version does not use Trash.
6. Delete or rename a test image externally and confirm Browser/open tabs reconcile without F5.

Watch for repeated reloads, lost selection/scroll, stale tab names, watcher delays, or unexpected memory/CPU spikes. CI proves behavior on generated files under Xvfb; it is not a native GNOME/Nova usability benchmark.

Next planned file-operation pass: **move-to-folder + GNOME Trash integration**, retaining these watcher/session/cache regressions.
