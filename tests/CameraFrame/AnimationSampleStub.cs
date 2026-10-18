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
            SourceId = sourceId; SceneId = sceneId; CharacterId = characterId;
            AnimationKey = animationKey; PhaseHash = phaseHash; LoopType = loopType;
            PovActive = povActive; HasAnimation = hasAnimation; Stable = stable;
        }
    }
}
