using System;
using System.Collections.Generic;

using BepInEx.Logging;
using Character;
using UnityEngine;
using UnityEngine.Rendering;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace AmanatsuLocationVR
{

    internal sealed class PovHeadVisibility : IDisposable
    {
        struct SavedRenderer
        {
            public Renderer Renderer;
            public ShadowCastingMode ShadowMode;
            public bool UseForceOff;
            public bool ForceOff;
        }

        readonly ManualLogSource log;
        readonly List<Renderer> renderers = new List<Renderer>();
        readonly HashSet<int> rendererIds = new HashSet<int>();
        readonly List<SavedRenderer> changed = new List<SavedRenderer>();
        Human selected;
        int selectedId;
        bool cachedAccessories;

        DateTime nextRefresh;
        DateTime nextRetry;
        bool disposed;

        bool failedLogged;
        bool restoreFailedLogged;

        public PovHeadVisibility(ManualLogSource logger)
        {
            log = logger;
        }

        public string StateDescription { get; private set; } = "inactive";
        public bool PovActive { get; private set; }

        public void Prepare(bool enabled, bool hideAccessories = true)
        {
            Restore();
            PovActive = false;
            if (disposed) return;
            DateTime now = DateTime.UtcNow;
            try
            {
                var character = PovxCompatibility.SelectedCharacter;
                if (!PovxCompatibility.Valid(character))
                {
                    ClearSelection(); StateDescription = "POV inactive"; return;
                }
                PovActive = true;
                if (!enabled)
                {
                    ClearSelection();
                    StateDescription = "POV active; head hiding disabled";
                    return;
                }
                if (changed.Count != 0 || now < nextRetry) return;

                int characterId = character.GameObject.GetInstanceID();
                bool selectionChanged = !PovxCompatibility.Valid(selected) || characterId != selectedId || cachedAccessories != hideAccessories;
                if (selectionChanged || now >= nextRefresh)
                {
                    int oldCount = renderers.Count;
                    CacheRenderers(character, hideAccessories);
                    selected = character;
                    selectedId = characterId;
                    cachedAccessories = hideAccessories;
                    nextRefresh = now.AddSeconds(1);
                    if (selectionChanged || oldCount != renderers.Count)
                        log?.LogInfo("POV head visibility selected character " + characterId + ": " + renderers.Count
                            + " head/hair renderers" + (hideAccessories ? " including head accessories." : "."));
                }

                foreach (var renderer in renderers)
                {
                    if (!renderer || !renderer.enabled || renderer.forceRenderingOff) continue;
                    ShadowCastingMode mode = renderer.shadowCastingMode;
                    if (mode == ShadowCastingMode.ShadowsOnly) continue;
                    var saved = new SavedRenderer { Renderer = renderer, ShadowMode = mode };
                    if (mode == ShadowCastingMode.Off)
                    {

                        saved.UseForceOff = true;
                        saved.ForceOff = false;
                        changed.Add(saved);
                        renderer.forceRenderingOff = true;
                    }
                    else
                    {
                        changed.Add(saved);
                        renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    }
                }
                StateDescription = "character " + selectedId + ", " + changed.Count + " hidden renderers";
                failedLogged = false;
            }
            catch (Exception exception)
            {

                Restore();
                ClearSelection();
                nextRetry = now.AddSeconds(1);
                StateDescription = "temporarily unavailable";
                if (!failedLogged) log?.LogWarning("POV head visibility could not prepare; original visibility restored: " + exception);
                failedLogged = true;
            }
        }

        void CacheRenderers(Human character, bool hideAccessories)
        {
            renderers.Clear(); rendererIds.Clear();
            var face = character.Face;
            if (face != null)
            {
                AddRoot(face.objHead);
                AddRenderer(face.rendFace); AddRenderer(face._rendFaceShadow);
                AddRenderers(face.rendEye); AddRenderer(face.rendEyeline);
                AddRenderer(face.rendEyelineUp); AddRenderer(face.rendEyelineDown);
                AddRenderer(face.rendEyelid); AddRenderer(face.rendEyebrow);
                AddRenderer(face.rendTongueFace); AddRenderer(face.rendTooth); AddRenderer(face.rendDoubleTooth);
                var gags=face._objGags;
                if(gags!=null) foreach(var root in gags) AddRoot(root);
            }
            var hair=character.Hair?.Hairs;
            if(hair!=null)
                foreach(var item in hair) if(item!=null && item.HasValue) { AddRoot(item.Obj); AddRenderers(item.Renderers); }
            if(!hideAccessories) return;
            var head=character.GetRefTransform(Table.RefObjKey.HeadParent);
            var accessories=character.Acs?.Accessories;
            if(!head || accessories==null) return;

            foreach(var item in accessories)
            {
                if(item==null || !item.HasValue || !item.Obj) continue;
                var t=item.Obj.transform;
                while(t && t!=head) t=t.parent;
                if(t==head) { AddRoot(item.Obj); AddRenderers(item.Renderers); }
            }
        }
        void AddRoot(GameObject root)
        {
            if (root) AddRenderers(root.GetComponentsInChildren<Renderer>(true));
        }

        void AddRenderers(Il2CppArrayBase<Renderer> values)
        {
            if (values == null) return;
            for (int i = 0; i < values.Length; ++i) AddRenderer(values[i]);
        }

        void AddRenderer(Renderer renderer)
        {
            if (renderer && rendererIds.Add(renderer.GetInstanceID())) renderers.Add(renderer);
        }

        public void Restore()
        {
            for (int i = changed.Count - 1; i >= 0; --i)
            {
                var saved = changed[i];
                try
                {
                    if (saved.Renderer)
                    {
                        if (saved.UseForceOff) saved.Renderer.forceRenderingOff = saved.ForceOff;
                        else saved.Renderer.shadowCastingMode = saved.ShadowMode;
                    }
                    changed.RemoveAt(i);
                }
                catch (Exception exception)
                {

                    if (!restoreFailedLogged) log?.LogWarning("POV head visibility will retry restoring a renderer: " + exception);
                    restoreFailedLogged = true;
                }
            }
            if (changed.Count == 0) restoreFailedLogged = false;
        }

        void ClearSelection()
        {
            selected = null;
            selectedId = 0;
            renderers.Clear();
            rendererIds.Clear();
            nextRefresh = DateTime.MinValue;
        }

        public void Dispose()
        {
            Restore();
            if (disposed) return;
            disposed = true;
            ClearSelection();

            PovActive = false;
            StateDescription = "disposed";
        }
    }
}
