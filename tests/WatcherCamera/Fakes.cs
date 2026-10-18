using System.Reflection;
using System.Reflection.Emit;

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public readonly List<string> Messages = new();
        public void LogInfo(object message) => Messages.Add(message.ToString());
        public void LogWarning(object message) => Messages.Add(message.ToString());
    }
}

namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> All = new();
        public bool Alive = true;
        public Object() { All.Add(this); }
        public static implicit operator bool(Object value) => value != null && value.Alive;
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value)
        {
            value.Alive = false;
            if (value is GameObject root) foreach (var component in root.Components) component.Alive = false;
        }
    }
    public sealed class GameObject : Object
    {
        public readonly string name;
        public readonly Transform transform = new();
        public readonly List<Camera> Components = new();
        public GameObject(string name) { this.name = name; }
        public T AddComponent<T>() where T : Camera, new()
        {
            var camera = new T { gameObject = this };
            Components.Add(camera);
            return camera;
        }
    }
    public class Camera : Object
    {
        public GameObject gameObject;
        public bool enabled = true;
        public int cullingMask = -1;
        public Transform transform => gameObject.transform;
    }
    public sealed class Transform
    {
        public Vector3 position;
        public Quaternion rotation;
        public void SetPositionAndRotation(Vector3 value, Quaternion orientation) { position = value; rotation = orientation; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }
}

namespace AL.H
{
    public sealed class HScene
    {
        public UnityEngine.Camera MainCamera { get; set; }
    }
}

namespace HarmonyLib
{
    public sealed class CodeInstruction
    {
        public OpCode opcode;
        public object operand;
        public readonly List<Label> labels = new();
        public readonly List<object> blocks = new();
        public CodeInstruction(OpCode opcode, object operand = null) { this.opcode = opcode; this.operand = operand; }
    }
    public sealed class HarmonyMethod
    {
        public readonly MethodInfo method;
        public HarmonyMethod(MethodInfo method) { this.method = method; }
    }
    public sealed class Harmony
    {
        public static readonly Dictionary<MethodInfo, Func<AL.H.HScene, UnityEngine.Camera>> Patched = new();
        public static string FailMethod;
        public Harmony(string id) { }
        public void Patch(MethodInfo method, HarmonyMethod transpiler)
        {
            if (method.Name == FailMethod) throw new InvalidOperationException("Injected patch failure");
            var code = method.GetMethodBody().GetILAsByteArray();
            var instructions = new List<CodeInstruction>();
            for (int i = 0; i < code.Length; ++i)
            {
                switch (code[i])
                {
                    case 0x02: instructions.Add(new CodeInstruction(OpCodes.Ldarg_0)); break;
                    case 0x28:
                    case 0x6f:
                        instructions.Add(new CodeInstruction(code[i] == 0x28 ? OpCodes.Call : OpCodes.Callvirt,
                            method.Module.ResolveMethod(BitConverter.ToInt32(code, i + 1))));
                        i += 4;
                        break;
                    case 0x2a: instructions.Add(new CodeInstruction(OpCodes.Ret)); break;
                    default: throw new InvalidOperationException("Unexpected fixture opcode " + code[i]);
                }
            }
            var rewritten = (IEnumerable<CodeInstruction>)transpiler.method.Invoke(null, new object[] { instructions });
            var replacement = new DynamicMethod(method.Name, typeof(UnityEngine.Camera), new[] { typeof(AL.H.HScene) }, typeof(Harmony).Module, true);
            var generator = replacement.GetILGenerator();
            foreach (var instruction in rewritten)
            {
                if (instruction.operand is MethodInfo target) generator.Emit(instruction.opcode, target);
                else generator.Emit(instruction.opcode);
            }
            Patched.Add(method, replacement.CreateDelegate<Func<AL.H.HScene, UnityEngine.Camera>>());
        }
        public void UnpatchSelf() { Patched.Clear(); }
    }
}

internal static class WatchersFixture
{
    internal static UnityEngine.Camera Arrange(AL.H.HScene scene) => scene.MainCamera;
    internal static UnityEngine.Camera Place(AL.H.HScene scene) => scene.MainCamera;
    internal static UnityEngine.Camera PlaceCouple(AL.H.HScene scene) => scene.MainCamera;
    internal static UnityEngine.Camera BlocksView(AL.H.HScene scene) => scene.MainCamera;
    internal static UnityEngine.Camera PickAt(AL.H.HScene scene) => scene.MainCamera;
    internal static UnityEngine.Camera Bukkake(AL.H.HScene scene) => scene.MainCamera;
}

internal static class MissingMethodsFixture
{
    internal static UnityEngine.Camera Arrange(AL.H.HScene scene) => scene.MainCamera;
}
