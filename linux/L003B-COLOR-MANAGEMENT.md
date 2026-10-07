# L003b - source-profile-aware sRGB normalization

Status: compilation, tagged Display P3 pixel conversion, thumbnail/viewer/Info checks and every inherited orientation/cache/tab/render/session regression **passed** on Ubuntu CI. Native Nova acceptance remains pending.

Version: **0.1.0-alpha.9**. Window label: **L003b**.

## Colour pipeline in this pass

L003b introduces a defined **sRGB working space** for normal SDR image pixels.

During the initial codec probe, Myoken inspects Skia's decoded source colour-space information. Untagged/unspecified sources are treated as sRGB. Native sRGB sources need no transform. Recognised non-sRGB spaces are decoded with an explicit sRGB destination before resizing/orientation/caching.

Current source labels include:

- sRGB
- Display P3
- Adobe RGB
- Rec. 2020
- sRGB primaries with a non-sRGB transfer function
- Custom / ICC colour space

The labels are descriptive. The conversion decision is simply: a Skia source colour space exists and is not sRGB → request an sRGB decode.

For **TopLeft + sRGB/untagged** files, the established Avalonia decode path remains the fast path. Non-sRGB tagged files use Skia's colour-aware target decode. Files requiring EXIF orientation already use Skia; those pixels are now explicitly decoded/resized/oriented in sRGB before an Avalonia bitmap is created.

This means ThumbnailCache and PreviewCache store already-normalised sRGB pixels. Explicit full-resolution upgrades use the same source-profile rule.

## UI

When a source transform occurs, the image status includes:

```text
color→sRGB
```

Info shows:

```text
Color
  Source: Display P3
  Working: sRGB
  Source → sRGB: applied
  Display profile: not applied by Myoken yet.
```

For an untagged source it also explains that the image is treated as sRGB.

The separate EXIF `Color space` metadata line from L003a remains a metadata description. The new Color block reports what the decoder actually did with pixel data.

## Completed automated validation - 7 October 2026

Tested source: **a486886ad88240448bcbd5e3664c142b8f735c1b**.

Passing run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37631683377

Job: **112827293000**.

Commands:

```bash
bash linux/check.sh
bash linux/smoke.sh
```

The test generated a PNG tagged as Display P3 with raw numeric RGB **220, 90, 40**. Skia recognised the encoded source as Display P3. Myoken's source-profile path:

- flagged the conversion as active;
- decoded to its sRGB working space;
- produced **238, 78, 4**;
- matched a separate Skia sRGB-target reference decode within one channel value;
- differed from the source tagged values, proving conversion rather than metadata-only relabelling;
- produced the same managed result through the thumbnail path;
- exposed the source/working/transform boundary in a real ImageViewer Info panel.

Every L003a orientation 1-8 pixel/dimension test remained passing. Existing thumbnail/preview caches, neighbour preloading, tabs, rendered sampling, full-resolution zoom, pointer input and cross-process session restoration also remained passing.

## Scope and limitations

This is **source-profile-aware SDR normalization**, not full output-device colour management.

Myoken currently hands sRGB working pixels to Avalonia/X11/XWayland. It does not query the active monitor profile and does not itself transform sRGB working pixels into that monitor's calibrated device space. GNOME/compositor/display handling therefore remains outside the tested Myoken pipeline.

HDR/PQ/HLG tone mapping is not implemented. A Rec.2020 or other non-sRGB source that Skia can represent will be requested into the SDR sRGB target, but L003b contains no HDR display policy and does not claim HDR correctness.

The current automated colour fixture is Display P3. Adobe RGB, Rec.2020 and arbitrary ICC labels follow the same Skia source→sRGB code path but do not yet have separate golden fixtures.

No new dependency was added in L003b. MetadataExtractor remains from L003a; pixel conversion is performed by the existing SkiaSharp backend.

## Isolation

Relative to L003a, the change is confined to Myoken.Linux decode/UI/regression/version files. Windows/WPF, `main`, Myoken.Core, package versions and the session schema are unchanged.

## Nova acceptance

Close Myoken, update a clean `linux/ubuntu-gnome` checkout, run `bash linux/check.sh`, then `bash linux/run.sh`. Expect **L003b**.

Useful native checks:

1. Open a real Display P3 photo (recent iPhone/phone photos are often useful, depending on export format). Info should identify a non-sRGB source when Skia recognises the profile and status should show **color→sRGB**.
2. Compare the image with a normal sRGB screenshot/photo. It should not look wildly oversaturated from treating P3 numeric values as sRGB.
3. Check the Browser thumbnail and full image look consistent with one another.
4. Recheck an EXIF-rotated portrait image; orientation and colour conversion must coexist.
5. Switch tabs/preloaded images and verify no colour change appears when a cache hit occurs.

Do not use this milestone to judge calibrated monitor-profile accuracy or HDR output. Those require a later display-output pipeline.
