using System.Reflection;
using System.Reflection.Emit;
using AL.H;
using AmanatsuLocationVR;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UObject = UnityEngine.Object;

int checks = 0;
void Check(bool condition, string message) { ++checks; if (!condition) throw new Exception(message); }
var logger = new ManualLogSource();
var source = new GameObject("Source").AddComponent<Camera>();
source.transform.position = new Vector3(0, 3, -10);
var scene = new HScene { MainCamera = source };
var other = new GameObject("Other").AddComponent<Camera>();
var otherScene = new HScene { MainCamera = other };
var position = new Vector3(20, 2, 0);
var rotation = new Quaternion(0, .70710677f, 0, .70710677f);
var originalPosition = source.transform.position;
var helper = new WatcherCameraCompatibility(logger);
Check(Harmony.Patched.Count == 0 && logger.Messages.Count == 0, "Missing optional plugin should require no patch or warning.");
helper.SetPose(source, position, rotation);
Check(WatcherCameraCompatibility.ResolveCamera(scene) == source, "Missing optional plugin must retain the game camera.");
helper.Install(typeof(WatchersFixture));
Check(Harmony.Patched.Count == 4, "All four camera avoidance paths must be patched.");
Check(Harmony.Patched.Keys.All(x => x.Name is "Arrange" or "Place" or "PlaceCouple" or "BlocksView"), "Patch scope must exclude picking and choreography.");
foreach (var replacement in Harmony.Patched.Values) Check(replacement(scene) == source, "Inactive compatibility must retain game camera.");
helper.SetPose(source, position, rotation);
var proxy = WatcherCameraCompatibility.ResolveCamera(scene);
Check(proxy != source && !proxy.enabled && proxy.cullingMask == 0, "Proxy must remain excluded from rendering.");
Check(proxy.transform.position.Equals(position) && proxy.transform.rotation.Equals(rotation), "Watcher camera must use the stereo eye center and tracked orientation.");
Check(source.transform.position.Equals(originalPosition), "Compatibility must preserve the desktop camera.");
foreach (var replacement in Harmony.Patched.Values)
{
    Check(replacement(scene) == proxy, "Transpiled camera avoidance must receive the tracked camera.");
    Check(replacement(otherScene) == other, "Unrelated scene must retain its own camera.");
}
Check(WatchersFixture.PickAt(scene) == source && WatchersFixture.Bukkake(scene) == source, "Mouse picking and choreography must retain the game camera.");
var nextPosition = new Vector3(-4, 9, 12);
helper.SetPose(source, nextPosition, rotation);
Check(WatcherCameraCompatibility.ResolveCamera(scene) == proxy && proxy.transform.position.Equals(nextPosition), "Tracked pose refresh must reuse the camera.");
helper.Deactivate();
foreach (var replacement in Harmony.Patched.Values) Check(replacement(scene) == source, "Stopping VR must restore every camera avoidance path.");
helper.SetPose(source, position, rotation);
helper.SetPose(null, position, rotation);
Check(WatcherCameraCompatibility.ResolveCamera(scene) == source, "Losing the game camera must deactivate compatibility.");
helper.SetPose(source, position, rotation);
helper.Dispose();
Check(Harmony.Patched.Count == 0 && !proxy && !proxy.gameObject, "Dispose must remove patches and destroy the proxy.");
Check(WatcherCameraCompatibility.ResolveCamera(scene) == source, "Disposed compatibility must restore the game camera.");
helper.Dispose();
helper.SetPose(source, position, rotation);
Check(Harmony.Patched.Count == 0 && !proxy, "Disposed compatibility must not reactivate.");

var transpiler = typeof(WatcherCameraCompatibility).GetMethod("UseVrCamera", BindingFlags.NonPublic | BindingFlags.Static);
var getter = typeof(HScene).GetProperty(nameof(HScene.MainCamera)).GetGetMethod();
var resolver = typeof(WatcherCameraCompatibility).GetMethod(nameof(WatcherCameraCompatibility.ResolveCamera), BindingFlags.NonPublic | BindingFlags.Static);
var unrelated = typeof(string).GetProperty(nameof(string.Length)).GetGetMethod();
var call = new CodeInstruction(OpCodes.Callvirt, getter);
var block = new object();
call.labels.Add(default);
call.blocks.Add(block);
var directCall = new CodeInstruction(OpCodes.Call, getter);
var otherCall = new CodeInstruction(OpCodes.Callvirt, unrelated);
var branch = new CodeInstruction(OpCodes.Br_S, default(Label));
var input = new[] { call, directCall, otherCall, branch };
var output = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { input })).ToArray();
Check(output.Length == input.Length && output.Zip(input).All(pair => ReferenceEquals(pair.First, pair.Second)), "Transpiler must preserve instruction identity and count.");
Check(call.opcode == OpCodes.Call && Equals(call.operand, resolver) && directCall.opcode == OpCodes.Call && Equals(directCall.operand, resolver), "Both getter call opcodes must use the static resolver with the same receiver stack.");
Check(call.labels.Count == 1 && call.blocks.Single() == block, "Getter rewrite must preserve branch labels and exception blocks.");
Check(otherCall.opcode == OpCodes.Callvirt && Equals(otherCall.operand, unrelated) && branch.opcode == OpCodes.Br_S, "Unrelated calls and control flow must remain unchanged.");

var failureLogger = new ManualLogSource();
using (var failed = new WatcherCameraCompatibility(failureLogger))
{
    failed.Install(typeof(MissingMethodsFixture));
    Check(Harmony.Patched.Count == 0 && failureLogger.Messages.Count == 1, "Incompatible optional plugin must roll back partial patches with one warning.");
    failed.SetPose(source, position, rotation);
    Check(WatcherCameraCompatibility.ResolveCamera(scene) == source, "Failed installation must keep the game camera.");
}
Harmony.FailMethod = "PlaceCouple";
using (var failed = new WatcherCameraCompatibility(new ManualLogSource()))
{
    failed.Install(typeof(WatchersFixture));
    Check(Harmony.Patched.Count == 0, "Patch failure must roll back all installed hooks.");
}
Harmony.FailMethod = null;
Check(UObject.All.OfType<Camera>().Count(x => x && x.gameObject.name == "AmanatsuLocationVR_WatcherCamera") == 0, "Compatibility must not leak cameras.");
Console.WriteLine($"Watcher camera compatibility: {checks} checks passed.");
