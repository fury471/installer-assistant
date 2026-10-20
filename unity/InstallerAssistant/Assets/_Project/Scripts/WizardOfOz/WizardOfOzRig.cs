using InstallerAssistant.App;
using InstallerAssistant.AR;
using InstallerAssistant.Labels;
using InstallerAssistant.Records;
using InstallerAssistant.UI;
using InstallerAssistant.Verification;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static InstallerAssistant.UI.UiKit;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace InstallerAssistant.WizardOfOz
{
    /// <summary>
    /// The Wizard of Oz behind the app: photos instead of the camera, scripted readings instead of perception,
    /// and the study leader's controls. Put it on the same GameObject as InstallerApp and set the app's mode to
    /// Wizard of Oz. Live mode ignores it, so it can stay in the scene.
    ///
    /// OPERATOR KEYS (click the Game view first): R restart · 1 start at the reader · 2 start at the controller ·
    /// S skip to the second end · 3/4 planted fault · K calibrate the photo · H hide this line.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WizardOfOzRig : MonoBehaviour, IServiceSource, IStudyOperator
    {
        [Tooltip("Photos, positions and planted faults. Empty = built-in mock images.")]
        public DemoLayout layout;
        [Tooltip("Which planted fault the session starts with (index into the layout's scenarios).")]
        public int scenario;
        [Tooltip("Which end the participant is at when they first press \"Scan a cable\".")]
        public CableEnd startEnd = CableEnd.Device;
        [Tooltip("Speeds up or slows down every simulated processing delay.")]
        [Range(0.25f, 3f)] public float speed = 1f;
        public bool showOperatorHint = true;

        InstallerApp app;
        DemoLayout L;
        WozWorld world;
        PhotoCameraView view;
        UsageRule rule;
        CableLabel label;
        bool active, startWithoutLabel;
        RectTransform overlayRT;
        TextMeshProUGUI hintText;

        // ================================================================== IServiceSource

        public ServiceSet CreateServices(InstallerApp owner)
        {
            app = owner;
            L = layout != null ? layout : ScriptableObject.CreateInstance<DemoLayout>();
            if (!CableLabel.TryParse(L.cableLabel, out label) || AxisCatalog.Resolve(label) == null)
            {
                Debug.LogWarning("[InstallerAssistant] The layout's cable label \"" + L.cableLabel + "\" is not a label the app can check; using " +
                                 "IA|C01|D1|A1610.READER1|A4020E. Format: see CableLabel.");
                label = new CableLabel("C01", "D1", AxisCatalog.OsdpReaderOnReader1.ControllerModule, AxisCatalog.OsdpReaderOnReader1.DeviceModule);
            }
            rule = AxisCatalog.Resolve(label);
            scenario = FirstUsableScenario(scenario);
            startWithoutLabel = Scenario.startWithMissingLabel;
            world = new WozWorld { Layout = L, Scenario = Scenario, Speed = speed, LabelAttached = !startWithoutLabel };
            BuildOverlay();
            view = new PhotoCameraView(L, world, rule, overlayRT, layout != null, () => startEnd, owner.Toast);
            active = true;
            return new ServiceSet
            {
                View = view,
                Labels = new WozLabelScanner(world),
                Wires = new WozWireReader(world),
                SecondOpinion = new WozSecondOpinion(world),
                Records = new InMemoryRecordStore(),
                Operator = this,
                Style = new OverlayStyle
                {
                    markerScale = L.markerScale, labelTextScale = L.labelTextScale, tagsBelowTerminals = L.readerTagsBelowTerminals,
                    snapshotOnLeft = L.snapshotOnLeft, snapshotInset = L.snapshotInset, snapshotTopInset = L.snapshotTopInset,
                    handwrittenLabels = L.handwrittenLabels,
                },
                Description = "Wizard of Oz · layout " + L.name + " · fault \"" + Scenario.name + "\"",
            };
        }

        // ================================================================== IStudyOperator

        public CableEnd StartEnd { get { return startEnd; } set { startEnd = value; } }

        public string[] FaultNames
        {
            get
            {
                int n = L != null && L.scenarios != null ? L.scenarios.Length : 0;
                var names = new string[n];
                for (int i = 0; i < n; i++) names[i] = L.scenarios[i].name;
                return names;
            }
        }

        public int Fault { get { return scenario; } }

        public void ChooseFault(int index)
        {
            if (!Usable(index)) { app.Toast("No photo for this fault in " + L.name + ": add one to the layout"); return; }
            scenario = index;
            startWithoutLabel = Scenario.startWithMissingLabel;
            world.Scenario = Scenario;
            world.LabelAttached = !startWithoutLabel;
            UpdateHint();
        }

        public bool StartWithoutLabel
        {
            get { return startWithoutLabel; }
            set { startWithoutLabel = value; world.LabelAttached = !value; }
        }

        public bool ShowOperatorKeys
        {
            get { return showOperatorHint; }
            set { showOperatorHint = value; UpdateHint(); }
        }

        public void SessionReset()
        {
            world.Scenario = Scenario;
            world.Record = null;
            world.Rewired = false;
            world.DeviceRewired = false;
            world.LabelAttached = !startWithoutLabel;
            world.ReaderReads = 0;
            world.ControllerChecks = 0;
            world.Speed = speed;
        }

        public void BeginCheck()
        {
            world.ReaderReads = 0;
            world.ControllerChecks = 0;
            world.Speed = speed;
        }

        public void LabelAttached() { world.LabelAttached = true; }

        public void Rewired(CableRecord deviceEnd)
        {
            world.Record = deviceEnd;
            world.Rewired = true;
        }

        public void RewiredAtDevice() { world.DeviceRewired = true; }

        public CableLabel Prefill(IRecordStore store, out EndSnapshot snapshot)
        {
            var e = startEnd == CableEnd.Controller ? CableEnd.Controller : CableEnd.Device;
            var now = System.DateTime.Now;
            if (e == CableEnd.Device)
            {
                int n = rule.DeviceTerminals.Length;
                var c = new int[n];
                for (int i = 0; i < n; i++) c[i] = Mathf.Clamp(Get(L.readerColours, i, i), 0, WireColours.Names.Length - 1);
                var rec = new CableRecord(label.AtEnd(CableEnd.Device), rule, c, new bool[n], now);
                store.Save(rec);
                world.Record = rec;
            }
            else
            {
                int n = rule.Connector.Count;
                var pins = new int[n]; var conf = new float[n];
                for (int p = 0; p < n; p++) { pins[p] = Get(Scenario.pinColours, p, WireColours.None); conf[p] = 0.95f; }
                store.Save(new ControllerRecord(label.AtEnd(CableEnd.Controller), rule, pins, conf, new bool[n], now));
                world.LabelAttached = true;   // the controller label was scanned on the earlier visit
            }
            snapshot = view.SnapshotOf(e);
            return label.AtEnd(e);
        }

        // ================================================================== operator keys and overlay

        void Update()
        {
            if (!active) return;
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
            if (r) { view.StopCalibration(); app.ResetSession(); app.GoHome(); return; }
            if (k1 || k2) { view.StopCalibration(); startEnd = k1 ? CableEnd.Device : CableEnd.Controller; app.StartFresh(startEnd); return; }
            if (sk) { view.StopCalibration(); app.SkipToSecondEnd(); return; }
            if (k3 || k4)
            {
                int i = k3 ? 0 : 1;
                if (!Usable(i)) { ChooseFault(i); return; }
                view.StopCalibration();
                ChooseFault(i);
                app.ResetSession();
                app.GoHome();
                app.Toast("Planted fault: " + Scenario.name);
                return;
            }
            if (h) { showOperatorHint = !showOperatorHint; UpdateHint(); }
            if (kk) view.ToggleCalibration();
            if (esc && view.Calibrating) view.StopCalibration();
        }

        /// <summary>The operator's own canvas, above the app: the key line and the calibration layer.</summary>
        void BuildOverlay()
        {
            var go = new GameObject("Wizard of Oz overlay", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2340);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            overlayRT = (RectTransform)go.transform;

            hintText = Txt(overlayRT, "", 22, Hex("#5B6B82", 0.9f), Face.Regular, TextAlignmentOptions.Center);
            hintText.raycastTarget = false;
            Place(hintText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(1040, 30));
            hintText.textWrappingMode = TextWrappingModes.NoWrap;
            hintText.enableAutoSizing = true; hintText.fontSizeMin = 14; hintText.fontSizeMax = 22;
            UpdateHint();
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

        // ================================================================== scenarios

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

        static T Get<T>(T[] arr, int i, T fallback) { return arr != null && i >= 0 && i < arr.Length ? arr[i] : fallback; }
    }
}
