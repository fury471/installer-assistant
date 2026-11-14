using System;
using System.Collections.Generic;
using UnityEngine;

namespace InstallerAssistant.AR
{
    /// <summary>
    /// One sighting of a terminal: the ray from the camera centre through the point where a reader found it, in the
    /// frame of the marks' anchor, with an optional range along that ray (from the pin spacing of the same frame).
    /// </summary>
    public struct RaySighting
    {
        public Vector3 Origin;          // camera centre
        public Vector3 Direction;       // unit
        public bool HasRange;
        public float Range;             // metres along Direction, from the pin spacing
        public float SigmaLateral;      // metres: how far off the ray the terminal may be (reader error)
        public float SigmaRange;        // metres: uncertainty of Range
        public long Frame;

        public Vector3 PriorPoint { get { return Origin + Direction * Range; } }
    }

    /// <summary>
    /// Where a terminal is, from every sighting of it (package B). Each sighting is a Gaussian around its ray:
    /// tight across the ray (the reader finds the hole to about a quarter of a pitch), loose along it (the depth
    /// from pin spacing assumes the block faces the camera). The terminal is the point of least weighted squared
    /// distance, a linear triangulation over all rays (Hartley and Sturm, 1997; Hartley and Zisserman, 2004,
    /// ch. 12) with the spacing depth as a weak prior, so one view gives the old answer and two views from a
    /// few centimetres apart fix the depth. Plain maths on Vector3, so it runs in an Editor check as it does on
    /// the phone.
    /// </summary>
    public static class AnchorFusion
    {
        /// <summary>The fused point. With one sighting this is its prior point (or a point on its ray).</summary>
        public static Vector3 Fuse(IList<RaySighting> sightings)
        {
            if (sightings == null || sightings.Count == 0) return Vector3.zero;
            // the spacing ranges of one block share their error (the same tilt), so together they count as one
            int priors = 0;
            foreach (var s in sightings) if (s.HasRange) priors++;
            double[,] a = new double[3, 3];
            double[] b = new double[3];
            foreach (var s in sightings)
            {
                Vector3 d = s.Direction.normalized;
                double lat = 1.0 / Sq(Mathf.Max(1e-5f, s.SigmaLateral));
                double rng = s.HasRange ? 1.0 / (Sq(Mathf.Max(1e-5f, s.SigmaRange)) * priors) : 0.0;
                Vector3 mu = s.HasRange ? s.PriorPoint : s.Origin;
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                    {
                        double ddT = d[i] * (double)d[j];
                        double w = lat * ((i == j ? 1.0 : 0.0) - ddT) + rng * ddT;
                        a[i, j] += w;
                        b[i] += w * mu[j];
                    }
            }
            double[] x;
            if (!Solve3(a, b, out x))
            {
                var last = sightings[sightings.Count - 1];          // degenerate: keep the newest prior
                return last.HasRange ? last.PriorPoint : last.Origin + last.Direction * 0.3f;
            }
            return new Vector3((float)x[0], (float)x[1], (float)x[2]);
        }

        /// <summary>
        /// Fuse, then, while three or more sightings remain, drop the one whose ray passes farthest from the point if
        /// that is beyond <paramref name="gate"/> metres, and fuse again: a sighting that slipped through the gate
        /// (a read one pin along the epipolar line looks like a depth change) is outvoted once more views agree.
        /// Removes the dropped sightings from the list.
        /// </summary>
        public static Vector3 FuseRobust(List<RaySighting> sightings, float gate)
        {
            Vector3 x = Fuse(sightings);
            while (sightings.Count >= 3)
            {
                int worst = -1;
                float far = gate;
                for (int i = 0; i < sightings.Count; i++)
                {
                    float d = OffRay(x, sightings[i]);
                    if (d > far) { far = d; worst = i; }
                }
                if (worst < 0) break;
                sightings.RemoveAt(worst);
                x = Fuse(sightings);
            }
            return x;
        }

        /// <summary>Distance of a point from a sighting's ray (metres), the reader-error part of the residual.</summary>
        public static float OffRay(Vector3 point, RaySighting s)
        {
            Vector3 d = s.Direction.normalized;
            Vector3 v = point - s.Origin;
            return (v - d * Vector3.Dot(v, d)).magnitude;
        }

        /// <summary>Range of a point along a sighting's ray (metres).</summary>
        public static float Along(Vector3 point, RaySighting s)
        {
            return Vector3.Dot(point - s.Origin, s.Direction.normalized);
        }

