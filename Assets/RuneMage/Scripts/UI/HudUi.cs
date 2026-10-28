using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuneMage.Voice;

namespace RuneMage.UI
{
    /// <summary>
    /// Head-following world-space HUD: vitals, subtitles, objective, voice hints, dialogue choices, spell status and help.
    /// Subtitles and cards are accessibility aids: every interaction also works by voice alone. As a motor/speech fallback,
    /// choice cards can be selected by holding a fingertip on them (dwell), never by a controller button.
    /// </summary>
    public sealed class HudUi : MonoBehaviour
    {
        const float Px = 0.0011f;               // metres per canvas pixel
        const float Distance = 1.55f;

        Game game;
        Canvas canvas;
        Transform root;

        // vitals
        UiKit.Bar wardBar, manaBar;
        // mic
        Image micDot; TextMeshProUGUI micLabel;
        // objective
        TextMeshProUGUI objectiveText; CanvasGroup objectiveGroup;
        // subtitles
        CanvasGroup subtitleGroup; TextMeshProUGUI speakerText, lineText; float subtitleHideAt;
        // hints
        CanvasGroup hintsGroup; readonly List<TextMeshProUGUI> hintTexts = new List<TextMeshProUGUI>();
        // choices
        CanvasGroup choicesGroup;
        sealed class Card { public RectTransform R; public TextMeshProUGUI Label, Say; public Image Fill; public float Dwell; public System.Action Pick; }
        readonly List<Card> cards = new List<Card>();
        // toast + title + help
        CanvasGroup toastGroup; TextMeshProUGUI toastText; float toastHideAt;
        CanvasGroup titleGroup; TextMeshProUGUI titleText, titleSub;
        CanvasGroup helpGroup; TextMeshProUGUI helpText; float helpHideAt;
        // spell status
        CanvasGroup spellGroup; TextMeshProUGUI spellTitle, spellLine; Image spellAccent;
        // trace
        CanvasGroup traceGroup; TextMeshProUGUI traceText;

        Vector3 smoothPos; Quaternion smoothRot; bool placed;

        public void Init(Game g)
        {
            game = g;
            BuildAll();
            game.Voice.ChoicesChanged += RefreshChoices;
            game.Voice.Feedback += line => ShowSubtitle("", line, 4f);
        }

        /// <summary>
        /// Builds (or, if the scene already has it, re-finds) every HUD object. The HUD is saved in the scene under "HUD" so it can be seen
        /// and edited before Play; at runtime this only hides the parts that are not in use yet and picks up the references.
        /// </summary>
        public void BuildAll()
        {
            UiKit.BeginBuild();
            cards.Clear(); hintTexts.Clear();
            var t = UiKit.Child(transform, "HUD", out bool created);
            var go = t.gameObject;
            canvas = UiKit.Ensure<Canvas>(go);
            if (created)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 50;
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(1100, 640);
                rt.localScale = Vector3.one * Px;
                go.transform.position = new Vector3(0f, 1.5f, Distance);       // in front of the rig; at runtime it follows your head
            }
            root = go.transform;

            BuildVitals();
            BuildObjective();
            BuildSubtitles();
            BuildHints();
            BuildChoices();
            BuildToast();
            BuildTitle();
            BuildHelp();
            BuildSpellStatus();
            BuildTrace();
            UiKit.EndBuild();
            if (Application.isPlaying) ClearSamples(); else ApplySamples();
        }

