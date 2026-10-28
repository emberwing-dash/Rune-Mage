using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuneMage.Recognition;
using RuneMage.Voice;

namespace RuneMage.UI
{
    /// <summary>
    /// The Emberbound Grimoire: a floating book summoned by holding the left palm up (or "open my grimoire"), dismissed by turning
    /// the palm down (or "close the grimoire"), paged by a right-hand swipe (or "next page"). Pages teach each spell with its rune
    /// diagram, spoken word, how to draw it, and a mastery bar. Nothing needs typing.
    /// </summary>
    public sealed class Grimoire : MonoBehaviour
    {
        sealed class Page { public string Kind; public SpellDef Spell; }

        Game game;
        Transform root;
        Canvas leftCanvas, rightCanvas;
        TextMeshProUGUI lTitle, lSub, lState, rBody, rHint, rWord, rMastery, rFoot;
        UiKit.Bar masteryBar;
        Image lAccent;
        readonly List<GameObject> emblem = new List<GameObject>();
        readonly List<Page> pages = new List<Page>();
        int index;
        public bool IsOpen { get; private set; }
        float openAmount;
        Vector3 smoothPos; Quaternion smoothRot;

        public event Action<SpellDef> SpellLearned;
        public event Action Opened, Closed;

        const float Px = 0.0007f;

        public void Init(Game g)
        {
            game = g;
            BuildBook();
            RegisterVoice();
            g.Hands.GrimoireSummon += () => Open();
            g.Hands.GrimoireDismiss += () => Close();
            g.Hands.SwipeGesture += dir => { if (IsOpen) Turn(dir > 0 ? 1 : -1); };
            root.gameObject.SetActive(false);
        }

        // --- build ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds (or re-finds) the book. It is saved in the scene under "Grimoire" so it can be seen and edited before Play; at runtime this
        /// only picks up the references. <paramref name="savedCover"/> is the leather material asset (the editor passes it in).
        /// </summary>
        public void BuildBook(Material savedCover = null)
        {
            UiKit.BeginBuild();
            root = UiKit.Child(transform, "Grimoire", out bool rootNew);

            // leather cover seen from underneath/behind the pages
            var coverT = root.Find("Cover");
            if (coverT == null)
            {
                var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cover.name = "Cover";
                if (Application.isPlaying) Destroy(cover.GetComponent<Collider>()); else DestroyImmediate(cover.GetComponent<Collider>());
                cover.transform.SetParent(root, false);
                cover.transform.localScale = new Vector3(0.64f, 0.44f, 0.012f);
                cover.transform.localPosition = new Vector3(0, 0, 0.03f);
                var cm = savedCover;
                if (cm == null)
                {
                    cm = Fx.Make(Fx.White, new Color(0.35f, 0.2f, 0.12f, 1f), false, 1f, 1, 1, 0, new Color(0.2f, 0.1f, 0.06f, 1f));
                    cm.SetFloat("_ZWrite", 1f); cm.renderQueue = 2000;
                }
                cover.GetComponent<MeshRenderer>().sharedMaterial = cm;
            }

            leftCanvas = MakePage("LeftPage", new Vector3(-0.15f, 0, 0));
            rightCanvas = MakePage("RightPage", new Vector3(0.15f, 0, 0));

            var lp = leftCanvas.transform;
            lAccent = UiKit.Panel(lp, "accent", new Vector2(0, 238), new Vector2(300, 8), UiKit.Gold);
            lTitle = UiKit.Label(lp, "", new Vector2(0, 195), new Vector2(360, 76), 40, UiKit.Gold, TextAlignmentOptions.Center, FontStyles.Bold, null, "Title");
            lSub = UiKit.Label(lp, "", new Vector2(0, 150), new Vector2(360, 36), 22, UiKit.Dim, TextAlignmentOptions.Center, FontStyles.Normal, null, "Subtitle");
            lState = UiKit.Label(lp, "", new Vector2(0, -215), new Vector2(360, 70), 24, UiKit.Cyan, TextAlignmentOptions.Center, FontStyles.Bold, null, "State");

            var rp = rightCanvas.transform;
            rBody = UiKit.Label(rp, "", new Vector2(0, 130), new Vector2(350, 240), 25, UiKit.Text, TextAlignmentOptions.TopLeft, FontStyles.Normal, null, "Body");
            rHint = UiKit.Label(rp, "", new Vector2(0, -60), new Vector2(350, 90), 22, UiKit.Cyan, TextAlignmentOptions.TopLeft, FontStyles.Normal, null, "Hint");
            rWord = UiKit.Label(rp, "", new Vector2(0, -135), new Vector2(350, 40), 28, UiKit.Gold, TextAlignmentOptions.Left, FontStyles.Bold, null, "Word");
            rMastery = UiKit.Label(rp, "", new Vector2(0, -180), new Vector2(350, 30), 20, UiKit.Dim, TextAlignmentOptions.Left, FontStyles.Normal, null, "Mastery");
            masteryBar = UiKit.MakeBar(rp, new Vector2(0, -208), 340, 18, UiKit.Gold);
            rFoot = UiKit.Label(rp, "", new Vector2(0, -250), new Vector2(350, 44), 19, UiKit.Dim, TextAlignmentOptions.Center, FontStyles.Normal, null, "Footer");
            UiKit.EndBuild();

            // a sample diagram saved for the editor preview is replaced by the live one
            var sample = leftCanvas.transform.Find("EmblemSample");
            if (sample != null) { if (Application.isPlaying) Destroy(sample.gameObject); else DestroyImmediate(sample.gameObject); }
            if (Application.isPlaying)
            {
                foreach (var t in new[] { lTitle, lSub, lState, rBody, rHint, rWord, rMastery, rFoot }) t.text = "";
            }
            else ApplySample(rootNew);
        }

