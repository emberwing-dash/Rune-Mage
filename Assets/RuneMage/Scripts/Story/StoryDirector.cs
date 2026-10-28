using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RuneMage.UI;
using RuneMage.Voice;

namespace RuneMage
{
    /// <summary>
    /// Drives the narrative: prologue, the guided spell-drawing tutorial, three chapters of rift fights, the optional shard quest,
    /// the Candle Queen finale and endless Rift Trials. Everything the player must do is a spoken command, a spoken choice or a
    /// hand gesture. Dialogue is spoken (TTS when available) and subtitled.
    /// </summary>
    public sealed class StoryDirector : MonoBehaviour
    {
        Game game;
        Npc mirel, pip;
        string lastNarration = "";

        // --- event flags (set by subscribers, consumed by coroutines) --------------------------------------------
        bool strokeStarted, ringClosed, beginRequested, readyRequested, defeated, fizzled, blockedSeen, skipRequested;
        MagicType lastGlyph = MagicType.None;
        SpellDef lastCast;
        SpellDef lastLearned;
        int kills, wispKills;
        readonly HashSet<string> shown = new HashSet<string>();
        Coroutine practiceRoutine, mainRoutine;
        bool scripted;                       // true while a tutorial step owns the guide
        int shardsFound;
        readonly List<ShardPickup> shardObjs = new List<ShardPickup>();

        public void Init(Game g)
        {
            game = g;
            game.Canvas.StrokeStarted += () => strokeStarted = true;
            game.Canvas.RingClosed += () => ringClosed = true;
            game.Canvas.GlyphAccepted += t => lastGlyph = t;
            game.Canvas.Rejected += why => { game.Hud.Toast(why, UiKit.Bad, 2.4f); };
            game.Canvas.ComboUnknown += why => game.Hud.Toast(why, UiKit.Bad, 2.4f);
            game.Canvas.SpellArmed += s => game.Hud.Toast(s.Name + " ready", SpellEffects.ElementColor(s), 1.8f);
            game.Caster.Cast += (s, v) => lastCast = s;
            game.Caster.Fizzled += _ => fizzled = true;
            game.Caster.Discovered += s => game.Hud.Toast("Hidden spell discovered: " + s.Name, UiKit.Gold, 3.5f);
            game.Grimoire.SpellLearned += s => lastLearned = s;
            game.Combat.EnemyDied += e => { kills++; if (e.Def.Kind == EnemyKind.Wisp) wispKills++; };
            game.Combat.Blocked += () => blockedSeen = true;
            game.Combat.Reaction += OnReaction;
            game.Combat.EnemyBark += (e, line) => { if (!string.IsNullOrEmpty(line)) game.Hud.ShowSubtitle(e.Def.Name, line, 4f); };
            game.Ward.Depleted += () => defeated = true;
            game.Hands.GrimoireSummon += () => { beginRequested = true; };      // palm-up also begins when speech is unavailable
            game.Quests.Changed += (q, s) => RefreshObjective();

            BuildNpcs();
            RegisterVoice();
            mainRoutine = StartCoroutine(Main());
        }

        // --- NPCs -----------------------------------------------------------------------------------------------

