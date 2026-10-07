# L003c - HEIC / HEIF / AVIF decoding

Status: compilation, real generated HEIC and AVIF decode/thumbnail/viewer/metadata/cache checks, and every inherited browser/cache/preload/orientation/colour/tab/session regression **passed** on Ubuntu CI. Native Nova acceptance remains pending.

Version: **0.1.0-alpha.10**. Window label: **L003c**.

## Runtime decoder

Myoken.Linux adds:

```text
PhotoSauce.NativeCodecs.Libheif 1.23.5-preview1
```

The plugin wraps libheif and is decode-only for HEIC/AVIF. Its NuGet package carries compatible native binaries for Linux x64/arm64, so users of an already-working Myoken Ubuntu checkout do **not** need to install Ubuntu libheif packages for runtime decoding.

The GitHub Actions workflow does install Ubuntu `libheif-examples`, x265 and AV1 encoder plugins. Those are used only to generate genuine disposable HEIC/AVIF inputs for the integration test. They are not Myoken runtime dependencies.

Extensions recognised by Browser:

- `.heic`
- `.heif`
- `.avif`

## Decode pipeline

For these containers Myoken:

1. reads the primary image information through PhotoSauce/libheif;
2. applies the existing 120-million-input-pixel / 64-million-full-resolution policy;
3. asks MagicScaler to normalise orientation;
4. asks MagicScaler to convert exposed colour profiles to the existing SDR sRGB working space;
5. requests BGRA32 output;
6. copies that bounded output into an Avalonia bitmap;
7. passes it through the same ThumbnailCache, PreviewCache, tab/viewer and session paths as the established formats.

libheif normalises HEIF item orientation by default, so the HEIF-family ViewerImage is already visual-orientation-normalised when it reaches Myoken. The Info panel labels the source as a libheif/MagicScaler-managed container profile and reports sRGB working output.

MetadataExtractor already recognises HEIF/HEIC/AVIF containers. The L003c integration test requires the lazy Info metadata read to complete without an error for both formats.

## Completed automated validation - 7 October 2026

Tested source: **e29f4c53c1fa13f012f939c5a322f0bd2b318a0a**.

Passing run: https://github.com/kuchida75/myoken-image-viewer/actions/runs/37634344732

Job: **112836484365**.

The CI test generated a 64×48 red/green/blue/yellow source image, encoded it into HEIC and AVIF, then exercised the real application decoder.

Confirmed for both HEIC and AVIF:

- genuine container fixture exists and is accepted by Browser policy;
- primary full decode returns the expected 64×48 dimensions;
- decoded pixels contain the expected red first quadrant;
- long-edge-32 thumbnail decoding succeeds;
- output reports the decoder-managed sRGB path;
- a real image tab opens successfully;
- the lazy Info metadata reader completes without reporting a metadata error.

Additionally:

- HEIC Info reports the decoder-managed source and sRGB working output;
- returning to HEIC after another image reuses PreviewCache without a new preview decode;
- all L003a EXIF orientation and L003b Display P3→sRGB assertions remain passing;
- all previous browser/tree, thumbnail/preview cache, preloading, single-row tabs, Smooth/Pixels/100%, zoom/pan and fresh-process session checks remain passing.

The first candidate compiled only after qualifying PhotoSauce's `PixelFormats` name against Avalonia's same-named type. That compile-only defect was not promoted, and all new decoder assertions remained present.

## Important limitations

**Primary still image only.** HEIF collections, sequences, bursts, depth/auxiliary images and multi-image navigation are not exposed yet.

**8-bit RGB fidelity.** The current PhotoSauce libheif pixel source decodes to 8-bit RGB. L003c therefore does not claim HEIF alpha-plane preservation, 10/12-bit fidelity or HDR precision.

**Thumbnail native cost.** PhotoSauce/libheif currently decodes its primary image before MagicScaler performs Myoken's requested resize. A 256-pixel thumbnail does not mean the native codec allocated only a 256-pixel decode. This is especially relevant for large phone photos: retained Myoken cache memory stays bounded, but transient native decode RAM/CPU can be larger.

**SDR sRGB working output.** Source profile handling is normalised through MagicScaler to Myoken's sRGB working space. Monitor-profile output and HDR tone mapping are still outside the implemented pipeline.

**Decode-only and licensing.** Myoken does not write HEIC/AVIF in this milestone. Distribution must include applicable PhotoSauce/libheif/native dependency notices. HEVC patent/licensing requirements can vary by jurisdiction and distribution model.

JPEG XL is deliberately **not** included in L003c.

## Isolation

Relative to L003b, the application change is confined to Myoken.Linux decoder/UI/tests/version, the Linux smoke script and Linux CI fixture generation. Myoken.Core, Windows/WPF, `main` and the session schema are unchanged.

## Nova acceptance

Close Myoken, update the clean `linux/ubuntu-gnome` checkout, run `bash linux/check.sh`, then `bash linux/run.sh`. No new APT codec package should be required; the NuGet restore supplies the decoder package/native runtime.

Useful checks:

1. Open a real iPhone/phone `.heic` photo and confirm Browser thumbnail + image tab.
2. Open Info and check file/container metadata loads instead of an error.
3. Try a real `.avif` if available and repeat thumbnail/viewer/Info checks.
4. Zoom a large HEIC to 100%; verify the full-view transition and then switch tabs/back to exercise cache behavior.
5. Compare thumbnail and viewer orientation/colour for a portrait phone image.
6. Watch responsiveness/RAM with a large HEIC separately; CI proves correctness on small fixtures, not large-phone-photo native decode performance.

If Nova acceptance is sound, the next isolated codec milestone is **JPEG XL**.
