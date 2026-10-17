param([string]$Revision)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
Push-Location $repo
try {
    if ($Revision) { $listing = @(& git ls-tree -r $Revision) }
    else { $listing = @(& git ls-files --stage) }
    if ($LASTEXITCODE -ne 0 -or $listing.Count -eq 0) { throw 'Unable to read the publication snapshot' }
    $rootFiles = @('.gitignore', '.gitattributes', 'LICENSE', 'NOTICE', 'THIRD_PARTY_NOTICES.md',
        'README.md', 'CHANGELOG.md', 'BUILD_HISTORY.md', 'BUILD_HISTORY.json', 'version.json')
    $icons = @('src/ZonerInspiredViewer/Assets/AppIcon.ico', 'src/ZonerInspiredViewer/Assets/AppIcon.png',
        'src/ZonerInspiredViewer/Assets/AppIcon-256.png')
    $fixtures = @{
        'tools/Fixtures/astronaut.png' = '88431CD9653CCD539741B555FB0A46B61558B301D4110412B5BC28B5E3EA6CB5'
        'tools/Fixtures/libheif-example.heic' = '7F8B363E4936C0666A25F64F3A92FDA10BD8E5453BE4592530B65A55DD98F3F2'
    }
    $fixtureNotices = @('tools/Fixtures/README.md', 'tools/Fixtures/astronaut-NOTICE.txt', 'tools/Fixtures/libheif-examples-COPYING.txt')
    $secretPattern = '(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{20,}|AKIA[0-9A-Z]{16}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)'
    $homePattern = '(?i)\b[A-Z]:[\\/]Users[\\/][^\\/\s<>"'']+'
    $forbidden = '(?i)(^|/)(sources|artifacts|\.git|profiles?|sessions|instances|thumbs|search-index|backups|face-canvases|fixture-data|\.viewer-[^/]*-backups|\.zen-batch-backups)(/|$)|(^|/)(session(-[^/]*)?\.json|browser\.json|viewer-backup[^/]*\.json|onnxruntime_profile[^/]*\.json|\.env([.]|$))$|\.(dll|exe|pdb|zip|7z|onnx|pth|pt|ckpt|safetensors|f32|nupkg|sqlite[^/]*|db|pem|pfx|p12|key|myokenfaces|log|tmp|bak)$'
    $count = 0; $bytes = 0; $photographs = 0
    foreach ($entry in $listing) {
        $pattern = if ($Revision) { '^(\d{6}) blob ([a-f0-9]+)\t(.+)$' } else { '^(\d{6}) ([a-f0-9]+) 0\t(.+)$' }
        if ($entry -notmatch $pattern) { throw 'Unexpected Git entry, unresolved merge or submodule in publication snapshot' }
        $mode = $Matches[1]; $blob = $Matches[2]; $path = $Matches[3]
        if ($mode -ne '100644' -and $mode -ne '100755') { throw "Linked/non-file entry is not allowed: $path" }
        if ($path -match $forbidden) { throw "Private/generated payload is forbidden: $path" }
        $allowed = $rootFiles -contains $path -or $icons -contains $path -or $fixtures.ContainsKey($path) -or $fixtureNotices -contains $path
        if ($path -match '^docs/[^/]+\.md$|^tools/[^/]+\.(cs|ps1|py|json)$|^src/ZonerInspiredViewer/[^/]+\.(cs|csproj)$|^src/ZonerInspiredViewer/Assets/[^/]+\.(hlsl|md)$|^src/ZonerInspiredViewer/Dependencies/(README\.md|Codecs/[^/]+\.xml|Licenses/[^/]+\.txt|GpuRuntime/[^/]+\.txt|Tools/viewer-supir\.py)$') { $allowed = $true }
        if (-not $allowed) { throw "File is outside the reviewed source allowlist: $path" }
        $fullPath = [IO.Path]::GetFullPath((Join-Path $repo $path))
        if (-not $fullPath.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escapes repository' }
        $file = Get-Item -LiteralPath $fullPath -Force
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked source file is forbidden: $path" }
        for ($ancestor = $file.Directory; $ancestor -and $ancestor.FullName -ne $repo; $ancestor = $ancestor.Parent) {
            if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked source path is forbidden: $path" }
        }
        if ($file.Length -gt 5MB) { throw "Unexpectedly large source file: $path" }
        $workingBlob = (& git hash-object "--path=$path" -- $fullPath).Trim()
        if ($LASTEXITCODE -ne 0 -or $workingBlob -ne $blob) { throw "Snapshot differs from audited working file: $path" }
        if ($fixtures.ContainsKey($path)) {
            if ((Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash -ne $fixtures[$path]) { throw "Public fixture checksum differs: $path" }
            $photographs++
        }
        elseif ($icons -notcontains $path) {
            $text = [IO.File]::ReadAllText($fullPath)
            if ($text -match $secretPattern) { throw "Potential credential found; inspect locally: $path" }
            if ($text -match $homePattern) { throw "Personal Windows home path found: $path" }
        }
        $count++; $bytes += $file.Length
    }
    $version = Get-Content -LiteralPath version.json -Raw | ConvertFrom-Json
    $versionText = '{0}.{1}.{2}' -f $version.Major, $version.Minor, $version.Build
    $buildInfo = Get-Content -LiteralPath src/ZonerInspiredViewer/BuildInfo.cs -Raw
    if ($buildInfo -notmatch ('public const string Version = "' + [regex]::Escape($versionText) + '";')) { throw 'App and source version metadata differ' }
    $snapshot = 'Git index'; $tree = $null
    if ($Revision) {
        $snapshot = (& git rev-parse $Revision).Trim()
        $tree = (& git rev-parse "${Revision}^{tree}").Trim()
    }
    [pscustomobject][ordered]@{
        Snapshot = $snapshot; Tree = $tree; Version = $versionText; FilesChecked = $count; SourceBytes = $bytes
        PinnedPublicPhotoFixtures = $photographs; PrivateProfileOrCacheIncluded = $false
        RuntimeBinariesOrModelWeightsIncluded = $false; CredentialAndHomePathChecks = 'Passed'
        NormalWindowsProfileReadOrModified = $false
    } | ConvertTo-Json
}
finally { Pop-Location }
