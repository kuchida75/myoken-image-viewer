# Linux application scope

Read ../../linux/AGENTS.md and ../../linux/README.md before changes. Preserve the Windows v0.2.167 baseline and WPF sources. Work in a separate Linux checkout/branch. Use bash linux/check.sh from the repository root. Do not run tools/build.ps1. Keep UI, decoding and Linux session I/O here rather than introducing platform dependencies into Myoken.Core. Report compilation, headless tests and real GNOME validation separately.


L003c: HEIC/HEIF/AVIF support lives in HeifAvifDecoder and must remain a Linux application dependency. PhotoSauce's NuGet package supplies runtime native libheif binaries; do not make host APT libheif packages a user prerequisite. CI encoder packages are fixture-generation only. Preserve primary-image-only, 8-bit-RGB and native-full-decode-before-resize limitations in reporting. All HEIF-family output must enter the existing sRGB working/cache paths; keep JPEG XL separate.


L003d: JPEG XL support lives in JxlDecoder and AdvancedCodecRegistry. PhotoSauce CodecManager is global; register libheif + libjxl together and regression-test coexistence. Runtime libjxl comes from the NuGet package; Ubuntu libjxl-tools is CI fixture-generation only. Preserve first-frame-only JxlDecoderOptions(0..1), 8-bit-output/HDR limitations, alpha preservation, full-native-frame-before-thumbnail caveat and explicit lack of detailed JXL EXIF/XMP panel extraction.


L004a: MainWindow owns file mutation policy and current-folder reconciliation; FolderWatcher only batches filesystem events. External rename must update the existing DocumentTab/ImageViewer path rather than create a duplicate viewer. Per-path cache invalidation is preferred over global clears. Delete is intentionally permanent and requires explicit UI confirmation; never describe it as Trash. Same-folder rename only in this pass; move/Trash remain separate.