        void BuildNpcs()
        {
            mirel = Npc.Create(game, "Mirel", new Color(1f, 0.82f, 0.4f), new Vector3(-0.75f, -0.05f, 1.35f), 0.15f, 1f);
            mirel.Fallback = "I do not know that, apprentice. Ask me about the grimoire, runes, the rift, the Ember Court, or any element.";
            mirel.Knowledge["grimoire|book|spell book"] = "The Emberbound Grimoire is a living book. It does not store spells. It remembers the shapes you have drawn well.";
            mirel.Knowledge["rune|runes|symbol|symbols"] = "A rune is an element written as a shape. A triangle for fire, a flowing wave for wind. The shape matters, not the size.";
            mirel.Knowledge["circle|seal|ring"] = "The seal is the door. Without a closed circle, a rune is only a drawing. With one, it becomes a promise.";
            mirel.Knowledge["rift|rifts|portal|portals"] = "The rifts are wounds between your world and the Ember Court. They open where walls are thin: your room is one of them.";
            mirel.Knowledge["ember court|court|zombies|walkers"] = "The Ember Court is a kingdom of fire that forgot how to die. Its walkers are mostly flame: water and ice undo them.";
            mirel.Knowledge["candle queen|queen|boss|chandelure"] = "The Candle Queen keeps the court burning. Her flame shield breaks to water, ice or wind. Do not fear her volleys: ward against them.";
            mirel.Knowledge["fire"] = "Fire is the first rune because it is the easiest to draw. It is also the one the Ember Court laughs at.";
            mirel.Knowledge["water"] = "Water is the court's oldest fear. A downward triangle, like a drop falling.";
            mirel.Knowledge["wind"] = "Wind is two waves in a row. It pushes enemies back and snuffs small flames.";
            mirel.Knowledge["lightning"] = "Lightning is a zig-zag, and it is greedy: it will not answer unless you speak its word aloud. Say fulgur.";
            mirel.Knowledge["earth"] = "Earth is a square. Patient, solid, and good at stunning things.";
            mirel.Knowledge["ice"] = "Ice is a snowflake of three crossing lines. It slows what it touches.";
            mirel.Knowledge["barrier|shield|ward|aegis"] = "The Aegis ward is a diamond. It swallows hostile magic. Raise it before the Warlock's volleys land.";
            mirel.Knowledge["heal|healing|mend"] = "Healing light is a plus sign. Drawn calmly, it restores your ward.";
            mirel.Knowledge["who are you|mirel|yourself"] = "I keep the lantern that lit this grimoire's first page. I have been waiting for someone who draws in the air.";
            mirel.Knowledge["hidden|secret|secrets|combine|combination"] = "Two runes in one circle may answer in a voice neither has alone. Wind feeds fire. Water carries lightning. Experiment.";
            mirel.Knowledge["shard|shards|echoes"] = "The rune shards are echoes of the old teachers. They hum when you are close. Reach out and touch one.";

            pip = Npc.Create(game, "Pip", new Color(1f, 0.45f, 0.25f), new Vector3(0.7f, 0.05f, 1.2f), 0.08f, 1.35f);
            pip.Follow = true;
            pip.Fallback = "Beats me! Ask about fire, shards, or whether Mirel is always this serious.";
            pip.Knowledge["fire|flame"] = "Oh, fire? I am made of it. It tickles. Do not draw it at the zombies, though. They like it.";
            pip.Knowledge["shard|shards|echoes"] = "Shiny humming rocks! I counted three. One was under your couch. I would not tell you where, but it IS under the couch.";
            pip.Knowledge["mirel"] = "Mirel? She is lovely. She also says 'apprentice' forty times a day. I counted.";
            pip.Knowledge["secret|secrets|hidden"] = "Psst: put wind and fire in the same circle. And say both words. You are welcome.";
            pip.Knowledge["warlock|ash warlock"] = "Grumpy fellow. Throws embers. Put up a ward and zap him with something that crackles.";
            pip.Knowledge["queen|candle queen|boss"] = "She is shiny and angry. Bring water. Lots of water.";
            pip.gameObject.SetActive(false);
        }

        // --- voice commands -----------------------------------------------------------------------------------------

        void RegisterVoice()
        {
            var v = game.Voice;
            v.Register("begin", new[] { "begin", "start", "continue", "let's go", "start the adventure", "continue the adventure", "play" },
                "starts or continues the adventure", _ => beginRequested = true, "Story");
            v.Register("ready", new[] { "i am ready", "ready", "i'm ready", "bring her on" },
                "tells the story you are ready", _ => readyRequested = true, "Story");
            v.Register("cancel_circle", new[] { "cancel", "erase", "clear the circle", "erase the circle", "start again" },
                "erases the circle in front of you", _ => { game.Canvas.Clear("Circle erased."); }, "Magic");
            v.Register("erase_rune", new[] { "erase the rune", "wrong rune", "redraw the rune" },
                "keeps the circle, erases the rune", _ => game.Canvas.EraseRunes(), "Magic", () => game.Canvas.RingPresent);
            v.Register("guides_on", new[] { "show the guide", "guides on", "show guides", "help me draw" },
                "shows the tracing guide", _ => { SaveSystem.Data.GuidesEnabled = true; game.Say("Guides on."); SaveSystem.Save(); ResumeGuideIfIdle(); }, "Magic");
            v.Register("guides_off", new[] { "hide the guide", "guides off", "hide guides" },
                "hides the tracing guide", _ => { SaveSystem.Data.GuidesEnabled = false; game.Guide.Hide(); game.Say("Guides off."); SaveSystem.Save(); }, "Magic");
            v.Register("practice", new[] { "practice {x}", "practise {x}", "practice", "teach me to draw {x}", "show me how to draw {x}" },
                "shows a guide for a spell you know", x => { var s = string.IsNullOrWhiteSpace(x) ? game.Grimoire.CurrentSpell : Grimoire.FindSpell(x); if (s == null) game.Say("Which spell? Say “practice fire”."); else StartPractice(s, false); }, "Magic");
            v.Register("cast", new[] { "cast {x}", "i cast {x}" },
                "starts guided drawing for a spell", x =>
                {
                    var s = Grimoire.FindSpell(x) ?? SpellBook.ByIncantation(x, out _);
                    if (s == null) { game.Say("I do not know that spell."); return; }
                    if (game.Canvas.Armed != null && game.Canvas.Armed.Id == s.Id) { game.Voice.Route(s.Incantation); return; }
                    StartPractice(s, true);
                }, "Magic");
            v.Register("remind", new[] { "remind me", "what do i do", "what now", "repeat that", "say that again", "what was that" },
                "repeats your current goal", _ => Remind(), "Story");
            v.Register("ask", new[] { "ask mirel about {x}", "ask about {x}", "tell me about {x}", "what is {x}", "what are {x}", "ask pip about {x}", "who is {x}" },
                "asks a companion a question", x => StartCoroutine(AnswerQuestion(x)), "Story");
            v.Register("voice_off", new[] { "mute the voice", "stop talking", "voice off", "be quiet" },
                "stops spoken narration (subtitles stay)", _ => { SaveSystem.Data.VoiceOverEnabled = false; game.Tts.Stop(); game.Hud.Toast("Narration off", UiKit.Dim); SaveSystem.Save(); }, "General");
            v.Register("voice_on", new[] { "turn the voice on", "voice on", "speak to me", "narrate" },
                "turns spoken narration on", _ => { SaveSystem.Data.VoiceOverEnabled = true; game.Say("Narration on."); SaveSystem.Save(); }, "General");
            v.Register("restart", new[] { "start a new game", "new game", "reset my progress", "start over completely" },
                "erases your progress (asks first)", _ => game.Voice.AskYesNo("Erase all progress and start over?", ok =>
                {
                    if (!ok) { game.Say("Progress kept."); return; }
                    SaveSystem.Reset();
                    UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                }), "General");
            v.Register("try_again", new[] { "try again", "retry", "again" },
                "retries after a defeat", _ => skipRequested = true, "Story");
        }

