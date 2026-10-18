namespace BepInEx.Logging { public class ManualLogSource { public void LogInfo(object x){} public void LogWarning(object x){} } }
namespace Il2CppSystem { public sealed class Action<T,U> { public System.Action<T,U> Callback; public static implicit operator Action<T,U>(System.Action<T,U> x)=>new(){Callback=x}; } }
namespace UnityEngine
{
 public class Object {
  public static readonly List<Object> All=new(); public bool Alive=true; public string name; public HideFlags hideFlags;
  public Object(){All.Add(this);} public static implicit operator bool(Object x)=>x is not null&&x.Alive;
  public static void DontDestroyOnLoad(Object x){}
  public static void Destroy(Object x){if(x is null)return;x.Alive=false;if(x is GameObject go)foreach(var c in All.OfType<Component>().Where(c=>c.gameObject==go))c.Alive=false;}
  public static T[] FindObjectsOfType<T>() where T:Object=>All.OfType<T>().Where(x=>x.Alive&&(x is not Component c||c.gameObject.activeSelf)).ToArray();
 }
 public enum HideFlags { DontUnloadUnusedAsset }
 public record struct Vector2(float x,float y);
 public record struct Vector3(float x,float y,float z) { public static Vector3 zero=>new(0,0,0);public static Vector3 one=>new(1,1,1);public static Vector3 operator*(Vector3 v,float n)=>new(v.x*n,v.y*n,v.z*n); }
 public record struct Quaternion(float x,float y,float z,float w){public static Quaternion identity=>new(0,0,0,1);}
 public record struct Color(float r,float g,float b,float a){public static Color white=>new(1,1,1,1);}
 public class Component:Object { public GameObject gameObject; public Transform transform=>gameObject.transform; }
 public class Transform:Component {public Vector3 position,localScale=Vector3.one;public Quaternion rotation;}
 public class GameObject:Object {public Transform transform;public int layer;public bool activeSelf=true;public GameObject(string label){name=label;transform=new Transform{gameObject=this};}public T AddComponent<T>()where T:Component,new()=>new T{gameObject=this};public void SetActive(bool active){activeSelf=active;}}
 public class Camera:Component{public int cullingMask=-1;}
 public class RenderTexture:Object{public bool Created=true;public bool IsCreated()=>Created;}
 public class Shader:Object{public static bool Available=true;public bool isSupported=true;public static Shader Find(string name)=>Available?new(){name=name}:null;}
 public class Material:Object{
  public readonly Dictionary<string,object> Values=new();public readonly Dictionary<string,bool> Passes=new();public readonly HashSet<string> Keywords=new(); public int renderQueue;
  public Material(Shader shader){} public void SetFloat(string key,float value)=>Values[key]=value;public void SetColor(string key,Color value)=>Values[key]=value;public void SetTexture(string key,RenderTexture value)=>Values[key]=value;
  public void EnableKeyword(string key)=>Keywords.Add(key);public void DisableKeyword(string key)=>Keywords.Remove(key);public void SetOverrideTag(string key,string value)=>Values[key]=value;public void SetShaderPassEnabled(string key,bool value)=>Passes[key]=value;
 }
 public class Mesh:Object{public Vector3[] vertices;public Vector2[] uv;public int[] triangles;public int Rebuilds;public void MarkDynamic(){}public void Clear(){++Rebuilds;}public void RecalculateBounds(){}}
 public class MeshFilter:Component{public Mesh sharedMesh;}
 public class Renderer:Component{public bool enabled=true;}
 public enum MotionVectorGenerationMode{ForceNoMotion}
 public class MeshRenderer:Renderer {public Material sharedMaterial;public Rendering.ShadowCastingMode shadowCastingMode;public bool receiveShadows;public Rendering.LightProbeUsage lightProbeUsage;public Rendering.ReflectionProbeUsage reflectionProbeUsage;public MotionVectorGenerationMode motionVectorGenerationMode;}
 public class CanvasRenderer:Component{}
 public static class Time{public static float realtimeSinceStartup;}
}
namespace UnityEngine.Rendering{
 public enum BlendMode{One=1,OneMinusSrcAlpha=10}public enum CullMode{Off=0}public enum RenderQueue{Transparent=3000}public enum ShadowCastingMode{Off}public enum LightProbeUsage{Off}public enum ReflectionProbeUsage{Off}
 public struct ScriptableRenderContext{}
 public static class RenderPipelineManager {
  public static readonly List<Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera>> Begin=new(),End=new();
  public static void add_beginCameraRendering(Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera> a)=>Begin.Add(a); public static void remove_beginCameraRendering(Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera>a)=>Begin.Remove(a);
  public static void add_endCameraRendering(Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera>a)=>End.Add(a);public static void remove_endCameraRendering(Il2CppSystem.Action<ScriptableRenderContext,UnityEngine.Camera>a)=>End.Remove(a);
  public static void RenderBegin(UnityEngine.Camera c){foreach(var a in Begin.ToArray())a.Callback(default,c);}public static void RenderEnd(UnityEngine.Camera c){foreach(var a in End.ToArray())a.Callback(default,c);}
 }
}
namespace UnityEngine.SceneManagement{public struct Scene{public int handle;}public static class SceneManager{public static int SceneHandle=1;public static int sceneCount=>1;public static Scene GetSceneAt(int index)=>new(){handle=SceneHandle};}}
namespace AmanatsuLocationVR{
 internal static class UiHud{static readonly HashSet<int> layers=new();public static uint ReservedLayerMask{get{uint m=0;foreach(var layer in layers)m|=1u<<layer;return m;}}public static void ReservePresentationLayer(int layer)=>layers.Add(layer);public static void ReleasePresentationLayer(int layer)=>layers.Remove(layer);}
}
