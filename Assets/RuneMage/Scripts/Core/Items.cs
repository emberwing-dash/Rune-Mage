using System.Collections.Generic;
using UnityEngine;

namespace RuneMage
{
    public enum ItemKind { HealWard, RestoreMana, Elixir, Material }
    public enum Rarity { Common, Uncommon, Rare }

    public sealed class ItemDef
    {
        public string Id, Name, Description;
        public ItemKind Kind;
        public float Amount;
        public Rarity Rarity;
        public string[] Aliases = new string[0];
        public bool Usable => Kind != ItemKind.Material;

        public Color Color
        {
            get
            {
                switch (Kind)
                {
                    case ItemKind.HealWard: return new Color(1f, 0.35f, 0.3f);
                    case ItemKind.RestoreMana: return new Color(0.35f, 0.65f, 1f);
                    case ItemKind.Elixir: return new Color(1f, 0.85f, 0.35f);
                    default: return new Color(0.8f, 0.75f, 0.65f);
                }
            }
        }
    }

    public sealed class DropRule { public string ItemId; public float Chance; public int Min = 1, Max = 1; }

    /// <summary>Item definitions and per-monster drop tables.</summary>
    public static class ItemBook
    {
        public static readonly List<ItemDef> All = new List<ItemDef>
        {
            new ItemDef { Id="ember_salve", Name="Ember Salve", Kind=ItemKind.HealWard, Amount=40, Rarity=Rarity.Common,
                Description="A warm balm rendered from cinder fat. Restores 40 ward.", Aliases=new[]{"health potion","healing potion","salve","ember salve","red potion"} },
            new ItemDef { Id="moonwater", Name="Moonwater Phial", Kind=ItemKind.RestoreMana, Amount=50, Rarity=Rarity.Common,
                Description="Cold, bright water in a glass phial. Restores 50 mana.", Aliases=new[]{"mana potion","moonwater","blue potion","phial"} },
            new ItemDef { Id="phoenix_elixir", Name="Phoenix Elixir", Kind=ItemKind.Elixir, Amount=60, Rarity=Rarity.Rare,
                Description="Distilled from a Candle Queen's last flame. Restores 60 ward and 60 mana.", Aliases=new[]{"elixir","phoenix elixir","gold potion"} },
            new ItemDef { Id="cinder_ash", Name="Cinder Ash", Kind=ItemKind.Material, Rarity=Rarity.Common,
                Description="Grey ash that never quite goes cold. Keepsake of the Ember Court." },
            new ItemDef { Id="warlock_sigil", Name="Warlock Sigil", Kind=ItemKind.Material, Rarity=Rarity.Uncommon,
                Description="A scorched sigil stone torn from an Ash Warlock's robe." },
            new ItemDef { Id="candle_wax", Name="Queen's Candle Wax", Kind=ItemKind.Material, Rarity=Rarity.Rare,
                Description="Wax from the Candle Queen. It hums when you hold it near a flame." },
        };

        public static ItemDef Find(string id) => All.Find(i => i.Id == id);

        public static List<DropRule> Drops(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Zombie:
                    return new List<DropRule> { new DropRule { ItemId = "cinder_ash", Chance = 0.6f }, new DropRule { ItemId = "ember_salve", Chance = 0.3f }, new DropRule { ItemId = "moonwater", Chance = 0.15f } };
                case EnemyKind.ZombieMage:
                    return new List<DropRule> { new DropRule { ItemId = "moonwater", Chance = 0.5f }, new DropRule { ItemId = "ember_salve", Chance = 0.25f }, new DropRule { ItemId = "warlock_sigil", Chance = 0.3f } };
                case EnemyKind.Wisp:
                    return new List<DropRule> { new DropRule { ItemId = "cinder_ash", Chance = 0.25f } };
                default: // Candle Queen
                    return new List<DropRule> { new DropRule { ItemId = "phoenix_elixir", Chance = 1f }, new DropRule { ItemId = "candle_wax", Chance = 1f },
                                                new DropRule { ItemId = "moonwater", Chance = 1f, Min = 2, Max = 3 }, new DropRule { ItemId = "ember_salve", Chance = 1f, Min = 1, Max = 2 } };
            }
        }

        /// <summary>Match a spoken phrase ("drink the health potion", "use mana potion") to a usable item.</summary>
        public static ItemDef FromSpeech(string text)
        {
            string n = Voice.TextMatch.Normalize(text);
            ItemDef best = null; float bs = 0.72f;
            foreach (var i in All)
            {
                if (!i.Usable) continue;
                foreach (var a in i.Aliases)
                {
                    float s = Mathf.Max(Voice.TextMatch.Similarity(n, a), Voice.TextMatch.ContainsWord(n, a.Split(' ')[0], 0.85f) && n.Contains(a.Split(' ')[a.Split(' ').Length - 1]) ? 0.9f : 0f);
                    if (s > bs) { bs = s; best = i; }
                }
            }
            if (best == null && (n.Contains("potion") || n.Contains("health") || n.Contains("heal"))) best = Find("ember_salve");
            if (best == null && n.Contains("mana")) best = Find("moonwater");
            return best;
        }
    }
}
