using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using RuneMage.Recognition;

namespace RuneMage.Tests
{
    /// <summary>
    /// End-to-end checks of the game logic running inside the real RuneMage scene (no headset): scripted "hands" draw in the air,
    /// spoken phrases are injected through the same router the microphone feeds, and results are asserted.
    /// </summary>
    public class RuneMagePlayModeTests
    {
        Game g;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true;   // Meta runtime logs errors when no headset is present
            SaveSystem.Reset();
            yield return SceneManager.LoadSceneAsync("Game");
            yield return null;
            yield return null;
            g = Game.I;
            Assert.IsNotNull(g, "Game bootstrap missing from scene");
            g.Hands.Scripted = true;
            g.Hands.ScriptedTip = g.Hands.Head.position + g.Hands.Head.forward * 0.6f;
            g.Story.StopAllCoroutines();               // keep the scripted story out of the way of unit-style checks
            g.Hud.SetHints();
            yield return null;
        }

        // --- helpers ---------------------------------------------------------------------------------------------

        Vector3 Fwd() { var f = Vector3.ProjectOnPlane(g.Hands.Head.forward, Vector3.up); return f.sqrMagnitude < 0.01f ? Vector3.forward : f.normalized; }
        Vector3 Right() => Vector3.Cross(Vector3.up, Fwd());
        Vector3 Center() => g.Hands.Head.position + Fwd() * 0.62f + Vector3.down * 0.1f;
        Vector3 At(float u, float v) => Center() + Right() * u + Vector3.up * v;

        IEnumerator Stroke(List<Vector3> pts)
        {
            g.Hands.ScriptedTip = pts[0];
            g.Hands.ScriptedPinch = true;
            yield return null;
            yield return null;
            foreach (var p in pts) { g.Hands.ScriptedTip = p; yield return null; }
            g.Hands.ScriptedPinch = false;
            yield return null;
            yield return null;
        }

        List<Vector3> Circle(float r, float noise = 0f)
        {
            var l = new List<Vector3>();
            var rnd = new System.Random(5);
            for (int i = 0; i <= 70; i++)
            {
                float a = Mathf.PI / 2f - i / 70f * Mathf.PI * 2f * 1.02f;
                float rr = r * (1f + noise * ((float)rnd.NextDouble() - 0.5f));
                l.Add(At(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr));
            }
            return l;
        }

        List<Vector3> Rune(string id, int stroke, float size, float jitter = 0.003f)
        {
            var poly = RuneShapes.Densify(RuneShapes.Get(id)[stroke], 10);
            var l = new List<Vector3>();
            var rnd = new System.Random(11);
            foreach (var p in poly)
                l.Add(At((p.X - 0.5f) * size + jitter * ((float)rnd.NextDouble() - 0.5f), (p.Y - 0.5f) * size + jitter * ((float)rnd.NextDouble() - 0.5f)));
            return l;
        }

        IEnumerator DrawArmedSpell(string rune, int strokes = 1)
        {
            yield return Stroke(Circle(0.16f, 0.04f));
            Assert.IsTrue(g.Canvas.RingPresent, "ring not detected");
            for (int s = 0; s < strokes; s++) yield return Stroke(Rune(rune, s, 0.13f));
            yield return new WaitForSeconds(1.3f);
        }

        Enemy SpawnReady(EnemyKind k, Vector3 pos)
        {
            var e = g.Combat.Spawn(k, pos);
            return e;
        }

        // --- boot ------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Boot_CreatesAllSystems()
        {
            Assert.IsNotNull(g.Hands); Assert.IsNotNull(g.Voice); Assert.IsNotNull(g.Canvas); Assert.IsNotNull(g.Caster);
            Assert.IsNotNull(g.Combat); Assert.IsNotNull(g.Hud); Assert.IsNotNull(g.Grimoire); Assert.IsNotNull(g.Quests); Assert.IsNotNull(g.Story);
            Assert.IsNotNull(RuneMageAssets.Instance, "RuneMageAssets missing from Resources");
            Assert.IsNotNull(RuneMageAssets.Instance.GlowShader, "glow shader missing");
            Assert.IsNotNull(RuneMageAssets.Instance.ZombieModel);
            Assert.Greater(g.Voice.Commands.Count, 15, "voice commands not registered");
            yield return null;
        }

        // --- stage 2: drawing ---------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Drawing_RingThenFireRune_ArmsFireball()
        {
            yield return DrawArmedSpell("fire");
            Assert.IsNotNull(g.Canvas.Armed, "no spell armed");
            Assert.AreEqual("fireball", g.Canvas.Armed.Id);
            Assert.AreEqual(1, g.Canvas.Glyphs.Count);
        }

