using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using RuneMage.Recognition;
using RuneMage.Voice;

namespace RuneMage.UI
{
    public enum MenuKind { Main, Achievements, MagicList }

    /// <summary>A selectable button on a menu. The Id decides what it does; Fill is the hold-to-select progress bar.</summary>
    public sealed class MenuButton : MonoBehaviour
    {
        public string Id;
        public Image Fill;
        public TextMeshProUGUI Label;
        [NonSerialized] public float Dwell;
        public RectTransform R => (RectTransform)transform;
    }

    /// <summary>One row of a paged list (an achievement or a spell). The texts are filled in from the save data at runtime.</summary>
    public sealed class MenuRow : MonoBehaviour
    {
        public TextMeshProUGUI Title, Desc, Extra, Mark;
        public Image Plate, Accent, Badge;
    }

    /// <summary>
    /// Controller for the three menu scenes (Main Menu, Achievements, Magic List). The whole layout is real objects stored in the
    /// scene under "MenuCanvas", so it shows in the Scene view and can be edited by hand; at runtime this only wires the buttons and
    /// fills in the numbers and texts. Every action is a spoken command, with hold-a-fingertip (or pinch) on a button as the hand-only way.
    /// </summary>
    public sealed class MenuScene : MonoBehaviour
    {
        public MenuKind Kind;
        public Transform CenterEye;
        public OVRHand LeftOvrHand, RightOvrHand;
        public OVRSkeleton LeftSkeleton, RightSkeleton;
        [Tooltip("Move the menu in front of the player when the scene starts. Turn off to keep exactly where you placed it.")]
        public bool RecenterOnStart = true;

        SpeechService speech; TtsService tts; VoiceRouter voice; HandInput hands;
        Transform root;
        readonly List<MenuButton> buttons = new List<MenuButton>();
        readonly List<GameObject> lines = new List<GameObject>();
        Transform pageTitle, pageHub, pageInv, pageList;
        TextMeshProUGUI micLabel, titleLabel; Image micDot;
        MenuRow[] rows;
        int page; bool showInventory, hub;
        float emberTimer, lockout;

        /// <summary>Set by the sub-scenes so "Back" lands on the hub (the page after Play) instead of the title page.</summary>
        public static bool OpenHub;

        const float Px = 0.0011f;
        const string CanvasName = "MenuCanvas";
        static readonly Color Orange = new Color(1f, 0.6f, 0.3f);
        static readonly Color Plate = new Color(0.07f, 0.05f, 0.08f, 0.97f);

        // --- lifecycle ----------------------------------------------------------------------------------------------------

        void Awake()
        {
            SaveSystem.Load();
            speech = gameObject.AddComponent<SpeechService>();
            tts = gameObject.AddComponent<TtsService>();
            voice = gameObject.AddComponent<VoiceRouter>();
            voice.Init(speech, tts);
            hands = gameObject.AddComponent<HandInput>();
            hands.Head = CenterEye;
            hands.Bind(LeftOvrHand, LeftSkeleton, RightOvrHand, RightSkeleton);
        }

        void Start()
        {
            hub = Kind == MenuKind.Main && OpenHub; OpenHub = false;
            if (transform.Find(CanvasName) == null) BuildAll();          // a scene saved without its UI still works
            Bind();
            if (RecenterOnStart) Recenter();
            RegisterCommands();
            Render();
            string intro = Kind == MenuKind.Main ? (hub ? "Say begin, magic list, achievements, inventory or back." : "Rune Mage. Say play or quit.")
                         : Kind == MenuKind.Achievements ? "Achievements. Say next page, previous page, or back."
                         : "Magic list. Say next page, show a spell by name, or back.";
            if (SaveSystem.Data.VoiceOverEnabled) tts.SayNow(intro);
        }

        void Bind()
        {
            root = transform.Find(CanvasName);
            buttons.Clear(); buttons.AddRange(root.GetComponentsInChildren<MenuButton>(true));
            pageTitle = root.Find("Page_Title"); pageHub = root.Find("Page_Hub"); pageInv = root.Find("Page_Inventory"); pageList = root.Find("Page_List");
            var mic = root.Find("Mic");
            if (mic != null) { micDot = mic.Find("Dot").GetComponent<Image>(); micLabel = mic.Find("Label").GetComponent<TextMeshProUGUI>(); }
            var t = pageTitle != null ? pageTitle.Find("GameTitle") : null;
            titleLabel = t != null ? t.GetComponent<TextMeshProUGUI>() : null;
            rows = pageList != null ? pageList.GetComponentsInChildren<MenuRow>(true) : new MenuRow[0];
        }

