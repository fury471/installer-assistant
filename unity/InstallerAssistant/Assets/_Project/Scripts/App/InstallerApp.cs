using System;
using System.Collections;
using System.Collections.Generic;
using InstallerAssistant.AI;
using InstallerAssistant.AR;
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
using UnityEngine.InputSystem.UI;
#endif

namespace InstallerAssistant.App
{
    /// <summary>
    /// The Installer Assistant app. Owner: package C (App, records and UI).
    ///
    /// InstallerApp.cs        state, start-up, canvas, the API the Wizard-of-Oz operator uses
    /// InstallerApp.Pages.cs  Home (scan a cable), Reports, Settings, report
    /// InstallerApp.Check.cs  the check: scan a label, record that end, walk, pair, verdict, fix
    ///
    /// The app only talks to the outside through the contracts in ServiceSet (camera view, label scanner, wire
    /// reader, second opinion, record store). Composition.cs picks the implementations from 'mode':
    ///   Wizard of Oz  photos and scripted readings (WizardOfOzRig on the same GameObject)
    ///   Live          AR camera (B), perception service and QR decoding (A), JSON records (C)
    /// The flow, screens, wiring rules, rule check, fixes, verdicts, records and log are the same in both.
    /// </summary>
    public partial class InstallerApp : MonoBehaviour
    {
        [Tooltip("Wizard of Oz: photos and scripted readings (needs a WizardOfOzRig on this GameObject). Live: the product.")]
        public AppMode mode = AppMode.WizardOfOz;
        [Tooltip("Reading confidence below which a terminal turns amber.")]
        [Range(0.5f, 0.99f)] public float confidenceThreshold = VerdictCombiner.DefaultThreshold;

        [Header("Live services")]
        [Tooltip("Perception service (ml/perception_service). On a phone, use the laptop's IP address, not localhost.")]
        public string perceptionUrl = "http://localhost:8000";
        [Tooltip("Records file in Application.persistentDataPath.")]
        public string recordsFile = "installer-assistant-records.json";
        [Tooltip("Test only: the live label scanner \"finds\" this label every time, e.g. IA|D1|READER|C01 (no end: the app asks which end).\nLets you run the live flow before QR decoding works. Leave empty for real scanning.")]
        public string testLabel = "";

        [Header("Look")]
        [Tooltip("Overlay settings for live mode. The Wizard of Oz uses the values of its DemoLayout.")]
        public OverlayStyle overlay = new OverlayStyle();
        [Tooltip("Inter Regular. Empty = TextMesh Pro default font.")]
        public Font fontRegular;
        [Tooltip("Inter SemiBold. Empty = TextMesh Pro default font.")]
        public Font fontSemiBold;

        // ------------------------------------------------------------------ app state

        /// <summary>New: nothing scanned. OneEnd: one end saved, waiting for its other end. Verified: both ends match.</summary>
        enum CableState { New, OneEnd, Verified }

        /// <summary>A cable seen in this session, with what the app keeps of each end.</summary>
        sealed class Cable
        {
            public CableLabel Label;
            public string Title, Detail;
            public CableState State;
            public CableEnd FirstEnd;
            public DateTime SavedAt;
            public EndSnapshot DeviceShot, ControllerShot;
            public CameraFrame DeviceFrame, ControllerFrame;
        }

        sealed class Report
        {
            public Cable Cable;
            public CableRecord Record;
            public DateTime At;
            public int Fixes, CheckedByYou, Wires;
            public float Seconds;
            /// <summary>Checked in this session (has evidence); false for example records.</summary>
            public bool Live;
            public string FixText;
        }

        readonly List<Report> reports = new List<Report>();
        readonly Dictionary<string, Cable> cables = new Dictionary<string, Cable>();
        bool useSecondOpinion = true;

        // ------------------------------------------------------------------ services (see Composition.cs)

        ServiceSet svc;
        ICameraView view;
        ILabelScanner labels;
        IWireReader wires;
        ISecondOpinion secondOpinion;
        IRecordStore records;
        IStudyOperator op;          // Wizard of Oz only; null in live mode
        OverlayStyle style;
        SessionLog log;

        // ------------------------------------------------------------------ check state

        UsageRule rule = AxisCatalog.OsdpReaderOnReader1;
        CableLabel cable;               // the label scanned last (null until the first scan of a check)
        Cable current;                  // that cable's entry
        CableRecord record;             // device end, once saved
        ControllerRecord ctrlRecord;    // controller end, once saved
        RuleCheck check;
        PinResult[] results;
        int[] readColours;              // the end being read now: per terminal (device) or per pin (controller)
        float[] readConfidence;
        bool[] readEdited;
        int fixesOnFirstCheck, confirmedByYou;
        string firstFixText;
        float startTime;

        // ------------------------------------------------------------------ views

        Canvas canvas;
        RectTransform canvasRT, screen, topLayer;
        Image canvasBackground;
        TextMeshProUGUI toastText;
        LayoutElement toastWidth;
        CanvasGroup toastGroup;
        Coroutine toastCo;
        string clicked;

        // ================================================================== lifecycle

        void Start()
        {
            MakeSprites();
            LoadFonts(fontRegular, fontSemiBold);
            EnsureEventSystem();

            svc = Composition.Build(this);
            view = svc.View; labels = svc.Labels; wires = svc.Wires; secondOpinion = svc.SecondOpinion;
            records = svc.Records; op = svc.Operator;
            style = svc.Style ?? overlay ?? new OverlayStyle();
            Debug.Log("[InstallerAssistant] " + svc.Description);

            var cam = Camera.main;
            if (cam != null && !view.SeeThrough) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Page; }
            BuildCanvas();
            ResetSession();
            ShowHome();
        }

