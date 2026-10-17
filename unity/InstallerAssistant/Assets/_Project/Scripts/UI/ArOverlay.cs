using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static InstallerAssistant.UI.UiKit;

namespace InstallerAssistant.UI
{
    /// <summary>Visual state of one terminal marker.</summary>
    public enum Mark { Hidden, Reading, Correct, Wrong, Check, Edited, Confirmed }

    /// <summary>
    /// A status disk anchored on one terminal, plus a leader line and a tag with the wire colour.
    /// Lives under a RectTransform that covers the camera image; position = fraction of the image.
    /// </summary>
    public sealed class PinMarker
    {
        public int Id;               // reader: terminal index; controller: pin number
        public string Name;
        public Vector2 Pos;
        public Mark State = Mark.Hidden;
        public int Colour = -1;
        public RectTransform Root, Vis, Tag, Leader, Pulse, IconHolder;
        public Image Disk, Arc, TagSwatch, TagBg;
        public TextMeshProUGUI TagText;

        public const float TagH = 64f, TagFont = 28f;
        /// <summary>Width of the tag, measured from its text (unscaled units).</summary>
        public float Width = 220f;

        public static Color ColourOf(Mark s)
        {
            switch (s)
            {
                case Mark.Wrong: return Bad;
                case Mark.Check: return Warn;
                case Mark.Reading: case Mark.Edited: return Accent;
                default: return Ok;
            }
        }

        public static Icon IconOf(Mark s)
        {
            switch (s)
            {
                case Mark.Wrong: return Icon.Cross;
                case Mark.Check: return Icon.Alert;
                case Mark.Edited: return Icon.Pencil;
                case Mark.Correct: case Mark.Confirmed: return Icon.Check;
                default: return Icon.None;
            }
        }

        public static PinMarker Build(RectTransform parent, int id, string name, Vector2 pos, float markerScale, float textScale, Action onTap)
        {
            var t = new PinMarker { Id = id, Name = name, Pos = pos };
            t.Root = Rect("Terminal " + name, parent);
            t.Root.sizeDelta = Vector2.zero;
            t.MoveTo(pos);

            t.Leader = Img("Leader", t.Root, null, Hex("#FFFFFF", 0.9f), 0).rectTransform;
            var size = Rect("MarkerSize", t.Root);
            size.localScale = Vector3.one * markerScale;
            t.Vis = Rect("Vis", size);
            t.Pulse = Img("Pulse", t.Vis, sRing, Ok, 0).rectTransform;
            t.Pulse.sizeDelta = new Vector2(96, 96);
            var shade = Img("Shade", t.Vis, sSoft, Hex("#000000", 0.35f), 0);
            shade.rectTransform.sizeDelta = new Vector2(110, 110);
            var halo = Img("Ring", t.Vis, sCircle, Surface, 0);
            halo.rectTransform.sizeDelta = new Vector2(76, 76);
            t.Disk = Img("Disk", t.Vis, sCircle, Ok, 0);
            t.Disk.rectTransform.sizeDelta = new Vector2(62, 62);
            t.Arc = Img("Arc", t.Vis, sRing, Accent, 0);
            t.Arc.rectTransform.sizeDelta = new Vector2(70, 70);
            t.Arc.type = Image.Type.Filled; t.Arc.fillMethod = Image.FillMethod.Radial360; t.Arc.fillAmount = 0.3f;
            t.IconHolder = Rect("Icon", t.Disk.rectTransform);
            Stretch(t.IconHolder);
            var hit = Img("Hit", t.Vis, null, new Color(0, 0, 0, 0), 0);
            hit.rectTransform.sizeDelta = new Vector2(TouchTarget, TouchTarget);
            MakeButton(hit, onTap);

            t.Tag = Rect("Tag", t.Root);
            t.Tag.localScale = Vector3.one * textScale;
            t.Tag.sizeDelta = new Vector2(t.Width, TagH);
            var tagShade = Img("Shade", t.Tag, sSoft, Hex("#000000", 0.16f), 0);
            Stretch(tagShade.rectTransform, -14, -18, -14, -8);
            t.TagBg = Img("Bg", t.Tag, sRound, Surface, TagH / 2);
            Stretch(t.TagBg.rectTransform);
            t.TagSwatch = Img("Swatch", t.Tag, sCircle, Color.white, 0);
            Place(t.TagSwatch.rectTransform, new Vector2(0, 0.5f), new Vector2(34, 0), new Vector2(26, 26));
            t.TagText = Txt(t.Tag, "", TagFont, Ink, Face.Semi, TextAlignmentOptions.Left);
            Place(t.TagText.rectTransform, new Vector2(0, 0.5f), new Vector2(58, 0), new Vector2(t.Width - 80, 56), new Vector2(0, 0.5f));
            t.TagText.textWrappingMode = TextWrappingModes.NoWrap;
            MakeButton(t.TagBg, onTap);
            t.Set(Mark.Hidden, -1, null);
            return t;
        }