        // --- helpers -----------------------------------------------------------------------------------------------

        IEnumerator Speak(Npc npc, string line, bool wait = true)
        {
            lastNarration = line;
            string who = npc == null ? "" : npc.DisplayName;
            game.Hud.ShowSubtitle(who, line, 0f);
            if (npc != null) npc.Talk(Mathf.Clamp(line.Length / 14f, 1.5f, 8f));
            if (!SaveSystem.Data.VoiceOverEnabled || !wait) { game.Tts.SayNow(SaveSystem.Data.VoiceOverEnabled ? line : ""); }
            if (wait)
            {
                if (SaveSystem.Data.VoiceOverEnabled) yield return game.Tts.Say(line);
                else yield return new WaitForSeconds(Mathf.Clamp(line.Length / 16f, 1.6f, 7f));
                yield return new WaitForSeconds(0.35f);
            }
            game.Hud.ShowSubtitle(who, line, 3f);
        }

        IEnumerator AskChoice(Npc npc, string line, params (string label, string[] keys)[] opts)
        {
            yield return Speak(npc, line);
            int picked = -1;
            var list = new List<VoiceChoice>();
            for (int i = 0; i < opts.Length; i++)
            {
                int idx = i;
                list.Add(new VoiceChoice { Label = opts[i].label, Keywords = opts[i].keys, OnChosen = () => picked = idx });
            }
            game.Voice.SetChoices(list);
            game.Hud.SetHints("one", opts.Length > 1 ? "two" : null, opts.Length > 2 ? "three" : null);
            float t = 0f;
            while (picked < 0)
            {
                t += Time.deltaTime;
                if (t > 20f)
                {
                    t = 0f;
                    var sb = new System.Text.StringBuilder("You can say: ");
                    for (int i = 0; i < opts.Length; i++) sb.Append(opts[i].keys[0]).Append(i < opts.Length - 1 ? ", or " : ".");
                    game.Say(sb.ToString());
                }
                yield return null;
            }
            game.Hud.SetHints();
            lastPicked = picked;
        }

        int lastPicked;

        IEnumerator WaitFlag(Func<bool> done, string hint = null, float hintEvery = 25f, Action onHint = null)
        {
            float t = 0f;
            while (!done())
            {
                t += Time.deltaTime;
                if (hint != null && t > hintEvery) { t = 0f; game.Say(hint); onHint?.Invoke(); }
                yield return null;
            }
        }

        void ClearFlags()
        {
            strokeStarted = ringClosed = beginRequested = readyRequested = fizzled = blockedSeen = skipRequested = false;
            lastGlyph = MagicType.None; lastCast = null; lastLearned = null;
        }

        void RefreshObjective() => game.Hud.SetObjective(game.Quests.CurrentObjective());

        void Remind()
        {
            string obj = game.Quests.CurrentObjective();
            string line = string.IsNullOrEmpty(lastNarration) ? (obj ?? "Say continue to move the story on.") : lastNarration;
            if (obj != null) line = "Your goal: " + obj + ". " + (line == lastNarration ? "" : line);
            game.Say(line);
        }

        void OnReaction(Enemy e, MagicType el, string r)
        {
            string key = e.Def.Kind + el.ToString() + r;
            if (shown.Contains(key)) return;
            shown.Add(key);
            if (r == "resist" && e.Def.Kind == EnemyKind.Zombie) resistSeen = true;
            if (r == "resist") game.Hud.Toast(e.Def.Name + " resists " + el, UiKit.Bad, 2.5f);
            else game.Hud.Toast("Weak to " + el + "!", UiKit.Good, 2.2f);
        }

