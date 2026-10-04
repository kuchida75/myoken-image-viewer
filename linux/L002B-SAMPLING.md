# L002b sampling correction

Status: candidate source and regression tests authored; compilation and CI results must be verified before advancing linux/ubuntu-gnome. Native Nova validation of this patch is pending.

## User report and diagnosis - 4 October 2026

The user supplied an L002b screenshot and reported: "looks pixelated, dragging and returning to the same viewing position work correctly." This confirms user-tested dragging and in-process viewing-position restoration, not every viewer control or a restart test. The screenshot's status shows a 1020 x 475 image at 176.8% zoom, marked full resolution. The screenshot remains in the conversation; no user image is copied into the repository or CI.

The source at 731e359 selected BitmapInterpolationMode.None for every View.Zoom >= 1 in ImageSurface.NotifyViewChanged. That forces nearest-neighbour enlargement, including fractional scales such as 176.8%, accentuating hard pixel boundaries. The reported full-resolution state and small source dimensions do not suggest the 4096-preview limit as the cause for this example. A display/compositor contribution cannot be ruled out solely from the screenshot.

## Change

Linux version 0.1.0-alpha.3.1 retains the L002b window title and adds a Smooth/Pixels button to image tabs. Smooth is the default for scaled viewing, with HighQuality interpolation applied directly around DrawImage. Full-resolution 100% uses unfiltered 1:1 render-target mapping; a reduced preview at source zoom 100% is not misidentified as exact 1:1. Pixels explicitly opts into nearest-neighbour enlargement above 100%; minification remains filtered. Sampling is evaluated on every draw, including when original-resolution pixels replace a reduced preview.

The button shows the current mode and toggles it without resetting zoom/pan or re-decoding the same bitmap. Mode selection is per tab and retained across in-process tab switching, not across application restart. It does not edit any source image or introduce AI upscaling. Smoothing reduces hard blocks but cannot recreate detail absent from a low-resolution screenshot.

The toolbar hint wraps in the remaining width rather than overflowing when the new control is added. The browser, pointer/pan geometry, decoding limits/gates, dependency versions, portable core and session schema/location are unchanged. Windows/WPF/main and Windows profiles are outside this patch.

## Regression coverage

The existing guarded X11 suite now renders a generated 16x16 black/white checkerboard at 176.8%, 200% and 800%, and reads back pixels to distinguish actual smooth interpolation from nearest-neighbour blocks. A separate 100% render compares every source pixel. Reduced-preview/full-source transitions and minification policy are checked. No user images are used.

The live viewer checks exercise the Smooth/Pixels button route, preservation of geometry and bitmap identity, independent defaults on other tabs and mode retention after hidden-tab reload. All original pointer/drag, source-resolution upgrade, browser and fresh-process session tests remain mandatory. The same linux/check.sh and linux/smoke.sh commands run these checks; no new test dependencies are introduced.

## Nova acceptance

Close Myoken normally, update the clean linux/ubuntu-gnome checkout, run bash linux/check.sh, and launch bash linux/run.sh. Do not upgrade Ubuntu, drivers or Python packages for this patch. Use the same screenshot at approximately 176.8%, leave Smooth selected and toggle Pixels for a direct comparison without changing viewing position. Check 100%, pan and switching away/back. Full-resolution status describes decoded source detail; it does not mean new detail is invented when magnifying beyond 100%.

Reference: https://docs.avaloniaui.net/docs/graphics-animation/image-interpolation and https://docs.avaloniaui.net/docs/how-to/image-how-to describe the distinct interpolation modes. Actual Myoken validation must be based on completed tests and the user's desktop report, not documentation alone.
