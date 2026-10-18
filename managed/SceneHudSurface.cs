using System;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace AmanatsuLocationVR
{

    internal sealed class SceneHudSurface : IDisposable
    {
        readonly ManualLogSource log;
        readonly Action<ScriptableRenderContext, Camera> beginAction, endAction;
        Il2CppSystem.Action<ScriptableRenderContext, Camera> beginDelegate, endDelegate;
        GameObject root;
        MeshRenderer renderer;
        Mesh mesh;
        Material material;
        RenderTexture boundTexture;
        Camera leftEye, rightEye;
        bool ownsLeftMaskBit, ownsRightMaskBit, active, disposed, logged, failedLogged;
        int layer = -1;
        long sceneSignature = long.MinValue;
        float nextRetry;
        float lastWidth, lastDistance, lastAspect, lastOffset, lastCurvature;
        bool lastFlip;
        bool hasGeometry;

        public SceneHudSurface(ManualLogSource logger)
        {
            log = logger;
            beginAction = BeginCamera;
            endAction = EndCamera;
        }

        public bool Active => active && root && renderer && mesh && material && boundTexture;

        public bool Prepare(RenderTexture texture, Camera left, Camera right,
            Vector3 framePosition, Quaternion frameRotation, float unitsPerMetre,
            float width, float distance, float aspect, float verticalOffset, float curvature,
            bool flipY, bool enabled)
        {
            RestoreEyeMasks();
            active = false;
            if (renderer) renderer.enabled = false;
            if (disposed || !enabled || !texture || !texture.IsCreated() || !left || !right)
            {
                Disable();
                return false;
            }
            if (Time.realtimeSinceStartup < nextRetry) return false;
            try
            {
                if (!Finite(unitsPerMetre) || unitsPerMetre <= 0f || !Finite(framePosition.x) || !Finite(framePosition.y) || !Finite(framePosition.z) ||
                    !Finite(frameRotation.x) || !Finite(frameRotation.y) || !Finite(frameRotation.z) || !Finite(frameRotation.w) ||
                    !HudSurfaceMath.TryLocalPoint(.5f, .5f, width, distance, aspect, verticalOffset, curvature, out _))
                    throw new ArgumentException("Scene HUD geometry must be finite with positive dimensions.");
                EnsureResources();
                ReserveLayer();
                if (!hasGeometry || width != lastWidth || distance != lastDistance || aspect != lastAspect ||
                    verticalOffset != lastOffset || curvature != lastCurvature || flipY != lastFlip)
                    UpdateMesh(width, distance, aspect, verticalOffset, curvature, flipY);
                if (boundTexture != texture)
                {
                    material.SetTexture("_BaseMap", texture);
                    boundTexture = texture;
                }
                root.transform.position = framePosition;
                root.transform.rotation = frameRotation;
                root.transform.localScale = Vector3.one * unitsPerMetre;
                leftEye = left;
                rightEye = right;
                int bit = 1 << layer;
                ownsLeftMaskBit = (left.cullingMask & bit) == 0;
                ownsRightMaskBit = (right.cullingMask & bit) == 0;
                if (ownsLeftMaskBit) left.cullingMask |= bit;
                if (ownsRightMaskBit) right.cullingMask |= bit;
                active = true;
                failedLogged = false;
                if (!logged)
                {
                    log?.LogInfo("Scene HUD active: URP Unlit, depth-tested against scene geometry, premultiplied alpha, visible only to VR eyes.");
                    logged = true;
                }
                return true;
            }
            catch (Exception exception)
            {
                Disable();
                nextRetry = Time.realtimeSinceStartup + 2f;
                if (!failedLogged) log?.LogWarning("Scene HUD unavailable; compositor HUD retained: " + exception);
                failedLogged = true;
                return false;
            }
        }

        void EnsureResources()
        {
            if (root && renderer && mesh && material) return;
            Disable();
            DestroyResources();

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader || !shader.isSupported) throw new InvalidOperationException("The game's URP Unlit shader is unavailable.");
            material = new Material(shader) { name = "AmanatsuLocationVR_SceneHudMaterial", hideFlags = HideFlags.DontUnloadUnusedAsset };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_AlphaClip", 0f);
            material.SetColor("_BaseColor", Color.white);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent + 100;
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("DepthNormalsOnly", false);
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.SetShaderPassEnabled("MotionVectors", false);
            material.SetShaderPassEnabled("XRMotionVectors", false);
            root = new GameObject("AmanatsuLocationVR_SceneHud");
            UObject.DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.DontUnloadUnusedAsset;
            mesh = new Mesh { name = "AmanatsuLocationVR_SceneHudMesh", hideFlags = HideFlags.DontUnloadUnusedAsset };
            mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = root.AddComponent<MeshRenderer>();
            renderer.enabled = false;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            beginDelegate = beginAction;
            endDelegate = endAction;
            RenderPipelineManager.add_beginCameraRendering(beginDelegate);
            RenderPipelineManager.add_endCameraRendering(endDelegate);
        }

        void ReserveLayer()
        {
            long signature = 17;
            for (int index = 0; index < SceneManager.sceneCount; ++index)
                signature = unchecked(signature * 31 + SceneManager.GetSceneAt(index).handle);
            if (layer >= 0 && signature == sceneSignature) return;
            uint occupied = UiHud.ReservedLayerMask;
            if (layer >= 0) occupied &= ~(1u << layer);
            foreach (var value in UObject.FindObjectsOfType<Renderer>())
                if (value && value != renderer) occupied |= 1u << value.gameObject.layer;
            foreach (var value in UObject.FindObjectsOfType<CanvasRenderer>())
                if (value) occupied |= 1u << value.gameObject.layer;
            if (layer < 0 || (occupied & (1u << layer)) != 0)
            {
                int selected = -1;
                for (int index = 31; index >= 8; --index)
                    if ((occupied & (1u << index)) == 0) { selected = index; break; }
                if (selected < 0) throw new InvalidOperationException("No unused renderer layer is available for the scene HUD.");
                if (layer >= 0) UiHud.ReleasePresentationLayer(layer);
                layer = selected;
                UiHud.ReservePresentationLayer(layer);
                root.layer = layer;
            }
            sceneSignature = signature;
        }

        void UpdateMesh(float width, float distance, float aspect, float offset, float curvature, bool flipY)
        {
            int segments = HudSurfaceMath.HorizontalSegments;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            for (int index = 0; index <= segments; ++index)
            {
                float u = index / (float)segments;
                if (!HudSurfaceMath.TryLocalPoint(u, 0f, width, distance, aspect, offset, curvature, out vertices[index * 2]) ||
                    !HudSurfaceMath.TryLocalPoint(u, 1f, width, distance, aspect, offset, curvature, out vertices[index * 2 + 1]))
                    throw new InvalidOperationException("Scene HUD mesh contains an invalid vertex.");
                uv[index * 2] = new Vector2(u, HudSurfaceMath.ScreenToSurfaceV(0f, flipY));
                uv[index * 2 + 1] = new Vector2(u, HudSurfaceMath.ScreenToSurfaceV(1f, flipY));
                if (index == segments) continue;
                int vertex = index * 2, triangle = index * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex + 2;
                triangles[triangle + 4] = vertex + 1;
                triangles[triangle + 5] = vertex + 3;
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            lastWidth = width; lastDistance = distance; lastAspect = aspect;
            lastOffset = offset; lastCurvature = curvature; lastFlip = flipY;
            hasGeometry = true;
        }

        void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (renderer) renderer.enabled = Active && (camera == leftEye || camera == rightEye);
        }
        void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (renderer) renderer.enabled = false;
        }

        void RestoreEyeMasks()
        {
            if (layer >= 0)
            {
                int bit = 1 << layer;
                if (ownsLeftMaskBit && leftEye) leftEye.cullingMask &= ~bit;
                if (ownsRightMaskBit && rightEye) rightEye.cullingMask &= ~bit;
            }
            ownsLeftMaskBit = ownsRightMaskBit = false;
            leftEye = rightEye = null;
        }

        public void Disable()
        {
            active = false;
            if (renderer) renderer.enabled = false;
            RestoreEyeMasks();
            if (material && boundTexture) material.SetTexture("_BaseMap", null);
            boundTexture = null;
            if (layer >= 0) UiHud.ReleasePresentationLayer(layer);
            layer = -1;
            sceneSignature = long.MinValue;
        }

        void DestroyResources()
        {
            if (beginDelegate != null) RenderPipelineManager.remove_beginCameraRendering(beginDelegate);
            if (endDelegate != null) RenderPipelineManager.remove_endCameraRendering(endDelegate);
            beginDelegate = endDelegate = null;
            if (renderer) renderer.enabled = false;
            if (root) UObject.Destroy(root);
            if (mesh) UObject.Destroy(mesh);
            if (material) UObject.Destroy(material);
            root = null; renderer = null; mesh = null; material = null;
            boundTexture = null;
            hasGeometry = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            Disable();
            disposed = true;
            DestroyResources();
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
