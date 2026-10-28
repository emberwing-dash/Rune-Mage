using System.Collections.Generic;
using UnityEngine;
using RuneMage.Recognition;

namespace RuneMage
{
    /// <summary>
    /// Floating trace-guide in mid-air: a dotted outline with a travelling light that shows the stroke path. Dots turn green as the
    /// player's fingertip passes through them. It is anchored in space (not on a surface), so the player draws in the air.
    /// </summary>
    public sealed class GuideRenderer : MonoBehaviour
    {
        sealed class Dot
        {
            public Transform T;
            public Vector2 P;          // plane coords
            public bool Hit;
            public MeshRenderer R;
        }

        Game game;
        SpellCanvas canvas;
        readonly List<Dot> dots = new List<Dot>();
        readonly List<Vector2> tracerPath = new List<Vector2>();
        Transform tracer, root;
        string showing;               // "ring" or a rune id
        float tracerT;
        Material dotIdle, dotHit;

        public bool Visible => root != null && root.gameObject.activeSelf;
        public float Progress { get; private set; }
        public string Current => showing;
        public bool Complete => Progress >= 0.75f;

        public void Init(Game g)
        {
            game = g;
            canvas = g.Canvas;
            root = new GameObject("guide").transform;
            root.SetParent(transform, false);
            dotIdle = Fx.Make(Fx.Radial, new Color(0.7f, 0.9f, 1f, 0.9f), true, 1.2f);
            dotHit = Fx.Make(Fx.Radial, new Color(0.4f, 1f, 0.5f, 1f), true, 1.8f);
            tracer = Fx.Billboard(root, "tracer", Fx.Radial, new Color(1f, 0.95f, 0.6f), 0.06f, 2.4f);
            root.gameObject.SetActive(false);
        }

        public void Hide()
        {
            showing = null;
            canvas.GuideActive = false;
            canvas.ExpectedRune = null;
            if (root != null) root.gameObject.SetActive(false);
        }

        /// <summary>Show the seal-ring guide.</summary>
        public void ShowRing(float radius = 0.17f)
        {
            canvas.GuideActive = true;
            canvas.ExpectedRune = null;
            canvas.PreparePlaneAtGuide();
            showing = "ring";
            Build(RingPath(Vector2.zero, radius, 44), canvas.PlaneOrigin, true, new Color(1f, 0.85f, 0.45f));
        }

        /// <summary>Show a rune guide inside the existing seal.</summary>
        public void ShowRune(string runeId)
        {
            if (!canvas.RingPresent) return;
            canvas.GuideActive = true;
            canvas.ExpectedRune = runeId;
            showing = runeId;
            float size = canvas.RingRadius * 1.15f;
            var path = new List<Vector2>();
            foreach (var poly in RuneShapes.Get(runeId))
            {
                var d = RuneShapes.Densify(poly, 5);
                foreach (var p in d) path.Add(canvas.RingCenter + new Vector2(p.X - 0.5f, p.Y - 0.5f) * size);
                path.Add(new Vector2(float.NaN, float.NaN));     // stroke break marker
            }
            Build(path, canvas.PlaneOrigin, false, Magic.Color(Magic.FromRuneId(runeId)));
        }

        static List<Vector2> RingPath(Vector2 c, float r, int n)
        {
            var l = new List<Vector2>();
            // start at the top and go clockwise, which is how people naturally draw a circle
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.PI / 2f - i / (float)n * Mathf.PI * 2f;
                l.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            return l;
        }

        void Build(List<Vector2> path, Vector3 originWorld, bool ringMode, Color tint)
        {
            foreach (var d in dots) if (d.T != null) Destroy(d.T.gameObject);
            dots.Clear();
            tracerPath.Clear();
            Progress = 0f;
            tracerT = 0f;
            root.gameObject.SetActive(true);

            dotIdle = Fx.Make(Fx.Radial, new Color(tint.r * 0.5f + 0.4f, tint.g * 0.5f + 0.4f, tint.b * 0.5f + 0.5f, 0.9f), true, 1.3f);

            Vector2 offset = Vector2.zero;
            if (ringMode) offset = Vector2.zero;
            int skip = 0;
            Vector2 prev = Vector2.zero; bool havePrev = false;
            float acc = 0f;
            foreach (var raw in path)
            {
                if (float.IsNaN(raw.x)) { havePrev = false; tracerPath.Add(raw); continue; }
                Vector2 p = ringMode ? raw : raw;
                if (havePrev) acc += Vector2.Distance(prev, p);
                if (!havePrev || acc >= 0.028f)
                {
                    acc = 0f;
                    var t = Fx.Billboard(root, "dot", Fx.Radial, Color.white, 0.02f, 1.2f);
                    var mr = t.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = dotIdle;
                    t.position = canvas.FromPlane(p + offset);
                    dots.Add(new Dot { T = t, P = p + offset, R = mr });
                    skip++;
                }
                prev = p; havePrev = true;
                tracerPath.Add(p + offset);
            }
            // guide dots are positioned in world space once; they stay put while the player draws
            tracer.SetAsLastSibling();
        }

        void Update()
        {
            if (!Visible || tracerPath.Count < 2) return;

            // travelling light
            tracerT += Time.deltaTime * 0.35f;
            if (tracerT > 1.35f) tracerT = 0f;
            float f = Mathf.Clamp01(tracerT / 1.2f) * (tracerPath.Count - 1);
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, tracerPath.Count - 2);
            Vector2 a = tracerPath[i], b = tracerPath[i + 1];
            Vector2 p = float.IsNaN(a.x) ? (float.IsNaN(b.x) ? Vector2.zero : b) : float.IsNaN(b.x) ? a : Vector2.Lerp(a, b, f - i);
            tracer.position = canvas.FromPlane(p, -0.005f);
            tracer.localScale = Vector3.one * (0.045f + 0.012f * Mathf.Sin(Time.time * 10f));

            // mark dots the fingertip passes through
            var inker = game.Hands.Inker;
            if (inker != null && canvas.HasPlane)
            {
                Vector3 tip = inker.IndexTip;
                Vector2 tp = canvas.ToPlane(tip);
                float depth = Mathf.Abs(Vector3.Dot(tip - canvas.PlaneOrigin, canvas.PlaneForward));
                if (depth < 0.22f)
                {
                    int hit = 0;
                    foreach (var d in dots)
                    {
                        if (!d.Hit && Vector2.Distance(d.P, tp) < 0.045f)
                        {
                            d.Hit = true;
                            d.R.sharedMaterial = dotHit;
                            d.T.localScale = Vector3.one * 0.03f;
                        }
                        if (d.Hit) hit++;
                    }
                    Progress = dots.Count > 0 ? hit / (float)dots.Count : 0f;
                }
            }
        }

        /// <summary>Guide dots reset so the player can trace it again.</summary>
        public void ResetProgress()
        {
            foreach (var d in dots) { d.Hit = false; d.R.sharedMaterial = dotIdle; d.T.localScale = Vector3.one * 0.02f; }
            Progress = 0f;
        }
    }
}
