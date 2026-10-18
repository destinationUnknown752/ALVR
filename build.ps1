param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [string]$InteropDir,
    [switch]$RunSmokeTest
)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path
if (!$InteropDir) { $InteropDir = Join-Path $gameRoot 'BepInEx\interop' }
$interopRoot = (Resolve-Path -LiteralPath $InteropDir).Path
& dotnet build (Join-Path $PSScriptRoot 'managed\AmanatsuLocationVR.csproj') -c Release --nologo "-p:GameDir=$gameRoot" "-p:InteropDir=$interopRoot"
if ($LASTEXITCODE -ne 0) { throw 'Managed build failed.' }
& (Join-Path $PSScriptRoot 'native\build.ps1') -RunSmokeTest:$RunSmokeTest
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
$packageDir = Join-Path $PSScriptRoot 'dist\BepInEx\plugins\AmanatsuLocationVR'
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build\managed\AmanatsuLocationVR.dll') -Destination $packageDir
foreach ($fileName in @('AmanatsuLocationVRNative.dll', 'openvr_api.dll', 'LICENSE.OpenVR.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "native\build\$fileName") -Destination $packageDir
}
foreach ($document in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $packageDir
}
Write-Output "Built $packageDir"
