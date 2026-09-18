param([switch]$BuildTests, [string]$BuildNote = 'Rebuild of current source; see CHANGELOG.md.')

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\ZonerInspiredViewer"
$out = Join-Path $root "artifacts\MyokenImageViewer"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (!(Test-Path $csc)) {
    throw "Windows C# compiler not found at $csc"
}

New-Item -ItemType Directory -Force $out | Out-Null
$version = & (Join-Path $PSScriptRoot 'version.ps1') -BuildNote $BuildNote
$buildSucceeded = $false
try {

function Resolve-AssemblyPath($name) {
    $frameworkPath = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\$name"
    if (Test-Path $frameworkPath) {
        return $frameworkPath
    }

    $assemblyRoot = Join-Path $env:WINDIR "Microsoft.NET\assembly"
    $found = Get-ChildItem $assemblyRoot -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*\GAC_64\*" -or $_.FullName -like "*\GAC_MSIL\*" } |
        Select-Object -First 1
    if ($found) {
        return $found.FullName
    }

    throw "Could not resolve $name"
}

$references = @(
    "Microsoft.VisualBasic.dll",
    "PresentationCore.dll",
    "PresentationFramework.dll",
    "ReachFramework.dll",
    "System.Printing.dll",
    "System.Runtime.Serialization.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    "System.Xaml.dll",
    "WindowsBase.dll"
    "netstandard.dll"
)

$referenceArgs = $references | ForEach-Object { "/reference:" + (Resolve-AssemblyPath $_) }
$dependencies = Join-Path $src 'Dependencies'
foreach ($name in @('Microsoft.ML.OnnxRuntime', 'System.Memory', 'System.Buffers', 'System.Numerics.Vectors', 'System.Runtime.CompilerServices.Unsafe', 'Magick.NET.Core', 'Magick.NET-Q8-x64')) {
    $referenceArgs += '/reference:' + (Join-Path $dependencies ($name + '.dll'))
}
function Copy-EnhancementDependencies($destination) {
    Copy-Item -Path "$dependencies\*" -Destination $destination -Recurse -Force
}
Copy-EnhancementDependencies $out
$sources = Get-ChildItem $src -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName }
$exe = Join-Path $out "MyokenImageViewer.exe"
$assets = Join-Path $src 'Assets'
$assetBuildDirectory = Join-Path $root 'artifacts\build-tools'
New-Item -ItemType Directory -Force $assetBuildDirectory | Out-Null
$assetBuilder = Join-Path $assetBuildDirectory 'BuildAssets.exe'
& $csc /nologo /target:exe /reference:System.Drawing.dll /out:$assetBuilder (Join-Path $PSScriptRoot 'BuildAssets.cs')
if ($LASTEXITCODE -ne 0) { throw "Asset compiler failed: $LASTEXITCODE" }
& $assetBuilder shader (Join-Path $assets 'QuickEnhance.hlsl') (Join-Path $assets 'QuickEnhance.ps')
if ($LASTEXITCODE -ne 0) { throw "Shader build failed: $LASTEXITCODE" }
& $assetBuilder shader (Join-Path $assets 'AdvancedEnhance.hlsl') (Join-Path $assets 'AdvancedEnhance.ps')
if ($LASTEXITCODE -ne 0) { throw "Advanced shader build failed: $LASTEXITCODE" }
& $assetBuilder shader (Join-Path $assets 'ManualEnhance.hlsl') (Join-Path $assets 'ManualEnhance.ps')
if ($LASTEXITCODE -ne 0) { throw "Manual enhancement shader build failed: $LASTEXITCODE" }
& $assetBuilder icon (Join-Path $assets 'AppIcon.png') (Join-Path $assets 'AppIcon.ico')
if ($LASTEXITCODE -ne 0) { throw "Icon build failed: $LASTEXITCODE" }
$assetArgs = @(
    '/win32icon:' + (Join-Path $assets 'AppIcon.ico')
    '/resource:' + (Join-Path $assets 'AppIcon.ico') + ',Viewer.AppIcon.ico'
    '/resource:' + (Join-Path $assets 'QuickEnhance.ps') + ',Viewer.QuickEnhance.ps'
    '/resource:' + (Join-Path $assets 'AdvancedEnhance.ps') + ',Viewer.AdvancedEnhance.ps'
    '/resource:' + (Join-Path $assets 'ManualEnhance.ps') + ',Viewer.ManualEnhance.ps'
    '/resource:' + (Join-Path $root 'CHANGELOG.md') + ',Viewer.Changelog.md'
    '/resource:' + (Join-Path $root 'BUILD_HISTORY.json') + ',Viewer.BuildHistory.json'
)

& $csc /nologo /target:winexe /platform:x64 /optimize+ /debug:pdbonly /out:$exe $referenceArgs $assetArgs $sources
if ($LASTEXITCODE -ne 0) {
    throw "Viewer compiler failed: $LASTEXITCODE"
}

Write-Host "Built version $version at $exe"

if ($BuildTests) {
    $testOut = Join-Path $root 'artifacts\tests'
    New-Item -ItemType Directory -Force $testOut | Out-Null
    Copy-EnhancementDependencies $testOut
    $testSource = Get-ChildItem $PSScriptRoot -Filter 'Viewer*Tests.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
    $testReferenceArgs = $referenceArgs
    foreach ($name in @('UIAutomationProvider.dll', 'UIAutomationTypes.dll')) {
        $testReferenceArgs += '/reference:' + (Resolve-AssemblyPath $name)
    }
    & $csc /nologo /debug:full /optimize- /target:exe /platform:x64 /main:ViewerRegressionTests /out:"$testOut\ViewerRegressionTests.exe" $testReferenceArgs $assetArgs $sources $testSource
    if ($LASTEXITCODE -ne 0) { throw "Regression compiler failed: $LASTEXITCODE" }
    & $csc /nologo /target:winexe /platform:x64 /main:ViewerPreviewEntry /out:"$testOut\ViewerPreview.exe" $testReferenceArgs $assetArgs $sources $testSource
    if ($LASTEXITCODE -ne 0) { throw "Preview compiler failed: $LASTEXITCODE" }
    Write-Host "Built regression checks and isolated visual preview in $testOut"
}
$buildSucceeded = $true
}
finally {
    $result = if ($buildSucceeded) { 'Built' } else { 'Failed' }
    & (Join-Path $PSScriptRoot 'version.ps1') -Action Complete -Version $version -Status $result -OutputDirectory $out | Out-Null
}
