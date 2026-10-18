using System.Reflection;
using BepInEx.Logging;
using AmanatsuLocationVR;
using UnityEngine;
using N = System.Numerics;

int checks=0;
var identity=new Quaternion(N.Quaternion.Identity);
var basis=new Quaternion(N.Quaternion.CreateFromYawPitchRoll(.7f,-.3f,.2f));
var basePosition=new Vector3(12,-4,25);
var rawPosition=new Vector3(2,1.6f,-3);
var rawRotation=new Quaternion(N.Quaternion.CreateFromYawPitchRoll(-.9f,.4f,-.2f));
var appliedRotation=new Quaternion(N.Quaternion.CreateFromYawPitchRoll(.3f,-.2f,.1f));
var zero=new Vector3(0,0,0);
var pixels=new[]{(0f,0f),(1f,1f),(0f,1f),(1f,0f),(.5f,.5f),(.17f,.83f)};

foreach(float units in new[]{1f,10f,25f})
foreach(var translation in new[]{zero,new Vector3(.2f,-.1f,.3f)})
foreach(var applied in new[]{identity,appliedRotation})
foreach(var uv in pixels)
{
    float width=1.4f, distance=1f, aspect=16f/9f, offset=.24f;
    var localPoint=new Vector3((uv.Item1-.5f)*width,(uv.Item2-.5f)*width/aspect+offset,distance);
    var expectedOrigin=basePosition+basis*(translation*units);
    var expectedPoint=basePosition+basis*(localPoint*units);
    Check(WorldPointerMath.TryFlatRay(basePosition,basis,translation,units,width,distance,aspect,offset,
        uv.Item1,uv.Item2,out var origin,out var direction),"flat ray accepted");
    Close(origin,expectedOrigin,"flat origin uses applied metres once");
    Close(origin+direction*units,expectedPoint,"flat ray hits HUD landmark");

    var pointRaw=rawPosition+rawRotation*Quaternion.Inverse(applied)*(localPoint-translation);
    Check(WorldPointerMath.TryCurvedRay(basePosition,basis,translation,applied,rawPosition,rawRotation,
        pointRaw,units,out var curvedOrigin,out var curvedDirection),"raw overlay point accepted");
    Close(curvedOrigin,expectedOrigin,"curved origin");
    Close(curvedOrigin+curvedDirection*units,expectedPoint,"raw-to-effective mapping with tracking/pin modes");
    Check(WorldPointerMath.TryAimRotation(curvedDirection,basis,out var aim),"aim accepted");
    Check(WorldPointerMath.TryPixelProjection(uv.Item1,uv.Item2,.01f*units,1000,out var projection),"pixel projection accepted");
    var proxy=new Camera { nearClipPlane=.01f*units, projectionMatrix=projection };
    proxy.transform.position=curvedOrigin;proxy.transform.rotation=aim;
    var ray=proxy.ScreenPointToRay(new Vector3(uv.Item1*Screen.width,uv.Item2*Screen.height,0));
    Close(ray.direction,(expectedPoint-expectedOrigin).normalized,"selected screen pixel aims through panel");
    Close(ray.origin,expectedOrigin+ray.direction*(.01f*units),"near origin stays on the same ray");
}

var warpedPoint=new Vector3(.62f,-.18f,.73f);
var warpedRaw=rawPosition+rawRotation*Quaternion.Inverse(appliedRotation)*warpedPoint;
Check(WorldPointerMath.TryCurvedRay(basePosition,basis,zero,appliedRotation,rawPosition,rawRotation,
    warpedRaw,10,out var warpedOrigin,out var warpedDirection),"nonplanar point");
Close(warpedOrigin+warpedDirection*10,basePosition+basis*(warpedPoint*10),"curved depth preserved");
Check(WorldPointerMath.TryAimRotation(basis*Vector3.up,basis,out var verticalAim),"vertical look fallback");
Close(verticalAim*Vector3.forward,basis*Vector3.up,"vertical aim remains finite and correct");
Check(!WorldPointerMath.TryFlatRay(basePosition,basis,new Vector3(0,0,1.1f),10,1.4f,1,2,0,.5f,.5f,out _,out _),"head through flat panel rejected");
Check(!WorldPointerMath.TryPixelProjection(-.1f,.5f,.01f,100,out _),"outside pixel rejected");
Check(!WorldPointerMath.TryPixelProjection(.5f,.5f,.01f,.01f,out _),"invalid depth range rejected");
Check(!WorldPointerMath.TryCurvedRay(basePosition,basis,zero,identity,rawPosition,rawRotation,
    new Vector3(float.NaN,0,0),10,out _,out _),"nonfinite query rejected");

var logger = new ManualLogSource(); var helper = new WorldPointer(logger);
var source = new Camera(); source.transform.position = new Vector3(999,999,999);
var location = new AL.H.UI.PointSetting { _camera=source };
var character = new AL.OutlinableManager { _mainCamera=source };
var locationPrefix = typeof(WorldPointer).GetMethod("BeforeLocationPicker",BindingFlags.NonPublic|BindingFlags.Static)!;
var characterPrefix = typeof(WorldPointer).GetMethod("BeforeCharacterPicker",BindingFlags.NonPublic|BindingFlags.Static)!;
var finalizer = typeof(WorldPointer).GetMethod("AfterPicker",BindingFlags.NonPublic|BindingFlags.Static)!;
var locationInput = new AL.H.UI.PointSetting.__c();

