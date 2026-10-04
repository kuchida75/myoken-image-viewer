# L002c1 - single-row document tabs

Status: source candidate authored; compilation and integration status must be established by its CI run. New native Ubuntu/GNOME acceptance is pending.

## Scope

Linux version 0.1.0-alpha.4, title Myoken Ubuntu/GNOME - Linux preview L002c1.

The first L002c pass replaces wrapping tabs with a fixed-height, single-row strip. Browser is pinned outside the horizontal image-tab scroller. Left/right buttons and the wheel over the strip scroll headers without selecting images. Selecting an image, cycling tabs or resizing reveals the active image header. Open tabs provides a scrollable menu of full filenames with parent folders, including literal underscores and Unicode names. Selection order follows the existing tab/session order and includes Browser.

Ctrl+Tab and Ctrl+Shift+Tab cycle forward/backward, including when the folder path has focus. Ctrl+W and the image-tab close button close an image. Middle-button press/release inside an image header closes that tab; releasing outside cancels. Browser cannot be closed. Closing the active image selects the next image to its right, otherwise the previous image, otherwise Browser. Closing an inactive tab must not reload or reset the active image.

DocumentTab is an in-process model, not a new session DTO. DocumentTabs presents only the selected Control and reuses that document's ImageViewer across switches. Existing exact case-sensitive path deduplication, zoom/pan, Smooth/Pixels sampling, active-only decoding and session schema/location remain unchanged. Zoom/pan and sampling mode are still not persisted across a full restart.

This pass does NOT add RAM/disk image caches, preloading, new codecs, metadata handling, file operations or native Wayland support. Bounded thumbnail/preview caches are the following independently tested pass. Headers/menu entries are not virtualised; this is not a claim about thousands of open tabs or screen-reader acceptance.

## Candidate tests

The new X11 integration checks create 42 image tabs from disposable fixtures, narrow the window to 820 logical pixels and verify single-row geometry, active-header visibility and pinned controls. They open the actual popup, check full Unicode/underscore names and distinct parent paths, and use MenuItem.Click, routed keyboard and middle-button capture/release events. They check close neighbours, inactive-image identity, Browser protection and recovery after all images close. The existing browser, rendered sampling, viewer input and fresh-process session tests remain mandatory. Popup screenshots are not implied by main-window RenderTargetBitmap captures.

## Nova acceptance

Close Myoken normally, update the clean linux/ubuntu-gnome checkout with the guarded block in linux/README.md, then run bash linux/check.sh and bash linux/run.sh. No new system packages or driver changes are required. Open enough image tabs to overflow, resize the window, scroll headers and select a hidden tab from Open tabs. Confirm Browser remains reachable and no second row appears. Test Ctrl+Tab/Ctrl+Shift+Tab, middle-click and Ctrl+W, then confirm zoom/pan and session restore still work. Do not infer desktop acceptance from CI.

## Previous sampling feedback

On 4 October 2026 the user supplied a screenshot with Smooth selected at 189.3%, reporting "much better". This is user-confirmed improvement of the L002b sampling patch on Nova, not evidence of L002c1 acceptance. No user screenshot, file contents or session is copied into this repository or CI.