        IEnumerator AnswerQuestion(string topic)
        {
            // route to Pip when the player mentions him, or alternately; Mirel by default
            string tn = TextMatch.Normalize(topic);
            Npc who = (pip.gameObject.activeSelf && (TextMatch.ContainsWord(tn, "pip", 0.8f) || UnityEngine.Random.value < 0.25f)) ? pip : mirel;
            tn = tn.Replace("pip", "").Replace("mirel", "").Trim();
            string ans = who.Answer(tn);
            yield return Speak(who, ans, false);
        }

        // --- guided drawing ---------------------------------------------------------------------------------------

        /// <summary>Ring guide, then rune guide; completes when the rune is accepted (or the seal is cleared).</summary>
        IEnumerator GuideSequence(MagicType rune, float giveUpAfter = 120f)
        {
            string id = Magic.RuneId(rune);
            game.Canvas.Clear(null);
            game.Guide.ShowRing();
            float t = 0f;
            bool ringMsg = false;
            while (!game.Canvas.RingPresent)
            {
                t += Time.deltaTime;
                if (!ringMsg && t > 12f) { ringMsg = true; game.Say("Trace the glowing dots with your pinched finger, all the way round, and close the loop."); }
                if (t > giveUpAfter) yield break;
                yield return null;
            }
            yield return new WaitForSeconds(0.4f);
            game.Guide.ShowRune(id);
            t = 0f;
            while (!game.Canvas.Glyphs.Exists(g => g.Type == rune))
            {
                if (!game.Canvas.RingPresent) yield break;
                t += Time.deltaTime;
                if (t > giveUpAfter) yield break;
                yield return null;
            }
            game.Guide.Hide();
        }

        public void StartPractice(SpellDef spell, bool fromVoice)
        {
            if (scripted) { game.Say("First finish this lesson. Then we can practise."); return; }
            if (!game.Caster.CanUse(spell))
            {
                game.Say("You have not learned " + spell.Name + " yet. Say “learn " + spell.Primary.ToString().ToLower() + " magic” when it is discovered.");
                return;
            }
            if (practiceRoutine != null) StopCoroutine(practiceRoutine);
            practiceRoutine = StartCoroutine(PracticeRoutine(spell));
        }

        IEnumerator PracticeRoutine(SpellDef spell)
        {
            game.Say("Draw " + spell.Name + ". " + spell.Hint + (spell.RequiresIncantation ? " Then speak “" + spell.Incantation + "”." : " Then push your palm forward."), true);
            yield return GuideSequence(spell.Primary);
            if (spell.IsCombo && game.Canvas.RingPresent)
            {
                game.Guide.ShowRune(Magic.RuneId(spell.Secondary));
                float t = 0f;
                while (!game.Canvas.Glyphs.Exists(g => g.Type == spell.Secondary) && game.Canvas.RingPresent && t < 120f) { t += Time.deltaTime; yield return null; }
            }
            game.Guide.Hide();
            practiceRoutine = null;
        }

        void ResumeGuideIfIdle()
        {
            var cur = game.Grimoire.CurrentSpell;
            if (cur != null && practiceRoutine == null && !scripted) StartPractice(cur, false);
        }

        // =======================================================================================================
        //  MAIN STORY
        // =======================================================================================================

        IEnumerator Main()
        {
            yield return new WaitForSeconds(1.2f);
            game.InkAllowed = false;
            game.Hud.ShowTitle("RUNE MAGE", "The Emberbound Grimoire", 4.5f);
            yield return new WaitForSeconds(2.5f);

            if (game.Speech.Status == SpeechStatus.Initialising) yield return new WaitForSeconds(2f);
            if (!game.Speech.IsAvailable)
                game.Hud.Toast("Voice unavailable: " + game.Speech.StatusDetail + ". Using hand gestures.", UiKit.Gold, 5f);

            // start / continue -- by voice, or palm-up
            ClearFlags();
            bool returning = SaveSystem.Data.Chapter > 0 || SaveSystem.Data.TutorialDone;
            string prompt = returning ? "Welcome back, apprentice. Say “continue” when you are ready. Or raise your open left palm." : "Say “begin” to start your story. Or raise your open left palm.";
            game.Hud.SetHints(returning ? "continue" : "begin", "what can I say", "open my grimoire");
            yield return Speak(mirel, prompt, false);
            float waited = 0f;
            while (!beginRequested)
            {
                waited += Time.deltaTime;
                if (waited > 30f) { waited = 0; game.Say(prompt); }
                yield return null;
            }
            game.Hud.SetHints();
            ClearFlags();

            if (!SaveSystem.Data.TutorialDone) yield return Prologue();
            yield return Chapters();
        }

        // --- prologue + tutorial ------------------------------------------------------------------------------

