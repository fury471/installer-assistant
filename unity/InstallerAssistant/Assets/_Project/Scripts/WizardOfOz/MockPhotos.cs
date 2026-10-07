using UnityEngine;

namespace InstallerAssistant.WizardOfOz
{
    /// <summary>
    /// Draws stand-in "photos" of the reader end and the controller end, so the demo runs
    /// before real photos exist. Positions match the defaults in DemoLayout.
    /// </summary>
    public static class MockPhotos
    {
        public const int W = 1080, H = 1920;

        public const float OffsetY = 346f;

        public static Texture2D Reader(Color[] wireColours)
        {
            var p = new Painter(W, H, 11);
            p.Gradient(new Color(0.40f, 0.42f, 0.45f), new Color(0.27f, 0.29f, 0.31f), 7f);
            p.oy = OffsetY;

            // reader backplate
            p.RoundRect(318, 182, 762, 1012, 54, new Color(0, 0, 0, 0.35f));
            p.RoundRect(330, 170, 750, 1000, 50, new Color(0.80f, 0.81f, 0.83f));
            p.RoundRect(346, 186, 734, 984, 42, new Color(0.85f, 0.86f, 0.88f));
            p.Disk(540, 250, 16, new Color(0.35f, 0.36f, 0.38f));
            p.Disk(540, 930, 16, new Color(0.35f, 0.36f, 0.38f));
            p.RoundRect(470, 620, 610, 640, 8, new Color(0.70f, 0.71f, 0.73f));

            // cable sheath and wires
            p.Line(540, 790, 540, 1990, 30, new Color(0.17f, 0.18f, 0.20f));
            p.Line(540, 790, 540, 1990, 22, new Color(0.22f, 0.23f, 0.25f));
            for (int k = 0; k < 4; k++)
            {
                float x = 432 + 72 * k;
                p.Wire(x, 505, x, 690, 540 + (k - 1.5f) * 14, 812, wireColours[k]);
            }

            // 4-pin terminal block with screws
            p.RoundRect(395, 400, 685, 520, 14, new Color(0.09f, 0.10f, 0.11f));
            p.RoundRect(395, 400, 685, 420, 10, new Color(0.16f, 0.17f, 0.18f));
            for (int k = 0; k < 4; k++) p.Screw(432 + 72 * k, 460);
            for (int k = 0; k < 4; k++) p.RoundRect(420 + 72 * k, 352, 444 + 72 * k, 362, 3, new Color(0.25f, 0.26f, 0.28f));

            p.Label(540, 1000, 101);
            p.Vignette(0.45f);
            return p.ToTexture("Mock reader end");
        }

        public static Texture2D Controller(Color[] wireColours, bool withLabel = true)
        {
            var p = new Painter(W, H, 23);
            p.Gradient(new Color(0.88f, 0.89f, 0.90f), new Color(0.72f, 0.74f, 0.76f), 5f);
            p.oy = OffsetY;

            // controller board
            p.RoundRect(104, 152, 976, 1112, 40, new Color(0, 0, 0, 0.30f));
            p.RoundRect(100, 140, 980, 1100, 36, new Color(0.20f, 0.25f, 0.23f));
            for (int i = 0; i < 9; i++)
                p.RoundRect(140, 180 + i * 100, 940, 183 + i * 100, 1, new Color(0.27f, 0.33f, 0.30f));
            for (int i = 0; i < 8; i++)
                p.RoundRect(150 + i * 104, 180, 153 + i * 104, 1060, 1, new Color(0.27f, 0.33f, 0.30f));

            // upper terminal blocks (decoration)
            TerminalBlock(p, 160, 230, 6);
            TerminalBlock(p, 580, 230, 6);
            p.RoundRect(160, 700, 420, 820, 12, new Color(0.10f, 0.11f, 0.12f));
            p.RoundRect(660, 700, 920, 820, 12, new Color(0.10f, 0.11f, 0.12f));
            p.Disk(890, 640, 10, new Color(0.25f, 0.85f, 0.45f));

            // cable and wires into the reader block
            p.Line(540, 790, 540, 1990, 30, new Color(0.17f, 0.18f, 0.20f));
            p.Line(540, 790, 540, 1990, 22, new Color(0.22f, 0.23f, 0.25f));
            for (int k = 0; k < 4; k++)
            {
                float x = 450 + 60 * k;
                p.Wire(x, 565, x, 700, 540 + (k - 1.5f) * 14, 812, wireColours[k]);
            }

            // reader block: 8 terminals, ours are the middle four
            p.RoundRect(300, 460, 780, 580, 14, new Color(0.08f, 0.09f, 0.10f));
            p.RoundRect(300, 460, 780, 480, 10, new Color(0.15f, 0.16f, 0.17f));
            for (int k = 0; k < 8; k++) p.Screw(330 + 60 * k, 520);
            for (int k = 0; k < 8; k++) p.RoundRect(318 + 60 * k, 418, 342 + 60 * k, 428, 3, new Color(0.85f, 0.87f, 0.86f));

            if (withLabel) p.Label(540, 1000, 101);
            p.Vignette(0.35f);
            return p.ToTexture("Mock controller end");
        }

