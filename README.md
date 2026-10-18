# ALVR

Version 0.1.2 — VR support for **Amanatsu Location** with headset tracking, stereo rendering, character POV, a configurable menu HUD and subtitles. Use the game's keyboard and mouse controls; VR controller input is not supported.

## Requirements

- Windows x64 and Amanatsu Location, Unity 6000.0.65f1 / URP.
- BepInEx 6 for Unity IL2CPP x64, `6.0.0-be.788`, with its .NET 6 runtime.
- SteamVR and an OpenVR-compatible headset connected to SteamVR.
- Direct3D 11.

## Installation

1. Install [BepInEx 6 IL2CPP](https://builds.bepinex.dev/projects/bepinex_be) in the game directory. Launch the game once to generate interop files, then close it.
2. Extract the ALVR release ZIP into the game directory. The three DLLs must be together in `BepInEx/plugins/AmanatsuLocationVR/`:

   ```text
   AmanatsuLocationVR.dll
   AmanatsuLocationVRNative.dll
   openvr_api.dll
   ```

3. Start SteamVR and connect the headset.
4. Launch `AmanatsuLocation.exe` with `-force-d3d11`. Exit competing VR injectors before launching.

BepInEx and SteamVR are installed separately.

To update, close the game and replace the plugin's files. To uninstall, close the game and remove `BepInEx/plugins/AmanatsuLocationVR/`.

## Controls

The game window must have focus.

| Key | Action |
| --- | --- |
| `,` | Enter or leave character POV; cancel suspended POV |
| `.` | Cycle available POV characters |
| `L` | Detach or reattach the animated POV reference |
| `Ctrl+Shift+R` | Reset SteamVR seated position and yaw |
| `Ctrl+Shift+V` | Open or close VR settings |
| `Space` | Hide or show the menu HUD and pause or resume its capture |

Space is ignored while typing or using VR settings. Subtitles remain independent of the menu HUD. Settings can be opened while the HUD is hidden.

If the selected POV character becomes unavailable, the view temporarily returns to the game camera and resumes on the same character when they return. Use comma to cancel this suspended POV.

## Known issues

The selection pointer can be slightly offset, making characters difficult to select. Move the camera closer to the character to make selecting and talking to her easier.

## Settings

Open settings with `Ctrl+Shift+V`. Use **Recenter** to reset the seated view and **Restart VR** to retry after connecting a headset. **Enable VR** turns VR rendering on or off.

The View tab includes desktop mirroring and eye texture flipping. HUD settings include width, distance, vertical offset, curvature and HUD texture flipping. **Always on top** keeps the HUD above scene geometry; turn it off to let closer scene objects cover it. VR settings remain visible on top while open. Subtitles have separate placement and visibility settings.

Use **Save** to store changes. Each setting can be restored to its default. The configuration file is `BepInEx/config/local.amanatsulocationvr.cfg`.

**Diagnostics** manually captures full-resolution images and pose information in the plugin's `diagnostics/` directory and briefly stalls rendering.

## Build and install from source

Install .NET SDK 9, Visual Studio 2022 C++ x64 build tools and a Windows SDK. Install BepInEx in the game and launch it once to generate interop assemblies.

```powershell
./build.ps1 -GameDir 'C:\Games\AmanatsuLocation' -RunSmokeTest
```

If needed, pass `-InteropDir` with the directory containing the generated Unity and game interop DLLs. The default location relative to `-GameDir` is `BepInEx/interop`. The managed plugin targets .NET 6. The native bridge and OpenVR runtime are included in the build output; game and loader assemblies are referenced from the installation and are not redistributed.

The package is built in `dist/BepInEx/plugins/AmanatsuLocationVR/`. With the game closed:

```powershell
./install.ps1 -GameDir 'C:\Games\AmanatsuLocation'
./launch.ps1 -GameDir 'C:\Games\AmanatsuLocation'
```

`install.ps1` backs up an existing plugin directory under `backups/`. Use `disable.ps1 -GameDir 'C:\Games\AmanatsuLocation'` with the game closed to disable the installed plugin.

Run offline managed tests with `./test.ps1`. `-RunSmokeTest` runs the native ABI and D3D11 texture tests without starting a VR scene. These checks do not replace testing in the game and headset.

## Credits

- [BepInEx](https://github.com/BepInEx/BepInEx) — plugin loading.
- [Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) — IL2CPP interoperability.
- [HarmonyX](https://github.com/BepInEx/HarmonyX) — runtime patching.
- [OpenVR](https://github.com/ValveSoftware/openvr) — VR interfaces and runtime integration; its license is included.
- [VRGIN](https://github.com/Eusth/VRGIN) and [HS2VR](https://github.com/OrangeSpork/HS2VR) — inspiration for Unity VR integration.
- [POVX (HS2_PovX)](https://github.com/FairBear/HS2_PovX) — inspiration for the comma POV toggle and period character cycling controls.
