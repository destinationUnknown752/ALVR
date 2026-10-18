using System;
using System.Diagnostics;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace AmanatsuLocationVR
{

    internal sealed class DesktopMirror : IDisposable
    {
        readonly ManualLogSource log;
        GameObject canvasObject;
        Canvas canvas;
        RawImage image;
        GameObject hudObject;
        Canvas hudCanvas;
        RawImage hudImage;
        GameObject subtitleObject;
        Canvas subtitleCanvas;
        RawImage subtitleImage;
        RectTransform subtitleRect;
        bool subtitleMirrorFailed;
        UniversalAdditionalCameraData suppressedData;
        Camera suppressedCamera;
        int previousCullingMask;
        CameraClearFlags previousClearFlags;
        Color previousBackground;
        bool previousPostProcessing, previousShadows;
        CameraOverrideOption previousDepthOption, previousColorOption;
        AntialiasingMode previousAntialiasing;
        bool disposed;
        string lastState;
        Camera cachedSource;
        GameObject cachedSourceObject;
        UniversalAdditionalCameraData cachedSourceData;
        RenderTexture boundEyeTexture, boundHudTexture, boundSubtitleTexture;
        int eyeDisplay = -1, hudDisplay = -1, subtitleDisplay = -1;
        int eyeWidth, eyeHeight, mirrorScreenWidth = -1, mirrorScreenHeight = -1;
        int subtitleWidth, subtitleHeight, subtitleScreenWidth = -1;
        bool eyeShown, hudShown, subtitlesShown;
        long prepareTicks, prepareMaximumTicks, uiTicks;
        int prepareSamples, uiSamples, canvasScans, canvasesChecked, componentLookups, presentationWrites;

        public DesktopMirror(ManualLogSource logger)
        {
            log = logger;
        }

        public bool Active => eyeShown && canvas && canvas.enabled;

        public string TimingSummary
        {
            get
            {
                double milliseconds = 1000.0 / Stopwatch.Frequency;
                string result = $"Desktop mirror CPU: prepare {(prepareSamples == 0 ? 0 : prepareTicks * milliseconds / prepareSamples):F2} ms " +
                    $"(max {prepareMaximumTicks * milliseconds:F2}), UI copies {(uiSamples == 0 ? 0 : uiTicks * milliseconds / uiSamples):F2} ms; " +
                    $"samples {prepareSamples}/{uiSamples}, live canvas scans {canvasScans}, canvas checks {canvasesChecked}, " +
                    $"URP component lookups {componentLookups}, presentation writes {presentationWrites}";
                prepareTicks = prepareMaximumTicks = uiTicks = 0;
                prepareSamples = uiSamples = canvasScans = canvasesChecked = componentLookups = presentationWrites = 0;
                return result;
            }
        }

        public void SetHudTexture(RenderTexture hudTexture, int targetDisplay = 0)
        {
            long started = Stopwatch.GetTimestamp();
            try { SetHudTextureCore(hudTexture, targetDisplay); }
            finally { uiTicks += Stopwatch.GetTimestamp() - started; ++uiSamples; }
        }

        void SetHudTextureCore(RenderTexture hudTexture, int targetDisplay)
        {
            if (disposed) return;
            if (!hudTexture || !hudTexture.IsCreated())
            {
                HideHud();
                return;
            }
            try
            {
                EnsureHudCanvas();
                if (hudDisplay != targetDisplay) { hudCanvas.targetDisplay = targetDisplay; hudDisplay = targetDisplay; ++presentationWrites; }
                if (!ReferenceEquals(boundHudTexture, hudTexture)) { hudImage.texture = hudTexture; boundHudTexture = hudTexture; ++presentationWrites; }
                if (!hudShown) { hudCanvas.enabled = true; hudShown = true; ++presentationWrites; }
            }
            catch
            {

                HideHud();
                throw;
            }
        }

        public void SetSubtitleTexture(RenderTexture texture, int targetDisplay = 0)
        {
            long started = Stopwatch.GetTimestamp();
            try { SetSubtitleTextureCore(texture, targetDisplay); }
            finally { uiTicks += Stopwatch.GetTimestamp() - started; ++uiSamples; }
        }

        void SetSubtitleTextureCore(RenderTexture texture, int targetDisplay)
        {
            if (disposed) return;
            if (!texture || !texture.IsCreated()) { HideSubtitles(); subtitleMirrorFailed = false; return; }
            if (subtitleMirrorFailed) return;
            try
            {
                if (!subtitleObject || !subtitleCanvas || !subtitleImage || !subtitleRect)
                {
                    boundSubtitleTexture = null;
                    subtitlesShown = false;
                    subtitleDisplay = subtitleScreenWidth = -1;
                    if (subtitleObject) UObject.Destroy(subtitleObject);
                    subtitleObject = new GameObject("AmanatsuLocationVR_DesktopSubtitles", new[]
                    {
                        Il2CppType.Of<RectTransform>(), Il2CppType.Of<Canvas>()
                    });
                    UObject.DontDestroyOnLoad(subtitleObject);
                    subtitleObject.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    subtitleCanvas = subtitleObject.GetComponent<Canvas>();
                    subtitleCanvas.enabled = false;
                    subtitleCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    subtitleCanvas.sortingOrder = 32761;
                    subtitleCanvas.pixelPerfect = false;
                    var picture = new GameObject("AmanatsuLocationVR_DesktopSubtitleImage", new[] { Il2CppType.Of<RectTransform>() });
                    picture.transform.SetParent(subtitleObject.transform, false);
                    subtitleRect = picture.GetComponent<RectTransform>();
                    subtitleRect.anchorMin = subtitleRect.anchorMax = new Vector2(0.5f, 0f);
                    subtitleRect.pivot = new Vector2(0.5f, 0f);
                    subtitleRect.anchoredPosition3D = new Vector3(0f, 12f, 0f);
                    subtitleRect.localScale = Vector3.one;
                    subtitleImage = picture.AddComponent<RawImage>();
                    subtitleImage.raycastTarget = false;
                    subtitleImage.color = Color.white;
                    subtitleImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                }
                if (subtitleDisplay != targetDisplay) { subtitleCanvas.targetDisplay = targetDisplay; subtitleDisplay = targetDisplay; ++presentationWrites; }
                if (!ReferenceEquals(boundSubtitleTexture, texture))
                {
                    subtitleImage.texture = texture;
                    boundSubtitleTexture = texture;
                    subtitleWidth = texture.width;
                    subtitleHeight = texture.height;
                    subtitleScreenWidth = -1;
                    ++presentationWrites;
                }
                int screenWidth = Screen.width;
                if (subtitleScreenWidth != screenWidth)
                {
                    float width = screenWidth * 0.7f;
                    subtitleRect.sizeDelta = new Vector2(width, width * subtitleHeight / Math.Max(1, subtitleWidth));
                    subtitleScreenWidth = screenWidth;
                    ++presentationWrites;
                }
                if (!subtitlesShown) { subtitleCanvas.enabled = true; subtitlesShown = true; ++presentationWrites; }
            }
            catch (Exception exception)
            {
                subtitleMirrorFailed = true;
                HideSubtitles();
                log?.LogWarning("Desktop subtitle mirror unavailable: " + exception.Message);
            }
        }

        public void Prepare(Camera source, RenderTexture leftEye, bool enabled, bool allowCameraSpaceUi = false)
        {
            long started = Stopwatch.GetTimestamp();
            try { PrepareCore(source, leftEye, enabled, allowCameraSpaceUi); }
            finally
            {
                long elapsed = Stopwatch.GetTimestamp() - started;
                prepareTicks += elapsed;
                prepareMaximumTicks = Math.Max(prepareMaximumTicks, elapsed);
                ++prepareSamples;
            }
        }

        void PrepareCore(Camera source, RenderTexture leftEye, bool enabled, bool allowCameraSpaceUi)
        {
            RestoreSource();
            if (disposed) return;
            try
            {
                if (!ReferenceEquals(source, cachedSource) || !cachedSourceObject)
                {
                    cachedSource = source;
                    cachedSourceObject = source ? source.gameObject : null;
                    cachedSourceData = null;
                }
                if (!enabled || !source || !source.enabled || !cachedSourceObject.activeInHierarchy ||
                    source.targetTexture != null || !leftEye || !leftEye.IsCreated())
                {
                    HideCanvas();
                    SetState("disabled", "Desktop mirror inactive; normal desktop rendering retained.");
                    return;
                }

                if (!cachedSourceData)
                {
                    cachedSourceData = source.GetComponent<UniversalAdditionalCameraData>();
                    ++componentLookups;
                }
                var sourceData = cachedSourceData;
                if (!sourceData || sourceData.renderType != CameraRenderType.Base)
                {
                    HideCanvas();
                    SetState("unsupported-camera", "Desktop mirror paused for an unsupported or custom-rendered camera; normal desktop rendering retained.");
                    return;
                }
                int display = source.targetDisplay;

                if (!allowCameraSpaceUi)
                {

                    ++canvasScans;
                    foreach (var existing in UObject.FindObjectsOfType<Canvas>())
                    {

                        if (ReferenceEquals(existing, canvas) || ReferenceEquals(existing, hudCanvas) ||
                            ReferenceEquals(existing, subtitleCanvas)) continue;
                        ++canvasesChecked;
                        if (!existing || existing.renderMode != RenderMode.ScreenSpaceCamera || !existing.isActiveAndEnabled) continue;
                        var uiCamera = existing.worldCamera;
                        bool onDesktop = uiCamera
                            ? uiCamera.targetTexture == null && uiCamera.targetDisplay == display
                            : existing.targetDisplay == display;
                        if (uiCamera == source || onDesktop)
                        {
                            HideCanvas();
                            SetState("camera-ui", "Desktop mirror paused for camera-space UI; normal desktop rendering retained.");
                            return;
                        }
                    }
                }

                EnsureCanvas();
                if (eyeDisplay != display) { canvas.targetDisplay = display; eyeDisplay = display; ++presentationWrites; }
                if (!ReferenceEquals(boundEyeTexture, leftEye))
                {
                    image.texture = leftEye;
                    boundEyeTexture = leftEye;

                    eyeWidth = leftEye.width;
                    eyeHeight = leftEye.height;
                    mirrorScreenWidth = mirrorScreenHeight = -1;
                    ++presentationWrites;
                }
                int screenWidth = Screen.width, screenHeight = Screen.height;
                if (mirrorScreenWidth != screenWidth || mirrorScreenHeight != screenHeight)
                {
                    image.uvRect = CenteredCrop(eyeWidth, eyeHeight, screenWidth, screenHeight);
                    mirrorScreenWidth = screenWidth;
                    mirrorScreenHeight = screenHeight;
                    ++presentationWrites;
                }
                if (!eyeShown) { canvas.enabled = true; eyeShown = true; ++presentationWrites; }

                suppressedCamera = source;
                suppressedData = sourceData;
                previousCullingMask = source.cullingMask;
                previousClearFlags = source.clearFlags;
                previousBackground = source.backgroundColor;
                previousPostProcessing = sourceData.renderPostProcessing;
                previousShadows = sourceData.renderShadows;
                previousDepthOption = sourceData.requiresDepthOption;
                previousColorOption = sourceData.requiresColorOption;
                previousAntialiasing = sourceData.antialiasing;

                suppressedCamera = source;
                suppressedData = sourceData;
                source.cullingMask = 0;
                source.clearFlags = CameraClearFlags.SolidColor;
                source.backgroundColor = Color.black;
                sourceData.renderPostProcessing = false;
                sourceData.renderShadows = false;
                sourceData.requiresDepthOption = CameraOverrideOption.Off;
                sourceData.requiresColorOption = CameraOverrideOption.Off;
                sourceData.antialiasing = AntialiasingMode.None;
                SetState("mirroring", "Desktop mirror displays the left eye; original camera world geometry, shadows and post effects are suppressed.");
            }
            catch (Exception exception)
            {

                RestoreSource();
                HideCanvas();
                if (lastState != "error") log?.LogWarning("Desktop mirror unavailable; normal desktop rendering retained. " + exception);
                lastState = "error";
            }
        }

        public void RestoreSource()
        {
            var previous = suppressedData;
            var previousCamera = suppressedCamera;
            suppressedData = null;
            suppressedCamera = null;
            if (previousCamera)
            {
                if (previousCamera.cullingMask == 0) previousCamera.cullingMask = previousCullingMask;
                if (previousCamera.clearFlags == CameraClearFlags.SolidColor) previousCamera.clearFlags = previousClearFlags;
                if (previousCamera.backgroundColor == Color.black) previousCamera.backgroundColor = previousBackground;
            }
            if (previous)
            {
                if (!previous.renderPostProcessing) previous.renderPostProcessing = previousPostProcessing;
                if (!previous.renderShadows) previous.renderShadows = previousShadows;
                if (previous.requiresDepthOption == CameraOverrideOption.Off) previous.requiresDepthOption = previousDepthOption;
                if (previous.requiresColorOption == CameraOverrideOption.Off) previous.requiresColorOption = previousColorOption;
                if (previous.antialiasing == AntialiasingMode.None) previous.antialiasing = previousAntialiasing;
            }
        }

        void EnsureCanvas()
        {
            if (canvasObject && canvas && image) return;
            boundEyeTexture = null;
            eyeShown = false;
            eyeDisplay = mirrorScreenWidth = mirrorScreenHeight = -1;
            if (canvasObject) UObject.Destroy(canvasObject);
            canvasObject = new GameObject("AmanatsuLocationVR_DesktopMirror", new[]
            {
                Il2CppType.Of<RectTransform>(), Il2CppType.Of<Canvas>()
            });
            UObject.DontDestroyOnLoad(canvasObject);
            canvasObject.hideFlags = HideFlags.DontUnloadUnusedAsset;
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.enabled = false;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -32768;
            canvas.pixelPerfect = false;

            var picture = new GameObject("AmanatsuLocationVR_LeftEyeImage", new[] { Il2CppType.Of<RectTransform>() });
            picture.transform.SetParent(canvasObject.transform, false);
            var rect = picture.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            image = picture.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.color = Color.white;

        }

        void EnsureHudCanvas()
        {
            if (hudObject && hudCanvas && hudImage) return;
            HideHud();
            boundHudTexture = null;
            hudShown = false;
            hudDisplay = -1;
            if (hudObject) UObject.Destroy(hudObject);
            hudObject = new GameObject("AmanatsuLocationVR_DesktopHud", new[]
            {
                Il2CppType.Of<RectTransform>(), Il2CppType.Of<Canvas>()
            });
            UObject.DontDestroyOnLoad(hudObject);
            hudObject.hideFlags = HideFlags.DontUnloadUnusedAsset;
            hudCanvas = hudObject.GetComponent<Canvas>();
            hudCanvas.enabled = false;
            hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hudCanvas.sortingOrder = 32760;
            hudCanvas.pixelPerfect = false;

            var picture = new GameObject("AmanatsuLocationVR_DesktopHudImage", new[] { Il2CppType.Of<RectTransform>() });
            picture.transform.SetParent(hudObject.transform, false);
            var rect = picture.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            hudImage = picture.AddComponent<RawImage>();
            hudImage.raycastTarget = false;
            hudImage.color = Color.white;
            hudImage.uvRect = new Rect(0f, 0f, 1f, 1f);

        }

        static Rect CenteredCrop(int textureWidth, int textureHeight, int screenWidth, int screenHeight)
        {
            float textureAspect = Math.Max(1, textureWidth) / (float)Math.Max(1, textureHeight);
            float screenAspect = Math.Max(1, screenWidth) / (float)Math.Max(1, screenHeight);
            if (textureAspect > screenAspect)
            {
                float width = screenAspect / textureAspect;
                return new Rect((1f - width) * 0.5f, 0f, width, 1f);
            }
            float height = textureAspect / screenAspect;
            return new Rect(0f, (1f - height) * 0.5f, 1f, height);
        }

        void HideCanvas()
        {
            if (eyeShown && canvas) { canvas.enabled = false; ++presentationWrites; }
            if (!ReferenceEquals(boundEyeTexture, null) && image) { image.texture = null; ++presentationWrites; }
            eyeShown = false;
            boundEyeTexture = null;
        }

        void HideHud()
        {
            if (hudShown && hudCanvas) { hudCanvas.enabled = false; ++presentationWrites; }
            if (!ReferenceEquals(boundHudTexture, null) && hudImage) { hudImage.texture = null; ++presentationWrites; }
            hudShown = false;
            boundHudTexture = null;
        }

        void HideSubtitles()
        {
            if (subtitlesShown && subtitleCanvas) { subtitleCanvas.enabled = false; ++presentationWrites; }
            if (!ReferenceEquals(boundSubtitleTexture, null) && subtitleImage) { subtitleImage.texture = null; ++presentationWrites; }
            subtitlesShown = false;
            boundSubtitleTexture = null;
        }

        void SetState(string state, string message)
        {
            if (lastState == state) return;
            lastState = state;
            log?.LogInfo(message);
        }

        public void Dispose()
        {
            RestoreSource();
            if (disposed) return;
            disposed = true;
            HideCanvas();

            HideHud();
            HideSubtitles();
            if (canvasObject) UObject.Destroy(canvasObject);
            if (hudObject) UObject.Destroy(hudObject);
            if (subtitleObject) UObject.Destroy(subtitleObject);
            canvasObject = null;
            canvas = null;
            image = null;
            hudObject = null;
            hudCanvas = null;
            hudImage = null;
            subtitleObject = null;
            subtitleCanvas = null;
            subtitleImage = null;
            subtitleRect = null;
            cachedSource = null;
            cachedSourceObject = null;
            cachedSourceData = null;
        }
    }
}
