using System;
using System.Text;
using TMPro;
using UnityEngine;
using RuneMage.UI;

namespace RuneMage
{
    /// <summary>A loot orb dropped by a defeated monster. Drifts to the player (hands-free) or is collected by touching it.</summary>
    public sealed class ItemDrop : MonoBehaviour
    {
        public ItemDef Def; public int Count;
        Game game; float age; Vector3 vel;

        public static ItemDrop Create(Game g, ItemDef def, int count, Vector3 pos)
        {
            var go = new GameObject("Drop_" + def.Id);
            go.transform.position = pos;
            var d = go.AddComponent<ItemDrop>();
            d.game = g; d.Def = def; d.Count = count;
            d.vel = new Vector3(UnityEngine.Random.Range(-0.4f, 0.4f), UnityEngine.Random.Range(0.6f, 1.1f), UnityEngine.Random.Range(-0.4f, 0.4f));
            Color c = def.Color;
            float size = def.Rarity == Rarity.Rare ? 0.3f : 0.2f;
            Fx.Billboard(go.transform, "glow", Fx.Radial, new Color(c.r, c.g, c.b, 0.7f), size * 1.8f, 1.6f);
            Fx.Billboard(go.transform, "core", Fx.Radial, Color.Lerp(c, Color.white, 0.6f), size * 0.6f, 2.6f);
            if (def.Rarity != Rarity.Common) Fx.Billboard(go.transform, "ring", Fx.RingTex, c, size * 2.2f, 2f);
            Fx.PointLight(go.transform, c, 0.5f, 1.2f);
            return d;
        }

        void Update()
        {
            age += Time.deltaTime;
            var head = game.Hands.Head;
            if (head == null) return;
            Vector3 target = game.PlayerChest;
            if (age < 0.9f)
            {
                vel += Vector3.down * 2.2f * Time.deltaTime;                         // little pop, then settle
                transform.position += vel * Time.deltaTime;
                if (transform.position.y < game.Spatial.FloorY + 0.25f) { transform.position = new Vector3(transform.position.x, game.Spatial.FloorY + 0.25f, transform.position.z); vel.y = 0; }
            }
            else
            {
                float pull = Mathf.Lerp(0.6f, 3.5f, Mathf.Clamp01((age - 0.9f) / 3f));   // accelerates toward you: nothing to bend down for
                transform.position = Vector3.MoveTowards(transform.position, target, pull * Time.deltaTime);
            }
            bool touched = false;
            foreach (var h in new[] { game.Hands.Right, game.Hands.Left })
                if (h.Tracked && Vector3.Distance(h.IndexTip, transform.position) < 0.15f) touched = true;
            if (touched || Vector3.Distance(transform.position, target) < 0.3f)
            {
                game.Inventory.Collect(Def, Count, transform.position);
                Destroy(gameObject);
            }
        }
    }

    /// <summary>Items from monster drops, potion use by voice, and the floating inventory panel.</summary>
    public sealed class Inventory : MonoBehaviour
    {
        Game game;
        Transform panelRoot; TextMeshProUGUI listText;
        bool open; float lastUse = -5f;
        Vector3 pos; Quaternion rot; bool placed;

        public event Action<ItemDef, int> Collected;
        public event Action<ItemDef> Used;

        public void Init(Game g)
        {
            game = g;
            game.Combat.EnemyDied += OnEnemyDied;
            BuildPanel();
            var v = game.Voice;
            v.Register("open_inventory", new[] { "open inventory", "show inventory", "open my inventory", "inventory", "show my potions", "my items", "open my bag" },
                "shows your potions and monster drops", _ => Toggle(true), "Inventory", () => !open);
            v.Register("close_inventory", new[] { "close inventory", "close my inventory", "close the bag", "hide inventory" }, "closes the inventory", _ => Toggle(false), "Inventory", () => open);
            v.Register("use_item", new[] { "drink {x}", "use {x}", "drink a {x}", "use the {x}", "quaff {x}" },
                "drinks a potion, e.g. “drink health potion”", x => Use(x), "Inventory");
        }

        // --- drops ---------------------------------------------------------------------------------------------

        void OnEnemyDied(Enemy e)
        {
            SaveSystem.Data.AddCounter("kills");
            SaveSystem.Data.AddCounter("kills_" + e.Def.Kind);
            foreach (var rule in ItemBook.Drops(e.Def.Kind))
            {
                if (UnityEngine.Random.value > rule.Chance) continue;
                var def = ItemBook.Find(rule.ItemId);
                if (def == null) continue;
                int n = UnityEngine.Random.Range(rule.Min, rule.Max + 1);
                ItemDrop.Create(game, def, n, e.Center);
            }
            SaveSystem.Save();
        }

        public void Collect(ItemDef def, int n, Vector3 at)
        {
            SaveSystem.Data.AddItem(def.Id, n);
            SaveSystem.Data.AddCounter("loot", n);
            SaveSystem.Save();
            AudioSynth.PlayAt("glyph", at, 0.7f, def.Rarity == Rarity.Rare ? 1.5f : 1.1f);
            Fx.Burst(at, def.Color, 14, 1.1f, 0.03f, 0.5f);
            game.Hud.Toast("+" + n + " " + def.Name, def.Color, 1.8f);
            Collected?.Invoke(def, n);
            if (open) Refresh();
        }

        // --- use ------------------------------------------------------------------------------------------------

        public void Use(string spoken)
        {
            var def = ItemBook.FromSpeech(spoken);
            if (def == null) { game.Say("I do not know that item. Try “drink health potion” or “drink mana potion”."); return; }
            Use(def);
        }

