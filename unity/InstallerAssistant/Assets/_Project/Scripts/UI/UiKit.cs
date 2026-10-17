using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InstallerAssistant.UI
{
    /// <summary>Icon drawn from simple strokes. Shape carries the meaning; colour only reinforces it.</summary>
    public enum Icon { None, Check, Cross, Alert, Pencil, ChevronLeft, ChevronRight, Jobs, Reports, Settings, Info, Camera }

    /// <summary>Typeface roles. One family (Inter): Regular for reading, SemiBold for titles, emphasis and controls.</summary>
    public enum Face { Regular, Semi }

    /// <summary>
    /// Design system of the Installer Assistant: a white, content-first theme in the style of news and data apps.
    /// No borders around content: grouping comes from white space, light grey blocks and thin dividers; one blue
    /// for actions and selection; a single typeface (Inter, Regular + SemiBold) with bold headlines.
    /// Code-only uGUI + TextMesh Pro, reference canvas 1080 x 2340 portrait (1 dp is about 2.6 px).
    ///
    /// Colour has a job, never decoration: slate ink and greys for content, one blue for actions, brand and
    /// progress, and three status hues that always come with an icon and a word (WCAG 2.2, SC 1.4.1).
    /// Every text colour meets WCAG AA (4.5:1) on Page, Surface and its own tint; see docs/blueprint-demo.md.
    /// </summary>
    public static class UiKit
    {
        // ---------------------------------------------------------------- palette

        public static readonly Color Page = Hex("#FFFFFF");        // white page: content first, no frames
        public static readonly Color Surface = Hex("#FFFFFF");     // sheets, pills, tags
        public static readonly Color Panel = Hex("#F6F7F9");       // grouped blocks on the white page
        public static readonly Color Sunken = Hex("#EEF0F3");      // tracks, secondary buttons, segmented control
        public static readonly Color Hairline = Hex("#E5E7EB");    // 2 px dividers
        public static readonly Color Ink = Hex("#111827");         // headlines and text (17.7:1 on white)
        public static readonly Color Ink2 = Hex("#4B5563");        // secondary text (7.6:1)
        public static readonly Color Ink3 = Hex("#6B7280");        // captions, times (4.8:1 on white, 4.5:1 on Panel)
        public static readonly Color Accent = Hex("#2B63E6");      // blue: primary action, selection, progress (5.2:1)
        public static readonly Color AccentSoft = Hex("#EBF1FE");  // selected pill
        public static readonly Color Ok = Hex("#15803D");          // green (5.0:1)
        public static readonly Color OkSoft = Hex("#E8F6EC");
        public static readonly Color Bad = Hex("#C8281E");         // red (5.6:1)
        public static readonly Color BadSoft = Hex("#FDEDEC");
        public static readonly Color Warn = Hex("#F59E0B");        // amber fill, ink on top
        public static readonly Color WarnInk = Hex("#B45309");     // amber text (5.0:1)
        public static readonly Color WarnSoft = Hex("#FFF4DE");
        public static readonly Color OnColour = Hex("#FFFFFF");    // text and icons on Accent, Ok and Bad fills
        public static readonly Color Scrim = Hex("#111827", 0.40f);

        /// <summary>Wire insulation colours, codes 0-7 as in InstallerAssistant.Verification.WireColours.</summary>
        public static readonly Color[] Wire =
        {
            Hex("#E53935"), Hex("#1E1F22"), Hex("#2E9E4F"), Hex("#F2C230"),
            Hex("#F1F1EE"), Hex("#7B4B2A"), Hex("#2F6FDB"), Hex("#F08A24")
        };

        // ---------------------------------------------------------------- type scale (px on the 1080-wide canvas)

        public const float DisplaySize = 72;    // semibold, page heroes
        public const float Title = 54;      // semibold, page and sheet titles
        public const float Head = 40;       // semibold, card titles
        public const float Body = 38;       // about 14.5 sp
        public const float Small = 33;      // secondary lines
        public const float Caption = 28;    // labels, chips
        public const float Overline = 25;   // section labels in capitals

        /// <summary>Minimum touch target: 48 dp is about 126 px.</summary>
        public const float TouchTarget = 128;
        public const float Gutter = 48;

        // ---------------------------------------------------------------- fonts

        public static TMP_FontAsset FontRegular, FontSemi;

        /// <summary>Creates dynamic font assets from the imported TTFs once. Missing fonts fall back to the TMP default.</summary>
        public static void LoadFonts(Font regular, Font semi)
        {
            if (FontRegular == null && regular != null) FontRegular = TMP_FontAsset.CreateFontAsset(regular);
            if (FontSemi == null && semi != null) FontSemi = TMP_FontAsset.CreateFontAsset(semi);
            var fallback = FontRegular != null ? FontRegular : TMP_Settings.defaultFontAsset;
            if (FontSemi != null && fallback != null && FontSemi != fallback) FontSemi.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
        }

        // ---------------------------------------------------------------- sprites

        public static Sprite sRound, sCircle, sRing, sSoft, sFadeUp, sFadeDown;

        public static void MakeSprites()
        {
            if (sRound != null) return;
            sRound = MakeSprite(68, 33, (x, y) => Mathf.Clamp01(0.5f - SdfDist(x, y, 68, 68, 32, 0)), "Round");
            sCircle = MakeSprite(128, 0, (x, y) => Mathf.Clamp01(0.5f - (new Vector2(x - 64, y - 64).magnitude - 62)), "Circle");
            sRing = MakeSprite(128, 0, (x, y) =>
            {
                float d = new Vector2(x - 64, y - 64).magnitude;
                return Mathf.Clamp01(0.5f - (d - 62)) * Mathf.Clamp01(d - 55 + 0.5f);
            }, "Ring");
            sSoft = MakeSprite(128, 0, (x, y) =>
            {
                float d = SdfDist(x, y, 128, 128, 40, 30);
                return Mathf.Clamp01(1f - Mathf.SmoothStep(0f, 1f, (d + 28f) / 28f));
            }, "Soft");
            sFadeUp = MakeSprite(64, 0, (x, y) => Mathf.SmoothStep(1, 0, y / 63f), "FadeUp");
            sFadeDown = MakeSprite(64, 0, (x, y) => Mathf.SmoothStep(0, 1, y / 63f), "FadeDown");
        }

        static float SdfDist(float x, float y, float w, float h, float r, float inset)
        {
            float qx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - inset - r);
            float qy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - inset - r);
            return new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        static Sprite MakeSprite(int size, int border, Func<float, float, float> alpha, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = name };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = new Color(1, 1, 1, alpha(x + 0.5f, y + 0.5f));
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        // ---------------------------------------------------------------- primitives

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Img(string name, Transform parent, Sprite sprite, Color color, float radius)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            if (sprite == sRound && radius > 0)
            {
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 32f / radius;
            }
            return img;
        }

        public static TextMeshProUGUI Txt(Transform parent, string text, float size, Color color, Face face = Face.Regular,
            TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            var asset = face == Face.Semi ? FontSemi : FontRegular;
            if (asset != null) t.font = asset;
            else if (face != Face.Regular) t.fontStyle = FontStyles.Bold;   // no font files: fall back to synthetic bold
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = true;
            // Large Inter reads best slightly tighter; body text gets a little extra leading.
            t.lineSpacing = size >= Title ? -2 : 4;
            if (size >= Title) t.characterSpacing = -1.5f;
            return t;
        }

        /// <summary>A small section heading above a group, e.g. "Recent checks" (sentence case, no capitals shouting).</summary>
        public static TextMeshProUGUI OverlineText(Transform parent, string text, Color color)
        {
            return Txt(parent, text, Caption + 2, color, Face.Semi);
        }

        public static Button MakeButton(Image img, Action onClick)
        {
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.highlightedColor = Color.white; cb.pressedColor = new Color(0.9f, 0.9f, 0.88f, 1f); cb.selectedColor = Color.white;
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            b.onClick.AddListener(() => onClick());
            return b;
        }

        /// <summary>A light grey block without a border (on a white page). Children go straight into it (vertical layout).</summary>
        public static RectTransform Card(RectTransform parent, float padding = 40, float spacing = 20, Color? fill = null, Color? border = null, float radius = 32)
        {
            var outer = Img("Card", parent, sRound, border ?? fill ?? Panel, radius);
            var inner = Img("Fill", outer.rectTransform, sRound, fill ?? Panel, radius - 2);
            Stretch(inner.rectTransform, 2, 2, 2, 2);
            inner.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var vl = outer.gameObject.AddComponent<VerticalLayoutGroup>();
            int p = Mathf.RoundToInt(padding);
            vl.padding = new RectOffset(p, p, p, p);
            vl.spacing = spacing;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            return outer.rectTransform;
        }

        /// <summary>Soft shadow under a floating element (sheet, floating button). Ignored by layouts.</summary>
        public static void Shadow(RectTransform target, float spread, float alpha, float drop = 10)
        {
            var s = Img("Shadow", target, sSoft, Hex("#000000", alpha), 0);
            Stretch(s.rectTransform, -spread, -spread - drop, -spread, -spread + drop);
            s.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            s.transform.SetAsFirstSibling();
        }

        public enum ButtonKind { Primary, Secondary, Quiet, Accent }

        /// <summary>Primary = blue fill; secondary = light grey fill; quiet = text only; accent = blue tint (rare). Fully rounded.</summary>
        public static Button Btn(RectTransform parent, string label, ButtonKind kind, Action onClick, float height = 136)
        {
            Color fill = kind == ButtonKind.Primary ? Accent : kind == ButtonKind.Accent ? AccentSoft : kind == ButtonKind.Secondary ? Sunken : new Color(0, 0, 0, 0);
            var img = Img("Button " + label, parent, sRound, fill, height * 0.5f);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = Mathf.Min(height, TouchTarget); le.flexibleWidth = 1;
            var b = MakeButton(img, onClick);
            var t = Txt(img.rectTransform, label, Body, kind == ButtonKind.Primary ? OnColour : kind == ButtonKind.Accent ? Accent : Ink, Face.Semi, TextAlignmentOptions.Center);
            Stretch(t.rectTransform, 24, 0, 24, 0);
            t.enableAutoSizing = true; t.fontSizeMin = 28; t.fontSizeMax = Body;
            return b;
        }

        /// <summary>Round floating button with an icon (used over the camera image).</summary>
        public static Image RoundButton(RectTransform parent, Icon icon, Action onClick, float size = 112)
        {
            var holder = Rect("RoundButton", parent);
            holder.sizeDelta = new Vector2(size, size);
            Shadow(holder, 18, 0.18f, 6);
            var b = Img("Face", holder, sCircle, Surface, 0);
            Stretch(b.rectTransform);
            MakeButton(b, onClick);
            var ic = Rect("Icon", b.rectTransform); Stretch(ic);
            DrawIcon(ic, icon, size * 0.42f, Ink);
            return b;
        }

        /// <summary>Small rounded label with a tinted background.</summary>
        public static RectTransform Chip(RectTransform parent, string text, Color bg, Color fg)
        {
            var img = Img("Chip", parent, sRound, bg, 24);
            var hl = img.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(22, 22, 8, 10);
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            var fit = img.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t = Txt(img.rectTransform, text, Caption, fg, Face.Semi, TextAlignmentOptions.Center);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return img.rectTransform;
        }

        /// <summary>A floating pill (instructions, toasts) that sizes itself to its content.</summary>
        public static RectTransform Pill(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 pivot, Color bg)
        {
            var bgi = Img(name, parent, sRound, bg, 40);
            var rt = bgi.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            var hl = bgi.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(32, 36, 20, 22);
            hl.spacing = 18;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            var fit = bgi.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        /// <summary>On/off switch; returns a setter so the owner can change it.</summary>
        public static Action<bool> Switch(RectTransform parent, bool on, Action<bool> changed)
        {
            var track = Img("Switch", parent, sRound, on ? Accent : Hairline, 28);
            Fixed(track.rectTransform, 96, 56);
            var knob = Img("Knob", track.rectTransform, sCircle, Surface, 0);
            knob.rectTransform.sizeDelta = new Vector2(44, 44);
            bool state = on;
            Action<bool> set = v =>
            {
                state = v;
                track.color = v ? Accent : Hairline;
                knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(v ? 1 : 0, 0.5f);
                knob.rectTransform.anchoredPosition = new Vector2(v ? -28 : 28, 0);
            };
            set(on);
            MakeButton(track, () => { set(!state); changed(state); });
            return set;
        }

        public static RectTransform HRow(RectTransform parent, float height, float spacing = 20)
        {
            var row = Rect("Row", parent);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = spacing; hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            var le = row.gameObject.AddComponent<LayoutElement>();
            if (height > 0) le.preferredHeight = height;
            return row;
        }

        public static RectTransform VBox(RectTransform parent, float spacing)
        {
            var box = Rect("Box", parent);
            var vl = box.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = spacing;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            return box;
        }

        public static void Divider(RectTransform parent)
        {
            var d = Img("Divider", parent, null, Hairline, 0);
            var le = d.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 2; le.minHeight = 2;
        }

        /// <summary>A wire-colour dot with a hairline ring; colour -1 draws an empty ring ("no wire").</summary>
        public static Image Swatch(RectTransform parent, int colour, float size)
        {
            var ring = Img("Swatch", parent, colour < 0 ? sRing : sCircle, colour < 0 ? Ink3 : Hex("#000000", 0.18f), 0);
            Fixed(ring.rectTransform, size, size);
            if (colour >= 0)
            {
                var dot = Img("Fill", ring.rectTransform, sCircle, Wire[Mathf.Clamp(colour, 0, Wire.Length - 1)], 0);
                Place(dot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size - 4, size - 4));
            }
            return ring;
        }

        /// <summary>Round status badge with its icon.</summary>
        public static Image Badge(RectTransform parent, Color colour, Icon icon, float size, Color? iconColour = null)
        {
            var b = Img("Badge", parent, sCircle, colour, 0);
            Fixed(b.rectTransform, size, size);
            var holder = Rect("Icon", b.rectTransform);
            Stretch(holder);
            DrawIcon(holder, icon, size * 0.62f, iconColour ?? Surface);
            return b;
        }

        public static void Space(RectTransform parent, float h)
        {
            var s = Rect("Space", parent);
            var le = s.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = h; le.minHeight = h;
        }

        public static void Fixed(RectTransform rt, float w, float h)
        {
            rt.sizeDelta = new Vector2(w, h);
            var le = rt.GetComponent<LayoutElement>();
            if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.minWidth = w; le.preferredHeight = h; le.minHeight = h;
            le.flexibleWidth = 0;
        }

        public static LayoutElement Flex(Component c, float preferredWidth = -1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (preferredWidth >= 0) { le.preferredWidth = preferredWidth; le.flexibleWidth = 0; }
            else le.flexibleWidth = 1;
            return le;
        }

        public static void Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            Place(rt, anchor, pos, size, new Vector2(0.5f, 0.5f));
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>Vertical scroll area filling parent between top and bottom insets; returns the content column.</summary>
        public static RectTransform Scroll(RectTransform parent, float top, float bottom, float sidePadding = Gutter)
        {
            var vp = Img("Scroll", parent, null, new Color(0, 0, 0, 0), 0);
            vp.raycastTarget = true;
            Stretch(vp.rectTransform, 0, bottom, 0, top);
            vp.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", vp.rectTransform);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var vl = content.gameObject.AddComponent<VerticalLayoutGroup>();
            int p = Mathf.RoundToInt(sidePadding);
            vl.padding = new RectOffset(p, p, 16, 64);
            vl.spacing = 28;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = vp.gameObject.AddComponent<ScrollRect>();
            sr.viewport = vp.rectTransform;
            sr.content = content;
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 40;
            return content;
        }

        // ---------------------------------------------------------------- icons (drawn from strokes)

        public static void DrawIcon(RectTransform parent, Icon icon, float size, Color c)
        {
            Clear(parent);
            float th = Mathf.Max(3f, size * 0.12f);
            switch (icon)
            {
                case Icon.Check:
                    Bar(parent, new Vector2(-0.30f, 0.02f) * size, new Vector2(-0.08f, -0.20f) * size, th, c);
                    Bar(parent, new Vector2(-0.08f, -0.20f) * size, new Vector2(0.32f, 0.22f) * size, th, c);
                    break;
                case Icon.Cross:
                    Bar(parent, new Vector2(-0.22f, 0.22f) * size, new Vector2(0.22f, -0.22f) * size, th, c);
                    Bar(parent, new Vector2(-0.22f, -0.22f) * size, new Vector2(0.22f, 0.22f) * size, th, c);
                    break;
                case Icon.Alert:
                    Bar(parent, new Vector2(0, 0.28f) * size, new Vector2(0, -0.04f) * size, th, c);
                    Place(Img("Dot", parent, sCircle, c, 0).rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -0.25f * size), new Vector2(th * 1.2f, th * 1.2f));
                    break;
                case Icon.Pencil:
                    Bar(parent, new Vector2(-0.22f, -0.22f) * size, new Vector2(0.20f, 0.20f) * size, th, c);
                    Bar(parent, new Vector2(-0.26f, -0.26f) * size, new Vector2(-0.12f, -0.26f) * size, th * 0.8f, c);
                    break;
                case Icon.ChevronLeft:
                    Bar(parent, new Vector2(0.08f, 0.24f) * size, new Vector2(-0.16f, 0f) * size, th, c);
                    Bar(parent, new Vector2(-0.16f, 0f) * size, new Vector2(0.08f, -0.24f) * size, th, c);
                    break;
                case Icon.ChevronRight:
                    Bar(parent, new Vector2(-0.08f, 0.24f) * size, new Vector2(0.16f, 0f) * size, th, c);
                    Bar(parent, new Vector2(0.16f, 0f) * size, new Vector2(-0.08f, -0.24f) * size, th, c);
                    break;
                case Icon.Jobs:
                    for (int i = 0; i < 3; i++)
                    {
                        float y = (0.24f - 0.24f * i) * size;
                        Place(Img("Dot", parent, sCircle, c, 0).rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-0.30f * size, y), new Vector2(th * 1.3f, th * 1.3f));
                        Bar(parent, new Vector2(-0.12f * size, y), new Vector2(0.34f * size, y), th, c);
                    }
                    break;
                case Icon.Reports:
                    Outline(parent, new Vector2(-0.28f, -0.36f) * size, new Vector2(0.28f, 0.36f) * size, th, c);
                    Bar(parent, new Vector2(-0.13f, 0.12f) * size, new Vector2(0.13f, 0.12f) * size, th * 0.9f, c);
                    Bar(parent, new Vector2(-0.13f, -0.08f) * size, new Vector2(0.13f, -0.08f) * size, th * 0.9f, c);
                    break;
                case Icon.Settings:
                    for (int i = 0; i < 2; i++)
                    {
                        float y = (0.16f - 0.32f * i) * size, k = i == 0 ? -0.12f : 0.14f;
                        Bar(parent, new Vector2(-0.34f * size, y), new Vector2(0.34f * size, y), th * 0.9f, c);
                        Place(Img("Knob", parent, sCircle, c, 0).rectTransform, new Vector2(0.5f, 0.5f), new Vector2(k * size, y), new Vector2(th * 2.6f, th * 2.6f));
                    }
                    break;
                case Icon.Info:
                    Bar(parent, new Vector2(0, 0.06f) * size, new Vector2(0, -0.28f) * size, th, c);
                    Place(Img("Dot", parent, sCircle, c, 0).rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 0.24f * size), new Vector2(th * 1.3f, th * 1.3f));
                    break;
                case Icon.Camera:
                    Outline(parent, new Vector2(-0.36f, -0.24f) * size, new Vector2(0.36f, 0.22f) * size, th, c);
                    var lens = Img("Lens", parent, sRing, c, 0);
                    Place(lens.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -0.01f * size), new Vector2(0.30f * size, 0.30f * size));
                    break;
            }
        }

        static void Outline(RectTransform parent, Vector2 a, Vector2 b, float th, Color c)
        {
            Bar(parent, new Vector2(a.x, a.y), new Vector2(b.x, a.y), th, c);
            Bar(parent, new Vector2(a.x, b.y), new Vector2(b.x, b.y), th, c);
            Bar(parent, new Vector2(a.x, a.y), new Vector2(a.x, b.y), th, c);
            Bar(parent, new Vector2(b.x, a.y), new Vector2(b.x, b.y), th, c);
        }

        /// <summary>A rounded bar from a to b (local coordinates, centre-anchored).</summary>
        public static Image Bar(RectTransform parent, Vector2 a, Vector2 b, float thick, Color c)
        {
            var img = Img("Bar", parent, sRound, c, thick * 0.5f);
            SetBar(img.rectTransform, a, b, thick);
            return img;
        }

        public static void SetBar(RectTransform rt, Vector2 a, Vector2 b, float thick)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            Vector2 d = b - a;
            rt.sizeDelta = new Vector2(d.magnitude + thick, thick);
            rt.anchoredPosition = (a + b) * 0.5f;
            rt.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        // ---------------------------------------------------------------- animation

        public static float Ease(float k) { k = Mathf.Clamp01(k); return 1f - Mathf.Pow(1f - k, 3f); }

        public static IEnumerator Fade(CanvasGroup g, float a, float b, float seconds)
        {
            if (g == null) yield break;
            float t = 0;
            while (t < seconds && g != null)
            {
                g.alpha = Mathf.Lerp(a, b, Ease(t / seconds));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (g != null) g.alpha = b;
        }

        /// <summary>Slides a panel in from below (sheets) while fading it in.</summary>
        public static IEnumerator SlideUp(RectTransform rt, CanvasGroup g, float distance, float seconds)
        {
            if (rt == null) yield break;
            Vector2 end = rt.anchoredPosition, start = end - new Vector2(0, distance);
            float t = 0;
            while (t < seconds && rt != null)
            {
                float k = Ease(t / seconds);
                rt.anchoredPosition = Vector2.Lerp(start, end, k);
                if (g != null) g.alpha = k;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (rt != null) rt.anchoredPosition = end;
            if (g != null) g.alpha = 1;
        }

        public static IEnumerator Pop(Transform tr)
        {
            float t = 0;
            while (t < 0.28f && tr != null)
            {
                float k = t / 0.28f;
                float s = 1f + 0.08f * Mathf.Sin(k * Mathf.PI) * (1f - k);
                tr.localScale = new Vector3(s, s, 1f);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (tr != null) tr.localScale = Vector3.one;
        }

        // ---------------------------------------------------------------- misc

        public static Color Hex(string hex, float alpha = 1f)
        {
            Color c;
            ColorUtility.TryParseHtmlString(hex, out c);
            c.a = alpha;
            return c;
        }

        public static string HexOf(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }
    }
}
