using System;
using System.Collections.Generic;
using System.Reflection;
using AL;
using AL.H.UI;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AmanatsuLocationVR
{

    internal sealed class WorldPointer : IDisposable
    {
        const string HarmonyId = "local.amanatsulocationvr.world-pointer";
        static WorldPointer current;
        readonly ManualLogSource log;
        readonly Harmony harmony = new Harmony(HarmonyId);
        readonly List<CameraSwap> pending = new List<CameraSwap>();
        readonly float[] queriedPoint = new float[3];
        GameObject proxyObject;
        Camera proxy, source;
        Vector3 basePosition, headTranslationMetres, rawHeadPositionMetres;
        Quaternion baseRotation, headRotation, rawHeadRotation;
        float unitsPerMetre, hudWidth, hudDistance, hudAspect, hudVerticalOffset, hudCurvature;
        bool enabled, installed, disposed, failed, sceneSurface, hudFlipY, queryWarning, pickerWarning;
        bool loggedLocation, loggedCharacter;

        public WorldPointer(ManualLogSource logger) { log = logger; current = this; }

        public void Configure(Camera sourceCamera, Vector3 framePosition, Quaternion frameRotation,
            Vector3 appliedHeadTranslationMetres, Quaternion appliedHeadRotation,
            Vector3 rawHeadPosition, Quaternion rawHeadOrientation, float unitsPerMeter,
            float widthMetres, float distanceMetres, float aspect, float verticalOffsetMetres,
            float appliedCurvature, bool sceneHud, bool flipY, bool active)
        {
            if (disposed) return;
            RetryRestores();
            if (!active || !sourceCamera) { Disable(); return; }
            source = sourceCamera; basePosition = framePosition; baseRotation = frameRotation;
            headTranslationMetres = appliedHeadTranslationMetres; headRotation = appliedHeadRotation;
            rawHeadPositionMetres = rawHeadPosition; rawHeadRotation = rawHeadOrientation;
            unitsPerMetre = unitsPerMeter; hudWidth = widthMetres; hudDistance = distanceMetres;
            hudAspect = aspect; hudVerticalOffset = verticalOffsetMetres; hudCurvature = appliedCurvature;
            sceneSurface = sceneHud; hudFlipY = flipY; enabled = true;
            if (!installed && !failed) Install();
        }

        void Install()
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var callbacks = typeof(PointSetting).GetNestedType("__c", BindingFlags.Public | BindingFlags.NonPublic);
                var location = callbacks?.GetMethod("_Initialize_b__17_2", flags);
                var character = typeof(OutlinableManager).GetMethod("UpdateProc", flags, null, Type.EmptyTypes, null);
                if (location == null || location.ReturnType != typeof(void) || location.GetParameters().Length != 2 ||
                    location.GetParameters()[1].ParameterType != typeof(PointSetting) || character == null || character.ReturnType != typeof(void))
                    throw new MissingMethodException("Verified AL character/location picker signatures have changed.");
                var finalizer = new HarmonyMethod(typeof(WorldPointer).GetMethod(nameof(AfterPicker), BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(location, prefix: new HarmonyMethod(typeof(WorldPointer).GetMethod(nameof(BeforeLocationPicker), BindingFlags.Static | BindingFlags.NonPublic)), finalizer: finalizer);
                harmony.Patch(character, prefix: new HarmonyMethod(typeof(WorldPointer).GetMethod(nameof(BeforeCharacterPicker), BindingFlags.Static | BindingFlags.NonPublic)), finalizer: finalizer);
                installed = true;
                log?.LogInfo("HUD world picking hooked: action-map characters and Change Location; camera swaps are scoped to each raycast callback.");
            }
            catch (Exception ex)
            {
                harmony.UnpatchSelf(); failed = true;
                log?.LogWarning("HUD world picking unavailable; original game cameras retained: " + ex.Message);
            }
        }

        static void BeforeLocationPicker(PointSetting self, out CameraSwap __state)
        {
            __state = null;
            var owner = current;
            if (owner == null || !owner.CanPick || self == null) return;
            try
            {

                if (!owner.UpdateProxy()) return;
                var swap = new CameraSwap(owner, self, null, self._camera);
                owner.pending.Add(swap); __state = swap;
                self._camera = owner.proxy;
                if (!owner.loggedLocation) { owner.loggedLocation = true; owner.log?.LogInfo("Change Location now follows the visible HUD cursor."); }
            }
            catch (Exception ex) { owner.WarnPicker(ex); owner.Finish(__state); __state = null; }
        }

        static void BeforeCharacterPicker(OutlinableManager __instance, out CameraSwap __state)
        {
            __state = null;
            var owner = current;
            if (owner == null || !owner.CanPick || !__instance) return;
            try
            {
                if (!owner.UpdateProxy()) return;
                var swap = new CameraSwap(owner, null, __instance, __instance._mainCamera);
                owner.pending.Add(swap); __state = swap;
                __instance._mainCamera = owner.proxy;
                if (!owner.loggedCharacter) { owner.loggedCharacter = true; owner.log?.LogInfo("Action-map character selection now follows the visible HUD cursor."); }
            }
            catch (Exception ex) { owner.WarnPicker(ex); owner.Finish(__state); __state = null; }
        }

        static void AfterPicker(CameraSwap __state) { __state?.Owner.Finish(__state); }
        bool CanPick
        {
            get
            {
                if (!enabled || !installed || disposed || !source) return false;
                foreach (var swap in pending) if (swap.Finished) return false;
                return true;
            }
        }

        void Finish(CameraSwap swap)
        {
            if (swap == null) return;
            swap.Finished = true;
            if (TryRestore(swap)) pending.Remove(swap);
            TryDestroyProxy();
        }

        void RetryRestores()
        {
            for (int i = pending.Count - 1; i >= 0; --i)
                if (pending[i].Finished && TryRestore(pending[i])) pending.RemoveAt(i);
        }

        bool TryRestore(CameraSwap swap)
        {
            try
            {

                if (swap.Location != null && swap.Location._camera == proxy) swap.Location._camera = swap.Original;
                if (swap.Character && swap.Character._mainCamera == proxy) swap.Character._mainCamera = swap.Original;
                return true;
            }
            catch (Exception ex) { WarnPicker(ex); return false; }
        }

        void WarnPicker(Exception ex)
        {
            if (pickerWarning) return;
            pickerWarning = true;
            log?.LogWarning("HUD picker camera operation failed; restoration will retry: " + ex.Message);
        }

        bool UpdateProxy()
        {
            if (!CanPick || Screen.width < 4 || Screen.height < 4 ||
                !Finite(unitsPerMetre) || unitsPerMetre <= 0f || !Finite(hudCurvature) || hudCurvature < 0f) return false;
            var mouse = Input.mousePosition;
            float u = mouse.x / Screen.width, v = mouse.y / Screen.height;
            if (!WorldPointerMath.ValidUv(u, v)) return false;
            float surfaceV = HudSurfaceMath.ScreenToSurfaceV(v, hudFlipY);
            Vector3 origin, direction;
            if (sceneSurface)
            {
                if (!HudSurfaceMath.TryLocalPoint(u, surfaceV, hudWidth, hudDistance, hudAspect, hudVerticalOffset, hudCurvature, out var point) ||
                    !WorldPointerMath.TrySurfaceRay(basePosition, baseRotation, headTranslationMetres, unitsPerMetre, point, out origin, out direction)) return false;
            }
            else if (hudCurvature > 0f)
            {
                try
                {
                    if (Native.RGVR_GetHudPoint(u, surfaceV, queriedPoint) != 1)
                    { WarnQuery("SteamVR could not verify the curved panel point."); return false; }
                    var point = new Vector3(queriedPoint[0], queriedPoint[1], -queriedPoint[2]);
                    if (!WorldPointerMath.TryCurvedRay(basePosition, baseRotation, headTranslationMetres, headRotation,
                        rawHeadPositionMetres, rawHeadRotation, point, unitsPerMetre, out origin, out direction)) return false;
                }
                catch (Exception ex) { WarnQuery(ex.Message); return false; }
            }
            else if (!WorldPointerMath.TryFlatRay(basePosition, baseRotation, headTranslationMetres, unitsPerMetre,
                hudWidth, hudDistance, hudAspect, hudVerticalOffset, u, surfaceV, out origin, out direction)) return false;

            float pointDistance = (float)Math.Sqrt(direction.sqrMagnitude) * unitsPerMetre;
            float near = Mathf.Max(0.0001f, Mathf.Min(0.01f * unitsPerMetre, pointDistance * 0.25f));
            float far = Mathf.Max(source.farClipPlane, near + 1f);

            if (!WorldPointerMath.TryPixelProjection(u, v, near, far, out var projection) ||
                !WorldPointerMath.TryAimRotation(direction, baseRotation, out var rotation)) return false;
            EnsureProxy();
            proxy.transform.position = origin; proxy.transform.rotation = rotation;
            proxy.ResetWorldToCameraMatrix(); proxy.nearClipPlane = near; proxy.farClipPlane = far;
            proxy.targetDisplay = source.targetDisplay; proxy.pixelRect = new Rect(0f, 0f, Screen.width, Screen.height);
            proxy.projectionMatrix = projection;
            return true;
        }

        void WarnQuery(string detail)
        {
            if (queryWarning) return;
            queryWarning = true;
            log?.LogWarning("Curved overlay picking unavailable; original game ray retained. " + detail);
        }

        void EnsureProxy()
        {
            if (proxyObject && proxy) return;
            if (proxyObject) UObject.Destroy(proxyObject);
            proxyObject = new GameObject("AmanatsuLocationVR_WorldPointer");
            UObject.DontDestroyOnLoad(proxyObject); proxyObject.hideFlags = HideFlags.HideAndDontSave;
            proxy = proxyObject.AddComponent<Camera>(); proxy.enabled = false; proxy.targetTexture = null;
            proxy.orthographic = false; proxy.cullingMask = 0; proxy.allowHDR = false; proxy.allowMSAA = false;
            proxy.allowDynamicResolution = false; proxy.useOcclusionCulling = false; proxy.rect = new Rect(0f, 0f, 1f, 1f);

        }

        public void Disable() { enabled = false; RetryRestores(); source = null; queryWarning = false; }
        public void Dispose()
        {
            if (disposed) return;
            Disable(); disposed = true; harmony.UnpatchSelf();
            if (current == this) current = null;
            TryDestroyProxy();
        }
        void TryDestroyProxy() { if (disposed && pending.Count == 0 && proxyObject) UObject.Destroy(proxyObject); }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        sealed class CameraSwap
        {
            public readonly WorldPointer Owner;
            public readonly PointSetting Location;
            public readonly OutlinableManager Character;
            public readonly Camera Original;
            public bool Finished;
            public CameraSwap(WorldPointer owner, PointSetting location, OutlinableManager character, Camera original)
            { Owner = owner; Location = location; Character = character; Original = original; }
        }
    }

    internal static class WorldPointerMath
    {
        internal static bool ValidUv(float u, float v) => Finite(u) && Finite(v) && u >= 0f && u <= 1f && v >= 0f && v <= 1f;

        internal static bool TrySurfaceRay(Vector3 basePosition, Quaternion baseRotation,
            Vector3 appliedTranslationMetres, float units, Vector3 localPointMetres,
            out Vector3 origin, out Vector3 direction)
        {
            origin = direction = default(Vector3);
            if (!ValidFrame(basePosition, baseRotation, appliedTranslationMetres, units) || !Finite(localPointMetres)) return false;
            origin = basePosition + baseRotation * (appliedTranslationMetres * units);
            direction = baseRotation * (localPointMetres - appliedTranslationMetres);
            return Finite(origin) && ValidDirection(direction);
        }

        internal static bool TryFlatRay(Vector3 basePosition, Quaternion baseRotation,
            Vector3 appliedTranslationMetres, float units, float width, float distance,
            float aspect, float verticalOffset, float u, float v, out Vector3 origin, out Vector3 direction)
        {
            origin = direction = default(Vector3);
            if (!ValidUv(u, v) || !Finite(width) || width <= 0f || !Finite(distance) || distance <= 0f ||
                !Finite(aspect) || aspect <= 0f || !Finite(verticalOffset) ||
                !ValidFrame(basePosition, baseRotation, appliedTranslationMetres, units) ||
                distance - appliedTranslationMetres.z <= 0.00001f) return false;
            var localPoint = new Vector3((u - 0.5f) * width,
                (v - 0.5f) * width / aspect + verticalOffset, distance);
            origin = basePosition + baseRotation * (appliedTranslationMetres * units);
            direction = baseRotation * (localPoint - appliedTranslationMetres);
            return Finite(origin) && ValidDirection(direction);
        }

        internal static bool TryCurvedRay(Vector3 basePosition, Quaternion baseRotation,
            Vector3 appliedTranslationMetres, Quaternion appliedRotation, Vector3 rawHeadPositionMetres,
            Quaternion rawHeadRotation, Vector3 overlayPointMetres, float units,
            out Vector3 origin, out Vector3 direction)
        {
            origin = direction = default(Vector3);
            if (!ValidFrame(basePosition, baseRotation, appliedTranslationMetres, units) ||
                !Finite(rawHeadPositionMetres) || !Finite(overlayPointMetres)) return false;
            origin = basePosition + baseRotation * (appliedTranslationMetres * units);
            direction = baseRotation * appliedRotation * Quaternion.Inverse(rawHeadRotation) *
                (overlayPointMetres - rawHeadPositionMetres);
            return Finite(origin) && ValidDirection(direction);
        }

        internal static bool TryPixelProjection(float u, float v, float near, float far, out Matrix4x4 projection)
        {
            projection = default(Matrix4x4);
            if (!ValidUv(u, v) || !Finite(near) || !Finite(far) || near <= 0f || far <= near) return false;

            projection = StereoMath.Projection(new[] { -2f * u, 2f * (1f - u), -2f * v, 2f * (1f - v) }, near, far);
            return Finite(projection.m00) && Finite(projection.m11) && Finite(projection.m02) &&
                Finite(projection.m12) && Finite(projection.m22) && Finite(projection.m23);
        }

        internal static bool TryAimRotation(Vector3 direction, Quaternion baseRotation, out Quaternion rotation)
        {
            rotation = default(Quaternion);
            if (!ValidDirection(direction)) return false;
            var forward = direction.normalized;
            var up = baseRotation * Vector3.up;
            if (!Finite(up)) return false;
            if (Cross(forward, up).sqrMagnitude < 0.0001f) up = baseRotation * Vector3.right;
            if (!Finite(up) || Cross(forward, up).sqrMagnitude < 0.0001f) return false;
            rotation = Quaternion.LookRotation(forward, up);
            return ValidDirection(rotation * Vector3.forward);
        }

        static bool ValidFrame(Vector3 position, Quaternion rotation, Vector3 translation, float units) =>
            Finite(position) && Finite(translation) && Finite(units) && units > 0f &&
            ValidDirection(rotation * Vector3.forward) && ValidDirection(rotation * Vector3.up);
        static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        static bool ValidDirection(Vector3 value) => Finite(value) && Finite(value.sqrMagnitude) && value.sqrMagnitude > 0.00000001f;
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