        public bool Use(ItemDef def)
        {
            if (Time.time - lastUse < 1.5f) { game.Say("Wait a moment between potions."); return false; }
            if (SaveSystem.Data.ItemCount(def.Id) <= 0) { game.Say("You have no " + def.Name + "."); return false; }
            if (!def.Usable) { game.Say(def.Name + " cannot be drunk."); return false; }
            bool needsWard = def.Kind == ItemKind.HealWard || def.Kind == ItemKind.Elixir;
            bool needsMana = def.Kind == ItemKind.RestoreMana || def.Kind == ItemKind.Elixir;
            if (needsWard && !needsMana && game.Ward.Ward >= game.Ward.MaxWard - 1f) { game.Say("Your ward is already full."); return false; }
            if (needsMana && !needsWard && game.Ward.Mana >= game.Ward.MaxMana - 1f) { game.Say("Your mana is already full."); return false; }

            SaveSystem.Data.TakeItem(def.Id);
            if (needsWard) game.Ward.Heal(def.Amount);
            if (needsMana) game.Ward.RestoreMana(def.Amount);
            lastUse = Time.time;
            SaveSystem.Data.AddCounter("potions");
            SaveSystem.Save();
            AudioSynth.PlayAt("cast_heal", game.Hands.Head.position + Vector3.down * 0.3f, 0.7f);
            Fx.Burst(game.Hands.Head.position + Vector3.down * 0.4f, def.Color, 18, 0.7f, 0.04f, 1f, -0.2f);
            game.Say("You drink the " + def.Name + ".", false);
            Used?.Invoke(def);
            if (open) Refresh();
            return true;
        }

        // --- panel ------------------------------------------------------------------------------------------------

        /// <summary>Builds (or re-finds) the panel. It is saved in the scene under "InventoryPanel" so it can be seen and edited before Play.</summary>
        public void BuildPanel()
        {
            UiKit.BeginBuild();
            var t = UiKit.Child(transform, "InventoryPanel", out bool created);
            var go = t.gameObject;
            var c = UiKit.Ensure<Canvas>(go);
            if (created)
            {
                c.renderMode = RenderMode.WorldSpace; c.sortingOrder = 40;
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(700, 560);
                rt.localScale = Vector3.one * 0.0009f;
                go.transform.position = new Vector3(-0.95f, 1.4f, 1.2f);       // beside the HUD in the editor; at runtime it opens beside you
            }
            UiKit.Panel(go.transform, "bg", Vector2.zero, new Vector2(700, 560), UiKit.Glass);
            UiKit.Label(go.transform, "INVENTORY", new Vector2(0, 245), new Vector2(660, 50), 36, UiKit.Gold, TextAlignmentOptions.Center, FontStyles.Bold, null, "Title");
            listText = UiKit.Label(go.transform, "", new Vector2(0, -20), new Vector2(640, 440), 26, UiKit.Text, TextAlignmentOptions.TopLeft, FontStyles.Normal, null, "List");
            listText.richText = true;
            listText.enableAutoSizing = true; listText.fontSizeMin = 18; listText.fontSizeMax = 26;
            UiKit.Label(go.transform, "say “drink health potion” • “close inventory”", new Vector2(0, -255), new Vector2(660, 36), 22, UiKit.Cyan, TextAlignmentOptions.Center, FontStyles.Normal, null, "Hint");
            UiKit.EndBuild();
            panelRoot = go.transform;
            if (Application.isPlaying) panelRoot.gameObject.SetActive(false);
            else listText.text = "<color=#" + ColorUtility.ToHtmlStringRGB(new Color(0.55f, 1f, 0.6f)) + "><b>Ember Salve</b></color>  x2\n<size=70%><color=#aab4d8>Restores 40 ward.</color></size>\n" +
                                 "<color=#" + ColorUtility.ToHtmlStringRGB(new Color(0.5f, 0.75f, 1f)) + "><b>Moonwater Phial</b></color>  x1\n<size=70%><color=#aab4d8>Restores 50 mana.</color></size>";
        }

        public bool IsOpen => open;

        public void Toggle(bool on)
        {
            open = on;
            panelRoot.gameObject.SetActive(on);
            placed = false;
            if (on) Refresh();
        }

        public void Refresh()
        {
            var sb = new StringBuilder();
            var d = SaveSystem.Data;
            if (d.InvIds.Count == 0) sb.Append("Empty. Defeat monsters to collect potions and keepsakes.");
            for (int i = 0; i < d.InvIds.Count; i++)
            {
                var def = ItemBook.Find(d.InvIds[i]);
                if (def == null) continue;
                string col = ColorUtility.ToHtmlStringRGB(def.Color);
                sb.Append("<color=#" + col + "><b>" + def.Name + "</b></color>  x" + d.InvCounts[i] + "\n");
                sb.Append("<size=70%><color=#aab4d8>" + def.Description + "</color></size>\n");
            }
            listText.text = sb.ToString();
        }

        void Update()
        {
            if (!open || game.Hands.Head == null) return;
            var head = game.Hands.Head;
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            Vector3 target = head.position + flat * 1.0f + Vector3.up * -0.1f + Vector3.Cross(Vector3.up, flat) * -0.45f;
            Quaternion r = Quaternion.LookRotation(target - head.position, Vector3.up);
            if (!placed) { pos = target; rot = r; placed = true; }
            pos = Vector3.Lerp(pos, target, Time.deltaTime * 2f); rot = Quaternion.Slerp(rot, r, Time.deltaTime * 2f);
            panelRoot.SetPositionAndRotation(pos, rot);
        }
    }
}
