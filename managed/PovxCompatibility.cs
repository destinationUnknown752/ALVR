using System;
using System.Collections.Generic;
using System.Reflection;
using AL.H;
using BepInEx.Logging;
using Character;
using HarmonyLib;
using ILLGAMES.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace AmanatsuLocationVR
{

    internal sealed class PovxCompatibility : IDisposable
    {
        const string HarmonyId = "local.amanatsulocationvr.pov";
        readonly ManualLogSource log;
        readonly Harmony harmony = new Harmony(HarmonyId);
        static PovxCompatibility current;
        static Human selected;
        HScene scene;
        HScene selectionScene;
        Camera savedCamera;
        CameraController savedController;
        Quaternion savedRotation;
        Vector3 savedControllerRotation;
        int savedScene, selectedObjectId, selectionSceneId;
        bool active, suppressMouse, patched, disposed, failureLogged, requested, suspended, availabilityFailureLogged;
        string suspensionReason;
        DateTime nextSceneSearch;
        public PovxCompatibility(ManualLogSource logger)
        {
            log = logger; FrameSource = new PovFrameSource(logger); current = this;
        }
        public PovFrameSource FrameSource { get; }
        internal static Human SelectedCharacter
        {
            get
            {
                var owner = current;
                if (owner == null || !owner.active || !owner.requested) return null;

                owner.RefreshSelection();
                return owner.requested && !owner.suspended ? selected : null;
            }
        }
        internal static bool PovActive => SelectedCharacter != null;
        internal static bool PovRequested => current != null && current.active && current.requested;
        internal static string PovStateDescription => !PovRequested ? "POV inactive"
            : current.suspended ? "POV suspended: " + current.suspensionReason
            : "POV active for character " + current.selectedObjectId;
        internal static bool Valid(Human human)
        {

            try { return human != null && !human.Disposed && human.GameObject
                    && human.GameObject.activeInHierarchy && human.VisibleAll; }
            catch { return false; }
        }
        internal static HScene FindScene()
        {
            var owner = current;
            if (owner == null) return null;
            if (owner.scene && (owner.scene.IsDisposed || owner.scene.IsEnded)) owner.scene = null;
            if (!owner.scene && DateTime.UtcNow >= owner.nextSceneSearch)
            {
                owner.nextSceneSearch = DateTime.UtcNow.AddSeconds(1);
                owner.scene = UObject.FindObjectOfType<HScene>();
                if (owner.scene && (owner.scene.IsDisposed || owner.scene.IsEnded)) owner.scene = null;
            }
            return owner.scene;
        }
        public void Apply(bool enabled, bool disableMouseLook)
        {
            if (disposed) return;
            if (!enabled && active) Exit();
            active = enabled; suppressMouse = enabled && disableMouseLook;
            FrameSource.SetActive(enabled);
            if (enabled && !patched) InstallInputGuards();
        }
        void InstallInputGuards()
        {
            try
            {
                var prefix = new HarmonyMethod(typeof(PovxCompatibility).GetMethod(nameof(BeforeCameraInput), BindingFlags.NonPublic | BindingFlags.Static));

                foreach (var type in new[] { typeof(CameraController), typeof(CameraControllerEX), typeof(AL.Oil.OilCameraController) })
                    foreach (var name in new[] { "InputMouseProc", "InputKeyProc", "InputMouseWheelZoomProc" })
                    {
                        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                            null, Type.EmptyTypes, null);
                        if (method != null && method.ReturnType == typeof(bool)) harmony.Patch(method, prefix: prefix);
                    }
                patched = true;
                log?.LogInfo("AL camera POV input guards installed. Comma toggles POV; period selects the next character.");
            }
            catch (Exception ex)
            {
                harmony.UnpatchSelf();
                if (!failureLogged) log?.LogWarning("POV camera input suppression unavailable; character pose remains independent of mouse input: " + ex);
                failureLogged = true;
            }
        }
        static bool BeforeCameraInput(CameraController __instance, ref bool __result)
        {
            var owner = current;
            if (owner == null || !owner.active || !owner.suppressMouse || !PovActive) return true;

            if (!__instance) return true;
            if (owner.savedController)
            {
                if (__instance.GetInstanceID() != owner.savedController.GetInstanceID()) return true;
            }
            else if (!owner.savedCamera || !__instance.NowCamera || __instance.NowCamera != owner.savedCamera) return true;
            __result = false; return false;
        }
        public void HandleInput(Camera source, bool allowHotkeys)
        {
            if (!active || disposed) return;
            try
            {
                RefreshSelection();
                if (!allowHotkeys) return;
                if (Input.GetKeyDown(KeyCode.Comma))
                {

                    if (requested) Exit(); else Enter(source);
                }
                if (requested && Input.GetKeyDown(KeyCode.Period)) Cycle();
            }
            catch (Exception ex)
            {
                if (requested) SetSuspended(true, "character state temporarily unavailable");
                if (!availabilityFailureLogged) log?.LogWarning("AL POV waits for its selected character after a transition: " + ex);
                availabilityFailureLogged = true;
            }
        }
        void RefreshSelection()
        {
            if (!requested) return;
            try
            {
                if (selectionSceneId != 0 && (!selectionScene || selectionScene.IsDisposed || selectionScene.IsEnded))
                { Exit(); return; }
                if (savedScene != SceneManager.GetActiveScene().handle)
                {

                    if (selectionSceneId != 0)
                        SetSuspended(true, "waiting for the original animation scene");
                    else Exit();
                    return;
                }

                bool available = Valid(selected) && selected.GameObject.GetInstanceID() == selectedObjectId;
                string reason = "selected character is hidden or unavailable";
                if (selectionSceneId != 0)
                {
                    var sceneNow = FindScene();
                    if (sceneNow && sceneNow.GetInstanceID() != selectionSceneId) { Exit(); return; }
                    bool participates = false;
                    var actors = selectionScene.Actors;
                    if (actors != null && selected != null)
                        foreach (var actor in actors)
                        {

                            var human = actor?.Human;
                            if (human != null && human.Pointer == selected.Pointer
                                && human.GameObject && human.GameObject.GetInstanceID() == selectedObjectId)
                            { participates = actor.Active; break; }
                        }
                    available &= participates;
                    if (!participates) reason = "selected actor is not participating";
                }
                SetSuspended(!available, reason);
                availabilityFailureLogged = false;
            }
            catch (Exception ex)
            {
                SetSuspended(true, "character state temporarily unavailable");
                if (!availabilityFailureLogged) log?.LogWarning("AL POV availability temporarily unreadable; retaining selected identity: " + ex);
                availabilityFailureLogged = true;
            }
        }
        void SetSuspended(bool value, string reason)
        {
            bool changed = suspended != value;
            if (changed && !value) CaptureCameraRotation();
            suspended = value;
            suspensionReason = value ? reason : null;
            if (!changed) return;
            FrameSource.Invalidate();
            if (value)
            {
                RestoreCameraRotation();
                log?.LogInfo("POV suspended for character " + selectedObjectId + ": " + reason + ". It will resume when the same character returns.");
            }
            else log?.LogInfo("POV resumed for character " + selectedObjectId + ".");
        }
        List<Human> Characters()
        {
            var values = new List<Human>();
            var sceneNow = FindScene();
            if (sceneNow && sceneNow.Actors != null)
            {

                foreach (var actor in sceneNow.Actors)
                    if (actor != null && actor.Active && actor.IsPC && Valid(actor.Human)) Add(values, actor.Human);
                foreach (var actor in sceneNow.Actors)
                    if (actor != null && actor.Active && actor.IsMan && Valid(actor.Human)) Add(values, actor.Human);
                foreach (var actor in sceneNow.Actors)
                    if (actor != null && actor.Active && Valid(actor.Human)) Add(values, actor.Human);
            }

            if (!sceneNow)
            {
                var humans = UObject.FindObjectsOfType<HumanComponent>();
                if (humans != null)
                    foreach (var component in humans) if (component && Valid(component.Human)) Add(values, component.Human);
            }
            return values;
        }
        static void Add(List<Human> values, Human value)
        {
            foreach (var existing in values) if (existing.Pointer == value.Pointer) return;
            values.Add(value);
        }
        void Enter(Camera source)
        {
            if (!source) return;
            var chars = Characters();
            if (chars.Count == 0) { log?.LogInfo("POV requested, but no loaded character is available."); return; }
            savedCamera = source; savedRotation = source.transform.rotation;
            savedScene = SceneManager.GetActiveScene().handle;
            var sceneNow = FindScene();
            savedController = sceneNow && sceneNow.MainCamera == source ? sceneNow.CameraController : source.GetComponent<CameraController>();
            if (savedController) savedControllerRotation = savedController.CameraRot;
            selected = chars[0];
            RememberSelection(sceneNow);
            log?.LogInfo("AL character scale: MODEL_SCALE=" + Human.MODEL_SCALE + ", model bounds height=" + Human.Bounds.size.y + ", root lossy scale=" + selected.Transform.lossyScale);
            log?.LogInfo("POV enabled for character " + selected.GameObject.GetInstanceID() + ".");
        }
        void Cycle()
        {
            if (selectionSceneId != 0 && savedScene != SceneManager.GetActiveScene().handle) return;
            var chars = Characters(); if (chars.Count == 0) return;
            if (suspended) CaptureCameraRotation();
            int index = chars.FindIndex(h => h.Pointer == selected.Pointer);
            selected = chars[(index + 1) % chars.Count];
            RememberSelection(FindScene());
            log?.LogInfo("POV selected character " + selected.GameObject.GetInstanceID() + ".");
        }
        void RememberSelection(HScene context)
        {
            selectedObjectId = selected.GameObject.GetInstanceID();
            selectionScene = context;
            selectionSceneId = context ? context.GetInstanceID() : 0;
            requested = true; suspended = false; suspensionReason = null;
            availabilityFailureLogged = false;
            FrameSource.Invalidate();
        }
        void Exit()
        {
            bool hadSelection = requested;
            if (suspended) CaptureCameraRotation();
            requested = suspended = false;
            selected = null;
            selectionScene = null; selectedObjectId = selectionSceneId = 0; suspensionReason = null;
            FrameSource.Invalidate();

            RestoreCameraRotation();
            savedCamera = null; savedController = null;
            if (hadSelection) log?.LogInfo("POV disabled; pre-POV camera rotation restored.");
        }
        void RestoreCameraRotation()
        {
            if (savedScene == SceneManager.GetActiveScene().handle)
            {
                try
                {
                    if (savedController) savedController.SetRotation(savedControllerRotation);
                    if (savedCamera) savedCamera.transform.rotation = savedRotation;
                }
                catch (Exception ex) { log?.LogWarning("POV exit camera restoration unavailable: " + ex.Message); }
            }
        }
        void CaptureCameraRotation()
        {
            if (savedScene != SceneManager.GetActiveScene().handle) return;
            try
            {
                var rotation = savedCamera ? savedCamera.transform.rotation : savedRotation;
                var controllerRotation = savedController ? savedController.CameraRot : savedControllerRotation;
                savedRotation = rotation; savedControllerRotation = controllerRotation;
            }
            catch (Exception ex) { log?.LogWarning("Could not preserve the adjusted ordinary camera rotation: " + ex.Message); }
        }
        public void Dispose()
        {
            if (disposed) return;
            Exit(); disposed = true; active = false; FrameSource.Unbind();
            harmony.UnpatchSelf(); if (current == this) current = null;
        }
    }
}
