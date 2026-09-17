param([string]$DownloadDirectory = (Join-Path $PSScriptRoot '..\artifacts\dependency-downloads'), [switch]$Download)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manifest = Get-Content (Join-Path $PSScriptRoot 'enhancement-dependencies.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force $DownloadDirectory | Out-Null
foreach ($item in $manifest) {
    $path = Join-Path $DownloadDirectory $item.File
    if (!(Test-Path -LiteralPath $path)) {
        if (!$Download) { throw "Missing $($item.File). Run this script with -Download to retrieve pinned dependencies." }
        Invoke-WebRequest -UseBasicParsing -Uri $item.Url -OutFile $path
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.SHA256) { throw "Checksum mismatch: $($item.File)" }
}
$destination = Join-Path $PSScriptRoot '..\src\ZonerInspiredViewer\Dependencies'
New-Item -ItemType Directory -Force $destination, "$destination\Licenses", "$destination\Models" | Out-Null
$packages = @(
    @('ort-managed', 'lib/netstandard2.0/Microsoft.ML.OnnxRuntime.dll', 'LICENSE.txt'),
    @('ort-dml', 'runtimes/win-x64/native/onnxruntime.dll', 'LICENSE'),
    @('microsoft.ai.directml', 'bin/x64-win/DirectML.dll', 'LICENSE.txt'),
    @('system.memory', 'lib/net461/System.Memory.dll', 'LICENSE.TXT'),
    @('system.buffers', 'lib/net461/System.Buffers.dll', 'LICENSE.TXT'),
    @('system.numerics.vectors', 'lib/net46/System.Numerics.Vectors.dll', 'LICENSE.TXT'),
    @('system.runtime.compilerservices.unsafe', 'lib/net461/System.Runtime.CompilerServices.Unsafe.dll', 'LICENSE.TXT')
)
foreach ($package in $packages) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Join-Path $DownloadDirectory ($package[0] + '.nupkg')))
    try {
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($package[1]), (Join-Path $destination (Split-Path $package[1] -Leaf)), $true)
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($package[2]), (Join-Path "$destination\Licenses" ($package[0] + '.txt')), $true)
        $notices = $zip.GetEntry('ThirdPartyNotices.txt')
        if ($notices) { [System.IO.Compression.ZipFileExtensions]::ExtractToFile($notices, (Join-Path "$destination\Licenses" ($package[0] + '-notices.txt')), $true) }
    } finally { $zip.Dispose() }
}
Copy-Item -LiteralPath (Join-Path $DownloadDirectory 'u2netp.onnx'), (Join-Path $DownloadDirectory 'humanseg.onnx'), (Join-Path $DownloadDirectory 'super-resolution-10.onnx') -Destination "$destination\Models" -Force
Get-ChildItem $destination -Recurse -File | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash
