# Build From The Source Repository

## Windows Baseline

Myoken Image Viewer is a Windows x64 C# / WPF application targeting .NET
Framework 4.8. Version **0.2.167** introduces the Myoken name without changing
the Windows feature set. The initial Zen Image Viewer source baseline remains
tagged **v0.2.166**. Ubuntu support is not implemented in this baseline.

Git contains source, tests, icon assets, a documented HEIC test fixture,
third-party notices and pinned dependency manifests. Unlike the existing local
full package, a fresh clone does **not** contain runtime DLLs, model weights or
an executable. References elsewhere in the README to bundled/offline builds
describe the full package or an already restored working tree.

## Prerequisites

- Windows 10/11 x64 with .NET Framework 4.8 and Windows PowerShell 5.1.
- Git, and network access for the initial dependency restore.
- For Visual Studio project builds, the .NET Framework 4.8 developer/targeting
  pack. The supplied script uses the installed Windows framework compiler.
- An interactive Windows desktop for WPF UI tests. GPU-specific checks need
  compatible hardware/drivers; not every test runs on every machine.

## Restore Dependencies

Run from the repository root after reviewing the
[third-party terms](../THIRD_PARTY_NOTICES.md):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\prepare-codec-dependencies.ps1 -Download
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\prepare-enhancement-dependencies.ps1 -Download
```

These existing scripts verify SHA-256 hashes before extracting/copying pinned
downloads. The enhancement restore includes ONNX Runtime, DirectML, supporting
assemblies and the basic super-resolution / segmentation models. Downloads go
under `artifacts`; restored dependencies go under
`src/ZonerInspiredViewer/Dependencies`. Both binary locations are excluded
from Git. No dependencies are downloaded by the application at runtime.

Optional NVIDIA JPEG acceleration uses separately licensed CUDA/nvJPEG files:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\prepare-gpu-runtime.ps1
```

Without these optional files, the viewer retains its CPU JPEG decoding path.

## Build And Run

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build.ps1 -BuildTests -BuildNote "Local source build"
.\artifacts\MyokenImageViewer\MyokenImageViewer.exe
```

The build script compiles shader resources and the icon, builds the app and
optionally its regression executables. Each build deliberately reserves the
next version number and updates `version.json`, `BuildInfo.cs` and the build
journals, even if compilation fails. Do not build in a checkout that you need
to keep byte-for-byte unchanged. Build in a separate clone/worktree instead.

The Visual Studio project is `src/ZonerInspiredViewer/MyokenImageViewer.csproj`.
The legacy source namespace and user-data identifiers are intentionally retained
for compatibility; they are not the application's display name.

Focused checks after a test build:

```powershell
.\artifacts\tests\ViewerRegressionTests.exe --natural-sort-only
.\artifacts\tests\ViewerRegressionTests.exe --branding-only
.\artifacts\tests\ViewerRegressionTests.exe --workflow-only
.\artifacts\tests\ViewerRegressionTests.exe --tab-stacks-only
```

Tests create isolated profiles and generated fixtures. Some suites require
optional models, GPU hardware, or an unlocked desktop; a source build alone
does not establish that those suites pass. See the main README for individual
test switches and known feature limitations.

## Optional Larger AI Models

Real-ESRGAN, Real-HAT and SwinIR weights are deliberately absent from Git.
`tools/upscale-models.json` records upstream checkpoint URLs, expected hashes,
model IDs, output filenames and provenance. Obtain the selected checkpoint
from that recorded source into `artifacts/dependency-downloads` after reviewing
its terms. The export script verifies its hash before loading it.

The existing export tool records this toolchain: Python 3.12,
PyTorch 2.6.0 CPU, torchvision 0.21.0 CPU, spandrel 0.4.1,
ONNX 1.17.0 and NumPy 1.26.4. Use an isolated environment with those versions;
Python is not needed to run the exported models in the viewer.

Example, with that environment active and the recorded SwinIR checkpoint
already downloaded:

```powershell
python .\tools\export-upscale-models.py --model swinir
```

Use `--model realesrgan` or `--model realhat` for the other models. The output
directory defaults to `src/ZonerInspiredViewer/Dependencies/Models`; rebuild
after adding a model so it is copied beside the executable. Compare the output
SHA-256 to the manifest. The viewer checks hashes too; do not bypass those
checks to accept an unexplained mismatch. Checkpoint parity fixtures are
emitted under `artifacts/dependency-downloads` for the model tests.

SUPIR remains an optional separate local installation, not a Git dependency.
Its adapter is included, but actual SUPIR inference is not validated in this
baseline. Review its code and all model licenses separately.