        /// <summary>Editor time: what each part looks like when it is showing, so the layout can be judged and edited in the Scene view.</summary>
        void ApplySamples()
        {
            wardBar.Set(0.8f); manaBar.Set(0.6f);
            micLabel.text = "Voice ready";
            objectiveText.text = "Draw a seal ring in the air";
            speakerText.text = "MIREL"; lineText.text = "Subtitles appear here while a character speaks.";
            string[] hints = { "cast fireball", "what can I say", "open grimoire" };
            for (int i = 0; i < hintTexts.Count; i++) hintTexts[i].text = "SAY  “" + hints[i] + "”";
            for (int i = 0; i < cards.Count; i++) { cards[i].Label.text = "Choice " + (i + 1); cards[i].Say.text = "say “" + (i + 1) + "”"; }
            toastText.text = "+1 Ember Salve";
            titleText.text = "CHAPTER ONE"; titleSub.text = "The Emberbound Grimoire";
            helpText.text = "say “cast fireball”\nsay “open my grimoire”\nsay “what can I say”";
            spellTitle.text = "Ember Lance"; spellLine.text = "Push your palm forward or say “ignis”  •  seal 92%";
            traceText.text = "Trace the circle  40%";
        }

        void ClearSamples()
        {
            foreach (var tx in new[] { objectiveText, speakerText, lineText, toastText, titleText, titleSub, helpText, spellTitle, spellLine, traceText })
                if (tx != null) tx.text = "";
            foreach (var h in hintTexts) h.text = "";
            foreach (var c in cards) { c.Label.text = ""; c.Say.text = ""; }
        }

        /// <summary>Alpha of a part that is hidden until used: hidden in play, shown (when <paramref name="preview"/>) in the editor.</summary>
        static void Hide(CanvasGroup g, bool preview = true) => g.alpha = !Application.isPlaying && preview ? 1f : 0f;

        // --- construction ---------------------------------------------------------------------------------------

        void BuildVitals()
        {
            var p = UiKit.Panel(root, "vitals", new Vector2(-440, 275), new Vector2(300, 84), UiKit.Glass);
            UiKit.Label(p.transform, "WARD", new Vector2(-98, 20), new Vector2(80, 28), 20, UiKit.Dim, TextAlignmentOptions.Left, FontStyles.Bold, null, "WardLabel");
            wardBar = UiKit.MakeBar(p.transform, new Vector2(30, 20), 180, 22, new Color(0.45f, 1f, 0.6f));
            UiKit.Label(p.transform, "MANA", new Vector2(-98, -20), new Vector2(80, 28), 20, UiKit.Dim, TextAlignmentOptions.Left, FontStyles.Bold, null, "ManaLabel");
            manaBar = UiKit.MakeBar(p.transform, new Vector2(30, -20), 180, 22, new Color(0.4f, 0.7f, 1f));

            var m = UiKit.Panel(root, "mic", new Vector2(440, 275), new Vector2(240, 56), UiKit.Glass);
            micDot = UiKit.Panel(m.transform, "dot", new Vector2(-92, 0), new Vector2(20, 20), UiKit.Good, null, true);
            micLabel = UiKit.Label(m.transform, "Voice ready", new Vector2(14, 0), new Vector2(170, 36), 24, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Normal, null, "MicLabel");
        }

        void BuildObjective()
        {
            var p = UiKit.Panel(root, "objective", new Vector2(0, 275), new Vector2(500, 64), UiKit.Glass);
            objectiveGroup = UiKit.Ensure<CanvasGroup>(p.gameObject);
            UiKit.Panel(p.transform, "bar", new Vector2(-246, 0), new Vector2(6, 40), UiKit.Gold);
            objectiveText = UiKit.Label(p.transform, "", new Vector2(8, 0), new Vector2(470, 56), 26, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Normal, null, "ObjectiveText");
            Hide(objectiveGroup);
        }

        void BuildSubtitles()
        {
            var p = UiKit.Panel(root, "subtitles", new Vector2(0, -215), new Vector2(900, 150), UiKit.Glass);
            subtitleGroup = UiKit.Ensure<CanvasGroup>(p.gameObject);
            speakerText = UiKit.Label(p.transform, "", new Vector2(0, 50), new Vector2(840, 34), 24, UiKit.Gold, TextAlignmentOptions.Left, FontStyles.Bold, null, "Speaker");
            lineText = UiKit.Label(p.transform, "", new Vector2(0, -12), new Vector2(840, 100), 33, UiKit.Text, TextAlignmentOptions.TopLeft, FontStyles.Normal, null, "Line");
            lineText.enableAutoSizing = true; lineText.fontSizeMin = 24; lineText.fontSizeMax = 33;
            Hide(subtitleGroup);
        }

