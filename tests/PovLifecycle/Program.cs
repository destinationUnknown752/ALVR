using System;
using System.Linq;
using System.Reflection;
using AL.H;
using AmanatsuLocationVR;
using BepInEx.Logging;
using Character;
using ILLGAMES.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

static class Program
{
    static int checks,scenarios;
    static void Check(bool value,string message) { ++checks; if(!value) throw new Exception(message); }
    static void Same(Human expected,string message) => Check(PovxCompatibility.SelectedCharacter==expected,message);
    static bool Eq(Quaternion a,Quaternion b) => a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w;
    static bool Eq(Vector3 a,Vector3 b) => a.x==b.x&&a.y==b.y&&a.z==b.z;
    static void Run(Action<Fixture> action) { using var f=new Fixture(); action(f); ++scenarios; }
    public static void Main()
    {
        Run(f=> {
            f.Enter(); Same(f.Male,"initial POV chooses male");
            Check(f.Owner.FrameSource.TryGetPose(out _,out _),"initial eye frame available");
            Check(!f.InputAllowed(f.Controller),"active POV blocks selected camera input");
            var other=new CameraController { NowCamera=new Camera() };
            Check(f.InputAllowed(other),"other cameras retain input");
            f.Camera.transform.rotation=new Quaternion(0,1,0,0); f.Controller.CameraRot=new Vector3(40,50,60);
            f.Male.VisibleAll=false; f.Tick();
            Check(PovxCompatibility.PovRequested&&!PovxCompatibility.PovActive,"hidden actor suspends without cancelling");
            Check(Eq(f.Camera.transform.rotation,f.InitialRotation)&&Eq(f.Controller.CameraRot,f.InitialControllerRotation),"suspension restores ordinary orientation");
            Check(f.InputAllowed(f.Controller),"suspension releases ordinary camera input");
            Check(!f.Owner.FrameSource.TryGetPose(out _,out _),"suspension releases animated frame");
            Check(f.Owner.FrameSource.StateDescription.StartsWith("POV suspended:"),"diagnostics explain suspension");
            int writes=f.Controller.RotationWrites;
            f.Tick();f.Tick();Check(f.Controller.RotationWrites==writes,"suspension does not reset camera each frame");
            f.Male.VisibleAll=true; f.Tick(); Same(f.Male,"same actor resumes when visible");
            Check(f.Owner.FrameSource.TryGetPose(out _,out _),"resumed actor supplies refreshed frame");
            Check(!f.InputAllowed(f.Controller),"resume guards camera again");
            Check(f.Male.Face.objHeadBone.HierarchyReads==2,"resumption rebuilds cached bone selection");
        });
        Run(f=> {
            f.Enter(); f.Male.Face.TestHeadRenderer.enabled=false;
            f.Male.Face.TestHeadRenderer.forceRenderingOff=true; f.Tick();
            Same(f.Male,"VR renderer-only head hiding does not suspend POV");
            f.Male.Enabled=false; f.Tick(); Same(f.Male,"paused character updater alone does not imply hidden actor");
            f.Male.Enabled=true; f.Tick(); Same(f.Male,"enabled character updater keeps selection");
            f.Male.GameObject.activeInHierarchy=false; f.Tick(); Check(!PovxCompatibility.PovActive,"inactive root suspends");
            f.Male.GameObject.activeInHierarchy=true; f.Tick(); Same(f.Male,"root reactivation resumes");
        });
        Run(f=> {
            f.Enter();f.MaleActor.Active=false;f.Tick();
            Check(!PovxCompatibility.PovActive&&PovxCompatibility.PovRequested,"nonparticipating actor suspends despite visible human");
            f.MaleActor.Active=true;f.Tick();Same(f.Male,"participation resumes");
            f.Scene.Actors=new[]{f.FemaleActor};f.Tick();
            Check(!PovxCompatibility.PovActive,"actor absent from scene list suspends");
            var replacement=new HActor { Human=f.Female,IsMan=true,IsPC=true };
            f.Scene.Actors=new[]{replacement};f.Tick();
            Check(!PovxCompatibility.PovActive,"replacement actor in same slot never auto-selected");
            f.Scene.Actors=new[]{new HActor { Human=f.Male,IsMan=true },f.FemaleActor};f.Tick();
            Same(f.Male,"new actor wrapper for exact remembered human resumes");
        });
        Run(f=> {
            f.Enter();f.Male.VisibleAll=false;f.Tick();
            var rotation=new Quaternion(0,.6f,0,.8f);var controller=new Vector3(7,8,9);
            f.Camera.transform.rotation=rotation;f.Controller.CameraRot=controller;
            f.Male.VisibleAll=true;f.Tick();Same(f.Male,"resume after ordinary view adjustment");
            f.Camera.transform.rotation=Quaternion.identity;f.Controller.CameraRot=Vector3.zero;
            f.Male.VisibleAll=false;f.Tick();
            Check(Eq(f.Camera.transform.rotation,rotation)&&Eq(f.Controller.CameraRot,controller),"second suspension uses most recent ordinary view");
            var finalRotation=new Quaternion(.6f,0,0,.8f);var finalController=new Vector3(21,22,23);
            f.Camera.transform.rotation=finalRotation;f.Controller.CameraRot=finalController;
            f.Press(KeyCode.Comma);
            Check(!PovxCompatibility.PovRequested,"comma cancels pending request");
            Check(Eq(f.Camera.transform.rotation,finalRotation)&&Eq(f.Controller.CameraRot,finalController),"suspended cancellation keeps adjusted ordinary view");
            f.Male.VisibleAll=true;f.Tick();Check(!PovxCompatibility.PovActive,"cancelled request never resumes");
        });
        Run(f=> {
            f.Enter();f.Male.VisibleAll=false;f.Tick();
            var rotation=new Quaternion(0,0,.6f,.8f);f.Camera.transform.rotation=rotation;
            f.Press(KeyCode.Period);Same(f.Female,"period explicitly selects visible actor while suspended");
            f.Male.VisibleAll=true;f.Tick();Same(f.Female,"old actor return does not steal explicit selection");
            f.Camera.transform.rotation=Quaternion.identity;f.Press(KeyCode.Comma);
            Check(Eq(f.Camera.transform.rotation,rotation),"cycle from suspension captures adjusted ordinary view");
        });
        Run(f=> {
            f.Enter();f.Scene.Actors=Array.Empty<HActor>();
            var unrelated=new HumanComponent { Human=new Human() };
            f.Tick();f.Press(KeyCode.Period);
            Check(PovxCompatibility.PovRequested&&!PovxCompatibility.PovActive,"empty actor list keeps remembered selection, never picks global editor actor");
            f.Scene.Actors=new[]{f.MaleActor};f.Tick();Same(f.Male,"empty list transition resumes original identity");
        });
        Run(f=> {
            f.Enter();f.Scene.ThrowActors=true;f.Tick();
            Check(PovxCompatibility.PovRequested&&!PovxCompatibility.PovActive,"temporary native read failure suspends");
            f.Tick();f.Tick();
            Check(f.Log.Messages.Count(x=>x.Contains("availability temporarily unreadable"))==1,"native read failure warning is throttled");
            f.Scene.ThrowActors=false;f.Tick();Same(f.Male,"native read recovery resumes");
        });
        Run(f=> {
            f.Enter();f.Male.Disposed=true;
            f.MaleActor.Human=new Human();f.Tick();
            Check(PovxCompatibility.PovRequested&&!PovxCompatibility.PovActive,"disposed human replaced by a different human never auto-adopted");
            f.Press(KeyCode.Comma);Check(!PovxCompatibility.PovRequested,"manual cancel works for disposed selection");
        });
        Run(f=> {
            f.Enter();var original=f.Male.GameObject;
            f.Male.GameObject=new GameObject();f.Tick();
            Check(!PovxCompatibility.PovActive,"same native pointer with different Unity identity cannot resume");
            f.Male.GameObject=original;f.Tick();Same(f.Male,"exact original Unity identity may resume");
        });
        Run(f=> {
            f.Enter();f.Male.VisibleAll=false;f.Tick();
            SceneManager.ActiveHandle=2;f.Tick();
            Check(PovxCompatibility.PovRequested&&!PovxCompatibility.PovActive,"temporary active-scene change suspends while original H scene lives");
            f.Press(KeyCode.Period);Check(!PovxCompatibility.PovActive,"cycle cannot attach original actor inside another active mechanic scene");
            f.Male.VisibleAll=true;SceneManager.ActiveHandle=1;f.Tick();Same(f.Male,"return to original live animation scene resumes remembered actor");
        });
        Run(f=> {
            f.Enter();SceneManager.ActiveHandle=2;f.Tick();
            f.Scene.IsDisposed=true;f.Tick();
            Check(!PovxCompatibility.PovRequested,"original H scene disposal clears request even during foreign mechanic scene");
            SceneManager.ActiveHandle=1;f.Tick();Check(!PovxCompatibility.PovActive,"ended context cannot resume on returning active-scene handle");
        });
        Run(f=> {
            f.Scene.Alive=false;var editor=new HumanComponent { Human=f.Male };
            f.Enter();SceneManager.ActiveHandle=2;f.Tick();
            Check(!PovxCompatibility.PovRequested,"editor POV clears on scene change without a live animation context");
        });
        Run(f=> {
            f.Enter();f.Male.VisibleAll=false;f.Tick();f.Scene.IsEnded=true;f.Tick();
            Check(!PovxCompatibility.PovRequested,"ended H scene cancels pending POV even in same Unity scene");
        });
        Run(f=> {
            f.Enter();f.Male.VisibleAll=false;f.Tick();f.Owner.Apply(false,true);
            Check(!PovxCompatibility.PovRequested,"VR disable clears pending POV");
            f.Male.VisibleAll=true;f.Owner.Apply(true,true);f.Tick();Check(!PovxCompatibility.PovActive,"VR restart does not unexpectedly resume old request");
        });
        Run(f=> {
            f.Enter();f.Male.VisibleAll=false;
            Check(PovxCompatibility.SelectedCharacter==null,"late availability check suspends before render without Update");
            f.Male.VisibleAll=true;Same(f.Male,"late availability check resumes without waiting for hotkeys");
            Check(f.Owner.FrameSource.TryGetPose(out _,out _),"late resume returns valid eye frame");
        });
        Console.WriteLine($"PovLifecycle: {checks} checks passed across {scenarios} scenarios.");
    }
    sealed class Fixture:IDisposable
    {
        public readonly ManualLogSource Log=new();
        public readonly Human Male,Female;
        public readonly HActor MaleActor,FemaleActor;
        public readonly Camera Camera;
        public readonly CameraControllerEX Controller;
        public readonly HScene Scene;
        public readonly PovxCompatibility Owner;
        public readonly Quaternion InitialRotation=new(0,.6f,0,.8f);
        public readonly Vector3 InitialControllerRotation=new(10,20,30);
        public Fixture()
        {
            UnityEngine.Object.Registry.Clear();Input.Pressed.Clear();SceneManager.ActiveHandle=1;
            Male=new();Female=new();
            MaleActor=new() { Human=Male,IsMan=true };FemaleActor=new() { Human=Female };
            Camera=new();Camera.transform.rotation=InitialRotation;
            Controller=new() { NowCamera=Camera,CameraRot=InitialControllerRotation };
            Scene=new() { MainCamera=Camera,CameraController=Controller,Actors=new[]{FemaleActor,MaleActor} };
            Owner=new(Log);Owner.Apply(true,true);
        }
        public void Enter() { Press(KeyCode.Comma);Check(PovxCompatibility.PovActive,"POV enters"); }
        public void Tick() { Owner.HandleInput(Camera,false); }
        public void Press(KeyCode key) { Input.Pressed.Add(key);Owner.HandleInput(Camera,true);Input.Pressed.Clear(); }
        public bool InputAllowed(CameraController controller)
        {
            var method=typeof(PovxCompatibility).GetMethod("BeforeCameraInput",BindingFlags.Static|BindingFlags.NonPublic);
            object[] args={controller,true};return (bool)method.Invoke(null,args);
        }
        public void Dispose() { Owner.Dispose(); }
    }
}
