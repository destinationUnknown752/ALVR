param([switch]$RunSmokeTest)
$ErrorActionPreference = 'Stop'
$nativeRoot = $PSScriptRoot
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsRoot = & $vswherePath -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsRoot) { throw 'Visual Studio C++ x64 build tools are required.' }
Import-Module (Join-Path $vsRoot 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $vsRoot -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'
$nativeOut = Join-Path $nativeRoot 'build'
New-Item -ItemType Directory -Force -Path $nativeOut | Out-Null
$includeDir = Join-Path $nativeRoot 'vendor\openvr\headers'
$importLib = Join-Path $nativeRoot 'vendor\openvr\lib\win64\openvr_api.lib'
$sourceFile = Join-Path $nativeRoot 'AmanatsuLocationVRNative.cpp'
$outputDll = Join-Path $nativeOut 'AmanatsuLocationVRNative.dll'
& cl.exe /nologo /std:c++17 /EHsc /W4 /WX /O2 /MT /Brepro /LD /utf-8 "/I$includeDir" $sourceFile "/Fo$nativeOut\AmanatsuLocationVRNative.obj" /link $importLib "/OUT:$outputDll" "/IMPLIB:$nativeOut\AmanatsuLocationVRNative.lib" /MACHINE:X64 /DYNAMICBASE /NXCOMPAT /Brepro /DEBUG:NONE
if ($LASTEXITCODE -ne 0) { throw "Native build failed: $LASTEXITCODE" }
Copy-Item -LiteralPath (Join-Path $nativeRoot 'vendor\openvr\bin\win64\openvr_api.dll') -Destination $nativeOut -Force
Copy-Item -LiteralPath (Join-Path $nativeRoot 'vendor\openvr\LICENSE') -Destination (Join-Path $nativeOut 'LICENSE.OpenVR.txt') -Force
if ($RunSmokeTest) {
    $testSource = Join-Path $nativeRoot 'smoke.cpp'
    & cl.exe /nologo /std:c++17 /EHsc /W4 /WX /O2 /MT /Brepro /utf-8 $testSource "/Fo$nativeOut\smoke.obj" /link d3d11.lib "/OUT:$nativeOut\smoke.exe" /MACHINE:X64 /Brepro /DEBUG:NONE
    if ($LASTEXITCODE -ne 0) { throw "Smoke test build failed: $LASTEXITCODE" }
    & (Join-Path $nativeOut 'smoke.exe')
    if ($LASTEXITCODE -ne 0) { throw "Smoke test failed: $LASTEXITCODE" }
}
Get-Item -LiteralPath $outputDll | Select-Object FullName,Length,LastWriteTime