        void BuildHints()
        {
            var holder = UiKit.Rect(root, "hints", new Vector2(0, -118), new Vector2(1000, 50));
            hintsGroup = UiKit.Ensure<CanvasGroup>(holder.gameObject);
            for (int i = 0; i < 3; i++)
            {
                var chip = UiKit.Panel(holder, "chip" + i, new Vector2((i - 1) * 330, 0), new Vector2(316, 46), UiKit.GlassLight);
                var t = UiKit.Label(chip.transform, "", Vector2.zero, new Vector2(296, 40), 22, UiKit.Cyan, TextAlignmentOptions.Center);
                hintTexts.Add(t);
                chip.gameObject.SetActive(!Application.isPlaying);
            }
            Hide(hintsGroup);
        }

        void BuildChoices()
        {
            var holder = UiKit.Rect(root, "choices", new Vector2(0, 20), new Vector2(760, 330));
            choicesGroup = UiKit.Ensure<CanvasGroup>(holder.gameObject);
            for (int i = 0; i < 4; i++)
            {
                var card = UiKit.Panel(holder, "card" + i, new Vector2(0, 120 - i * 82), new Vector2(740, 72), UiKit.GlassLight);
                var fill = UiKit.Panel(card.transform, "dwell", new Vector2(-370, 0), new Vector2(0, 72), new Color(0.5f, 0.9f, 1f, 0.35f));
                fill.rectTransform.pivot = new Vector2(0, 0.5f);
                fill.rectTransform.anchoredPosition = new Vector2(-370, 0);
                var badge = UiKit.Panel(card.transform, "badge", new Vector2(-330, 0), new Vector2(46, 46), UiKit.Gold, null, true);
                UiKit.Label(badge.transform, (i + 1).ToString(), Vector2.zero, new Vector2(46, 46), 30, new Color(0.1f, 0.08f, 0.02f), TextAlignmentOptions.Center, FontStyles.Bold);
                var label = UiKit.Label(card.transform, "", new Vector2(30, 12), new Vector2(620, 40), 29, UiKit.Text);
                var say = UiKit.Label(card.transform, "", new Vector2(30, -20), new Vector2(620, 28), 20, UiKit.Cyan);
                cards.Add(new Card { R = card.rectTransform, Label = label, Say = say, Fill = fill });
                card.gameObject.SetActive(!Application.isPlaying);
            }
            Hide(choicesGroup, false);
        }

        void BuildToast()
        {
            var p = UiKit.Panel(root, "toast", new Vector2(0, 170), new Vector2(760, 88), UiKit.Glass);
            toastGroup = UiKit.Ensure<CanvasGroup>(p.gameObject);
            toastText = UiKit.Label(p.transform, "", Vector2.zero, new Vector2(720, 80), 34, UiKit.Gold, TextAlignmentOptions.Center, FontStyles.Bold);
            Hide(toastGroup, false);
        }

        void BuildTitle()
        {
            var p = UiKit.Panel(root, "title", new Vector2(0, 40), new Vector2(900, 240), new Color(0.03f, 0.03f, 0.08f, 0.9f));
            titleGroup = UiKit.Ensure<CanvasGroup>(p.gameObject);
            titleText = UiKit.Label(p.transform, "", new Vector2(0, 40), new Vector2(860, 110), 76, UiKit.Gold, TextAlignmentOptions.Center, FontStyles.Bold);
            titleSub = UiKit.Label(p.transform, "", new Vector2(0, -60), new Vector2(860, 80), 30, UiKit.Text, TextAlignmentOptions.Center);
            Hide(titleGroup, false);
        }