        static void TerminalBlock(Painter p, int x0, int y0, int n)
        {
            p.RoundRect(x0, y0, x0 + 60 * n, y0 + 110, 12, new Color(0.08f, 0.09f, 0.10f));
            for (int k = 0; k < n; k++) p.Screw(x0 + 30 + 60 * k, y0 + 58);
        }

        /// <summary>Tiny software rasteriser; coordinates have (0,0) at the top-left.</summary>
        class Painter
        {
            readonly int w, h;
            readonly Color[] px;
            readonly System.Random rng;
            /// <summary>Vertical offset added to every shape (moves the whole scene down).</summary>
            public float oy;

            public Painter(int w, int h, int seed)
            {
                this.w = w; this.h = h;
                px = new Color[w * h];
                rng = new System.Random(seed);
            }

            void Blend(int x, int y, Color c, float a)
            {
                if (x < 0 || y < 0 || x >= w || y >= h || a <= 0f) return;
                int i = (h - 1 - y) * w + x;
                a *= c.a;
                Color d = px[i];
                px[i] = new Color(d.r + (c.r - d.r) * a, d.g + (c.g - d.g) * a, d.b + (c.b - d.b) * a, 1f);
            }

            public void Gradient(Color top, Color bottom, float noise)
            {
                for (int y = 0; y < h; y++)
                {
                    Color row = Color.Lerp(top, bottom, y / (float)h);
                    for (int x = 0; x < w; x++)
                    {
                        float n = ((float)rng.NextDouble() - 0.5f) * noise / 255f;
                        px[(h - 1 - y) * w + x] = new Color(row.r + n, row.g + n, row.b + n, 1f);
                    }
                }
            }

            public void RoundRect(float x0, float y0, float x1, float y1, float r, Color c)
            {
                y0 += oy; y1 += oy;
                float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
                float hx = (x1 - x0) * 0.5f, hy = (y1 - y0) * 0.5f;
                r = Mathf.Min(r, Mathf.Min(hx, hy));
                for (int y = (int)y0 - 1; y <= (int)y1 + 1; y++)
                    for (int x = (int)x0 - 1; x <= (int)x1 + 1; x++)
                    {
                        float qx = Mathf.Abs(x + 0.5f - cx) - (hx - r);
                        float qy = Mathf.Abs(y + 0.5f - cy) - (hy - r);
                        float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude
                                  + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
                        Blend(x, y, c, Mathf.Clamp01(0.5f - d));
                    }
            }

            public void Disk(float cx, float cy, float r, Color c)
            {
                cy += oy;
                for (int y = (int)(cy - r - 1); y <= (int)(cy + r + 1); y++)
                    for (int x = (int)(cx - r - 1); x <= (int)(cx + r + 1); x++)
                    {
                        float d = new Vector2(x + 0.5f - cx, y + 0.5f - cy).magnitude - r;
                        Blend(x, y, c, Mathf.Clamp01(0.5f - d));
                    }
            }