        public void MoveTo(Vector2 pos)
        {
            Pos = pos;
            Root.anchorMin = Root.anchorMax = new Vector2(pos.x, 1f - pos.y);
            Root.anchoredPosition = Vector2.zero;
        }

        /// <summary>Sets state, wire colour (-1 = no wire) and tag text. Returns true if it became visible.</summary>
        public bool Set(Mark s, int colour, string tag)
        {
            bool wasHidden = State == Mark.Hidden;
            State = s;
            Colour = colour;
            bool shown = s != Mark.Hidden;
            Root.gameObject.SetActive(shown);
            if (!shown) return false;
            bool reading = s == Mark.Reading;
            Arc.gameObject.SetActive(reading);
            Disk.color = reading ? Hex("#FFFFFF", 0.0f) : ColourOf(s);
            DrawIcon(IconHolder, reading ? Icon.None : IconOf(s), 40, s == Mark.Check ? Ink : Surface);
            Tag.gameObject.SetActive(!reading);
            Leader.gameObject.SetActive(!reading);
            Pulse.GetComponent<Image>().color = ColourOf(s);
            TagSwatch.sprite = colour < 0 ? sRing : sCircle;
            TagSwatch.color = colour < 0 ? Ink3 : Wire[Mathf.Clamp(colour, 0, Wire.Length - 1)];
            TagText.text = tag ?? Name;
            // the tag is as wide as its words, so short names take less room on screen
            float textW = Mathf.Ceil(TagText.GetPreferredValues(TagText.text, 2000f, 60f).x);
            Width = 58f + textW + 26f;
            Tag.sizeDelta = new Vector2(Width, TagH);
            TagText.rectTransform.sizeDelta = new Vector2(textW + 4f, 56f);
            TagText.color = s == Mark.Wrong ? Bad : s == Mark.Check ? WarnInk : Ink;
            TagBg.color = s == Mark.Wrong ? BadSoft : s == Mark.Check ? WarnSoft : Surface;
            return wasHidden || !reading;
        }

        public void Animate(float time, float inverseScale)
        {
            if (Root == null || !Root.gameObject.activeSelf) return;
            Root.localScale = new Vector3(inverseScale, inverseScale, 1f);
            if (State == Mark.Reading) Arc.rectTransform.localEulerAngles = new Vector3(0, 0, -time * 360f);
            bool alert = State == Mark.Wrong || State == Mark.Check;
            float k = Mathf.Repeat(time * (alert ? 1.3f : 0.6f) + Id * 0.17f, 1f);
            float s = 1f + k * (alert ? 0.8f : 0.4f);
            Pulse.localScale = new Vector3(s, s, 1f);
            var img = Pulse.GetComponent<Image>();
            var c = img.color;
            c.a = (1f - k) * (State == Mark.Reading ? 0f : alert ? 0.9f : 0.45f);
            img.color = c;
        }
    }

    /// <summary>
    /// Places the tags in screen space so they never overlap, never leave the visible area and never cross
    /// their leader lines (view management, Bell, Feiner &amp; Höllerer 2001).
    ///
    /// A horizontal terminal row gets a row of tags above or below it; a vertical pin block gets a column beside it.
    /// Tags keep the order of their terminals and are packed inside the visible area (the screen minus the top
    /// controls and the bottom sheet). When one row does not fit, the tags split into two staggered tiers; when that
    /// still does not fit, they shrink (down to 70 %). The side flips when the preferred side has no room.
    /// </summary>
    public static class TagLayout
    {
        const float Gap = 12f, MarkerGap = 64f;

