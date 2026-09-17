GPU JPEG runtime
================

Bundled Windows x64 redistributables from NVIDIA CUDA 12.9 Update 1:
  CUDA Runtime 12.9.79: cudart64_12.dll
  nvJPEG 12.4.0.76: nvjpeg64_12.dll

The original NVIDIA license is included separately for each package.
No system-wide toolkit installation, driver modification or runtime download is performed by the viewer.
The NVIDIA display driver must support this CUDA runtime; incompatible, missing or non-NVIDIA devices use CPU decoding.

Provenance:
https://developer.download.nvidia.com/compute/cuda/redist/redistrib_12.9.1.json
https://docs.nvidia.com/cuda/nvjpeg/

Package SHA-256:
cuda_cudart-windows-x86_64-12.9.79-archive.zip
179e9c43b0735ffe67207b3da556eb5a0c50f3047961882b7657d3b822d34ef8
libnvjpeg-windows-x86_64-12.4.0.76-archive.zip
b253241fc88bf30947b8ee068101aca8930960f113d8ee4a9583de021a79ffa1

tools/prepare-gpu-runtime.ps1 reproduces the checked download and local extraction.
nvJPEG uses hybrid CPU/GPU decoding; this is not a claim of a dedicated JPEG hardware engine on GeForce.
Decoded BGR data is written into a CUDA-registered D3D11 buffer and converted to the shared display texture on GPU.
One readback supplies the existing CPU-side editing/export/navigator workflows; ordinary display/navigation does not read back the texture.