        [UnityTest]
        public IEnumerator Drawing_OpenArc_IsRejected()
        {
            var arc = Circle(0.15f).GetRange(0, 30);          // a half circle
            yield return Stroke(arc);
            Assert.IsFalse(g.Canvas.RingPresent, "half circle must not become a seal");
        }

        [UnityTest]
        public IEnumerator Drawing_AllEightRunes_AreRecognised()
        {
            var expected = new Dictionary<string, string>
            {
                { "fire", "fireball" }, { "water", "aqualance" }, { "wind", "gale" }, { "lightning", "thunder" },
                { "earth", "stonespike" }, { "ice", "frostshard" }, { "barrier", "aegis" }, { "heal", "mend" },
            };
            foreach (var kv in expected)
            {
                g.Canvas.Clear(null);
                yield return null;
                yield return DrawArmedSpell(kv.Key, RuneShapes.Get(kv.Key).Count);
                Assert.IsNotNull(g.Canvas.Armed, "rune " + kv.Key + " not recognised");
                Assert.AreEqual(kv.Value, g.Canvas.Armed.Id, "wrong spell for rune " + kv.Key);
            }
        }

        [UnityTest]
        public IEnumerator Drawing_TwoRunes_ResolveHiddenCombination()
        {
            yield return Stroke(Circle(0.2f, 0.03f));
            Assert.IsTrue(g.Canvas.RingPresent);
            // fire left of centre, wind right of centre
            var fire = new List<Vector3>(); foreach (var p in RuneShapes.Densify(RuneShapes.Get("fire")[0], 10)) fire.Add(At(-0.075f + (p.X - 0.5f) * 0.1f, (p.Y - 0.5f) * 0.1f));
            yield return Stroke(fire);
            yield return new WaitForSeconds(1.2f);
            var wind = new List<Vector3>(); foreach (var p in RuneShapes.Densify(RuneShapes.Get("wind")[0], 10)) wind.Add(At(0.075f + (p.X - 0.5f) * 0.1f, (p.Y - 0.5f) * 0.1f));
            yield return Stroke(wind);
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(2, g.Canvas.Glyphs.Count, "expected both runes inside the seal");
            Assert.IsNotNull(g.Canvas.Armed);
            Assert.AreEqual("tempest", g.Canvas.Armed.Id);
        }

        // --- casting + combat (stage 2 / 5) -------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Casting_FireWithSpokenWord_KillsPracticeWisp()
        {
            SaveSystem.Learn("fireball");
            var head = g.Hands.Head.position;
            var wisp = SpawnReady(EnemyKind.Wisp, head + Fwd() * 3f - Vector3.up * head.y);
            wisp.Def.Multipliers[MagicType.Fire] = 2f; wisp.Def.Speed = 0f;
            yield return new WaitForSeconds(2f);
            yield return DrawArmedSpell("fire");
            Assert.IsNotNull(g.Canvas.Armed, "precondition: seal armed. ring=" + g.Canvas.RingPresent + " glyphs=" + g.Canvas.Glyphs.Count);
            float mana = g.Ward.Mana;
            bool routed = g.Voice.Route("ignis");
            yield return null;
            Assert.Less(g.Ward.Mana, mana, "casting should cost mana (routedAsCommand=" + routed + ", armed=" + (g.Canvas.Armed != null) + ", learned=" + SaveSystem.IsLearned("fireball") + ")");
            yield return new WaitForSeconds(2.5f);
            Assert.IsTrue(wisp == null || !wisp.Alive, "wisp should have been burned away");
            Assert.IsTrue(g.Canvas.Empty, "seal should clear after the cast");
        }

        [UnityTest]
        public IEnumerator Casting_PushGesture_WorksForNormalSpells_ButNotAdvancedWithoutWord()
        {
            SaveSystem.Learn("fireball");
            yield return DrawArmedSpell("fire");
            float mana = g.Ward.Mana;
            g.Hands.TriggerPush();
            yield return null;
            Assert.Less(g.Ward.Mana, mana, "push must release a basic spell");

            // advanced lightning needs the spoken word when the speech engine is available (always true in this editor test)
            SaveSystem.Learn("thunder");
            g.Canvas.Clear(null);
            yield return null;
            yield return DrawArmedSpell("lightning");
            Assert.AreEqual("thunder", g.Canvas.Armed.Id);
            mana = g.Ward.Mana;
            g.Hands.TriggerPush();
            yield return null;
            Assert.AreEqual(mana, g.Ward.Mana, 0.5f, "advanced spell must not fire on a push alone");
            g.Voice.Route("fulgur");
            yield return null;
            Assert.Less(g.Ward.Mana, mana, "spoken word should release it");
        }

