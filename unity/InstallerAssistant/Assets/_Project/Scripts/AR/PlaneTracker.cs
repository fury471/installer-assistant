using System;
using System.Collections.Generic;

namespace InstallerAssistant.AR
{
    /// <summary>
    /// Follows the picture of a flat scene from one small grey camera image to the next (package B, the webcam
    /// view, where there is no AR pose): where a point of an earlier image is now. Used to keep the marks on the
    /// terminals between two reads, which are seconds apart.
    ///
    /// Each step: corners of the previous image (the smaller eigenvalue of the gradient matrix, one per grid cell;
    /// Shi and Tomasi, 1994) are followed into the new image with the pyramidal Lucas-Kanade tracker (Lucas and
    /// Kanade, 1981; Bouguet, 2001), and an affine motion is fitted to them with RANSAC (Fischler and Bolles, 1981)
    /// and least squares on the inliers. Affine, not a homography: between two video frames the perspective part
    /// is far below the tracking noise, and six parameters from points in a small area stay steady where eight
    /// wobble. The steps are multiplied into Cumulative. The error of that product grows with time, so the
    /// reads, which come every few seconds, put the marks back; when a step fails (blur, the scene left the
    /// image) Epoch changes and positions from before it must not be carried on: the view hides them.
    ///
    /// Plain arrays, no Unity types: the same code runs in a test without the Editor. Images are grey bytes, row 0
    /// at the top. Motions are 2 x 3 affine maps [a b tx; c d ty] in pixels of the image given to Step.
    /// </summary>
    public sealed class PlaneTracker
    {
        public const int Levels = 3;
        const int Radius = 4;                 // 9 x 9 window
        const int Iterations = 10;
        const int Cell = 16;                  // one corner per cell of this size
        const int MaxPoints = 110;            // enough for a 6-parameter fit with outliers; time grows with every point
        const int DetectEvery = 8;            // steps between searches for new corners (unless nearly none are left)
        const int MinInliers = 12;
        const float MinInlierShare = 0.4f;
        const float InlierPx = 0.75f;
        const float MaxResidual = 18f;        // mean absolute grey difference of a followed window

        /// <summary>Changes whenever tracking was lost: positions from an earlier epoch cannot be carried forward.</summary>
        public int Epoch { get; private set; }
        /// <summary>The motion from the first image of this epoch to the latest one.</summary>
        public double[] Cumulative { get; private set; } = Identity();
        /// <summary>The motion of the last step (previous image to latest), identity after a loss.</summary>
        public double[] Last { get; private set; } = Identity();
        public int Inliers { get; private set; }
        public int Tracked { get; private set; }

        // Two pyramids (grey and its two gradients per level) that swap roles each step, and the window buffers:
        // allocated once, so a step creates no garbage for the collector to stop the frame for.
        float[][] prev, next, prevDx, prevDy, nextDx, nextDy;
        float[] scratch;
        int[] pw, ph;
        bool started;
        int sinceDetect;
        readonly float[] win = new float[(2 * Radius + 1) * (2 * Radius + 1)], wix = new float[(2 * Radius + 1) * (2 * Radius + 1)],
                         wiy = new float[(2 * Radius + 1) * (2 * Radius + 1)], other = new float[(2 * Radius + 1) * (2 * Radius + 1)];
        readonly List<float> ax = new List<float>(MaxPoints), ay = new List<float>(MaxPoints), bx = new List<float>(MaxPoints), by = new List<float>(MaxPoints);
        bool[] inlierBuffer = new bool[MaxPoints];
        readonly List<float> px = new List<float>(), py = new List<float>();
        float fx0, fy0, fx1 = 1, fy1 = 1;      // where corners are looked for (normalised), around the marks
        readonly Random random = new Random(1);
        public static double[] Identity() { return new double[] { 1, 0, 0, 0, 1, 0 }; }

        public void Reset()
        {
            started = false;
            px.Clear(); py.Clear();
            Cumulative = Identity();
            Last = Identity();
            Epoch++;
        }

        /// <summary>Look for corners only in this part of the image (normalised, top-left origin): around the marks,
        /// so the motion fitted is the board's and not the table's.</summary>
        public void Focus(float x0, float y0, float x1, float y1)
        {
            fx0 = Math.Max(0, Math.Min(x0, x1)); fy0 = Math.Max(0, Math.Min(y0, y1));
            fx1 = Math.Min(1, Math.Max(x0, x1)); fy1 = Math.Min(1, Math.Max(y0, y1));
        }

