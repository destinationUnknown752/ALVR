using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UObject = UnityEngine.Object;

namespace AmanatsuLocationVR
{
    public sealed class VRBehaviour : MonoBehaviour
    {
        public VRBehaviour(IntPtr ptr) : base(ptr) { }
        Plugin P { [HideFromIl2Cpp] get => Plugin.Instance; }
        readonly Camera[] eyes = new Camera[2];
        readonly UniversalAdditionalCameraData[] eyeData = new UniversalAdditionalCameraData[2];
        readonly RenderTexture[] textures = new RenderTexture[2];
        readonly Vector3[] eyePositions = new Vector3[2];
        readonly Quaternion[] eyeRotations = new Quaternion[2];
        readonly float[][] raw = { new float[4], new float[4] };
        readonly float[] pose = new float[12];
        readonly float[] hudAnchorPose = new float[12];
        readonly TrackingReference tracking = new TrackingReference();
        readonly WorkTiming updateTiming = new WorkTiming(), syncTiming = new WorkTiming();
        readonly WorkTiming lateTiming = new WorkTiming(), frameTiming = new WorkTiming();
        float eyeTextureAspect;
        Camera source;
        DesktopMirror desktopMirror;
        UiHud hud;
        SceneHudSurface sceneHud;
        SubtitlePresentation subtitles;
        PovxCompatibility povx;
        PovHeadVisibility headVisibility;
        WatcherCameraCompatibility watcherCamera;
        WorldPointer worldPointer;
        VrSettingsMenu settingsMenu;
        AnimationState animationState;
        CameraFrame cameraFrame;
        AnimationSample animationSample;
        Vector3 framePosition, appliedHeadTranslation;
        Quaternion frameRotation = Quaternion.identity, appliedHeadRotation = Quaternion.identity;
        int evaluatedFrame = -1;
        bool frameReady, hudVisible = true, wasPositionPinned;
        bool ShowHud { [HideFromIl2Cpp] get => settingsMenu != null && settingsMenu.Visible || P.HudEnabled.Value && hudVisible; }
        IntPtr hudPointer, submittedHudPointer;
        RenderTexture registeredHudTexture;
        float submittedHudWidth, submittedHudDistance;
        bool submittedHudFlip, submittedEyeFlip;
        Action<ScriptableRenderContext, Il2CppSystem.Collections.Generic.List<Camera>> beginFrameManaged;
        Il2CppSystem.Action<ScriptableRenderContext, Il2CppSystem.Collections.Generic.List<Camera>> beginFrameNative;
        bool initialized, attempted, calibrated, running, captureRequested;
        bool havePose, currentTrackingUsable;
        bool manualRecenter, restartRequested;
        Vector3 referencePosition, currentPosition;
        Quaternion currentRotation = Quaternion.identity;
        IntPtr renderEvent;
        float nextScan, nextStats;
        float lastStatsTime;
        ulong lastFrameCount;
        bool savedFramePacing;
        int desktopVSync, desktopFrameRate;
        int previousLeft = int.MinValue, previousRight = int.MinValue;
        string diagnosticDirectory;

        public void Start()
        {
            diagnosticDirectory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "diagnostics");
            desktopMirror = new DesktopMirror(P.Log);
            hud = new UiHud(P.Log);
            sceneHud = new SceneHudSurface(P.Log);
            subtitles = new SubtitlePresentation(P.Log);
            povx = new PovxCompatibility(P.Log);
            headVisibility = new PovHeadVisibility(P.Log);
            watcherCamera = new WatcherCameraCompatibility(P.Log);
            worldPointer = new WorldPointer(P.Log);
            animationState = new AnimationState(P.Log);
            cameraFrame = new CameraFrame(P.Log);
            settingsMenu = new VrSettingsMenu(P.Log, P.Config, P.MenuSettings,
                () => manualRecenter = true,
                () => restartRequested = true,
                () => captureRequested = true);
            beginFrameManaged = BeforeFrame;
            beginFrameNative = beginFrameManaged;
            RenderPipelineManager.add_beginContextRendering(beginFrameNative);
            StartCoroutine(EndOfFrame().WrapToIl2Cpp());
            Application.runInBackground = true;
        }

