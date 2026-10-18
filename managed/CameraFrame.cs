using System;
using BepInEx.Logging;
using UnityEngine;

namespace AmanatsuLocationVR
{
    internal enum DetachedTransitionPolicy { Rebase = 0, ReattachOnAnimation = 1, Freeze = 2 }

    internal sealed class CameraFrame
    {
        public struct Settings
        {
            public bool Stabilize;
            public float PositionTauSeconds, RotationTauSeconds, StabilizationStrength;
            public Vector3 PovPositionOffset, PovRotationEuler;
            public DetachedTransitionPolicy TransitionPolicy;
        }

        readonly ManualLogSource log;
        bool initialized, filterReady, togglePending, haveStableReference;
        Vector3 filteredPosition, frozenPosition, stablePosition;
        Quaternion filteredRotation, frozenRotation, stableRotation;
        AnimationSample previous, stableSample;
        public bool Detached { get; private set; }
        public string StateDescription { get; private set; } = "attached";
        public Vector3 LatestLivePosition { get; private set; }
        public Quaternion LatestLiveRotation { get; private set; }

        public CameraFrame(ManualLogSource logger) { log = logger; }

        public void ToggleDetach() { togglePending = !togglePending; }

        public void Reset()
        {
            initialized = filterReady = togglePending = haveStableReference = false;
            Detached = false;
            StateDescription = "attached";
        }

        public void Evaluate(Vector3 livePosition, Quaternion liveRotation, AnimationSample sample,
            float deltaTime, Settings settings, out Vector3 position, out Quaternion rotation)
        {
            LatestLivePosition = livePosition;
            LatestLiveRotation = liveRotation;

            bool contextChanged = initialized && !SameContext(previous, sample);
            if (contextChanged)
            {
                if (Detached) SetAttached("Camera reattached to the new scene, source camera or POV character.");
                filterReady = haveStableReference = false;
            }
            bool comparable = sample.Stable && haveStableReference && sample.HasAnimation && stableSample.HasAnimation;
            bool animationChanged = comparable && sample.AnimationKey != stableSample.AnimationKey;
            bool phaseChanged = comparable && (animationChanged || sample.PhaseHash != stableSample.PhaseHash || sample.LoopType != stableSample.LoopType);

            bool stabilize = settings.Stabilize && sample.PovActive;
            if (!filterReady || animationChanged || !stabilize || !Finite(deltaTime) || deltaTime > 0.5f)
            {
                filteredPosition = livePosition;
                filteredRotation = liveRotation;
                filterReady = true;
            }
            else
            {
                filteredPosition = Vector3.Lerp(filteredPosition, livePosition, Alpha(deltaTime, settings.PositionTauSeconds));
                filteredRotation = Quaternion.Slerp(filteredRotation, liveRotation, Alpha(deltaTime, settings.RotationTauSeconds));
            }
            float strength = stabilize ? Clamp01(settings.StabilizationStrength) : 0f;
            position = Vector3.Lerp(livePosition, filteredPosition, strength);
            rotation = Quaternion.Slerp(liveRotation, filteredRotation, strength);

            if (Detached && phaseChanged)
            {
                if (settings.TransitionPolicy == DetachedTransitionPolicy.Rebase)
                {
                    var inverseOld = Quaternion.Inverse(stableRotation);
                    var localPosition = inverseOld * (frozenPosition - stablePosition);
                    var localRotation = inverseOld * frozenRotation;
                    frozenPosition = livePosition + liveRotation * localPosition;
                    frozenRotation = liveRotation * localRotation;
                    StateDescription = "detached; rebased to new " + (animationChanged ? "animation" : "phase");
                    log?.LogInfo("Camera remains detached; frozen offset rebased to the new " + (animationChanged ? "animation." : "phase."));
                }
                else if (animationChanged && settings.TransitionPolicy == DetachedTransitionPolicy.ReattachOnAnimation)
                {
                    SetAttached("Camera reattached after the animation changed.");
                    position = filteredPosition = livePosition;
                    rotation = filteredRotation = liveRotation;
                }
            }

            if (togglePending)
            {
                togglePending = false;
                if (!sample.PovActive) { }
                else if (Detached)
                {
                    SetAttached("Camera reattached to the current game/POVX pose.");

                    position = filteredPosition = livePosition;
                    rotation = filteredRotation = liveRotation;
                }
                else
                {
                    Detached = true;
                    frozenPosition = position;
                    frozenRotation = rotation;

                    stablePosition = livePosition;
                    stableRotation = liveRotation;
                    stableSample = sample;
                    haveStableReference = true;
                    StateDescription = "detached";
                    log?.LogInfo("Camera detached: rendered game reference frozen; headset tracking remains live.");
                }
            }
            if (Detached) { position = frozenPosition; rotation = frozenRotation; }

            if (sample.Stable && (!Detached || !haveStableReference || phaseChanged))
            {
                stablePosition = livePosition;
                stableRotation = liveRotation;
                stableSample = sample;
                haveStableReference = true;
            }
            previous = sample;
            initialized = true;

            if (sample.PovActive)
            {
                position += rotation * settings.PovPositionOffset;
                rotation *= Quaternion.Euler(settings.PovRotationEuler);
            }
        }

        void SetAttached(string message)
        {
            Detached = false;
            StateDescription = "attached";
            log?.LogInfo(message);
        }

        static bool SameContext(AnimationSample a, AnimationSample b)
        {
            return a.SourceId == b.SourceId && a.SceneId == b.SceneId && a.CharacterId == b.CharacterId
                && a.PovActive == b.PovActive && a.HasAnimation == b.HasAnimation;
        }

        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static float Clamp01(float value) { return !Finite(value) ? 0f : Math.Max(0f, Math.Min(1f, value)); }
        static float Alpha(float deltaTime, float tau)
        {
            if (!Finite(tau) || tau <= 0f) return 1f;
            return (float)(1.0 - Math.Exp(-Math.Max(0f, deltaTime) / tau));
        }
    }
}
