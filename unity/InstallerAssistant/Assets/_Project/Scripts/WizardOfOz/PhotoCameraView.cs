using System;
using System.Collections.Generic;
using InstallerAssistant.AR;
using InstallerAssistant.Detection;
using InstallerAssistant.Labels;
using InstallerAssistant.UI;
using InstallerAssistant.Verification;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static InstallerAssistant.UI.UiKit;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InstallerAssistant.WizardOfOz
{
    /// <summary>
    /// The camera view of the Wizard of Oz: a photo per end instead of the live camera. A frame is the photo, so
    /// reading positions map 1:1 onto it. The photo is zoomed so it always fills the screen, sways a little like a
    /// hand-held phone, and can be calibrated by the operator (key K) to place the label and terminals.
    /// </summary>
    public sealed class PhotoCameraView : ICameraView
    {
        readonly DemoLayout L;
        readonly WozWorld world;
        readonly UsageRule rule;
        readonly RectTransform operatorLayer;
        readonly bool layoutIsAsset;
        readonly Func<CableEnd> startEnd;
        readonly Action<string> toast;
        readonly Dictionary<DemoLayout.PhotoView, Texture2D> mockCache = new Dictionary<DemoLayout.PhotoView, Texture2D>();
        readonly Dictionary<string, Vector2> anchors = new Dictionary<string, Vector2>();
        readonly Dictionary<string, Texture2D> fixedCache = new Dictionary<string, Texture2D>();

        RectTransform root, mover, photoRT, markers;
        RawImage photo;
        AspectRatioFitter fit;
        DemoLayout.PhotoView view;
        Texture2D current;
        CableEnd end = CableEnd.Unknown;
        long frameId;

        // operator calibration
        bool calibrating;
        int calibStep;
        Vector2 calibPin1;
        RectTransform calibLayer;
        TextMeshProUGUI calibText;
        readonly List<RectTransform> calibDots = new List<RectTransform>();

        public PhotoCameraView(DemoLayout layout, WozWorld world, UsageRule rule, RectTransform operatorLayer, bool layoutIsAsset,
            Func<CableEnd> startEnd, Action<string> toast)
        {
            L = layout; this.world = world; this.rule = rule; this.operatorLayer = operatorLayer;
            this.layoutIsAsset = layoutIsAsset; this.startEnd = startEnd; this.toast = toast;
        }

        // ================================================================== ICameraView

        public bool SeeThrough { get { return false; } }
        public float MarkerScale { get { return mover != null && mover.localScale.x > 0 ? 1f / mover.localScale.x : 1f; } }
        public Vector2 LabelHint { get { return view != null ? view.labelPos : new Vector2(0.5f, 0.6f); } }

        public CameraFrame Latest
        {
            get
            {
                return new CameraFrame
                {
                    Id = ++frameId, Timestamp = Time.unscaledTimeAsDouble, Format = FramePixels.None,
                    Width = current != null ? current.width : 0, Height = current != null ? current.height : 0,
                    Source = "photo:" + (current != null ? current.name : "none"),
                };
            }
        }

        public RectTransform Attach(RectTransform parent)
        {
            Detach();
            var bg = Img("Camera", parent, null, Hex("#111111"), 0);
            root = bg.rectTransform;
            Stretch(root);
            root.SetAsFirstSibling();
            bg.gameObject.AddComponent<RectMask2D>();
            mover = UiKit.Rect("Mover", root);
            Stretch(mover);
            photo = new GameObject("Photo", typeof(RectTransform)).AddComponent<RawImage>();
            photo.transform.SetParent(mover, false);
            photo.raycastTarget = false;
            photoRT = photo.rectTransform;
            Stretch(photoRT);
            fit = photo.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            markers = UiKit.Rect("Markers", photoRT);
            Stretch(markers);
            if (end != CableEnd.Unknown) ShowEnd(end);
            return markers;
        }

        public void Detach()
        {
            if (calibrating) StopCalibration();
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = mover = photoRT = markers = null;
            photo = null; fit = null;
        }

        public void ShowEnd(CableEnd e)
        {
            end = e != CableEnd.Unknown ? e : startEnd();
            if (end == CableEnd.Unknown) end = CableEnd.Device;
            world.AtController = end == CableEnd.Controller;
            Texture2D tex;
            view = Pick(end, out tex);
            world.View = view;
            current = tex;
            if (photo != null && tex != null)
            {
                photo.texture = tex;
                fit.aspectRatio = tex.width / (float)tex.height;
            }
            anchors.Clear();
            PreAnchor();
        }

        public void Anchor(string key, ImagePoint where, CameraFrame frame)
        {
            if (where.Valid) anchors[key] = new Vector2(where.X, where.Y);
        }

        public void ClearAnchors() { anchors.Clear(); }

        public bool TryGetPosition(string key, out Vector2 position) { return anchors.TryGetValue(key, out position); }

        public EndSnapshot Snapshot(CableEnd e, ImagePoint[] positions)
        {
            var pts = new Vector2[positions != null ? positions.Length : 0];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = positions[i].Valid ? new Vector2(positions[i].X, positions[i].Y) : Get(view != null ? view.points : null, i, new Vector2(0.5f, 0.5f));
            return new EndSnapshot { End = e, Image = current, Points = pts };
        }

        /// <summary>A snapshot of an end as the layout describes it (operator shortcut, no reading involved).</summary>
        public EndSnapshot SnapshotOf(CableEnd e)
        {
            Texture2D tex;
            var v = e == CableEnd.Controller ? Scenario.wiring : L.reader;
            tex = e == CableEnd.Controller ? ControllerTexture(v, Scenario.pinColours, true) : ReaderTexture();
            var pts = v.points != null ? (Vector2[])v.points.Clone() : new Vector2[0];
            return new EndSnapshot { End = e, Image = tex, Points = pts };
        }

        public void Tick()
        {
            if (mover == null || view == null) return;
            float t = Time.unscaledTime;
            // A camera feed always fills the screen: never scale the photo below the screen
            // (that showed the dark background as a border) and never pan past its edges.
            Vector2 pan = new Vector2(Mathf.Clamp(view.pan.x, -0.4f, 0.4f), Mathf.Clamp(view.pan.y, -0.4f, 0.4f));
            float cover = Mathf.Max(1f / (1f - 2f * Mathf.Abs(pan.x)), 1f / (1f - 2f * Mathf.Abs(pan.y)));
            float s = Mathf.Max(view.zoom, cover) * (calibrating ? 1f : 1.02f);
            Vector2 size = photoRT.rect.size;
            Vector2 sway = calibrating ? Vector2.zero
                : new Vector2(Mathf.PerlinNoise(t * 0.35f, 1.3f) - 0.5f, Mathf.PerlinNoise(2.1f, t * 0.3f) - 0.5f) * 18f;
            Vector2 room = (s - 1f) * 0.5f * size - Vector2.one * 9f;   // keep 9 px for the hand sway
            Vector2 off = new Vector2(-pan.x * size.x * s, pan.y * size.y * s);
            off.x = Mathf.Clamp(off.x, -Mathf.Max(0f, room.x), Mathf.Max(0f, room.x));
            off.y = Mathf.Clamp(off.y, -Mathf.Max(0f, room.y), Mathf.Max(0f, room.y));
            mover.localScale = new Vector3(s, s, 1f);
            mover.anchoredPosition = off + sway;

            if (calibrating)
                for (int i = 0; i < calibDots.Count; i++) calibDots[i].localScale = Vector3.one * MarkerScale;
        }

        // ================================================================== photos

        DemoLayout.Scenario Scenario { get { return world.Scenario ?? new DemoLayout.Scenario(); } }

        DemoLayout.PhotoView Pick(CableEnd e, out Texture2D tex)
        {
            if (e != CableEnd.Controller)
            {
                if (world.DeviceRewired) { tex = ReaderFixedTexture(); return L.readerFixed; }
                tex = ReaderTexture(); return L.reader;
            }
            var sc = Scenario;
            if (world.Rewired) { tex = ControllerTexture(L.controllerFixed, null, true); return L.controllerFixed; }
            if (!world.LabelAttached && (sc.noLabel.photo != null || L.UsesMockImages)) { tex = ControllerTexture(sc.noLabel, sc.pinColours, false); return sc.noLabel; }
            tex = ControllerTexture(sc.wiring, sc.pinColours, true);
            return sc.wiring;
        }

        /// <summary>The scripted readers return the photo's points, so they are anchored from the start.</summary>
        void PreAnchor()
        {
            if (view == null) return;
            int n = end == CableEnd.Controller ? rule.Connector.Count : rule.DeviceTerminals.Length;
            for (int i = 0; i < n; i++)
                if (view.points != null && i < view.points.Length) anchors[CameraKeys.Of(end, i)] = view.points[i];
            anchors[CameraKeys.Label] = view.labelPos;
        }

        Texture2D ReaderTexture()
        {
            if (L.reader.photo != null) return L.reader.photo;
            Texture2D t;
            if (!mockCache.TryGetValue(L.reader, out t))
            {
                var c = new Color[4];
                for (int i = 0; i < 4; i++) c[i] = Wire[Mathf.Clamp(Get(L.readerColours, i, i), 0, Wire.Length - 1)];
                t = MockPhotos.Reader(c);
                mockCache[L.reader] = t;
            }
            return t;
        }

        /// <summary>The reader after a fix there: its photo, or a drawn image with the colours it now carries.</summary>
        Texture2D ReaderFixedTexture()
        {
            if (L.readerFixed.photo != null) return L.readerFixed.photo;
            var colours = world.DeviceColours(rule);
            string key = string.Join(",", Array.ConvertAll(colours, x => x.ToString()));
            Texture2D t;
            if (!fixedCache.TryGetValue(key, out t))
            {
                var c = new Color[colours.Length];
                for (int i = 0; i < c.Length; i++) c[i] = Wire[Mathf.Clamp(colours[i], 0, Wire.Length - 1)];
                t = MockPhotos.Reader(c);
                fixedCache[key] = t;
            }
            return t;
        }

        /// <summary>Photo of a controller view; with no photo, a mock showing pinColours (null = correctly wired).</summary>
        Texture2D ControllerTexture(DemoLayout.PhotoView v, int[] pinColours, bool withLabel)
        {
            if (v.photo != null) return v.photo;
            Texture2D t;
            if (mockCache.TryGetValue(v, out t)) return t;
            var c = new Color[8];
            for (int p = 1; p <= 8; p++)
            {
                int colour = pinColours != null ? Get(pinColours, p - 1, -1)
                    : rule.TerminalFor(p) != null ? Get(L.readerColours, rule.TerminalIndex(rule.TerminalFor(p)), -1) : -1;
                c[p - 1] = colour >= 0 ? Wire[Mathf.Clamp(colour, 0, Wire.Length - 1)] : new Color(0, 0, 0, 0);
            }
            t = MockPhotos.Controller(c, withLabel);
            mockCache[v] = t;
            return t;
        }

        static T Get<T>(T[] arr, int i, T fallback) { return arr != null && i >= 0 && i < arr.Length ? arr[i] : fallback; }

        // ================================================================== operator calibration (key K)

        int CalibCount { get { return end == CableEnd.Controller ? 3 : 1 + rule.DeviceTerminals.Length; } }

        /// <summary>Starts or stops placing the label and terminals on the current photo by clicking.</summary>
        public void ToggleCalibration()
        {
            if (photo == null || view == null || operatorLayer == null) return;
            if (calibrating) { StopCalibration(); return; }
            calibrating = true;
            calibStep = 0;
            var catcher = Img("Calibrate", operatorLayer, null, Hex("#000000", 0.01f), 0);
            catcher.raycastTarget = true;
            calibLayer = catcher.rectTransform;
            Stretch(calibLayer);
            catcher.gameObject.AddComponent<ClickCatcher>().onClick = OnCalibrationClick;
            var pill = Pill(calibLayer, "CalibText", new Vector2(0.5f, 1), new Vector2(0, -420), new Vector2(0.5f, 0.5f), WarnSoft);
            calibText = Txt(pill, "", Small, WarnInk, Face.Semi, TextAlignmentOptions.Left);
            DrawCalibDots();
            UpdateCalibText();
        }

        public bool Calibrating { get { return calibrating; } }

        public void StopCalibration()
        {
            calibrating = false;
            if (calibLayer != null) UnityEngine.Object.Destroy(calibLayer.gameObject);
            calibLayer = null;
            foreach (var d in calibDots) if (d != null) UnityEngine.Object.Destroy(d.gameObject);
            calibDots.Clear();
        }

        void DrawCalibDots()
        {
            foreach (var d in calibDots) if (d != null) UnityEngine.Object.Destroy(d.gameObject);
            calibDots.Clear();
            if (markers == null || view == null) return;
            var pts = new List<Vector2>(view.points ?? new Vector2[0]) { view.labelPos };
            foreach (var p in pts)
            {
                var d = Img("CalibDot", markers, sRing, Hex("#FFFFFF", 0.95f), 0).rectTransform;
                d.anchorMin = d.anchorMax = new Vector2(p.x, 1f - p.y);
                d.sizeDelta = new Vector2(40, 40);
                calibDots.Add(d);
            }
        }

        void UpdateCalibText()
        {
            bool ctrl = end == CableEnd.Controller;
            string what = calibStep == 0 ? "the cable LABEL"
                : ctrl ? (calibStep == 1 ? "pin 1 (" + rule.Connector.Pin(1).Print + ")" : "pin " + rule.Connector.Count + " (" + rule.Connector.Pin(rule.Connector.Count).Print + ")")
                : "terminal " + rule.DeviceTerminals[calibStep - 1].Print;
            calibText.text = (ctrl ? "Controller" : "Reader") + " · click " + what + "  (" + (calibStep + 1) + "/" + CalibCount + ") · Esc to stop";
        }

        void OnCalibrationClick(PointerEventData e)
        {
            if (!calibrating || photoRT == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(photoRT, e.position, null, out local)) return;
            UnityEngine.Rect r = photoRT.rect;
            var p = new Vector2(Mathf.Clamp01((local.x - r.xMin) / r.width), Mathf.Clamp01(1f - (local.y - r.yMin) / r.height));
            if (calibStep == 0) view.labelPos = p;
            else if (end != CableEnd.Controller)
            {
                int i = calibStep - 1;
                if (view.points == null || view.points.Length < rule.DeviceTerminals.Length)
                {
                    var n = new Vector2[rule.DeviceTerminals.Length];
                    if (view.points != null) Array.Copy(view.points, n, view.points.Length);
                    view.points = n;
                }
                view.points[i] = p;
            }
            else if (calibStep == 1) calibPin1 = p;
            else view.points = DemoLayout.Interpolate(calibPin1, p);
            PreAnchor();
            DrawCalibDots();
#if UNITY_EDITOR
            if (layoutIsAsset) EditorUtility.SetDirty(L);
#endif
            calibStep++;
            if (calibStep >= CalibCount)
            {
                StopCalibration();
                toast(layoutIsAsset ? "Positions saved to " + L.name : "Positions set (assign a DemoLayout asset to keep them)");
#if UNITY_EDITOR
                if (layoutIsAsset) AssetDatabase.SaveAssets();
#endif
                return;
            }
            UpdateCalibText();
        }
    }

    /// <summary>Forwards pointer clicks to a callback (used for calibration).</summary>
    public class ClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public Action<PointerEventData> onClick;
        public void OnPointerClick(PointerEventData e) { if (onClick != null) onClick(e); }
    }
}