        void BuildHelp()
        {
            var p = UiKit.Panel(root, "help", new Vector2(0, 10), new Vector2(860, 500), UiKit.Glass);
            helpGroup = UiKit.Ensure<CanvasGroup>(p.gameObject);
            UiKit.Label(p.transform, "WHAT CAN I SAY?", new Vector2(0, 215), new Vector2(800, 50), 34, UiKit.Gold, TextAlignmentOptions.Center, FontStyles.Bold);
            helpText = UiKit.Label(p.transform, "", new Vector2(0, -25), new Vector2(800, 410), 25, UiKit.Text, TextAlignmentOptions.TopLeft);
            helpText.enableAutoSizing = true; helpText.fontSizeMin = 17; helpText.fontSizeMax = 25;
            Hide(helpGroup, false);
        }

        void BuildSpellStatus()
        {
            var p = UiKit.Panel(root, "spell", new Vector2(400, 40), new Vector2(300, 110), UiKit.Glass);
            spellGroup = UiKit.Ensure<CanvasGroup>(p.gameObject);
            spellAccent = UiKit.Panel(p.transform, "accent", new Vector2(-158, 0), new Vector2(7, 80), UiKit.Gold);
            spellTitle = UiKit.Label(p.transform, "", new Vector2(8, 26), new Vector2(290, 36), 28, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            spellLine = UiKit.Label(p.transform, "", new Vector2(8, -18), new Vector2(290, 56), 20, UiKit.Dim, TextAlignmentOptions.TopLeft);
            Hide(spellGroup);
        }

        void BuildTrace()
        {
            var p = UiKit.Panel(root, "trace", new Vector2(-400, 40), new Vector2(300, 60), UiKit.Glass);
            traceGroup = UiKit.Ensure<CanvasGroup>(p.gameObject);
            traceText = UiKit.Label(p.transform, "", Vector2.zero, new Vector2(280, 50), 26, UiKit.Good, TextAlignmentOptions.Center, FontStyles.Bold);
            Hide(traceGroup);
        }

        // --- public API ----------------------------------------------------------------------------------------

        public void ShowSubtitle(string speaker, string line, float seconds = 0f)
        {
            speakerText.text = speaker == null ? "" : speaker.ToUpperInvariant();
            lineText.text = line;
            subtitleGroup.alpha = 1f;
            subtitleHideAt = seconds > 0 ? Time.unscaledTime + seconds : float.MaxValue;
        }

        public void HideSubtitle() => subtitleHideAt = 0f;

        public void SetObjective(string text)
        {
            objectiveText.text = text ?? "";
            objectiveGroup.alpha = string.IsNullOrEmpty(text) ? 0f : 1f;
        }

        public void SetHints(params string[] phrases)
        {
            for (int i = 0; i < hintTexts.Count; i++)
            {
                bool on = phrases != null && i < phrases.Length && !string.IsNullOrEmpty(phrases[i]);
                hintTexts[i].transform.parent.gameObject.SetActive(on);
                if (on) hintTexts[i].text = "SAY  “" + phrases[i] + "”";
            }
            hintsGroup.alpha = phrases == null || phrases.Length == 0 ? 0f : 1f;
        }

        public void Toast(string text, Color? c = null, float seconds = 2.4f)
        {
            toastText.text = text;
            toastText.color = c ?? UiKit.Gold;
            toastGroup.alpha = 1f;
            toastHideAt = Time.unscaledTime + seconds;
        }

        public void ShowTitle(string title, string sub, float seconds)
        {
            titleText.text = title; titleSub.text = sub;
            StartCoroutine(TitleRoutine(seconds));
        }

        IEnumerator TitleRoutine(float seconds)
        {
            titleGroup.alpha = 0f;
            while (titleGroup.alpha < 1f) { titleGroup.alpha += Time.unscaledDeltaTime * 1.5f; yield return null; }
            yield return new WaitForSecondsRealtime(seconds);
            while (titleGroup.alpha > 0f) { titleGroup.alpha -= Time.unscaledDeltaTime * 1.2f; yield return null; }
        }

        public void ShowHelp(List<string> lines, float seconds = 18f)
        {
            helpText.text = string.Join("\n", lines);
            helpGroup.alpha = 1f;
            helpHideAt = Time.unscaledTime + seconds;
        }

        public void HideHelp() => helpHideAt = 0f;

        // --- per-frame ------------------------------------------------------------------------------------------

        void RefreshChoices()
        {
            var vc = game.Voice;
            foreach (var c in cards) c.R.gameObject.SetActive(false);
            if (vc.AwaitingYesNo)
            {
                Fill(0, "Yes", "say “yes”", () => vc.Route("yes"));
                Fill(1, "No", "say “no”", () => vc.Route("no"));
                lastCardCount = 2;
                choicesGroup.alpha = 1f;
                return;
            }
            var ch = vc.ActiveChoices;
            if (ch == null || ch.Count == 0) { lastCardCount = 0; choicesGroup.alpha = 0f; return; }
            for (int i = 0; i < ch.Count && i < cards.Count; i++)
            {
                var choice = ch[i];
                string kw = choice.Keywords != null && choice.Keywords.Length > 0 ? choice.Keywords[0] : "";
                Fill(i, choice.Label, "say “" + (i + 1 == 1 ? "one" : i + 1 == 2 ? "two" : i + 1 == 3 ? "three" : "four") + "” or “" + kw + "”", () => vc.Route(choice.Label));
            }
            lastCardCount = Mathf.Min(ch.Count, cards.Count);
            choicesGroup.alpha = 1f;
        }

        int lastCardCount;

        void Fill(int i, string label, string say, System.Action pick)
        {
            var c = cards[i];
            c.R.gameObject.SetActive(true);
            c.Label.text = label; c.Say.text = say; c.Pick = pick; c.Dwell = 0f;
            c.Fill.rectTransform.sizeDelta = new Vector2(0, 72);
            // yes/no cards re-use slots 0 and 1: centre them
            c.R.anchoredPosition = new Vector2(0, 120 - i * 82);
        }

        void Update()
        {
            if (game == null || game.Hands.Head == null) return;
            Follow();

            wardBar.Set(game.Ward.Ward / game.Ward.MaxWard);
            wardBar.Fill.color = Color.Lerp(UiKit.Bad, UiKit.Good, game.Ward.Ward / game.Ward.MaxWard);
            manaBar.Set(game.Ward.Mana / game.Ward.MaxMana);

            UpdateMic();

            if (Time.unscaledTime > subtitleHideAt) UiKit.FadeGroup(subtitleGroup, 0f, 2.5f);
            if (Time.unscaledTime > toastHideAt) UiKit.FadeGroup(toastGroup, 0f, 2f);
            if (Time.unscaledTime > helpHideAt) UiKit.FadeGroup(helpGroup, 0f, 3f);

            UpdateSpellStatus();
            UpdateTrace();
            UpdateDwell();
        }

        void Follow()
        {
            var head = game.Hands.Head;
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            Vector3 target = head.position + flat.normalized * Distance + Vector3.down * 0.05f;
            Quaternion rot = Quaternion.LookRotation(target - head.position, Vector3.up);
            if (!placed) { smoothPos = target; smoothRot = rot; placed = true; }
            // lazy follow: stays put for small head motions, glides after bigger ones
            float angle = Quaternion.Angle(smoothRot, rot);
            float k = Mathf.Clamp01((angle - 12f) / 40f) * 4f + 0.6f;
            smoothPos = Vector3.Lerp(smoothPos, target, Time.deltaTime * k);
            smoothRot = Quaternion.Slerp(smoothRot, rot, Time.deltaTime * k);
            root.position = smoothPos;
            root.rotation = smoothRot;
        }

        void UpdateMic()
        {
            var sp = game.Speech;
            switch (sp.Status)
            {
                case SpeechStatus.Listening:
                    micDot.color = sp.HearingSpeech ? UiKit.Cyan : UiKit.Good;
                    micDot.rectTransform.localScale = Vector3.one * (sp.HearingSpeech ? 1.25f + 0.2f * Mathf.Sin(Time.time * 14f) : 1f);
                    micLabel.text = sp.HearingSpeech ? "Hearing you..." : "Listening";
                    break;
                case SpeechStatus.Initialising:
                    micDot.color = UiKit.Gold; micLabel.text = "Voice starting"; break;
                case SpeechStatus.PermissionDenied:
                    micDot.color = UiKit.Bad; micLabel.text = "Mic blocked: gestures"; break;
                default:
                    micDot.color = UiKit.Bad; micLabel.text = "Voice off: gestures"; break;
            }
        }

        void UpdateSpellStatus()
        {
            var cv = game.Canvas;
            if (cv.Armed != null)
            {
                var s = cv.Armed;
                spellGroup.alpha = 1f;
                spellTitle.text = s.Name;
                spellAccent.color = SpellEffects.ElementColor(s);
                bool needWord = s.RequiresIncantation && game.Speech.IsAvailable;
                spellLine.text = (needWord ? "Say “" + s.Incantation + "”" : "Push your palm forward or say “" + s.Incantation + "”")
                                 + "  •  seal " + Mathf.RoundToInt(cv.Quality * 100f) + "%";
            }
            else if (cv.RingPresent)
            {
                spellGroup.alpha = 1f;
                spellTitle.text = "Seal open";
                spellAccent.color = UiKit.Gold;
                spellLine.text = "Draw a rune inside the circle.";
            }
            else UiKit.FadeGroup(spellGroup, 0f, 4f);
        }

        void UpdateTrace()
        {
            if (game.Guide.Visible && game.Guide.Current != null)
            {
                traceGroup.alpha = 1f;
                float p = game.Guide.Progress;
                traceText.text = (game.Guide.Current == "ring" ? "Trace the circle  " : "Trace the rune  ") + Mathf.RoundToInt(p * 100f) + "%";
                traceText.color = p >= 0.75f ? UiKit.Good : UiKit.Cyan;
            }
            else UiKit.FadeGroup(traceGroup, 0f, 4f);
        }

        void UpdateDwell()
        {
            if (lastCardCount == 0) return;
            var h = game.Hands.Right.Tracked ? game.Hands.Right : (game.Hands.Left.Tracked ? game.Hands.Left : null);
            if (h == null || game.Hands.UsingSimulation) { foreach (var c in cards) c.Dwell = 0; return; }
            Vector3 tip = h.IndexTip;
            var corners = new Vector3[4];
            for (int i = 0; i < lastCardCount; i++)
            {
                var c = cards[i];
                c.R.GetWorldCorners(corners);
                Vector3 right = corners[3] - corners[0], up = corners[1] - corners[0];
                Vector3 n = Vector3.Cross(right, up).normalized;
                float dist = Mathf.Abs(Vector3.Dot(tip - corners[0], n));
                float u = Vector3.Dot(tip - corners[0], right.normalized) / right.magnitude;
                float v = Vector3.Dot(tip - corners[0], up.normalized) / up.magnitude;
                bool over = dist < 0.07f && u > 0 && u < 1 && v > 0 && v < 1;
                c.Dwell = over ? c.Dwell + Time.deltaTime : Mathf.Max(0, c.Dwell - Time.deltaTime * 2f);
                c.Fill.rectTransform.sizeDelta = new Vector2(740f * Mathf.Clamp01(c.Dwell / 1.1f), 72);
                if (c.Dwell >= 1.1f) { c.Dwell = 0; c.Pick?.Invoke(); return; }
            }
        }
    }
}
