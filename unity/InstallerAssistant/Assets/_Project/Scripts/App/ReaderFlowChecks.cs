#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using InstallerAssistant.AI;
using InstallerAssistant.AR;
using InstallerAssistant.Detection;
using InstallerAssistant.Labels;
using InstallerAssistant.Records;
using InstallerAssistant.UI;
using InstallerAssistant.Verification;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace InstallerAssistant.App.Editor
{
    [InitializeOnLoad]
    public static class ReaderFlowCheckBatch
    {
        const string Pending = "InstallerAssistant.ReaderFlowChecks";
        static ReaderFlowCheckBatch() { EditorApplication.playModeStateChanged += OnPlayMode; }
        public static void Run()
        {
            SessionState.SetBool(Pending, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.isPlaying = true;
        }
        public static void BuildAndroid()
        {
            const string output = "Builds/InstallerAssistant_ReaderRetry_20261102.apk";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/_Project/Scenes/App.unity" }, locationPathName = output,
                target = BuildTarget.Android, options = BuildOptions.Development });
            Debug.Log("[ReaderFlowChecks] Android build " + report.summary.result + " " + output);
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            new GameObject("Reader flow checks").AddComponent<ReaderFlowChecks>().Begin();
        }
    }

    /// <summary>Exercise the actual reader UI and coroutines with controlled sensor responses. This tests recovery,
    /// not perception accuracy. It builds an unsaved scene and never changes the product or WoZ scenes.</summary>
    public sealed class ReaderFlowChecks : MonoBehaviour
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        InstallerApp app;
        View view;
        Reader reader;
        Operator studyOperator;
        CableLabel label;
        readonly List<string> passed = new List<string>();
        string output;
        bool ready;

        void LateUpdate()
        {
            if (!ready) return;
            Call("UpdateCheck"); Call("LateUpdateCheck");
        }

        public void Begin()
        {
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../data/reader-flow-checks"));
            Directory.CreateDirectory(output);
            StartCoroutine(Guard(Check(), true));
        }
        IEnumerator Guard(IEnumerator routine, bool finish = false)
        {
            while (true)
            {
                object next;
                try { if (!routine.MoveNext()) break; next = routine.Current; }
                catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); yield break; }
                if (next is IEnumerator nested) yield return Guard(nested);
                else yield return next;
            }
            if (finish)
            {
                File.WriteAllText(Path.Combine(output, "checks.txt"), string.Join("\n", passed) + "\nALL PASSED: " + passed.Count);
                Debug.Log("[ReaderFlowChecks] ALL PASSED: " + passed.Count);
                EditorApplication.Exit(0);
            }
        }
        IEnumerator Check()
        {
            UiKit.MakeSprites(); UiKit.LoadFonts(null, null);
            app = new GameObject("InstallerApp under test").AddComponent<InstallerApp>();
            app.enabled = false; // inject services before any production Start / Update can run
            view = new View(); reader = new Reader(); studyOperator = new Operator();
            CableLabel.TryParse("IA|C01|D1|A1610.READER1|A4020E|DEV", out label);
            Set("view", view); Set("wires", reader); Set("secondOpinion", new UnknownOpinion());
            Set("records", new InMemoryRecordStore()); Set("op", studyOperator); Set("style", new OverlayStyle());
            Set("cable", label); Set("rule", AxisCatalog.OsdpReaderOnReader1);
            Set("log", new SessionLog(() => Time.unscaledTime, "reader-flow-check"));
            Set("current", Call("CableFor", label));
            Set("ctrlRecord", new ControllerRecord(label.AtEnd(CableEnd.Controller), AxisCatalog.OsdpReaderOnReader1,
                new[] { 1, 0, 2, 3, -1, -1, -1, -1 }, new[] { .99f,.99f,.99f,.99f,.99f,.99f,.99f,.99f }, new bool[8], DateTime.Now));
            Call("BuildCanvas"); Call("BuildCheckScreen"); Call("BuildMarks", CableEnd.Device);
            Call("SetTitle", "Reader end", "Door 1 reader · end 2 of 2"); ready = true;
            reader.Next.Enqueue(Reading(new[] { 0,1,2,3 }, .99f));
            app.StartCoroutine((IEnumerator)Call("ReaderSecond"));
            yield return WaitButton("Check the wiring");
            Press("Check the wiring");
            yield return WaitTitle("Check 4 wires yourself");
            Assert(Button("Read again") != null, "all-amber verdict keeps a retry button");
            Assert(Button("Finish") == null, "uncertain verdict cannot finish");
            yield return new WaitForSecondsRealtime(.35f);
            Capture("reader-amber.png");

            // A retry fails completely, then succeeds with a different wiring. Both paths must remain recoverable.
            reader.Next.Enqueue(new DeviceEndReading(new[] { -1,-1,-1,-1 }, new float[4]));
            Press("Read again");
            yield return WaitTitle("No wires found");
            Assert(Button("Read again") != null, "failed retry keeps its retry button");
            Assert(Button("Check again") == null, "failed retry cannot save a reading");
            Assert(view.Anchors.Count == 0, "failed retry clears stale reader anchors");
            Assert(studyOperator.RewiredCount == 0, "plain retry does not rewire the WoZ bench");
            yield return new WaitForSecondsRealtime(.35f);
            Capture("reader-failed-retry.png");
            reader.Next.Enqueue(Reading(new[] { 0,1,3,2 }, .99f));
            Press("Read again");
            yield return WaitButton("Check again");
            Press("Check again");
            yield return WaitTitle("Check 4 wires yourself");
            CableRecord saved;
            Assert(((IRecordStore)Get("records")).TryGet(label.Key, out saved) && saved.Colours[2] == 3 && saved.Colours[3] == 2,
                "successful retry replaces the reader record with the fresh colours");
            Assert(reader.Count == 3, "retry actually requests new frames");
            Assert(studyOperator.RewiredCount == 0, "success after retry still does not simulate a fix");
            app.StopAllCoroutines();

            var record = new CableRecord(label, AxisCatalog.OsdpReaderOnReader1, new[] {0,1,2,3}, new bool[4], DateTime.Now);
            Set("record", record);
            var controller = ((ControllerRecord)Get("ctrlRecord")).AsReading();
            var check = RuleChecker.Check(record, controller); Set("check", check);
            var verdicts = VerdictCombiner.Evaluate(check, controller, (Opinion[])Call("RulesOnly"), .8f);
            Set("results", verdicts); Call("ReaderVerdictSheet"); yield return null;
            Assert(Button("Read again") != null && Button("Finish") != null, "correct verdict keeps finish and optional reread");
            verdicts[0].Verdict = Verdict.Check;
            Call("ReaderVerdictSheet"); yield return null;
            Assert(((TMP_Text)Get("sheetTitle")).text == "Check one wire yourself" && Button("Read again") != null,
                "single uncertain wire has a retry and singular title");
            record = new CableRecord(label, AxisCatalog.OsdpReaderOnReader1, new[] {0,1,3,2}, new bool[4], DateTime.Now);
            Set("record", record); check = RuleChecker.Check(record, controller); Set("check", check);
            Set("results", VerdictCombiner.Evaluate(check, controller, (Opinion[])Call("RulesOnly"), .8f));
            Call("ReaderVerdictSheet"); yield return null;
            Assert(Button("Read again") != null && Button("Rewired · check again") != null && Button("Finish") == null,
                "wrong wiring retains the physical-fix action alongside plain retry");

            record = new CableRecord(label, AxisCatalog.OsdpReaderOnReader1, new[] {0,1,2,3}, new bool[4], DateTime.Now);
            Set("record", record);
            var extraPin = new ControllerReading(new[] {1,0,2,3,6,-1,-1,-1}, new[] {.99f,.99f,.99f,.99f,.99f,.99f,.99f,.99f});
            check = RuleChecker.Check(record, extraPin); Set("check", check);
            verdicts = VerdictCombiner.Evaluate(check, extraPin, (Opinion[])Call("RulesOnly"), .8f);
            foreach (var result in verdicts) if (result.Pin == 5) result.Verdict = Verdict.Check;
            Set("results", verdicts); Call("ReaderVerdictSheet"); yield return null;
            Assert(Button("Go to the controller") != null && Button("Read again") != null && Button("Finish") == null,
                "uncertain controller-only pin has an actionable route instead of an invisible amber wire");

            // Exactly the partial reading seen in the live log: three uncertain 'no wire' answers and one red.
            reader.Next.Enqueue(new DeviceEndReading(new[] { -1,-1,0,-1 }, new[] { .73f,.76f,.82f,.69f }, Points()));
            Set("readColours", null); Set("readConfidence", null); Set("readEdited", null);
            bool confirmed = false;
            app.StartCoroutine(Confirm(() => confirmed = true));
            yield return WaitTitle("Check 3 wires");
            Assert(((IList)Call("Shown")).Count == 4, "partial reader result retains all four terminal rows");
            Assert(Call("DuplicateColour") == null, "multiple empty terminals do not count as duplicate wire colours");
            Press("Save reader end"); yield return null; yield return null;
            Assert(!confirmed, "uncertain no-wire readings cannot be silently saved");
            var content = (RectTransform)Get("sheetContent");
            content.GetChild(0).GetComponent<Button>().onClick.Invoke();
            yield return WaitText("Which colour is on +?");
            Assert(Get("modalOpen") is bool open && open, "no-wire row opens the colour correction dialog");
            app.StopAllCoroutines();
        }

        IEnumerator Confirm(Action done) { yield return (IEnumerator)Call("ConfirmWires", "Save reader end"); done(); }
        IEnumerator WaitButton(string name) { yield return Until(() => Button(name) != null, "button " + name); }
        IEnumerator WaitTitle(string text) { yield return Until(() => ((TMP_Text)Get("sheetTitle")).text == text, "title " + text); yield return null; }
        IEnumerator WaitText(string text) { yield return Until(() => Array.Exists(FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None), t => t.text == text), text); }
        IEnumerator Until(Func<bool> ready, string name)
        {
            float deadline = Time.realtimeSinceStartup + 12f;
            while (!ready()) { if (Time.realtimeSinceStartup > deadline) throw new Exception("Timed out: " + name); yield return null; }
            yield return null; // allow Destroy from the previous sheet to complete
        }
        Button Button(string text)
        {
            foreach (var b in ((RectTransform)Get("sheetButtons")).GetComponentsInChildren<Button>())
                if (b.gameObject.activeInHierarchy && b.GetComponentInChildren<TMP_Text>().text == text) return b;
            return null;
        }
        void Press(string name) { var b = Button(name); Assert(b != null, "tap " + name); b.onClick.Invoke(); }
        object Get(string name) { return typeof(InstallerApp).GetField(name, Private).GetValue(app); }
        void Set(string name, object value) { typeof(InstallerApp).GetField(name, Private).SetValue(app, value); }
        object Call(string name, params object[] args) { return typeof(InstallerApp).GetMethod(name, Private).Invoke(app, args); }
        void Assert(bool yes, string name) { if (!yes) throw new Exception("Reader flow regression: " + name); passed.Add("PASS " + name); }

        void Capture(string filename)
        {
            var cam = new GameObject("UI evidence camera", typeof(Camera)).GetComponent<Camera>();
            cam.transform.position = new Vector3(0,0,-10); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.gray;
            var target = new RenderTexture(540,1200,24); target.Create(); cam.targetTexture = target;
            var canvas = (Canvas)Get("canvas"); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases();
            foreach (var b in ((RectTransform)Get("sheetButtons")).GetComponentsInChildren<Button>())
            {
                var corners = new Vector3[4]; b.GetComponent<RectTransform>().GetWorldCorners(corners);
                foreach (var corner in corners) { var vp = cam.WorldToViewportPoint(corner); Assert(vp.y >= 0 && vp.y <= 1, "retry button lies inside viewport"); }
            }
            cam.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(540,1200,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,540,1200),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(output,filename), image.EncodeToPNG()); RenderTexture.active = previous;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            Destroy(image); Destroy(cam.gameObject); target.Release(); Destroy(target);
        }
        static ImagePoint[] Points() { return new[] { new ImagePoint(.35f,.4f),new ImagePoint(.45f,.4f),new ImagePoint(.55f,.4f),new ImagePoint(.65f,.4f) }; }
        static DeviceEndReading Reading(int[] colours, float confidence) { return new DeviceEndReading(colours, new[] { confidence,confidence,confidence,confidence }, Points()); }

        sealed class Reader : IWireReader
        {
            public int Count; public readonly Queue<DeviceEndReading> Next = new Queue<DeviceEndReading>();
            public IEnumerator ReadDeviceEnd(UsageRule r, CableLabel c, CameraFrame f, Action<DeviceEndReading> done) { Count++; yield return null; done(Next.Dequeue()); }
            public IEnumerator ReadControllerEnd(UsageRule r, CableLabel c, CameraFrame f, Action<ControllerReading> done) { throw new Exception("Unexpected controller read"); }
        }
        sealed class UnknownOpinion : ISecondOpinion
        { public IEnumerator Compare(CableRecord r, CameraFrame d, CameraFrame c, Action<Opinion[]> done) { yield return null; var opinions = new Opinion[8]; for (int i = 0; i < opinions.Length; i++) opinions[i] = Opinion.Unknown; done(opinions); } }
        sealed class View : ICameraView
        {
            public readonly Dictionary<string,Vector2> Anchors = new Dictionary<string,Vector2>(); long id;
            public bool SeeThrough => false; public Vector2 LabelHint => Vector2.one * .5f; public float MarkerScale => 1;
            public CameraFrame Latest => new CameraFrame { Id = ++id };
            public RectTransform Attach(RectTransform p) { var r = UiKit.Rect("Test camera",p); UiKit.Stretch(r); return r; }
            public void Detach() { } public void Tick() { } public void ShowEnd(CableEnd e) { Anchors.Clear(); }
            public void ClearAnchors() { Anchors.Clear(); }
            public void Anchor(string key, ImagePoint p, CameraFrame f) { Anchors[key] = new Vector2(p.X,p.Y); }
            public bool TryGetPosition(string key, out Vector2 p) { return Anchors.TryGetValue(key,out p); }
            public EndSnapshot Snapshot(CableEnd e, ImagePoint[] p) { return new EndSnapshot { End = e }; }
        }
        sealed class Operator : IStudyOperator
        {
            public int RewiredCount; public CableEnd StartEnd { get; set; } public string[] FaultNames => new string[0]; public int Fault => 0;
            public bool StartWithoutLabel { get; set; } public bool ShowOperatorKeys { get; set; }
            public void ChooseFault(int i) { } public void SessionReset() { } public void BeginCheck() { } public void LabelAttached() { }
            public void Rewired(CableRecord r) { } public void RewiredAtDevice() { RewiredCount++; }
            public CableLabel Prefill(IRecordStore s, out EndSnapshot shot) { shot = null; return null; }
        }
    }
}
#endif