        IEnumerator Prologue()
        {
            game.Quests.Accept("first_ink");
            RefreshObjective();
            yield return Speak(mirel, "At last. A hand that can still draw in the air. I am Mirel, keeper of the lantern in this grimoire.");
            yield return AskChoice(mirel, "Your room is thin tonight. Things from the Ember Court are crossing over. Shall I teach you to defend it?",
                ("Who are you, and what is the Ember Court?", new[] { "who are you", "ember court", "who" }),
                ("Yes, teach me.", new[] { "yes teach me", "teach me", "yes" }),
                ("I am not sure I want this.", new[] { "not sure", "no", "afraid" }));
            if (lastPicked == 0)
            {
                yield return Speak(mirel, "A kingdom of fire that forgot how to die. Its creatures burn forever, and they are drawn to places where magic is written. Which is exactly what you are about to do.");
            }
            else if (lastPicked == 2)
            {
                yield return Speak(mirel, "Fear is a good teacher. Draw one circle with me. If you still wish to stop afterwards, we will stop.");
            }
            yield return Speak(mirel, "You will not use wands or buttons. Magic is drawn in the air with a pinch, and finished with your hand or your voice. Ask me anything you like at any time: say, ask Mirel about the rift.");
            game.Voice.AskYesNo("Will you accept my first lesson?", ok => { _answer = ok; _answered = true; });
            _answered = false;
            yield return new WaitUntil(() => _answered);
            if (!_answer) { yield return Speak(mirel, "Then I will teach you gently, and you may leave whenever you wish. Begin by raising your hand."); }
            else yield return Speak(mirel, "Splendid. Raise your right hand in front of you.");

            yield return Tutorial();
        }

        bool _answer, _answered;

        IEnumerator Tutorial()
        {
            scripted = true;
            game.InkAllowed = true;
            SaveSystem.Data.GuidesEnabled = true;

            // 0. learn fire (voice) ----------------------------------------------------------------------------
            SaveSystem.Discover("fireball");
            game.Quests.SetStep("first_ink", 0);
            ClearFlags();
            game.Hud.SetHints("learn fire magic");
            yield return Speak(mirel, "First, claim the knowledge. Say: learn fire magic.", false);
            yield return WaitFlag(() => lastLearned != null && lastLearned.Id == "fireball", "Say it clearly: learn fire magic.");
            game.Hud.SetHints();
            yield return Speak(mirel, "Fire is yours. Now we write it.");

            // 1. pinch + ring ----------------------------------------------------------------------------------
            game.Quests.SetStep("first_ink", 1);
            ClearFlags();
            yield return Speak(mirel, "Pinch your thumb and first finger together. That is your ink. Follow the glowing circle in the air, slowly, all the way around.", false);
            game.Canvas.Clear(null);
            game.Guide.ShowRing();
            float notTracked = 0f;
            while (!game.Canvas.RingPresent)
            {
                notTracked = game.Hands.AnyTracked ? 0f : notTracked + Time.deltaTime;
                if (notTracked > 8f) { notTracked = -30f; game.Say("I cannot see your hands. Hold them in front of you, in the light."); }
                if (fizzled) fizzled = false;
                yield return null;
            }
            yield return Speak(mirel, "The seal opens. That ring is a door. Nothing passes through it until you give it a shape.");

            // 2. rune ------------------------------------------------------------------------------------------
            game.Quests.SetStep("first_ink", 2);
            ClearFlags();
            yield return Speak(mirel, "Inside the circle, draw the fire rune: an upward triangle. Follow the dots.", false);
            game.Guide.ShowRune("fire");
            yield return WaitFlag(() => game.Canvas.Armed != null && game.Canvas.Armed.Id == "fireball", "Trace the glowing triangle inside the ring, starting at the top.", 28f, () => game.Guide.ResetProgress());
            game.Guide.Hide();
            yield return Speak(mirel, "Feel that? The circle is alive and ready. Spells are released two ways: push your open palm toward your target, or speak the word. For fire, the word is ignis.");

            // 3. release at a practice wisp --------------------------------------------------------------------
            game.Quests.SetStep("first_ink", 3);
            ClearFlags();
            var target = SpawnPracticeWisp();
            game.Hud.SetHints("ignis");
            yield return Speak(mirel, "Aim at the wisp floating over there. Push your palm toward it, or say ignis.", false);
            float tt = 0f;
            while (target != null && target.Alive)
            {
                // the seal was used (a miss) or expired: guide another redraw
                if (game.Canvas.Empty && !game.Canvas.Busy)
                {
                    tt += Time.deltaTime;
                    if (tt > 2.5f)
                    {
                        tt = 0f;
                        game.Say("Let us draw it again. Follow the glowing guide.");
                        yield return GuideSequence(MagicType.Fire);
                    }
                }
                else tt = 0f;
                yield return null;
            }
            game.Hud.SetHints();
            yield return Speak(mirel, "Perfect. Draw, release, repeat. The more carefully you draw, the stronger the spell. You may also say: hide the guide, when you no longer need it.");
            game.Quests.SetStep("first_ink", 4);
            scripted = false;
            game.Guide.Hide();
            game.Canvas.Clear(null);

            // 4. first rift ------------------------------------------------------------------------------------
            yield return Speak(mirel, "Brace yourself. The wall behind you is thinning. Something is crossing over.");
            game.Quests.Complete("first_ink");
            game.Quests.Accept("cinder_court");
            SaveSystem.Data.TutorialDone = true;
            SaveSystem.Data.Chapter = 1;
            SaveSystem.Save();
            RefreshObjective();
            AudioSynth.PlayAt("quest", game.Hands.Head.position + game.Hands.Head.forward, 0.8f);
            game.Hud.Toast("Quest complete: First Ink", UiKit.Gold, 3f);
        }