        /// <summary>Returns the side the tags went to: +1 right, -1 left, 0 = a row above/below.</summary>
        /// <param name="visible">Free area in canvas coordinates (centred on the canvas pivot).</param>
        public static int Apply(List<PinMarker> marks, RectTransform canvas, Rect visible, bool preferBelow, float markerScale, float textScale)
        {
            var vis = new List<PinMarker>();
            foreach (var m in marks) if (m.Root != null && m.Root.gameObject.activeInHierarchy) vis.Add(m);
            if (vis.Count == 0) return 0;
            int n = vis.Count;
            var pts = new Vector2[n];
            float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;
            for (int i = 0; i < n; i++)
            {
                pts[i] = canvas.InverseTransformPoint(vis[i].Root.position);
                minX = Mathf.Min(minX, pts[i].x); maxX = Mathf.Max(maxX, pts[i].x);
                minY = Mathf.Min(minY, pts[i].y); maxY = Mathf.Max(maxY, pts[i].y);
            }
            bool row = n > 1 && (maxX - minX) >= (maxY - minY);

            // order along the row (left to right) or the column (top to bottom)
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((i, j) => row ? pts[i].x.CompareTo(pts[j].x) : pts[j].y.CompareTo(pts[i].y));

            float scale = textScale;
            var centre = new Vector2[n];
            var size = new Vector2[n];
            int side = 0;

            if (row)
            {
                float avail = visible.width;
                int tiers = 1;
                float need = RowWidth(vis, order, scale, 1, 0);
                if (need > avail)
                {
                    tiers = 2;
                    need = Mathf.Max(RowWidth(vis, order, scale, 2, 0), RowWidth(vis, order, scale, 2, 1));
                    if (need > avail) scale *= Mathf.Max(0.7f, avail / need);
                }
                float h = PinMarker.TagH * scale, tierStep = h + Gap;
                float off = MarkerGap * markerScale + h * 0.5f;
                float roomBelow = (minY - off - h * 0.5f) - visible.yMin - (tiers - 1) * tierStep;
                float roomAbove = visible.yMax - (maxY + off + h * 0.5f) - (tiers - 1) * tierStep;
                bool below = preferBelow ? (roomBelow >= 0 || roomBelow >= roomAbove) : !(roomAbove >= 0 || roomAbove >= roomBelow);
                for (int tier = 0; tier < tiers; tier++)
                {
                    var idx = new List<int>();
                    for (int k = tier; k < n; k += tiers) idx.Add(order[k]);
                    var c = new float[idx.Count]; var w = new float[idx.Count];
                    for (int k = 0; k < idx.Count; k++) { c[k] = pts[idx[k]].x; w[k] = vis[idx[k]].Width * scale; }
                    Pack(c, w, Gap, visible.xMin, visible.xMax);
                    float y = below ? minY - off - tier * tierStep : maxY + off + tier * tierStep;
                    y = below ? Mathf.Max(y, visible.yMin + h * 0.5f + (tiers - 1 - tier) * tierStep)
                              : Mathf.Min(y, visible.yMax - h * 0.5f - (tiers - 1 - tier) * tierStep);
                    for (int k = 0; k < idx.Count; k++) { centre[idx[k]] = new Vector2(c[k], y); size[idx[k]] = new Vector2(w[k], h); }
                }
            }
            else
            {
                float h = PinMarker.TagH * scale;
                float maxW = 0;
                foreach (var m in vis) maxW = Mathf.Max(maxW, m.Width * scale);
                float gap = MarkerGap * markerScale;
                float roomRight = visible.xMax - maxX - gap, roomLeft = minX - visible.xMin - gap;
                bool right = roomRight >= maxW || roomRight >= roomLeft;
                side = right ? 1 : -1;
                // pack along y, bottom to top, inside the visible band
                var ys = new float[n]; var hs = new float[n];
                for (int k = 0; k < n; k++) { int i = order[n - 1 - k]; ys[k] = pts[i].y; hs[k] = h; }
                Pack(ys, hs, Gap, visible.yMin, visible.yMax);
                for (int k = 0; k < n; k++)
                {
                    int i = order[n - 1 - k];
                    float w = vis[i].Width * scale;
                    float x = right ? maxX + gap + w * 0.5f : minX - gap - w * 0.5f;
                    x = Mathf.Clamp(x, visible.xMin + w * 0.5f, visible.xMax - w * 0.5f);
                    centre[i] = new Vector2(x, ys[k]);
                    size[i] = new Vector2(w, h);
                }
            }

            for (int i = 0; i < n; i++)
            {
                var t = vis[i];
                // the marker root is scaled by 1/zoom so tags keep their size; convert canvas units into root units
                float s = t.Root.lossyScale.x / Mathf.Max(1e-5f, canvas.lossyScale.x);
                s = Mathf.Max(1e-5f, s);
                Vector2 off = (centre[i] - pts[i]) / s;
                Vector2 half = size[i] * 0.5f / s;
                t.Tag.localScale = Vector3.one * (size[i].y / PinMarker.TagH / s);
                t.Tag.anchoredPosition = off;
                // leader: from the marker's edge to the nearest point of the tag
                Vector2 near = new Vector2(Mathf.Clamp(0f, off.x - half.x, off.x + half.x), Mathf.Clamp(0f, off.y - half.y, off.y + half.y));
                Vector2 from = near.sqrMagnitude > 1e-4f ? near.normalized * 40f * markerScale : Vector2.zero;
                SetBar(t.Leader, from, near, 3f);
                t.Leader.anchorMin = t.Leader.anchorMax = new Vector2(0.5f, 0.5f);
            }
            return side;
        }