        /// <summary>Takes the next image. False when the motion from the previous image could not be found
        /// (then Epoch has changed and Cumulative starts again from this image).</summary>
        public bool Step(byte[] gray, int width, int height)
        {
            if (pw == null || pw[0] != width || ph[0] != height) { Allocate(width, height); started = false; }
            Build(gray, next, nextDx, nextDy);
            if (!started)
            {
                Start();
                return true;
            }
            // new corners when few are left, but not at every step: looking for them costs as much as following them
            sinceDetect++;
            if (px.Count < 2 * MinInliers || (px.Count < MaxPoints * 0.6f && sinceDetect >= DetectEvery))
                Detect(prev[0], prevDx[0], prevDy[0], pw[0], ph[0]);

            int n = px.Count;
            ax.Clear(); ay.Clear(); bx.Clear(); by.Clear();
            for (int i = 0; i < n; i++)
            {
                if (Follow(px[i], py[i], out float qx, out float qy))
                {
                    ax.Add(px[i]); ay.Add(py[i]); bx.Add(qx); by.Add(qy);
                }
            }
            Tracked = ax.Count;
            bool[] inlier;
            double[] motion;
            if (!FitAffine(ax, ay, bx, by, out motion, out inlier) || !Plausible(motion))
            {
                Inliers = 0;
                Start();
                Epoch++;
                return false;
            }
            px.Clear(); py.Clear();
            int kept = 0;
            for (int i = 0; i < ax.Count; i++)
                if (inlier[i] && bx[i] >= Radius + 1 && by[i] >= Radius + 1 && bx[i] < width - Radius - 2 && by[i] < height - Radius - 2)
                { px.Add(bx[i]); py.Add(by[i]); kept++; }
            Inliers = kept;
            Last = motion;
            Cumulative = Compose(motion, Cumulative);
            Swap();
            return true;
        }

        void Allocate(int width, int height)
        {
            pw = new int[Levels]; ph = new int[Levels];
            prev = new float[Levels][]; next = new float[Levels][];
            prevDx = new float[Levels][]; prevDy = new float[Levels][]; nextDx = new float[Levels][]; nextDy = new float[Levels][];
            for (int l = 0, w = width, h = height; l < Levels; l++, w /= 2, h /= 2)
            {
                pw[l] = w; ph[l] = h;
                prev[l] = new float[w * h]; next[l] = new float[w * h];
                prevDx[l] = new float[w * h]; prevDy[l] = new float[w * h]; nextDx[l] = new float[w * h]; nextDy[l] = new float[w * h];
            }
            scratch = new float[width * height];
        }

        void Swap()
        {
            var t = prev; prev = next; next = t;
            t = prevDx; prevDx = nextDx; nextDx = t;
            t = prevDy; prevDy = nextDy; nextDy = t;
        }

        /// <summary>The image just built becomes the first of a new epoch.</summary>
        void Start()
        {
            Swap();
            started = true;
            px.Clear(); py.Clear();
            Cumulative = Identity();
            Last = Identity();
            Detect(prev[0], prevDx[0], prevDy[0], pw[0], ph[0]);
        }

        // ------------------------------------------------------------------ affine maps

        /// <summary>first, then second: the map p -> second(first(p)).</summary>
        public static double[] Compose(double[] second, double[] first)
        {
            return new[]
            {
                second[0] * first[0] + second[1] * first[3], second[0] * first[1] + second[1] * first[4], second[0] * first[2] + second[1] * first[5] + second[2],
                second[3] * first[0] + second[4] * first[3], second[3] * first[1] + second[4] * first[4], second[3] * first[2] + second[4] * first[5] + second[5],
            };
        }

        public static bool Invert(double[] m, out double[] inverse)
        {
            double det = m[0] * m[4] - m[1] * m[3];
            inverse = Identity();
            if (Math.Abs(det) < 1e-9) return false;
            double a = m[4] / det, b = -m[1] / det, c = -m[3] / det, d = m[0] / det;
            inverse = new[] { a, b, -(a * m[2] + b * m[5]), c, d, -(c * m[2] + d * m[5]) };
            return true;
        }

        public static void Apply(double[] m, double x, double y, out double ox, out double oy)
        {
            ox = m[0] * x + m[1] * y + m[2];
            oy = m[3] * x + m[4] * y + m[5];
        }

