param([Parameter(Mandatory = $true)][string]$GameDir)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name AmanatsuLocation -ErrorAction SilentlyContinue) { throw 'Close Amanatsu Location before disabling the plugin, or turn off Enable VR in settings.' }
$dll = Join-Path $GameDir 'BepInEx\plugins\AmanatsuLocationVR\AmanatsuLocationVR.dll'
if (Test-Path -LiteralPath $dll) {
    Rename-Item -LiteralPath $dll -NewName ('AmanatsuLocationVR.dll.disabled.' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    Write-Output 'Plugin disabled. Run install.ps1 to enable it again.'
}
