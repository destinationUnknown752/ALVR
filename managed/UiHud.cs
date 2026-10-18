using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace AmanatsuLocationVR
{

    internal sealed class UiHud : IDisposable
    {
        static readonly HashSet<int> reservedLayers = new HashSet<int>();
        internal static uint ReservedLayerMask
        {
            get { uint mask = 0; foreach (int layer in reservedLayers) mask |= 1u << layer; return mask; }
        }
        internal static void ReservePresentationLayer(int layer) { reservedLayers.Add(layer); }
        internal static void ReleasePresentationLayer(int layer) { reservedLayers.Remove(layer); }
        readonly ManualLogSource log;
        readonly bool subtitlePass;
        readonly string label;
        Canvas onlyCanvas, excludedCanvas;
        bool renderVisible = true;
        readonly List<CanvasState> canvases = new List<CanvasState>();
        readonly Dictionary<int, CanvasState> canvasById = new Dictionary<int, CanvasState>();
        readonly List<LayerState> layers = new List<LayerState>();
        readonly Dictionary<int, LayerState> layerById = new Dictionary<int, LayerState>();
        readonly HashSet<int> capturedObjects = new HashSet<int>();
        readonly HashSet<int> candidateIds = new HashSet<int>();
        readonly List<Canvas> candidates = new List<Canvas>();
        readonly List<LayerState> pendingLayerChanges = new List<LayerState>();
        readonly List<GameObject> cursorObjects = new List<GameObject>();
        readonly Action<ScriptableRenderContext, Camera> renderAction;
        Il2CppSystem.Action<ScriptableRenderContext, Camera> renderDelegate;
        GameObject cameraObject;
        Camera camera;
        UniversalAdditionalCameraData cameraData;
        RenderTexture texture;
        GameObject cursorObject;
        Canvas cursorCanvas;
        RectTransform cursorPosition;
        bool active, disposed, renderFailed;
        int captureLayer = -1;
        int captureDisplay = -1;
        float nextDiscovery;
        int renderedFrames;
        long prepareTicks, discoveryTicks;
        int prepareSamples, renderSamples, discoverySamples;
        int rootAdoptions, worldScans, hierarchyScans, descendantVisits;
        long worldSceneSignature = long.MinValue;
        string state;
        string activeStateKey, activeStateMessage;
        int reportedRoots = -1, reportedWidth, reportedHeight;

        public UiHud(ManualLogSource logger, bool subtitlesOnly = false)
        {
            log = logger;
            subtitlePass = subtitlesOnly;
            label = subtitlesOnly ? "Subtitles" : "HUD";
            renderAction = Render;
        }

        public RenderTexture Texture => active && !renderFailed && texture && texture.IsCreated() ? texture : null;
        public bool Failed => renderFailed;

        public void SetExcludedCanvas(Canvas value)
        {
            if (excludedCanvas == value) return;
            excludedCanvas = value;
            nextDiscovery = 0f;
        }

        public void PrepareSubtitle(Camera source, Canvas value, bool visible)
        {
            if (!subtitlePass) throw new InvalidOperationException("A subtitle capture instance is required.");
            if (onlyCanvas != value) { onlyCanvas = value; nextDiscovery = 0f; }
            renderVisible = visible;
            Prepare(source, value);
        }

        public string TimingSummary
        {
            get
            {
                double milliseconds = 1000.0 / Stopwatch.Frequency;
                string result = $"{label} CPU: prepare {(prepareSamples == 0 ? 0 : prepareTicks * milliseconds / prepareSamples):F2} ms, " +
                    $"URP capture callbacks {renderSamples}, " +
                    $"discovery {(discoverySamples == 0 ? 0 : discoveryTicks * milliseconds / discoverySamples):F2} ms; " +
                    $"samples {prepareSamples}/{renderSamples}/{discoverySamples}; roots adopted {rootAdoptions}, " +
                    $"world scans {worldScans}, hierarchy scans {hierarchyScans}, descendant visits {descendantVisits}, cached objects {layers.Count}";
                prepareTicks = discoveryTicks = 0;
                prepareSamples = renderSamples = discoverySamples = 0;
                rootAdoptions = worldScans = hierarchyScans = descendantVisits = 0;
                return result;
            }
        }

        public void Prepare(Camera source, bool enabled)
        {
            long started = Stopwatch.GetTimestamp();
            try { PrepareCore(source, enabled); }
            finally { prepareTicks += Stopwatch.GetTimestamp() - started; ++prepareSamples; }
        }

        void PrepareCore(Camera source, bool enabled)
        {
            if (disposed) return;

            if (!enabled) renderFailed = false;
            if (!enabled || renderFailed || Screen.width < 4 || Screen.height < 4)
            {
                RestoreCanvases();
                return;
            }

            try
            {
                int display = source ? source.targetDisplay : 0;
                bool displayChanged = captureDisplay != display;
                if (displayChanged)
                {
                    RestoreCanvases();
                    captureDisplay = display;
                }
                int width = subtitlePass ? 1024 : Screen.width;
                int height = subtitlePass ? 256 : Screen.height;
                bool resized = !texture || texture.width != width || texture.height != height || !texture.IsCreated();
                EnsureResources(width, height);
                if (displayChanged) camera.targetDisplay = display;
                if (resized)
                {
                    camera.orthographicSize = texture.height * 0.5f;
                    camera.aspect = texture.width / (float)texture.height;
                }
                bool changed = false;
                if (displayChanged || resized || Time.realtimeSinceStartup >= nextDiscovery)
                {
                    long started = Stopwatch.GetTimestamp();
                    try { changed = Reconcile(display); }
                    finally { discoveryTicks += Stopwatch.GetTimestamp() - started; ++discoverySamples; }
                    nextDiscovery = Time.realtimeSinceStartup + 0.5f;
                }
                if (canvases.Count == 0)
                {
                    active = false;
                    if (camera.enabled) camera.enabled = false;
                    if (cursorCanvas && cursorCanvas.enabled) cursorCanvas.enabled = false;
                    if (changed) Canvas.ForceUpdateCanvases();
                    SetState("no-ui", label + " capture waiting for screen-space game UI.");
                    return;
                }
                if (!subtitlePass) UpdateCursor(display);

                if (changed || resized) Canvas.ForceUpdateCanvases();
                if (camera.enabled != renderVisible) camera.enabled = renderVisible;
                active = true;
                if (reportedRoots != canvases.Count || reportedWidth != width || reportedHeight != height)
                {
                    reportedRoots = canvases.Count; reportedWidth = width; reportedHeight = height;
                    activeStateKey = "active:" + reportedRoots + ":" + width + "x" + height;
                    activeStateMessage = $"{label} capture active: {reportedRoots} persistent root canvases, {width}x{height} RGBA, layer {captureLayer}; discovery every 0.5 s.";
                }
                if (state != activeStateKey) SetState(activeStateKey, activeStateMessage);
            }
            catch (Exception exception)
            {
                RestoreCanvases();
                renderFailed = true;
                active = false;
                if (camera) camera.enabled = false;
                SetState("error", label + " capture unavailable; original game UI retained. " + exception, true);
            }
        }

        bool Reconcile(int display)
        {
            bool changed = false;
            candidates.Clear();
            candidateIds.Clear();
            foreach (var existing in EnumerateCanvases())
            {
                if (!existing || !existing.isActiveAndEnabled || !existing.isRootCanvas || IsOwned(existing.transform)) continue;
                if (existing == excludedCanvas) continue;
                int id = existing.GetInstanceID();
                if (canvasById.TryGetValue(id, out var saved) && !saved.IsCapturedBy(camera))
                {

                    saved.Restore(camera);
                    canvases.Remove(saved);
                    canvasById.Remove(id);
                    changed = true;
                    saved = null;
                }
                if (saved == null)
                {
                    if (existing.renderMode == RenderMode.WorldSpace) continue;
                    var originalCamera = existing.worldCamera;
                    int canvasDisplay = existing.targetDisplay;
                    if (existing.renderMode == RenderMode.ScreenSpaceCamera && originalCamera)
                    {
                        if (originalCamera.targetTexture != null) continue;
                        canvasDisplay = originalCamera.targetDisplay;
                    }
                    if (canvasDisplay != display) continue;
                    saved = new CanvasState(existing);
                    canvases.Add(saved);
                    canvasById.Add(id, saved);
                    existing.renderMode = RenderMode.ScreenSpaceCamera;
                    existing.worldCamera = camera;
                    existing.planeDistance = 1f;
                    ++rootAdoptions;
                    changed = true;
                }
                candidates.Add(existing);
                candidateIds.Add(id);
            }
            for (int index = canvases.Count - 1; index >= 0; --index)
            {
                var saved = canvases[index];
                if (candidateIds.Contains(saved.Id)) continue;
                saved.Restore(camera);
                canvasById.Remove(saved.Id);
                canvases.RemoveAt(index);
                changed = true;
            }

            bool refreshLayers = changed;
            float now = Time.realtimeSinceStartup;
            capturedObjects.Clear();
            pendingLayerChanges.Clear();
            foreach (var saved in canvases)
            {
                int count = saved.Transform.hierarchyCount;
                if (count != saved.HierarchyCount || now >= saved.NextHierarchyScan)
                {
                    ++hierarchyScans;
                    refreshLayers = true;
                    saved.LayerObjects.Clear();
                    saved.LayerObjectIds.Clear();
                    changed |= TrackLayerObject(saved.Object, saved);
                    foreach (var renderer in saved.Canvas.GetComponentsInChildren<CanvasRenderer>(true))
                    {
                        ++descendantVisits;

                        if (renderer) changed |= TrackLayerObject(renderer.gameObject, saved);
                    }
                    foreach (var childCanvas in saved.Canvas.GetComponentsInChildren<Canvas>(true))
                    {
                        ++descendantVisits;
                        if (childCanvas) changed |= TrackLayerObject(childCanvas.gameObject, saved);
                    }
                    saved.HierarchyCount = count;
                    saved.NextHierarchyScan = now + 2f;
                }
                else
                {

                    foreach (var item in saved.LayerObjects) capturedObjects.Add(item.Id);
                }
            }
            if (refreshLayers)
            {
                for (int index = layers.Count - 1; index >= 0; --index)
                {
                    var saved = layers[index];
                    if (capturedObjects.Contains(saved.Id)) continue;
                    saved.Restore(captureLayer);
                    layerById.Remove(saved.Id);
                    layers.RemoveAt(index);
                    changed = true;
                }
            }
            if (canvases.Count == 0) return changed;

            long sceneSignature = 17;
            int sceneCount = SceneManager.sceneCount;
            for (int index = 0; index < sceneCount; ++index)
                sceneSignature = unchecked(sceneSignature * 31 + SceneManager.GetSceneAt(index).handle);
            bool layerChanged = false;
            if (captureLayer < 0 || sceneSignature != worldSceneSignature)
            {
                ++worldScans;
                uint occupied = 0;
                foreach (var renderer in UObject.FindObjectsOfType<Renderer>())
                    if (renderer && !capturedObjects.Contains(renderer.gameObject.GetInstanceID())) occupied |= 1u << renderer.gameObject.layer;
                foreach (var renderer in UObject.FindObjectsOfType<CanvasRenderer>())
                    if (renderer && !capturedObjects.Contains(renderer.gameObject.GetInstanceID()) && !IsOwned(renderer.transform)) occupied |= 1u << renderer.gameObject.layer;

                foreach (int reserved in reservedLayers)
                    if (reserved != captureLayer) occupied |= 1u << reserved;
                if (captureLayer < 0 || (occupied & (1u << captureLayer)) != 0)
                {
                    int selectedLayer = -1;
                    for (int index = 31; index >= 8; --index)
                        if ((occupied & (1u << index)) == 0) { selectedLayer = index; break; }
                    if (selectedLayer < 0) throw new InvalidOperationException("No unused renderer layer is available for UI capture.");
                    if (captureLayer >= 0) reservedLayers.Remove(captureLayer);
                    captureLayer = selectedLayer;
                    reservedLayers.Add(captureLayer);
                    camera.cullingMask = 1 << captureLayer;
                    changed = layerChanged = true;
                }
                worldSceneSignature = sceneSignature;
            }
            foreach (var saved in layerChanged ? layers : pendingLayerChanges)
                if (saved.Object && saved.Object.layer != captureLayer) { saved.Object.layer = captureLayer; changed = true; }
            return changed;
        }

        IEnumerable<Canvas> EnumerateCanvases()
        {
            if (subtitlePass)
            {
                if (onlyCanvas) yield return onlyCanvas;
                yield break;
            }
            foreach (var value in UObject.FindObjectsOfType<Canvas>()) yield return value;
        }

        bool TrackLayerObject(GameObject value, CanvasState owner)
        {
            int id = value.GetInstanceID();
            if (!owner.LayerObjectIds.Add(id)) return false;
            bool firstVisit = capturedObjects.Add(id);
            if (layerById.TryGetValue(id, out var saved))
            {

                owner.LayerObjects.Add(saved);
                if (!firstVisit) return false;

                if (value.layer != captureLayer)
                {
                    saved.Layer = value.layer;
                    pendingLayerChanges.Add(saved);
                    return true;
                }
                return false;
            }
            saved = new LayerState(value);
            layers.Add(saved);
            layerById.Add(id, saved);
            owner.LayerObjects.Add(saved);
            pendingLayerChanges.Add(saved);
            return true;
        }

        static bool IsOwned(Transform value)
        {
            while (value)
            {
                if (value.name.StartsWith("AmanatsuLocationVR_", StringComparison.Ordinal)) return true;
                value = value.parent;
            }
            return false;
        }

        void UpdateCursor(int display)
        {
            if (!Cursor.visible || Cursor.lockState == CursorLockMode.Locked)
            {
                if (cursorCanvas && cursorCanvas.enabled) cursorCanvas.enabled = false;
                return;
            }
            EnsureCursor();
            if (cursorCanvas.worldCamera != camera) cursorCanvas.worldCamera = camera;
            if (cursorCanvas.targetDisplay != display) cursorCanvas.targetDisplay = display;
            foreach (var value in cursorObjects)
                if (value && value.layer != captureLayer) value.layer = captureLayer;
            var mouse = Input.mousePosition;

            var position = new Vector3(mouse.x, mouse.y, 0f);
            if (cursorPosition.anchoredPosition3D != position) cursorPosition.anchoredPosition3D = position;
            if (!cursorCanvas.enabled) cursorCanvas.enabled = true;
        }

        void EnsureCursor()
        {
            if (cursorObject && cursorCanvas && cursorPosition) return;
            if (cursorObject) UObject.Destroy(cursorObject);
            cursorObjects.Clear();
            cursorObject = new GameObject("AmanatsuLocationVR_HudCursor", new[]
            {
                Il2CppType.Of<RectTransform>(), Il2CppType.Of<Canvas>()
            });
            UObject.DontDestroyOnLoad(cursorObject);
            cursorObject.hideFlags = HideFlags.DontUnloadUnusedAsset;
            cursorObjects.Add(cursorObject);
            cursorCanvas = cursorObject.GetComponent<Canvas>();
            cursorCanvas.enabled = false;
            cursorCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            cursorCanvas.worldCamera = camera;
            cursorCanvas.planeDistance = 0.99f;
            cursorCanvas.sortingOrder = 32767;
            cursorCanvas.scaleFactor = 1f;
            cursorCanvas.pixelPerfect = false;
            var canvasRect = cursorObject.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
            canvasRect.localScale = Vector3.one;

            var point = new GameObject("AmanatsuLocationVR_HudCursorPosition", new[] { Il2CppType.Of<RectTransform>() });
            point.transform.SetParent(cursorObject.transform, false);
            cursorObjects.Add(point);
            cursorPosition = point.GetComponent<RectTransform>();
            cursorPosition.anchorMin = Vector2.zero;
            cursorPosition.anchorMax = Vector2.zero;
            cursorPosition.pivot = new Vector2(0.5f, 0.5f);
            cursorPosition.sizeDelta = new Vector2(14f, 14f);
            cursorPosition.localScale = Vector3.one;

            AddCursorBar("OutlineHorizontal", 14f, 3f, Color.black);
            AddCursorBar("OutlineVertical", 3f, 14f, Color.black);
            AddCursorBar("WhiteHorizontal", 12f, 1f, Color.white);
            AddCursorBar("WhiteVertical", 1f, 12f, Color.white);

        }

        void AddCursorBar(string name, float width, float height, Color color)
        {
            var value = new GameObject("AmanatsuLocationVR_HudCursor" + name, new[] { Il2CppType.Of<RectTransform>() });
            value.transform.SetParent(cursorPosition, false);
            cursorObjects.Add(value);
            var rect = value.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition3D = Vector3.zero;
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
            var image = value.AddComponent<RawImage>();
            image.texture = Texture2D.whiteTexture;
            image.color = color;
            image.raycastTarget = false;
        }

        void EnsureResources(int width, int height)
        {
            if (!cameraObject)
            {
                cameraObject = new GameObject("AmanatsuLocationVR_" + label + "Camera");
                UObject.DontDestroyOnLoad(cameraObject);
                cameraObject.hideFlags = HideFlags.DontUnloadUnusedAsset;
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.allowDynamicResolution = false;
                camera.useOcclusionCulling = false;
                camera.depth = 10000f;

                camera.transform.position = new Vector3(0f, -10000f, -10000f);
                camera.transform.rotation = Quaternion.identity;
                cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
                cameraData.renderType = CameraRenderType.Base;
                cameraData.renderShadows = false;
                cameraData.renderPostProcessing = false;
                cameraData.volumeLayerMask = 0;
                cameraData.antialiasing = AntialiasingMode.None;
                cameraData.requiresDepthOption = CameraOverrideOption.Off;
                cameraData.requiresColorOption = CameraOverrideOption.Off;
                cameraData.allowXRRendering = false;
                cameraData.allowHDROutput = false;
                cameraData.stopNaN = false;
                cameraData.dithering = false;
                renderDelegate = renderAction;
                RenderPipelineManager.add_endCameraRendering(renderDelegate);
            }
            if (texture && texture.width == width && texture.height == height && texture.IsCreated()) return;
            camera.targetTexture = null;
            if (texture) { texture.Release(); UObject.Destroy(texture); }
            texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "AmanatsuLocationVR_" + label + "Texture",
                hideFlags = HideFlags.DontUnloadUnusedAsset,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!texture.Create()) throw new InvalidOperationException("Could not allocate the UI RenderTexture.");
            camera.targetTexture = texture;
            var previous = RenderTexture.active;
            try { RenderTexture.active = texture; GL.Clear(true, true, Color.clear); }
            finally { RenderTexture.active = previous; }
        }

        void Render(ScriptableRenderContext context, Camera renderedCamera)
        {
            if (!active || disposed || renderFailed || !camera || renderedCamera != camera || !texture) return;

            ++renderSamples;
            if (++renderedFrames == 1)
                log?.LogInfo(label + " URP capture camera completed; transparent UI texture available.");
        }

        public void RestoreCanvases()
        {
            active = false;
            nextDiscovery = 0f;
            captureDisplay = -1;
            if (cursorCanvas && cursorCanvas.enabled) cursorCanvas.enabled = false;
            bool changed = canvases.Count != 0 || layers.Count != 0;
            for (int index = canvases.Count - 1; index >= 0; --index)
            {
                try { canvases[index].Restore(camera); }
                catch (Exception exception) { log?.LogWarning("Could not restore one UI canvas: " + exception.Message); }
            }
            canvases.Clear();
            canvasById.Clear();
            for (int index = layers.Count - 1; index >= 0; --index)
                layers[index].Restore(captureLayer);
            layers.Clear();
            layerById.Clear();
            capturedObjects.Clear();
            candidates.Clear();
            candidateIds.Clear();
            pendingLayerChanges.Clear();
            if (captureLayer >= 0) reservedLayers.Remove(captureLayer);
            captureLayer = -1;
            worldSceneSignature = long.MinValue;
            if (camera && camera.enabled) camera.enabled = false;

            if (changed) Canvas.ForceUpdateCanvases();
        }

        void SetState(string value, string message, bool warning = false)
        {
            if (state == value) return;
            state = value;
            if (warning) log?.LogWarning(message); else log?.LogInfo(message);
        }

        public void Dispose()
        {
            RestoreCanvases();
            if (disposed) return;
            disposed = true;
            active = false;
            if (renderDelegate != null) RenderPipelineManager.remove_endCameraRendering(renderDelegate);
            if (camera) camera.targetTexture = null;
            if (texture) { texture.Release(); UObject.Destroy(texture); }
            if (cameraObject) UObject.Destroy(cameraObject);
            if (cursorObject) UObject.Destroy(cursorObject);
            cursorObjects.Clear();
            cursorObject = null;
            cursorCanvas = null;
            cursorPosition = null;
            texture = null;
            camera = null;
            cameraData = null;
            cameraObject = null;
            renderDelegate = null;
        }

        sealed class LayerState
        {
            public readonly GameObject Object;
            public readonly int Id;
            public int Layer;
            public LayerState(GameObject value) { Object = value; Id = value.GetInstanceID(); Layer = value.layer; }
            public void Restore(int ownedLayer)
            {
                if (Object && Object.layer == ownedLayer) Object.layer = Layer;
            }
        }

        sealed class CanvasState
        {
            readonly Canvas canvas;
            public readonly int Id;
            public Canvas Canvas => canvas;
            public readonly GameObject Object;
            public readonly Transform Transform;
            public readonly List<LayerState> LayerObjects = new List<LayerState>();
            public readonly HashSet<int> LayerObjectIds = new HashSet<int>();
            public int HierarchyCount = -1;
            public float NextHierarchyScan;
            readonly RenderMode mode;
            readonly Camera worldCamera;
            readonly float planeDistance;
            readonly RectTransform rect;
            readonly Vector2 anchorMin, anchorMax, pivot, sizeDelta;
            readonly Vector3 anchoredPosition, localScale;
            readonly Quaternion localRotation;

            public CanvasState(Canvas value)
            {
                canvas = value;
                Id = value.GetInstanceID();
                Object = value.gameObject;
                Transform = value.transform;
                mode = value.renderMode;
                worldCamera = value.worldCamera;
                planeDistance = value.planeDistance;
                rect = value.GetComponent<RectTransform>();
                if (!rect) return;
                anchorMin = rect.anchorMin;
                anchorMax = rect.anchorMax;
                pivot = rect.pivot;
                sizeDelta = rect.sizeDelta;
                anchoredPosition = rect.anchoredPosition3D;
                localRotation = rect.localRotation;
                localScale = rect.localScale;
            }

            public bool IsCapturedBy(Camera captureCamera)
            {
                return canvas && canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == captureCamera;
            }

            public void Restore(Camera captureCamera)
            {
                if (!canvas) return;
                if (!IsCapturedBy(captureCamera))
                {

                    if (canvas.worldCamera == captureCamera)
                    {
                        canvas.worldCamera = worldCamera;
                        if (canvas.planeDistance == 1f) canvas.planeDistance = planeDistance;
                    }
                    return;
                }
                canvas.renderMode = mode;
                canvas.worldCamera = worldCamera;
                if (canvas.planeDistance == 1f) canvas.planeDistance = planeDistance;

                if (!rect) return;
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.pivot = pivot;
                rect.sizeDelta = sizeDelta;
                rect.anchoredPosition3D = anchoredPosition;
                rect.localRotation = localRotation;
                rect.localScale = localScale;
            }
        }
    }
}
