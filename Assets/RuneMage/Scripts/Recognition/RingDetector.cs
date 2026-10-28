using System;
using System.Collections.Generic;

namespace RuneMage.Recognition
{
    public struct RingResult
    {
        public bool IsRing;
        public bool IsClosedLoop;     // a loop, even if too small/oval to be a valid seal
        public float CenterX, CenterY;
        public float Radius;
        public float Neatness;        // 0..1 (roundness and closure)
        public string Reason;         // user-facing hint when the loop is rejected
    }

    /// <summary>Decides whether a single stroke (plane coordinates, metres) is a magic seal ring.</summary>
    public static class RingDetector
    {
        public const float MinRadius = 0.07f;   // 14 cm across: small enough for comfortable wrist drawing
        public const float MaxRadius = 0.55f;

        /// <summary>Algebraic (Kasa) least-squares circle centre. Leaves cx, cy untouched if the fit is degenerate.</summary>
        static void FitCircleCenter(IList<Pt> pts, ref float cx0, ref float cy0)
        {
            double mx = cx0, my = cy0;
            double suu = 0, suv = 0, svv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
            foreach (var p in pts)
            {
                double u = p.X - mx, v = p.Y - my;
                suu += u * u; suv += u * v; svv += v * v;
                suuu += u * u * u; svvv += v * v * v; suvv += u * v * v; svuu += v * u * u;
            }
            double det = suu * svv - suv * suv;
            if (Math.Abs(det) < 1e-12) return;
            double a = 0.5 * (suuu + suvv), b = 0.5 * (svvv + svuu);
            double uc = (a * svv - b * suv) / det;
            double vc = (b * suu - a * suv) / det;
            cx0 = (float)(mx + uc);
            cy0 = (float)(my + vc);
        }

        public static RingResult Analyze(IList<Pt> pts)
        {
            var r = new RingResult { Reason = "Draw one closed circle." };
            if (pts == null || pts.Count < 8) return r;

            float cx = 0, cy = 0;
            foreach (var p in pts) { cx += p.X; cy += p.Y; }
            cx /= pts.Count; cy /= pts.Count;
            FitCircleCenter(pts, ref cx, ref cy);   // an overshoot or gap must not skew the centre

            // Radius statistics around the centroid, and total angular sweep.
            float sum = 0, sumSq = 0, pathLen = 0;
            float sweep = 0, prevAng = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                float dx = pts[i].X - cx, dy = pts[i].Y - cy;
                float rad = (float)Math.Sqrt(dx * dx + dy * dy);
                sum += rad; sumSq += rad * rad;
                float ang = (float)Math.Atan2(dy, dx);
                if (i > 0)
                {
                    float da = ang - prevAng;
                    while (da > Math.PI) da -= (float)(2 * Math.PI);
                    while (da < -Math.PI) da += (float)(2 * Math.PI);
                    sweep += da;
                    float sx = pts[i].X - pts[i - 1].X, sy = pts[i].Y - pts[i - 1].Y;
                    pathLen += (float)Math.Sqrt(sx * sx + sy * sy);
                }
                prevAng = ang;
            }
            float mean = sum / pts.Count;
            float var = Math.Max(0f, sumSq / pts.Count - mean * mean);
            float rel = mean > 1e-5f ? (float)Math.Sqrt(var) / mean : 1f;

            // Percentile radius ratio: ~1.1 for a hand-drawn circle, ~1.33 for a square or diamond.
            var radii = new List<float>(pts.Count);
            foreach (var p in pts)
            {
                float dx = p.X - cx, dy = p.Y - cy;
                radii.Add((float)Math.Sqrt(dx * dx + dy * dy));
            }
            radii.Sort();
            float r10 = radii[(int)(radii.Count * 0.10f)], r90 = radii[Math.Min(radii.Count - 1, (int)(radii.Count * 0.90f))];
            float ratio = r10 > 1e-5f ? r90 / r10 : 9f;

            float gx = pts[0].X - pts[pts.Count - 1].X, gy = pts[0].Y - pts[pts.Count - 1].Y;
            float gap = (float)Math.Sqrt(gx * gx + gy * gy);
            float circumference = (float)(2 * Math.PI * mean);

            bool swept = Math.Abs(sweep) >= 5.2f;                  // ~300 degrees
            bool closed = gap < Math.Max(0.7f * mean, 0.04f);
            bool round = rel < 0.14f && ratio < 1.28f;
            r.CenterX = cx; r.CenterY = cy; r.Radius = mean;
            r.IsClosedLoop = swept && closed;

            if (!swept || !closed)
            {
                r.Reason = "The circle is open. Close the loop.";
                return r;
            }
            if (!round)
            {
                r.Reason = "The circle is too wobbly. Make it rounder.";
                return r;
            }
            if (mean < MinRadius) { r.Reason = "The circle is too small."; return r; }
            if (mean > MaxRadius) { r.Reason = "The circle is too big."; return r; }
            if (pathLen > circumference * 1.6f) { r.Reason = "Draw the circle once."; return r; }

            r.IsRing = true;
            float roundness = 1f - Math.Min(1f, rel / 0.14f);
            float closure = 1f - Math.Min(1f, gap / (0.7f * mean + 1e-4f));
            r.Neatness = 0.65f * roundness + 0.35f * closure;
            r.Reason = null;
            return r;
        }
    }
}