        /// <summary>Editor time: a spell page as it looks when open, so the layout can be judged and edited in the Scene view.</summary>
        void ApplySample(bool place)
        {
            var s = SpellBook.All[0];
            Color c = SpellEffects.ElementColor(s);
            lAccent.color = c;
            lTitle.text = s.Name; lTitle.color = c; lTitle.fontSize = 36;
            lSub.text = s.Primary + " magic";
            lState.text = "LEARNED"; lState.color = UiKit.Good;
            rBody.text = s.Description;
            rHint.text = "How to draw: " + s.Hint;
            rWord.text = "Word: “" + s.Incantation + "”";
            rMastery.text = "Mastery  4 / " + s.MasteryCasts;
            masteryBar.Set(4f / s.MasteryCasts);
            rFoot.text = "2 / 12";
            var holder = UiKit.Rect(leftCanvas.transform, "EmblemSample", Vector2.zero, new Vector2(400, 560));
            DrawEmblem(s, holder, null);
            if (place) { root.position = new Vector3(0f, 1.15f, 0.6f); root.rotation = Quaternion.Euler(-18f, 0f, 0f); }
        }

        Canvas MakePage(string name, Vector3 localPos)
        {
            var t = UiKit.Child(root, name, out bool created);
            var c = UiKit.Ensure<Canvas>(t.gameObject);
            if (created)
            {
                t.localPosition = localPos;
                c.renderMode = RenderMode.WorldSpace;
                var rt = (RectTransform)t;
                rt.sizeDelta = new Vector2(400, 560);
                rt.localScale = Vector3.one * Px;
            }
            UiKit.Panel(t, "paper", Vector2.zero, new Vector2(400, 560), new Color(0.07f, 0.06f, 0.12f, 0.93f));
            return c;
        }

        // --- paging --------------------------------------------------------------------------------------------

        void RebuildPages()
        {
            pages.Clear();
            pages.Add(new Page { Kind = "intro" });
            foreach (var s in SpellBook.All)
            {
                if (s.Hidden && !SaveSystem.IsDiscovered(s.Id)) continue;
                pages.Add(new Page { Kind = "spell", Spell = s });
            }
            pages.Add(new Page { Kind = "quests" });
            if (SpellBook.All.Exists(s => s.Hidden && !SaveSystem.IsDiscovered(s.Id))) pages.Insert(pages.Count - 1, new Page { Kind = "hidden" });
            index = Mathf.Clamp(index, 0, pages.Count - 1);
        }

