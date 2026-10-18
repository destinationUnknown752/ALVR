using System.Reflection;
using UnityEngine;

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public readonly List<string> Warnings = new();
        public void LogInfo(object value) { }
        public void LogWarning(object value) => Warnings.Add(value.ToString());
    }
}
namespace HarmonyLib
{
    public sealed class HarmonyMethod(MethodInfo method) { public readonly MethodInfo Method=method; }
    public sealed class Harmony(string id)
    {
        public readonly string Id=id;
        public static readonly List<MethodBase> Patched = new();
        public void Patch(MethodBase target, HarmonyMethod prefix = null, HarmonyMethod finalizer = null) { Patched.Add(target); }
        public void UnpatchSelf() { Patched.Clear(); }
        public void Unpatch(MethodBase target, MethodInfo patch) { }
    }
}
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static implicit operator bool(Object value) => value != null && !value.Destroyed;
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value) { if (value != null) value.Destroyed = true; }
    }
    public sealed class GameObject(string name) : Object
    {
        public readonly string Name=name;
        public readonly Transform transform = new();
        public HideFlags hideFlags;
        public T AddComponent<T>() where T : Camera, new() { var value = new T(); value.gameObject = this; return value; }
    }
    public sealed class Transform
    {
        public Vector3 position;
        public Quaternion rotation = new(System.Numerics.Quaternion.Identity);
    }
    public struct Rect(float x, float y, float width, float height)
    { public float x=x, y=y, width=width, height=height; }
    public enum HideFlags { HideAndDontSave }
    public enum StereoTargetEyeMask { None }
    public static class Screen { public static int width=2560, height=1440; }
    public static class Input { public static Vector3 mousePosition; }
    public static class Mathf
    {
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static float Max(float a,float b)=>Math.Max(a,b);
    }
    public readonly struct Ray(Vector3 origin, Vector3 direction)
    { public readonly Vector3 origin=origin, direction=direction; }
    public class Camera : Object
    {
        public GameObject gameObject = new("camera");
        public Transform transform => gameObject.transform;
        public bool enabled, orthographic, allowHDR, allowMSAA, allowDynamicResolution, useOcclusionCulling;
        public int cullingMask, targetDisplay;
        public object targetTexture;
        public float nearClipPlane=.01f, farClipPlane=1000f;
        public StereoTargetEyeMask stereoTargetEye;
        public Rect rect, pixelRect = new(0,0,2560,1440);
        public Matrix4x4 projectionMatrix = AmanatsuLocationVR.StereoMath.Projection(new[]{-1f,1f,-1f,1f},.01f,1000f);
        public void ResetWorldToCameraMatrix() { }
        public Ray ScreenPointToRay(Vector3 mouse)
        {
            float u=(mouse.x-pixelRect.x)/pixelRect.width, v=(mouse.y-pixelRect.y)/pixelRect.height;
            float x=((2*u-1)+projectionMatrix.m02)/projectionMatrix.m00;
            float y=((2*v-1)+projectionMatrix.m12)/projectionMatrix.m11;

            var nearPoint=new Vector3(x*nearClipPlane,y*nearClipPlane,nearClipPlane);
            return new Ray(transform.position+transform.rotation*nearPoint,
                transform.rotation*new Vector3(x,y,1).normalized);
        }
    }
}
namespace AmanatsuLocationVR
{
    internal static class Native
    {
        public static Func<float,float,Vector3?> Query;
        public static int QueryCalls;
        public static int RGVR_GetHudPoint(float u,float v,float[] point)
        {
            ++QueryCalls;
            var value=Query?.Invoke(u,v);
            if (!value.HasValue) return 0;
            point[0]=value.Value.x;point[1]=value.Value.y;point[2]=value.Value.z;return 1;
        }
    }
}
namespace R3 { public struct Unit { } }
namespace AL
{
    public sealed class OutlinableManager : UnityEngine.Object
    {
        public Camera _mainCamera;
        public Ray LastRay;
        public void UpdateProc() { LastRay = _mainCamera.ScreenPointToRay(Input.mousePosition); }
    }
}
namespace AL.H.UI
{
    public sealed class PointSetting
    {
        public Camera _camera;
        public bool Active = true;
        public Ray LastRay;
        public sealed class __c
        {
            public void _Initialize_b__17_2(R3.Unit _, PointSetting self) { if (self.Active) self.LastRay = self._camera.ScreenPointToRay(Input.mousePosition); }
        }
    }
}
