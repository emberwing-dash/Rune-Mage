using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RuneMage
{
    public enum QuestState { Unavailable = 0, Offered = 1, Active = 2, Complete = 3 }

    public sealed class QuestDef
    {
        public string Id, Title, Summary, Reward;
        public bool Main;
        public string[] Steps;
    }

    /// <summary>Quest log: definitions in code, progress persisted in SaveData.</summary>
    public sealed class QuestSystem : MonoBehaviour
    {
        public static readonly List<QuestDef> Defs = new List<QuestDef>
        {
            new QuestDef { Id="first_ink", Main=true, Title="First Ink",
                Summary="Mirel the Lantern-Keeper will teach you to write magic in the air.",
                Steps=new[]{ "Learn fire magic", "Draw the seal ring", "Inscribe the fire rune", "Release the spell", "Defend against the first rift" },
                Reward="Fire magic, Water magic" },
            new QuestDef { Id="cinder_court", Main=true, Title="The Cinder Court",
                Summary="Burning walkers keep climbing out of the rifts. Fire only feeds them.",
                Steps=new[]{ "Learn water magic", "Defeat the Cinder Walkers" },
                Reward="Water magic, knowledge of enemy weaknesses" },
            new QuestDef { Id="ash_warlock", Main=true, Title="The Ash Warlock",
                Summary="A fire-caster stands beyond the rift. Shield yourself, then break it with wind or lightning.",
                Steps=new[]{ "Learn barrier magic", "Block an ember with an Aegis ward", "Defeat the Ash Warlock" },
                Reward="Barrier and Wind magic" },
            new QuestDef { Id="echoes", Main=false, Title="Echoes in the Walls",
                Summary="Three rune shards hum somewhere in your room. Each one remembers a forgotten element.",
                Steps=new[]{ "Find the first rune shard", "Find the second rune shard", "Find the third rune shard" },
                Reward="Ice, Earth and Lightning knowledge" },
            new QuestDef { Id="candle_queen", Main=true, Title="The Candle Queen",
                Summary="The rifts all lead to her. Break her flame shield, survive her volleys, and put her out.",
                Steps=new[]{ "Break the flame shield", "Survive Candlelight Nova", "Extinguish the Candle Queen" },
                Reward="The Emberbound Grimoire is complete" },
        };

        public event Action<QuestDef, QuestState> Changed;

        public static QuestDef Def(string id) => Defs.Find(d => d.Id == id);

        public void Init(Game g) { }

        QuestProgress Get(string id, bool create = true)
        {
            var p = SaveSystem.Data.Quests.Find(q => q.Id == id);
            if (p == null && create)
            {
                p = new QuestProgress { Id = id };
                SaveSystem.Data.Quests.Add(p);
            }
            return p;
        }

        public QuestState StateOf(string id) => (QuestState)(Get(id, false)?.State ?? 0);
        public int StepOf(string id) => Get(id, false)?.Step ?? 0;

        public void Offer(string id) { if (StateOf(id) == QuestState.Unavailable) Set(id, QuestState.Offered); }
        public void Accept(string id) { if (StateOf(id) != QuestState.Complete) Set(id, QuestState.Active); }
        public void Complete(string id) => Set(id, QuestState.Complete);

        public void SetStep(string id, int step)
        {
            var p = Get(id);
            if (step > p.Step) { p.Step = step; SaveSystem.Save(); Changed?.Invoke(Def(id), (QuestState)p.State); }
        }

        void Set(string id, QuestState s)
        {
            var p = Get(id);
            p.State = (int)s;
            SaveSystem.Save();
            Changed?.Invoke(Def(id), s);
        }

        public QuestDef CurrentMain()
        {
            foreach (var d in Defs)
                if (d.Main && StateOf(d.Id) == QuestState.Active) return d;
            return null;
        }

        public string CurrentObjective()
        {
            foreach (var d in Defs)
            {
                if (StateOf(d.Id) != QuestState.Active) continue;
                int step = Mathf.Clamp(StepOf(d.Id), 0, d.Steps.Length - 1);
                return d.Title + ": " + d.Steps[step];
            }
            return null;
        }

        /// <summary>Spoken/visual quest log.</summary>
        public string Describe(bool spoken)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var d in Defs)
            {
                var st = StateOf(d.Id);
                if (st != QuestState.Active && st != QuestState.Offered) continue;
                n++;
                int step = Mathf.Clamp(StepOf(d.Id), 0, d.Steps.Length - 1);
                sb.Append(d.Title);
                sb.Append(st == QuestState.Offered ? " (offered): " + d.Summary : ": " + d.Steps[step] + ".");
                sb.Append(spoken ? " " : "\n");
            }
            if (n == 0) sb.Append("You have no open quests. Say “continue” to move the story on.");
            return sb.ToString().Trim();
        }
    }
}
