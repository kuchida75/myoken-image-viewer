# L002c1 - single-row document tabs

Status: compilation, original policy/regression suites, new single-row tab-strip/popup/input checks, rendered sampling, viewer and fresh-process restoration checks **passed** on the Ubuntu CI runner. Native Ubuntu/GNOME acceptance of L002c1 remains pending.

## Scope and controls

Linux version **0.1.0-alpha.4**, title **Myoken Ubuntu/GNOME - Linux preview L002c1**.

This first L002c pass replaces wrapping tabs with a fixed-height, single-row strip. Browser is pinned outside the horizontal image-tab scroller. Left/right buttons and the wheel over the strip scroll headers without selecting images. Selecting an image, cycling tabs or resizing reveals the active image header. Open tabs provides a scrollable menu of full filenames with parent folders, including literal underscores and Unicode names. Selection order follows the existing tab/session order and includes Browser.

Ctrl+Tab and Ctrl+Shift+Tab cycle forward/backward, including when the folder path has focus. Ctrl+W and the image-tab close button close an image. Middle-button press/release inside an image header closes that tab; releasing outside cancels. Browser cannot be closed. Closing the active image selects the next image to its right, otherwise the previous image, otherwise Browser. Closing an inactive tab does not reload or reset the active image.

DocumentTab is an in-process model, not a new session DTO. DocumentTabs presents only the selected Control and reuses that document's ImageViewer across switches. Exact case-sensitive path deduplication, zoom/pan, Smooth/Pixels sampling, active-only decoding and the session schema/location are unchanged. Zoom/pan and sampling mode are still not persisted across a full restart; restored images start in Fit/Smooth.

This pass does NOT add RAM/disk image caches, preloading, new codecs, metadata handling, file operations or native Wayland support. Bounded thumbnail/preview caches are the following independently tested passes. Headers/menu entries are not virtualised; this is not a claim about thousands of open tabs or screen-reader acceptance.

## Completed automated validation - 4 October 2026

Tested application and test-source commit: `dcd1149fe1df69933af882715d9399e05cb2a790`.

Passing GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37234437118

Job `111530640003` completed successfully. The completed job steps and full log were reviewed before advancing the shared Linux branch. Runner: Ubuntu 24.04.5 LTS x64; .NET SDK 10.0.401. These checks ran on GitHub Actions, not on Nova or an assistant-controlled desktop.

Commands executed:

```bash
bash linux/check.sh
bash linux/smoke.sh
```

Confirmed by the completed log:

- Windows-baseline isolation and compilation passed. Zero compilation errors; the one existing obsolete Bitmap.Save warning in BrowserRegression.cs remains.
- Original natural-sort/session checks passed, including numeric/Unicode/case handling, session round trip, writer exclusion and corrupt/newer-schema preservation.
- All 600 browser viewport combinations and directory/path/cancellation checks passed, as did 60 viewer geometry/render-scale combinations and decode-policy limits.
- The X11 window test opened 42 generated image tabs and narrowed the window to 820 logical pixels. Headers shared one baseline inside a single fixed-height row; Browser and Open tabs remained outside horizontal overflow; the selected header was revealed. Inactive image tabs did not decode simply because their headers existed.
- Header-wheel input changed horizontal scroll offset without changing selected image or image zoom.
- The actual Open tabs button opened a populated popup containing all 43 documents (42 images plus Browser). Full long Unicode/underscore filenames and distinct parent folders for identical names were preserved. Selecting a MenuItem dismissed the popup, activated the intended document and revealed its header.
- Ctrl+Tab while editing the path changed selection without editing the text. Ctrl+Shift+Tab and forward/backward wrap through Browser passed.
- The middle-close target was first verified visible. Middle press captured the header without closing early; release inside closed the inactive tab without changing/re-decoding the active image. Release outside cancelled closing. Active close selected the right neighbour; Ctrl+W on the last image selected its left neighbour. Browser resisted middle-click and Ctrl+W; closing all images left it usable. Single-Browser cycling was a no-op.
- Existing browser scrolling, resize reflow, tree ancestors/parent navigation, stale-load rejection and hidden-browser resource release passed.
- Actual rendered Smooth/Pixels comparisons at 176.8%, 200% and 800%, exact 100% fixture pixels, preview/full-source transitions and minification passed.
- Original-resolution decoding of the 6000x2000 fixture, pointer-anchored wheel zoom, drag/capture/release, Fit, in-process zoom/pan/sampling retention, cancellation and corrupt-image checks passed.
- A fresh process restored its folder without a launch-path override, exact ordered remaining tabs and the active image, excluded a closed tab and retained the single-row strip. Returning to Browser restored folder/count status.

Seven generated-fixture screenshots were uploaded by CI, including the narrow overflowing strip. No user image or session was used. These captures do not establish a human visual review, full popup pixel coverage, GNOME input/scaling behaviour, physical monitor-pixel parity or a performance benchmark. The inherited synthetic 100k-entry browser test still reuses 120 fixture paths.

Earlier candidate checks exposed a toolbar helper shadowing the Button type, a test assuming zero vertical offset for centred headers, and an empty popup on the programmatic Open route. The helper was renamed, the row assertion now checks common baseline and containment, and the actual button path explicitly builds the menu before opening it. All affected assertions were retained; failing candidates were not promoted to linux/ubuntu-gnome. Additional wheel and visible-target checks passed in the final run.

The diff against Windows v0.2.167 contains additions only: no pre-existing Windows file was changed/deleted. Relative to the previous Linux build, application changes are confined to document tabs, MainWindow integration, Linux version and regression code. Image loaders, sampling, viewport geometry, portable core, dependency versions and session schema are unchanged. The final documentation follow-up changes only README.md, AGENTS.md, VALIDATION.md and this file. Application/test sources remain the passing revision above. Main and Windows releases are outside this update.

## Nova acceptance

Close Myoken normally, update the clean linux/ubuntu-gnome checkout with the guarded block in linux/README.md, then run bash linux/check.sh and bash linux/run.sh. No new system packages, Ubuntu/NVIDIA/CUDA upgrades or Python changes are required. Expect L002c1 in the title.

Open eight or more image tabs and narrow the window. Confirm there is no second row; Browser and Open tabs remain reachable. Scroll the header strip and choose a hidden image from Open tabs. Test Ctrl+Tab/Ctrl+Shift+Tab, middle-click and Ctrl+W. Close an inactive tab while another image is zoomed/panned; that image should not reset. Switch tabs and confirm existing per-tab viewing position and Smooth/Pixels behaviour. Quit normally and relaunch to recheck ordered tabs, active selection and closed-tab exclusion. New desktop acceptance must be recorded separately from CI.

## Previous sampling feedback

On 4 October 2026 the user supplied a screenshot with Smooth selected at 189.3%, reporting "much better". This is user-confirmed improvement of the L002b sampling patch on Nova, not evidence of L002c1 acceptance. No user screenshot, file contents or session is copied into this repository or CI.
