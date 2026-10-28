using UnityEngine;

namespace RuneMage
{
    /// <summary>
    /// Serialised references to every art asset the game spawns at runtime. Created by Tools > RuneMage > Setup so the
    /// assets are guaranteed to be included in the build (they are referenced, not looked up by path).
    /// </summary>
    public sealed class RuneMageAssets : ScriptableObject
    {
        public Shader GlowShader;

        [Header("Flame flipbooks (5x5 atlases built from the original baked frames)")]
        public Texture2D FlameOrange, FlameRed, FlameYellow;
        public Texture2D ZombieBody, ZombieMouth, ZombieEyes;
        public Texture2D ChandelureHead, ChandelureHands, ChandelureEyes, ChandelureBase;
        public Texture2D ChandelureHead2, ChandelureHands2, ChandelureHands3, ChandelureEyes2;
        public Texture2D MageEyes;
        public Texture2D TrailFlow;                 // Eric VFX Studio "trail" flow texture used for the drawn ink

        [Header("Environment (from Pumpkin SoulDew)")]
        public GameObject[] EnvTrees;
        public GameObject StandPlate, StandCandle;

        [Header("Models")]
        public GameObject ZombieModel;
        public GameObject MageModel;
        public GameObject ChandelureModel;
        public GameObject FlameSpellModel;

        [Header("Animation clips")]
        public AnimationClip ZombieWalk, ZombieAttack, ZombieDie, ZombieCrawl;
        public AnimationClip MageCast, MageDead;

        static RuneMageAssets instance;
        public static RuneMageAssets Instance
        {
            get
            {
                if (instance == null) instance = Resources.Load<RuneMageAssets>("RuneMageAssets");
                return instance;
            }
        }
    }
}
