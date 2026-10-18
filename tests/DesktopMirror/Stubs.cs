namespace BepInEx.Logging { public sealed class ManualLogSource { public void LogInfo(object x) { } public void LogWarning(object x) { } } }
namespace Il2CppInterop.Runtime { public static class Il2CppType { public static Type Of<T>() => typeof(T); } }
namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> All = new(); public bool Alive = true; public string name;
        public Object() { All.Add(this); }
        public static implicit operator bool(Object x) => x is not null && x.Alive;
        public static void DontDestroyOnLoad(Object x) { }
        public static void Destroy(Object x) { if (x is not null) x.Alive = false; }
        public static T[] FindObjectsOfType<T>() where T : Object => All.OfType<T>().Where(x => x.Alive).ToArray();
    }
    public enum CameraClearFlags { Skybox, SolidColor, Depth, Nothing }
    public enum HideFlags { DontUnloadUnusedAsset }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public record struct Vector2(float x, float y) { public static Vector2 zero => new(0, 0); public static Vector2 one => new(1, 1); }
    public record struct Vector3(float x, float y, float z) { public static Vector3 one => new(1, 1, 1); }
    public record struct Rect(float x, float y, float width, float height);
    public record struct Color(float r, float g, float b, float a) { public static Color white => new(1,1,1,1); public static Color black => new(0,0,0,1); }
    public static class Screen { public static int width = 1600, height = 900; }
    public class Component : Object
    {
        public GameObject gameObject; public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class GameObject : Object
    {
        readonly Dictionary<Type, Component> components = new();
        public Transform transform; public bool activeInHierarchy = true; public HideFlags hideFlags;
        public int ComponentLookups;
        public GameObject(string label, Type[] types = null)
        {
            name = label; transform = new Transform { gameObject = this };
            if (types != null) foreach (var type in types) Add(type);
        }
        Component Add(Type type)
        {
            var component = (Component)Activator.CreateInstance(type); component.gameObject = this; components[type] = component;
            if (component is Transform t) transform = t;
            return component;
        }
        public T AddComponent<T>() where T : Component, new() => (T)Add(typeof(T));
        public T GetComponent<T>() where T : Component { ++ComponentLookups; return components.TryGetValue(typeof(T), out var c) && c.Alive ? (T)c : null; }
    }
    public class Transform : Component { public Vector3 localScale; public void SetParent(Transform parent, bool preserve) { } }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot, offsetMin, offsetMax; public Vector3 anchoredPosition3D;
        Vector2 size; public int Writes;
        public Vector2 sizeDelta { get => size; set { size = value; ++Writes; } }
    }
    public class RenderTexture : Object
    {
        public int width, height; public bool Created = true;
        public RenderTexture(int w, int h) { width = w; height = h; }
        public bool IsCreated() => Created;
    }
    public class Camera : Component { public bool enabled = true; public RenderTexture targetTexture; public int targetDisplay; public int cullingMask = -1; public CameraClearFlags clearFlags = CameraClearFlags.Skybox; public Color backgroundColor = Color.white; }
    public class Canvas : Component
    {
        bool active = true; int display;
        public int Writes; public bool enabled { get => active; set { active = value; ++Writes; } }
        public int targetDisplay { get => display; set { display = value; ++Writes; } }
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
        public RenderMode renderMode; public Camera worldCamera; public int sortingOrder; public bool pixelPerfect;
    }
}
namespace UnityEngine.Rendering.Universal
{
    public enum CameraRenderType { Base, Overlay }
    public enum CameraOverrideOption { UsePipelineSettings, On, Off }
    public enum AntialiasingMode { None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing, TemporalAntiAliasing }
    public sealed class UniversalAdditionalCameraData : UnityEngine.Component
    {
        public CameraRenderType renderType;
        public bool renderPostProcessing = true, renderShadows = true;
        public CameraOverrideOption requiresDepthOption = CameraOverrideOption.On, requiresColorOption = CameraOverrideOption.On;
        public AntialiasingMode antialiasing = AntialiasingMode.TemporalAntiAliasing;
    }
}
namespace UnityEngine.UI
{
    public sealed class RawImage : UnityEngine.Component
    {
        UnityEngine.RenderTexture current; UnityEngine.Rect uv;
        public int Writes; public bool raycastTarget; public UnityEngine.Color color;
        public UnityEngine.RenderTexture texture { get => current; set { current = value; ++Writes; } }
        public UnityEngine.Rect uvRect { get => uv; set { uv = value; ++Writes; } }
    }
}
