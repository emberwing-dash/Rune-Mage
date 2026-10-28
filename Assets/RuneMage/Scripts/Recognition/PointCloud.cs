using System;
using System.Collections.Generic;

namespace RuneMage.Recognition
{
    /// <summary>2D point tagged with its stroke id. Pure C# (no UnityEngine) so it can be unit-tested outside Unity.</summary>
    public struct Pt
    {
        public float X, Y;
        public int Id;
        public Pt(float x, float y, int id = 0) { X = x; Y = y; Id = id; }
    }

    public struct MatchResult
    {
        public string Name;
        public float Score;   // 0..1, higher is better
        public float Margin;  // gap to the runner-up template
    }

    /// <summary>
    /// $P point-cloud recogniser (Vatavu, Anthony, Wobbrock 2012). Order- and stroke-count-independent, which suits
    /// runes drawn in the air where players lift and re-pinch unpredictably. Rotation is deliberately NOT normalised:
    /// a fire triangle must point up, a water triangle down.
    /// </summary>
    public sealed class PointCloudRecognizer
    {
        public const int N = 32;
        const float DistanceScale = 3.0f;

        readonly List<(string name, Pt[] pts)> templates = new List<(string, Pt[])>();

        public int TemplateCount => templates.Count;

        public void AddTemplate(string name, IList<Pt[]> strokes) => templates.Add((name, Prepare(strokes)));

        public MatchResult Recognize(IList<Pt[]> strokes)
        {
            var best = new MatchResult { Name = null, Score = 0f };
            if (templates.Count == 0) return best;
            var cand = Prepare(strokes);
            if (cand == null) return best;

            float bestD = float.MaxValue, second = float.MaxValue;
            string bestName = null;
            foreach (var t in templates)
            {
                float d = GreedyCloudMatch(cand, t.pts);
                if (d < bestD)
                {
                    second = bestD;
                    bestD = d;
                    bestName = t.name;
                }
                else if (d < second) second = d;
            }
            best.Name = bestName;
            best.Score = Math.Max(0f, 1f - bestD / DistanceScale);
            best.Margin = second == float.MaxValue ? 1f : (second - bestD) / DistanceScale;
            return best;
        }

        /// <summary>Per-template scores, used by the guide to show "how close" the player is.</summary>
        public float ScoreAgainst(string name, IList<Pt[]> strokes)
        {
            var cand = Prepare(strokes);
            if (cand == null) return 0f;
            foreach (var t in templates)
                if (t.name == name)
                    return Math.Max(0f, 1f - GreedyCloudMatch(cand, t.pts) / DistanceScale);
            return 0f;
        }

        // ---------------------------------------------------------------------------------------------------------

        static Pt[] Prepare(IList<Pt[]> strokes)
        {
            var all = new List<Pt>();
            for (int s = 0; s < strokes.Count; s++)
                foreach (var p in strokes[s]) all.Add(new Pt(p.X, p.Y, s));
            if (all.Count < 2) return null;
            var r = Resample(all, N);
            Scale(r);
            TranslateToCentroid(r);
            return r;
        }

        static float PathLength(List<Pt> pts)
        {
            float d = 0;
            for (int i = 1; i < pts.Count; i++)
                if (pts[i].Id == pts[i - 1].Id) d += Dist(pts[i - 1], pts[i]);
            return d;
        }

        static Pt[] Resample(List<Pt> input, int n)
        {
            var pts = new List<Pt>(input);
            float len = PathLength(pts);
            if (len < 1e-6f)
            {
                var flat = new Pt[n];
                for (int i = 0; i < n; i++) flat[i] = pts[0];
                return flat;
            }
            float I = len / (n - 1), D = 0f;
            var res = new List<Pt> { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                if (pts[i].Id != pts[i - 1].Id) continue;
                float d = Dist(pts[i - 1], pts[i]);
                if (d > 1e-9f && D + d >= I)
                {
                    float t = (I - D) / d;
                    var q = new Pt(pts[i - 1].X + t * (pts[i].X - pts[i - 1].X),
                                   pts[i - 1].Y + t * (pts[i].Y - pts[i - 1].Y), pts[i].Id);
                    res.Add(q);
                    pts.Insert(i, q);
                    D = 0f;
                }
                else D += d;
            }
            while (res.Count < n) res.Add(pts[pts.Count - 1]);
            if (res.Count > n) res.RemoveRange(n, res.Count - n);
            return res.ToArray();
        }

        static void Scale(Pt[] pts)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            float size = Math.Max(maxX - minX, maxY - minY);
            if (size < 1e-6f) size = 1f;
            for (int i = 0; i < pts.Length; i++)
            {
                pts[i].X = (pts[i].X - minX) / size;
                pts[i].Y = (pts[i].Y - minY) / size;
            }
        }

        static void TranslateToCentroid(Pt[] pts)
        {
            float cx = 0, cy = 0;
            foreach (var p in pts) { cx += p.X; cy += p.Y; }
            cx /= pts.Length; cy /= pts.Length;
            for (int i = 0; i < pts.Length; i++) { pts[i].X -= cx; pts[i].Y -= cy; }
        }

        static float GreedyCloudMatch(Pt[] a, Pt[] b)
        {
            int n = a.Length;
            int step = (int)Math.Floor(Math.Pow(n, 0.5));
            float min = float.MaxValue;
            for (int i = 0; i < n; i += step)
            {
                float d1 = CloudDistance(a, b, i);
                float d2 = CloudDistance(b, a, i);
                min = Math.Min(min, Math.Min(d1, d2));
            }
            return min;
        }

        static float CloudDistance(Pt[] a, Pt[] b, int start)
        {
            int n = a.Length;
            var matched = new bool[n];
            float sum = 0;
            int i = start;
            do
            {
                int index = -1;
                float min = float.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (matched[j]) continue;
                    float d = Dist(a[i], b[j]);
                    if (d < min) { min = d; index = j; }
                }
                matched[index] = true;
                float weight = 1f - ((i - start + n) % n) / (float)n;
                sum += weight * min;
                i = (i + 1) % n;
            } while (i != start);
            return sum;
        }

        static float Dist(Pt a, Pt b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