        Action ActionFor(string id)
        {
            switch (id)
            {
                case "play": return ShowHub;
                case "quit": return Quit;
                case "begin": return () => Go("Game");
                case "magic": return () => Go("MagicList");
                case "ach": return () => Go("Achievements");
                case "inv": return () => { showInventory = true; Render(); };
                case "hubback": return () => { hub = false; Render(); };
                case "invback": return () => { showInventory = false; Render(); };
                case "back": return GoHub;
                case "next": return () => Turn(1);
                default: return null;
            }
        }

        // --- building the layout (editor time; saved into the scene) --------------------------------------------------------------

        /// <summary>Creates every page as real objects. Run from the editor; delete the "MenuCanvas" child and use the context menu to start over.</summary>
        [ContextMenu("Rebuild UI (replaces any hand edits)")]
        public void BuildAll()
        {
            var old = transform.Find(CanvasName);
            if (old != null) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }

            var go = new GameObject(CanvasName, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(1100, 700);
            rt.localScale = Vector3.one * Px;
            go.transform.position = new Vector3(0f, 1.45f, 1.7f);          // roughly eye height, in front of the rig; moved in front of the player at runtime
            root = go.transform;

            // Crimson-Valor style: a dark plate with a double gold frame, so the menu reads against any real room.
            var frame = UiKit.Rect(root, "Frame", Vector2.zero, new Vector2(1116, 716));
            P(frame, "Outer", Vector2.zero, new Vector2(1116, 716), new Color(0.78f, 0.58f, 0.2f, 0.95f));
            P(frame, "Gap", Vector2.zero, new Vector2(1104, 704), new Color(0.05f, 0.03f, 0.03f, 1f));
            P(frame, "Inner", Vector2.zero, new Vector2(1096, 696), new Color(0.55f, 0.38f, 0.14f, 0.9f));
            P(frame, "Background", Vector2.zero, new Vector2(1090, 690), new Color(0.045f, 0.03f, 0.05f, 0.93f));

            var mic = P(root, "Mic", new Vector2(420, 305), new Vector2(220, 50), UiKit.Glass);
            P(mic.transform, "Dot", new Vector2(-84, 0), new Vector2(18, 18), UiKit.Good, true);
            L(mic.transform, "Label", "Listening", new Vector2(14, 0), new Vector2(150, 34), 22, UiKit.Text);

            switch (Kind)
            {
                case MenuKind.Main: BuildMain(); break;
                case MenuKind.Achievements: BuildList("ACHIEVEMENTS", 5, 82, 92, 175, 960); break;
                default: BuildList("MAGIC LIST", 4, 102, 112, 160, 1000); break;
            }
            Bind();
            Render();
        }

        static Image P(Transform parent, string name, Vector2 pos, Vector2 size, Color c, bool circle = false)
        {
            var img = UiKit.Panel(parent, name, pos, size, c, null, circle);
            return img;
        }

        static TextMeshProUGUI L(Transform parent, string name, string text, Vector2 pos, Vector2 size, float fs, Color c,
            TextAlignmentOptions al = TextAlignmentOptions.Left, FontStyles st = FontStyles.Normal)
        {
            var t = UiKit.Label(parent, text, pos, size, fs, c, al, st);
            t.gameObject.name = name;
            return t;
        }

        Transform Page(string name)
        {
            var r = UiKit.Rect(root, name, Vector2.zero, new Vector2(1100, 700));
            return r;
        }