        static float RowWidth(List<PinMarker> vis, List<int> order, float scale, int tiers, int tier)
        {
            float w = 0; int count = 0;
            for (int k = tier; k < order.Count; k += tiers) { w += vis[order[k]].Width * scale; count++; }
            return w + Gap * Mathf.Max(0, count - 1);
        }

        /// <summary>
        /// Moves ordered intervals (centres c, sizes w) apart so none overlap, as close as possible to where they
        /// want to be, and keeps them inside [lo, hi] when they fit.
        /// </summary>
        public static void Pack(float[] c, float[] w, float gap, float lo, float hi)
        {
            int n = c.Length;
            if (n == 0) return;
            var want = (float[])c.Clone();
            for (int i = 1; i < n; i++) c[i] = Mathf.Max(c[i], c[i - 1] + (w[i - 1] + w[i]) * 0.5f + gap);
            // pushing only to one side drifts the group: centre it back on where the tags want to be
            float drift = 0;
            for (int i = 0; i < n; i++) drift += (c[i] - want[i]) / n;
            for (int i = 0; i < n; i++) c[i] -= drift;
            for (int i = 1; i < n; i++) c[i] = Mathf.Max(c[i], c[i - 1] + (w[i - 1] + w[i]) * 0.5f + gap);
            // keep inside the bounds: right edge first, then the left edge wins if both cannot hold
            if (c[n - 1] + w[n - 1] * 0.5f > hi)
            {
                c[n - 1] = hi - w[n - 1] * 0.5f;
                for (int i = n - 2; i >= 0; i--) c[i] = Mathf.Min(c[i], c[i + 1] - (w[i] + w[i + 1]) * 0.5f - gap);
            }
            if (c[0] - w[0] * 0.5f < lo)
            {
                c[0] = lo + w[0] * 0.5f;
                for (int i = 1; i < n; i++) c[i] = Mathf.Max(c[i], c[i - 1] + (w[i - 1] + w[i]) * 0.5f + gap);
            }
        }
    }

    /// <summary>
    /// The repair drawn in place: a curved arrow from the wire's current pin to its target pin
    /// (two heads for a swap), so the fix is shown where the hands are, not only described in text.
    /// </summary>
    public sealed class FixArrow
    {
        const int N = 30;
        public RectTransform Root;
        readonly List<Image> core = new List<Image>();
        readonly List<Image> shade = new List<Image>();
        readonly Image[] heads = new Image[8];
        readonly bool twoHeads;
        public PinMarker From, To;

        FixArrow(bool twoHeads) { this.twoHeads = twoHeads; }

