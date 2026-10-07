using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace InstallerAssistant.WizardOfOz
{
    /// <summary>
    /// Wizard-of-Oz demo of the Installer Assistant verification flow (see the Verification Design doc).
    /// All "AI" results are scripted; the interaction and the screens are what is being shown.
    ///
    /// SETUP
    ///   1. Create a new empty scene (not the AR SampleScene).
    ///   2. Add an empty GameObject, add this component. Optionally assign a DemoLayout asset.
    ///   3. Game view: choose a portrait resolution, e.g. 1080x2340. Press Play.
    ///
    /// OPERATOR KEYS (Editor / keyboard): R restart · 1 jump to reader end · 2 jump to controller end
    ///   K calibrate positions on the current photo · H show/hide this hint
    /// </summary>
    public class InstallerAssistantDemo : MonoBehaviour
    {
        [Tooltip("Photos, positions and scripted results. Empty = built-in mock images and defaults.")]
        public DemoLayout layout;
        [Tooltip("Speeds up or slows down every fake processing delay.")]
        [Range(0.25f, 3f)] public float speed = 1f;
        public bool showOperatorHint = true;

        // ------------------------------------------------------------------ palette

        static readonly string[] WireNames = { "Red", "Black", "Green", "Yellow", "White", "Brown", "Blue", "Orange" };
        static readonly Color[] WireColours =
        {
            Hex("#E53935"), Hex("#1E1F22"), Hex("#2E9E4F"), Hex("#F2C230"),
            Hex("#F1F1EE"), Hex("#7B4B2A"), Hex("#2F6FDB"), Hex("#F08A24")
        };
        static readonly string[] DefaultReaderNames = { "+", "-", "A", "B" };
        static readonly string[] DefaultControllerNames = { "+12V", "-", "A", "B" };
        static readonly int[] ResultRowOrder = { 1, 0, 2, 3 }; // Display power -, power +, A, B; keep data indices unchanged.
        string[] ReaderNames { get { return Names(L != null ? L.readerTerminalNames : null, DefaultReaderNames); } }
        string[] ControllerNames { get { return Names(L != null ? L.controllerTerminalNames : null, DefaultControllerNames); } }
        static string[] Names(string[] arr, string[] fallback) { return arr != null && arr.Length >= 4 ? arr : fallback; }

        static readonly Color Ink = Hex("#F3F6F8");
        static readonly Color Muted = Hex("#A3ADB7");
        static readonly Color Faint = Hex("#6B7682");
        static readonly Color Glass = Hex("#0E1318", 0.88f);
        static readonly Color GlassLight = Hex("#FFFFFF", 0.07f);
        static readonly Color Accent = Hex("#4CC3FF");
        static readonly Color AccentInk = Hex("#04121B");
        static readonly Color Ok = Hex("#34C77B");
        static readonly Color Bad = Hex("#FF5A5F");
        static readonly Color Warn = Hex("#FFB020");
        static readonly Color Screen0 = Hex("#0A0E12");

        const float TagW = 248f, TagPitch = 262f;

        enum St { Hidden, Reading, Ok, Wrong, Check, Edited, YouConfirmed }

        class Term
        {
            public int index;
            public string name;
            public Vector2 pos;
            public int colour;
            public int expected = -1;
            public int confidence;
            public St state = St.Hidden;
            public bool rulesOk = true, aiOk = true;
            public string fix;
            // views
            public RectTransform root, vis, tag, leader, pulse;
            public Image disk, arc, tagSwatch, tagBg;
            public RectTransform icon;
            public TextMeshProUGUI tagText;
        }

        // ------------------------------------------------------------------ state

        DemoLayout L;
        Texture2D readerTex, controllerTex, controllerFixedTex, controllerNoLabelTex;
        readonly List<Term> terms = new List<Term>();
        int[] recorded;               // confirmed colours at the reader end
        bool atController;
        DemoLayout.ControllerPhotoPlacement controllerPlacement;
        Vector2[] ControllerPositions => controllerPlacement != null ? controllerPlacement.terminalPos : L.controllerTerminalPos;
        Vector2 ControllerLabelPosition => controllerPlacement != null ? controllerPlacement.labelPos : L.controllerLabelPos;
        Vector2 LabelFrameSize => !atController ? L.readerLabelSize
            : controllerPlacement != null ? controllerPlacement.labelSize : L.controllerLabelSize;
        string LabelDescription => L.handwrittenLabels ? "handwritten cable label" : "QR label";
        float startTime;
        int issuesFixed, checkedByYou;
        string clicked;               // last button id clicked
        Term tapped;                  // last terminal tapped
        bool modalOpen;
        bool calibrating;
        int calibStep;
        Coroutine flow;

        // ------------------------------------------------------------------ views

        Canvas canvas;
        RectTransform canvasRT, safe, mover, photoRT, markers, scanLine, qrBox, missingGuide;
        RawImage photo;
        AspectRatioFitter photoFit;
        TextMeshProUGUI stepText, instructionText, chipText, toastText, hintText, calibText;
        Image instructionDot;
        CanvasGroup chipGroup, instructionGroup, toastGroup, flashGroup, snapshotGroup, calibGroup;
        RectTransform sheet, sheetContent, sheetButtons, snapshot;
        TextMeshProUGUI sheetTitle, sheetSub;
        CanvasGroup sheetGroup;
        readonly List<RectTransform> snapshotDots = new List<RectTransform>();
        readonly List<RectTransform> pulseRings = new List<RectTransform>();
        readonly List<Term> hiddenBeforeCalibration = new List<Term>();
        int pendingRestart = -1;
        Coroutine toastCo;
        RectTransform snapshotHighlight;
        bool snapshotExpanded;
        Rect snapshotUv;

        static Sprite sRound, sCircle, sRing, sSoft, sFadeDown, sFadeUp;

        // ================================================================== lifecycle

        void Start()
        {
            L = layout != null ? layout : ScriptableObject.CreateInstance<DemoLayout>();
            MakeSprites();
            EnsureEventSystem();
            var cam = Camera.main;
            if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; }
            Restart(0);
        }

        void Restart(int phase)
        {
            if (flow != null) StopCoroutine(flow);
            StopAllCoroutines();
            if (canvas != null) Destroy(canvas.gameObject);
            terms.Clear();
            snapshotDots.Clear();
            pulseRings.Clear();
            hiddenBeforeCalibration.Clear();
            snapshot = null; snapshotHighlight = null; toastCo = null;
            modalOpen = false; calibrating = false; snapshotExpanded = false;
            clicked = null; tapped = null;
            issuesFixed = 0; checkedByYou = 0;
            LoadPhotos();
            BuildUI();
            startTime = Time.unscaledTime;
            if (phase == 2 && recorded == null) recorded = (int[])L.readerColours.Clone();
            flow = StartCoroutine(Flow(phase));
        }

        void LoadPhotos()
        {
            var rc = new Color[4];
            var cc = new Color[4];
            var fc = new Color[4];
            for (int i = 0; i < 4; i++)
            {
                rc[i] = WireColours[Mathf.Clamp(Get(L.readerColours, i, i), 0, 7)];
                cc[i] = WireColours[Mathf.Clamp(Get(L.controllerFoundColours, i, i), 0, 7)];
                fc[i] = rc[i];
            }
            if (readerTex == null) readerTex = L.readerPhoto != null ? L.readerPhoto : MockPhotos.Reader(rc);
            if (controllerTex == null) controllerTex = L.controllerPhoto != null ? L.controllerPhoto : MockPhotos.Controller(cc);
            if (controllerNoLabelTex == null)
            {
                if (L.controllerNoLabelPhoto != null) controllerNoLabelTex = L.controllerNoLabelPhoto;
                else if (L.controllerPhoto == null) controllerNoLabelTex = MockPhotos.Controller(cc, false);
            }
            if (controllerFixedTex == null)
            {
                if (L.controllerFixedPhoto != null) controllerFixedTex = L.controllerFixedPhoto;
                else if (L.controllerPhoto == null) controllerFixedTex = MockPhotos.Controller(fc);
            }
        }

        IEnumerator Flow(int phase)
        {
            if (phase == 0) { yield return StartScreen(); phase = 1; }
            if (phase == 1) { yield return ReaderEnd(); yield return WalkScreen(); }
            yield return ControllerEnd();
            yield return VerifiedScreen();
        }

        void Update()
        {
            if (pendingRestart >= 0) { int p = pendingRestart; pendingRestart = -1; Restart(p); return; }
            HandleKeys();
            float t = Time.unscaledTime;

            // a little hand-held camera movement
            if (mover != null)
            {
                bool still = calibrating;
                float zoom = atController ? (controllerPlacement != null ? controllerPlacement.zoom : L.controllerZoom) : L.readerZoom;
                Vector2 pan = atController ? (controllerPlacement != null ? controllerPlacement.pan : L.controllerPan) : L.readerPan;
                float s = zoom * (still ? 1f : 1.025f);
                Vector2 size = photoRT != null ? photoRT.rect.size : Vector2.zero;
                Vector2 sway = still ? Vector2.zero
                    : new Vector2(Mathf.PerlinNoise(t * 0.35f, 1.3f) - 0.5f, Mathf.PerlinNoise(2.1f, t * 0.3f) - 0.5f) * 18f;
                mover.localScale = new Vector3(s, s, 1f);
                mover.anchoredPosition = new Vector2(-pan.x * size.x * s, pan.y * size.y * s) + sway;
            }

            foreach (var term in terms) AnimateMarker(term, t);

            if (scanLine != null && scanLine.gameObject.activeSelf)
            {
                float h = canvasRT.rect.height;
                float k = Mathf.Repeat(t * 0.55f * speed, 1f);
                scanLine.anchoredPosition = new Vector2(0, -h * (0.12f + 0.55f * k));
                var g = scanLine.GetComponent<CanvasGroup>();
                g.alpha = Mathf.Sin(k * Mathf.PI);
            }

            if (qrBox != null && qrBox.gameObject.activeSelf)
            {
                Vector2 frame = LabelFrameSize;
                qrBox.sizeDelta = frame.x > 0 && frame.y > 0
                    ? Vector2.Scale(frame, photoRT.rect.size) * Scale() : new Vector2(300, 300);
                float p = 1f + 0.03f * Mathf.Sin(t * 5f);
                qrBox.localScale = new Vector3(p / Scale(), p / Scale(), 1f);
            }
            if (missingGuide != null && missingGuide.gameObject.activeSelf)
            {
                missingGuide.localScale = Vector3.one * (L.markerScale / Scale());
                missingGuide.GetComponent<CanvasGroup>().alpha = 0.65f + 0.35f * Mathf.Sin(t * 4f);
            }
            foreach (var ring in pulseRings)
            {
                if (ring == null || !ring.gameObject.activeInHierarchy) continue;
                float k = Mathf.Repeat(t * 1.4f, 1f);
                float p = 1f + 0.6f * k;
                ring.localScale = new Vector3(p, p, 1);
                ring.GetComponent<Image>().color = Hex("#FFFFFF", 1f - k);
            }
            if (sheet != null)
            {
                float w = Mathf.Min(canvasRT.rect.width, 1080f);
                sheet.sizeDelta = new Vector2(w, sheet.sizeDelta.y);
            }
        }

        void LateUpdate()
        {
            LayoutTags();
        }

        float Scale() { return mover != null ? mover.localScale.x : 1f; }

        // ================================================================== phases

        IEnumerator StartScreen()
        {
            var o = Overlay();
            var col = Column(o, 0.5f);
            var logo = Img("Logo", col, sRound, Accent, 44);
            Fixed(logo.rectTransform, 168, 168);
            DrawIcon(logo.rectTransform, St.Ok, 120, AccentInk);
            Space(col, 40);
            Txt(col, "Installer Assistant", 76, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            Space(col, 6);
            Txt(col, "AR wiring verification", 40, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
            Space(col, 80);
            var card = Card(col, 900);
            KeyValue(card, "Job", L.doorName + " · reader to controller");
            KeyValue(card, "Devices", "Door controller + card reader");
            KeyValue(card, "Checks", "Cable label · wiring · AI second opinion");
            Space(col, 80);
            var b = Btn(col, "Start verification", true, "start");
            Fixed((RectTransform)b.transform, 820, 140);
            Space(col, 40);
            Txt(col, "Wizard-of-Oz prototype · results are scripted", 28, Faint, FontStyles.Normal, TextAlignmentOptions.Center);
            yield return Fade(o.GetComponent<CanvasGroup>(), 0, 1, 0.35f);
            yield return WaitClick();
            yield return Fade(o.GetComponent<CanvasGroup>(), 1, 0, 0.3f);
            Destroy(o.gameObject);
        }

        IEnumerator ReaderEnd()
        {
            atController = false;
            SetPhoto(readerTex);
            stepText.text = "STEP 1 OF 2  ·  READER END";
            BuildTerms(false);
            HideChip();
            yield return FindLabel(L.readerLabelPos, false);

            // AI reading
            Sheet("Reading the wires", "AI reads the colour on each terminal.");
            var bar = ProgressLine("AI reading · 4 terminals");
            yield return ReadTerminals(bar, 2.4f, false);

            int unsure = Mathf.Clamp(L.uncertainReaderTerminal, -1, 3);
            for (int i = 0; i < 4; i++)
            {
                var t = terms[i];
                t.colour = Mathf.Clamp(Get(L.readerColours, i, i), 0, 7);
                t.confidence = i == unsure ? 71 : 93 + (i * 7) % 6;
                SetState(t, i == unsure ? St.Check : St.Ok);
            }

            while (true)
            {
                ReaderResultsSheet();
                yield return WaitClickOrTap();
                if (tapped != null)
                {
                    var t = tapped;
                    tapped = null;
                    yield return ColourPicker(t);
                    continue;
                }
                if (clicked == "rescan")
                {
                    Sheet("Reading the wires", "Reading again — hold the phone steady.");
                    var b2 = ProgressLine("AI reading · 4 terminals");
                    yield return ReadTerminals(b2, 1.8f, true);
                    foreach (var t in terms)
                    {
                        if (t.state == St.Edited) { SetState(t, St.Edited); continue; }
                        t.confidence = Mathf.Max(t.confidence, 94);
                        SetState(t, St.Ok);
                    }
                    continue;
                }
                if (clicked == "confirm")
                {
                    Term open = terms.Find(x => x.state == St.Check);
                    if (open != null) { Toast("Check " + open.name + " first: tap it to confirm or correct the colour"); continue; }
                    break;
                }
            }

            recorded = new int[4];
            for (int i = 0; i < 4; i++) recorded[i] = terms[i].colour;
            checkedByYou += terms.FindAll(x => x.state == St.Edited).Count;
            yield return Shutter();
            BuildSnapshot();
            Toast("Record saved · " + L.cableLabel);
            Sheet("Reader end recorded", "Colours and a snapshot are saved under the cable label.");
            yield return Wait(1.4f);
        }

        void ReaderResultsSheet()
        {
            int unsure = terms.FindAll(x => x.state == St.Check).Count;
            Sheet("Check the reading", unsure > 0
                ? "4 wires read · " + unsure + " needs your check. Tap a terminal to edit it."
                : "4 wires read. Confirm to save this end, or tap a terminal to edit it.");
            foreach (int i in ResultRowOrder) Row(terms[i]);
            Btn(sheetButtons, "Rescan", false, "rescan");
            Btn(sheetButtons, "Confirm", true, "confirm");
        }

        IEnumerator WalkScreen()
        {
            var o = Overlay();
            var col = Column(o, 0.5f);
            Txt(col, "Walk to the controller", 64, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            Space(col, 16);
            Txt(col, "The reader end of " + L.cableLabel + " is saved.\nScan the same label at the controller.", 38, Muted,
                FontStyles.Normal, TextAlignmentOptions.Center);
            Space(col, 90);

            // illustration: reader -> dashed path -> controller
            var art = Rect("Path", col);
            Fixed(art, 860, 260);
            var a = Img("Reader", art, sCircle, GlassLight, 0); Place(a.rectTransform, new Vector2(0, 0.5f), new Vector2(110, 0), new Vector2(150, 150));
            var b = Img("Controller", art, sCircle, Hex("#4CC3FF", 0.18f), 0); Place(b.rectTransform, new Vector2(1, 0.5f), new Vector2(-110, 0), new Vector2(150, 150));
            DrawIcon(a.rectTransform, St.Ok, 70, Ok);
            var dots = new List<Image>();
            for (int i = 0; i < 12; i++)
            {
                var d = Img("dot", art, sCircle, Hex("#FFFFFF", 0.25f), 0);
                Place(d.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-250 + i * 45.5f, 0), new Vector2(14, 14));
                dots.Add(d);
            }
            var lblA = Txt(art, "Reader", 30, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
            Place(lblA.rectTransform, new Vector2(0, 0.5f), new Vector2(110, -120), new Vector2(240, 44));
            var lblB = Txt(art, "Controller", 30, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
            Place(lblB.rectTransform, new Vector2(1, 0.5f), new Vector2(-110, -120), new Vector2(240, 44));
            Space(col, 90);

            var card = Card(col, 900);
            var row = HRow(card, 70);
            Txt(row, "Record", 32, Muted, FontStyles.Normal, TextAlignmentOptions.Left).gameObject.AddComponent<LayoutElement>().preferredWidth = 200;
            for (int i = 0; i < 4; i++)
            {
                var sw = Swatch(row, recorded[i], 34);
                var nm = Txt(row, ReaderNames[i], 30, Ink, FontStyles.Bold, TextAlignmentOptions.Left);
                nm.gameObject.AddComponent<LayoutElement>().preferredWidth = 90;
                sw.name = "sw";
            }
            Space(col, 90);
            var btn = Btn(col, "I'm at the controller", true, "go");
            Fixed((RectTransform)btn.transform, 820, 140);

            var g = o.GetComponent<CanvasGroup>();
            yield return Fade(g, 0, 1, 0.35f);
            clicked = null;
            while (clicked != "go")
            {
                float t = Time.unscaledTime * 1.2f;
                for (int i = 0; i < dots.Count; i++)
                {
                    float k = Mathf.Repeat(t - i / 12f, 1f);
                    dots[i].color = Color.Lerp(Hex("#4CC3FF", 1f), Hex("#FFFFFF", 0.18f), Mathf.Clamp01(k * 3f));
                }
                yield return null;
            }
            yield return Fade(g, 1, 0, 0.3f);
            Destroy(o.gameObject);
        }

        IEnumerator ControllerEnd()
        {
            atController = true;
            SetPhoto(controllerTex);
            stepText.text = "STEP 2 OF 2  ·  CONTROLLER END";
            BuildTerms(true);
            HideChip();
            if (snapshot == null) BuildSnapshot();
            ShowSnapshot(true);

            if (L.startWithMissingLabel)
            {
                if (controllerNoLabelTex != null) SetPhoto(controllerNoLabelTex);
                Instruction("Point the camera at the same cable label", Accent);
                Sheet("Find the cable label", "The label links this end to the record from the reader end.");
                ProgressLine("Looking for the " + LabelDescription, true);
                yield return Scan(1.8f);
                Instruction("No label found on this cable", Warn);
                ShowMissingGuide(true);
                Sheet("Label missing", "The app can't tell which cable this is. Attach the label " + L.cableLabel +
                                       " to this end, then scan again.");
                Btn(sheetButtons, "Label attached · scan again", true, "labelled");
                yield return WaitClick();
                ShowMissingGuide(false);
                SetPhoto(controllerTex);
            }

            yield return FindLabel(ControllerLabelPosition, true);
            Toast("Record loaded · reader end, 4 wires");

            // rules + AI second opinion
            Sheet("Checking the wiring", "Comparing this end with the record from the reader end.");
            var rules = ProgressLine("Rules · compare with the record");
            var ai = ProgressLine("AI second opinion");
            StartCoroutine(Progress(rules, 1.1f));
            yield return ReadTerminals(ai, 2.8f, false);
            Done(ai);

            int dis = Mathf.Clamp(L.aiDisagreesOnControllerTerminal, -1, 3);
            for (int i = 0; i < 4; i++)
            {
                var t = terms[i];
                t.expected = recorded[i];
                t.colour = Mathf.Clamp(Get(L.controllerFoundColours, i, i), 0, 7);
                t.rulesOk = t.colour == t.expected;
                t.aiOk = i == dis ? !t.rulesOk : t.rulesOk;
                t.fix = FixFor(t);
                SetState(t, t.rulesOk && t.aiOk ? St.Ok : (!t.rulesOk && !t.aiOk ? St.Wrong : St.Check));
            }

            while (true)
            {
                ControllerResultsSheet();
                yield return WaitClickOrTap();
                if (tapped != null)
                {
                    var t = tapped;
                    tapped = null;
                    yield return Detail(t);
                    continue;
                }
                if (clicked == "fix") break;
            }

            // installer fixes the wiring, then rescans
            issuesFixed = terms.FindAll(x => x.state == St.Wrong).Count;
            checkedByYou += terms.FindAll(x => x.state == St.YouConfirmed).Count;
            HighlightSnapshot(-1);
            Sheet("Checking again", "Comparing this end with the record from the reader end.");
            var r2 = ProgressLine("Rules · compare with the record");
            var a2 = ProgressLine("AI second opinion");
            if (controllerFixedTex != null) SetPhoto(controllerFixedTex);
            foreach (var t in terms) SetState(t, St.Reading);
            StartCoroutine(Progress(r2, 0.8f));
            yield return ReadTerminals(a2, 1.9f, true);
            Done(a2);
            foreach (var t in terms)
            {
                t.colour = t.expected;
                t.rulesOk = t.aiOk = true;
                SetState(t, St.Ok);
            }
            Instruction("All terminals match the reader end", Ok);
            Sheet("All 4 terminals correct", "Rules and AI second opinion agree on every terminal.");
            foreach (int i in ResultRowOrder) Row(terms[i]);
            Btn(sheetButtons, "Finish", true, "finish");
            yield return WaitClick();
        }

        void ControllerResultsSheet()
        {
            int wrong = terms.FindAll(x => x.state == St.Wrong).Count;
            int check = terms.FindAll(x => x.state == St.Check).Count;
            int ok = 4 - wrong - check;
            var parts = new List<string>();
            if (wrong > 0) parts.Add(wrong + " wrong");
            if (check > 0) parts.Add(check + " to check");
            if (ok > 0) parts.Add(ok + " OK");
            Sheet(string.Join("  ·  ", parts.ToArray()), "Tap a terminal for details and the fix.");
            foreach (int i in ResultRowOrder) Row(terms[i]);
            Btn(sheetButtons, "Fix & rescan", true, "fix");
            Instruction(wrong > 0 ? "Wiring does not match the reader end" : "Please check the amber terminal",
                wrong > 0 ? Bad : Warn);
        }

        string FixFor(Term t)
        {
            if (t.rulesOk) return "Compare this terminal with the snapshot. If the wire is loose or not fully inserted, re-seat it and rescan.";
            int other = terms.FindIndex(x => x != t && Get(L.controllerFoundColours, x.index, x.index) == t.expected);
            if (other >= 0 && terms[other].expected == t.colour)
                return "Swap " + WireNames[t.expected].ToLower() + " and " + WireNames[t.colour].ToLower() + ": " +
                       WireNames[t.expected].ToLower() + " to " + t.name + ", " + WireNames[t.colour].ToLower() + " to " + terms[other].name + ".";
            return "Move the " + WireNames[t.expected].ToLower() + " wire to " + t.name + ".";
        }

        IEnumerator VerifiedScreen()
        {
            yield return Shutter();
            float secs = Time.unscaledTime - startTime;
            var o = Overlay();
            var col = Column(o, 0.46f);
            var badge = Img("Badge", col, sCircle, Ok, 0);
            Fixed(badge.rectTransform, 200, 200);
            DrawIcon(badge.rectTransform, St.Ok, 120, Screen0);
            Space(col, 40);
            Txt(col, "Installation verified", 68, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            Space(col, 8);
            Txt(col, L.cableLabel + "  ·  " + L.doorName, 36, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
            Space(col, 60);

            var stats = HRow(col, 220);
            Fixed(stats, 900, 220);
            stats.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            Stat(stats, "4/4", "terminals correct");
            Stat(stats, issuesFixed.ToString(), "issues fixed");
            Stat(stats, checkedByYou.ToString(), "checked by you");
            Space(col, 40);

            var thumbs = HRow(col, 330);
            Fixed(thumbs, 900, 330);
            thumbs.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            Thumb(thumbs, readerTex, CropRect(L.readerTerminalPos, readerTex), "Reader end");
            Thumb(thumbs, controllerFixedTex != null ? controllerFixedTex : controllerTex,
                CropRect(ControllerPositions, controllerFixedTex != null ? controllerFixedTex : controllerTex), "Controller end");
            Space(col, 24);
            Txt(col, "Checked in " + Mathf.FloorToInt(secs / 60f) + ":" + Mathf.FloorToInt(secs % 60f).ToString("00"),
                30, Faint, FontStyles.Normal, TextAlignmentOptions.Center);
            Space(col, 60);
            var row = HRow(col, 140);
            Fixed(row, 900, 140);
            Btn(row, "Save report", false, "save");
            Btn(row, "Next cable", true, "next");

            yield return Fade(o.GetComponent<CanvasGroup>(), 0, 1, 0.4f);
            while (true)
            {
                yield return WaitClick();
                if (clicked == "save") { yield return Shutter(); Toast("Report saved · 2 snapshots, 4 terminals"); }
                if (clicked == "next") { recorded = null; pendingRestart = 0; yield break; }
            }
        }

        // ================================================================== shared steps

        IEnumerator FindLabel(Vector2 pos, bool controller)
        {
            Instruction("Point the camera at the cable label", Accent);
            Sheet("Find the cable label", controller
                ? "The label links this end to the record from the reader end."
                : L.handwrittenLabels ? "The handwritten label links this cable to its demo record."
                : "The QR label tells the app which cable this is and how to check it.");
            ProgressLine("Looking for the " + LabelDescription, true);
            yield return Scan(controller ? 1.1f : 1.8f);
            ShowQr(pos);
            yield return Wait(0.25f);
            ShowChip();
            Instruction("Label found · hold steady", Ok);
            yield return Wait(0.9f);
            qrBox.gameObject.SetActive(false);
        }

        IEnumerator Scan(float seconds)
        {
            scanLine.gameObject.SetActive(true);
            yield return Wait(seconds);
            scanLine.gameObject.SetActive(false);
        }

        IEnumerator ReadTerminals(Image bar, float seconds, bool quick)
        {
            foreach (var t in terms) SetState(t, St.Reading);
            scanLine.gameObject.SetActive(true);
            yield return Tween(seconds, k => bar.fillAmount = Ease(k));
            scanLine.gameObject.SetActive(false);
            Done(bar);
        }

        IEnumerator Progress(Image bar, float seconds)
        {
            yield return Tween(seconds, k => bar.fillAmount = Ease(k));
            Done(bar);
        }

        void Done(Image bar)
        {
            bar.fillAmount = 1f;
            bar.color = Ok;
            var label = bar.transform.parent.parent.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null && !label.text.EndsWith(" · done")) label.text += " · done";
        }

        IEnumerator ColourPicker(Term t)
        {
            var m = Modal();
            var card = Card(m, 940);
            var head = HRow(card, 80);
            var badge = Img("Badge", head, sCircle, Warn, 0);
            Fixed(badge.rectTransform, 64, 64);
            DrawIcon(badge.rectTransform, St.Check, 44, Screen0);
            Txt(head, "Which colour is on " + t.name + "?", 44, Ink, FontStyles.Bold, TextAlignmentOptions.Left)
                .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Txt(card, "AI read " + WireNames[t.colour].ToLower() + " (" + t.confidence + "% sure). Tap the right colour.",
                32, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
            var grid = Rect("Grid", card);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(196, 104);
            gl.spacing = new Vector2(18, 18);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 4;
            grid.gameObject.AddComponent<LayoutElement>().preferredHeight = 104 * 2 + 18;
            for (int c = 0; c < 8; c++)
            {
                int cc = c;
                var chip = Img("Colour", grid, sRound, c == t.colour ? Hex("#4CC3FF", 0.22f) : GlassLight, 26);
                MakeButton(chip, () => { clicked = "colour:" + cc; });
                var hr = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
                hr.padding = new RectOffset(20, 12, 0, 0); hr.spacing = 14; hr.childAlignment = TextAnchor.MiddleLeft;
                hr.childControlWidth = true; hr.childControlHeight = true; hr.childForceExpandWidth = false; hr.childForceExpandHeight = false;
                Swatch(chip.rectTransform, c, 36);
                Txt(chip.rectTransform, WireNames[c], 32, Ink, FontStyles.Normal, TextAlignmentOptions.Left);
            }
            var btns = HRow(card, 130);
            Btn(btns, "Cancel", false, "cancel");
            yield return OpenModal(m);
            clicked = null;
            while (clicked == null || !(clicked == "cancel" || clicked == "dismiss" || clicked.StartsWith("colour:"))) yield return null;
            if (clicked.StartsWith("colour:"))
            {
                t.colour = int.Parse(clicked.Substring(7));
                t.confidence = 100;
                SetState(t, St.Edited);
            }
            yield return CloseModal(m);
        }

        IEnumerator Detail(Term t)
        {
            HighlightSnapshot(t.index);
            var m = Modal();
            var card = Card(m, 940);
            var head = HRow(card, 80);
            var badge = Img("Badge", head, sCircle, StateColour(t.state), 0);
            Fixed(badge.rectTransform, 64, 64);
            DrawIcon(badge.rectTransform, t.state, 44, Screen0);
            string title = t.name + "  —  " + (t.state == St.Wrong ? "wrong wire" : t.state == St.Check ? "please check"
                : t.state == St.YouConfirmed ? "confirmed by you" : "correct");
            Txt(head, title, 44, Ink, FontStyles.Bold, TextAlignmentOptions.Left).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            ColourLine(card, "Expected", t.expected, "as on reader " + ReaderNames[t.index]);
            ColourLine(card, "Found", t.colour, t.rulesOk ? "matches the record" : "does not match");
            KeyValue(card, "Rules", t.rulesOk ? "match" : "mismatch");
            KeyValue(card, "AI second opinion", t.aiOk ? "match" : "mismatch");
            if (t.state == St.Wrong || t.state == St.Check)
            {
                Space(card, 4);
                var fix = Img("Fix", card, sRound, Hex(t.state == St.Wrong ? "#FF5A5F" : "#FFB020", 0.14f), 22);
                var vl = fix.gameObject.AddComponent<VerticalLayoutGroup>();
                vl.padding = new RectOffset(28, 28, 22, 22); vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;
                Txt(fix.rectTransform, (t.state == St.Wrong ? "Fix: " : "What to do: ") + t.fix, 34, Ink, FontStyles.Normal, TextAlignmentOptions.Left);
            }
            var snapTitle = Txt(card, "READER END \u00b7 SAME WIRE", 24, Muted, FontStyles.Bold, TextAlignmentOptions.Left);
            snapTitle.characterSpacing = 3;
            var crop = Img("Snapshot", card, sRound, Hex("#000000", 0.5f), 24);
            crop.gameObject.AddComponent<LayoutElement>().preferredHeight = 520;
            var cropHolder = Rect("Holder", crop.rectTransform);
            Stretch(cropHolder, 10, 10, 10, 10);
            BuildCrop(cropHolder, t.index, null);
            var btns = HRow(card, 130);
            if (t.state == St.Check)
            {
                Btn(btns, "It's wrong", false, "itswrong");
                Btn(btns, "Looks right", true, "looksright");
            }
            else Btn(btns, "Close", true, "close");

            yield return OpenModal(m);
            clicked = null;
            while (clicked == null || !(clicked == "close" || clicked == "dismiss" || clicked == "looksright" || clicked == "itswrong")) yield return null;
            if (clicked == "looksright") SetState(t, St.YouConfirmed);
            if (clicked == "itswrong")
            {
                t.fix = "Re-seat the " + WireNames[t.expected].ToLower() + " wire in " + t.name + " and rescan.";
                SetState(t, St.Wrong);
            }
            yield return CloseModal(m);
        }

        // ================================================================== UI building

        void BuildUI()
        {
            var go = new GameObject("InstallerAssistantDemo UI", typeof(RectTransform));
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2340);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            canvasRT = (RectTransform)go.transform;

            // camera view
            var bg = Img("Camera", canvasRT, null, Color.black, 0);
            Stretch(bg.rectTransform);
            bg.gameObject.AddComponent<RectMask2D>();
            mover = Rect("Mover", bg.rectTransform);
            Stretch(mover);
            photo = new GameObject("Photo", typeof(RectTransform)).AddComponent<RawImage>();
            photo.transform.SetParent(mover, false);
            photo.raycastTarget = false;
            photoRT = photo.rectTransform;
            Stretch(photoRT);
            photoFit = photo.gameObject.AddComponent<AspectRatioFitter>();
            photoFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            markers = Rect("Markers", photoRT);
            Stretch(markers);

            // readability gradients
            var top = Img("TopShade", canvasRT, sFadeDown, Hex("#000000", 0.75f), 0);
            Place(top.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -260), new Vector2(4000, 520));
            var bot = Img("BottomShade", canvasRT, sFadeUp, Hex("#000000", 0.6f), 0);
            Place(bot.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 450), new Vector2(4000, 900));

            // scan line
            scanLine = Rect("ScanLine", canvasRT);
            scanLine.anchorMin = new Vector2(0, 1); scanLine.anchorMax = new Vector2(1, 1);
            scanLine.sizeDelta = new Vector2(0, 90);
            scanLine.gameObject.AddComponent<CanvasGroup>();
            var glow = Img("Glow", scanLine, sSoft, Hex("#4CC3FF", 0.35f), 0); Stretch(glow.rectTransform);
            var core = Img("Core", scanLine, null, Hex("#BFEAFF", 0.9f), 0);
            Place(core.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000, 4));
            scanLine.gameObject.SetActive(false);

            // viewfinder corners
            Viewfinder();

            // flash
            var fl = Img("Flash", canvasRT, null, Color.white, 0);
            Stretch(fl.rectTransform);
            fl.raycastTarget = false;
            flashGroup = fl.gameObject.AddComponent<CanvasGroup>();
            flashGroup.alpha = 0; flashGroup.blocksRaycasts = false;

            // safe area holder for chrome
            safe = Rect("Safe", canvasRT);
            Stretch(safe);
            ApplySafeArea();

            // top bar
            var brand = Pill(safe, "Brand", new Vector2(0, 1), new Vector2(40, -70), new Vector2(0, 0.5f));
            var dot = Img("Dot", brand, sCircle, Accent, 0); Fixed(dot.rectTransform, 22, 22);
            Txt(brand, "Installer Assistant", 32, Ink, FontStyles.Bold, TextAlignmentOptions.Left);
            var step = Pill(safe, "Step", new Vector2(1, 1), new Vector2(-40, -70), new Vector2(1, 0.5f));
            stepText = Txt(step, "", 28, Muted, FontStyles.Bold, TextAlignmentOptions.Left);
            stepText.characterSpacing = 4;

            var ins = Pill(safe, "Instruction", new Vector2(0.5f, 1), new Vector2(0, -180), new Vector2(0.5f, 0.5f));
            instructionDot = Img("Dot", ins, sCircle, Accent, 0); Fixed(instructionDot.rectTransform, 22, 22);
            instructionText = Txt(ins, "", 34, Ink, FontStyles.Normal, TextAlignmentOptions.Left);
            instructionGroup = ins.gameObject.AddComponent<CanvasGroup>();
            instructionGroup.alpha = 0;

            var chip = Pill(safe, "Cable", new Vector2(0, 1), new Vector2(40, -290), new Vector2(0, 0.5f));
            var qrIcon = Img("QR", chip, sRound, Ink, 6); Fixed(qrIcon.rectTransform, 40, 40);
            var qrIn = Img("QRin", qrIcon.rectTransform, sRound, Screen0, 3); Place(qrIn.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20, 20));
            chipText = Txt(chip, "", 32, Ink, FontStyles.Bold, TextAlignmentOptions.Left);
            chipGroup = chip.gameObject.AddComponent<CanvasGroup>();
            chipGroup.alpha = 0;

            // QR detection box and missing-label guide
            qrBox = Rect("QRBox", markers);
            qrBox.sizeDelta = new Vector2(300, 300);
            Corners(qrBox, 300, 300, Ok, 70 * L.markerScale, 8 * L.markerScale);
            var qrLbl = Pill(qrBox, "QRLabel", new Vector2(0.5f, 1), new Vector2(0, 50), new Vector2(0.5f, 0.5f));
            qrLbl.localScale = Vector3.one * L.labelTextScale;
            Txt(qrLbl, (L.handwrittenLabels ? "Label · " : "QR · ") + L.cableLabel, 28, Ink, FontStyles.Bold, TextAlignmentOptions.Left);
            qrBox.gameObject.SetActive(false);

            missingGuide = Rect("MissingLabel", markers);
            missingGuide.sizeDelta = new Vector2(330, 300);
            missingGuide.gameObject.AddComponent<CanvasGroup>();
            Dashed(missingGuide, 330, 300, Warn);
            var mg = Pill(missingGuide, "MissingText", new Vector2(0.5f, 0), new Vector2(0, -60), new Vector2(0.5f, 0.5f));
            mg.localScale = Vector3.one * (L.labelTextScale / L.markerScale);
            Txt(mg, L.handwrittenLabels ? "Attach cable label here" : "Attach label " + L.cableLabel + " here", 30, Ink, FontStyles.Bold, TextAlignmentOptions.Left);
            missingGuide.gameObject.SetActive(false);

            // bottom sheet
            BuildSheet();

            // toast
            var toast = Pill(safe, "Toast", new Vector2(0.5f, 1), new Vector2(0, -400), new Vector2(0.5f, 0.5f));
            toastText = Txt(toast, "", 32, Ink, FontStyles.Normal, TextAlignmentOptions.Left);
            var tdot = Img("Dot", toast, sCircle, Ok, 0); Fixed(tdot.rectTransform, 22, 22); tdot.transform.SetAsFirstSibling();
            toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0; toastGroup.blocksRaycasts = false;

            // operator hint + calibration
            hintText = Txt(canvasRT, "R restart · 1 reader end · 2 controller end · K calibrate · H hide", 24,
                Hex("#FFFFFF", 0.45f), FontStyles.Normal, TextAlignmentOptions.Center);
            Place(hintText.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(1000, 34));
            hintText.gameObject.SetActive(showOperatorHint);

            var cal = Img("Calibrate", canvasRT, null, Hex("#000000", 0.01f), 0);
            cal.raycastTarget = true;
            Stretch(cal.rectTransform);
            cal.gameObject.AddComponent<ClickCatcher>().onClick = OnCalibrationClick;
            calibGroup = cal.gameObject.AddComponent<CanvasGroup>();
            var cp = Pill(cal.rectTransform, "CalibText", new Vector2(0.5f, 1), new Vector2(0, -300), new Vector2(0.5f, 0.5f));
            cp.GetComponent<Image>().color = Hex("#3A2A00", 0.92f);
            calibText = Txt(cp, "", 32, Ink, FontStyles.Bold, TextAlignmentOptions.Left);
            cal.gameObject.SetActive(false);
        }

        void BuildSheet()
        {
            sheet = Rect("Sheet", canvasRT);
            sheet.anchorMin = sheet.anchorMax = new Vector2(0.5f, 0);
            sheet.pivot = new Vector2(0.5f, 0);
            sheet.sizeDelta = new Vector2(1080, 400);
            var shadow = Img("Shadow", sheet, sSoft, Hex("#000000", 0.55f), 0);
            Stretch(shadow.rectTransform, -40, -120, -40, 40);
            shadow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var bgi = Img("Background", sheet, sRound, Glass, 56);
            Stretch(bgi.rectTransform, 0, -120, 0, 0);
            bgi.raycastTarget = true;   // taps on the sheet must not reach the markers behind it
            bgi.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var vl = sheet.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(56, 56, 26, 70);
            vl.spacing = 22;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            var fit = sheet.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var handleRow = Rect("Handle", sheet);
            handleRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 12;
            var handle = Img("Bar", handleRow, sRound, Hex("#FFFFFF", 0.22f), 6);
            Place(handle.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110, 10));

            var body = Rect("Body", sheet);
            var bvl = body.gameObject.AddComponent<VerticalLayoutGroup>();
            bvl.spacing = 22; bvl.childControlWidth = true; bvl.childControlHeight = true;
            bvl.childForceExpandWidth = true; bvl.childForceExpandHeight = false;
            sheetGroup = body.gameObject.AddComponent<CanvasGroup>();
            sheetTitle = Txt(body, "", 50, Ink, FontStyles.Bold, TextAlignmentOptions.Left);
            sheetSub = Txt(body, "", 34, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
            sheetContent = Rect("Content", body);
            var cvl = sheetContent.gameObject.AddComponent<VerticalLayoutGroup>();
            cvl.spacing = 14; cvl.childControlWidth = true; cvl.childControlHeight = true;
            cvl.childForceExpandWidth = true; cvl.childForceExpandHeight = false;
            sheetButtons = HRow(body, 140);
            sheetButtons.GetComponent<HorizontalLayoutGroup>().spacing = 24;
            sheet.gameObject.SetActive(false);
        }

        void Sheet(string title, string sub)
        {
            sheet.gameObject.SetActive(true);
            sheetTitle.text = title;
            sheetSub.text = sub;
            Clear(sheetContent);
            Clear(sheetButtons);
            StartCoroutine(Fade(sheetGroup, 0.2f, 1f, 0.2f));
        }

        Image ProgressLine(string label, bool indeterminate = false)
        {
            var row = Rect("Progress", sheetContent);
            var vl = row.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 12; vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandHeight = false;
            Txt(row, label, 32, Ink, FontStyles.Normal, TextAlignmentOptions.Left);
            var track = Img("Track", row, sRound, Hex("#FFFFFF", 0.10f), 8);
            track.gameObject.AddComponent<LayoutElement>().preferredHeight = 14;
            var fill = Img("Fill", track.rectTransform, sRound, Accent, 8);
            Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0;
            if (indeterminate) StartCoroutine(Indeterminate(fill));
            return fill;
        }

        IEnumerator Indeterminate(Image fill)
        {
            while (fill != null && fill.color != Ok)
            {
                fill.fillAmount = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
                yield return null;
            }
        }

        void Row(Term t)
        {
            var row = Img("Row " + t.name, sheetContent, sRound, GlassLight, 28);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 112;
            var term = t;
            MakeButton(row, () => { if (!modalOpen) tapped = term; });
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(26, 30, 0, 0); hl.spacing = 22; hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;

            var badge = Img("Badge", row.rectTransform, sCircle, StateColour(t.state), 0);
            Fixed(badge.rectTransform, 56, 56);
            DrawIcon(badge.rectTransform, t.state, 38, Screen0);
            var nm = Txt(row.rectTransform, t.name, 38, Ink, FontStyles.Bold, TextAlignmentOptions.Left);
            nm.gameObject.AddComponent<LayoutElement>().preferredWidth = 110;
            Swatch(row.rectTransform, t.colour, 34);
            var cn = Txt(row.rectTransform, WireNames[t.colour], 34, Ink, FontStyles.Normal, TextAlignmentOptions.Left);
            cn.gameObject.AddComponent<LayoutElement>().preferredWidth = 170;
            var dt = Txt(row.rectTransform, RowDetail(t), 30, StateText(t.state), FontStyles.Normal, TextAlignmentOptions.Right);
            dt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        string RowDetail(Term t)
        {
            if (!atController)
            {
                if (t.state == St.Check) return "unsure · " + t.confidence + "%";
                if (t.state == St.Edited) return "set by you";
                return t.confidence + "%";
            }
            switch (t.state)
            {
                case St.Wrong: return "should be " + WireNames[t.expected].ToLower();
                case St.Check: return "AI disagrees · check";
                case St.YouConfirmed: return "confirmed by you";
                default: return "matches reader " + ReaderNames[t.index];
            }
        }

        // ------------------------------------------------------------------ markers

        void BuildTerms(bool controller)
        {
            foreach (var t in terms) if (t.root != null) Destroy(t.root.gameObject);
            terms.Clear();
            Vector2[] pos = controller ? ControllerPositions : L.readerTerminalPos;
            string[] names = controller ? ControllerNames : ReaderNames;
            for (int i = 0; i < 4; i++)
            {
                var t = new Term { index = i, name = names[i], pos = Get(pos, i, new Vector2(0.4f + 0.06f * i, 0.3f)) };
                t.colour = Mathf.Clamp(Get(L.readerColours, i, i), 0, 7);
                BuildMarker(t);
                terms.Add(t);
                SetState(t, St.Hidden);
            }
        }

        void BuildMarker(Term t)
        {
            t.root = Rect("Terminal " + t.name, markers);
            t.root.anchorMin = t.root.anchorMax = new Vector2(t.pos.x, 1f - t.pos.y);
            t.root.sizeDelta = Vector2.zero;

            t.leader = Img("Leader", t.root, null, Hex("#FFFFFF", 0.7f), 0).rectTransform;
            t.leader.pivot = new Vector2(0.5f, 0.5f);

            var markerSize = Rect("MarkerSize", t.root);
            markerSize.localScale = Vector3.one * L.markerScale;
            t.vis = Rect("Vis", markerSize);
            t.pulse = Img("Pulse", t.vis, sRing, Ok, 0).rectTransform;
            t.pulse.sizeDelta = new Vector2(96, 96);
            var halo = Img("Halo", t.vis, sCircle, Hex("#000000", 0.45f), 0);
            halo.rectTransform.sizeDelta = new Vector2(76, 76);
            t.disk = Img("Disk", t.vis, sCircle, Ok, 0);
            t.disk.rectTransform.sizeDelta = new Vector2(62, 62);
            t.arc = Img("Arc", t.vis, sRing, Accent, 0);
            t.arc.rectTransform.sizeDelta = new Vector2(70, 70);
            t.arc.type = Image.Type.Filled; t.arc.fillMethod = Image.FillMethod.Radial360; t.arc.fillAmount = 0.3f;
            t.icon = Rect("Icon", t.disk.rectTransform);
            Stretch(t.icon);
            var hit = Img("Hit", t.vis, null, new Color(0, 0, 0, 0), 0);
            hit.rectTransform.sizeDelta = new Vector2(130, 130);
            var term = t;
            MakeButton(hit, () => { if (!modalOpen && !calibrating) tapped = term; });

            t.tag = Rect("Tag", t.root);
            t.tag.localScale = Vector3.one * L.labelTextScale;
            t.tag.sizeDelta = new Vector2(TagW, 70);
            t.tagBg = Img("Bg", t.tag, sRound, Glass, 35);
            Stretch(t.tagBg.rectTransform);
            t.tagSwatch = Img("Swatch", t.tag, sCircle, Color.white, 0);
            Place(t.tagSwatch.rectTransform, new Vector2(0, 0.5f), new Vector2(38, 0), new Vector2(30, 30));
            t.tagText = Txt(t.tag, "", 30, Ink, FontStyles.Bold, TextAlignmentOptions.Left);
            Place(t.tagText.rectTransform, new Vector2(0, 0.5f), new Vector2(66, 0), new Vector2(TagW - 84, 60), new Vector2(0, 0.5f));
            t.tagText.textWrappingMode = TextWrappingModes.NoWrap;
            t.tagText.enableAutoSizing = true;
            t.tagText.fontSizeMin = 20; t.tagText.fontSizeMax = 30;
            MakeButton(t.tagBg, () => { if (!modalOpen && !calibrating) tapped = term; });
        }

        void SetState(Term t, St s)
        {
            t.state = s;
            bool shown = s != St.Hidden;
            t.root.gameObject.SetActive(shown);
            if (!shown) return;
            bool reading = s == St.Reading;
            t.arc.gameObject.SetActive(reading);
            t.disk.color = reading ? Hex("#0E1318", 0.9f) : StateColour(s);
            DrawIcon(t.icon, reading ? St.Hidden : s, 42, Screen0);
            t.tag.gameObject.SetActive(!reading);
            t.leader.gameObject.SetActive(!reading);
            t.pulse.GetComponent<Image>().color = StateColour(s);
            t.tagSwatch.color = WireColours[t.colour];
            t.tagText.text = t.name + "  " + WireNames[t.colour];
            t.tagBg.color = s == St.Wrong ? Hex("#3A1214", 0.92f) : s == St.Check ? Hex("#3A2A06", 0.92f) : Glass;
            if (!reading) StartCoroutine(Pop(t.vis));
        }

        void AnimateMarker(Term t, float time)
        {
            if (t.root == null || !t.root.gameObject.activeSelf) return;
            float inv = 1f / Scale();
            t.root.localScale = new Vector3(inv, inv, 1f);
            if (t.state == St.Reading) t.arc.rectTransform.localEulerAngles = new Vector3(0, 0, -time * 360f);
            bool alert = t.state == St.Wrong || t.state == St.Check;
            float k = Mathf.Repeat(time * (alert ? 1.3f : 0.6f) + t.index * 0.17f, 1f);
            float s = 1f + k * (alert ? 0.8f : 0.4f);
            t.pulse.localScale = new Vector3(s, s, 1f);
            var c = t.pulse.GetComponent<Image>().color;
            c.a = (1f - k) * (t.state == St.Reading ? 0f : alert ? 0.9f : 0.45f);
            t.pulse.GetComponent<Image>().color = c;
        }

        /// <summary>Places the tags so they never overlap: a row above horizontal terminals, a column beside vertical ones.</summary>
        void LayoutTags()
        {
            if (terms.Count == 0 || canvasRT == null) return;
            var pts = new Vector2[terms.Count];
            float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f, meanX = 0;
            for (int i = 0; i < terms.Count; i++)
            {
                pts[i] = canvasRT.InverseTransformPoint(terms[i].root.position);
                minX = Mathf.Min(minX, pts[i].x); maxX = Mathf.Max(maxX, pts[i].x);
                minY = Mathf.Min(minY, pts[i].y); maxY = Mathf.Max(maxY, pts[i].y);
                meanX += pts[i].x / terms.Count;
            }
            float halfW = canvasRT.rect.width * 0.5f;
            bool row = (maxX - minX) >= (maxY - minY);
            // slot = rank along the terminal row (left to right) or column (top to bottom),
            // so leader lines never cross whatever order the terminals have on the board
            var slot = new int[terms.Count];
            for (int i = 0; i < terms.Count; i++)
                for (int j = 0; j < terms.Count; j++)
                {
                    if (i == j) continue;
                    bool before = row ? (pts[j].x < pts[i].x || (pts[j].x == pts[i].x && j < i))
                                      : (pts[j].y > pts[i].y || (pts[j].y == pts[i].y && j < i));
                    if (before) slot[i]++;
                }

            // column mode: each tag level with its own terminal, pushed apart only if they would overlap
            var colY = new float[terms.Count];
            if (!row)
            {
                var bySlot = new int[terms.Count];
                for (int i = 0; i < terms.Count; i++) bySlot[slot[i]] = i;
                float before = 0, after = 0;
                for (int k = 0; k < terms.Count; k++)
                {
                    int i = bySlot[k];
                    colY[i] = pts[i].y;
                    if (k > 0) colY[i] = Mathf.Min(colY[i], colY[bySlot[k - 1]] - 82f * L.labelTextScale);
                    before += pts[i].y; after += colY[i];
                }
                float shift = (before - after) / terms.Count;
                for (int i = 0; i < terms.Count; i++) colY[i] += shift;
            }
            float pitch = TagPitch * L.labelTextScale;
            bool tagsBelow = !atController && L.readerTagsBelowTerminals;
            for (int i = 0; i < terms.Count; i++)
            {
                var t = terms[i];
                Vector2 target;
                if (row)
                {
                    float span = pitch * (terms.Count - 1) + TagW * L.labelTextScale;
                    float c = Mathf.Clamp(meanX, -halfW + span * 0.5f + 20f, halfW - span * 0.5f - 20f);
                    float y = tagsBelow ? minY - 170f * L.markerScale : maxY + 170f * L.markerScale;
                    target = new Vector2(c + (slot[i] - (terms.Count - 1) * 0.5f) * pitch, y);
                }
                else
                {
                    bool right = meanX < 0;
                    float x = right ? maxX + 210f * L.labelTextScale : minX - 210f * L.labelTextScale;
                    target = new Vector2(x, colY[i]);
                }
                Vector2 off = target - pts[i];
                t.tag.anchoredPosition = off;
                Vector2 from = off.normalized * 40f * L.markerScale;
                Vector2 to = off - (row ? new Vector2(0, tagsBelow ? -35f : 35f) : new Vector2(Mathf.Sign(off.x) * TagW * 0.5f, 0f)) * L.labelTextScale;
                Vector2 d = to - from;
                t.leader.anchoredPosition = (from + to) * 0.5f;
                t.leader.sizeDelta = new Vector2(d.magnitude, 3f);
                t.leader.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
        }

        // ------------------------------------------------------------------ chrome helpers

        void SetPhoto(Texture2D tex)
        {
            photo.texture = tex;
            photoFit.aspectRatio = tex.width / (float)tex.height;
            controllerPlacement = null;
            if (atController)
            {
                var candidate = tex == controllerNoLabelTex ? L.controllerNoLabelPlacement
                    : tex == controllerFixedTex ? L.controllerFixedPlacement : null;
                if (candidate != null && candidate.enabled) controllerPlacement = candidate;
                // Preserve each terminal's identity and state while moving it to the new photograph.
                foreach (var t in terms)
                {
                    t.pos = Get(ControllerPositions, t.index, t.pos);
                    t.root.anchorMin = t.root.anchorMax = new Vector2(t.pos.x, 1f - t.pos.y);
                }
            }
        }

        void Instruction(string text, Color dot)
        {
            instructionText.text = text;
            instructionDot.color = dot;
            if (instructionGroup.alpha < 1f) StartCoroutine(Fade(instructionGroup, 0, 1, 0.25f));
            StartCoroutine(Pop(instructionGroup.transform));
        }

        void ShowChip()
        {
            chipText.text = L.cableLabel + "   <color=#A3ADB7>reader cable</color>";
            StartCoroutine(Fade(chipGroup, 0, 1, 0.3f));
            StartCoroutine(Pop(chipGroup.transform));
        }

        void HideChip() { chipGroup.alpha = 0; }

        void ShowQr(Vector2 pos)
        {
            qrBox.anchorMin = qrBox.anchorMax = new Vector2(pos.x, 1f - pos.y);
            qrBox.anchoredPosition = Vector2.zero;
            if (L.handwrittenLabels)
            {
                var caption = (RectTransform)qrBox.Find("QRLabel");
                float edge = pos.x > 0.5f ? 1f : 0f;
                caption.anchorMin = caption.anchorMax = new Vector2(edge, 1);
                caption.pivot = new Vector2(edge, 0.5f);
            }
            qrBox.gameObject.SetActive(true);
        }

        void ShowMissingGuide(bool on)
        {
            Vector2 pos = ControllerLabelPosition;
            missingGuide.anchorMin = missingGuide.anchorMax = new Vector2(pos.x, 1f - pos.y);
            missingGuide.anchoredPosition = Vector2.zero;
            missingGuide.gameObject.SetActive(on);
        }

        void Toast(string text)
        {
            toastText.text = text;
            if (toastCo != null) StopCoroutine(toastCo);
            toastCo = StartCoroutine(ToastRoutine());
        }

        IEnumerator ToastRoutine()
        {
            yield return Fade(toastGroup, toastGroup.alpha, 1, 0.2f);
            yield return new WaitForSecondsRealtime(2.4f);
            yield return Fade(toastGroup, 1, 0, 0.35f);
        }

        IEnumerator Shutter()
        {
            flashGroup.alpha = 0.85f;
            yield return Fade(flashGroup, 0.85f, 0, 0.35f);
        }

        void Viewfinder()
        {
            var vf = Rect("Viewfinder", canvasRT);
            vf.anchorMin = new Vector2(0, 0); vf.anchorMax = new Vector2(1, 1);
            vf.offsetMin = new Vector2(70, 560); vf.offsetMax = new Vector2(-70, -560);
            Corners(vf, 0, 0, Hex("#FFFFFF", 0.55f), 80, 6);
        }

        /// <summary>Four L-shaped corner brackets. w/h = 0 means: stretch to the parent.</summary>
        void Corners(RectTransform parent, float w, float h, Color c, float len, float thick)
        {
            for (int i = 0; i < 4; i++)
            {
                var a = new Vector2(i % 2, i / 2);
                var horiz = Img("C", parent, sRound, c, thick * 0.5f);
                var vert = Img("C", parent, sRound, c, thick * 0.5f);
                horiz.rectTransform.anchorMin = horiz.rectTransform.anchorMax = a;
                vert.rectTransform.anchorMin = vert.rectTransform.anchorMax = a;
                horiz.rectTransform.pivot = a; vert.rectTransform.pivot = a;
                horiz.rectTransform.sizeDelta = new Vector2(len, thick);
                vert.rectTransform.sizeDelta = new Vector2(thick, len);
            }
        }

        void Dashed(RectTransform parent, float w, float h, Color c)
        {
            const float dash = 26, gap = 16, th = 6;
            for (float x = -w / 2; x < w / 2 - dash / 2; x += dash + gap)
            {
                Seg(parent, new Vector2(x + dash / 2, h / 2), new Vector2(dash, th), c);
                Seg(parent, new Vector2(x + dash / 2, -h / 2), new Vector2(dash, th), c);
            }
            for (float y = -h / 2; y < h / 2 - dash / 2; y += dash + gap)
            {
                Seg(parent, new Vector2(w / 2, y + dash / 2), new Vector2(th, dash), c);
                Seg(parent, new Vector2(-w / 2, y + dash / 2), new Vector2(th, dash), c);
            }
        }

        void Seg(RectTransform parent, Vector2 p, Vector2 size, Color c)
        {
            var s = Img("Dash", parent, sRound, c, 3);
            Place(s.rectTransform, new Vector2(0.5f, 0.5f), p, size);
        }

        // ------------------------------------------------------------------ snapshot panel

        Rect CropRect(Vector2[] pts, Texture2D tex)
        {
            float x0 = 1, x1 = 0, y0 = 1, y1 = 0;
            foreach (var p in pts) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y); }
            float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            float aspect = tex.width / (float)tex.height;
            float w = Mathf.Max(x1 - x0 + 0.22f, 0.3f);
            float hgt = w * aspect * 3f / 4f;
            hgt = Mathf.Max(hgt, y1 - y0 + 0.12f);
            w = hgt / aspect * 4f / 3f;
            w = Mathf.Min(w, 1f); hgt = Mathf.Min(hgt, 1f);
            float u0 = Mathf.Clamp(cx - w / 2, 0, 1 - w), v0 = Mathf.Clamp(cy - hgt / 2, 0, 1 - hgt);
            return new Rect(u0, 1f - v0 - hgt, w, hgt);   // RawImage uvRect (origin bottom-left)
        }

        void BuildSnapshot()
        {
            if (snapshot != null) Destroy(snapshot.gameObject);
            snapshotDots.Clear();
            snapshotUv = CropRect(L.readerTerminalPos, readerTex);
            snapshot = Rect("Snapshot", safe);
            snapshot.anchorMin = snapshot.anchorMax = new Vector2(L.snapshotOnLeft ? 0 : 1, 1);
            snapshot.pivot = snapshot.anchorMax;
            snapshot.anchoredPosition = new Vector2(L.snapshotOnLeft ? L.snapshotInset : -L.snapshotInset, -L.snapshotTopInset);
            snapshot.sizeDelta = new Vector2(380, 330);
            snapshotGroup = snapshot.gameObject.AddComponent<CanvasGroup>();
            var shadow = Img("Shadow", snapshot, sSoft, Hex("#000000", 0.6f), 0);
            Stretch(shadow.rectTransform, -30, -40, -30, 20);
            var frame = Img("Frame", snapshot, sRound, Glass, 30);
            Stretch(frame.rectTransform);
            MakeButton(frame, ToggleSnapshot);
            var title = Txt(snapshot, "READER END \u00b7 RECORDED", 22, Muted, FontStyles.Bold, TextAlignmentOptions.Left);
            title.characterSpacing = 3;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(22, -26), new Vector2(340, 30), new Vector2(0, 0.5f));
            var holder = Rect("Image", snapshot);
            Stretch(holder, 12, 12, 12, 52);
            BuildCrop(holder, -1, snapshotDots);
            snapshotHighlight = Img("Highlight", snapshot, sRing, Color.white, 0).rectTransform;
            snapshotHighlight.sizeDelta = new Vector2(70, 70);
            snapshotHighlight.gameObject.SetActive(false);
            pulseRings.Add(snapshotHighlight);
            snapshot.gameObject.SetActive(false);
        }

        /// <summary>Reader-end crop with a dot and name on each terminal; optionally one pulsing ring.</summary>
        void BuildCrop(RectTransform holder, int highlight, List<RectTransform> dotsOut)
        {
            holder.gameObject.AddComponent<RectMask2D>();
            var img = new GameObject("Crop", typeof(RectTransform)).AddComponent<RawImage>();
            img.transform.SetParent(holder, false);
            img.raycastTarget = false;
            img.texture = readerTex;
            img.uvRect = snapshotUv;
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 4f / 3f;
            var root = Rect("Dots", img.rectTransform);
            Stretch(root);
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = Get(L.readerTerminalPos, i, Vector2.one * 0.5f);
                float u = (p.x - snapshotUv.x) / snapshotUv.width;
                float v = ((1f - p.y) - snapshotUv.y) / snapshotUv.height;
                var dot = Rect("T" + i, root);
                dot.anchorMin = dot.anchorMax = new Vector2(u, v);
                var ring = Img("Ring", dot, sCircle, Hex("#FFFFFF", 0.95f), 0); ring.rectTransform.sizeDelta = new Vector2(30, 30);
                var sw = Img("Sw", dot, sCircle, WireColours[recorded != null ? recorded[i] : i], 0); sw.rectTransform.sizeDelta = new Vector2(22, 22);
                var nm = Txt(dot, ReaderNames[i], 22, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
                Place(nm.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -34), new Vector2(60, 28));
                if (dotsOut != null) dotsOut.Add(dot);
                if (i == highlight)
                {
                    var hl = Img("Highlight", dot, sRing, Color.white, 0).rectTransform;
                    hl.sizeDelta = new Vector2(70, 70);
                    pulseRings.Add(hl);
                }
            }
        }

        void ShowSnapshot(bool on)
        {
            snapshot.gameObject.SetActive(on);
            if (on) { StartCoroutine(Fade(snapshotGroup, 0, 1, 0.3f)); StartCoroutine(Pop(snapshot)); }
        }

        void ToggleSnapshot()
        {
            snapshotExpanded = !snapshotExpanded;
            float inset = snapshotExpanded ? 40f : L.snapshotInset;
            StartCoroutine(ResizeSnapshot(snapshotExpanded ? new Vector2(1000, 840) : new Vector2(380, 330),
                new Vector2(L.snapshotOnLeft ? inset : -inset, snapshotExpanded ? -150 : -L.snapshotTopInset)));
        }

        IEnumerator ResizeSnapshot(Vector2 size, Vector2 pos)
        {
            Vector2 s0 = snapshot.sizeDelta, p0 = snapshot.anchoredPosition;
            snapshot.SetAsLastSibling();
            yield return Tween(0.3f, k =>
            {
                float e = Ease(k);
                snapshot.sizeDelta = Vector2.Lerp(s0, size, e);
                snapshot.anchoredPosition = Vector2.Lerp(p0, pos, e);
            });
        }

        void HighlightSnapshot(int index)
        {
            if (snapshotHighlight == null) return;
            if (index < 0 || index >= snapshotDots.Count) { snapshotHighlight.gameObject.SetActive(false); return; }
            snapshotHighlight.SetParent(snapshotDots[index], false);
            snapshotHighlight.anchoredPosition = Vector2.zero;
            snapshotHighlight.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------ overlays and modals

        RectTransform Overlay()
        {
            var bg = Img("Overlay", canvasRT, null, Screen0, 0);
            bg.raycastTarget = true;
            Stretch(bg.rectTransform);
            var glowTop = Img("Glow", bg.rectTransform, sSoft, Hex("#4CC3FF", 0.10f), 0);
            Place(glowTop.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -200), new Vector2(1600, 900));
            var g = bg.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0;
            bg.rectTransform.SetAsLastSibling();
            return bg.rectTransform;
        }

        RectTransform Column(RectTransform parent, float y)
        {
            var col = Rect("Column", parent);
            col.anchorMin = col.anchorMax = new Vector2(0.5f, y);
            col.pivot = new Vector2(0.5f, 0.5f);
            col.sizeDelta = new Vector2(960, 0);
            var vl = col.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = false; vl.childForceExpandHeight = false;
            col.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return col;
        }

        RectTransform Modal()
        {
            modalOpen = true;
            var dim = Img("Modal", canvasRT, null, Hex("#000000", 0.6f), 0);
            Stretch(dim.rectTransform);
            MakeButton(dim, () => clicked = "dismiss");
            dim.gameObject.AddComponent<CanvasGroup>().alpha = 0;
            dim.rectTransform.SetAsLastSibling();
            var col = Column(dim.rectTransform, 0.42f);
            return col;
        }

        IEnumerator OpenModal(RectTransform col)
        {
            var g = col.parent.GetComponent<CanvasGroup>();
            StartCoroutine(Pop(col));
            yield return Fade(g, 0, 1, 0.2f);
        }

        IEnumerator CloseModal(RectTransform col)
        {
            var g = col.parent.GetComponent<CanvasGroup>();
            yield return Fade(g, 1, 0, 0.15f);
            Destroy(col.parent.gameObject);
            modalOpen = false;
            clicked = null;
        }

        RectTransform Card(RectTransform parent, float width)
        {
            var card = Img("Card", parent, sRound, Hex("#141B22", 0.98f), 44);
            var le = card.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width;
            var vl = card.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(48, 48, 44, 44);
            vl.spacing = 22;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            // Block clicks reaching the dimmer behind the card.
            card.raycastTarget = true;
            return card.rectTransform;
        }

        void KeyValue(RectTransform parent, string key, string value)
        {
            var row = HRow(parent, 52);
            var k = Txt(row, key, 32, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
            k.gameObject.AddComponent<LayoutElement>().preferredWidth = 300;
            var v = Txt(row, value, 32, Ink, FontStyles.Normal, TextAlignmentOptions.Left);
            v.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        void ColourLine(RectTransform parent, string key, int colour, string note)
        {
            var row = HRow(parent, 56);
            var k = Txt(row, key, 32, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
            k.gameObject.AddComponent<LayoutElement>().preferredWidth = 300;
            Swatch(row, colour, 34);
            var v = Txt(row, WireNames[colour] + "  <color=#6B7682>" + note + "</color>", 32, Ink, FontStyles.Normal, TextAlignmentOptions.Left);
            v.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        void Stat(RectTransform parent, string big, string small)
        {
            var box = Img("Stat", parent, sRound, GlassLight, 32);
            box.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var vl = box.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.MiddleCenter; vl.childControlHeight = true; vl.childControlWidth = true;
            vl.childForceExpandHeight = false; vl.spacing = 4;
            Txt(box.rectTransform, big, 64, Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            Txt(box.rectTransform, small, 28, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
        }

        void Thumb(RectTransform parent, Texture2D tex, Rect uv, string caption)
        {
            var box = Img("Thumb", parent, sRound, GlassLight, 28);
            box.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var holder = Rect("Mask", box.rectTransform);
            Stretch(holder, 12, 60, 12, 12);
            holder.gameObject.AddComponent<RectMask2D>();
            var img = new GameObject("Img", typeof(RectTransform)).AddComponent<RawImage>();
            img.transform.SetParent(holder, false);
            Stretch(img.rectTransform);
            img.texture = tex; img.uvRect = uv;
            var fit = img.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 4f / 3f;
            var cap = Txt(box.rectTransform, caption, 28, Muted, FontStyles.Normal, TextAlignmentOptions.Center);
            Place(cap.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(400, 40));
        }

        // ------------------------------------------------------------------ calibration (operator)

        void HandleKeys()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.rKey.wasPressedThisFrame) { recorded = null; Restart(0); }
            if (kb.digit1Key.wasPressedThisFrame) Restart(1);
            if (kb.digit2Key.wasPressedThisFrame) Restart(2);
            if (kb.hKey.wasPressedThisFrame && hintText != null) hintText.gameObject.SetActive(!hintText.gameObject.activeSelf);
            if (kb.kKey.wasPressedThisFrame) ToggleCalibration();
            if (kb.escapeKey.wasPressedThisFrame && calibrating) ToggleCalibration();
#else
            if (Input.GetKeyDown(KeyCode.R)) { recorded = null; Restart(0); }
            if (Input.GetKeyDown(KeyCode.Alpha1)) Restart(1);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Restart(2);
            if (Input.GetKeyDown(KeyCode.H) && hintText != null) hintText.gameObject.SetActive(!hintText.gameObject.activeSelf);
            if (Input.GetKeyDown(KeyCode.K)) ToggleCalibration();
            if (Input.GetKeyDown(KeyCode.Escape) && calibrating) ToggleCalibration();
#endif
        }

        void ToggleCalibration()
        {
            if (photo == null || photo.texture == null) return;
            calibrating = !calibrating;
            calibStep = 0;
            calibGroup.gameObject.SetActive(calibrating);
            calibGroup.transform.SetAsLastSibling();
            if (calibrating)
            {
                hiddenBeforeCalibration.Clear();
                foreach (var t in terms)
                    if (t.root != null && t.state == St.Hidden) { hiddenBeforeCalibration.Add(t); SetState(t, St.Ok); }
            }
            else
            {
                foreach (var t in hiddenBeforeCalibration) SetState(t, St.Hidden);
                hiddenBeforeCalibration.Clear();
            }
            UpdateCalibText();
        }

        void UpdateCalibText()
        {
            string[] names = atController ? ControllerNames : ReaderNames;
            string what = calibStep == 0 ? "the cable LABEL" : "terminal " + names[calibStep - 1];
            calibText.text = (atController ? "CONTROLLER" : "READER") + " END · click " + what + "  (" + (calibStep + 1) + "/5) · Esc to stop";
        }

        void OnCalibrationClick(PointerEventData e)
        {
            if (!calibrating) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(photoRT, e.position, null, out local)) return;
            Rect r = photoRT.rect;
            var p = new Vector2(Mathf.Clamp01((local.x - r.xMin) / r.width), Mathf.Clamp01(1f - (local.y - r.yMin) / r.height));
            if (calibStep == 0)
            {
                if (!atController) L.readerLabelPos = p;
                else if (controllerPlacement != null) controllerPlacement.labelPos = p;
                else L.controllerLabelPos = p;
                ShowQr(p);
            }
            else
            {
                int i = calibStep - 1;
                Vector2[] arr = atController ? ControllerPositions : L.readerTerminalPos;
                if (arr == null || arr.Length < 4)
                {
                    var n = new Vector2[4];
                    if (arr != null) Array.Copy(arr, n, arr.Length);
                    arr = n;
                    if (!atController) L.readerTerminalPos = arr;
                    else if (controllerPlacement != null) controllerPlacement.terminalPos = arr;
                    else L.controllerTerminalPos = arr;
                }
                arr[i] = p;
                terms[i].pos = p;
                terms[i].root.anchorMin = terms[i].root.anchorMax = new Vector2(p.x, 1f - p.y);
            }
#if UNITY_EDITOR
            if (layout != null) EditorUtility.SetDirty(layout);
#endif
            calibStep++;
            if (calibStep >= 5)
            {
                ToggleCalibration();
                qrBox.gameObject.SetActive(false);
                Toast(layout != null ? "Positions saved to " + layout.name : "Positions set (assign a DemoLayout asset to keep them)");
#if UNITY_EDITOR
                if (layout != null) AssetDatabase.SaveAssets();
#endif
                return;
            }
            UpdateCalibText();
        }

        // ------------------------------------------------------------------ waiting for input

        IEnumerator WaitClick()
        {
            clicked = null;
            while (clicked == null || clicked == "dismiss") yield return null;
        }

        IEnumerator WaitClickOrTap()
        {
            clicked = null; tapped = null;
            while ((clicked == null || clicked == "dismiss") && tapped == null) yield return null;
        }

        // ------------------------------------------------------------------ icons

        void DrawIcon(RectTransform parent, St s, float size, Color c)
        {
            Clear(parent);
            float th = size * 0.14f;
            switch (s)
            {
                case St.Ok:
                case St.YouConfirmed:
                    Bar(parent, new Vector2(-0.30f, 0.02f) * size, new Vector2(-0.08f, -0.20f) * size, th, c);
                    Bar(parent, new Vector2(-0.08f, -0.20f) * size, new Vector2(0.32f, 0.22f) * size, th, c);
                    break;
                case St.Wrong:
                    Bar(parent, new Vector2(-0.22f, 0.22f) * size, new Vector2(0.22f, -0.22f) * size, th, c);
                    Bar(parent, new Vector2(-0.22f, -0.22f) * size, new Vector2(0.22f, 0.22f) * size, th, c);
                    break;
                case St.Check:
                    Bar(parent, new Vector2(0, 0.28f) * size, new Vector2(0, -0.06f) * size, th, c);
                    var d = Img("Dot", parent, sCircle, c, 0);
                    Place(d.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -0.26f * size), new Vector2(th * 1.15f, th * 1.15f));
                    break;
                case St.Edited:
                    Bar(parent, new Vector2(-0.22f, -0.22f) * size, new Vector2(0.20f, 0.20f) * size, th, c);
                    Bar(parent, new Vector2(-0.26f, -0.26f) * size, new Vector2(-0.12f, -0.26f) * size, th * 0.8f, c);
                    break;
            }
        }

        void Bar(RectTransform parent, Vector2 a, Vector2 b, float thick, Color c)
        {
            var img = Img("Bar", parent, sRound, c, thick * 0.5f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            Vector2 d = b - a;
            rt.sizeDelta = new Vector2(d.magnitude + thick, thick);
            rt.anchoredPosition = (a + b) * 0.5f;
            rt.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        static Color StateColour(St s)
        {
            switch (s)
            {
                case St.Wrong: return Bad;
                case St.Check: return Warn;
                case St.Reading: return Accent;
                case St.Edited: return Accent;
                default: return Ok;
            }
        }

        static Color StateText(St s)
        {
            switch (s)
            {
                case St.Wrong: return Bad;
                case St.Check: return Warn;
                case St.Edited: return Accent;
                default: return Muted;
            }
        }

        // ------------------------------------------------------------------ small UI factory

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Image Img(string name, Transform parent, Sprite sprite, Color color, float radius)
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

        static Button MakeButton(Image img, Action onClick)
        {
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => onClick());
            return b;
        }

        static TextMeshProUGUI Txt(Transform parent, string text, float size, Color color, FontStyles style, TextAlignmentOptions align)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = true;
            return t;
        }

        Button Btn(RectTransform parent, string label, bool primary, string id)
        {
            var img = Img("Button " + label, parent, sRound, primary ? Accent : Hex("#FFFFFF", 0.10f), 70);
            img.raycastTarget = true;
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 140; le.flexibleWidth = 1;
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.highlightedColor = new Color(1, 1, 1, 0.92f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            b.colors = cb;
            b.onClick.AddListener(() => { clicked = id; });
            var t = Txt(img.rectTransform, label, 40, primary ? AccentInk : Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(t.rectTransform);
            return b;
        }

        RectTransform Pill(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 pivot)
        {
            var bg = Img(name, parent, sRound, Glass, 40);
            var rt = bg.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            var hl = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(30, 34, 18, 18);
            hl.spacing = 16;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            var fit = bg.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        static RectTransform HRow(RectTransform parent, float height)
        {
            var row = Rect("Row", parent);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 18; hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            return row;
        }

        Image Swatch(RectTransform parent, int colour, float size)
        {
            var ring = Img("Swatch", parent, sCircle, Hex("#FFFFFF", 0.35f), 0);
            Fixed(ring.rectTransform, size, size);
            var dot = Img("Fill", ring.rectTransform, sCircle, WireColours[colour], 0);
            Place(dot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size - 6, size - 6));
            return ring;
        }

        static void Space(RectTransform parent, float h)
        {
            var s = Rect("Space", parent);
            var le = s.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = h; le.minHeight = h;
        }

        static void Fixed(RectTransform rt, float w, float h)
        {
            rt.sizeDelta = new Vector2(w, h);
            var le = rt.GetComponent<LayoutElement>();
            if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.minWidth = w; le.preferredHeight = h; le.minHeight = h;
            le.flexibleWidth = 0;
        }

        static void Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            Place(rt, anchor, pos, size, new Vector2(0.5f, 0.5f));
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
        }

        void ApplySafeArea()
        {
            Rect sa = UnityEngine.Screen.safeArea;
            if (UnityEngine.Screen.width <= 0 || UnityEngine.Screen.height <= 0) return;
            safe.anchorMin = new Vector2(sa.xMin / UnityEngine.Screen.width, sa.yMin / UnityEngine.Screen.height);
            safe.anchorMax = new Vector2(sa.xMax / UnityEngine.Screen.width, sa.yMax / UnityEngine.Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;
        }

        void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            var module = es.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        // ------------------------------------------------------------------ animation helpers

        IEnumerator Wait(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds / Mathf.Max(0.1f, speed));
        }

        IEnumerator Tween(float seconds, Action<float> step)
        {
            float dur = seconds / Mathf.Max(0.1f, speed), t = 0;
            while (t < dur)
            {
                step(Mathf.Clamp01(t / dur));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            step(1f);
        }

        IEnumerator Fade(CanvasGroup g, float a, float b, float seconds)
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

        IEnumerator Pop(Transform tr)
        {
            float t = 0;
            while (t < 0.32f && tr != null)
            {
                float k = t / 0.32f;
                float s = 1f + 0.12f * Mathf.Sin(k * Mathf.PI) * (1f - k);
                tr.localScale = new Vector3(s, s, 1f);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (tr != null) tr.localScale = Vector3.one;
        }

        static float Ease(float k) { k = Mathf.Clamp01(k); return 1f - Mathf.Pow(1f - k, 3f); }

        // ------------------------------------------------------------------ sprites

        static void MakeSprites()
        {
            if (sRound != null) return;
            sRound = MakeSprite(68, 33, (x, y) => SdfRound(x, y, 68, 68, 32), "Round");
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
            sFadeDown = MakeSprite(64, 0, (x, y) => Mathf.SmoothStep(0, 1, y / 63f), "FadeDown");
            sFadeUp = MakeSprite(64, 0, (x, y) => Mathf.SmoothStep(1, 0, y / 63f), "FadeUp");
        }

        static float SdfRound(float x, float y, float w, float h, float r)
        {
            return Mathf.Clamp01(0.5f - SdfDist(x, y, w, h, r, 0));
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

        // ------------------------------------------------------------------ misc

        static Color Hex(string hex, float alpha = 1f)
        {
            Color c;
            ColorUtility.TryParseHtmlString(hex, out c);
            c.a = alpha;
            return c;
        }

        static T Get<T>(T[] arr, int i, T fallback)
        {
            return arr != null && i >= 0 && i < arr.Length ? arr[i] : fallback;
        }
    }

    /// <summary>Forwards pointer clicks to a callback (used for calibration).</summary>
    public class ClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public Action<PointerEventData> onClick;
        public void OnPointerClick(PointerEventData e) { if (onClick != null) onClick(e); }
    }
}
