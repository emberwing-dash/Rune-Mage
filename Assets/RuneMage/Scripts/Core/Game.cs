using System.Collections.Generic;
using UnityEngine;
using RuneMage.UI;
using RuneMage.Voice;

namespace RuneMage
{
    /// <summary>
    /// Composition root. The scene contains this component plus the Meta XR camera rig (with passthrough, hands and MRUK);
    /// everything else is created here so the systems stay modular and the scene file stays tiny.
    /// </summary>
    public sealed class Game : MonoBehaviour
    {
        public static Game I { get; private set; }

        [Header("Wired in the scene")]
        public Transform CenterEye;
        public OVRHand LeftOvrHand, RightOvrHand;
        public OVRSkeleton LeftSkeleton, RightSkeleton;

        public HandInput Hands { get; private set; }
        public SpeechService Speech { get; private set; }
        public TtsService Tts { get; private set; }
        public VoiceRouter Voice { get; private set; }
        public MrSpatial Spatial { get; private set; }
        public PlayerWard Ward { get; private set; }
        public CombatSystem Combat { get; private set; }
        public SpellCanvas Canvas { get; private set; }
        public GuideRenderer Guide { get; private set; }
        public SpellCaster Caster { get; private set; }
        public HudUi Hud { get; private set; }
        public Grimoire Grimoire { get; private set; }
        public QuestSystem Quests { get; private set; }
        public StoryDirector Story { get; private set; }
        public Environment Env { get; private set; }
        public Inventory Inventory { get; private set; }
        public Achievements Achievements { get; private set; }

        /// <summary>Set false while a menu-like moment (book open, cutscene) should not accept ink.</summary>
        public bool InkAllowed = true;
        public bool ShowFingerOrb = true;

        public Vector3 PlayerChest => Hands.Head.position + Vector3.down * 0.3f;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 72;
            SaveSystem.Load();

            if (CenterEye == null && Camera.main != null) CenterEye = Camera.main.transform;

            Speech = gameObject.AddComponent<SpeechService>();
            Tts = gameObject.AddComponent<TtsService>();
            Voice = gameObject.AddComponent<VoiceRouter>();
            Voice.Init(Speech, Tts);

            Hands = gameObject.AddComponent<HandInput>();
            Hands.Head = CenterEye;
            Hands.Bind(LeftOvrHand, LeftSkeleton, RightOvrHand, RightSkeleton);

            Ward = gameObject.AddComponent<PlayerWard>();
            Spatial = gameObject.AddComponent<MrSpatial>();
            Combat = gameObject.AddComponent<CombatSystem>();
            Canvas = gameObject.AddComponent<SpellCanvas>();
            Guide = gameObject.AddComponent<GuideRenderer>();
            Caster = gameObject.AddComponent<SpellCaster>();
            Quests = gameObject.AddComponent<QuestSystem>();
            Hud = Get<HudUi>();
            Grimoire = Get<Grimoire>();
            Story = gameObject.AddComponent<StoryDirector>();
            Env = FindFirstObjectByType<Environment>();               // the trees and stand are saved in the scene ("Props")
            if (Env == null) Env = gameObject.AddComponent<Environment>();
            Inventory = Get<Inventory>();
            Achievements = gameObject.AddComponent<Achievements>();
        }

        void Start()
        {
            Spatial.Init(this);
            Combat.Init(this);
            Canvas.Init(this);
            Guide.Init(this);
            Caster.Init(this);
            Quests.Init(this);
            Hud.Init(this);
            Grimoire.Init(this);
            Story.Init(this);
            Env.Init(this);
            Inventory.Init(this);
            Achievements.Init(this);
            Voice.Register("main_menu", new[] { "main menu", "go to the main menu", "back to menu", "quit to menu" },
                "returns to the main menu (progress is saved)", _ => { SaveSystem.Save(); UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu"); }, "General");
            HideRigHandMeshes();
        }

        /// <summary>The HUD, grimoire and inventory panel are saved in the scene as components with their UI; reuse them, add if missing.</summary>
        T Get<T>() where T : Component
        {
            var c = GetComponent<T>();
            return c != null ? c : gameObject.AddComponent<T>();
        }

        /// <summary>The real hands are visible through passthrough; hide Meta's virtual hand meshes but keep tracking.</summary>
        void HideRigHandMeshes()
        {
            foreach (var h in new[] { LeftOvrHand, RightOvrHand })
            {
                if (h == null) continue;
                foreach (var r in h.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                var mr = h.GetComponent<OVRMeshRenderer>();
                if (mr != null) mr.enabled = false;
            }
        }

        // --- helpers shared by systems --------------------------------------------------------------------------

        /// <summary>Show a subtitle and (optionally) speak a line.</summary>
        public void Say(string line, bool speak = true)
        {
            Hud.ShowSubtitle("", line, Mathf.Clamp(line.Length / 12f, 2.5f, 8f));
            if (speak && SaveSystem.Data.VoiceOverEnabled) Tts.SayNow(line);
        }

        /// <summary>"Cast fireball" with no circle yet: guide the player through drawing it. Never bypasses the drawing.</summary>
        public void RequestGuidedCast(SpellDef spell)
        {
            Story.StartPractice(spell, true);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveSystem.Save();
        }

        void OnApplicationQuit() => SaveSystem.Save();
    }
}
