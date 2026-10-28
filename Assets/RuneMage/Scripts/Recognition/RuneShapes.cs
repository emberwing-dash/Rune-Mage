using System;
using System.Collections.Generic;

namespace RuneMage.Recognition
{
    /// <summary>
    /// Reference stroke shapes for every rune, in a unit box (y up). Used both to build the recogniser templates and
    /// to draw the guide the player traces. Authored in code so there are no external data files to keep in sync.
    /// </summary>
    public static class RuneShapes
    {
        public static readonly string[] Ids =
            { "fire", "water", "wind", "lightning", "earth", "ice", "barrier", "heal" };

        static readonly Dictionary<string, List<Pt[]>> cache = new Dictionary<string, List<Pt[]>>();

        public static List<Pt[]> Get(string id)
        {
            if (cache.TryGetValue(id, out var c)) return c;
            var s = Build(id);
            cache[id] = s;
            return s;
        }

        static Pt[] Poly(params float[] xy)
        {
            var p = new Pt[xy.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new Pt(xy[i * 2], xy[i * 2 + 1], 0);
            return p;
        }

        static List<Pt[]> Build(string id)
        {
            var l = new List<Pt[]>();
            switch (id)
            {
                case "fire": // upward triangle
                    l.Add(Poly(0.5f, 1f, 0f, 0f, 1f, 0f, 0.5f, 1f));
                    break;
                case "water": // downward triangle
                    l.Add(Poly(0f, 1f, 1f, 1f, 0.5f, 0f, 0f, 1f));
                    break;
                case "wind": // two waves
                {
                    var pts = new Pt[25];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float x = i / 24f;
                        pts[i] = new Pt(x, 0.5f + 0.28f * (float)Math.Sin(x * Math.PI * 4), 0);
                    }
                    l.Add(pts);
                    break;
                }
                case "lightning": // bolt
                    l.Add(Poly(0.62f, 1f, 0.22f, 0.52f, 0.72f, 0.48f, 0.32f, 0f));
                    break;
                case "earth": // square
                    l.Add(Poly(0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f, 0f, 0f));
                    break;
                case "ice": // snowflake: three crossing lines
                    l.Add(Poly(0.5f, 0f, 0.5f, 1f));
                    l.Add(Poly(0.07f, 0.25f, 0.93f, 0.75f));
                    l.Add(Poly(0.07f, 0.75f, 0.93f, 0.25f));
                    break;
                case "barrier": // diamond
                    l.Add(Poly(0.5f, 1f, 1f, 0.5f, 0.5f, 0f, 0f, 0.5f, 0.5f, 1f));
                    break;
                case "heal": // cross
                    l.Add(Poly(0.5f, 0f, 0.5f, 1f));
                    l.Add(Poly(0f, 0.5f, 1f, 0.5f));
                    break;
                default:
                    throw new ArgumentException("Unknown rune " + id);
            }
            return l;
        }

        /// <summary>Densify a polyline so the recogniser sees evenly spaced points, like a real stroke.</summary>
        public static Pt[] Densify(Pt[] stroke, int perSegment = 12)
        {
            var o = new List<Pt>();
            for (int i = 0; i < stroke.Length - 1; i++)
                for (int k = 0; k < perSegment; k++)
                {
                    float t = k / (float)perSegment;
                    o.Add(new Pt(stroke[i].X + (stroke[i + 1].X - stroke[i].X) * t,
                                 stroke[i].Y + (stroke[i + 1].Y - stroke[i].Y) * t, 0));
                }
            o.Add(stroke[stroke.Length - 1]);
            return o.ToArray();
        }

        public static PointCloudRecognizer BuildRecognizer()
        {
            var rec = new PointCloudRecognizer();
            foreach (var id in Ids)
            {
                var strokes = new List<Pt[]>();
                foreach (var s in Get(id)) strokes.Add(Densify(s));
                rec.AddTemplate(id, strokes);
            }
            return rec;
        }
    }
}
