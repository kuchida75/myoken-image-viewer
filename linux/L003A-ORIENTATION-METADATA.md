# L003a - EXIF orientation and basic metadata

Status: compilation, all-eight-orientation pixel/dimension tests, portable metadata extraction, Info-panel checks and every inherited cache/tab/viewer/session regression **passed** on Ubuntu CI. Native Nova acceptance remains pending.

Version: **0.1.0-alpha.8**. Window label: **L003a**.

## Behaviour

Myoken now inspects the decoder's encoded origin and normalises EXIF/encoded orientations **1-8** before decoded pixels enter thumbnail or viewer-preview caches. This includes mirror-horizontal, 180°, mirror-vertical, transpose, 90° clockwise, transverse and 90° counter-clockwise. For orientations 5-8, viewer source width/height are swapped to the visual dimensions. The original encoded dimensions remain available in Info.

TopLeft/normal images retain the existing Avalonia bitmap decode path. Images requiring an orientation transform use a bounded Skia decode/resize and affine transform before being copied into an Avalonia bitmap. The same common orientation layer feeds thumbnails, PreviewCache, normal viewer display and explicit full-resolution viewing, so cache entries are already visually oriented.

Image tabs add **Info** next to Smooth/Pixels. With the viewer focused, **I** toggles the same 340-pixel side panel. Metadata is not scanned for every thumbnail: the panel parses it lazily only when first opened. It shows file path/size/modified time, display and encoded dimensions, orientation, common camera fields, and a bounded details list. Refresh/F5 invalidates thumbnail/preview caches and metadata; if Info is visible, its metadata is reread.

Portable metadata types and parsing are in Myoken.Core, which remains **netstandard2.0** and UI/platform independent. It now references **MetadataExtractor 2.9.3**. The current Windows WPF application does not reference Myoken.Core and is not changed by this milestone.

## Completed automated validation - 7 October 2026

Tested source: **b1a3b7166a27a3312189069610690bf914b5c4a5**.

Passing run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37629673905

Job: **112820339136**. Completed Ubuntu 24.04.5 CI logs were reviewed.

Commands:

```bash
bash linux/check.sh
bash linux/smoke.sh
```

Confirmed:

- Windows-baseline isolation and compilation passed; zero errors and the single pre-existing obsolete screenshot-helper warning.
- Portable ImageOrientation normalization/display-name/axis-swap tests passed.
- Generated 40×30 JPEGs carrying EXIF orientations 1-8 passed oriented-dimension checks and distinct four-corner colour signatures for every transform.
- Orientation 6 produced an oriented thumbnail with the expected portrait dimensions before cache publication.
- Portable metadata extraction returned EXIF orientation plus synthetic Make/Model/Software fields.
- A real ImageViewer tab used the oriented 30×40 source geometry for a 40×30 encoded orientation-6 JPEG.
- The lazy Info panel displayed the camera metadata and readable orientation label.
- Existing thumbnail/preview caches, neighbour preloading, tab-strip controls, Smooth/Pixels rendered output, full-resolution viewer path, zoom/pan and fresh-process session restoration remained passing.

The first L003a candidates exposed API/compile compatibility issues (MetadataExtractor extension import, a property-name collision, and a Skia draw overload). The final source retains the intended assertions rather than weakening them. Metadata invalidation was also changed to a reloadable per-read cancellation token before the final passing run.

## Metadata scope

The summary fields currently include camera make/model, date taken, exposure time, f-number, ISO, focal length, software, reported colour-space tag and image description where present. The details list is capped at **256 tags**, with each description capped at **2048 characters**. Unsupported/malformed metadata is reported without changing source files.

Metadata is read-only. This milestone does not write EXIF/XMP/IPTC data and does not add search/indexing by metadata yet.

## Colour and codec limits

**L003a does not implement ICC colour management.** The Info panel may display an EXIF colour-space description, but decoded pixels are not transformed through embedded ICC profiles to the monitor profile. Do not call this colour-management parity.

Current image-format support remains JPEG, PNG, WebP, BMP and first-frame GIF. HEIC, AVIF and JXL are still future codec work. RAW formats and their maker-specific orientation/metadata behaviour are outside current support.

## Isolation and dependency notice

A diff against Windows v0.2.167 shows no changes to the Windows source, build/version journals or scripts. Windows still uses its existing WPF application. Myoken.Core remains netstandard2.0 but now has a portable third-party dependency on MetadataExtractor 2.9.3; Linux packaging/distribution must include the appropriate resolved third-party licence/notices.

## Nova acceptance

Close Myoken normally, update the clean `linux/ubuntu-gnome` checkout, run `bash linux/check.sh`, then `bash linux/run.sh`. Expect **L003a**.

Use a real phone/camera JPEG that relies on EXIF orientation if available (portrait files with Orientation 6 or 8 are especially useful). Confirm its Browser thumbnail and image tab are upright. Open **Info** (or press **I** with viewer focus) and compare display versus encoded dimensions, orientation, camera and date fields. Toggle Info while zoomed/panned and switch tabs to make sure the view state remains intact. Press Refresh with Info open and confirm the panel rereads rather than asking for a reopen.

Also spot-check ordinary screenshots with no special orientation; their existing appearance/performance should remain unchanged. Colour accuracy, ICC profiles and HEIC/AVIF/JXL are not acceptance criteria for L003a.
