using System.Collections.Generic;
using UnityEngine;

namespace RuneMage
{
    public enum MagicType { None, Fire, Water, Wind, Lightning, Earth, Ice, Barrier, Heal }

    public enum SpellKind { Projectile, Bolt, Burst, Spike, Ward, Heal }

    public static class Magic
    {
        public static Color Color(MagicType t)
        {
            switch (t)
            {
                case MagicType.Fire: return new Color(1f, 0.45f, 0.12f);
                case MagicType.Water: return new Color(0.25f, 0.6f, 1f);
                case MagicType.Wind: return new Color(0.55f, 1f, 0.8f);
                case MagicType.Lightning: return new Color(1f, 0.95f, 0.3f);
                case MagicType.Earth: return new Color(0.7f, 0.55f, 0.32f);
                case MagicType.Ice: return new Color(0.7f, 0.92f, 1f);
                case MagicType.Barrier: return new Color(0.75f, 0.5f, 1f);
                case MagicType.Heal: return new Color(0.5f, 1f, 0.55f);
                default: return new Color(0.8f, 0.85f, 1f);
            }
        }

        public static string RuneId(MagicType t) => t == MagicType.None ? null : t.ToString().ToLowerInvariant();

        public static MagicType FromRuneId(string id)
        {
            return System.Enum.TryParse(id, true, out MagicType t) ? t : MagicType.None;
        }
    }

    /// <summary>Static description of a spell. Learned/mastery state lives in SaveData.</summary>
    public sealed class SpellDef
    {
        public string Id;
        public string Name;
        public MagicType Primary;
        public MagicType Secondary = MagicType.None;   // set for hidden combination spells
        public SpellKind Kind;
        public float Damage;
        public float ManaCost;
        public float Cooldown;
        public string[] Incantations;                  // spoken words, first one is the canonical spelling
        public bool RequiresIncantation;               // advanced spell: ring + glyph + voice
        public bool Hidden;                            // discovered by experimentation
        public string Description;
        public string Hint;                            // one-line voice-over for the grimoire page
        public int MasteryCasts = 6;                   // successful casts to master

        public string Incantation => Incantations[0];
        public bool IsCombo => Secondary != MagicType.None;
    }