        /// <summary>A step between two video frames is close to the identity: a fit that turns, grows or shears
        /// the picture by much is a wrong fit, not a motion.</summary>
        static bool Plausible(double[] m)
        {
            double det = m[0] * m[4] - m[1] * m[3];
            return det > 0.6 && det < 1.6 && Math.Abs(m[0] - 1) < 0.35 && Math.Abs(m[4] - 1) < 0.35 && Math.Abs(m[1]) < 0.35 && Math.Abs(m[3]) < 0.35;
        }

        // ------------------------------------------------------------------ images

        /// <summary>Fills a pyramid from the grey image: each level smoothed (3 x 3 binomial, the tracker's gradients
        /// need a smooth image), halved for the next, and its two gradient images (central differences), which the
        /// tracker would otherwise recompute for every window.</summary>
        void Build(byte[] gray, float[][] levels, float[][] dx, float[][] dy)
        {
            int w = pw[0], h = ph[0];
            var first = levels[0];
            for (int y = 0; y < h; y++)                        // rows, then columns, through the scratch buffer
                for (int x = 0, i = y * w; x < w; x++, i++)
                    scratch[i] = 0.25f * gray[x > 0 ? i - 1 : i] + 0.5f * gray[i] + 0.25f * gray[x < w - 1 ? i + 1 : i];
            for (int y = 0; y < h; y++)
                for (int x = 0, i = y * w; x < w; x++, i++)
                    first[i] = 0.25f * scratch[y > 0 ? i - w : i] + 0.5f * scratch[i] + 0.25f * scratch[y < h - 1 ? i + w : i];
            for (int l = 1; l < Levels; l++)
            {
                int sw = pw[l - 1], dw = pw[l], dh = ph[l];
                var src = levels[l - 1]; var dst = levels[l];
                for (int y = 0; y < dh; y++)
                    for (int x = 0; x < dw; x++)
                    {
                        int i = 2 * y * sw + 2 * x;
                        dst[y * dw + x] = 0.25f * (src[i] + src[i + 1] + src[i + sw] + src[i + sw + 1]);
                    }
            }
            for (int l = 0; l < Levels; l++)
            {
                int lw = pw[l], lh = ph[l];
                var img = levels[l]; var gx = dx[l]; var gy = dy[l];
                for (int y = 0; y < lh; y++)
                    for (int x = 0, i = y * lw; x < lw; x++, i++)
                    {
                        gx[i] = 0.5f * (img[x < lw - 1 ? i + 1 : i] - img[x > 0 ? i - 1 : i]);
                        gy[i] = 0.5f * (img[y < lh - 1 ? i + lw : i] - img[y > 0 ? i - lw : i]);
                    }
            }
        }

        static float At(float[] img, int w, int h, float x, float y)
        {
            if (x < 0) x = 0; else if (x > w - 1.001f) x = w - 1.001f;
            if (y < 0) y = 0; else if (y > h - 1.001f) y = h - 1.001f;
            int xi = (int)x, yi = (int)y;
            float fx = x - xi, fy = y - yi;
            int i = yi * w + xi;
            return (1 - fy) * ((1 - fx) * img[i] + fx * img[i + 1]) + fy * ((1 - fx) * img[i + w] + fx * img[i + w + 1]);
        }

        // ------------------------------------------------------------------ corners

        /// <summary>Adds the strongest corner of every grid cell that has none yet (inside the focus area).</summary>
        void Detect(float[] img, float[] dxImg, float[] dyImg, int w, int h)
        {
            int cols = w / Cell, rows = h / Cell;
            if (cols < 1 || rows < 1) return;
            var taken = new bool[cols * rows];
            for (int i = 0; i < px.Count; i++)
            {
                int cx = (int)(px[i] / Cell), cy = (int)(py[i] / Cell);
                if (cx >= 0 && cy >= 0 && cx < cols && cy < rows) taken[cy * cols + cx] = true;
            }
            int margin = Radius + 2;
            var best = new float[cols * rows];
            var bestX = new int[cols * rows]; var bestY = new int[cols * rows];
            float strongest = 0;
            int x0 = Math.Max(margin, (int)(fx0 * w)), x1 = Math.Min(w - margin, (int)(fx1 * w));
            int y0 = Math.Max(margin, (int)(fy0 * h)), y1 = Math.Min(h - margin, (int)(fy1 * h));
            sinceDetect = 0;
            for (int y = y0; y < y1; y += 2)                     // every second pixel: corners are wider than that
                for (int x = x0; x < x1; x += 2)
                {
                    int cell = (y / Cell) * cols + x / Cell;
                    if (y / Cell >= rows || x / Cell >= cols || taken[cell]) continue;
                    float gxx = 0, gxy = 0, gyy = 0;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int i = (y + dy) * w + x + dx;
                            float ix = dxImg[i], iy = dyImg[i];
                            gxx += ix * ix; gxy += ix * iy; gyy += iy * iy;
                        }
                    float half = 0.5f * (gxx + gyy);
                    float eig = half - (float)Math.Sqrt(Math.Max(0, half * half - (gxx * gyy - gxy * gxy)));
                    if (eig > best[cell]) { best[cell] = eig; bestX[cell] = x; bestY[cell] = y; }
                    if (eig > strongest) strongest = eig;
                }
            float floor = Math.Max(4f, 0.02f * strongest);       // flat cells (a table top) give no corner
            for (int c = 0; c < best.Length && px.Count < MaxPoints; c++)
                if (best[c] > floor) { px.Add(bestX[c]); py.Add(bestY[c]); }
        }