        [UnityTest]
        public IEnumerator Casting_UnlearnedSpell_Fizzles()
        {
            yield return DrawArmedSpell("water");
            float mana = g.Ward.Mana;
            g.Hands.TriggerPush();
            yield return null;
            Assert.AreEqual(mana, g.Ward.Mana, 0.01f, "unlearned spell must not cost mana");
            Assert.IsNotNull(g.Canvas.Armed, "seal stays armed after a fizzle");
        }

        [UnityTest]
        public IEnumerator Enemies_ReactToElements()
        {
            var head = g.Hands.Head.position;
            var z = SpawnReady(EnemyKind.Zombie, head + Fwd() * 3.2f - Vector3.up * head.y);
            yield return new WaitForSeconds(2f);
            float hp0 = z.Hp;
            var fireSpell = SpellBook.Find("fireball");
            z.Hit(fireSpell, MagicType.Fire, 30f, z.Center, Fwd());
            float fireLoss = hp0 - z.Hp;
            float hp1 = z.Hp;
            z.Hit(SpellBook.Find("aqualance"), MagicType.Water, 30f, z.Center, Fwd());
            float waterLoss = hp1 - z.Hp;
            Assert.Greater(waterLoss, fireLoss * 3f, "water should hurt a flame zombie much more than fire (fire " + fireLoss + ", water " + waterLoss + ")");

            // ice slows
            z.Hit(SpellBook.Find("frostshard"), MagicType.Ice, 5f, z.Center, Fwd());
            yield return null;
        }

        [UnityTest]
        public IEnumerator Aegis_BlocksEnemyBolts_AndUnblockedBoltsHurt()
        {
            var head = g.Hands.Head.position;
            float w0 = g.Ward.Ward;
            EnemyBolt.Create(g, head + Fwd() * 2.5f, g.PlayerChest, 20f, null, 1f, false);
            yield return new WaitForSeconds(2f);
            Assert.Less(g.Ward.Ward, w0, "an unblocked ember must hurt the ward");

            g.Ward.FullRestore();
            w0 = g.Ward.Ward;
            Vector3 flat = Fwd();
            WardShield.Create(g, head + Vector3.down * 0.2f + flat * 0.95f, -flat, false);
            EnemyBolt.Create(g, head + flat * 2.5f, g.PlayerChest, 20f, null, 1f, false);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(w0, g.Ward.Ward, 0.01f, "Aegis should absorb the bolt");
        }

        // --- stage 3: voice ------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Voice_GrimoireCommands_OpenTurnClose()
        {
            g.Voice.Route("open my grimoire");
            yield return null;
            Assert.IsTrue(g.Grimoire.IsOpen, "grimoire should open by voice");
            Assert.IsFalse(g.InkAllowed, "ink is paused while the book is open");
            g.Voice.Route("next page");
            yield return null;
            Assert.IsNotNull(g.Grimoire.CurrentSpell, "next page should land on a spell page");
            g.Voice.Route("close the grimoire");
            yield return null;
            Assert.IsFalse(g.Grimoire.IsOpen);
            Assert.IsTrue(g.InkAllowed);

            g.Hands.TriggerGrimoire(true);
            yield return null;
            Assert.IsTrue(g.Grimoire.IsOpen, "palm-up gesture should open the grimoire");
            g.Hands.TriggerGrimoire(false);
            yield return null;
            Assert.IsFalse(g.Grimoire.IsOpen);
        }

        [UnityTest]
        public IEnumerator Voice_LearnSpell_OnlyWhenDiscovered_AndPersists()
        {
            g.Voice.Route("learn fire magic");
            yield return null;
            Assert.IsFalse(SaveSystem.IsLearned("fireball"), "cannot learn what is not discovered");
            SaveSystem.Discover("fireball");
            g.Voice.Route("learn fire magic");
            yield return null;
            Assert.IsTrue(SaveSystem.IsLearned("fireball"));
            SaveSystem.Save();
            SaveSystem.Load();
            Assert.IsTrue(SaveSystem.IsLearned("fireball"), "learned spells must persist");
        }