        public void Open(int? goTo = null)
        {
            RebuildPages();
            if (goTo.HasValue) index = Mathf.Clamp(goTo.Value, 0, pages.Count - 1);
            if (IsOpen) { Render(); return; }
            IsOpen = true;
            game.InkAllowed = false;
            root.gameObject.SetActive(true);
            openAmount = 0f;
            smoothPos = TargetPose(out smoothRot);
            Render();
            AudioSynth.PlayAt("quest", game.Hands.Head.position + game.Hands.Head.forward * 0.5f, 0.5f, 0.8f);
            Opened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            game.InkAllowed = true;
            root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        public void Turn(int dir)
        {
            if (!IsOpen) return;
            index = (index + dir + pages.Count) % pages.Count;
            AudioSynth.PlayAt("glyph", root.position, 0.4f, 0.8f);
            Render();
        }

        public bool GoToSpell(string text)
        {
            var s = FindSpell(text);
            if (s == null) return false;
            if (!IsOpen) Open();
            int i = pages.FindIndex(p => p.Spell == s);
            if (i < 0) return false;
            index = i;
            Render();
            return true;
        }

        public SpellDef CurrentSpell => IsOpen && index < pages.Count ? pages[index].Spell : null;

        static readonly Dictionary<string, MagicType> alias = new Dictionary<string, MagicType>
        {
            { "fire", MagicType.Fire }, { "flame", MagicType.Fire }, { "water", MagicType.Water }, { "wind", MagicType.Wind }, { "air", MagicType.Wind },
            { "lightning", MagicType.Lightning }, { "thunder", MagicType.Lightning }, { "earth", MagicType.Earth }, { "stone", MagicType.Earth },
            { "ice", MagicType.Ice }, { "frost", MagicType.Ice }, { "barrier", MagicType.Barrier }, { "shield", MagicType.Barrier }, { "ward", MagicType.Barrier },
            { "healing", MagicType.Heal }, { "heal", MagicType.Heal }, { "light", MagicType.Heal },
        };

        public static SpellDef FindSpell(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string n = TextMatch.Normalize(text);
            foreach (var s in SpellBook.All)
                if (TextMatch.Similarity(n, s.Name) >= 0.78f || TextMatch.Similarity(n, s.Id) >= 0.85f) return s;
            MagicType best = MagicType.None; float bs = 0.72f;
            foreach (var kv in alias)
            {
                float v = Mathf.Max(TextMatch.Similarity(n, kv.Key), TextMatch.ContainsWord(n, kv.Key, 0.86f) ? 0.9f : 0f);
                if (v > bs) { bs = v; best = kv.Value; }
            }
            if (best == MagicType.None) return null;
            return SpellBook.All.Find(s => !s.IsCombo && s.Primary == best);
        }

        // --- learning -------------------------------------------------------------------------------------------

        public bool TryLearn(string text)
        {
            var s = FindSpell(text) ?? CurrentSpell;
            if (s == null) { game.Say("Which magic? Say the element, like “learn water magic”."); return false; }
            if (SaveSystem.IsLearned(s.Id)) { game.Say("You already know " + s.Name + ". Draw its rune to cast it."); return false; }
            if (!SaveSystem.IsDiscovered(s.Id))
            {
                game.Say(s.Hidden ? "That spell is hidden. Try combining two runes in one circle." : "You have not discovered " + s.Primary.ToString().ToLower() + " magic yet. Keep exploring and finish quests.");
                return false;
            }
            SaveSystem.Learn(s.Id);
            game.Hud.Toast(s.Name + " learned", Magic.Color(s.Primary));
            game.Say(s.Name + " learned. Draw a seal, then " + s.Hint.ToLower().Replace("seal ring, then ", "") + " Say “" + s.Incantation + "” to speak its word.");
            AudioSynth.PlayAt("quest", game.Hands.Head.position + game.Hands.Head.forward * 0.5f, 0.8f);
            if (IsOpen) Render();
            SpellLearned?.Invoke(s);
            return true;
        }

        // --- render ---------------------------------------------------------------------------------------------

        void Render()
        {
            foreach (var l in emblem) if (l != null) Destroy(l);
            emblem.Clear();
            if (pages.Count == 0) return;
            var p = pages[index];
            string counter = (index + 1) + " / " + pages.Count;
            rMastery.text = ""; masteryBar.FillRect.parent.gameObject.SetActive(false); rWord.text = ""; rHint.text = ""; lState.text = "";

            switch (p.Kind)
            {
                case "intro":
                    lTitle.text = "The Emberbound Grimoire";
                    lTitle.fontSize = 30; lTitle.enableAutoSizing = true; lTitle.fontSizeMin = 22; lTitle.fontSizeMax = 40;
                    lSub.text = "Mirel's lantern-light remembers every rune";
                    rBody.text = "Magic is not spoken into being. It is written.\n\nDraw a seal ring in the air, inscribe a rune inside it, then push your palm or speak its word.";
                    rHint.text = "Turn pages: swipe your hand, or say “next page”.";
                    rFoot.text = counter + "   •   say “What can I say?”";
                    break;
                case "quests":
                    lTitle.text = "Quest Log"; lTitle.fontSize = 40;
                    lSub.text = "";
                    rBody.text = game.Quests.Describe(false);
                    rHint.text = "Say “show my quests” to hear them.";
                    rFoot.text = counter;
                    break;
                case "hidden":
                    lTitle.text = "???"; lTitle.fontSize = 40;
                    lSub.text = "Hidden spells";
                    rBody.text = "Some magic is not taught. Put two runes in one circle and see what answers.";
                    rFoot.text = counter;
                    break;
                default:
                    var s = p.Spell;
                    bool known = SaveSystem.IsDiscovered(s.Id);
                    bool learned = SaveSystem.IsLearned(s.Id);
                    Color c = SpellEffects.ElementColor(s);
                    lAccent.color = c;
                    lTitle.fontSize = 36;
                    lTitle.text = known ? s.Name : "Unknown spell";
                    lTitle.color = c;
                    lSub.text = known ? (s.IsCombo ? "Combination  " + s.Primary + " + " + s.Secondary : s.Primary + " magic" + (s.RequiresIncantation ? "  •  advanced" : "")) : "";
                    if (known)
                    {
                        DrawEmblem(s, leftCanvas.transform, emblem);
                        rBody.text = s.Description;
                        rHint.text = "How to draw: " + s.Hint;
                        rWord.text = "Word: “" + s.Incantation + "”";
                        int casts = SaveSystem.Data.CastCount(s.Id);
                        rMastery.text = SaveSystem.Data.Mastered.Contains(s.Id) ? "Mastered" : "Mastery  " + Mathf.Min(casts, s.MasteryCasts) + " / " + s.MasteryCasts;
                        masteryBar.FillRect.parent.gameObject.SetActive(true);
                        masteryBar.Set(Mathf.Min(casts, s.MasteryCasts) / (float)s.MasteryCasts);
                        lState.text = learned ? (SaveSystem.Data.Mastered.Contains(s.Id) ? "MASTERED" : "LEARNED") : "Say “learn " + s.Primary.ToString().ToLower() + " magic”";
                        lState.color = learned ? UiKit.Good : UiKit.Cyan;
                        rFoot.text = (learned ? "Say “practice” to trace it with a guide.   " : "") + counter;
                    }
                    else
                    {
                        rBody.text = "You have not found this knowledge yet. Finish quests or find rune shards hidden in your room.";
                        rFoot.text = counter;
                    }
                    break;
            }
        }

        /// <summary>Ring and rune diagram, drawn from thin UI segments in the page's pixel space.</summary>
        void DrawEmblem(SpellDef s, Transform parent, List<GameObject> track)
        {
            Color c = SpellEffects.ElementColor(s);
            Vector2 centre = new Vector2(0, -14);
            const int seg = 48; const float R = 150f;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
                var g = UiKit.Segment(parent, centre + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * R, centre + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * R, 7f, c).gameObject;
                if (track != null) track.Add(g);
            }
            DrawRune(Magic.RuneId(s.Primary), c, s.IsCombo ? -57f : 0f, s.IsCombo ? 72f : 128f, parent, track, centre);
            if (s.IsCombo) DrawRune(Magic.RuneId(s.Secondary), Magic.Color(s.Secondary), 57f, 72f, parent, track, centre);
        }