        public void Update()
        {
            long started = Stopwatch.GetTimestamp(), syncTicks = 0;
            try
            {
                RestorePresentation();
                bool controlShift = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                    && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                if (controlShift && Input.GetKeyDown(KeyCode.V)) settingsMenu?.Toggle();
                settingsMenu?.Update();
                bool allowHotkeys = settingsMenu == null || (!settingsMenu.Visible && !settingsMenu.IsTextInputFocused);
                if (allowHotkeys && Input.GetKeyDown(KeyCode.L)) cameraFrame?.ToggleDetach();
                if (allowHotkeys && Input.GetKeyDown(KeyCode.Space))
                {

                    hudVisible = !hudVisible;
                    P.Log.LogInfo("Menu HUD " + (hudVisible ? "shown." : "hidden; menu capture paused."));
                }

                if (!ShowHud || settingsMenu != null && settingsMenu.Visible) worldPointer?.Disable();
                if (controlShift && Input.GetKeyDown(KeyCode.R))
                {
                    manualRecenter = true;
                    P.Log.LogInfo("SteamVR seated position/yaw reset requested; physical pitch/roll remain gravity referenced.");
                }

                if (restartRequested)
                {
                    restartRequested = false;
                    if (initialized) StopVR();
                    P.Enabled.Value = true;
                    attempted = false;
                    P.Log.LogInfo("VR restart requested from settings.");
                }
                if (initialized && submittedEyeFlip != P.FlipY.Value)
                {
                    if (Native.RGVR_SetTextures(textures[0].GetNativeTexturePtr(), textures[1].GetNativeTexturePtr(), P.FlipY.Value ? 1 : 0) != 1)
                        throw new InvalidOperationException("Native bridge rejected the updated eye orientation.");
                    submittedEyeFlip = P.FlipY.Value;
                    P.Log.LogInfo("FlipY = " + P.FlipY.Value);
                }
                if (!P.Enabled.Value)
                {
                    if (initialized) StopVR();
                    attempted = false;
                    return;
                }
                if (!initialized && !attempted && Time.realtimeSinceStartup > 5f) InitializeVR();
                if (!initialized) return;
                povx?.Apply(true, P.DisablePovxMouseLook.Value);
                povx?.HandleInput(source, allowHotkeys);
                if (manualRecenter && Native.RGVR_ResetSeatedOrigin() == 1)
                {
                    tracking.RequestPositionAnchor();
                    manualRecenter = false;
                }

                long syncStarted = Stopwatch.GetTimestamp();
                GL.IssuePluginEvent(renderEvent, 2);

                textures[0].GetNativeTexturePtr();
                syncTicks = Stopwatch.GetTimestamp() - syncStarted;
                syncTiming.AddTicks(syncTicks);
                int result = Native.RGVR_GetCachedPose(pose);
                Native.RGVR_GetTrackingState(out int connected, out int valid, out int activity, out ulong generation);
                if (result == 1)
                {
                    StereoMath.Pose(pose, out var position, out var rotation);
                    if (tracking.Accept(position, rotation, connected != 0, valid != 0, activity, generation))
                    {
                        P.Log.LogInfo($"Headset position anchored; SteamVR origin generation={generation}, position={tracking.OriginPosition}. Runtime gravity and yaw retained.");
                    }
                }
                currentPosition = tracking.Position;
                currentRotation = tracking.Rotation;
                referencePosition = tracking.OriginPosition;
                calibrated = havePose = tracking.Ready;
                currentTrackingUsable = havePose && result == 1 && connected != 0 && valid != 0
                    && (activity == -1 || activity == 1 || activity == 2) && generation == tracking.OriginGeneration;
                if (!currentTrackingUsable) worldPointer?.Disable();
                if (havePose) UpdateAppliedHeadPose();

            }
            catch (Exception ex) { Fail("Update", ex); }
            finally { updateTiming.AddTicks(Stopwatch.GetTimestamp() - started - syncTicks); }
        }

        public void LateUpdate()
        {
            if (!initialized) return;
            long started = Stopwatch.GetTimestamp();
            try
            {
                RestorePresentation();
                if (Time.realtimeSinceStartup >= nextScan || !ValidSource(source))
                {
                    nextScan = Time.realtimeSinceStartup + 2f;
                    var candidate = FindSource();
                    if (candidate != source)
                    {
                        source = candidate;
                        watcherCamera?.Deactivate();
                        frameReady = false;
                        cameraFrame.Reset();
                        P.Log.LogInfo(source ? "Following camera: " + CameraPath(source) : "Waiting for an active scene camera.");
                        DumpCameras();
                    }
                }
                bool wasRunning = running;
                running = ValidSource(source) && havePose;
                if (!running)
                {
                    sceneHud?.Disable();
                    watcherCamera?.Deactivate();
                    SetEyesEnabled(false);
                    if (wasRunning) ClearEyeTextures();
                    PrepareUi();
                    desktopMirror?.Prepare(source, null, false);
                    UpdateHudTexture(hud?.Texture);
                    worldPointer?.Disable();
                    return;
                }
                var urpSource = source.GetComponent<UniversalAdditionalCameraData>();
                float eyeDepth = source.depth + 100f;

                if (!P.HudAlwaysOnTop.Value) eyeDepth = Mathf.Max(eyeDepth, 10010f);
                for (int i = 0; i < 2; ++i)
                {
                    var eye = eyes[i];
                    eye.CopyFrom(source);
                    ConfigureEyeRendering(eyeData[i], urpSource);
                    eye.targetTexture = textures[i];
                    eye.rect = new Rect(0f, 0f, 1f, 1f);
                    eye.allowMSAA = false;
                    eye.allowDynamicResolution = false;
                    eye.depth = eyeDepth + i;
                    eye.orthographic = false;
                    eye.aspect = eyeTextureAspect;
                    eye.useJitteredProjectionMatrixForTransparentRendering = false;
                    eye.enabled = true;
                }

                PrepareUi();

                desktopMirror?.Prepare(source, textures[0], P.MirrorDesktop.Value);
            }
            catch (Exception ex) { Fail("LateUpdate", ex); }
            finally { lateTiming.AddTicks(Stopwatch.GetTimestamp() - started); }
        }