        void BuildMain()
        {
            // title page: the game name and two buttons, nothing else
            var tp = Page("Page_Title");
            var title = L(tp, "GameTitle", "RUNE MAGE", new Vector2(0, 175), new Vector2(1000, 150), 120, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            title.characterSpacing = 14f; title.enableVertexGradient = true;
            title.colorGradient = new VertexGradient(new Color(1f, 0.9f, 0.55f), new Color(1f, 0.9f, 0.55f), new Color(1f, 0.5f, 0.15f), new Color(1f, 0.5f, 0.15f));
            P(tp, "Rule", new Vector2(0, 100), new Vector2(560, 3), new Color(0.78f, 0.58f, 0.2f, 0.9f));
            L(tp, "Subtitle", "THE EMBERBOUND GRIMOIRE", new Vector2(0, 62), new Vector2(900, 40), 28, new Color(1f, 0.65f, 0.35f), TextAlignmentOptions.Center).characterSpacing = 10f;
            Btn(tp, "play", "Play", "play", new Vector2(0, -60), new Vector2(520, 100), UiKit.Gold, Icon.Wands, 48);
            Btn(tp, "quit", "Quit", "quit", new Vector2(0, -190), new Vector2(520, 100), new Color(0.85f, 0.35f, 0.3f), Icon.Power, 48);

            // hub (after Play): begin the adventure, plus everything else
            var hp = Page("Page_Hub");
            L(hp, "HubTitle", "RUNE MAGE", new Vector2(0, 290), new Vector2(700, 70), 54, UiKit.Gold, TextAlignmentOptions.Center, FontStyles.Bold).characterSpacing = 12f;
            Btn(hp, "begin", "Begin", "begin", new Vector2(0, 110), new Vector2(760, 110), UiKit.Gold, Icon.Wands, 52);
            Btn(hp, "magic", "Magic List", "magic list", new Vector2(-200, -50), new Vector2(380, 100), Orange, Icon.None, 32);
            Btn(hp, "ach", "Achievements", "achievements", new Vector2(200, -50), new Vector2(380, 100), Orange, Icon.None, 30);
            Btn(hp, "inv", "Inventory", "inventory", new Vector2(-200, -170), new Vector2(380, 100), Orange, Icon.None, 32);
            Btn(hp, "hubback", "Back", "back", new Vector2(200, -170), new Vector2(380, 100), new Color(0.75f, 0.75f, 0.85f), Icon.None, 32);

            // inventory
            var ip = Page("Page_Inventory");
            L(ip, "Title", "INVENTORY", new Vector2(-150, 300), new Vector2(700, 70), 54, UiKit.Gold, TextAlignmentOptions.Left, FontStyles.Bold);
            L(ip, "Sub", "Potions and drops from defeated monsters", new Vector2(-150, 255), new Vector2(760, 36), 24, UiKit.Dim);
            var body = L(ip, "Body", "", new Vector2(0, -20), new Vector2(940, 460), 28, UiKit.Text, TextAlignmentOptions.TopLeft);
            body.richText = true; body.enableAutoSizing = true; body.fontSizeMin = 18; body.fontSizeMax = 28;
            Btn(ip, "invback", "Back", "back", new Vector2(0, -290), new Vector2(420, 64), UiKit.Dim, Icon.None, 30);
        }

        void BuildList(string title, int count, float rowH, float step, float firstY, float width)
        {
            var lp = Page("Page_List");
            L(lp, "Title", title, new Vector2(-150, 300), new Vector2(700, 70), 54, UiKit.Gold, TextAlignmentOptions.Left, FontStyles.Bold);
            L(lp, "Sub", "", new Vector2(-150, 255), new Vector2(760, 36), 24, UiKit.Dim);
            for (int i = 0; i < count; i++)
            {
                var row = P(lp, "Row" + i, new Vector2(0, firstY - i * step), new Vector2(width, rowH), UiKit.GlassLight);
                var mr = row.gameObject.AddComponent<MenuRow>();
                mr.Plate = row;
                if (Kind == MenuKind.Achievements)
                {
                    mr.Badge = P(row.transform, "Badge", new Vector2(-430, 0), new Vector2(46, 46), UiKit.Dim, true);
                    mr.Mark = L(row.transform, "Mark", "?", new Vector2(-430, 0), new Vector2(46, 46), 30, new Color(0.05f, 0.08f, 0.05f), TextAlignmentOptions.Center, FontStyles.Bold);
                    mr.Title = L(row.transform, "Title", "", new Vector2(40, 16), new Vector2(760, 36), 30, UiKit.Gold, TextAlignmentOptions.Left, FontStyles.Bold);
                    mr.Desc = L(row.transform, "Desc", "", new Vector2(40, -17), new Vector2(760, 30), 22, UiKit.Text);
                }
                else
                {
                    mr.Accent = P(row.transform, "Accent", new Vector2(-488, 0), new Vector2(7, 80), UiKit.Dim);
                    mr.Title = L(row.transform, "Title", "", new Vector2(90, 24), new Vector2(560, 38), 32, UiKit.Dim, TextAlignmentOptions.Left, FontStyles.Bold);
                    mr.Desc = L(row.transform, "Desc", "", new Vector2(90, -8), new Vector2(760, 28), 21, UiKit.Text);
                    mr.Extra = L(row.transform, "Extra", "", new Vector2(90, -34), new Vector2(850, 26), 18, UiKit.Dim);
                }
            }
            Btn(lp, "back", "Back", "back", new Vector2(-250, -300), new Vector2(380, 56), UiKit.Dim, Icon.None, 30);
            Btn(lp, "next", "Next page", "next page", new Vector2(250, -300), new Vector2(380, 56), UiKit.Cyan, Icon.None, 30);
        }

        enum Icon { None, Wands, Power }

        /// <summary>Framed button in the Crimson Valor mould: gold border, dark plate, optional icon, big label.</summary>
        void Btn(Transform parent, string id, string label, string say, Vector2 pos, Vector2 size, Color acc, Icon icon, float fontSize)
        {
            var border = P(parent, "Button_" + id, pos, size + new Vector2(8, 8), new Color(acc.r * 0.8f, acc.g * 0.8f, acc.b * 0.8f, 0.95f));
            var plate = P(border.transform, "Plate", Vector2.zero, size, Plate);
            var fill = P(plate.transform, "HoldFill", new Vector2(-size.x / 2f, 0), new Vector2(0, size.y), new Color(acc.r, acc.g, acc.b, 0.3f));
            fill.rectTransform.pivot = new Vector2(0, 0.5f);
            bool sub = !string.IsNullOrEmpty(say) && size.y >= 72;
            float tx = icon == Icon.None ? 0 : 38;
            var lab = L(plate.transform, "Label", label.ToUpperInvariant(), new Vector2(tx, sub ? 12 : 0), new Vector2(size.x - 40 - tx, size.y * 0.6f), fontSize, UiKit.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            lab.characterSpacing = 6f;
            if (sub) L(plate.transform, "Hint", "say “" + say + "”", new Vector2(tx, -size.y * 0.27f), new Vector2(size.x - 40 - tx, 28), 20, new Color(acc.r, acc.g, acc.b, 0.85f), TextAlignmentOptions.Center);
            if (icon != Icon.None) DrawIcon(plate.transform, icon, new Vector2(-size.x / 2f + 62, 0), acc);
            var mb = border.gameObject.AddComponent<MenuButton>();
            mb.Id = id; mb.Fill = fill; mb.Label = lab;
        }

        void DrawIcon(Transform parent, Icon icon, Vector2 at, Color c)
        {
            var holder = UiKit.Rect(parent, "Icon", at, new Vector2(80, 80));
            if (icon == Icon.Wands)
            {
                foreach (float a in new[] { 40f, -40f })
                {
                    var blade = P(holder, "Wand", Vector2.zero, new Vector2(9, 74), c);
                    blade.rectTransform.localRotation = Quaternion.Euler(0, 0, a);
                    var guard = P(holder, "Guard", Quaternion.Euler(0, 0, a) * new Vector3(0, -16, 0), new Vector2(26, 7), c);
                    guard.rectTransform.localRotation = Quaternion.Euler(0, 0, a);
                    P(holder, "Tip", Quaternion.Euler(0, 0, a) * new Vector3(0, 40, 0), new Vector2(14, 14), Color.Lerp(c, Color.white, 0.6f), true);
                }
            }
            else if (icon == Icon.Power)
            {
                P(holder, "RingOuter", Vector2.zero, new Vector2(54, 54), c, true);
                P(holder, "RingInner", Vector2.zero, new Vector2(40, 40), Plate, true);
                P(holder, "Gap", new Vector2(0, 26), new Vector2(22, 26), Plate);
                P(holder, "Bar", new Vector2(0, 8), new Vector2(8, 40), c);
            }
        }

        // --- filling in the live values -------------------------------------------------------------------------------------------

        void Render()
        {
            if (root == null) return;
            foreach (var l in lines) if (l != null) Destroy(l);
            lines.Clear();
            lockout = 0.7f;

            if (Kind == MenuKind.Main)
            {
                bool title = !hub && !showInventory;
                Show(pageTitle, title); Show(pageHub, hub && !showInventory); Show(pageInv, showInventory);
                RefreshHub(); RefreshInventory();            // also fills the hidden pages, so they look right the moment you switch them on in the editor
            }
            else if (Kind == MenuKind.Achievements) RefreshAchievements();
            else RefreshMagicList();
        }

        static void Show(Transform t, bool on) { if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on); }

        void RefreshHub()
        {
            var d = SaveSystem.Data;
            bool cont = d.TutorialDone || d.Chapter > 0;
            foreach (var b in buttons)
                if (b.Id == "begin") b.Label.text = (cont ? "Continue" : "Begin").ToUpperInvariant();
            var old = pageHub.Find("Stats");                       // an earlier layout had a progress line under the buttons
            if (old != null) old.gameObject.SetActive(false);
        }

        void RefreshInventory()
        {
            var d = SaveSystem.Data;
            var sb = new StringBuilder();
            if (d.InvIds.Count == 0) sb.Append("Nothing yet. Defeat monsters in the game to collect potions and keepsakes.");
            for (int i = 0; i < d.InvIds.Count; i++)
            {
                var def = ItemBook.Find(d.InvIds[i]); if (def == null) continue;
                sb.Append("<color=#" + ColorUtility.ToHtmlStringRGB(def.Color) + "><b>" + def.Name + "</b></color>  x" + d.InvCounts[i] + "\n<size=70%><color=#aab4d8>" + def.Description + "</color></size>\n");
            }
            pageInv.Find("Body").GetComponent<TextMeshProUGUI>().text = sb.ToString();
        }

        const int AchPerPage = 5;
        const int SpellsPerPage = 4;

        void RefreshAchievements()
        {
            int total = Achievements.All.Count;
            int pages = Mathf.CeilToInt(total / (float)AchPerPage);
            page = Mathf.Clamp(page, 0, pages - 1);
            pageList.Find("Sub").GetComponent<TextMeshProUGUI>().text = SaveSystem.Data.Unlocked.Count + " of " + total + " unlocked   •   page " + (page + 1) + "/" + pages;
            for (int i = 0; i < rows.Length; i++)
            {
                int idx = page * AchPerPage + i;
                var r = rows[i];
                r.gameObject.SetActive(idx < total);
                if (idx >= total) continue;
                var a = Achievements.All[idx];
                bool got = Achievements.IsUnlocked(a.Id);
                r.Plate.color = got ? new Color(0.14f, 0.2f, 0.16f, 0.9f) : UiKit.GlassLight;
                r.Badge.color = got ? UiKit.Good : new Color(0.3f, 0.32f, 0.42f);
                r.Mark.text = got ? "✓" : "?";
                r.Title.text = a.Title; r.Title.color = got ? UiKit.Gold : UiKit.Dim;
                string prog = a.Counter != null && !got ? "   (" + Mathf.Min(SaveSystem.Data.Counter(a.Counter), a.Target) + "/" + a.Target + ")" : "";
                r.Desc.text = a.Description + prog;
            }
        }

        void RefreshMagicList()
        {
            int total = SpellBook.All.Count;
            int pages = Mathf.CeilToInt(total / (float)SpellsPerPage);
            page = Mathf.Clamp(page, 0, pages - 1);
            pageList.Find("Sub").GetComponent<TextMeshProUGUI>().text = "Every spell and the rune that writes it   •   page " + (page + 1) + "/" + pages;
            for (int i = 0; i < rows.Length; i++)
            {
                int idx = page * SpellsPerPage + i;
                var r = rows[i];
                r.gameObject.SetActive(idx < total);
                if (idx >= total) continue;
                var s = SpellBook.All[idx];
                bool known = SaveSystem.IsDiscovered(s.Id);
                bool learned = SaveSystem.IsLearned(s.Id);
                bool mastered = SaveSystem.Data.Mastered.Contains(s.Id);
                Color col = SpellEffects.ElementColor(s);
                r.Accent.color = known ? col : new Color(0.3f, 0.32f, 0.42f);
                string state = !known ? "Undiscovered" : mastered ? "Mastered" : learned ? "Learned" : "Discovered: say “learn " + s.Primary.ToString().ToLower() + " magic” in the game";
                r.Title.text = known ? s.Name : (s.Hidden ? "Hidden spell" : "???"); r.Title.color = known ? col : UiKit.Dim;
                r.Desc.text = known ? (s.IsCombo ? "Combination: " + s.Primary + " + " + s.Secondary : s.Primary + " magic") + "   •   word: “" + s.Incantation + "”" : "Find rune shards or finish quests to discover it.";
                r.Extra.text = state + (known ? "   •   " + s.Hint : ""); r.Extra.color = learned ? UiKit.Good : UiKit.Dim;
                if (known && Application.isPlaying) DrawEmblem((RectTransform)r.transform, s, col);
            }
        }

        /// <summary>Little ring + rune diagram, drawn from thin UI segments (so it scales and sorts with the rest of the canvas).</summary>
        void DrawEmblem(RectTransform row, SpellDef s, Color col)
        {
            Vector2 centre = new Vector2(-405, 0);
            const int seg = 40;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                lines.Add(UiKit.Segment(row, centre + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 38f, centre + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 38f, 4f, col).gameObject);
            }
            void Rune(string id, float xo, float size, Color c)
            {
                foreach (var poly in RuneShapes.Get(id))
                    for (int i = 0; i + 1 < poly.Length; i++)
                        lines.Add(UiKit.Segment(row,
                            centre + new Vector2(xo + (poly[i].X - 0.5f) * size, (poly[i].Y - 0.5f) * size),
                            centre + new Vector2(xo + (poly[i + 1].X - 0.5f) * size, (poly[i + 1].Y - 0.5f) * size), 3.5f, c).gameObject);
            }
            Rune(Magic.RuneId(s.Primary), s.IsCombo ? -14 : 0, s.IsCombo ? 30 : 46, col);
            if (s.IsCombo) Rune(Magic.RuneId(s.Secondary), 14, 30, Magic.Color(s.Secondary));
        }