        void DrawRune(string id, Color c, float xOff, float size, Transform parent, List<GameObject> track, Vector2 centre)
        {
            foreach (var poly in RuneShapes.Get(id))
                for (int i = 0; i + 1 < poly.Length; i++)
                {
                    var g = UiKit.Segment(parent,
                        centre + new Vector2(xOff + (poly[i].X - 0.5f) * size, (poly[i].Y - 0.5f) * size),
                        centre + new Vector2(xOff + (poly[i + 1].X - 0.5f) * size, (poly[i + 1].Y - 0.5f) * size), 6f, c).gameObject;
                    if (track != null) track.Add(g);
                }
        }

        // --- placement ------------------------------------------------------------------------------------------

        Vector3 TargetPose(out Quaternion rot)
        {
            var head = game.Hands.Head;
            Vector3 pos;
            if (game.Hands.Left.Tracked && game.Hands.Left.PalmNormal.y > 0.3f)
                pos = game.Hands.Left.Palm + Vector3.up * 0.22f + head.forward * 0.05f;
            else
            {
                Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                pos = head.position + flat * 0.55f + Vector3.down * 0.22f;
            }
            Vector3 toHead = head.position - pos;
            // pages face the reader, tilted back like a book held in the hands
            rot = Quaternion.LookRotation(-toHead.normalized, Vector3.up) * Quaternion.Euler(-18f, 0, 0);
            return pos;
        }

