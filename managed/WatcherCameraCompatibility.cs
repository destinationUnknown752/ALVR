using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using AL.H;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace AmanatsuLocationVR
{
    internal sealed class WatcherCameraCompatibility : IDisposable
    {
        const string HarmonyId = "local.amanatsulocationvr.watchercamera";
        static readonly MethodInfo mainCameraGetter = typeof(HScene).GetProperty(nameof(HScene.MainCamera)).GetGetMethod();
        static readonly MethodInfo cameraResolver = typeof(WatcherCameraCompatibility).GetMethod(nameof(ResolveCamera), BindingFlags.NonPublic | BindingFlags.Static);
        static WatcherCameraCompatibility current;
        readonly Harmony harmony = new Harmony(HarmonyId);
        readonly ManualLogSource log;
        Camera proxy, source;
        bool installed, active, disposed;

        public WatcherCameraCompatibility(ManualLogSource logger)
        {
            log = logger;
            current = this;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "AL_HWatchers")
                {
                    Install(assembly.GetType("AL_HWatchers.Watchers"));
                    break;
                }
        }

        internal void Install(Type watchers)
        {
            if (watchers == null || installed || disposed) return;
            try
            {
                var transpiler = new HarmonyMethod(typeof(WatcherCameraCompatibility).GetMethod(nameof(UseVrCamera), BindingFlags.NonPublic | BindingFlags.Static));
                foreach (string name in new[] { "Arrange", "Place", "PlaceCouple", "BlocksView" })
                {
                    var method = watchers.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    if (method == null) throw new MissingMethodException(watchers.FullName, name);
                    harmony.Patch(method, transpiler: transpiler);
                }
                var root = new GameObject("AmanatsuLocationVR_WatcherCamera");
                UObject.DontDestroyOnLoad(root);
                proxy = root.AddComponent<Camera>();
                proxy.enabled = false;
                proxy.cullingMask = 0;
                installed = true;
                log?.LogInfo("AL_HWatchers camera avoidance follows the tracked VR view.");
            }
            catch (Exception exception)
            {
                harmony.UnpatchSelf();
                if (proxy) UObject.Destroy(proxy.gameObject);
                proxy = null;
                log?.LogWarning("AL_HWatchers camera compatibility unavailable: " + exception);
            }
        }

        static IEnumerable<CodeInstruction> UseVrCamera(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && Equals(instruction.operand, mainCameraGetter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = cameraResolver;
                }
                yield return instruction;
            }
        }

        internal static Camera ResolveCamera(HScene scene)
        {
            var original = scene.MainCamera;
            var owner = current;
            return owner != null && owner.active && owner.proxy && original && owner.source && original == owner.source ? owner.proxy : original;
        }

        public void SetPose(Camera gameCamera, Vector3 position, Quaternion rotation)
        {
            if (!installed || disposed || !gameCamera || !proxy) { Deactivate(); return; }
            source = gameCamera;
            proxy.transform.SetPositionAndRotation(position, rotation);
            active = true;
        }

        public void Deactivate()
        {
            active = false;
            source = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Deactivate();
            harmony.UnpatchSelf();
            if (proxy) UObject.Destroy(proxy.gameObject);
            proxy = null;
            if (current == this) current = null;
        }
    }
}
