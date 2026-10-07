# L003d - JPEG XL decoding

Status: compilation, real lossless-alpha JPEG XL decode/thumbnail/viewer/cache/codec-registry checks and every inherited Linux regression **passed** on Ubuntu CI. Native Nova acceptance remains pending.

Version: **0.1.0-alpha.11**. Window label: **L003d**.

## Runtime decoder

Myoken.Linux adds:

```text
PhotoSauce.NativeCodecs.Libjxl 0.12.0-preview1
```

The current package wraps libjxl 0.12.0 and includes native glibc Linux x64/arm64 binaries. An already-working Nova checkout therefore needs no host JPEG XL decoder package.

GitHub Actions installs Ubuntu `libjxl-tools` solely to get `cjxl` for creation of a genuine disposable test image. That package is not a Myoken runtime prerequisite.

## Shared advanced-codec registry

PhotoSauce `CodecManager.Configure` replaces its process-global codec collection. L003c previously registered libheif alone. L003d introduces `AdvancedCodecRegistry`, which atomically registers:

- libheif for HEIC/HEIF/AVIF;
- libjxl for JPEG XL.

Both decoder families then call the same one-time registry. The integration test explicitly decodes HEIC again after JPEG XL use to prevent regressions where one codec silently disables the other.

## Decode behavior

Browser recognises `.jxl`.

For JPEG XL Myoken:

1. loads image/container information;
2. selects **frame 0 only** using `JxlDecoderOptions(0..1)`;
3. applies the existing pixel-count guards;
4. asks MagicScaler to normalize EXIF-style orientation;
5. asks MagicScaler/libjxl to normalize profile output to SDR sRGB;
6. converts the result to BGRA32, preserving 8-bit alpha;
7. copies it into Avalonia and reuses the existing ThumbnailCache/PreviewCache/viewer paths.

The Info panel reports a decoder-managed JPEG XL source profile and sRGB working output.

## Completed automated validation - 7 October 2026

Tested source: **6c2d57e900df134adaec108260b2973a9f1e02a7**.

Passing run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37645201794

Job: **112874047078**.

The test constructs a 64×48 RGBA PNG, with opaque red/green/blue quadrants and a yellow quadrant whose alpha is exactly **64**. Ubuntu `cjxl` encodes this losslessly to `sample.jxl`.

Confirmed:

- `.jxl` is accepted by Browser policy;
- full first-frame decode returns 64×48;
- the opaque red quadrant remains recognisable;
- decoded bottom-right alpha is exactly **64**;
- requested long-edge-32 thumbnail succeeds;
- a real image tab opens at 64×48 visual dimensions;
- Info reports decoder-managed JPEG XL colour and sRGB working output;
- Info shows an explicit JXL metadata-detail limitation instead of a generic parse error;
- Browser→JXL return is a PreviewCache hit with no additional preview decode;
- HEIC still decodes after JPEG XL, validating the shared process-global codec registry;
- all inherited L003c/L003b/L003a/L002 checks remain passing.

The first candidate referenced an internal PhotoSauce frame-options implementation and failed compilation. The passing revision uses the plugin's public `JxlDecoderOptions(0..1)`; coverage was retained.

## Metadata limit

The libjxl plugin can internally expose ICC and EXIF container data, but MetadataExtractor 2.9.3 does not currently provide the existing portable Myoken reader a JPEG XL path. L003d does not duplicate a second metadata parser merely to hide that boundary.

Info therefore shows file, dimensions, normalized orientation/color state and:

```text
JPEG XL EXIF/XMP detail extraction is not exposed in this panel yet.
```

Bridging libjxl's EXIF boxes into the portable metadata model can be a later metadata pass.

## Fidelity and performance limits

**First frame only.** Animated/multi-frame JPEG XL is not played or navigated.

**8-bit output.** The current PhotoSauce libjxl decoder requests UINT8 pixels. Alpha is preserved and tested, but 10/12/16-bit and HDR precision are not retained by this path.

**SDR sRGB output.** Source profiles are normalized to the existing SDR sRGB working space. Monitor-profile conversion and HDR tone mapping remain unimplemented.

**Thumbnail native cost.** The libjxl plugin allocates/materializes its decoded frame before MagicScaler/Myoken performs the thumbnail resize. Small retained thumbnails can therefore still incur larger transient native memory/CPU.

**Preview dependency.** PhotoSauce/libjxl 0.12.0 is still a preview-line dependency. Keep the package pinned and re-run codec tests before any future version update.

Myoken does not expose JPEG XL writing in L003d even though the underlying plugin has encoder support.

## Isolation

Relative to L003c, changes are confined to Myoken.Linux decoder/UI/tests/version, the Linux smoke fixture and Linux CI fixture generation. Windows/WPF, `main`, Myoken.Core and the session schema remain unchanged.

## Nova acceptance

Close Myoken, update the clean `linux/ubuntu-gnome` checkout, run `bash linux/check.sh`, then `bash linux/run.sh`. NuGet supplies the runtime decoder; no `apt install libjxl-tools` is required on Nova.

Useful checks:

1. Open a real `.jxl`; confirm Browser thumbnail and viewer tab.
2. Open **Info**; file/dimensions/color should work and the JXL metadata-detail note should be explicit.
3. If you have a JXL with transparency, compare alpha edges/background with another viewer.
4. Switch JXL → HEIC/AVIF → JXL and ensure all formats continue to decode.
5. Zoom a large JXL to 100% and switch away/back to exercise full decode vs cached preview.
6. Watch RAM/CPU separately on a large JXL; CI correctness uses a tiny 64×48 fixture and is not a large-file performance result.

After native acceptance, the original HEIC/AVIF/JXL baseline codec target is complete. The next iteration should focus on **filesystem/file operations and live folder updates**, or deeper codec fidelity (multi-frame/high-bit-depth/HDR) as a separate track.
