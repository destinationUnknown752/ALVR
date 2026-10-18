using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace AmanatsuLocationVR
{
    [BepInPlugin(Id, "Amanatsu Location VR", "0.1.4")]
    [BepInProcess("AmanatsuLocation.exe")]
    public sealed class Plugin : BasePlugin
    {
        public const string Id = "local.amanatsulocationvr";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled, HeadTracking, PositionTracking, FlipY, MirrorDesktop, HudEnabled, HudFlipY, DisablePovxMouseLook;
        internal ConfigEntry<bool> HidePovHead, HidePovAccessories, HudWorldPointer, HudAlwaysOnTop;
        internal ConfigEntry<bool> SubtitlesEnabled, SubtitleVisible;
        internal ConfigEntry<float> RenderScale, UnitsPerMeter, NearClip, PovNearClip, HudWidth, HudDistance;
        internal ConfigEntry<float> SubtitleWidth, SubtitleDistance, SubtitleVerticalOffset;
        internal ConfigEntry<int> MaxDimension;
        internal ConfigEntry<string> CameraName;
        internal ConfigEntry<bool> PinPositionWhileAttached, Stabilize, SubtitleAtTop;
        internal ConfigEntry<float> HudVerticalOffset, HudCurvature, SubtitleHorizontalOffset;
        internal ConfigEntry<float> PositionTau, RotationTau, StabilizationStrength;
        internal ConfigEntry<float> PovOffsetX, PovOffsetY, PovOffsetZ, PovPitch, PovYaw, PovRoll;
        internal ConfigEntry<int> SubtitleFontSize, DetachedTransition;
        internal IReadOnlyList<SettingSpec> MenuSettings;
        public override void Load()
        {
            Instance = this;
            Config.SaveOnConfigSet = false;
            Enabled = Config.Bind("General", "Enabled", true, "Enable stereo cameras and SteamVR. Change in Ctrl+Shift+V settings; Restart VR retries initialization.");
            HeadTracking = Config.Bind("Tracking", "Rotation", true, "Apply headset rotation in SteamVR's level seated reference. Ctrl+Shift+R or Recenter in settings resets runtime position/yaw without discarding physical pitch or roll.");
            PositionTracking = Config.Bind("Tracking", "Position", true, "Apply headset translation relative to recenter pose.");
            UnitsPerMeter = Config.Bind("Tracking", "UnitsPerMeter", 10f, "Unity units per physical metre. Scales IPD and head translation together.");
            RenderScale = Config.Bind("Rendering", "RenderScale", 1f, "Multiplier on SteamVR recommended texture dimensions. 1 uses the full recommendation. Restart to change.");
            MaxDimension = Config.Bind("Rendering", "MaxDimension", 0, "Optional maximum eye texture dimension. 0 uses the full SteamVR recommendation with no cap. Restart to change.");
            NearClip = Config.Bind("Rendering", "NearClip", 0.05f, "Near clipping distance in physical metres.");
            PovNearClip = Config.Bind("Rendering", "PovNearClip", 0.01f, "POV near clip in physical metres. Default 0.01 m at the selected world scale. Does not move the camera.");
            FlipY = Config.Bind("Rendering", "FlipY", true, "Flip textures vertically for compositor submission. Unity URP compositor orientation. Adjustable live in the VR settings View tab; normally leave enabled.");
            MirrorDesktop = Config.Bind("Rendering", "MirrorDesktop", true, "Reuse the left-eye texture on the desktop instead of a third 3D render. Keeps overlay UI; falls back to normal rendering for camera-space UI. Adjustable live in settings.");
            CameraName = Config.Bind("Rendering", "CameraName", "", "Optional exact camera GameObject name; blank uses Camera.main then a scene camera.");
            HudEnabled = Config.Bind("HUD", "Enabled", true, "Capture menus on the game reference frame. Space hides/shows the HUD independently of native UI state and pauses menu capture while hidden. Ctrl+Shift+V opens VR settings even when the HUD is hidden.");
            HudAlwaysOnTop = Config.Bind("HUD", "AlwaysOnTop", true, "Keep the menu HUD above scene geometry. Disable to render it at its actual distance so nearer scene objects can occlude it. Subtitles remain independent.");
            HudWidth = Config.Bind("HUD", "Width", 1.4f, "Virtual menu panel width in metres.");
            HudDistance = Config.Bind("HUD", "Distance", 1f, "Virtual menu panel distance in metres from the centered pose.");
            HudFlipY = Config.Bind("HUD", "FlipY", true, "Vertical flip for the HUD compositor texture only. Adjustable live in the settings HUD tab.");
            HudWorldPointer = Config.Bind("HUD", "WorldPointer", true, "Aim game world selection through the visible HUD cursor, including character and location selection, detached/pinned frames and curved panels.");
            DisablePovxMouseLook = Config.Bind("Compatibility", "DisablePovxMouseLook", true, "While VR is active, suppress game camera mouse look so clicking menus does not steer the POV camera. Ordinary game camera controls outside POV are preserved.");
            HidePovHead = Config.Bind("Compatibility", "HidePovHead", true, "Hide the selected character head/hair during rendering, also affecting reflection captures. Does not move the camera or its reference frame.");
            HidePovAccessories = Config.Bind("Compatibility", "HidePovAccessories", true, "Also hide accessories classified by the game as attached to the selected character's head. Requires HidePovHead.");
            SubtitlesEnabled = Config.Bind("Subtitles", "Enabled", true, "Move game subtitle text to a transparent headset-relative panel. Independent of Space hiding the menu HUD.");
            SubtitleWidth = Config.Bind("Subtitles", "Width", 1.2f, "Subtitle panel width in physical metres.");
            SubtitleDistance = Config.Bind("Subtitles", "Distance", 1f, "Subtitle panel distance in physical metres, relative to the headset.");
            SubtitleVerticalOffset = Config.Bind("Subtitles", "VerticalOffset", 0.45f, "Subtitle vertical distance from the forward view in metres. AtTop selects above or below.");
            SubtitleAtTop = Config.Bind("Subtitles", "AtTop", false, "Place subtitles above the forward view instead of below it.");
            SubtitleVisible = Config.Bind("Subtitles", "Visible", true, "Show the separate subtitle panel. False hides captions and pauses their UI render pass.");
            SubtitleHorizontalOffset = Config.Bind("Subtitles", "HorizontalOffset", 0f, "Subtitle horizontal position in metres; positive is right.");
            SubtitleFontSize = Config.Bind("Subtitles", "FontSize", 36, "Subtitle font size in the fixed-resolution caption canvas.");
            HudVerticalOffset = Config.Bind("HUD", "VerticalOffset", 0f, "Panel centre above the game reference frame in metres; negative moves it down.");
            HudCurvature = Config.Bind("HUD", "Curvature", 0f, "Horizontal panel curvature from 0 (flat) to 1. Scene mode uses a cylinder; compositor mode uses SteamVR curvature and falls back to flat if unsupported.");
            PinPositionWhileAttached = Config.Bind("POV", "PinPositionWhileAttached", true, "Ignore physical head XYZ movement while attached to POV; keep head rotation. L detach restores head translation when Tracking.Position is enabled.");
            DetachedTransition = Config.Bind("POV", "DetachedTransition", 0, "L-detached animation transitions: 0 rebase on animation/phase and remain detached; 1 reattach on whole animation only; 2 keep frozen until L.");
            PovOffsetX = Config.Bind("POV", "OffsetX", 0f, "Additional POV frame offset in physical metres, along local right.");
            PovOffsetY = Config.Bind("POV", "OffsetY", 0f, "Additional POV frame offset in physical metres, along local up.");
            PovOffsetZ = Config.Bind("POV", "OffsetZ", 0f, "Additional POV frame offset in physical metres, along local forward.");
            PovPitch = Config.Bind("POV", "Pitch", 0f, "Additional POV frame pitch in degrees.");
            PovYaw = Config.Bind("POV", "Yaw", 0f, "Additional POV frame yaw in degrees.");
            PovRoll = Config.Bind("POV", "Roll", 0f, "Additional POV frame roll in degrees.");
            Stabilize = Config.Bind("Stabilization", "Enabled", true, "Smooth the animation-driven POV frame, never the headset pose. L detach remains a separate feature.");
            PositionTau = Config.Bind("Stabilization", "PositionSeconds", 0.08f, "Position smoothing time constant in seconds. Larger values suppress more animation movement and add more following delay.");
            RotationTau = Config.Bind("Stabilization", "RotationSeconds", 0.12f, "Rotation smoothing time constant in seconds; physical head rotation stays immediate.");
            StabilizationStrength = Config.Bind("Stabilization", "Strength", 1f, "Blend between direct animation motion (0) and smoothed frame (1).");
            var revision = Config.Bind("Internal", "SettingsRevision", 0, "Tracks one-time migrations of previous candidate defaults.");
            if (revision.Value < 2)
            {
                if (SubtitleVerticalOffset.Value < 0f) SubtitleVerticalOffset.Value = Math.Abs(SubtitleVerticalOffset.Value);
                revision.Value = 2;
            }
            MenuSettings = CreateMenuSettings();
            Config.Save();
            AddComponent<VRBehaviour>();
            Log.LogInfo("Amanatsu Location VR 0.1.2 loaded. Stereo, head tracking and built-in character POV. Comma toggles POV; period cycles characters. Ctrl+Shift+V settings, Ctrl+Shift+R recenter, L detach/reattach in POV, Space HUD. No function-key bindings. VR enable/restart, diagnostics, mirror and texture flips are in settings. SteamVR / DX11 required.");
        }

        IReadOnlyList<SettingSpec> CreateMenuSettings() => new List<SettingSpec>
        {
            new SettingSpec(Enabled, "Enable VR", "View", description: "When disabled, reopen this panel on the desktop to enable VR again."),
            new SettingSpec(HidePovHead, "Hide POV head and hair", "View"),
            new SettingSpec(HidePovAccessories, "Hide head accessories", "View"),
            new SettingSpec(PovNearClip, "POV near clipping", "View", .001f, .2f, .001f, "cm", 100),
            new SettingSpec(NearClip, "Other views near clipping", "View", .001f, .5f, .001f, "cm", 100),
            new SettingSpec(HeadTracking, "Head rotation tracking", "View"),
            new SettingSpec(PositionTracking, "Head position tracking", "View"),
            new SettingSpec(UnitsPerMeter, "World scale", "View", .1f, 20f, .1f, "units/m"),
            new SettingSpec(MirrorDesktop, "Reuse left eye on monitor", "View"),
            new SettingSpec(FlipY, "Flip eye textures vertically", "View", description: "Normally enabled; only change if the eye images are upside down."),
            new SettingSpec(HudEnabled, "Enable menu HUD", "HUD"),
            new SettingSpec(HudFlipY, "Flip HUD texture vertically", "HUD"),
            new SettingSpec(HudAlwaysOnTop, "Always on top", "HUD", description: "Turn off to let closer scene objects hide parts of the HUD."),
            new SettingSpec(HudDistance, "Distance", "HUD", .3f, 3f, .01f, "m"),
            new SettingSpec(HudWidth, "Width", "HUD", .3f, 3f, .01f, "m"),
            new SettingSpec(HudVerticalOffset, "Vertical offset", "HUD", -1.5f, 1.5f, .01f, "m"),
            new SettingSpec(HudCurvature, "Horizontal curvature", "HUD", 0, 1, .01f),
            new SettingSpec(HudWorldPointer, "Align world picker to HUD cursor", "HUD"),
            new SettingSpec(SubtitleVisible, "Show subtitles", "Subtitles"),
            new SettingSpec(SubtitleAtTop, "Place at top", "Subtitles"),
            new SettingSpec(SubtitleFontSize, "Font size", "Subtitles", 16, 72, 1, "px"),
            new SettingSpec(SubtitleVerticalOffset, "Vertical distance from centre", "Subtitles", 0, 1.5f, .01f, "m"),
            new SettingSpec(SubtitleHorizontalOffset, "Horizontal offset", "Subtitles", -1, 1, .01f, "m"),
            new SettingSpec(SubtitleDistance, "Panel distance", "Subtitles", .3f, 3f, .01f, "m"),
            new SettingSpec(SubtitleWidth, "Panel width", "Subtitles", .3f, 3f, .01f, "m"),
            new SettingSpec(PinPositionWhileAttached, "Pin head position while attached", "POV"),
            new SettingSpec(DetachedTransition, "Detached transition policy", "POV", 0, 2, 1, description: "0: rebase and stay detached   1: reattach on animation   2: stay frozen"),
            new SettingSpec(PovOffsetX, "Frame right / left", "POV", -.5f, .5f, .005f, "cm", 100),
            new SettingSpec(PovOffsetY, "Frame up / down", "POV", -.5f, .5f, .005f, "cm", 100),
            new SettingSpec(PovOffsetZ, "Frame forward / back", "POV", -.5f, .5f, .005f, "cm", 100),
            new SettingSpec(PovPitch, "Frame pitch", "POV", -90, 90, 1, "deg"),
            new SettingSpec(PovYaw, "Frame yaw", "POV", -180, 180, 1, "deg"),
            new SettingSpec(PovRoll, "Frame roll", "POV", -90, 90, 1, "deg"),
            new SettingSpec(Stabilize, "Stabilize animation frame", "Stabilization"),
            new SettingSpec(StabilizationStrength, "Strength", "Stabilization", 0, 1, .01f, "%", 100),
            new SettingSpec(PositionTau, "Position smoothing", "Stabilization", 0, .5f, .005f, "ms", 1000),
            new SettingSpec(RotationTau, "Rotation smoothing", "Stabilization", 0, .5f, .005f, "ms", 1000)
        };
    }
}
