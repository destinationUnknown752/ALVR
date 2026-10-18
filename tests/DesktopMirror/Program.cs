using AmanatsuLocationVR;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using BepInEx.Logging;

int checks = 0;
void Check(bool condition, string message) { ++checks; if (!condition) throw new Exception(message); }
int Writes() => UnityEngine.Object.All.OfType<Canvas>().Sum(x => x.Writes) + UnityEngine.Object.All.OfType<RawImage>().Sum(x => x.Writes)
    + UnityEngine.Object.All.OfType<RectTransform>().Sum(x => x.Writes);
Canvas Canvas(string name) => UnityEngine.Object.All.OfType<Canvas>().Single(x => x.gameObject.name == name && x.Alive);
var sourceObject = new GameObject("Source"); var source = sourceObject.AddComponent<Camera>();
var hd = sourceObject.AddComponent<UniversalAdditionalCameraData>();
var eye = new RenderTexture(2000, 2200); var hud = new RenderTexture(1600, 900); var captions = new RenderTexture(1024, 256);
using var mirror = new DesktopMirror(new ManualLogSource());
mirror.SetHudTexture(hud); mirror.SetSubtitleTexture(captions); mirror.Prepare(source, eye, true); mirror.RestoreSource();
int writes = Writes(), lookups = sourceObject.ComponentLookups;
for (int frame = 0; frame < 12; ++frame)
{
    mirror.SetHudTexture(hud); mirror.SetSubtitleTexture(captions); mirror.Prepare(source, eye, true); mirror.RestoreSource();
}
Check(Writes() == writes, "Unchanged frames rewrote owned canvas/texture/geometry state.");
Check(sourceObject.ComponentLookups == lookups, "Unchanged source repeated URP component discovery.");
Check(source.cullingMask == -1 && Canvas("AmanatsuLocationVR_DesktopMirror").enabled, "Normal frame did not restore source while retaining mirror.");
Screen.width = 1920; Screen.height = 1080;
mirror.SetSubtitleTexture(captions); mirror.Prepare(source, eye, true); mirror.RestoreSource();
Check(Writes() == writes + 2, "Screen resize must update only eye crop and subtitle rectangle.");
var popup = new GameObject("Immediate popup").AddComponent<Canvas>(); popup.renderMode = RenderMode.ScreenSpaceCamera; popup.worldCamera = source;
mirror.Prepare(source, eye, true);
Check(!Canvas("AmanatsuLocationVR_DesktopMirror").enabled && source.cullingMask == -1, "New camera-space menu was hidden by a stale obstruction cache.");
popup.enabled = false; mirror.Prepare(source, eye, true);
Check(Canvas("AmanatsuLocationVR_DesktopMirror").enabled && source.cullingMask == 0, "Mirror did not immediately recover after menu closed.");
mirror.RestoreSource(); UnityEngine.Object.Destroy(hd); hd = sourceObject.AddComponent<UniversalAdditionalCameraData>();
mirror.Prepare(source, eye, true);
Check(source.cullingMask == 0 && sourceObject.ComponentLookups == lookups + 1, "Replaced URP component was not rediscovered.");
mirror.RestoreSource(); hd.renderType = CameraRenderType.Overlay; mirror.Prepare(source, eye, true);
Check(!Canvas("AmanatsuLocationVR_DesktopMirror").enabled && source.cullingMask == -1, "Overlay camera did not pause mirroring.");
hd.renderType = CameraRenderType.Base; source.cullingMask = 0; mirror.Prepare(source, eye, true); mirror.RestoreSource();
Check(source.cullingMask == 0, "Mirror overwrote pre-existing empty culling mask.");
mirror.SetHudTexture(null); mirror.SetSubtitleTexture(null); mirror.Prepare(source, null, false);
writes = Writes(); mirror.SetHudTexture(null); mirror.SetSubtitleTexture(null); mirror.Prepare(source, null, false);
Check(Writes() == writes, "Already-hidden copies repeated clear/disable writes.");
Check(!Canvas("AmanatsuLocationVR_DesktopHud").enabled && !Canvas("AmanatsuLocationVR_DesktopSubtitles").enabled, "Hidden UI copies remained enabled.");
source.cullingMask = 12345; source.clearFlags = CameraClearFlags.Skybox; source.backgroundColor = Color.white;
hd.renderPostProcessing = true; hd.renderShadows = true;
hd.requiresDepthOption = CameraOverrideOption.On; hd.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
hd.antialiasing = AntialiasingMode.TemporalAntiAliasing;
mirror.Prepare(source, eye, true);
Check(source.enabled && source.cullingMask == 0 && source.clearFlags == CameraClearFlags.SolidColor && source.backgroundColor == Color.black,
    "Source must stay discoverable but suppress geometry and skybox.");
Check(!hd.renderPostProcessing && !hd.renderShadows && hd.requiresDepthOption == CameraOverrideOption.Off &&
    hd.requiresColorOption == CameraOverrideOption.Off && hd.antialiasing == AntialiasingMode.None, "World-only pipeline work was retained.");
mirror.RestoreSource();
Check(source.cullingMask == 12345 && source.clearFlags == CameraClearFlags.Skybox && source.backgroundColor == Color.white,
    "Original camera state was not restored.");
Check(hd.renderPostProcessing && hd.renderShadows && hd.requiresDepthOption == CameraOverrideOption.On &&
    hd.requiresColorOption == CameraOverrideOption.UsePipelineSettings && hd.antialiasing == AntialiasingMode.TemporalAntiAliasing,
    "Original URP pipeline settings were not restored.");
mirror.Prepare(source, eye, true);
source.cullingMask = 17; source.clearFlags = CameraClearFlags.Nothing; source.backgroundColor = new Color(1,0,0,1);
hd.requiresDepthOption = CameraOverrideOption.UsePipelineSettings; hd.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
mirror.RestoreSource();
Check(source.cullingMask == 17 && source.clearFlags == CameraClearFlags.Nothing && source.backgroundColor == new Color(1,0,0,1),
    "A subsequent game camera change was overwritten during restoration.");
Check(hd.requiresDepthOption == CameraOverrideOption.UsePipelineSettings && hd.antialiasing == AntialiasingMode.FastApproximateAntialiasing,
    "A subsequent game URP change was overwritten during restoration.");
source.targetTexture = new RenderTexture(100, 100);
mirror.Prepare(source, eye, true);
Check(source.cullingMask == 17 && !Canvas("AmanatsuLocationVR_DesktopMirror").enabled, "Photo/offscreen camera was suppressed.");
source.targetTexture = null;
mirror.Prepare(source, eye, true);
mirror.Dispose();
Check(source.cullingMask == 17 && hd.renderPostProcessing && hd.renderShadows, "Disposal failed to restore a currently suppressed source.");
Console.WriteLine($"PASS: {checks} mirror caching, resize and immediate-UI fallback assertions.");