        void Update()
        {
            if (!IsOpen) return;
            var pos = TargetPose(out var rot);
            smoothPos = Vector3.Lerp(smoothPos, pos, Time.deltaTime * 8f);
            smoothRot = Quaternion.Slerp(smoothRot, rot, Time.deltaTime * 8f);
            root.SetPositionAndRotation(smoothPos, smoothRot);
            openAmount = Mathf.MoveTowards(openAmount, 1f, Time.deltaTime * 4f);
            root.localScale = Vector3.one * Mathf.SmoothStep(0.3f, 1f, openAmount);
        }

        // --- voice -----------------------------------------------------------------------------------------------

        void RegisterVoice()
        {
            var v = game.Voice;
            v.Register("open_grimoire", new[] { "open my grimoire", "open the grimoire", "grimoire", "show my grimoire", "open spell book" },
                "opens your spell book", _ => { Open(); game.Say("Your grimoire. Swipe or say next page.", false); }, "Grimoire", () => !IsOpen);
            v.Register("close_grimoire", new[] { "close the grimoire", "close my grimoire", "close book", "close" },
                "closes the book", _ => Close(), "Grimoire", () => IsOpen);
            v.Register("next_page", new[] { "next page", "turn the page", "next" },
                "turns the page", _ => Turn(1), "Grimoire", () => IsOpen);
            v.Register("prev_page", new[] { "previous page", "go back", "last page", "back" },
                "turns back", _ => Turn(-1), "Grimoire", () => IsOpen);
            v.Register("goto_page", new[] { "go to {x} page", "show me {x} page", "open {x} page", "show {x} spell" },
                "jumps to a spell's page", x => { if (!GoToSpell(x)) game.Say("I cannot find a page for " + x + "."); }, "Grimoire");
            v.Register("learn", new[] { "learn {x} magic", "learn {x}", "teach me {x}", "study {x}" },
                "learns a discovered spell", x => TryLearn(x), "Grimoire");
            v.Register("show_quests", new[] { "show my quests", "my quests", "what are my quests", "quest log", "show quests" },
                "reads your quests aloud", _ =>
                {
                    string d = game.Quests.Describe(true);
                    game.Hud.ShowSubtitle("Quest log", game.Quests.Describe(false), 12f);
                    if (SaveSystem.Data.VoiceOverEnabled) game.Tts.SayNow(d);
                }, "Quests");
            v.Register("help", new[] { "what can i say", "help", "show commands", "voice commands" },
                "lists what you can say", _ =>
                {
                    game.Hud.ShowHelp(game.Voice.HelpLines(true), 20f);
                    game.Say("These are the things you can say right now.", true);
                }, "General", null, true);
        }
    }
}
