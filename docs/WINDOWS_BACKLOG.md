# Windows Backlog

Windows feature tracking, including implementation status and original acceptance requirements.

## WIN-001 Face Detection And Face Canvases With RTX Acceleration

**Status:** Implemented in 0.2.190-0.2.199 (17 October 2026). Added 15 October 2026.
Open **Faces** on the toolbar or **File > Faces reference and canvas**. RTX-only
DirectML detection, crop review, reference preview and new PNG export are
implemented. Version 0.2.200 adds optional recursive scanning with **Include subfolders**.
Version 0.2.203 replaces Photo mosaic in the interface with **Interactive faces**:
click a face to browse its original in the same viewport; Esc restores the canvas.
Missing originals report unavailability without losing the map. Auto columns,
themed folder picking, context-menu access and rebindable F6 are also included.
Version 0.2.204 adds background link refresh and automatic removal/re-layout of
missing originals; this does not rerun detection or change original files.
Versions 0.2.207-0.2.213 add adjustable surroundings/crop shapes, saved interactive
documents, automatic last-canvas session restore, optional ZIP backup inclusion,
folder chooser view modes and a draggable zoomed-canvas overview. See
[feature details and limits](FEATURE_REFERENCE.md#faces-and-canvases).
**Minimum requirement:** Real NVIDIA RTX acceleration is mandatory for acceptance,
not a later optional enhancement. Target hardware includes the RTX 5090.

Port the existing Ubuntu face-detection and canvas feature selectively into the
Windows WPF app. Preserve the existing Windows interface and workflows; do not
merge the whole Ubuntu branch or replace either platform's architecture.

### Functional Scope

- Detect faces locally, present cropped previews for review, remove unwanted
  crops, and open the corresponding original image.
- Create a faces-reference canvas from reviewed crops, preserving proportions.
- Create an interactive face map with links to original images, same-view
  zoom/pan and Esc return; refresh links removes unavailable originals.
- Include canvas dimensions, columns, reference auto-height, preview, fit,
  actual size, zoom/pan, and separate PNG export using original-resolution crops.
- Keep bounded scanning, previews and exports, cancellation, progress, per-file
  errors, source-change checks and non-overwriting publication. Preserve originals.
- This feature detects faces only. It must not identify people, generate identity
  embeddings, name people, or group faces by identity.

### Mandatory RTX Acceptance

- Execute face-detector inference on the selected NVIDIA RTX adapter. Merely
  displaying a CPU-generated result through WPF/GPU composition does not qualify.
- Evaluate the existing DirectML integration first; a compatible CUDA backend is
  also acceptable. Verify the pinned YuNet model and operator support against
  the Windows runtime rather than assuming Ubuntu's newer runtime is interchangeable.
- Confirm actual GPU execution, not only provider registration, an adapter label,
  or a silent CPU execution-provider fallback. Display the active adapter/backend
  and processing state accurately.
- Honor the configured GPU memory budget and DXGI pressure, bound concurrent work,
  support cancellation, and keep browsing/UI responsive. Accelerate image
  preprocessing and canvas composition where practical. File enumeration, metadata,
  decoding or PNG encoding may still use CPU work.
- Report unavailable RTX hardware, incompatible models and memory pressure clearly.
  Any retained CPU fallback must be explicitly labelled as degraded operation;
  a CPU-only implementation does not complete this backlog item.
- Validate on the RTX 5090 with real inference and representative images. Check
  detected boxes/crops and rendered pixels against a CPU reference, including
  rotated faces, overlapping detection tiles, blank images and transparency.
- Cover budget-pressure behavior, cancellation/close, source changes, export
  conflicts, dark/light/narrow layouts and model integrity in Windows tests.

### Reuse And Integration

Reuse the pinned detector model, preprocessing/postprocessing, crop/layout and
canvas algorithms, attribution and relevant regression cases. Adapt Avalonia/Skia
image and window code, Linux file-safety operations and task ownership to the
existing Windows equivalents. Retain local processing, model checksum verification
and the model's notices in dependency restore and packaging.

Reference: Ubuntu alpha.76, commit
`f118ab2ca428efaf4a067b9d04d007f9759b9876`, including the L052 feature and later fixes:

- [Feature notes](https://github.com/kuchida75/myoken-ubuntu-gnome/blob/f118ab2ca428efaf4a067b9d04d007f9759b9876/linux/L052-FACES-CANVASES.md).
- [Detector](https://github.com/kuchida75/myoken-ubuntu-gnome/blob/f118ab2ca428efaf4a067b9d04d007f9759b9876/src/Myoken.Linux/FaceDetection.cs).
- [Crops and canvases](https://github.com/kuchida75/myoken-ubuntu-gnome/blob/f118ab2ca428efaf4a067b9d04d007f9759b9876/src/Myoken.Linux/FaceImages.cs).
- [Faces interface](https://github.com/kuchida75/myoken-ubuntu-gnome/blob/f118ab2ca428efaf4a067b9d04d007f9759b9876/src/Myoken.Linux/FacesWindow.cs).
- [Imaging tests](https://github.com/kuchida75/myoken-ubuntu-gnome/blob/f118ab2ca428efaf4a067b9d04d007f9759b9876/tests/Myoken.Linux.Imaging.Tests/FaceChecks.cs).

These reference links require access to the Ubuntu repository. Inspect its latest
implementation before porting additional fixes; keep unrelated Ubuntu work intact.
