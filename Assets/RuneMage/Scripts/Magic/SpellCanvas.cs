using System;
using System.Collections.Generic;
using UnityEngine;
using RuneMage.Recognition;

namespace RuneMage
{
    /// <summary>
    /// The mid-air drawing surface. A pinching fingertip lays glowing ink in 3D; strokes are flattened onto a vertical
    /// "spell plane" facing the player so shapes can be recognised, then replaced by neat versions:
    ///   1. a closed circle becomes the SEAL (ring)
    ///   2. shapes drawn inside it are matched to RUNES (fire triangle, lightning bolt, ...)
    ///   3. ring + rune(s) arm a spell that is released by a push gesture or a spoken incantation
    /// Nothing here touches menus or buttons.
    /// </summary>
    public sealed class SpellCanvas : MonoBehaviour
    {
        // --- tuning -----------------------------------------------------------------------------------------------
        const float MinPointSpacing = 0.006f;
        const float InkWidth = 0.0075f;
        const float GlyphSettleDelay = 0.85f;
        const float IdleClearSeconds = 30f;
        const float AcceptDistance = 1.5f;          // recogniser distance for free drawing
        const float GuidedAcceptDistance = 2.1f;    // more forgiving when a guide names the expected rune
        const float PlaneFlatten = 0.8f;            // how strongly ink is pulled onto the plane (assist)

        // --- state ------------------------------------------------------------------------------------------------
        public sealed class Glyph
        {
            public MagicType Type;
            public Vector2 Center;
            public float Size;
            public float Quality;
            public List<LineRenderer> Lines = new List<LineRenderer>();
        }

        sealed class Stroke
        {
            public List<Vector3> World = new List<Vector3>();
            public List<Pt> Plane = new List<Pt>();
            public LineRenderer Line;
            public float Length;
            public bool Pending;      // inside ring and waiting to be classified as a rune
        }

        Game game;
        HandInput hands;
        PointCloudRecognizer recognizer;

        public bool HasPlane { get; private set; }
        Vector3 origin, fwd, right;
        public Vector3 PlaneOrigin => origin;
        public Vector3 PlaneForward => fwd;     // away from the player
        public Vector3 PlaneRight => right;

        readonly List<Stroke> strokes = new List<Stroke>();
        Stroke current;
        Vector3 lastPoint;
        float lastInkTime, glyphTimer = -1f, idleTimer;

        public bool RingPresent { get; private set; }
        public Vector2 RingCenter { get; private set; }
        public float RingRadius { get; private set; }
        public float RingNeatness { get; private set; }
        LineRenderer ringLine;
        Transform ringRoot, ringFill, ringPulse;
        Light ringLight;
        Color ringColor = new Color(1f, 0.85f, 0.45f);

        public readonly List<Glyph> Glyphs = new List<Glyph>();
        public SpellDef Armed { get; private set; }
        public float Quality { get; private set; } = 1f;
        public bool Busy => current != null;

        // guide support
        public string ExpectedRune;                 // set by the tutorial / practice guide ("fire")
        public bool GuideActive;                    // guide pins the plane to its anchor

        Transform tipOrb;
        public Vector3 GuideCenter { get; private set; }
        bool guideAnchored;

        // --- events (UI, tutorial and audio subscribe) ---------------------------------------------------------
        public event Action RingClosed;
        public event Action<MagicType> GlyphAccepted;
        public event Action<string> Rejected;                // human-readable reason
        public event Action<SpellDef> SpellArmed;
        public event Action<string> ComboUnknown;
        public event Action Cleared;
        public event Action StrokeStarted;

        public void Init(Game g)
        {
            game = g;
            hands = g.Hands;
            recognizer = RuneShapes.BuildRecognizer();
            tipOrb = Fx.Billboard(transform, "inkTipOrb", Fx.Radial, new Color(0.7f, 0.95f, 1f), 0.05f, 2f);
            tipOrb.gameObject.SetActive(false);
        }

        // --- plane ------------------------------------------------------------------------------------------------

        /// <summary>Where the guide wants the seal to appear (stable until cleared or the player turns away).</summary>
        public Vector3 EnsureGuideAnchor()
        {
            var head = hands.Head;
            if (head == null) return GuideCenter;
            Vector3 flatFwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (flatFwd.sqrMagnitude < 0.01f) flatFwd = Vector3.forward;
            flatFwd.Normalize();
            if (!guideAnchored || Vector3.Angle(GuideDirFlat(), flatFwd) > 55f)
            {
                Vector3 c = head.position + flatFwd * 0.62f + Vector3.down * 0.12f;
                GuideCenter = c;
                guideAnchored = true;
                if (!RingPresent) HasPlane = false;
            }
            return GuideCenter;
        }