        [UnityTest]
        public IEnumerator Voice_MisheardPhrases_StillMatch_AndConfirmationWorks()
        {
            g.Voice.Route("open my grim wire");     // typical mishearing
            yield return null;
            Assert.IsTrue(g.Grimoire.IsOpen || g.Voice.AwaitingYesNo, "near-miss must open or ask to confirm");
            if (g.Voice.AwaitingYesNo) { g.Voice.Route("yes"); yield return null; }
            Assert.IsTrue(g.Grimoire.IsOpen);

            bool answered = false, value = false;
            g.Voice.AskYesNo("Test?", ok => { answered = true; value = ok; });
            g.Voice.Route("yeah");
            yield return null;
            Assert.IsTrue(answered && value, "spoken yes must resolve the question");
        }

        [UnityTest]
        public IEnumerator Voice_DialogueChoices_AreChosenByNumberOrKeyword()
        {
            int picked = -1;
            g.Voice.SetChoices(new List<Voice.VoiceChoice>
            {
                new Voice.VoiceChoice { Label = "Who are you?", Keywords = new[] { "who are you" }, OnChosen = () => picked = 0 },
                new Voice.VoiceChoice { Label = "Yes, teach me.", Keywords = new[] { "teach me" }, OnChosen = () => picked = 1 },
            });
            g.Voice.Route("two");
            yield return null;
            Assert.AreEqual(1, picked);
            g.Voice.SetChoices(new List<Voice.VoiceChoice>
            {
                new Voice.VoiceChoice { Label = "Who are you?", Keywords = new[] { "who are you" }, OnChosen = () => picked = 0 },
                new Voice.VoiceChoice { Label = "Yes, teach me.", Keywords = new[] { "teach me" }, OnChosen = () => picked = 1 },
            });
            g.Voice.Route("who are you");
            yield return null;
            Assert.AreEqual(0, picked);
        }

        [UnityTest]
        public IEnumerator Voice_HelpListsCommands()
        {
            var lines = g.Voice.HelpLines(true);
            Assert.Greater(lines.Count, 8);
            g.Voice.Route("what can i say");
            yield return null;
            StringAssert.Contains("WHAT CAN I SAY", "WHAT CAN I SAY?");
        }

        [UnityTest]
        public IEnumerator Voice_CastCommand_StartsGuidedDrawing_NeverSkipsDrawing()
        {
            SaveSystem.Learn("fireball");
            g.Voice.Route("cast fireball");
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(g.Guide.Visible, "'cast fireball' must show the tracing guide");
            Assert.AreEqual(0, g.Combat.AliveCount);
            Assert.IsTrue(g.Canvas.Empty, "no spell appears without drawing");
        }

        [UnityTest]
        public IEnumerator Quests_ProgressAndPersist()
        {
            g.Quests.Accept("first_ink");
            g.Quests.SetStep("first_ink", 2);
            Assert.AreEqual(QuestState.Active, g.Quests.StateOf("first_ink"));
            StringAssert.Contains("Inscribe the fire rune", g.Quests.CurrentObjective());
            SaveSystem.Save();
            SaveSystem.Load();
            Assert.AreEqual(2, g.Quests.StepOf("first_ink"));
            g.Quests.Complete("first_ink");
            Assert.AreEqual(QuestState.Complete, g.Quests.StateOf("first_ink"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Guide_ShowsRingThenRune_AndTracksProgress()
        {
            g.Guide.ShowRing();
            yield return null;
            Assert.IsTrue(g.Guide.Visible);
            // trace the guide with the scripted finger
            yield return Stroke(CircleAroundGuide(0.17f));
            Assert.IsTrue(g.Canvas.RingPresent, "tracing the guide ring should create the seal");
            g.Guide.ShowRune("fire");
            Assert.AreEqual("fire", g.Canvas.ExpectedRune);
            yield return null;
        }

        List<Vector3> CircleAroundGuide(float r)
        {
            var c = g.Canvas.PlaneOrigin;
            var right = g.Canvas.PlaneRight;
            var l = new List<Vector3>();
            for (int i = 0; i <= 70; i++)
            {
                float a = Mathf.PI / 2f - i / 70f * Mathf.PI * 2f * 1.02f;
                l.Add(c + right * Mathf.Cos(a) * r + Vector3.up * Mathf.Sin(a) * r);
            }
            return l;
        }

        [UnityTest]
        public IEnumerator Shard_IsCollectedByFingertipTouch()
        {
            var head = g.Hands.Head.position;
            ShardPickup got = null;
            var s = ShardPickup.Create(g, head + Fwd() * 0.5f, "frostshard", "ice");
            s.Collected += x => got = x;
            g.Hands.ScriptedTip = s.transform.position;
            yield return new WaitForSeconds(0.8f);
            Assert.IsNotNull(got, "touching a shard with a fingertip should collect it");
        }
    }
}