            public void Line(float x0, float y0, float x1, float y1, float r, Color c)
            {
                float len = new Vector2(x1 - x0, y1 - y0).magnitude;
                int steps = Mathf.Max(1, (int)(len / 2f));
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    Disk(Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), r, c);
                }
            }

            public void Wire(float x0, float y0, float cx, float cy, float x1, float y1, Color c)
            {
                Color dark = Color.Lerp(c, Color.black, 0.45f);
                Color light = Color.Lerp(c, Color.white, 0.45f);
                Curve(x0, y0, cx, cy, x1, y1, 10f, dark);
                Curve(x0, y0, cx, cy, x1, y1, 8f, c);
                Curve(x0 - 3, y0, cx - 3, cy, x1 - 3, y1, 2.2f, light);
            }

            void Curve(float x0, float y0, float cx, float cy, float x1, float y1, float r, Color c)
            {
                const int n = 160;
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n, u = 1 - t;
                    Disk(u * u * x0 + 2 * u * t * cx + t * t * x1, u * u * y0 + 2 * u * t * cy + t * t * y1, r, c);
                }
            }

            public void Screw(float x, float y)
            {
                Disk(x, y + 2, 21, new Color(0, 0, 0, 0.5f));
                Disk(x, y, 20, new Color(0.62f, 0.64f, 0.66f));
                Disk(x - 4, y - 4, 13, new Color(0.80f, 0.82f, 0.84f));
                Line(x - 11, y + 11, x + 11, y - 11, 2.5f, new Color(0.25f, 0.26f, 0.28f));
            }

            /// <summary>White cable label with a QR-like pattern, centred on (cx, cy).</summary>
            public void Label(float cx, float cy, int seed)
            {
                RoundRect(cx - 114, cy - 96, cx + 114, cy + 108, 14, new Color(0, 0, 0, 0.35f));
                RoundRect(cx - 110, cy - 100, cx + 110, cy + 100, 12, new Color(0.97f, 0.97f, 0.96f));
                const int n = 21, m = 8;
                float ox = cx - n * m * 0.5f, oy = cy - n * m * 0.5f;
                var r = new System.Random(seed);
                Color ink = new Color(0.08f, 0.08f, 0.09f);
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        bool on;
                        int f = Finder(i, j, n);
                        if (f >= 0) on = f == 1;
                        else on = r.NextDouble() < 0.47;
                        if (on) RoundRect(ox + i * m, oy + j * m, ox + (i + 1) * m, oy + (j + 1) * m, 0, ink);
                    }
            }

            static int Finder(int i, int j, int n)
            {
                int[,] corners = { { 0, 0 }, { n - 7, 0 }, { 0, n - 7 } };
                for (int k = 0; k < 3; k++)
                {
                    int a = i - corners[k, 0], b = j - corners[k, 1];
                    if (a >= -1 && a <= 7 && b >= -1 && b <= 7)
                    {
                        if (a < 0 || a > 6 || b < 0 || b > 6) return 0;
                        int ring = Mathf.Min(Mathf.Min(a, b), Mathf.Min(6 - a, 6 - b));
                        return ring == 1 ? 0 : 1;
                    }
                }
                return -1;
            }

            public void Vignette(float strength)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x - w * 0.5f) / (w * 0.5f), dy = (y - h * 0.45f) / (h * 0.5f);
                        float v = Mathf.Clamp01((dx * dx + dy * dy - 0.35f) * 0.8f) * strength;
                        int i = (h - 1 - y) * w + x;
                        px[i] = Color.Lerp(px[i], Color.black, v);
                    }
            }

            public Texture2D ToTexture(string name)
            {
                var t = new Texture2D(w, h, TextureFormat.RGB24, false) { name = name, wrapMode = TextureWrapMode.Clamp };
                t.SetPixels(px);
                t.Apply(false, false);
                return t;
            }
        }
    }
}
