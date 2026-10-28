using System;
using System.Collections.Generic;
using UnityEngine;
using RuneMage.Voice;

namespace RuneMage
{
    /// <summary>
    /// Releases an armed seal. Spells are cast by a push gesture or by speaking the incantation; advanced spells (and combinations)
    /// need BOTH the drawn circle and the spoken word. If speech recognition is unavailable the incantation requirement is
    /// relaxed to a two-handed push so the game stays playable.
    /// </summary>
    public sealed class SpellCaster : MonoBehaviour
    {
        Game game;
        readonly Dictionary<string, float> nextReady = new Dictionary<string, float>();
        float lastIncantationTime = -10f;
        string lastIncantationSpell;
        float lastPushTime = -10f;

        public event Action<SpellDef, bool> Cast;                // spell, via voice
        public event Action<string> Fizzled;
        public event Action<SpellDef> Discovered;
        public event Action<SpellDef> Mastered;

        public void Init(Game g)
        {
            game = g;
            game.Hands.PushGesture += OnPush;
            game.Voice.Incantation += OnIncantation;
        }

        // --- triggers ------------------------------------------------------------------------------------------

        void OnPush()
        {
            lastPushTime = Time.time;
            var armed = game.Canvas.Armed;
            if (armed == null)
            {
                if (game.Canvas.RingPresent) Fizz("The seal is empty. Draw a rune inside it first.", false);
                return;      // pushing with no circle is just a gesture
            }
            if (armed.RequiresIncantation && game.Speech.IsAvailable)
            {
                // allow "word then push" within 4 seconds
                if (armed.Id == lastIncantationSpell && Time.time - lastIncantationTime < 4f) { TryCast(true); return; }
                Fizz("This circle needs its spoken word: say “" + armed.Incantation + "”.", true);
                return;
            }
            TryCast(false);
        }

        void OnIncantation(string heard, float confidence)
        {
            float c;
            var spoken = SpellBook.ByIncantation(heard, out c);
            var armed = game.Canvas.Armed;
            bool generic = IsGenericRelease(heard);

            if (spoken != null && c >= 0.7f)
            {
                lastIncantationTime = Time.time;
                lastIncantationSpell = spoken.Id;
                if (armed != null)
                {
                    // exact spell, or (for combos) any one of its two element words, counts
                    bool matches = spoken.Id == armed.Id ||
                                   (armed.IsCombo && (spoken.Primary == armed.Primary || spoken.Primary == armed.Secondary) && !spoken.IsCombo);
                    if (matches)
                    {
                        if (armed.IsCombo && spoken.Id != armed.Id)
                        {
                            // first word of a combination: wait for the second one
                            if (Time.time - comboWordTime > 6f) comboWords.Clear();
                            comboWords.Add(spoken.Primary);
                            comboWordTime = Time.time;
                            if (!(comboWords.Contains(armed.Primary) && comboWords.Contains(armed.Secondary)))
                            {
                                game.Say("One word spoken. The circle waits for the other.");
                                return;
                            }
                            comboWords.Clear();
                        }
                        TryCast(true);
                        return;
                    }
                    Fizz("That word belongs to " + spoken.Name + ", but your circle holds " + armed.Name + ".", true);
                    return;
                }
                if (game.Canvas.RingPresent) { Fizz("Your seal has no rune yet. Draw one inside the circle.", false); return; }
                // a spoken name with no circle starts guided drawing (see StoryDirector "cast <x>")
                game.RequestGuidedCast(spoken);
                return;
            }

            if (generic && armed != null && !armed.RequiresIncantation)
            {
                TryCast(true);
            }
        }

        readonly HashSet<MagicType> comboWords = new HashSet<MagicType>();
        float comboWordTime;

        static bool IsGenericRelease(string heard)
        {
            string[] words = { "cast", "release", "unleash", "go", "now", "fire away", "launch" };
            foreach (var w in words) if (TextMatch.Similarity(heard, w) >= 0.85f) return true;
            return false;
        }

        // --- casting -------------------------------------------------------------------------------------------

        void Fizz(string why, bool speak)
        {
            Fizzled?.Invoke(why);
            game.Say(why, speak);
            AudioSynth.PlayAt("error", game.Hands.Head.position + game.Hands.Head.forward * 0.6f, 0.5f);
        }

        public bool CanUse(SpellDef s)
        {
            if (!s.Hidden) return SaveSystem.IsLearned(s.Id);
            // hidden combinations only need their ingredient spells
            return SaveSystem.IsLearned(SpellBook.All.Find(x => !x.IsCombo && x.Primary == s.Primary).Id) &&
                   SaveSystem.IsLearned(SpellBook.All.Find(x => !x.IsCombo && x.Primary == s.Secondary).Id);
        }

        void TryCast(bool byVoice)
        {
            if (!game.Canvas.TryRelease(out var rel)) return;
            var s = rel.Spell;

            if (!CanUse(s))
            {
                Fizz(s.Hidden ? "You have not learned the elements this combines." : "You have not learned " + s.Name + " yet. Say “learn " + s.Primary.ToString().ToLower() + " magic”.", true);
                return;
            }
            if (nextReady.TryGetValue(s.Id, out var t) && Time.time < t)
            {
                Fizz(s.Name + " is still recharging.", false);
                return;
            }
            if (!game.Ward.Spend(s.ManaCost))
            {
                Fizz("Not enough mana. Breathe: it returns in a moment.", true);
                return;
            }
            nextReady[s.Id] = Time.time + s.Cooldown;

            game.Canvas.FlashAndClear();
            SpellEffects.Perform(game, rel);
            if (s.Hidden && !SaveSystem.Data.Discovered.Contains(s.Id))
            {
                SaveSystem.Discover(s.Id);
                SaveSystem.Learn(s.Id);
                game.Say("You discovered a hidden spell: " + s.Name + "!");
                Discovered?.Invoke(s);
            }
            int n = SaveSystem.Data.AddCast(s.Id);
            if (n == s.MasteryCasts && !SaveSystem.Data.Mastered.Contains(s.Id))
            {
                SaveSystem.Data.Mastered.Add(s.Id);
                game.Say(s.Name + " mastered!");
                Mastered?.Invoke(s);
            }
            SaveSystem.Save();
            Cast?.Invoke(s, byVoice);
        }
    }
}
