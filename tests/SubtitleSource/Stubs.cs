using System;
using System.Linq;
using System.Collections.Generic;
namespace BepInEx.Logging { public sealed class ManualLogSource { public readonly List<string> Infos = new(); public void LogInfo(object x) => Infos.Add(x.ToString()); public void LogWarning(object x) { } } }
namespace Il2CppInterop.Runtime { public static class Il2CppType { public static Type Of<T>() => typeof(T); } }
namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> All = new(); public static int Scans; public bool Alive = true; public string name;
        public Object() { All.Add(this); }
        public static implicit operator bool(Object x) => x is not null && x.Alive;
        public static void DontDestroyOnLoad(Object x) { }
        public static void Destroy(Object x) { if (x is not null) x.Alive = false; if (x is GameObject go) foreach (var c in All.OfType<Component>().Where(c => c.gameObject == go)) c.Alive=false; }
        public static T[] FindObjectsOfType<T>() where T : Object { ++Scans; return All.OfType<T>().Where(x => x.Alive).ToArray(); }
    }
    public enum HideFlags { DontUnloadUnusedAsset }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public record struct Vector2(float x, float y) { public static Vector2 zero => new(0, 0); public static Vector2 one => new(1, 1); }
    public record struct Color(float r, float g, float b, float alpha) { public float a { get => alpha; set => alpha = value; } public static Color white => new(1,1,1,1); }
    public static class Time { public static float realtimeSinceStartup; }
    public class Component : Object
    {
        public GameObject gameObject; public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T[] GetComponents<T>() where T : Component => gameObject.GetComponents<T>();
    }
    public class Behaviour : Component { public bool enabled = true; public bool isActiveAndEnabled => Alive && enabled && gameObject.activeInHierarchy; }
    public class GameObject : Object
    {
        readonly List<Component> components = new();
        public Transform transform; public bool activeInHierarchy = true; public HideFlags hideFlags;
        public GameObject(string label, Type[] types = null)
        {
            name = label; transform = new Transform { gameObject = this }; components.Add(transform);
            if (types != null) foreach (var type in types) Add(type);
        }
        Component Add(Type type)
        {
            var component = (Component)Activator.CreateInstance(type); component.gameObject = this; components.Add(component);
            if (component is Transform t) transform = t;
            return component;
        }
        public T AddComponent<T>() where T : Component, new() => (T)Add(typeof(T));
        public T GetComponent<T>() where T : Component => components.OfType<T>().FirstOrDefault(x => x.Alive);
        public T[] GetComponents<T>() where T : Component => components.OfType<T>().Where(x => x.Alive).ToArray();
    }
    public class Transform : Component { public Transform parent; public void SetParent(Transform value, bool preserve) { parent = value; } }
    public class RectTransform : Transform { public Vector2 anchorMin, anchorMax, pivot, offsetMin, offsetMax; }
    public class Canvas : Behaviour { public RenderMode renderMode; public int sortingOrder; }
    public class CanvasGroup : Behaviour { public float alpha = 1f; public bool ignoreParentGroups, interactable = true, blocksRaycasts = true; }
    public class Material : Object { }
}
namespace UnityEngine.UI
{
    public class CanvasScaler : UnityEngine.Behaviour { public enum ScaleMode { ConstantPixelSize } public ScaleMode uiScaleMode; public float scaleFactor; }
}
namespace TMPro
{
    public enum TextAlignmentOptions { Center }
    public enum TextWrappingModes { Normal }
    public enum TextOverflowModes { Overflow }
    public enum FontStyles { Normal, Bold }
    public class TMP_FontAsset : UnityEngine.Object { }
    public class TextMeshProUGUI : UnityEngine.Behaviour
    {
        public static bool ThrowOnNextFont;
        TMP_FontAsset fontValue;
        public TMP_FontAsset font { get => fontValue; set { if(ThrowOnNextFont) { ThrowOnNextFont=false; throw new Exception("font assignment failed"); } fontValue=value; } }
        public UnityEngine.Material fontSharedMaterial;
        public string text; public float fontSize = 24f;
        public bool raycastTarget, enableAutoSizing, richText, isRightToLeftText;
        public FontStyles fontStyle; public TextAlignmentOptions alignment; public TextWrappingModes textWrappingMode; public TextOverflowModes overflowMode;
        public int maxVisibleCharacters = int.MaxValue, maxVisibleWords = int.MaxValue, maxVisibleLines = int.MaxValue, firstVisibleCharacter;
        public UnityEngine.Color color = UnityEngine.Color.white;
    }
}
namespace ILLGAMES.ADV
{
    public class TextController : UnityEngine.Behaviour { public TMPro.TextMeshProUGUI MessageWindow, NameWindow; public bool MessageVisible = true; }
}
