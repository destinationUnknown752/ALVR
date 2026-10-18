param([Parameter(Mandatory = $true)][string]$GameDir)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path
if (!(Test-Path -LiteralPath (Join-Path $gameRoot 'AmanatsuLocation.exe'))) { throw 'AmanatsuLocation.exe was not found.' }
if (Get-Process -Name AmanatsuLocation -ErrorAction SilentlyContinue) { throw 'Close Amanatsu Location before installing.' }
foreach ($relative in @('BepInEx\core\BepInEx.Unity.IL2CPP.dll', 'BepInEx\core\Il2CppInterop.Runtime.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $gameRoot $relative))) { throw "Missing loader dependency: $relative" }
}
$packageDir = Join-Path $PSScriptRoot 'dist\BepInEx\plugins\AmanatsuLocationVR'
$files = @('AmanatsuLocationVR.dll', 'AmanatsuLocationVRNative.dll', 'openvr_api.dll', 'LICENSE.OpenVR.txt', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')
foreach ($name in $files) {
    if (!(Test-Path -LiteralPath (Join-Path $packageDir $name))) { throw "Missing $name; run build.ps1." }
}
$pluginDir = Join-Path $gameRoot 'BepInEx\plugins\AmanatsuLocationVR'
if (Test-Path -LiteralPath $pluginDir) {
    $backupDir = Join-Path $PSScriptRoot ('backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    Copy-Item -LiteralPath $pluginDir -Destination $backupDir -Recurse
}
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $packageDir $name) -Destination $pluginDir -Force }
Write-Output "Installed $pluginDir"