        void Turn(int dir)
        {
            int count = Kind == MenuKind.Achievements ? Mathf.CeilToInt(Achievements.All.Count / (float)AchPerPage) : Mathf.CeilToInt(SpellBook.All.Count / (float)SpellsPerPage);
            page = (page + dir + count) % count;
            Render();
        }

        void Recenter()
        {
            if (CenterEye == null || root == null) return;
            Vector3 flat = Vector3.ProjectOnPlane(CenterEye.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            flat.Normalize();
            root.position = CenterEye.position + flat * 1.7f + Vector3.down * 0.05f;
            root.rotation = Quaternion.LookRotation(flat, Vector3.up);
        }

        // --- navigation + voice -------------------------------------------------------------------------------------------

        void ShowHub() { hub = true; showInventory = false; page = 0; Render(); }

        void Go(string scene)
        {
            SaveSystem.Save();
            SceneManager.LoadScene(scene);
        }

        void GoHub() { OpenHub = true; Go("MainMenu"); }

        void Quit()
        {
            SaveSystem.Save();
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        void RegisterCommands()
        {
            if (Kind == MenuKind.Main)
            {
                voice.Register("play", new[] { "play", "start", "start game", "play the game" }, "opens the adventure menu", _ => ShowHub(), "Menu", () => !hub);
                voice.Register("begin", new[] { "begin", "begin adventure", "continue", "start adventure", "enter", "play", "start" }, "starts or continues your adventure", _ => Go("Game"), "Menu", () => hub && !showInventory);
                voice.Register("magic", new[] { "magic list", "spells", "show spells", "spell list" }, "opens the magic list", _ => Go("MagicList"), "Menu", () => hub && !showInventory);
                voice.Register("ach", new[] { "achievements", "show achievements" }, "opens achievements", _ => Go("Achievements"), "Menu", () => hub && !showInventory);
                voice.Register("inv", new[] { "inventory", "show inventory", "my items", "open inventory" }, "shows potions and drops", _ => { showInventory = true; Render(); }, "Menu", () => hub && !showInventory);
                voice.Register("back_inv", new[] { "back", "close inventory" }, "returns to the menu", _ => { showInventory = false; Render(); }, "Menu", () => showInventory);
                voice.Register("back_hub", new[] { "back", "go back", "title" }, "returns to the title", _ => { hub = false; Render(); }, "Menu", () => hub && !showInventory);
                voice.Register("quit", new[] { "quit", "exit game", "quit game" }, "quits the game", _ => Quit(), "Menu", () => !hub);
            }
            else
            {
                voice.Register("back", new[] { "back", "main menu", "go back", "return" }, "returns to the menu", _ => GoHub());
                voice.Register("next", new[] { "next page", "next" }, "next page", _ => Turn(1));
                voice.Register("prev", new[] { "previous page", "previous", "last page" }, "previous page", _ => Turn(-1));
                if (Kind == MenuKind.MagicList)
                    voice.Register("show", new[] { "show {x}", "go to {x}", "open {x}" }, "jumps to a spell, e.g. “show fire”", x =>
                    {
                        var s = Grimoire.FindSpell(x); if (s == null) return;
                        page = SpellBook.All.IndexOf(s) / SpellsPerPage; Render();
                    });
            }
            voice.Register("recenter", new[] { "recenter", "move the menu", "bring it back" }, "moves the menu in front of you", _ => Recenter());
            voice.Register("help", new[] { "what can i say", "help", "voice commands" }, "lists what you can say", _ =>
            {
                tts.SayNow("You can say: " + string.Join(", ", voice.HelpLines(true).ConvertAll(l => l.Split('”')[0].Replace("“", ""))));
            }, "General", null, true);
        }

        // --- per frame ----------------------------------------------------------------------------------------------------

        void Update()
        {
            if (micLabel != null)
            {
                bool on = speech.Status == SpeechStatus.Listening;
                micDot.color = on ? (speech.HearingSpeech ? UiKit.Cyan : UiKit.Good) : UiKit.Bad;
                micLabel.text = on ? (speech.HearingSpeech ? "Hearing you..." : "Listening") : "Voice off: use hands";
            }
            if (titleLabel != null && titleLabel.gameObject.activeInHierarchy)
            {
                titleLabel.color = Color.Lerp(Color.white, new Color(1f, 0.78f, 0.6f), 0.5f + 0.5f * Mathf.Sin(Time.time * 1.6f));
                emberTimer -= Time.deltaTime;
                if (emberTimer <= 0f)
                {
                    emberTimer = 0.25f;
                    Vector3 p = root.TransformPoint(new Vector3(UnityEngine.Random.Range(-480f, 480f), UnityEngine.Random.Range(-300f, -150f), -30f));
                    Fx.Burst(p, new Color(1f, 0.55f, 0.15f), 2, 0.12f, 0.012f, 2.6f, -0.12f);
                }
            }
            lockout -= Time.deltaTime;
            // select: hold an index fingertip on a button, or pinch while over it
            var h = hands.Right.Tracked ? hands.Right : (hands.Left.Tracked ? hands.Left : null);
            if (h == null || hands.Scripted) return;
            var corners = new Vector3[4];
            foreach (var b in buttons)
            {
                if (!b.gameObject.activeInHierarchy) continue;
                b.R.GetWorldCorners(corners);
                Vector3 right = corners[3] - corners[0], up = corners[1] - corners[0];
                Vector3 n = Vector3.Cross(right, up).normalized;
                Vector3 rel = h.IndexTip - corners[0];
                bool over = Mathf.Abs(Vector3.Dot(rel, n)) < 0.07f && Vector3.Dot(rel, right.normalized) / right.magnitude is > 0f and < 1f && Vector3.Dot(rel, up.normalized) / up.magnitude is > 0f and < 1f;
                if (lockout > 0f) over = false;
                b.Dwell = over ? b.Dwell + Time.deltaTime : Mathf.Max(0, b.Dwell - Time.deltaTime * 2f);
                b.Fill.rectTransform.sizeDelta = new Vector2((b.R.sizeDelta.x - 8f) * Mathf.Clamp01(b.Dwell / 0.9f), b.R.sizeDelta.y - 8f);
                if (b.Dwell >= 0.9f || (over && h.Pinching)) { b.Dwell = 0; ActionFor(b.Id)?.Invoke(); return; }
            }
        }
    }
}
