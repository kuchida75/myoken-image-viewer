# L002b sampling correction

Status: source compilation, rendered-pixel sampling checks and all existing core/browser/viewer/session regressions passed in Ubuntu CI. Native Nova validation of this patch is pending. The user confirmed dragging and per-tab viewing-position restoration in the preceding L002b build.

## User report and diagnosis - 4 October 2026

The user supplied an L002b screenshot and reported: "looks pixelated, dragging and returning to the same viewing position work correctly." This confirms user-tested dragging and in-process viewing-position restoration, not every viewer control or a restart test. The screenshot's status shows a 1020 x 475 image at 176.8% zoom, marked full resolution. The screenshot remains in the conversation; no user image is copied into the repository or CI.

The source at 731e359 selected BitmapInterpolationMode.None for every View.Zoom >= 1 in ImageSurface.NotifyViewChanged. That forces nearest-neighbour enlargement, including fractional scales such as 176.8%, accentuating hard pixel boundaries. The reported full-resolution state and small source dimensions do not suggest the 4096-preview limit as the cause for this example. A display/compositor contribution cannot be ruled out solely from the screenshot.

## Change

Linux version 0.1.0-alpha.3.1 retains the L002b window title and adds a Smooth/Pixels button to image tabs. Smooth is the default for scaled viewing, with HighQuality interpolation applied directly around DrawImage. Full-resolution 100% uses unfiltered 1:1 render-target mapping; a reduced preview at source zoom 100% is not misidentified as exact 1:1. Pixels explicitly opts into nearest-neighbour enlargement above 100%; minification remains filtered. Sampling is evaluated on every draw, including when original-resolution pixels replace a reduced preview.

The button shows the current mode and toggles it without resetting zoom/pan or re-decoding the same bitmap. Mode selection is per tab and retained across in-process tab switching, not across application restart. It does not edit any source image or introduce AI upscaling. Smoothing reduces hard blocks but cannot recreate detail absent from a low-resolution screenshot. Full resolution describes decoded source detail, not new detail invented above 100% zoom.

The toolbar hint wraps in the remaining width rather than overflowing when the new control is added. The browser, pointer/pan geometry, decoding limits/gates, dependency versions, portable core and session schema/location are unchanged. Windows/WPF/main and Windows profiles are outside this patch.

## Completed automated validation - 4 October 2026

Tested source: f8994de75e0491a059855ef57d5e44243bbaae40.

Passing GitHub Actions run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37232234047

Job: 111524211122. Runner reported Ubuntu 24.04.5, .NET SDK 10.0.401. Completed job steps and logs were read before promoting the patch. Tests ran on GitHub Actions, not on the user's computer or in the assistant's container. The final documentation commit changes only this file, README.md and VALIDATION.md; the application/test source remains the passing revision above.

Executed commands: bash linux/check.sh and bash linux/smoke.sh.

- Windows isolation, compilation, original core/session tests, 600 browser viewport combinations and directory checks, and 60 viewer geometry/scale combinations passed. Compilation had zero errors and the one pre-existing obsolete Bitmap.Save warning in BrowserRegression.cs.
- New rendering tests generated a 16x16 black/white checkerboard and rendered it through ImageSurface at 176.8%, 200% and 800%. Each sampled 12x12 interior region contained 144 blended samples in Smooth versus 0 in Pixels. This checks actual output pixels, not only a configuration enum.
- At 100%, all 256 source pixels matched the rendered fixture. Reduced-preview/full-source transitions and filtered minification passed.
- The live viewer's Smooth/Pixels button route changed neither zoom/pan nor decoded bitmap identity. New tabs defaulted independently to Smooth; the selected mode survived in-process tab switching. Existing wheel-anchor, drag/capture/release, full-resolution upgrade, cancellation, corrupted-image and view-position checks passed.
- Continuous browser scrolling, resize, tree navigation, hidden-view resource release and the fresh-process folder/ordered-tab/active-tab/closed-tab-exclusion regression passed.

Six generated-fixture screenshots were uploaded by CI, including Smooth and Pixels sampling fixtures. No user images or sessions were used. Pixel equality at 100% applies to the tested render target, not guaranteed physical monitor-pixel parity after GNOME/XWayland scaling. These automated checks do not establish the appearance of the user's screenshot on Nova or performance with large images.

## Nova acceptance

Close Myoken normally, update the clean linux/ubuntu-gnome checkout using the guarded README block, run bash linux/check.sh, and launch bash linux/run.sh. No new APT packages, Ubuntu/driver updates or Python changes are required. The title still says L002b; the new Smooth/Pixels button identifies the patch.

Use the same screenshot at approximately 176.8%, leave Smooth selected and toggle Pixels for a direct comparison without changing viewing position. Check 100%, pan and switching away/back. Smoother edges are expected; extra original detail is not. The user's new desktop result must be recorded separately from this passing CI run.

Reference: https://docs.avaloniaui.net/docs/graphics-animation/image-interpolation and https://docs.avaloniaui.net/docs/how-to/image-how-to describe the distinct interpolation modes. Actual Myoken validation is based on completed tests and user reports, not documentation alone.
