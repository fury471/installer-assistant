using System;
using System.Collections;
using System.Collections.Generic;
using InstallerAssistant.AI;
using InstallerAssistant.Detection;
using InstallerAssistant.Labels;
using InstallerAssistant.Records;
using InstallerAssistant.UI;
using InstallerAssistant.Verification;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static InstallerAssistant.UI.UiKit;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace InstallerAssistant.WizardOfOz
{
    /// <summary>
    /// The Installer Assistant MVP, run as a Wizard-of-Oz demo on photos.
    ///
    /// App shell (InstallerAssistantDemo.Pages.cs): Home (scan a cable), Reports, Settings, report.
    /// The check (InstallerAssistantDemo.Check.cs): either end first, walk, other end, verdict and fix, on the camera view.
    ///
    /// Real: the flow, the screens, the A1610 wiring rules, the rule check, the fix messages, the verdicts,
    /// the records and the log. Faked: the three sensing services (WozServices.cs) and the past checks on Home.
    ///
    /// SETUP: open Scenes/Blueprint_Demo.unity, Game view 1080 x 2340, Play.
    /// OPERATOR KEYS: R restart · 1 start at the reader · 2 start at the controller · S skip to the second end ·
    /// 3/4 planted fault · K calibrate · H operator line.
    /// </summary>
    public partial class InstallerAssistantDemo : MonoBehaviour
    {
        [Tooltip("Photos, positions and planted faults. Empty = built-in mock images.")]
        public DemoLayout layout;
        [Tooltip("Speeds up or slows down every simulated processing delay.")]
        [Range(0.25f, 3f)] public float speed = 1f;
        public bool showOperatorHint = true;
        [Tooltip("Reading confidence below which a terminal turns amber.")]
        [Range(0.5f, 0.99f)] public float confidenceThreshold = VerdictCombiner.DefaultThreshold;
        [Tooltip("Which planted fault the demo starts with (index into the layout's scenarios).")]
        public int scenario;

        [Header("Fonts (empty = TextMesh Pro default)")]
        public Font fontRegular;
        public Font fontSemiBold;

        // ------------------------------------------------------------------ app state (past checks are demo data)

        /// <summary>New: nothing scanned. OneEnd: one end saved, waiting for its other end. Verified: both ends match.</summary>
        enum CableState { New, OneEnd, Verified }

        class Cable
        {
            public string Title, Detail, When;
            public CableLabel Label;
            public CableState State;
            public CableEnd FirstEnd;
            public DateTime SavedAt;
            public bool Live;          // the one cable the demo can actually check
        }

        class Report
        {
            public Cable Cable;
            public DateTime At;
            public int Fixes, CheckedByYou, Wires;
            public float Seconds;
            public bool Live;
            public string FixText;
        }

        readonly List<Report> reports = new List<Report>();
        Cable liveCable;
        bool useSecondOpinion = true;
        bool plantMissingLabel;
        /// <summary>Demo only: which end the participant is at when they first press "Scan a cable".</summary>
        CableEnd demoStart = CableEnd.Device;

        // ------------------------------------------------------------------ services (the real app swaps these)

        ILabelScanner labels;
        IWireReader wires;
        ISecondOpinion secondOpinion;
        IRecordStore records = new InMemoryRecordStore();
        SessionLog log;
        WozWorld world;

        // ------------------------------------------------------------------ check state

        DemoLayout L;
        UsageRule rule = AxisCatalog.OsdpReaderOnReader1;
        CableLabel cable;
        CableRecord record;             // device (reader) end, once saved
        ControllerRecord ctrlRecord;    // controller end, once saved
        RuleCheck check;
        PinResult[] results;
        int[] readColours;              // the end being read now: per terminal (reader) or per pin (controller)
        float[] readConfidence;
        bool[] readEdited;
        int fixesOnFirstCheck, confirmedByYou;
        string firstFixText;
        float startTime;

        // ------------------------------------------------------------------ views

        Canvas canvas;
        RectTransform canvasRT, screen, topLayer;
        TextMeshProUGUI toastText, hintText, calibText;
        LayoutElement toastWidth;
        CanvasGroup toastGroup, calibGroup;
        Coroutine toastCo;
        string clicked;
        int pendingRestart = -1;

        // ================================================================== lifecycle

        void Start()
        {
            L = layout != null ? layout : ScriptableObject.CreateInstance<DemoLayout>();
            MakeSprites();
            LoadFonts(fontRegular, fontSemiBold);
            EnsureEventSystem();
            var cam = Camera.main;
            if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Page; }
            scenario = FirstUsableScenario(scenario);
            plantMissingLabel = Scenario.startWithMissingLabel;
            if (!CableLabel.TryParse(L.cableLabel, out cable)) cable = new CableLabel("D1", "READER", "C01");
            rule = AxisCatalog.Lookup(cable.Usage) ?? AxisCatalog.OsdpReaderOnReader1;
            BuildCanvas();
            ResetDemoData();
            ShowHome();
        }

        DemoLayout.Scenario Scenario
        {
            get { return L.scenarios != null && L.scenarios.Length > 0 ? L.scenarios[Mathf.Clamp(scenario, 0, L.scenarios.Length - 1)] : new DemoLayout.Scenario(); }
        }

        bool Usable(int i)
        {
            if (L.scenarios == null || i < 0 || i >= L.scenarios.Length) return false;
            return L.UsesMockImages || L.scenarios[i].wiring.photo != null;
        }

        int FirstUsableScenario(int preferred)
        {
            if (Usable(preferred)) return preferred;
            for (int i = 0; L.scenarios != null && i < L.scenarios.Length; i++) if (Usable(i)) return i;
            return 0;
        }

        /// <summary>A fresh site visit: nothing scanned yet, and one past check for the Reports page.</summary>
        void ResetDemoData()
        {
            reports.Clear();
            records = new InMemoryRecordStore();
            record = null; ctrlRecord = null; readColours = null;
            results = null; check = null;
            liveCable = new Cable { Title = cable.DoorName + " " + cable.Usage.ToLowerInvariant(), Label = cable, Live = true, State = CableState.New,
                Detail = "AXIS A4020-E → A1610, Reader 1" };
            CableLabel d2;
            CableLabel.TryParse("D2|READER|C02", out d2);
            var door2 = new Cable { Title = "Door 2 reader", Label = d2, State = CableState.Verified, When = "14 Oct",
                Detail = "AXIS A4020-E → A1610, Reader 2" };
            reports.Add(new Report { Cable = door2, At = new DateTime(DateTime.Now.Year, 10, 14, 15, 42, 0), Fixes = 0, CheckedByYou = 0, Wires = 4,
                Seconds = 98, FixText = "No fixes needed" });
        }

        void Update()
        {
            if (pendingRestart >= 0) { int p = pendingRestart; pendingRestart = -1; Restart(p); return; }
            HandleKeys();
            UpdateCheck();
        }

        void LateUpdate()
        {
            LateUpdateCheck();
        }

        /// <summary>0 = Home, 1 = fresh check starting at the reader, 2 = fresh check starting at the controller,
        /// 3 = skip to the second end (the first end is filled in as configured).</summary>
        void Restart(int phase)
        {
            StopAllCoroutines();
            toastCo = null;
            if (toastGroup != null) toastGroup.alpha = 0;
            if (phase == 0) { ShowHome(); return; }
            if (phase == 3) { var first = demoStart; ResetDemoData(); StartCheck(Other(first), true); return; }
            ResetDemoData();
            demoStart = phase == 2 ? CableEnd.Controller : CableEnd.Device;
            StartCheck(demoStart);
        }

        // ================================================================== canvas and shared chrome

        void BuildCanvas()
        {
            if (canvas != null) Destroy(canvas.gameObject);
            var go = new GameObject("InstallerAssistant UI", typeof(RectTransform));
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2340);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            canvasRT = (RectTransform)go.transform;

            var bg = Img("Background", canvasRT, null, Page, 0);
            Stretch(bg.rectTransform);
            screen = UiKit.Rect("Screen", canvasRT);
            Stretch(screen);
            topLayer = UiKit.Rect("Top", canvasRT);
            Stretch(topLayer);

            // toast: a dark pill, the one strong element on a light screen
            var toast = Pill(topLayer, "Toast", new Vector2(0.5f, 1), new Vector2(0, -SafeTop() - 330), new Vector2(0.5f, 0.5f), Ink);
            toastText = Txt(toast, "", Small, OnColour, Face.Regular, TextAlignmentOptions.Left);
            toastWidth = toastText.gameObject.AddComponent<LayoutElement>();
            toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0; toastGroup.blocksRaycasts = false;

            hintText = Txt(topLayer, "", 22, Hex("#5B6B82", 0.9f), Face.Regular, TextAlignmentOptions.Center);
            Place(hintText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(1040, 30));
            hintText.textWrappingMode = TextWrappingModes.NoWrap;
            hintText.enableAutoSizing = true; hintText.fontSizeMin = 14; hintText.fontSizeMax = 22;
            UpdateHint();

            var cal = Img("Calibrate", topLayer, null, Hex("#000000", 0.01f), 0);
            cal.raycastTarget = true;
            Stretch(cal.rectTransform);
            cal.gameObject.AddComponent<ClickCatcher>().onClick = OnCalibrationClick;
            calibGroup = cal.gameObject.AddComponent<CanvasGroup>();
            var cp = Pill(cal.rectTransform, "CalibText", new Vector2(0.5f, 1), new Vector2(0, -SafeTop() - 330), new Vector2(0.5f, 0.5f), WarnSoft);
            calibText = Txt(cp, "", Small, WarnInk, Face.Semi, TextAlignmentOptions.Left);
            cal.gameObject.SetActive(false);
        }

        /// <summary>Clears the screen and returns a new full-screen root inside the safe area.</summary>
        RectTransform NewScreen(Color bg)
        {
            ClearCheckViews();
            Clear(screen);
            var root = Img("Root", screen, null, bg, 0);
            Stretch(root.rectTransform);
            var safe = UiKit.Rect("Safe", root.rectTransform);
            ApplySafeArea(safe);
            return safe;
        }

        float SafeTop()
        {
            if (UnityEngine.Screen.height <= 0 || canvasRT == null) return 0;
            float frac = 1f - UnityEngine.Screen.safeArea.yMax / UnityEngine.Screen.height;
            return frac * canvasRT.rect.height;
        }

        void ApplySafeArea(RectTransform rt)
        {
            Stretch(rt);
            Rect sa = UnityEngine.Screen.safeArea;
            if (UnityEngine.Screen.width <= 0 || UnityEngine.Screen.height <= 0) return;
            rt.anchorMin = new Vector2(sa.xMin / UnityEngine.Screen.width, sa.yMin / UnityEngine.Screen.height);
            rt.anchorMax = new Vector2(sa.xMax / UnityEngine.Screen.width, sa.yMax / UnityEngine.Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        void UpdateHint()
        {
            if (hintText == null) return;
            string names = "";
            for (int i = 0; L.scenarios != null && i < L.scenarios.Length && i < 2; i++)
                names += "  " + (i + 3) + " " + L.scenarios[i].name + (i == scenario ? " •" : "") + (Usable(i) ? "" : " (no photo)");
            hintText.text = "Operator: R restart · 1 start at reader · 2 start at controller · S skip to 2nd end ·" + names + " · K calibrate · H hide";
            hintText.gameObject.SetActive(showOperatorHint);
        }

        void Toast(string text)
        {
            toastText.text = text;
            toastWidth.preferredWidth = Mathf.Min(toastText.GetPreferredValues(text).x, 860f);
            if (toastCo != null) StopCoroutine(toastCo);
            toastCo = StartCoroutine(ToastRoutine());
        }

        IEnumerator ToastRoutine()
        {
            toastGroup.transform.SetAsLastSibling();
            yield return Fade(toastGroup, toastGroup.alpha, 1, 0.2f);
            yield return new WaitForSecondsRealtime(2.8f);
            yield return Fade(toastGroup, 1, 0, 0.35f);
        }

        // ================================================================== operator keys

        void HandleKeys()
        {
            bool r = false, k1 = false, k2 = false, k3 = false, k4 = false, h = false, kk = false, esc = false, sk = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            r = kb.rKey.wasPressedThisFrame; k1 = kb.digit1Key.wasPressedThisFrame; k2 = kb.digit2Key.wasPressedThisFrame;
            k3 = kb.digit3Key.wasPressedThisFrame; k4 = kb.digit4Key.wasPressedThisFrame;
            h = kb.hKey.wasPressedThisFrame; kk = kb.kKey.wasPressedThisFrame; esc = kb.escapeKey.wasPressedThisFrame; sk = kb.sKey.wasPressedThisFrame;
#else
            r = Input.GetKeyDown(KeyCode.R); k1 = Input.GetKeyDown(KeyCode.Alpha1); k2 = Input.GetKeyDown(KeyCode.Alpha2);
            k3 = Input.GetKeyDown(KeyCode.Alpha3); k4 = Input.GetKeyDown(KeyCode.Alpha4);
            h = Input.GetKeyDown(KeyCode.H); kk = Input.GetKeyDown(KeyCode.K); esc = Input.GetKeyDown(KeyCode.Escape); sk = Input.GetKeyDown(KeyCode.S);
#endif
            if (r) { ResetDemoData(); Restart(0); return; }
            if (k1) { Restart(1); return; }
            if (k2) { Restart(2); return; }
            if (sk) { Restart(3); return; }
            if (k3 || k4) { ChooseScenario(k3 ? 0 : 1, true); return; }
            if (h) { showOperatorHint = !showOperatorHint; UpdateHint(); }
            if (kk) ToggleCalibration();
            if (esc && calibrating) ToggleCalibration();
        }

        void ChooseScenario(int i, bool restart)
        {
            if (!Usable(i)) { Toast("No photo for this fault in " + L.name + ": add one to the layout"); return; }
            scenario = i;
            plantMissingLabel = Scenario.startWithMissingLabel;
            UpdateHint();
            if (restart) { ResetDemoData(); Restart(0); }
            Toast("Planted fault: " + Scenario.name);
        }

        // ================================================================== small helpers

        IEnumerator WaitClick()
        {
            clicked = null;
            while (clicked == null || clicked == "dismiss") yield return null;
        }

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

        static string Short(string model) { return model.Replace(" Network Door Controller", "").Replace(" Reader", ""); }

        static string When(DateTime t)
        {
            if (t.Date == DateTime.Now.Date) return "Today, " + t.ToString("HH:mm");
            return t.ToString("d MMM");
        }

        void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
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