        Enemy SpawnPracticeWisp()
        {
            var head = game.Hands.Head;
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            Vector3 p = head.position + flat * 3f;
            p.y = game.Spatial.FloorY;
            var e = game.Combat.Spawn(EnemyKind.Wisp, p);
            e.Def.Multipliers[MagicType.Fire] = 2f;          // training wisp pops with a single fire spell
            e.Def.AttackInterval = 999f;
            e.Def.Speed = 0f;                                  // holds still so the first cast cannot miss
            return e;
        }

        // --- chapters ------------------------------------------------------------------------------------------

        IEnumerator Chapters()
        {
            game.InkAllowed = true;
            if (SaveSystem.Data.Chapter <= 1) yield return Chapter1();
            if (SaveSystem.Data.Chapter <= 2) yield return Chapter2();
            if (SaveSystem.Data.Chapter <= 3) yield return Chapter3();
            yield return Epilogue();
            yield return RiftTrials();
        }

        /// <summary>Spawn, fight and retry loop. Returns when every spawned enemy is dead.</summary>
        IEnumerator Encounter(Func<List<Enemy>> spawn, string retryLine)
        {
            while (true)
            {
                defeated = false;
                game.Ward.FullRestore();
                var list = spawn();
                while (true)
                {
                    bool alive = false;
                    foreach (var e in list) if (e != null && e.Alive) { alive = true; break; }
                    if (!alive) yield break;
                    if (defeated) break;
                    yield return null;
                }
                // defeat: gentle, no game over
                game.Combat.ClearAll();
                game.Canvas.Clear(null);
                game.Ward.FullRestore();
                skipRequested = false;
                yield return Speak(mirel, retryLine, false);
                game.Hud.SetHints("try again");
                game.Voice.AskYesNo("Try again?", ok => { _answer = ok; _answered = true; }, 60f);
                _answered = false;
                float w = 0;
                while (!_answered && !skipRequested && w < 65f) { w += Time.deltaTime; yield return null; }
                game.Hud.SetHints();
                game.Voice.ClearChoices();
                yield return new WaitForSeconds(1f);
            }
        }

        bool resistSeen;

        IEnumerator Chapter1()
        {
            game.Quests.Accept("cinder_court");
            game.Quests.SetStep("cinder_court", 0);
            RefreshObjective();
            ClearFlags();
            resistSeen = false;
            game.InkAllowed = true;
            Coroutine coach = null;
            yield return Encounter(() =>
            {
                if (coach != null) StopCoroutine(coach);
                var l = new List<Enemy> { game.Combat.Spawn(EnemyKind.Zombie), game.Combat.Spawn(EnemyKind.Zombie) };
                coach = StartCoroutine(Chapter1Coach());
                return l;
            }, "Your ward is spent. Breathe. The rift will wait. Shall we try again?");
            if (coach != null) StopCoroutine(coach);
            game.Hud.SetHints();
            game.Quests.SetStep("cinder_court", 1);
            game.Quests.Complete("cinder_court");
            SaveSystem.Data.Chapter = 2;
            SaveSystem.Save();
            game.Hud.Toast("Quest complete: The Cinder Court", UiKit.Gold, 3f);
            yield return Speak(mirel, "They are down. Water, ice and wind undo the Ember Court. Remember that, apprentice.");
        }

        /// <summary>Mid-fight teaching: fire is resisted, so Mirel hands the player water magic.</summary>
        IEnumerator Chapter1Coach()
        {
            float t = 0f;
            while (!resistSeen && t < 25f) { t += Time.deltaTime; yield return null; }
            SaveSystem.Discover("aqualance");
            game.Hud.SetHints("learn water magic");
            if (!SaveSystem.IsLearned("aqualance"))
            {
                yield return Speak(mirel, "Fire only feeds them! Quick: say, learn water magic.", false);
                yield return WaitFlag(() => SaveSystem.IsLearned("aqualance"), "Say: learn water magic.", 15f);
            }
            game.Hud.SetHints("practice water");
            game.Hud.Toast("Water magic learned", Magic.Color(MagicType.Water), 3f);
            game.Quests.SetStep("cinder_court", 1);
            yield return Speak(mirel, "Draw a seal, then a downward triangle: water. Say aqua or push your palm. Follow the guide.", false);
            StartPractice(SpellBook.Find("aqualance"), false);
            yield return new WaitForSeconds(6f);
            game.Hud.SetHints();
        }

