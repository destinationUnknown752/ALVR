$ErrorActionPreference = 'Stop'
$projects = @('StereoMath', 'CameraFrame/CameraFrame', 'DesktopMirror/DesktopMirror', 'HudPointer/HudPointer', 'HudSurface/HudSurface', 'PovLifecycle/PovLifecycle', 'SubtitleSource/SubtitleSource', 'SubtitlePresentation/SubtitlePresentation', 'TrackingReference/TrackingReference', 'WatcherCamera/WatcherCamera', 'WorldPointer/WorldPointer')
foreach ($project in $projects) {
    & dotnet run --project (Join-Path $PSScriptRoot "tests/$project.Tests.csproj") -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
}
& (Join-Path $PSScriptRoot 'tests/SettingsMenu/verify-spec.ps1')
