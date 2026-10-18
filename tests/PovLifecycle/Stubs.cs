using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public readonly List<string> Messages=new();
        public void LogInfo(object value) => Messages.Add(value.ToString());
        public void LogWarning(object value) => Messages.Add(value.ToString());
    }
}
namespace HarmonyLib
{
    public sealed class HarmonyMethod
    {
        public MethodInfo Method;
        public HarmonyMethod(MethodInfo method) { Method=method; }
    }
    public sealed class Harmony
    {
        public static readonly Dictionary<MethodInfo,MethodInfo> Prefixes=new();
        public Harmony(string id) { }
        public void Patch(MethodInfo method, HarmonyMethod prefix) { Prefixes[method]=prefix.Method; }
        public void UnpatchSelf() { Prefixes.Clear(); }
    }
}
namespace UnityEngine
{
    public class Object
    {
        static int next;
        public static readonly List<Object> Registry=new();
        readonly int id=++next;
        public bool Alive=true;
        public string name;
        public Object() { Registry.Add(this); }
        public int GetInstanceID() => id;
        public static implicit operator bool(Object value) => value!=null && value.Alive;
        public static T FindObjectOfType<T>() where T:Object => Registry.OfType<T>().FirstOrDefault(x=>x.Alive);
        public static T[] FindObjectsOfType<T>() where T:Object => Registry.OfType<T>().Where(x=>x.Alive).ToArray();
    }
    public class GameObject:Object
    {
        public bool activeInHierarchy=true;
        public readonly Transform transform=new();
        public readonly List<Transform> Bones=new();
        public readonly Dictionary<Type,Component> Components=new();
        public int HierarchyReads;
        public T[] GetComponentsInChildren<T>(bool includeInactive)
        { ++HierarchyReads; return Bones.OfType<T>().ToArray(); }
    }
    public class Component:Object
    {
        public GameObject gameObject=new();
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T:Component => gameObject.Components.TryGetValue(typeof(T),out var value)?(T)value:null;
    }
    public class Camera:Component { }
    public class Transform:Object
    {
        public Vector3 position,lossyScale=new(1,1,1);
        public Quaternion rotation=Quaternion.identity;
    }
    public class Renderer:Object
    {
        public bool enabled=true,forceRenderingOff;
    }
    public struct Bounds { public Vector3 size; }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero => new(0,0,0);
        public static Vector3 operator +(Vector3 a,Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a,float v) => new(a.x*v,a.y*v,a.z*v);
    }
    public struct Quaternion
    {
        public float x,y,z,w;
        public Quaternion(float x,float y,float z,float w) { this.x=x;this.y=y;this.z=z;this.w=w; }
        public static Quaternion identity => new(0,0,0,1);
    }
    public enum KeyCode { Comma,Period }
    public static class Input
    {
        public static readonly HashSet<KeyCode> Pressed=new();
        public static bool GetKeyDown(KeyCode key) => Pressed.Remove(key);
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public int handle; }
    public static class SceneManager
    {
        public static int ActiveHandle=1;
        public static Scene GetActiveScene() => new() { handle=ActiveHandle };
    }
}
namespace ILLGAMES.Unity
{
    using UnityEngine;
    public class CameraController:Component
    {
        public Camera NowCamera;
        public Vector3 CameraRot;
        public int RotationWrites;
        public void SetRotation(Vector3 value) { CameraRot=value; ++RotationWrites; }
        public bool InputMouseProc() => true;
        public bool InputKeyProc() => true;
        public bool InputMouseWheelZoomProc() => true;
    }
    public class CameraControllerEX:CameraController
    {
        public new bool InputMouseProc() => true;
        public new bool InputKeyProc() => true;
    }
}
namespace AL.Oil { public class OilCameraController:ILLGAMES.Unity.CameraController { } }
namespace Character
{
    using UnityEngine;
    public class Human
    {
        static int next;
        public IntPtr Pointer=new(++next);
        public bool Disposed,Enabled=true,VisibleAll=true;
        public GameObject GameObject=new();
        public Transform Transform => GameObject.transform;
        public HumanBody Body=new();
        public HumanFace Face=new();
        public static float MODEL_SCALE=1;
        public static Bounds Bounds => new() { size=new Vector3(8,16,2) };
        public Human()
        {
            Body._objBone.Bones.Add(new Transform { name="cf_s_head" });
            Face.objHeadBone.Bones.Add(new Transform { name="cf_J_Eye_s_L",position=new(-.3f,16,0) });
            Face.objHeadBone.Bones.Add(new Transform { name="cf_J_Eye_s_R",position=new(.3f,16,0) });
        }
    }
    public class HumanBody { public GameObject _objBone=new(); }
    public class HumanFace
    {
        public GameObject objHeadBone=new();
        public Renderer TestHeadRenderer=new();
    }
    public class HumanComponent:Component { public Human Human; }
}
namespace AL.H
{
    using Character;
    using UnityEngine;
    public class HActor
    {
        public Human Human;
        public bool Active=true,IsPC,IsMan;
    }
    public class HScene:Component
    {
        public bool IsDisposed,IsEnded,ThrowActors;
        HActor[] actors=Array.Empty<HActor>();
        public HActor[] Actors
        {
            get { if(ThrowActors) throw new InvalidOperationException("actors temporarily unreadable"); return actors; }
            set { actors=value; }
        }
        public Camera MainCamera;
        public ILLGAMES.Unity.CameraControllerEX CameraController;
    }
}