        IEnumerator Chapter2()
        {
            SaveSystem.Discover("aqualance");
            game.Quests.Accept("ash_warlock");
            game.Quests.Offer("echoes");
            RefreshObjective();
            pip.gameObject.SetActive(true);
            yield return Speak(pip, "Heeey! Did somebody say hot zombies? I am Pip. I live in the cracks between rifts. Nice drawing, by the way. Mostly.");
            yield return AskChoice(mirel, "Pip is harmless. Mostly. There is something else: three rune shards hum somewhere in your room. Each remembers a forgotten element. Will you look for them?",
                ("Yes, I will look for the shards.", new[] { "yes", "look for them", "accept" }),
                ("Not now.", new[] { "not now", "no", "later" }));
            if (lastPicked == 0)
            {
                game.Quests.Accept("echoes");
                SpawnShards();
                yield return Speak(mirel, "Good. They glow softly. Touch one with your fingertip to wake it. Ask me where they are, if you lose your way.");
            }
            else yield return Speak(mirel, "As you wish. They will keep. But the Ash Warlock will not. Prepare yourself.");

            yield return Speak(mirel, "First, you need shelter. Say: learn barrier magic, then draw a diamond: that is the Aegis ward. The word is murus.", false);
            SaveSystem.Discover("aegis");
            SaveSystem.Discover("gale");
            game.Quests.SetStep("ash_warlock", 0);
            game.Hud.SetHints("learn barrier magic", "learn wind magic", "practice barrier");
            ClearFlags();
            yield return WaitFlag(() => SaveSystem.IsLearned("aegis"), "Say: learn barrier magic.");
            game.Quests.SetStep("ash_warlock", 1);
            game.Hud.SetHints("learn wind magic");
            yield return Speak(mirel, "Wind next, for the Warlock's fire is weak to it. Say: learn wind magic.", false);
            yield return WaitFlag(() => SaveSystem.IsLearned("gale"), "Say: learn wind magic.");
            game.Hud.SetHints();
            yield return Speak(mirel, "When you are ready to meet the Ash Warlock, say: I am ready.", false);
            game.Hud.SetHints("I am ready");
            ClearFlags();
            yield return WaitFlag(() => readyRequested, "Say: I am ready. Take your time; practise with: practice barrier.", 40f);
            game.Hud.SetHints();
            ClearFlags();

            Enemy mage = null;
            yield return Encounter(() =>
            {
                mage = game.Combat.Spawn(EnemyKind.ZombieMage);
                return new List<Enemy> { mage };
            }, "The Warlock's fire overwhelmed your ward. Rest, then raise the Aegis sooner.");
            game.Quests.SetStep("ash_warlock", 2);
            game.Quests.Complete("ash_warlock");
            SaveSystem.Data.Chapter = 3;
            SaveSystem.Save();
            yield return Speak(pip, "Ha! Did you see his face? He had no face. That is the point.");
            yield return Speak(mirel, "Well done. Every rift leads to one throne: the Candle Queen's. Collect yourself, and speak when you are ready.");
        }

        IEnumerator Chapter3()
        {
            game.Quests.Accept("candle_queen");
            RefreshObjective();
            ClearFlags();
            game.Hud.SetHints("I am ready", "practice water", "ask Mirel about the Candle Queen");
            yield return Speak(mirel, "Rest your arms if you need to. Practise. Ask me about the Queen. When you are ready for her, say: I am ready.", false);
            yield return WaitFlag(() => readyRequested, "Say: I am ready, whenever you want to face the Candle Queen.", 45f);
            game.Hud.SetHints();
            ClearFlags();
            yield return Speak(mirel, "Her flame shield breaks only to water, ice or wind. When she gathers a nova, strike hard to interrupt it, or ward against it.");

            Enemy queen = null;
            bool shieldHinted = false;
            yield return Encounter(() =>
            {
                queen = game.Combat.Spawn(EnemyKind.Chandelure);
                StartCoroutine(BossCommentary(queen));
                return new List<Enemy> { queen };
            }, "The Queen's fire was too bright. Gather yourself. Water and wind are your friends.");
            if (!shieldHinted) { }
            game.Quests.SetStep("candle_queen", 2);
            game.Quests.Complete("candle_queen");
            SaveSystem.Data.Chapter = 4;
            SaveSystem.Save();
        }

        IEnumerator BossCommentary(Enemy queen)
        {
            bool p2 = false, p3 = false, shield = false;
            while (queen != null && queen.Alive)
            {
                if (!shield && !queen.ShieldUp && queen.Phase == 1) { shield = true; game.Quests.SetStep("candle_queen", 1); }
                if (!p2 && queen.Phase >= 2) { p2 = true; game.Quests.SetStep("candle_queen", 1); yield return Speak(mirel, "Her embers come in threes now. Raise an Aegis ward, then answer with water.", false); }
                if (!p3 && queen.Phase >= 3) { p3 = true; game.Quests.SetStep("candle_queen", 2); yield return Speak(mirel, "She is gathering all her fire. Interrupt it, or hide behind a ward!", false); }
                yield return null;
            }
        }