        /// <summary>Create the spell plane at the guide anchor before any ink exists, so the guide and the canvas agree.</summary>
        public void PreparePlaneAtGuide()
        {
            if (current != null || RingPresent) return;
            HasPlane = false;
            CreatePlane(EnsureGuideAnchor());
        }

        Vector3 GuideDirFlat()
        {
            var head = hands.Head;
            Vector3 d = Vector3.ProjectOnPlane(GuideCenter - head.position, Vector3.up);
            return d.sqrMagnitude < 0.001f ? Vector3.forward : d.normalized;
        }

        void CreatePlane(Vector3 point)
        {
            var head = hands.Head;
            Vector3 o = point;
            if (GuideActive) o = EnsureGuideAnchor();
            Vector3 away = Vector3.ProjectOnPlane(o - head.position, Vector3.up);
            if (away.sqrMagnitude < 0.01f) away = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            fwd = away.normalized;
            right = Vector3.Cross(Vector3.up, fwd).normalized;
            origin = o;
            HasPlane = true;
        }

        public Vector2 ToPlane(Vector3 w)
        {
            Vector3 d = w - origin;
            return new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, Vector3.up));
        }

        public Vector3 FromPlane(Vector2 p, float depth = 0f) => origin + right * p.x + Vector3.up * p.y + fwd * depth;

        // --- update -----------------------------------------------------------------------------------------------

        void Update()
        {
            if (hands == null || hands.Head == null) return;

            var inker = hands.Inker;
            if (inker != null && game.InkAllowed)
            {
                Vector3 tip = inker.IndexTip;
                if (current == null) BeginStroke(tip);
                else AddPoint(tip);
                tipOrb.gameObject.SetActive(true);
                tipOrb.position = tip;
            }
            else
            {
                if (current != null) EndStroke();
                // keep a faint orb on the index finger while the hand is up, so the player sees where ink will start
                var h = hands.Right.Tracked ? hands.Right : (hands.Left.Tracked ? hands.Left : null);
                if (h != null && game.InkAllowed && game.ShowFingerOrb)
                {
                    tipOrb.gameObject.SetActive(true);
                    tipOrb.position = h.IndexTip;
                    tipOrb.localScale = Vector3.one * 0.018f;
                }
                else tipOrb.gameObject.SetActive(false);
            }
            if (current != null && tipOrb.gameObject.activeSelf) tipOrb.localScale = Vector3.one * (0.045f + 0.01f * Mathf.Sin(Time.time * 18f));

            // classify pending runes after the player stops drawing
            if (glyphTimer >= 0f && current == null)
            {
                glyphTimer -= Time.deltaTime;
                if (glyphTimer < 0f) ClassifyPending();
            }

            // The drawing stays until the spell is released: the player speaks its name (or pushes), or says "cancel".
            idleTimer = 0f;

            AnimateRing();
        }

        // --- stroke handling --------------------------------------------------------------------------------------

        void BeginStroke(Vector3 tip)
        {
            if (!HasPlane) CreatePlane(tip);
            current = new Stroke();
            current.Line = Fx.NewLine(transform, "stroke", RingPresent ? ColorForPending() : new Color(0.6f, 0.9f, 1f), InkWidth);
            strokes.Add(current);
            lastPoint = tip;
            current.World.Add(tip);
            current.Plane.Add(PtOf(tip));
            RenderStroke(current);
            idleTimer = 0f;
            glyphTimer = -1f;
            AudioSynth.PlayAt("ink", tip, 0.45f, UnityEngine.Random.Range(0.95f, 1.1f));
            StrokeStarted?.Invoke();
        }

        Color ColorForPending() => ExpectedRune != null ? Magic.Color(Magic.FromRuneId(ExpectedRune)) : new Color(0.85f, 0.95f, 1f);

        Pt PtOf(Vector3 w)
        {
            var p = ToPlane(w);
            return new Pt(p.x, p.y, 0);
        }

        void AddPoint(Vector3 tip)
        {
            if ((tip - lastPoint).sqrMagnitude < MinPointSpacing * MinPointSpacing) return;
            current.Length += Vector3.Distance(tip, lastPoint);
            lastPoint = tip;
            current.World.Add(tip);
            current.Plane.Add(PtOf(tip));
            RenderStroke(current);
            idleTimer = 0f;
        }

        void RenderStroke(Stroke s)
        {
            int n = s.World.Count;
            s.Line.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                Vector3 w = s.World[i];
                Vector3 flat = FromPlane(ToPlane(w), 0f);
                float depth = Vector3.Dot(w - origin, fwd);
                s.Line.SetPosition(i, flat + fwd * depth * (1f - PlaneFlatten));
            }
            s.Line.widthCurve = AnimationCurve.Linear(0, 0.6f, 1, 1f);
        }

        void EndStroke()
        {
            var s = current;
            current = null;
            if (s == null) return;

            if (s.Length < 0.05f || s.Plane.Count < 6)
            {
                DestroyStroke(s);                  // an accidental pinch
                return;
            }

            if (!RingPresent)
            {
                var r = RingDetector.Analyze(s.Plane);
                if (r.IsRing) { PlaceRing(r, s); return; }
                Reject(s, r.IsClosedLoop ? r.Reason : "Start with a circle: draw a closed ring in the air.");
                return;
            }

            // A closed loop that is as big as the existing ring is "another ring": not supported yet.
            Vector2 c = Vector2.zero;
            foreach (var p in s.Plane) c += new Vector2(p.X, p.Y);
            c /= s.Plane.Count;
            float dist = Vector2.Distance(c, RingCenter);
            if (dist > RingRadius * 0.95f)
            {
                Reject(s, "Draw the rune inside the circle.");
                return;
            }
            s.Pending = true;
            glyphTimer = GlyphSettleDelay;
        }

        void Reject(Stroke s, string why)
        {
            AudioSynth.PlayAt("error", s.World[s.World.Count - 1], 0.6f);
            Rejected?.Invoke(why);
            StartCoroutine(FadeAndDestroy(s));
        }

        System.Collections.IEnumerator FadeAndDestroy(Stroke s)
        {
            if (s.Line != null) s.Line.sharedMaterial = Fx.Ink(new Color(1f, 0.3f, 0.25f));
            float t = 0;
            while (t < 0.6f && s.Line != null)
            {
                t += Time.deltaTime;
                s.Line.widthMultiplier = InkWidth * (1f - t / 0.6f);
                yield return null;
            }
            strokes.Remove(s);
            if (s.Line != null) Destroy(s.Line.gameObject);
        }

        void DestroyStroke(Stroke s)
        {
            strokes.Remove(s);
            if (s.Line != null) Destroy(s.Line.gameObject);
        }

        // --- seal ring --------------------------------------------------------------------------------------------

        void PlaceRing(RingResult r, Stroke s)
        {
            DestroyStroke(s);
            RingPresent = true;
            RingCenter = new Vector2(r.CenterX, r.CenterY);
            RingRadius = r.Radius;
            RingNeatness = r.Neatness;

            ringRoot = new GameObject("seal").transform;
            ringRoot.SetParent(transform, false);
            ringLine = Fx.NewLine(ringRoot, "rim", ringColor, 0.011f, true);
            const int seg = 72;
            ringLine.positionCount = seg;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                ringLine.SetPosition(i, FromPlane(RingCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * RingRadius));
            }
            // soft glowing disc + light so the seal reads as a lit magic circle in the room
            ringFill = Fx.Billboard(ringRoot, "fill", Fx.Radial, new Color(1f, 0.8f, 0.4f, 0.55f), RingRadius * 2f, 0.9f);
            Destroy(ringFill.GetComponent<FaceCamera>());
            ringFill.position = FromPlane(RingCenter, 0.005f);
            ringFill.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            ringPulse = Fx.Billboard(ringRoot, "pulse", Fx.RingTex, ringColor, RingRadius * 2f, 2f);
            Destroy(ringPulse.GetComponent<FaceCamera>());
            ringPulse.position = FromPlane(RingCenter, 0.006f);
            ringPulse.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            ringLight = Fx.PointLight(ringRoot, ringColor, 0.8f, 1.2f);
            ringLight.transform.position = FromPlane(RingCenter, -0.15f);

            AudioSynth.PlayAt("ring", FromPlane(RingCenter), 0.7f);
            Fx.Burst(FromPlane(RingCenter), ringColor, 30, 1.2f, 0.035f, 0.8f);
            RingClosed?.Invoke();
        }

        void AnimateRing()
        {
            if (!RingPresent || ringRoot == null) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (Armed != null ? 5f : 2.4f));
            ringPulse.localScale = Vector3.one * RingRadius * 2f * (1f + 0.04f * pulse);
            ringLight.intensity = 0.6f + pulse * (Armed != null ? 1.1f : 0.4f);
            var ln = ringLine;
            ln.widthMultiplier = 0.009f + pulse * 0.004f;
        }

        void TintRing(Color c)
        {
            ringColor = c;
            if (ringLine != null) ringLine.sharedMaterial = Fx.Ink(c);
            if (ringPulse != null) ringPulse.GetComponent<MeshRenderer>().sharedMaterial = Fx.Make(Fx.RingTex, c, true, 2f);
            if (ringFill != null) ringFill.GetComponent<MeshRenderer>().sharedMaterial = Fx.Make(Fx.Radial, new Color(c.r, c.g, c.b, 0.55f), true, 0.9f);
            if (ringLight != null) ringLight.color = c;
        }

        // --- rune classification ------------------------------------------------------------------------------------

        void ClassifyPending()
        {
            glyphTimer = -1f;
            var pending = strokes.FindAll(s => s.Pending);
            if (pending.Count == 0) return;

            // group strokes whose bounding boxes overlap (a snowflake or plus sign is several strokes)
            var groups = new List<List<Stroke>>();
            foreach (var s in pending)
            {
                Rect b = Bounds(s);
                List<Stroke> hit = null;
                foreach (var g in groups)
                {
                    Rect gb = Bounds(g);
                    gb.xMin -= 0.03f; gb.xMax += 0.03f; gb.yMin -= 0.03f; gb.yMax += 0.03f;
                    if (gb.Overlaps(b)) { hit = g; break; }
                }
                if (hit == null) groups.Add(new List<Stroke> { s }); else hit.Add(s);
            }

            foreach (var g in groups)
            {
                var strokeSets = new List<Pt[]>();
                foreach (var s in g) strokeSets.Add(s.Plane.ToArray());

                MatchResult m = recognizer.Recognize(strokeSets);
                float d = 3f * (1f - m.Score);
                bool ok = m.Name != null && d <= AcceptDistance && m.Margin >= 0.02f;

                // guided drawing: accept the named rune with a more forgiving distance
                if (!ok && ExpectedRune != null)
                {
                    float es = recognizer.ScoreAgainst(ExpectedRune, strokeSets);
                    if (3f * (1f - es) <= GuidedAcceptDistance) { m.Name = ExpectedRune; m.Score = es; ok = true; }
                }
                // a drawing aimed at the guide must not be "recognised" as a different rune
                if (ok && ExpectedRune != null && m.Name != ExpectedRune)
                {
                    float es = recognizer.ScoreAgainst(ExpectedRune, strokeSets);
                    if (3f * (1f - es) <= GuidedAcceptDistance) { m.Name = ExpectedRune; m.Score = es; }
                }

                if (!ok)
                {
                    foreach (var s in g) { s.Pending = false; Reject(s, "I could not read that rune. Trace the guide, or try again."); }
                    continue;
                }

                var type = Magic.FromRuneId(m.Name);
                if (Glyphs.Exists(x => x.Type == type))
                {
                    foreach (var s in g) { s.Pending = false; Reject(s, "That rune is already in the circle."); }
                    continue;
                }
                if (Glyphs.Count >= 2)
                {
                    foreach (var s in g) { s.Pending = false; Reject(s, "A circle holds at most two runes."); }
                    continue;
                }
                AcceptGlyph(type, g, m.Score);
            }
            Resolve();
        }

        static Rect Bounds(Stroke s) => Bounds(new List<Stroke> { s });

        static Rect Bounds(List<Stroke> g)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var s in g)
                foreach (var p in s.Plane)
                {
                    minX = Mathf.Min(minX, p.X); maxX = Mathf.Max(maxX, p.X);
                    minY = Mathf.Min(minY, p.Y); maxY = Mathf.Max(maxY, p.Y);
                }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        void AcceptGlyph(MagicType type, List<Stroke> group, float score)
        {
            Rect b = Bounds(group);
            float size = Mathf.Clamp(Mathf.Max(b.width, b.height), 0.07f, RingRadius * 1.3f);
            var glyph = new Glyph { Type = type, Center = b.center, Size = size, Quality = Mathf.Clamp01(score) };

            // swap the shaky strokes for the clean rune, tinted by its element
            foreach (var s in group) DestroyStroke(s);
            Color col = Magic.Color(type);
            foreach (var poly in RuneShapes.Get(Magic.RuneId(type)))
            {
                var lr = Fx.NewLine(ringRoot, "glyph_" + type, col, 0.0095f);
                lr.positionCount = poly.Length;
                for (int i = 0; i < poly.Length; i++)
                {
                    Vector2 p = glyph.Center + new Vector2(poly[i].X - 0.5f, poly[i].Y - 0.5f) * size;
                    lr.SetPosition(i, FromPlane(p, 0.002f));
                }
                glyph.Lines.Add(lr);
            }
            Glyphs.Add(glyph);
            AudioSynth.PlayAt("glyph", FromPlane(glyph.Center), 0.8f, 1f + 0.12f * Glyphs.Count);
            Fx.Burst(FromPlane(glyph.Center), col, 18, 0.9f, 0.03f, 0.6f);
            GlyphAccepted?.Invoke(type);
        }

        void Resolve()
        {
            var types = new List<MagicType>();
            foreach (var g in Glyphs) types.Add(g.Type);
            var spell = SpellBook.Resolve(types);

            float q = RingNeatness * 0.35f;
            float gq = 0f;
            foreach (var g in Glyphs) gq += g.Quality;
            q += Glyphs.Count > 0 ? 0.65f * gq / Glyphs.Count : 0f;
            Quality = Mathf.Clamp01(q);

            if (spell == null)
            {
                Armed = null;
                if (types.Count == 2)
                {
                    ComboUnknown?.Invoke("Those two runes do not combine. Try erasing one.");
                    AudioSynth.PlayAt("error", FromPlane(RingCenter), 0.5f);
                }
                return;
            }
            Armed = spell;
            Color c = spell.IsCombo ? Color.Lerp(Magic.Color(spell.Primary), Magic.Color(spell.Secondary), 0.5f) : Magic.Color(spell.Primary);
            TintRing(c);
            AudioSynth.PlayAt("ready", FromPlane(RingCenter), 0.8f);
            SpellArmed?.Invoke(spell);
        }

        // --- cast / clear -------------------------------------------------------------------------------------------

        public struct Release
        {
            public SpellDef Spell;
            public Vector3 Origin, Direction;
            public float Power;       // 0.8 .. 1.3, from drawing quality and seal size
            public float Quality;
        }

        public bool TryRelease(out Release r)
        {
            r = default;
            if (!RingPresent || Armed == null) return false;
            float sizeBoost = Mathf.InverseLerp(0.07f, 0.3f, RingRadius);
            r.Spell = Armed;
            r.Origin = FromPlane(RingCenter);
            r.Direction = fwd;
            r.Quality = Quality;
            r.Power = Mathf.Lerp(0.8f, 1.15f, Quality) + 0.15f * sizeBoost;
            return true;
        }

        public void FlashAndClear()
        {
            if (!RingPresent) return;
            Fx.Burst(FromPlane(RingCenter), ringColor, 40, 3f, 0.05f, 0.7f);
            Clear(null);
        }

        public void Clear(string message)
        {
            foreach (var s in strokes) if (s.Line != null) Destroy(s.Line.gameObject);
            strokes.Clear();
            current = null;
            foreach (var g in Glyphs) foreach (var l in g.Lines) if (l != null) Destroy(l.gameObject);
            Glyphs.Clear();
            if (ringRoot != null) Destroy(ringRoot.gameObject);
            RingPresent = false;
            Armed = null;
            HasPlane = false;
            glyphTimer = -1f;
            idleTimer = 0f;
            ringColor = new Color(1f, 0.85f, 0.45f);
            guideAnchored = false;
            if (message != null) Rejected?.Invoke(message);
            Cleared?.Invoke();
        }

        /// <summary>Erase just the runes but keep the seal (the player wants to redraw the symbol).</summary>
        public void EraseRunes()
        {
            foreach (var s in strokes.FindAll(x => x.Pending)) DestroyStroke(s);
            foreach (var g in Glyphs) foreach (var l in g.Lines) if (l != null) Destroy(l.gameObject);
            Glyphs.Clear();
            Armed = null;
            TintRing(new Color(1f, 0.85f, 0.45f));
        }

        public bool Empty => !RingPresent && strokes.Count == 0;
    }
}
