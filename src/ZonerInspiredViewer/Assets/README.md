# App Icon

`AppIcon.png` is the high-resolution Myoken Image Viewer shutter master generated with the built-in image-generation tool. The simplified teal/ivory silhouette replaces the original multicolor metallic icon to remain readable at title-bar and taskbar sizes. No lettering, fine texture or proprietary branding is used.

`AppIcon.ico` and `AppIcon-256.png` are format/size conversions produced by `tools/BuildAssets.cs`, retaining transparency. The ICO contains 16, 24, 32, 48, 64, 128 and 256 pixel frames; both the executable and WPF window embed the multi-size ICO. Run `ViewerRegressionTests.exe --branding-only` to verify identity, transparency, native icon resources and generated dark/light size previews.

Final generation prompt:

> Use case: logo-brand. Asset type: high-resolution Windows desktop application icon for an independent image viewer. Create one polished, original camera aperture/shutter emblem, centered and front-facing. Six clean interlocking shutter blades around a crisp hexagonal opening, bold enough to read at 16 pixels, with restrained dimensional bevels and subtly luminous edges. Professional photography-tool character. A balanced mix of teal, emerald, silver and a small warm coral accent on the blades, a charcoal central opening. No camera body, no letters, no text, no watermark, no proprietary logos, no surrounding tile or square background. Isolated emblem occupying about 86 percent of a square canvas; genuinely transparent alpha background outside the circular shutter, not a checkerboard. High-resolution 1024 by 1024 or larger. One icon only, no mockup or contact sheet.

# Quick Enhance Shader

`QuickEnhance.hlsl` is the editable source. `QuickEnhance.ps` is compiled for WPF's PS 2.0 pipeline by the build script and embedded into the executable. Its exposure curve preserves black and white endpoints, while luminance sharpening is thresholded and bounded to reduce noise and halos. Alpha remains unchanged.