foreach(float curve in new[]{0f,.01f,.3f,.75f,1f})
foreach(bool flip in new[]{false,true})
foreach(var uv in pixels.Concat(new[]{(.139f,.781f),(.997f,.127f)}))
foreach(var translation in new[]{zero,new Vector3(.1f,-.04f,.18f)})
{
    Input.mousePosition=new Vector3(uv.Item1*Screen.width,uv.Item2*Screen.height,0);
    helper.Configure(source,basePosition,basis,translation,appliedRotation,rawPosition,rawRotation,10,1.4f,1,16f/9f,.24f,curve,true,flip,true);
    Check(HudSurfaceMath.TryLocalPoint(uv.Item1,flip?uv.Item2:1-uv.Item2,1.4f,1,16f/9f,.24f,curve,out var point),"surface available");
    var expectedOrigin=basePosition+basis*(translation*10);
    var expectedDirection=(basis*(point-translation)).normalized;
    foreach(bool inScene in new[]{false,true})
    {
        object[] callArgs={inScene?(object)location:character,null!};
        (inScene?locationPrefix:characterPrefix).Invoke(null,callArgs);
        var pickCamera=inScene?location._camera:character._mainCamera;
        Check(pickCamera!=source&&!pickCamera.enabled,"only disabled proxy assigned");
        Close(pickCamera.transform.position,expectedOrigin,"physical midpoint origin ignores desktop camera movement");
        if(inScene)locationInput._Initialize_b__17_2(default,location);else character.UpdateProc();
        var ray=inScene?location.LastRay:character.LastRay;
        Close(ray.direction,expectedDirection,"visible HUD cursor and game ray agree for curvature/flip/pin");
        Close(ray.origin,expectedOrigin+expectedDirection*.1f,"ray starts on correct near plane");
        finalizer.Invoke(null,new[]{callArgs[1]});
        Check((inScene?location._camera:character._mainCamera)==source,"scope restores camera after game call");
    }
}
Check(Native.QueryCalls==0,"scene mesh picking never depends on stale SteamVR overlay geometry");
Check(HarmonyLib.Harmony.Patched.Count==2,"only exact character and location native methods hooked");

Input.mousePosition=new Vector3(.17f*Screen.width,.83f*Screen.height,0);
Native.Query=(u,v)=>new Vector3(warpedRaw.x,warpedRaw.y,-warpedRaw.z);
helper.Configure(source,basePosition,basis,zero,appliedRotation,rawPosition,rawRotation,10,1.4f,1,16f/9f,.24f,.6f,false,true,true);
object[] fallback={location,null!};locationPrefix.Invoke(null,fallback);locationInput._Initialize_b__17_2(default,location);
Close(location.LastRay.direction,(basis*warpedPoint).normalized,"overlay fallback preserves native point and Z flip");
finalizer.Invoke(null,new[]{fallback[1]});Check(location._camera==source,"overlay scope restores");
Native.Query=(u,v)=>null;
for(int i=0;i<2;i++){object[] unavailable={location,null!};locationPrefix.Invoke(null,unavailable);Check(location._camera==source&&unavailable[1]==null,"unverified overlay point leaves game camera untouched");}
Check(logger.Warnings.Count==1,"overlay failure logs once");

helper.Configure(source,basePosition,basis,zero,identity,rawPosition,rawRotation,10,1.4f,1,16f/9f,0,0,true,true,true);
object[] outer={location,null!};locationPrefix.Invoke(null,outer);var outerProxy=location._camera;
object[] inner={location,null!};locationPrefix.Invoke(null,inner);
finalizer.Invoke(null,new[]{inner[1]});Check(location._camera==outerProxy,"nested finalizer retains outer proxy");
finalizer.Invoke(null,new[]{outer[1]});Check(location._camera==source,"outer scope restores original");
object[] deliberate={character,null!};characterPrefix.Invoke(null,deliberate);
var newCamera=new Camera();character._mainCamera=newCamera;
finalizer.Invoke(null,new[]{deliberate[1]});Check(character._mainCamera==newCamera,"game's deliberate camera replacement preserved");
character._mainCamera=source;
location.Active=false;object[] inactive={location,null!};locationPrefix.Invoke(null,inactive);
Check(inactive[1]!=null&&location._camera!=source,"picker callback self needs no cached scene or duplicate Active gate");
var previousRay=location.LastRay;locationInput._Initialize_b__17_2(default,location);
Close(location.LastRay.direction,previousRay.direction,"native inactive guard remains in charge of picking");
finalizer.Invoke(null,new[]{inactive[1]});Check(location._camera==source,"inactive callback scope restores camera");location.Active=true;
Input.mousePosition=new Vector3(-1,10,0);object[] outside={character,null!};characterPrefix.Invoke(null,outside);
Check(outside[1]==null&&character._mainCamera==source,"off-screen mouse never mutates picker");
Input.mousePosition=new Vector3(100,100,0);object[] running={character,null!};characterPrefix.Invoke(null,running);
helper.Disable();finalizer.Invoke(null,new[]{running[1]});Check(character._mainCamera==source,"disable during scope still restores after exception-safe finalizer");
object[] disabled={character,null!};characterPrefix.Invoke(null,disabled);
Check(disabled[1]==null&&character._mainCamera==source,"hidden HUD/disabled hook has no camera mutation");
helper.Dispose();Check(HarmonyLib.Harmony.Patched.Count==0,"dispose removes all hooks");
Console.WriteLine($"PASS {checks} math, curved mesh/flip landmarks, character/location picks, overlay fallback and scoped restoration checks.");

void Check(bool condition,string label){if(!condition)throw new Exception(label);++checks;}
void Close(Vector3 actual,Vector3 expected,string label)
{
    if(!float.IsFinite(actual.sqrMagnitude)||(actual-expected).sqrMagnitude>0.00000004f)
        throw new Exception($"{label}: expected {expected}, got {actual}");
    ++checks;
}
