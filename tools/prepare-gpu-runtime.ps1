$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$downloads = Join-Path $root 'artifacts\gpu-runtime'
$destination = Join-Path $root 'src\ZonerInspiredViewer\Dependencies\GpuRuntime'
New-Item -ItemType Directory -Force $downloads,$destination | Out-Null
$packages = @(
    @{ Name = 'cuda_cudart'; Version = '12.9.79'; Hash = '179e9c43b0735ffe67207b3da556eb5a0c50f3047961882b7657d3b822d34ef8' },
    @{ Name = 'libnvjpeg'; Version = '12.4.0.76'; Hash = 'b253241fc88bf30947b8ee068101aca8930960f113d8ee4a9583de021a79ffa1' }
)
foreach ($package in $packages) {
    $name = "$($package.Name)-windows-x86_64-$($package.Version)-archive"
    $zip = Join-Path $downloads "$name.zip"
    if (!(Test-Path -LiteralPath $zip)) {
        Invoke-WebRequest "https://developer.download.nvidia.com/compute/cuda/redist/$($package.Name)/windows-x86_64/$name.zip" -OutFile $zip -UseBasicParsing
    }
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $package.Hash) { throw "Checksum mismatch: $name" }
    Expand-Archive -LiteralPath $zip -DestinationPath $downloads -Force
    $unpacked = Join-Path $downloads $name
    Get-ChildItem -LiteralPath (Join-Path $unpacked 'bin') -Filter '*.dll' | Copy-Item -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $unpacked 'LICENSE') -Destination (Join-Path $destination "$($package.Name)-LICENSE.txt") -Force
}
Get-ChildItem -LiteralPath $destination | Select-Object Name,Length
