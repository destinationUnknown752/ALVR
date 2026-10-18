param([Parameter(Mandatory = $true)][string]$GameDir)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name AmanatsuLocation -ErrorAction SilentlyContinue) { throw 'Amanatsu Location is already running.' }
if (Get-Process -Name vorpControl,vorpControl64.dat -ErrorAction SilentlyContinue) { throw 'Exit vorpX before launching Amanatsu Location VR so the injectors do not compete.' }
$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path
$gameExe = Join-Path $gameRoot 'AmanatsuLocation.exe'
if (!(Test-Path -LiteralPath $gameExe)) { throw 'AmanatsuLocation.exe was not found.' }

Start-Process -FilePath $gameExe -WorkingDirectory $gameRoot -ArgumentList '-force-d3d11 -screen-fullscreen 0 -screen-width 1280 -screen-height 720' -PassThru
