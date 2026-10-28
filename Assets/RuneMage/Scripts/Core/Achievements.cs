using System.Collections.Generic;
using UnityEngine;

namespace RuneMage
{
    public sealed class AchievementDef
    {
        public string Id, Title, Description;
        public int Target;                // for counter based achievements (0 = event based)
        public string Counter;
    }

    /// <summary>Achievement definitions (shared with the Achievements scene) and the in-game tracker.</summary>
    public sealed class Achievements : MonoBehaviour
    {
        public static readonly List<AchievementDef> All = new List<AchievementDef>
        {
            new AchievementDef { Id="apprentice",  Title="Apprentice of the Air", Description="Finish Mirel's first lesson." },
            new AchievementDef { Id="first_cast",  Title="First Spark", Description="Release your first spell." },
            new AchievementDef { Id="first_kill",  Title="Ember Slayer", Description="Defeat your first monster.", Counter="kills", Target=1 },
            new AchievementDef { Id="kills_25",    Title="Rift Sweeper", Description="Defeat 25 monsters.", Counter="kills", Target=25 },
            new AchievementDef { Id="kills_100",   Title="Court Breaker", Description="Defeat 100 monsters.", Counter="kills", Target=100 },
            new AchievementDef { Id="loot_10",     Title="Scavenger", Description="Collect 10 monster drops.", Counter="loot", Target=10 },
            new AchievementDef { Id="first_potion",Title="Apothecary", Description="Drink a potion.", Counter="potions", Target=1 },
            new AchievementDef { Id="four_spells", Title="Four Words", Description="Learn four spells.", Counter="learned", Target=4 },
            new AchievementDef { Id="all_spells",  Title="Grand Mage", Description="Learn all eight elemental spells.", Counter="learned", Target=8 },
            new AchievementDef { Id="mastery",     Title="Practised Hand", Description="Master a spell.", Counter="mastered", Target=1 },
            new AchievementDef { Id="hidden",      Title="Alchemist of Runes", Description="Discover a hidden combination spell." },
            new AchievementDef { Id="perfect",     Title="Perfect Seal", Description="Release a spell drawn at 95% quality or better." },
            new AchievementDef { Id="shards",      Title="Shard Hunter", Description="Awaken all three rune shards." },
            new AchievementDef { Id="warlock",     Title="Ash Breaker", Description="Defeat the Ash Warlock." },
            new AchievementDef { Id="queen",       Title="Candle Snuffer", Description="Extinguish the Candle Queen." },
        };

        public static AchievementDef Find(string id) => All.Find(a => a.Id == id);
        public static bool IsUnlocked(string id) => SaveSystem.Data.Unlocked.Contains(id);

        Game game;

        public void Init(Game g)
        {
            game = g;
            g.Caster.Cast += (s, v) => { Unlock("first_cast"); CheckCast(); };
            g.Caster.Discovered += s => Unlock("hidden");
            g.Caster.Mastered += s => { SaveSystem.Data.AddCounter("mastered"); Evaluate(); };
            g.Grimoire.SpellLearned += s => { SaveSystem.Data.CounterKeys.Remove("learned"); RecountLearned(); Evaluate(); };
            g.Combat.EnemyDied += e =>
            {
                if (e.Def.Kind == EnemyKind.ZombieMage && SaveSystem.Data.Counter("kills_ZombieMage") >= 1) Unlock("warlock");
                if (e.Def.Kind == EnemyKind.Chandelure) Unlock("queen");
                Evaluate();
            };
            g.Inventory.Collected += (d, n) => Evaluate();
            g.Inventory.Used += d => Evaluate();
            g.Quests.Changed += (q, st) =>
            {
                if (q.Id == "first_ink" && st == QuestState.Complete) Unlock("apprentice");
                if (q.Id == "echoes" && st == QuestState.Complete) Unlock("shards");
            };
            RecountLearned();
            Evaluate();
        }

        void RecountLearned()
        {
            int n = 0;
            foreach (var s in SpellBook.All) if (!s.Hidden && SaveSystem.IsLearned(s.Id)) n++;
            var d = SaveSystem.Data;
            int i = d.CounterKeys.IndexOf("learned");
            if (i < 0) { d.CounterKeys.Add("learned"); d.CounterValues.Add(n); } else d.CounterValues[i] = n;
        }

        void CheckCast()
        {
            // Quality of the cast seal is not stored on the event; the canvas reports it just before release
            if (game.Canvas.Quality >= 0.95f) Unlock("perfect");
        }

        public void Evaluate()
        {
            foreach (var a in All)
            {
                if (a.Counter == null || IsUnlocked(a.Id)) continue;
                if (SaveSystem.Data.Counter(a.Counter) >= a.Target) Unlock(a.Id);
            }
        }

        public void Unlock(string id)
        {
            if (IsUnlocked(id)) return;
            var def = Find(id);
            if (def == null) return;
            SaveSystem.Data.Unlocked.Add(id);
            SaveSystem.Save();
            game.Hud.Toast("Achievement: " + def.Title, new Color(1f, 0.85f, 0.4f), 3.5f);
            AudioSynth.PlayAt("quest", game.Hands.Head.position + game.Hands.Head.forward, 0.7f, 1.2f);
        }
    }
}