        public static FixArrow Build(RectTransform parent, PinMarker from, PinMarker to, bool twoHeads)
        {
            var a = new FixArrow(twoHeads) { From = from, To = to };
            a.Root = Rect("FixArrow", parent);
            Stretch(a.Root);
            a.Root.SetAsFirstSibling();   // under the markers and their tags
            for (int i = 0; i < N; i++) a.shade.Add(Img("Shade", a.Root, sRound, Hex("#000000", 0.45f), 6));
            for (int i = 0; i < 4; i++) a.heads[i] = Img("HeadShade", a.Root, sRound, Hex("#000000", 0.45f), 6);
            for (int i = 0; i < N; i++) a.core.Add(Img("Core", a.Root, sRound, Surface, 4));
            for (int i = 4; i < 8; i++) a.heads[i] = Img("Head", a.Root, sRound, Surface, 4);
            return a;
        }

        /// <summary>
        /// Recomputes the curve each frame from the two markers. bulgeSide: +1 bends to the right of the
        /// markers' column, -1 to the left (chosen opposite to the tags), 0 = upwards.
        /// </summary>
        public void Update(RectTransform space, int bulgeSide, float time, float inverseScale)
        {
            if (Root == null) return;
            Vector2 a = Local(space, From.Pos), b = Local(space, To.Pos);
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1f) { Root.gameObject.SetActive(false); return; }
            Root.gameObject.SetActive(true);
            Vector2 nrm = new Vector2(-d.y, d.x) / len;
            if (bulgeSide != 0 && Mathf.Sign(nrm.x) != bulgeSide) nrm = -nrm;
            if (bulgeSide == 0 && nrm.y < 0) nrm = -nrm;
            // a quadratic curve peaks at half its control offset: 180 gives a ~90 px arc, readable even between adjacent pins
            Vector2 c = (a + b) * 0.5f + nrm * Mathf.Max(180f * inverseScale, 0.8f * len);
            // start and end just outside the two markers; short arcs leave the marker sideways, so cap the trim
            float r = 40f * inverseScale;
            float t0 = Mathf.Min(0.12f, r / Mathf.Max(len, 1f)), t1 = 1f - t0;
            float thick = 9f * inverseScale, shadeThick = 15f * inverseScale;
            for (int i = 0; i < N; i++)
            {
                float u0 = Mathf.Lerp(t0, t1, i / (float)N), u1 = Mathf.Lerp(t0, t1, (i + 1) / (float)N);
                Vector2 p0 = Q(a, c, b, u0), p1 = Q(a, c, b, u1);
                SetBar(shade[i].rectTransform, p0, p1, shadeThick);
                SetBar(core[i].rectTransform, p0, p1, thick);
                float k = twoHeads ? 0.5f + 0.5f * Mathf.Sin(time * 4f)
                                   : Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * (i / (float)N * 2f - time * 1.2f)));
                core[i].color = Color.Lerp(Hex("#FFFFFF", 0.7f), Surface, k);
            }
            Head(Q(a, c, b, t1), Q(a, c, b, t1) - Q(a, c, b, t1 - 0.04f), 0, inverseScale);
            bool second = twoHeads;
            for (int i = 2; i < 4; i++) { heads[i].gameObject.SetActive(second); heads[i + 4].gameObject.SetActive(second); }
            if (second) Head(Q(a, c, b, t0), Q(a, c, b, t0) - Q(a, c, b, t0 + 0.04f), 2, inverseScale);
        }

        void Head(Vector2 tip, Vector2 dir, int slot, float inv)
        {
            dir.Normalize();
            float L = 28f * inv;
            for (int s = 0; s < 2; s++)
            {
                float ang = (s == 0 ? 28f : -28f) * Mathf.Deg2Rad;
                Vector2 back = new Vector2(-dir.x * Mathf.Cos(ang) + dir.y * Mathf.Sin(ang), -dir.y * Mathf.Cos(ang) - dir.x * Mathf.Sin(ang));
                SetBar(heads[slot + s].rectTransform, tip, tip + back * L, 15f * inv);
                SetBar(heads[slot + s + 4].rectTransform, tip, tip + back * L, 9f * inv);
            }
        }

        static Vector2 Q(Vector2 a, Vector2 c, Vector2 b, float t)
        {
            float u = 1 - t;
            return u * u * a + 2 * u * t * c + t * t * b;
        }

        static Vector2 Local(RectTransform space, Vector2 pos)
        {
            Rect r = space.rect;
            return new Vector2((pos.x - 0.5f) * r.width, (0.5f - pos.y) * r.height);
        }

        public void Destroy()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            Root = null;
        }
    }
}