        IEnumerator Epilogue()
        {
            game.Combat.ClearAll();
            game.Hud.SetObjective(null);
            yield return new WaitForSeconds(1.5f);
            yield return Speak(mirel, "The last candle gutters out. Listen. Your room is only a room again.");
            yield return Speak(pip, "Aww. I was going to miss the zombies. Hey, can I stay? I promise not to set anything on fire. Probably.");
            yield return Speak(mirel, "You wrote magic into the air, with your own hands and your own voice. The grimoire is complete. It no longer needs a keeper.");
            yield return Speak(mirel, "But the rifts do not all close at once. If you wish, I will open the Rift Trials, so you may keep your craft sharp.");
            game.Hud.ShowTitle("THE END", "The Emberbound Grimoire is complete", 6f);
            game.Hud.Toast("Say “begin” to open Rift Trials", UiKit.Gold, 6f);
            SaveSystem.Data.Chapter = 5;
            SaveSystem.Save();
            ClearFlags();
            game.Hud.SetHints("begin", "open my grimoire");
            yield return WaitFlag(() => beginRequested);
            game.Hud.SetHints();
        }

        IEnumerator RiftTrials()
        {
            int wave = 1;
            while (true)
            {
                ClearFlags();
                game.Hud.SetObjective("Rift Trial " + wave);
                int n = Mathf.Min(1 + wave, 5);
                yield return Encounter(() =>
                {
                    var l = new List<Enemy>();
                    for (int i = 0; i < n; i++)
                        l.Add(game.Combat.Spawn(i % 3 == 2 ? EnemyKind.ZombieMage : EnemyKind.Zombie));
                    if (wave % 4 == 0) l.Add(game.Combat.Spawn(EnemyKind.Chandelure));
                    return l;
                }, "The trial ends here. Try again whenever you like.");
                yield return Speak(mirel, "Wave cleared. Say “continue” when you are ready for the next.", false);
                wave++;
                ClearFlags();
                game.Hud.SetHints("continue");
                yield return WaitFlag(() => beginRequested, "Say: continue.", 40f);
                game.Hud.SetHints();
            }
        }

        // --- rune shards (optional quest) ---------------------------------------------------------------------------

        void SpawnShards()
        {
            var head = game.Hands.Head;
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            float[] angles = { 80f, -100f, 170f };
            float[] heights = { -0.25f, 0.15f, -0.45f };
            string[] spells = { "frostshard", "stonespike", "thunder" };
            string[] names = { "ice", "earth", "lightning" };
            for (int i = 0; i < 3; i++)
            {
                Vector3 fallback = head.position + Quaternion.AngleAxis(angles[i], Vector3.up) * flat * 1.7f + Vector3.up * heights[i];
                Vector3 p = game.Spatial.HasRoom ? game.Spatial.FindSurfacePoint(fallback) : fallback;
                int idx = i;
                var s = ShardPickup.Create(game, p, spells[i], names[i]);
                s.Collected += OnShardCollected;
                shardObjs.Add(s);
            }
            game.Voice.Register("where_shard", new[] { "where is the shard", "where are the shards", "i cannot find the shard", "give me a hint" },
                "gives a direction to the nearest shard", _ => StartCoroutine(ShardHint()), "Quests", () => shardObjs.Exists(s => s != null));
        }

        IEnumerator ShardHint()
        {
            ShardPickup best = null; float bd = float.MaxValue;
            foreach (var s in shardObjs) if (s != null) { float d = Vector3.Distance(s.transform.position, game.Hands.Head.position); if (d < bd) { bd = d; best = s; } }
            if (best == null) yield break;
            Vector3 to = best.transform.position - game.Hands.Head.position;
            float ang = Vector3.SignedAngle(Vector3.ProjectOnPlane(game.Hands.Head.forward, Vector3.up), Vector3.ProjectOnPlane(to, Vector3.up), Vector3.up);
            string dir = Mathf.Abs(ang) < 40f ? "straight ahead" : Mathf.Abs(ang) > 140f ? "behind you" : ang > 0 ? "to your right" : "to your left";
            string h = to.y > 0.25f ? ", up high" : to.y < -0.35f ? ", down low" : "";
            yield return Speak(mirel, "The nearest shard hums " + dir + h + ", about " + bd.ToString("0.0") + " metres away.", false);
        }

        void OnShardCollected(ShardPickup s)
        {
            shardObjs.Remove(s);
            shardsFound++;
            SaveSystem.Discover(s.SpellId);
            SaveSystem.Data.Shards.Add(s.SpellId);
            game.Quests.SetStep("echoes", shardsFound);
            var d = SpellBook.Find(s.SpellId);
            game.Hud.Toast("Shard awakened: " + d.Name + " discovered", Magic.Color(d.Primary), 3.5f);
            game.Say("The shard remembers " + d.Primary.ToString().ToLower() + " magic. Say: learn " + d.Primary.ToString().ToLower() + " magic.");
            AudioSynth.PlayAt("quest", s.transform.position, 1f);
            if (shardsFound >= 3)
            {
                game.Quests.Complete("echoes");
                StartCoroutine(Speak(mirel, "All three shards are awake. The old teachers would be proud. Try lightning with its spoken word: fulgur. And remember, two runes in one circle may surprise you."));
            }
        }
    }
}