        // ------------------------------------------------------------------ Lucas-Kanade

        bool Follow(float x, float y, out float qx, out float qy)
        {
            float gx = 0, gy = 0;                                 // the guess, carried down the pyramid
            int side = 2 * Radius + 1;
            qx = x; qy = y;
            for (int l = Levels - 1; l >= 0; l--)
            {
                float s = 1f / (1 << l);
                float cx = x * s, cy = y * s;
                int w = pw[l], h = ph[l];
                var A = prev[l]; var Ax = prevDx[l]; var Ay = prevDy[l]; var B = next[l];
                double gxx = 0, gxy = 0, gyy = 0;
                Window(A, w, h, cx, cy, win);
                Window(Ax, w, h, cx, cy, wix);
                Window(Ay, w, h, cx, cy, wiy);
                for (int k = 0; k < win.Length; k++) { gxx += wix[k] * wix[k]; gxy += wix[k] * wiy[k]; gyy += wiy[k] * wiy[k]; }
                double det = gxx * gyy - gxy * gxy;
                if (det < 1e-3) { if (l == 0) return false; gx *= 2; gy *= 2; continue; }
                float vx = 0, vy = 0;
                for (int it = 0; it < Iterations; it++)
                {
                    double bxs = 0, bys = 0;
                    Window(B, w, h, cx + gx + vx, cy + gy + vy, other);
                    for (int k = 0; k < win.Length; k++)
                    {
                        float d = win[k] - other[k];
                        bxs += d * wix[k]; bys += d * wiy[k];
                    }
                    float ex = (float)((gyy * bxs - gxy * bys) / det), ey = (float)((gxx * bys - gxy * bxs) / det);
                    vx += ex; vy += ey;
                    if (ex * ex + ey * ey < 0.0009f) break;
                }
                if (l == 0)
                {
                    qx = x + gx + vx; qy = y + gy + vy;
                    if (qx < 1 || qy < 1 || qx > w - 2 || qy > h - 2) return false;
                    double residual = 0;
                    Window(B, w, h, qx, qy, other);
                    for (int k = 0; k < win.Length; k++) residual += Math.Abs(win[k] - other[k]);
                    return residual / (side * side) < MaxResidual;
                }
                gx = 2 * (gx + vx); gy = 2 * (gy + vy);
            }
            return false;
        }

        /// <summary>The window around (cx, cy), bilinearly sampled. All its samples share one sub-pixel offset, so
        /// the four weights are computed once and the rows are read straight from the array; only a window that
        /// touches the border goes the slow, clamped way.</summary>
        static void Window(float[] img, int w, int h, float cx, float cy, float[] into)
        {
            int xi = (int)Math.Floor(cx), yi = (int)Math.Floor(cy);
            if (xi - Radius < 0 || yi - Radius < 0 || xi + Radius + 1 >= w || yi + Radius + 1 >= h)
            {
                for (int dy = -Radius, k = 0; dy <= Radius; dy++)
                    for (int dx = -Radius; dx <= Radius; dx++, k++) into[k] = At(img, w, h, cx + dx, cy + dy);
                return;
            }
            float fx = cx - xi, fy = cy - yi;
            float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
            for (int dy = -Radius, k = 0; dy <= Radius; dy++)
            {
                int i = (yi + dy) * w + xi - Radius;
                for (int dx = 0; dx <= 2 * Radius; dx++, k++, i++)
                    into[k] = w00 * img[i] + w10 * img[i + 1] + w01 * img[i + w] + w11 * img[i + w + 1];
            }
        }

        // ------------------------------------------------------------------ robust affine fit