    public static class SpellBook
    {
        public static readonly List<SpellDef> All = new List<SpellDef>
        {
            new SpellDef { Id="fireball", Name="Ember Lance", Primary=MagicType.Fire, Kind=SpellKind.Projectile, Damage=30, ManaCost=18, Cooldown=0.8f,
                Incantations=new[]{"ignis","ignite","fire","fireball"},
                Description="A spear of living flame. Zombies of the Ember Court resist it, so learn another element soon.",
                Hint="Draw a seal ring, then an upward triangle." },
            new SpellDef { Id="aqualance", Name="Tide Spear", Primary=MagicType.Water, Kind=SpellKind.Projectile, Damage=26, ManaCost=18, Cooldown=0.8f,
                Incantations=new[]{"aqua","water","wave"},
                Description="Pressurised water. Ember Court creatures hiss and stagger.",
                Hint="Seal ring, then a downward triangle." },
            new SpellDef { Id="gale", Name="Gale Cutter", Primary=MagicType.Wind, Kind=SpellKind.Burst, Damage=20, ManaCost=16, Cooldown=1.0f,
                Incantations=new[]{"ventus","wind","gale"},
                Description="A blade of wind that shoves enemies back and snuffs small flames.",
                Hint="Seal ring, then two flowing waves." },
            new SpellDef { Id="thunder", Name="Thunder Verdict", Primary=MagicType.Lightning, Kind=SpellKind.Bolt, Damage=42, ManaCost=30, Cooldown=1.6f,
                Incantations=new[]{"fulgur","thunder","lightning"}, RequiresIncantation=true,
                Description="An advanced spell. The circle alone is not enough: the lightning needs its true name spoken.",
                Hint="Seal ring, then a lightning zig-zag, and speak the word." },
            new SpellDef { Id="stonespike", Name="Stone Spike", Primary=MagicType.Earth, Kind=SpellKind.Spike, Damage=32, ManaCost=22, Cooldown=1.2f,
                Incantations=new[]{"terra","earth","stone"},
                Description="Raises a pillar of rock beneath the target.",
                Hint="Seal ring, then a square." },
            new SpellDef { Id="frostshard", Name="Frost Shard", Primary=MagicType.Ice, Kind=SpellKind.Projectile, Damage=22, ManaCost=18, Cooldown=0.9f,
                Incantations=new[]{"glacies","ice","frost"},
                Description="A crystal that chills what it strikes. Cold bodies move slower.",
                Hint="Seal ring, then a six-armed snowflake." },
            new SpellDef { Id="aegis", Name="Aegis Ward", Primary=MagicType.Barrier, Kind=SpellKind.Ward, Damage=0, ManaCost=25, Cooldown=3f,
                Incantations=new[]{"murus","shield","ward","barrier"},
                Description="A shimmering wall that swallows hostile magic for a few seconds.",
                Hint="Seal ring, then a diamond." },
            new SpellDef { Id="mend", Name="Healing Light", Primary=MagicType.Heal, Kind=SpellKind.Heal, Damage=0, ManaCost=28, Cooldown=2.5f,
                Incantations=new[]{"sana","heal","mend"},
                Description="Warm light that restores your ward.",
                Hint="Seal ring, then a plus sign." },

            // Hidden combinations: two runes inside one ring.
            new SpellDef { Id="tempest", Name="Inferno Tempest", Primary=MagicType.Fire, Secondary=MagicType.Wind, Kind=SpellKind.Burst, Damage=48, ManaCost=38, Cooldown=2.2f,
                Incantations=new[]{"ignis ventus","tempest","firestorm"}, RequiresIncantation=true, Hidden=true,
                Description="Wind feeds flame. A roaring cone of fire that no single rune can make.",
                Hint="Ring, fire and wind together, speak both words." },
            new SpellDef { Id="stormsurge", Name="Storm Surge", Primary=MagicType.Water, Secondary=MagicType.Lightning, Kind=SpellKind.Bolt, Damage=56, ManaCost=42, Cooldown=2.4f,
                Incantations=new[]{"aqua fulgur","storm","surge"}, RequiresIncantation=true, Hidden=true,
                Description="Lightning conducted through water. Chains between nearby enemies.",
                Hint="Ring, water and lightning together, speak both words." },
            new SpellDef { Id="crystalaegis", Name="Crystal Aegis", Primary=MagicType.Ice, Secondary=MagicType.Barrier, Kind=SpellKind.Ward, Damage=10, ManaCost=34, Cooldown=4f,
                Incantations=new[]{"glacies murus","crystal","fortress"}, RequiresIncantation=true, Hidden=true,
                Description="A frozen shield that lasts longer and bites back at attackers.",
                Hint="Ring, ice and barrier together, speak both words." },
        };

        public static SpellDef Find(string id) => All.Find(s => s.Id == id);

        /// <summary>Resolve the glyph set found in a ring to a spell (order independent).</summary>
        public static SpellDef Resolve(IList<MagicType> glyphs)
        {
            if (glyphs == null || glyphs.Count == 0) return null;
            if (glyphs.Count == 1)
                return All.Find(s => !s.IsCombo && s.Primary == glyphs[0]);
            if (glyphs.Count == 2)
            {
                var a = glyphs[0]; var b = glyphs[1];
                return All.Find(s => s.IsCombo && ((s.Primary == a && s.Secondary == b) || (s.Primary == b && s.Secondary == a)));
            }
            return null;
        }

        public static SpellDef ByIncantation(string spoken, out float confidence)
        {
            confidence = 0f;
            if (string.IsNullOrWhiteSpace(spoken)) return null;
            spoken = Voice.TextMatch.Normalize(spoken);
            SpellDef best = null;
            // prefer longer (combo) incantations when both words are spoken
            foreach (var s in All)
                foreach (var word in s.Incantations)
                {
                    float c = Voice.TextMatch.Similarity(spoken, word);
                    if (word.Contains(" ")) c += 0.02f;
                    if (c > confidence) { confidence = c; best = s; }
                }
            return best;
        }
    }
}