        void Update() { UpdateCheck(); }
        void LateUpdate() { LateUpdateCheck(); }

        // ================================================================== API for the Wizard-of-Oz operator

        /// <summary>The mode that is actually running (Live if Wizard of Oz was asked for but no rig was found).</summary>
        public bool IsWizardOfOz { get { return op != null; } }

        /// <summary>A fresh session: nothing on Home. The Wizard of Oz also clears its records and adds one example report.</summary>
        public void ResetSession()
        {
            StopAllCoroutines();
            toastCo = null;
            if (toastGroup != null) toastGroup.alpha = 0;
            reports.Clear();
            cables.Clear();
            // live records are the installer's work and outlive the session; only the Wizard of Oz starts from nothing
            if (records != null && op != null) records.Clear();
            ClearCheckState();
            if (op != null)
            {
                op.SessionReset();
                CableLabel d2;
                CableLabel.TryParse("IA|D2|READER|C02", out d2);
                var door2 = new Cable { Label = d2, Title = "Door 2 reader", State = CableState.Verified, Detail = "AXIS A4020-E → A1610, Reader 2" };
                reports.Add(new Report { Cable = door2, At = new DateTime(DateTime.Now.Year, 10, 14, 15, 42, 0), Fixes = 0, CheckedByYou = 0, Wires = 4,
                    Seconds = 98, FixText = "No fixes needed" });
            }
        }

        public void GoHome()
        {
            StopAllCoroutines();
            toastCo = null;
            if (toastGroup != null) toastGroup.alpha = 0;
            ShowHome();
        }

        /// <summary>Starts a check as if the installer pressed "Scan a cable" at this end.</summary>
        public void StartFresh(CableEnd end)
        {
            ResetSession();
            StartCheck(end);
        }

        /// <summary>Operator shortcut: the start end is recorded as configured, and the check starts at the other end.</summary>
        public void SkipToSecondEnd()
        {
            if (op == null) return;
            ResetSession();
            EndSnapshot shot;
            var label = op.Prefill(records, out shot);
            if (label == null) { ShowHome(); return; }
            var c = CableFor(label);
            var first = label.End;
            if (first == CableEnd.Device) c.DeviceShot = shot; else c.ControllerShot = shot;
            MarkOneEnd(c, first, DateTime.Now);
            StartCheck(Other(first));
        }

        public void Toast(string text)
        {
            if (toastText == null) return;
            toastText.text = text;
            toastWidth.preferredWidth = Mathf.Min(toastText.GetPreferredValues(text).x, 860f);
            if (toastCo != null) StopCoroutine(toastCo);
            toastCo = StartCoroutine(ToastRoutine());
        }

        // ================================================================== cables

        Cable CableFor(CableLabel label)
        {
            Cable c;
            if (cables.TryGetValue(label.Key, out c)) return c;
            var r = AxisCatalog.Lookup(label.Usage) ?? rule;
            c = new Cable
            {
                Label = label.AtEnd(CableEnd.Unknown), State = CableState.New,
                Title = label.DoorName + " " + label.Usage.ToLowerInvariant(),
                Detail = Short(r.DeviceModel) + " → " + Short(r.ControllerModel) + ", " + r.Connector.Name,
            };
            cables[label.Key] = c;
            return c;
        }

        void MarkOneEnd(Cable c, CableEnd e, DateTime at)
        {
            if (c.State == CableState.Verified) return;
            if (c.State != CableState.OneEnd) { c.FirstEnd = e; c.SavedAt = at; }
            c.State = CableState.OneEnd;
        }

        /// <summary>Cables with one end saved, the most recent first.</summary>
        List<Cable> Pending()
        {
            var list = new List<Cable>();
            foreach (var c in cables.Values) if (c.State == CableState.OneEnd) list.Add(c);
            list.Sort((a, b) => b.SavedAt.CompareTo(a.SavedAt));
            return list;
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

            canvasBackground = Img("Background", canvasRT, null, Page, 0);
            Stretch(canvasBackground.rectTransform);
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
            UnityEngine.Rect sa = UnityEngine.Screen.safeArea;
            if (UnityEngine.Screen.width <= 0 || UnityEngine.Screen.height <= 0) return;
            rt.anchorMin = new Vector2(sa.xMin / UnityEngine.Screen.width, sa.yMin / UnityEngine.Screen.height);
            rt.anchorMax = new Vector2(sa.xMax / UnityEngine.Screen.width, sa.yMax / UnityEngine.Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        IEnumerator ToastRoutine()
        {
            toastGroup.transform.SetAsLastSibling();
            yield return Fade(toastGroup, toastGroup.alpha, 1, 0.2f);
            yield return new WaitForSecondsRealtime(2.8f);
            yield return Fade(toastGroup, 1, 0, 0.35f);
        }

        // ================================================================== small helpers

        IEnumerator WaitClick()
        {
            clicked = null;
            while (clicked == null || clicked == "dismiss") yield return null;
        }

        static IEnumerator Wait(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }

        static IEnumerator Tween(float seconds, Action<float> step)
        {
            float t = 0;
            while (t < seconds)
            {
                step(Mathf.Clamp01(t / seconds));
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

        static void EnsureEventSystem()
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
}