        bool FitAffine(List<float> ax, List<float> ay, List<float> bx, List<float> by, out double[] motion, out bool[] inlier)
        {
            int n = ax.Count;
            motion = Identity();
            if (inlierBuffer.Length < n) inlierBuffer = new bool[n];
            inlier = inlierBuffer;
            if (n < MinInliers) return false;
            int bestCount = 0;
            double[] best = null;
            var m = new double[6];
            for (int trial = 0; trial < 120; trial++)
            {
                int i = random.Next(n), j = random.Next(n), k = random.Next(n);
                if (i == j || j == k || i == k) continue;
                if (!Affine3(ax[i], ay[i], ax[j], ay[j], ax[k], ay[k], bx[i], by[i], bx[j], by[j], bx[k], by[k], m)) continue;
                int count = 0;
                for (int p = 0; p < n; p++)
                {
                    double ex = m[0] * ax[p] + m[1] * ay[p] + m[2] - bx[p], ey = m[3] * ax[p] + m[4] * ay[p] + m[5] - by[p];
                    if (ex * ex + ey * ey < InlierPx * InlierPx) count++;
                }
                if (count > bestCount) { bestCount = count; best = (double[])m.Clone(); if (count > 0.9 * n) break; }   // nearly all agree: no better sample to find
            }
            if (best == null || bestCount < MinInliers || bestCount < MinInlierShare * n) return false;
            // least squares on the inliers, twice (the second round with the refined model's inliers)
            for (int round = 0; round < 2; round++)
            {
                double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sn = 0, sxu = 0, syu = 0, su = 0, sxv = 0, syv = 0, sv = 0;
                int count = 0;
                for (int p = 0; p < n; p++)
                {
                    double ex = best[0] * ax[p] + best[1] * ay[p] + best[2] - bx[p], ey = best[3] * ax[p] + best[4] * ay[p] + best[5] - by[p];
                    inlier[p] = ex * ex + ey * ey < InlierPx * InlierPx;
                    if (!inlier[p]) continue;
                    count++;
                    double x = ax[p], y = ay[p], u = bx[p], v = by[p];
                    sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y; sn += 1;
                    sxu += x * u; syu += y * u; su += u; sxv += x * v; syv += y * v; sv += v;
                }
                if (count < MinInliers) return false;
                double[] r1, r2;
                if (!Solve3(sxx, sxy, sx, sxy, syy, sy, sx, sy, sn, sxu, syu, su, out r1) ||
                    !Solve3(sxx, sxy, sx, sxy, syy, sy, sx, sy, sn, sxv, syv, sv, out r2)) return false;
                best = new[] { r1[0], r1[1], r1[2], r2[0], r2[1], r2[2] };
            }
            motion = best;
            return true;
        }

        static bool Affine3(double x1, double y1, double x2, double y2, double x3, double y3,
                            double u1, double v1, double u2, double v2, double u3, double v3, double[] m)
        {
            double[] r1, r2;
            if (!Solve3(x1, y1, 1, x2, y2, 1, x3, y3, 1, u1, u2, u3, out r1) || !Solve3(x1, y1, 1, x2, y2, 1, x3, y3, 1, v1, v2, v3, out r2)) return false;
            m[0] = r1[0]; m[1] = r1[1]; m[2] = r1[2]; m[3] = r2[0]; m[4] = r2[1]; m[5] = r2[2];
            return true;
        }

        /// <summary>Cramer's rule on a 3 x 3 system (rows a, b, c).</summary>
        static bool Solve3(double a1, double a2, double a3, double b1, double b2, double b3, double c1, double c2, double c3,
                           double r1, double r2, double r3, out double[] x)
        {
            x = null;
            double det = a1 * (b2 * c3 - b3 * c2) - a2 * (b1 * c3 - b3 * c1) + a3 * (b1 * c2 - b2 * c1);
            double scale = Math.Abs(a1) + Math.Abs(b2) + Math.Abs(c3) + 1e-12;
            if (Math.Abs(det) < 1e-9 * scale * scale * scale) return false;
            x = new[]
            {
                (r1 * (b2 * c3 - b3 * c2) - a2 * (r2 * c3 - b3 * r3) + a3 * (r2 * c2 - b2 * r3)) / det,
                (a1 * (r2 * c3 - b3 * r3) - r1 * (b1 * c3 - b3 * c1) + a3 * (b1 * r3 - r2 * c1)) / det,
                (a1 * (b2 * r3 - r2 * c2) - a2 * (b1 * r3 - r2 * c1) + r1 * (b1 * c2 - b2 * c1)) / det,
            };
            return true;
        }
    }
}