        /// <summary>
        /// Whether a new sighting agrees with the earlier ones: fused together, every ray passes within
        /// <paramref name="gate"/> metres of the point (an epipolar test: a read shifted across the epipolar line
        /// cannot meet the old rays), and the point is not beyond <paramref name="rangeSigmas"/> of the spacing
        /// range (a shift along it would move the point in depth instead). Returns the largest off-ray distance.
        /// </summary>
        public static bool Agrees(List<RaySighting> earlier, RaySighting next, float gate, float rangeSigmas, out float worst, out Vector3 fused)
        {
            var all = new List<RaySighting>(earlier) { next };
            fused = Fuse(all);
            worst = 0f;
            foreach (var s in all) worst = Mathf.Max(worst, OffRay(fused, s));
            bool rangeOk = !next.HasRange || Mathf.Abs(Along(fused, next) - next.Range) <= rangeSigmas * Mathf.Max(1e-4f, next.SigmaRange);
            return worst <= gate && rangeOk;
        }

        /// <summary>Distance between the two camera centres furthest apart (metres): how much the views can triangulate.</summary>
        public static float Baseline(IList<RaySighting> sightings)
        {
            float best = 0f;
            for (int i = 0; i < sightings.Count; i++)
                for (int j = i + 1; j < sightings.Count; j++)
                    best = Mathf.Max(best, (sightings[i].Origin - sightings[j].Origin).magnitude);
            return best;
        }

        /// <summary>
        /// The spacing of a row of terminals in an image (any 2D units): order the points along the row's main
        /// axis and take the median gap between consecutive points. The nearest-neighbour distance used before
        /// is the smaller of two noisy gaps, so it read the spacing short and the depth long (by about 4 % at
        /// 0.08 pitch of reader noise in simulation); a missing pin only adds one long gap, which the median ignores.
        /// </summary>
        public static bool RowSpacing(IList<Vector2> points, out float spacing)
        {
            spacing = 0;
            int n = points.Count;
            if (n < 2) return false;
            Vector2 mean = Vector2.zero;
            foreach (var p in points) mean += p;
            mean /= n;
            double sxx = 0, sxy = 0, syy = 0;
            foreach (var p in points) { var d = p - mean; sxx += d.x * d.x; sxy += d.x * d.y; syy += d.y * d.y; }
            double angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);         // principal axis of the 2 x 2 scatter
            var axis = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            var order = new List<Vector2>(points);
            order.Sort((a, b) => Vector2.Dot(a - mean, axis).CompareTo(Vector2.Dot(b - mean, axis)));
            var gaps = new List<float>();
            for (int i = 1; i < order.Count; i++)
            {
                float g = (order[i] - order[i - 1]).magnitude;
                if (g > 1e-6f) gaps.Add(g);
            }
            if (gaps.Count == 0) return false;
            gaps.Sort();
            spacing = gaps.Count % 2 == 1 ? gaps[gaps.Count / 2] : 0.5f * (gaps[gaps.Count / 2 - 1] + gaps[gaps.Count / 2]);
            return spacing > 1e-6f;
        }

        /// <summary>
        /// The median distance from each point to its nearest neighbour (metres). For a block's fused pins this
        /// must be the pitch: a read that found the row one pin along the epipolar line triangulates the whole row
        /// nearer or farther, and its spacing grows or shrinks with the depth.
        /// </summary>
        public static float MedianNeighbour(IList<Vector3> points)
        {
            var d = new List<float>();
            for (int i = 0; i < points.Count; i++)
            {
                float best = float.MaxValue;
                for (int j = 0; j < points.Count; j++)
                    if (i != j) best = Mathf.Min(best, (points[i] - points[j]).magnitude);
                if (best < float.MaxValue) d.Add(best);
            }
            if (d.Count == 0) return 0f;
            d.Sort();
            return d[d.Count / 2];
        }

        static double Sq(float v) { return (double)v * v; }

        /// <summary>Gaussian elimination with partial pivoting on a 3 x 3 system.</summary>
        static bool Solve3(double[,] a, double[] b, out double[] x)
        {
            x = new double[3];
            var m = new double[3, 4];
            double scale = 0;
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++) { m[i, j] = a[i, j]; scale = Math.Max(scale, Math.Abs(a[i, j])); }
                m[i, 3] = b[i];
            }
            if (scale <= 0) return false;
            for (int c = 0; c < 3; c++)
            {
                int p = c;
                for (int r = c + 1; r < 3; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[p, c])) p = r;
                if (Math.Abs(m[p, c]) < 1e-12 * scale) return false;
                if (p != c) for (int k = 0; k < 4; k++) { double t = m[c, k]; m[c, k] = m[p, k]; m[p, k] = t; }
                for (int r = 0; r < 3; r++)
                {
                    if (r == c) continue;
                    double f = m[r, c] / m[c, c];
                    for (int k = c; k < 4; k++) m[r, k] -= f * m[c, k];
                }
            }
            for (int i = 0; i < 3; i++) x[i] = m[i, 3] / m[i, i];
            return !(double.IsNaN(x[0]) || double.IsNaN(x[1]) || double.IsNaN(x[2]));
        }
    }
}