        [HideFromIl2Cpp]
        void InitializeVR()
        {
            attempted = true;
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                throw new InvalidOperationException("DX11 required. Launch AmanatsuLocation.exe -force-d3d11.");
            Native.Load(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            int error = Native.RGVR_Init();
            if (error != 0)
                throw new InvalidOperationException("SteamVR initialization failed: " + error + " " + Marshal.PtrToStringAnsi(Native.RGVR_ErrorName(error)));
            initialized = true;
            ApplyVrFramePacing();
            if (Native.RGVR_GetRecommendedSize(out uint recommendedWidth, out uint recommendedHeight) != 1)
                throw new InvalidOperationException("SteamVR returned no recommended texture dimensions.");
            float scale = Mathf.Clamp(P.RenderScale.Value, 0.2f, 2f);
            if (P.MaxDimension.Value > 0)
            {
                int cap = Math.Max(256, Math.Min(8192, P.MaxDimension.Value));
                scale = Mathf.Min(scale, (float)cap / Mathf.Max(recommendedWidth, recommendedHeight));
            }
            int width = Math.Max(256, (int)(recommendedWidth * scale));
            int height = Math.Max(256, (int)(recommendedHeight * scale));
            eyeTextureAspect = (float)width / height;
            for (int i = 0; i < 2; ++i)
            {
                var transform = new float[12];
                if (Native.RGVR_GetEye(i, transform, raw[i]) != 1 || raw[i][1] <= raw[i][0] || raw[i][3] <= raw[i][2])
                    throw new InvalidOperationException("SteamVR returned an invalid eye transform or projection.");
                StereoMath.Pose(transform, out eyePositions[i], out eyeRotations[i]);
                var go = new GameObject("AmanatsuLocationVR_" + (i == 0 ? "LeftEye" : "RightEye"));
                UObject.DontDestroyOnLoad(go);
                eyes[i] = go.AddComponent<Camera>();
                eyes[i].enabled = false;
                eyeData[i] = go.AddComponent<UniversalAdditionalCameraData>();
                ConfigureEyeRendering(eyeData[i], null);
                textures[i] = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                textures[i].name = "AmanatsuLocationVR_" + (i == 0 ? "LeftTexture" : "RightTexture");
                textures[i].hideFlags = HideFlags.DontUnloadUnusedAsset;
                textures[i].antiAliasing = 1;
                textures[i].useMipMap = false;
                textures[i].autoGenerateMips = false;
                textures[i].filterMode = FilterMode.Bilinear;
                if (!textures[i].Create()) throw new InvalidOperationException("Could not allocate eye RenderTexture.");
                eyes[i].targetTexture = textures[i];
            }
            if (Native.RGVR_SetTextures(textures[0].GetNativeTexturePtr(), textures[1].GetNativeTexturePtr(), P.FlipY.Value ? 1 : 0) != 1)
                throw new InvalidOperationException("Native bridge rejected the Unity D3D11 eye textures.");
            submittedEyeFlip = P.FlipY.Value;
            ClearEyeTextures();
            renderEvent = Native.RGVR_GetRenderEventFunc();
            calibrated = havePose = false;
            tracking.Reset();
            frameReady = false;
            evaluatedFrame = -1;
            nextScan = 0f;
            nextStats = Time.realtimeSinceStartup + 10f;
            lastStatsTime = Time.realtimeSinceStartup;
            lastFrameCount = 0;
            P.Log.LogInfo($"SteamVR ready; recommended {recommendedWidth}x{recommendedHeight}, eye textures {width}x{height}, scale={scale:F2}, cap={P.MaxDimension.Value}, IPD {Vector3.Distance(eyePositions[0], eyePositions[1]):F4} m, GPU {SystemInfo.graphicsDeviceName}; pipeline {GraphicsSettings.renderPipelineAsset?.name}");
            P.Log.LogInfo("Rendering setup: SRP Batcher=" + GraphicsSettings.useScriptableRenderPipelineBatching
                + "; two independent eye cameras. Graphics quality and source culling settings are preserved.");

            DumpCameras();
        }

        [HideFromIl2Cpp]
        IEnumerator EndOfFrame()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                try
                {
                    if (initialized)
                    {

                        subtitles?.CheckAfterRender();
                        desktopMirror?.SetSubtitleTexture(subtitles?.Texture, source ? source.targetDisplay : 0);
                        if (hud == null || !hud.Texture)
                        {
                            sceneHud?.Disable();
                            UpdateHudTexture(null);
                            desktopMirror?.SetHudTexture(null);
                        }

                        GL.IssuePluginEvent(renderEvent, 1);
                        if (captureRequested && (running || (hud != null && hud.Texture)))
                        {
                            captureRequested = false;
                            try { CaptureEyes(); } catch (Exception ex) { P.Log.LogWarning("Optional eye capture failed: " + ex); }
                        }
                    }
                    if (initialized && Time.realtimeSinceStartup >= nextStats)
                    {
                        nextStats = Time.realtimeSinceStartup + 15f;
                        Native.RGVR_GetStats(out int left, out int right, out ulong count);
                        Native.RGVR_GetHudStats(out int hudError, out int hudVisible, out ulong hudUpdates);
                        Native.RGVR_GetSubtitleStats(out int subtitleError, out int subtitleVisible, out ulong subtitleUpdates);
                        Native.RGVR_GetTimingStats(out double poseMs, out double stereoMs, out double hudMs);
                        float fps = (count - lastFrameCount) / Mathf.Max(0.001f, Time.realtimeSinceStartup - lastStatsTime);
                        lastStatsTime = Time.realtimeSinceStartup;
                        lastFrameCount = count;
                        P.Log.LogInfo($"VR status: frames={count}, fps={fps:F1}, submit L={left} R={right}, pose={havePose}, camera={(source ? source.name : "none")}, rendering={running}");
                        if (source) P.Log.LogInfo("Rendering setup: SRP Batcher=" + GraphicsSettings.useScriptableRenderPipelineBatching
                            + ", source occlusion culling=" + source.useOcclusionCulling);
                        P.Log.LogInfo($"HUD status: compositor visible={hudVisible}, scene surface={sceneHud?.Active}, menu enabled={ShowHud}, error={hudError} ({Marshal.PtrToStringAnsi(Native.RGVR_HudErrorName(hudError))}), updates={hudUpdates}");
                        P.Log.LogInfo($"Subtitles status: visible={subtitleVisible}, error={subtitleError}, updates={subtitleUpdates}");
                        if (hud != null) P.Log.LogInfo(hud.TimingSummary);
                        if (headVisibility != null) P.Log.LogInfo("POV visibility: " + headVisibility.StateDescription);
                        P.Log.LogInfo($"Native CPU: pose wait {poseMs:F2} ms, stereo submit {stereoMs:F2} ms, overlay updates {hudMs:F2} ms (pose includes compositor pacing).");
                        P.Log.LogInfo("VR callback wall time: " + updateTiming.ReadAndReset("Update excluding sync") + "; "
                            + syncTiming.ReadAndReset("render-thread sync including pose wait") + "; "
                            + lateTiming.ReadAndReset("LateUpdate") + "; " + frameTiming.ReadAndReset("BeforeFrame"));
                        if (desktopMirror != null) P.Log.LogInfo(desktopMirror.TimingSummary);
                        LogCompositorTiming();
                        WritePoseState();
                        if (count > 0 && (left != 0 || right != 0) && (left != previousLeft || right != previousRight))
                            P.Log.LogWarning("SteamVR compositor rejected a texture; see OpenVR EVRCompositorError codes. 0 is success.");
                        previousLeft = left;
                        previousRight = right;
                    }
                }
                catch (Exception ex) { Fail("EndOfFrame", ex); }
                finally { RestorePresentation(); }
            }
        }

        [HideFromIl2Cpp]
        static void ConfigureEyeRendering(UniversalAdditionalCameraData data, UniversalAdditionalCameraData original)
        {
            data.renderType = CameraRenderType.Base;
            data.allowXRRendering = false;
            data.allowHDROutput = false;
            data.antialiasing = AntialiasingMode.None;
            data.volumeFrameworkUpdateMode = VolumeFrameworkUpdateMode.ViaScripting;
            if (original)
            {
                if (data.m_RendererIndex != original.m_RendererIndex) data.SetRenderer(original.m_RendererIndex);
                data.renderShadows = original.renderShadows;
                data.renderPostProcessing = original.renderPostProcessing;
                data.requiresDepthOption = original.requiresDepthOption;
                data.requiresColorOption = original.requiresColorOption;
                data.volumeLayerMask = original.volumeLayerMask;
                data.volumeTrigger = original.volumeTrigger;
                data.stopNaN = original.stopNaN;
                data.dithering = original.dithering;
            }
            else
            {
                if (data.m_RendererIndex != -1) data.SetRenderer(-1);
                data.renderShadows = true;
                data.renderPostProcessing = false;
                data.requiresDepthOption = CameraOverrideOption.UsePipelineSettings;
                data.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
                data.volumeLayerMask = 1;
                data.volumeTrigger = null;
                data.stopNaN = false;
                data.dithering = false;
            }

        }

        [HideFromIl2Cpp]
        static void UpdateEyeEffects(Camera eye, UniversalAdditionalCameraData data)
        {
            if (!data.renderPostProcessing) return;
            CameraExtensions.UpdateVolumeStack(eye, data);
            var stack = data.volumeStack;
            if (stack == null) return;
            var blur = stack.GetComponent<MotionBlur>();
            if (blur) blur.intensity.value = 0f;
            var focus = stack.GetComponent<DepthOfField>();
            if (focus) focus.mode.value = DepthOfFieldMode.Off;
            var beautify = stack.GetComponent<Beautify.Universal.Beautify>();
            if (beautify) beautify.depthOfField.value = false;
        }

        [HideFromIl2Cpp]
        void RestorePresentation()
        {
            headVisibility?.Restore();
            desktopMirror?.RestoreSource();
        }

        [HideFromIl2Cpp]
        void ApplyVrFramePacing()
        {
            try
            {
                desktopVSync = QualitySettings.vSyncCount;
                desktopFrameRate = Application.targetFrameRate;
                savedFramePacing = true;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                P.Log.LogInfo($"SteamVR supplies frame pacing; desktop vSync={desktopVSync}, frame cap={desktopFrameRate} temporarily removed.");
            }
            catch (Exception ex) { P.Log.LogWarning("Could not change desktop frame pacing: " + ex.Message); }
        }

        [HideFromIl2Cpp]
        void RestoreFramePacing()
        {
            if (!savedFramePacing) return;
            savedFramePacing = false;
            try
            {
                if (QualitySettings.vSyncCount == 0) QualitySettings.vSyncCount = desktopVSync;
                if (Application.targetFrameRate == -1) Application.targetFrameRate = desktopFrameRate;
            }
            catch (Exception ex) { P.Log.LogWarning("Could not restore desktop frame pacing: " + ex.Message); }
        }

        [HideFromIl2Cpp]
        void BeforeFrame(ScriptableRenderContext context, Il2CppSystem.Collections.Generic.List<Camera> cameras)
        {
            if (!initialized || !running || !source) return;
            long started = Stopwatch.GetTimestamp();
            try
            {

                if (evaluatedFrame != Time.frameCount)
                {
                    evaluatedFrame = Time.frameCount;
                    animationSample = animationState.Sample(source);
                    var settings = new CameraFrame.Settings
                    {
                        Stabilize = P.Stabilize.Value,
                        PositionTauSeconds = Mathf.Clamp(P.PositionTau.Value, 0f, .5f),
                        RotationTauSeconds = Mathf.Clamp(P.RotationTau.Value, 0f, .5f),
                        StabilizationStrength = Mathf.Clamp01(P.StabilizationStrength.Value),
                        PovPositionOffset = new Vector3(P.PovOffsetX.Value, P.PovOffsetY.Value, P.PovOffsetZ.Value) * Mathf.Clamp(P.UnitsPerMeter.Value, .01f, 100f),
                        PovRotationEuler = new Vector3(P.PovPitch.Value, P.PovYaw.Value, P.PovRoll.Value),
                        TransitionPolicy = (DetachedTransitionPolicy)Mathf.Clamp(P.DetachedTransition.Value, 0, 2)
                    };

                    var livePosition = source.transform.position;
                    var liveRotation = source.transform.rotation;
                    if (animationSample.PovActive && povx != null &&
                        povx.FrameSource.TryGetPose(out var characterPosition, out var characterRotation))
                    {
                        livePosition = characterPosition;
                        liveRotation = characterRotation;
                    }
                    cameraFrame.Evaluate(livePosition, liveRotation, animationSample,
                        Time.unscaledDeltaTime, settings, out framePosition, out frameRotation);
                    frameReady = true;
                }
                headVisibility?.Prepare(P.HidePovHead.Value, P.HidePovAccessories.Value);
                float units = Mathf.Clamp(P.UnitsPerMeter.Value, 0.01f, 100f);
                float nearMetres = headVisibility != null && headVisibility.PovActive
                    ? P.PovNearClip.Value : P.NearClip.Value;
                float near = Mathf.Clamp(nearMetres * units, 0.001f, 10f);
                float far = Mathf.Max(source.farClipPlane, near + 10f);
                for (int i = 0; i < 2; ++i)
                {
                    var eye = eyes[i];
                    if (!eye) continue;
                    eye.nearClipPlane = near;
                    eye.farClipPlane = far;
                    eye.projectionMatrix = StereoMath.Projection(raw[i], near, far);
                }
                UpdateEyeTransforms();
                for (int i = 0; i < 2; ++i) if (eyes[i]) UpdateEyeEffects(eyes[i], eyeData[i]);
                PrepareSceneHud();
                ConfigureWorldPointer();
            }
            catch (Exception ex) { Fail("BeforeFrame", ex); }
            finally { frameTiming.AddTicks(Stopwatch.GetTimestamp() - started); }
        }

        [HideFromIl2Cpp]
        void LogCompositorTiming()
        {
            if (Native.RGVR_GetCompositorTiming(out var timing, Native.CompositorTiming.Size) != 1)
            {
                P.Log.LogInfo("SteamVR detailed timings unavailable; no CPU/GPU bottleneck classification made.");
                return;
            }
            P.Log.LogInfo($"SteamVR timing: samples={timing.SampleCount}, age={timing.SampleAgeMs:F0} ms, refresh={timing.DisplayHz:F1} Hz, app interval={timing.ClientFrameIntervalMs:F2} ms; pose-ready to submit wall={timing.AppCpuWallMs:F2} ms; app GPU pre-submit={timing.PreSubmitGpuMs:F2} ms (max {timing.MaxPreSubmitGpuMs:F2}), post-submit={timing.PostSubmitGpuMs:F2} ms; compositor GPU={timing.CompositorGpuMs:F2} ms, total GPU span={timing.TotalRenderGpuMs:F2} ms.");
            P.Log.LogInfo($"SteamVR pacing: present={timing.PresentCallMs:F2} ms, wait-for-present={timing.WaitForPresentMs:F2} ms, submit={timing.SubmitMs:F2} ms, pose wait={timing.WaitGetPosesMs:F2} ms; reused={timing.ReusedFrameCount}, CPU-late={timing.CpuLateFrameCount}, GPU-late={timing.GpuLateFrameCount}, smoothed={timing.MotionSmoothedFrameCount}, throttled={timing.ThrottledFrameCount}, dropped={timing.DroppedFrameCount} in {timing.SampleCount} frames. Wall spans include waits; total GPU includes compositor/scheduling.");
        }

        [HideFromIl2Cpp]
        void UpdateEyeTransforms()
        {
            float units = Mathf.Clamp(P.UnitsPerMeter.Value, 0.01f, 100f);
            UpdateAppliedHeadPose();
            var headTranslation = appliedHeadTranslation * units;
            var headRotation = appliedHeadRotation;
            var basePosition = frameReady ? framePosition : source.transform.position;
            var baseRotation = frameReady ? frameRotation : source.transform.rotation;
            for (int i = 0; i < 2; ++i)
            {
                var eye = eyes[i];
                if (!eye) continue;
                eye.transform.position = basePosition + baseRotation * (headTranslation + headRotation * (eyePositions[i] * units));
                eye.transform.rotation = baseRotation * headRotation * eyeRotations[i];
                eye.ResetWorldToCameraMatrix();
                eye.ResetCullingMatrix();
            }
            watcherCamera?.SetPose(source, (eyes[0].transform.position + eyes[1].transform.position) * .5f,
                baseRotation * headRotation);
        }

        [HideFromIl2Cpp]
        void UpdateAppliedHeadPose()
        {
            bool pin = animationSample.PovActive && !cameraFrame.Detached && P.PinPositionWhileAttached.Value;

            if (wasPositionPinned && !pin) tracking.AnchorCurrentPosition();
            wasPositionPinned = pin;
            referencePosition = tracking.OriginPosition;
            tracking.Applied(P.HeadTracking.Value, P.PositionTracking.Value, pin, out appliedHeadTranslation, out appliedHeadRotation);
            TrackingReference.HudAnchor(currentPosition, currentRotation, appliedHeadTranslation, appliedHeadRotation, hudAnchorPose);

            Native.RGVR_SetHudAnchor(currentTrackingUsable ? hudAnchorPose : null);
        }

        [HideFromIl2Cpp]
        void PrepareUi()
        {
            subtitles.Prepare(source, hud, havePose && ShowHud,
                havePose && P.SubtitlesEnabled.Value, P.SubtitleWidth.Value,
                P.SubtitleDistance.Value, (P.SubtitleAtTop.Value ? 1f : -1f) * Mathf.Abs(P.SubtitleVerticalOffset.Value),
                P.SubtitleFontSize.Value, P.SubtitleHorizontalOffset.Value, P.SubtitleVisible.Value);
            UpdateHudTexture(hud?.Texture);
            int display = source ? source.targetDisplay : 0;
            desktopMirror?.SetHudTexture(sceneHud != null && sceneHud.Active && desktopMirror.Active ? null : hud?.Texture, display);
            desktopMirror?.SetSubtitleTexture(subtitles.Texture, display);
        }

        [HideFromIl2Cpp]
        void PrepareSceneHud()
        {
            var texture = hud?.Texture;
            bool enabled = running && frameReady && havePose && ShowHud && texture
                && !P.HudAlwaysOnTop.Value && !(settingsMenu != null && settingsMenu.Visible);
            sceneHud?.Prepare(texture, eyes[0], eyes[1], framePosition, frameRotation,
                Mathf.Clamp(P.UnitsPerMeter.Value, .01f, 100f), Mathf.Clamp(P.HudWidth.Value, .3f, 4f),
                Mathf.Clamp(P.HudDistance.Value, .3f, 5f), texture ? (float)texture.width / texture.height : 1f,
                Mathf.Clamp(P.HudVerticalOffset.Value, -1.5f, 1.5f), Mathf.Clamp01(P.HudCurvature.Value),
                P.HudFlipY.Value, enabled);

            UpdateHudTexture(texture);
            desktopMirror?.SetHudTexture(sceneHud != null && sceneHud.Active && desktopMirror.Active ? null : texture,
                source ? source.targetDisplay : 0);
        }

        [HideFromIl2Cpp]
        void ConfigureWorldPointer()
        {
            var texture = hud?.Texture;
            bool sceneSurface = sceneHud != null && sceneHud.Active;
            bool active = initialized && currentTrackingUsable && frameReady && ValidSource(source) && ShowHud
                && P.HudWorldPointer.Value && !(settingsMenu != null && settingsMenu.Visible)
                && texture && texture == registeredHudTexture;
            if (!active)
            {
                worldPointer?.Disable();
                return;
            }
            float units = Mathf.Clamp(P.UnitsPerMeter.Value, 0.01f, 100f);
            float verticalOffset, curvature;
            if (sceneSurface)
            {
                verticalOffset = Mathf.Clamp(P.HudVerticalOffset.Value, -1.5f, 1.5f);
                curvature = Mathf.Clamp01(P.HudCurvature.Value);
            }
            else Native.RGVR_GetHudGeometry(out verticalOffset, out curvature, out _);
            worldPointer?.Configure(source, framePosition, frameRotation, appliedHeadTranslation, appliedHeadRotation,
                currentPosition, currentRotation, units, submittedHudWidth, submittedHudDistance,
                (float)texture.width / texture.height, verticalOffset, curvature, sceneSurface, submittedHudFlip, true);
        }

        [HideFromIl2Cpp]
        void UpdateHudTexture(RenderTexture texture)
        {
            float width = Mathf.Clamp(P.HudWidth.Value, 0.3f, 4f);
            float distance = Mathf.Clamp(P.HudDistance.Value, 0.3f, 5f);
            bool flip = P.HudFlipY.Value;
            Native.RGVR_SetHudGeometry(Mathf.Clamp(P.HudVerticalOffset.Value, -1.5f, 1.5f), Mathf.Clamp01(P.HudCurvature.Value));
            IntPtr pointer = !texture ? IntPtr.Zero : texture == registeredHudTexture ? hudPointer : texture.GetNativeTexturePtr();
            IntPtr overlayPointer = sceneHud != null && sceneHud.Active ? IntPtr.Zero : pointer;
            hudPointer = pointer;
            registeredHudTexture = texture;
            if (overlayPointer == submittedHudPointer && width == submittedHudWidth && distance == submittedHudDistance && flip == submittedHudFlip) return;
            int result = Native.RGVR_SetHudTexture(overlayPointer, width, distance, flip ? 1 : 0);
            if (result < 0) throw new InvalidOperationException("Native bridge rejected the HUD texture.");
            submittedHudPointer = overlayPointer;
            submittedHudWidth = width;
            submittedHudDistance = distance;
            submittedHudFlip = flip;
        }

        [HideFromIl2Cpp]
        void ClearEyeTextures()
        {
            var previous = RenderTexture.active;
            try
            {
                foreach (var texture in textures)
                {
                    if (!texture) continue;
                    RenderTexture.active = texture;
                    GL.Clear(true, true, Color.black);
                }
            }
            finally { RenderTexture.active = previous; }
        }

        [HideFromIl2Cpp]
        bool ValidSource(Camera camera)
        {
            if (!camera || !camera.enabled || !camera.gameObject.activeInHierarchy || camera.targetTexture != null
                || camera.name.StartsWith("AmanatsuLocationVR_") || camera.cameraType != CameraType.Game) return false;
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            return !data || data.renderType == CameraRenderType.Base;
        }

        [HideFromIl2Cpp]
        Camera FindSource()
        {
            var cameras = Camera.allCameras;
            if (!string.IsNullOrEmpty(P.CameraName.Value))
            {
                foreach (var camera in cameras) if (ValidSource(camera) && camera.name == P.CameraName.Value) return camera;
                return null;
            }
            var main = Camera.main;
            if (ValidSource(main) && !main.orthographic) return main;
            Camera best = null;
            foreach (var camera in cameras)
            {
                if (!ValidSource(camera) || camera.orthographic || camera.cullingMask == 0) continue;
                if (!best || camera.depth < best.depth) best = camera;
            }
            return best;
        }

        [HideFromIl2Cpp]
        static string CameraPath(Camera camera)
        {
            string result = camera.name;
            var parent = camera.transform.parent;
            while (parent) { result = parent.name + "/" + result; parent = parent.parent; }
            return result;
        }

        [HideFromIl2Cpp]
        void DumpCameras()
        {
            foreach (var camera in Camera.allCameras)
                P.Log.LogInfo($"Camera {CameraPath(camera)} enabled={camera.enabled} ortho={camera.orthographic} depth={camera.depth} mask={camera.cullingMask} target={(camera.targetTexture ? camera.targetTexture.name : "screen")} pos={camera.transform.position} angles={camera.transform.eulerAngles}");
        }

        [HideFromIl2Cpp]
        void CaptureEyes()
        {
            Directory.CreateDirectory(diagnosticDirectory);
            var old = RenderTexture.active;
            try
            {
                for (int i = 0; i < 2; ++i)
                {
                    RenderTexture.active = textures[i];
                    var image = new Texture2D(textures[i].width, textures[i].height, TextureFormat.RGB24, false);
                    try
                    {
                        image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                        image.Apply();
                        byte[] png = ImageConversion.EncodeToPNG(image);
                        File.WriteAllBytes(Path.Combine(diagnosticDirectory, i == 0 ? "left.png" : "right.png"), png);
                    }
                    finally { UObject.Destroy(image); }
                }
                if (hud != null && hud.Texture)
                {
                    var texture = hud.Texture;
                    RenderTexture.active = texture;
                    var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                    try
                    {
                        image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                        image.Apply();
                        File.WriteAllBytes(Path.Combine(diagnosticDirectory, "hud.png"), ImageConversion.EncodeToPNG(image));
                    }
                    finally { UObject.Destroy(image); }
                }
                if (subtitles != null && subtitles.Texture)
                {
                    var texture = subtitles.Texture;
                    RenderTexture.active = texture;
                    var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                    try
                    {
                        image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                        image.Apply();
                        File.WriteAllBytes(Path.Combine(diagnosticDirectory, "subtitles.png"), ImageConversion.EncodeToPNG(image));
                    }
                    finally { UObject.Destroy(image); }
                }
                DumpCameras();
                WritePoseState();
                P.Log.LogInfo("Eye captures saved: " + diagnosticDirectory);
            }
            finally { RenderTexture.active = old; }
        }

        [HideFromIl2Cpp]
        void SetEyesEnabled(bool value) { foreach (var eye in eyes) if (eye) eye.enabled = value; }

        [HideFromIl2Cpp]
        void WritePoseState()
        {
            Directory.CreateDirectory(diagnosticDirectory);
            var s = new System.Text.StringBuilder();
            s.AppendLine("UTC=" + DateTime.UtcNow.ToString("O"));
            s.AppendLine($"initialized={initialized} running={running} calibrated={calibrated}");
            s.AppendLine($"rotation={P.HeadTracking.Value} position={P.PositionTracking.Value} unitsPerMeter={P.UnitsPerMeter.Value}");
            s.AppendLine($"headPosition={currentPosition} headRotation={currentRotation} headAngles={currentRotation.eulerAngles}");
            s.AppendLine($"referencePosition={referencePosition} orientation=runtime-seated originGeneration={tracking.OriginGeneration}");
            s.AppendLine($"cameraFrame={cameraFrame?.StateDescription} pov={animationSample.PovActive} pin={P.PinPositionWhileAttached.Value} stabilized={P.Stabilize.Value}");
            s.AppendLine($"povRequested={PovxCompatibility.PovRequested} selection={PovxCompatibility.PovStateDescription}");
            s.AppendLine($"hud visible={ShowHud} spaceVisible={hudVisible} alwaysOnTop={P.HudAlwaysOnTop.Value} sceneSurface={sceneHud?.Active}");
            if (povx != null) s.AppendLine("povFrameSource=" + povx.FrameSource.StateDescription);
            if (cameraFrame != null) s.AppendLine($"liveFrame position={cameraFrame.LatestLivePosition} angles={cameraFrame.LatestLiveRotation.eulerAngles}; renderedFrame position={framePosition} angles={frameRotation.eulerAngles}");
            if (headVisibility != null) s.AppendLine("povVisibility=" + headVisibility.StateDescription);
            if (source) s.AppendLine($"source={CameraPath(source)} position={source.transform.position} angles={source.transform.eulerAngles}");
            for (int i = 0; i < 2; ++i) if (eyes[i]) s.AppendLine($"eye{i} position={eyes[i].transform.position} angles={eyes[i].transform.eulerAngles}");
            File.WriteAllText(Path.Combine(diagnosticDirectory, "pose-state.txt"), s.ToString());
        }

        [HideFromIl2Cpp]
        void StopVR()
        {
            running = false;
            watcherCamera?.Deactivate();
            cameraFrame?.Reset();
            tracking.Reset();
            wasPositionPinned = false;
            currentTrackingUsable = false;
            frameReady = false;
            evaluatedFrame = -1;
            worldPointer?.Disable();
            sceneHud?.Disable();
            RestoreFramePacing();
            povx?.Apply(false, false);
            RestorePresentation();
            desktopMirror?.SetHudTexture(null);
            desktopMirror?.SetSubtitleTexture(null);
            subtitles?.Stop(hud, initialized);
            desktopMirror?.Prepare(source, null, false);
            if (initialized) Native.RGVR_SetHudTexture(IntPtr.Zero, 1.4f, 1.5f, 0);
            hudPointer = submittedHudPointer = IntPtr.Zero;
            registeredHudTexture = null;
            hud?.Prepare(source, false);
            SetEyesEnabled(false);
            if (initialized && textures[0]) textures[0].GetNativeTexturePtr();
            if (initialized) Native.RGVR_Shutdown();
            initialized = false;
            for (int i = 0; i < 2; ++i)
            {
                if (eyes[i]) UObject.Destroy(eyes[i].gameObject);
                eyes[i] = null;
                eyeData[i] = null;
                if (textures[i]) { textures[i].Release(); UObject.Destroy(textures[i]); }
                textures[i] = null;
            }
            source = null;
            calibrated = havePose = false;
        }

        [HideFromIl2Cpp]
        void Fail(string stage, Exception ex)
        {
            P.Log.LogError(stage + ": " + ex);
            try { StopVR(); } catch (Exception cleanup) { P.Log.LogError(cleanup); }
            attempted = true;
            P.Log.LogWarning("VR stopped. Open settings with Ctrl+Shift+V and select Restart VR to retry. The normal desktop game remains available.");
        }

        public void OnApplicationQuit() { StopVR(); }
        public void OnDestroy()
        {
            if (beginFrameNative != null) RenderPipelineManager.remove_beginContextRendering(beginFrameNative);
            beginFrameNative = null;
            beginFrameManaged = null;
            StopVR();
            desktopMirror?.Dispose();
            hud?.Dispose();
            sceneHud?.Dispose();
            subtitles?.Dispose();
            povx?.Dispose();
            headVisibility?.Dispose();
            watcherCamera?.Dispose();
            worldPointer?.Dispose();
            settingsMenu?.Dispose();
            animationState?.Dispose();
        }
    }
}
