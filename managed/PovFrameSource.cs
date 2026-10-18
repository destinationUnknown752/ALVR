using System;
using BepInEx.Logging;
using Character;
using UnityEngine;

namespace AmanatsuLocationVR
{

    internal sealed class PovFrameSource
    {
        readonly ManualLogSource log;
        Human cached;
        Transform head, leftEye, rightEye;
        int headObjectId, bodyObjectId;
        DateTime nextRetry;
        bool active, failedLogged;
        public PovFrameSource(ManualLogSource logger) { log = logger; }
        public string StateDescription { get; private set; } = "inactive";
        public Vector3 LastPosition { get; private set; }
        public Quaternion LastRotation { get; private set; } = Quaternion.identity;
        public int SelectedCharacterId { get; private set; }
        public bool HasPose { get; private set; }
        internal void SetActive(bool value) { active = value; if (!value) Clear(); }
        internal void Unbind() { SetActive(false); }
        internal void Invalidate() { Clear(); StateDescription = PovxCompatibility.PovStateDescription; }
        void Clear()
        {
            cached = null; head = leftEye = rightEye = null; headObjectId = bodyObjectId = 0;
            HasPose = false; SelectedCharacterId = 0; StateDescription = "inactive";
        }
        public bool TryGetPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero; rotation = Quaternion.identity; HasPose = false;
            if (!active) return false;
            var character = PovxCompatibility.SelectedCharacter;
            try
            {
                if (!PovxCompatibility.Valid(character))
                {
                    Clear(); StateDescription = PovxCompatibility.PovStateDescription; return false;
                }
                var headObject = character.Face?.objHeadBone;
                int currentHeadId = headObject ? headObject.GetInstanceID() : 0;
                var bodyObject = character.Body?._objBone;
                int currentBodyId = bodyObject ? bodyObject.GetInstanceID() : 0;
                if (cached == null || cached.Pointer != character.Pointer || headObjectId != currentHeadId || bodyObjectId != currentBodyId
                    || ((!head || !leftEye || !rightEye) && DateTime.UtcNow >= nextRetry))
                {
                    cached = character; headObjectId = currentHeadId; bodyObjectId = currentBodyId;
                    SelectedCharacterId = character.GameObject.GetInstanceID();
                    head = leftEye = rightEye = null;

                    var bodyBones = bodyObject ? bodyObject.GetComponentsInChildren<Transform>(true) : null;
                    if (bodyBones != null) foreach (var bone in bodyBones)
                        if (bone && bone.name == "cf_s_head") { head = bone; break; }
                    var bones = (headObject ? headObject : character.GameObject).GetComponentsInChildren<Transform>(true);
                    foreach (var bone in bones)
                    {
                        if (!bone) continue;
                        switch (bone.name)
                        {
                            case "cf_s_head": if (!head) head = bone; break;
                            case "cf_J_Eye_s_L": leftEye = bone; break;
                            case "cf_J_Eye_s_R": rightEye = bone; break;
                        }
                    }
                    nextRetry = DateTime.UtcNow.AddSeconds(1);
                    StateDescription = "character " + SelectedCharacterId + ": "
                        + (head && leftEye && rightEye ? "final animated head + eye midpoint" : "waiting for head/eye bones");
                }
                if (!head || !leftEye || !rightEye) return false;
                position = (leftEye.position + rightEye.position) * 0.5f;
                rotation = head.rotation;
                if (!Finite(position) || !ValidRotation(rotation)) return false;
                LastPosition = position; LastRotation = rotation; HasPose = true; failedLogged = false;
                return true;
            }
            catch (Exception ex)
            {
                Clear(); nextRetry = DateTime.UtcNow.AddSeconds(1);
                StateDescription = "animated POV frame temporarily unavailable";
                if (!failedLogged) log?.LogWarning("Could not sample AL character POV; using game camera: " + ex);
                failedLogged = true; return false;
            }
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static bool ValidRotation(Quaternion q)
        {
            float n = q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w;
            return Finite(n) && n > 0.99f && n < 1.01f;
        }
    }
}
