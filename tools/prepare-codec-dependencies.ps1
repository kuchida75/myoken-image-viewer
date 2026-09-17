param([string]$DownloadDirectory = (Join-Path $PSScriptRoot '..\artifacts\dependency-downloads'), [switch]$Download)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manifest = Get-Content (Join-Path $PSScriptRoot 'codec-dependencies.json') -Raw | ConvertFrom-Json
$destination = Join-Path $PSScriptRoot '..\src\ZonerInspiredViewer\Dependencies'
New-Item -ItemType Directory -Force $DownloadDirectory, $destination, "$destination\Licenses" | Out-Null
foreach ($item in $manifest) {
    $path = Join-Path $DownloadDirectory $item.File
    if (!(Test-Path -LiteralPath $path)) {
        if (!$Download) { throw "Missing $($item.File). Run with -Download to retrieve pinned codecs." }
        Invoke-WebRequest -UseBasicParsing -Uri $item.Url -OutFile $path
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.SHA256) { throw "Checksum mismatch: $($item.File)" }
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        foreach ($entry in $item.Entries.PSObject.Properties) {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($entry.Name), (Join-Path $destination $entry.Value), $true)
        }
    } finally { $zip.Dispose() }
}
[Reflection.Assembly]::LoadFrom((Join-Path $destination 'Magick.NET.Core.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $destination 'Magick.NET-Q8-x64.dll')) | Out-Null
New-Item -ItemType Directory -Force "$destination\Codecs" | Out-Null
foreach ($config in [ImageMagick.Configuration.ConfigurationFiles]::Default.All) {
    if ($config.FileName -ne 'policy.xml') {
        [IO.File]::WriteAllText((Join-Path "$destination\Codecs" $config.FileName), $config.Data)
    }
}
Write-Host 'Bundled Magick.NET 14.16.0 x64 codecs are ready.'
