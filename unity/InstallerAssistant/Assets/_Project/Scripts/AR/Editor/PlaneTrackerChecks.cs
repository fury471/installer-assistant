using System;
using System.Collections.Generic;

namespace InstallerAssistant.AR.Editor
{
    /// <summary>
    /// Checks of PlaneTracker on a picture made of random blobs that is moved by a known motion at every step
    /// (sampled exactly, no resampling blur): after 40 steps of shift, turn and zoom a point must be where the
    /// known motion puts it, and a picture that suddenly shows something else must be reported as lost, with a
    /// new epoch. No Unity types, so the same file runs outside the Editor. Called from ArGeometryChecks.Run.
    /// </summary>
    public static class PlaneTrackerChecks
    {
        const int W = 180, H = 320;

        /// <summary>Returns one line per check that passed; throws on the first that fails.</summary>
        public static List<string> Run()
        {
            var passed = new List<string>();
            var random = new Random(3);
            var blobs = Blobs(random, 260);
            var tracker = new PlaneTracker();
            double[] truth = PlaneTracker.Identity();
            // per step: 1.3 px right, 0.9 px down, 0.25 degrees, 0.2 % larger, about the middle of the picture
            double angle = 0.25 * Math.PI / 180, scale = 1.002, cx = W / 2.0, cy = H / 2.0;
            double a = scale * Math.Cos(angle), b = scale * Math.Sin(angle);
            double[] step = { a, -b, cx - a * cx + b * cy + 1.3, b, a, cy - b * cx - a * cy + 0.9 };
            for (int t = 0; t <= 40; t++)
            {
                if (t > 0) truth = PlaneTracker.Compose(step, truth);
                Assert(tracker.Step(Render(blobs, truth, random), W, H), "step " + t + " followed");
            }
            double worst = 0;
            foreach (var p in new[] { new[] { 60.0, 100.0 }, new[] { 90.0, 160.0 }, new[] { 120.0, 230.0 } })
            {
                PlaneTracker.Apply(tracker.Cumulative, p[0], p[1], out double x, out double y);
                PlaneTracker.Apply(truth, p[0], p[1], out double tx, out double ty);
                worst = Math.Max(worst, Math.Sqrt((x - tx) * (x - tx) + (y - ty) * (y - ty)));
            }
            Assert(worst < 0.6, "after 40 steps (52 px, 10 degrees, 8 % zoom) a point is within 0.6 px (" + worst.ToString("F2") + ")");
            passed.Add("PASS tracker follows 40 steps of shift, turn and zoom: worst " + worst.ToString("F2") + " px");

            double[] back;
            Assert(PlaneTracker.Invert(tracker.Cumulative, out back), "cumulative motion can be inverted");
            PlaneTracker.Apply(PlaneTracker.Compose(back, tracker.Cumulative), 77, 123, out double ix, out double iy);
            Assert(Math.Abs(ix - 77) < 1e-6 && Math.Abs(iy - 123) < 1e-6, "inverse composes to the identity");
            passed.Add("PASS affine inverse and composition");

            int epoch = tracker.Epoch;
            bool followed = tracker.Step(Render(Blobs(new Random(99), 260), PlaneTracker.Identity(), random), W, H);
            Assert(!followed && tracker.Epoch != epoch, "another scene is reported as lost, with a new epoch");
            passed.Add("PASS a different picture is reported as lost");

            var flat = new byte[W * H];
            for (int i = 0; i < flat.Length; i++) flat[i] = 120;
            var blind = new PlaneTracker();
            blind.Step(flat, W, H);
            Assert(!blind.Step(flat, W, H), "a picture without corners cannot be followed");
            passed.Add("PASS a flat picture is reported as lost");
            return passed;
        }

        static double[][] Blobs(Random random, int count)
        {
            var blobs = new double[count][];
            for (int i = 0; i < count; i++)
                blobs[i] = new[] { random.NextDouble() * (W + 200) - 100, random.NextDouble() * (H + 200) - 100,
                                   2.0 + 5.0 * random.NextDouble(), random.NextDouble() < 0.5 ? -70.0 : 70.0 };
            return blobs;
        }

        /// <summary>The picture after the given motion: pixel p shows the scene point motion^-1 (p).</summary>
        static byte[] Render(double[][] blobs, double[] motion, Random random)
        {
            double[] back;
            PlaneTracker.Invert(motion, out back);
            var img = new byte[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    PlaneTracker.Apply(back, x, y, out double sx, out double sy);
                    double v = 128;
                    foreach (var blob in blobs)
                    {
                        double dx = sx - blob[0], dy = sy - blob[1], r = blob[2];
                        double d2 = dx * dx + dy * dy;
                        if (d2 < 9 * r * r) v += blob[3] * Math.Exp(-d2 / (2 * r * r));
                    }
                    v += 2 * (random.NextDouble() - 0.5);
                    img[y * W + x] = (byte)Math.Max(0, Math.Min(255, v));
                }
            return img;
        }

        static void Assert(bool yes, string name)
        {
            if (!yes) throw new Exception("Plane tracker regression: " + name);
        }
    }
}
