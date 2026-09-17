# Third-Party Components And Distribution Review

A license selected for Zen Image Viewer's original code does not relicense
third-party software, model weights, codec configuration or fixtures.
This inventory is not a legal opinion or a completed binary-release audit.

## Source Repository

Git deliberately excludes downloaded DLLs, ONNX/checkpoint files, package
archives, executable builds, personal photos, profiles and caches. Dependency
versions, SHA-256 hashes, restore/export tools and existing upstream notices
remain available. Review upstream terms before downloading or redistributing.

| Component | Recorded terms and location |
| --- | --- |
| Magick.NET 14.16.0 | Apache-2.0 for Magick.NET; ImageMagick and native dependencies have separate terms in `src/ZonerInspiredViewer/Dependencies/Licenses/Magick.NET-notices.txt`. |
| Native HEIF/HEVC codecs | The bundled package notices identify libheif and libde265 as LGPL. Other LGPL components also appear in those notices. Do not infer that the native package is entirely Apache-2.0. |
| ONNX Runtime 1.20.1 | MIT and upstream third-party notices in `Dependencies/Licenses/ort-*.txt`. |
| DirectML 1.15.2 | Microsoft Software License Terms and notices in `Dependencies/Licenses/microsoft.ai.directml*.txt`; not covered by ONNX Runtime's MIT license. |
| Supporting System.* assemblies | Individual upstream terms in `Dependencies/Licenses/system.*.txt`. |
| CUDA runtime / nvJPEG | NVIDIA terms in `Dependencies/GpuRuntime/*-LICENSE.txt`; restored by `tools/prepare-gpu-runtime.ps1`, not relicensed by this project. |
| Real-ESRGAN | Upstream BSD-3-Clause notice in `Dependencies/Licenses/Real-ESRGAN.txt`; checkpoint provenance/hashes in `tools/upscale-models.json`. |
| Real-HAT / SwinIR | Upstream Apache-2.0 notices in `Dependencies/Licenses/HAT.txt` and `SwinIR.txt`; checkpoint provenance/hashes in `tools/upscale-models.json`. |
| Basic ONNX super-resolution | ONNX Model Zoo repository Apache-2.0 notice in `Dependencies/Licenses/ONNX-super-resolution.txt`; review model-specific terms as well. |
| PPHumanSeg | OpenCV Zoo model-directory Apache-2.0 terms, retained in `Dependencies/Licenses/PPHumanSeg.txt`. |
| U-2-NetP | Upstream code Apache-2.0 notice in `Dependencies/Licenses/U-2-Net.txt`. The rembg conversion release lacks a separate model-specific provenance/license file; checkpoint redistribution remains a review item. |
| Spandrel | Export-time tool only, notice in `Dependencies/Licenses/Spandrel.txt`; Python export dependencies are not shipped in Git. |
| SUPIR | Only the viewer's optional adapter is included. SUPIR, SDXL and other required checkpoints must be installed and reviewed separately. |
| HEIC regression fixture | Documented upstream sample, hash and MIT examples license in `tools/Fixtures/README.md` and `libheif-examples-COPYING.txt`. |
| Codec XML resources | Upstream ImageMagick resources plus the viewer's raster-only policy; preserve their notices and package attribution. |

In this table, `Dependencies` means
`src/ZonerInspiredViewer/Dependencies`. Full provenance is in that folder's
[README](src/ZonerInspiredViewer/Dependencies/README.md). Do not replace the
upstream license files with the app's license.

## Before Publishing An Executable Or Model Pack

1. Inventory the exact files in the release, including transitively bundled
   native libraries. Retain all required licenses, copyright notices and
   source/change notices.
2. Resolve LGPL obligations for the actual native build/linking arrangement,
   including corresponding source and applicable relinking/replacement
   materials. Copying a license text alone is not a complete compliance check.
3. Verify NVIDIA and Microsoft redistribution conditions for the specific
   runtime versions and package contents.
4. Confirm model-weight distribution rights independently of repository code
   licenses. In particular, verify the U-2-NetP conversion provenance and the
   mirrored Real-HAT checkpoint before including them in a public model pack.
5. Review codec patent considerations for intended distribution jurisdictions
   and commercial use. An app's Apache patent grant is not a blanket codec
   patent license.
6. Keep user photos, session/backups, machine paths, caches and private test
   data out of any release. Document tests actually run on the release files.

The existing local Windows package has not been certified by this publication
preparation for public binary redistribution. Source publication and a binary
release are separate decisions.

Primary license references: [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0),
[MIT](https://opensource.org/license/mit),
[libheif](https://github.com/strukturag/libheif/blob/master/COPYING).
