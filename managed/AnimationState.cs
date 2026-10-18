using System;
using AL.H;
using BepInEx.Logging;
using Character;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AmanatsuLocationVR
{
    internal readonly struct AnimationSample
    {
        public readonly int SourceId, SceneId, CharacterId, PhaseHash, LoopType;
        public readonly string AnimationKey;
        public readonly bool PovActive, HasAnimation, Stable;
        public AnimationSample(int sourceId, int sceneId, int characterId, string animationKey,
            int phaseHash, int loopType, bool povActive, bool hasAnimation, bool stable)
        {
            SourceId=sourceId; SceneId=sceneId; CharacterId=characterId; AnimationKey=animationKey;
            PhaseHash=phaseHash; LoopType=loopType; PovActive=povActive; HasAnimation=hasAnimation; Stable=stable;
        }
    }

    internal sealed class AnimationState : IDisposable
    {
        readonly ManualLogSource log;
        int lastFrame=-1, lastSourceId, stableSamples;
        AnimationSample lastSample, candidate;
        bool disposed, failedLogged, haveCandidate;
        IntPtr infoPointer;
        string animationKey;
        public AnimationState(ManualLogSource logger) { log=logger; }
        public AnimationSample Sample(Camera source)
        {
            int sourceId=source ? source.GetInstanceID() : 0;
            if(disposed) return default;
            if(lastFrame==Time.frameCount && lastSourceId==sourceId) return lastSample;
            lastFrame=Time.frameCount; lastSourceId=sourceId;
            try
            {
                var scene=PovxCompatibility.FindScene();
                var character=PovxCompatibility.SelectedCharacter;
                bool pov=character!=null;
                Animator animator=character?.Body?.animBody;
                if(character==null && scene && scene.Actors!=null)
                    foreach(var actor in scene.Actors)
                        if(actor!=null && PovxCompatibility.Valid(actor.Human)) { character=actor.Human; animator=actor.Animator; break; }
                int characterId=PovxCompatibility.Valid(character) ? character.GameObject.GetInstanceID() : 0;
                int sceneId=scene ? scene.GetInstanceID() : SceneManager.GetActiveScene().handle;
                bool hasAnimation=scene && !scene.IsDisposed && !scene.IsEnded;
                var info=hasAnimation ? scene.NowAnimationInfo : null;
                int phase=0;
                bool settled=!hasAnimation;
                if(hasAnimation && info!=null)
                {
                    if(info.Pointer!=infoPointer || animationKey==null)
                    {
                        infoPointer=info.Pointer;
                        animationKey=info.Mode+":"+info.Kind+":"+info.ID+":"+info.NameAnimation;
                    }
                    settled=!scene.NowChangeAnimation && !scene.NowChangedPosture;
                    if(animator)
                    {
                        settled &= !animator.IsInTransition(0);
                        phase=animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
                    }
                    else settled=false;
                    var next=new AnimationSample(sourceId,sceneId,characterId,animationKey,phase,0,pov,true,false);
                    bool changed=!haveCandidate || next.SourceId!=candidate.SourceId || next.SceneId!=candidate.SceneId
                        || next.CharacterId!=candidate.CharacterId || next.AnimationKey!=candidate.AnimationKey
                        || next.PhaseHash!=candidate.PhaseHash || next.PovActive!=candidate.PovActive;
                    if(!settled) { haveCandidate=false; stableSamples=0; }
                    else if(changed) { candidate=next; haveCandidate=true; stableSamples=1; }
                    else if(stableSamples<2) ++stableSamples;
                    settled &= stableSamples>=2;
                }
                else if(hasAnimation) { haveCandidate=false; stableSamples=0; }
                else { infoPointer=IntPtr.Zero; animationKey=null; haveCandidate=false; stableSamples=0; }
                lastSample=new AnimationSample(sourceId,sceneId,characterId,hasAnimation?animationKey:null,phase,0,pov,hasAnimation,settled);
                failedLogged=false;
            }
            catch(Exception ex)
            {
                haveCandidate=false; stableSamples=0;
                lastSample=new AnimationSample(sourceId,lastSample.SceneId,lastSample.CharacterId,lastSample.AnimationKey,
                    lastSample.PhaseHash,lastSample.LoopType,PovxCompatibility.PovActive,lastSample.HasAnimation,false);
                if(!failedLogged) log?.LogWarning("AL animation reference unavailable; automatic camera rebasing waits: "+ex);
                failedLogged=true;
            }
            return lastSample;
        }
        public bool TryGetGameUiVisible(out bool visible)
        {
            visible=false;
            if(disposed) return false;
            try
            {
                var scene=PovxCompatibility.FindScene();
                if(!scene || scene.IsDisposed || scene.IsEnded) return false;
                var hide=scene._hideUI;
                visible=scene.IsEnableUI() && (hide==null || !hide.Value);
                return true;
            }
            catch { return false; }
        }
        public void Dispose() { disposed=true; animationKey=null; }
    }
}
