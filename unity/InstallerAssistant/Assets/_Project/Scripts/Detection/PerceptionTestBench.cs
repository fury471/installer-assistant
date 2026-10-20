using System.Collections;
using System.Text;
using InstallerAssistant.Labels;
using InstallerAssistant.Verification;
using UnityEngine;

namespace InstallerAssistant.Detection
{
    /// <summary>
    /// Package A's workbench in Unity (scene Scenes/Sandbox/Perception_Test): runs the live label scanner and the
    /// perception service on one still photo and draws what came back, without the app around it.
    /// Start the service first (ml/perception_service), then press Play, or use the component's ⋮ menu → Run.
    /// For batch accuracy numbers use ml/eval instead; this is for looking at single cases.
    /// </summary>
    public sealed class PerceptionTestBench : MonoBehaviour
    {
        [Tooltip("A photo of one end. Import settings: Read/Write enabled.")]
        public Texture2D image;
        public CableEnd end = CableEnd.Controller;
        [Tooltip("The cable's label (its plan): selects the wiring rule and the module the service looks for.")]
        public string cableLabel = "IA|C01|D1|A1610.READER1|A4020E";
        public string perceptionUrl = "http://localhost:8000";
        public bool runOnStart = true;

        PerceptionClient.ReadResponse last;
        string status = "Not run yet";
        string labelText = "";
        UsageRule rule;

        void Start() { if (runOnStart) Run(); }

        [ContextMenu("Run")]
        public void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[PerceptionTestBench] Enter Play mode first."); return; }
            StopAllCoroutines();
            StartCoroutine(RunRoutine());
        }

        IEnumerator RunRoutine()
        {
            CableLabel plan; string problem = "not a cable label";
            rule = CableLabel.TryParse(cableLabel, out plan) ? AxisCatalog.Resolve(plan, out problem) : null;
            if (image == null || rule == null) { status = image == null ? "Assign an image" : "Label \"" + cableLabel + "\": " + problem; yield break; }
            if (!image.isReadable) { status = "Enable Read/Write on the image's import settings"; yield break; }
            var frame = FrameOf(image);

            // the label, through the same scanner the app uses
            LabelScan scan = null;
            yield return new QrLabelScanner().Scan(frame, s => scan = s);
            labelText = scan.Status == LabelScanStatus.Found ? "Label " + scan.Label.Payload : "Label: " + scan.Status;

            // the wires, through the same client the app uses
            status = "Calling " + perceptionUrl + "/read …";
            var client = new PerceptionClient(perceptionUrl);
            float t0 = Time.realtimeSinceStartup;
            string error = null;
            yield return client.Read(frame, end, rule, plan, (r, e) => { last = r; error = e; });
            float ms = (Time.realtimeSinceStartup - t0) * 1000f;
            if (error != null) { status = "Error: " + error; Debug.LogWarning("[PerceptionTestBench] " + error); yield break; }

            var names = end == CableEnd.Controller ? PerceptionClient.PinNames(rule) : PerceptionClient.TerminalNames(rule);
            var sb = new StringBuilder("model " + last.model + " · " + ms.ToString("0") + " ms\n");
            for (int i = 0; i < names.Length && last.items != null && i < last.items.Length; i++)
            {
                var it = last.items[i];
                sb.Append(names[i]).Append(": ").Append(WireColours.Name(it.colour)).Append(' ').Append((it.confidence * 100).ToString("0")).Append('%')
                  .Append(it.found ? " at (" + it.x.ToString("0.000") + ", " + it.y.ToString("0.000") + ")" : " not located")
                  .Append(it.colour >= 0 ? " · runs to " + it.trace : "").Append('\n');
            }
            // the zone survey: which of the controller's other modules the service found, and how many wires each holds
            if (end == CableEnd.Controller && last.other_blocks != null)
                foreach (var b in last.other_blocks)
                {
                    int wires = 0;
                    if (b.items != null) foreach (var it in b.items) if (it.colour >= 0) wires++;
                    sb.Append(b.module).Append(": ").Append(b.located ? wires + (wires == 1 ? " wire" : " wires") : "not located").Append('\n');
                }
            status = sb.ToString();
            Debug.Log("[PerceptionTestBench] " + labelText + "\n" + status);
        }

        static CameraFrame FrameOf(Texture2D tex)
        {
            var px = tex.GetPixels32();
            var bytes = new byte[px.Length * 4];
            for (int i = 0, k = 0; i < px.Length; i++, k += 4) { bytes[k] = px[i].r; bytes[k + 1] = px[i].g; bytes[k + 2] = px[i].b; bytes[k + 3] = px[i].a; }
            return new CameraFrame { Id = 1, Width = tex.width, Height = tex.height, Format = FramePixels.Rgba32, Pixels = bytes, Source = "testbench:" + tex.name };
        }

        void OnGUI()
        {
            if (image == null) return;
            float w = Screen.width, h = Screen.height;
            float aspect = image.width / (float)image.height;
            float iw = w, ih = w / aspect;
            if (ih > h * 0.7f) { ih = h * 0.7f; iw = ih * aspect; }
            var r = new UnityEngine.Rect((w - iw) / 2, 10, iw, ih);
            GUI.DrawTexture(r, image, ScaleMode.StretchToFill);
            if (last != null && last.items != null)
                for (int i = 0; i < last.items.Length; i++)
                {
                    var it = last.items[i];
                    if (!it.found) continue;
                    var p = new Vector2(r.x + it.x * r.width, r.y + it.y * r.height);
                    GUI.color = it.colour >= 0 && it.colour < UI.UiKit.Wire.Length ? UI.UiKit.Wire[it.colour] : Color.gray;
                    GUI.DrawTexture(new UnityEngine.Rect(p.x - 8, p.y - 8, 16, 16), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(new UnityEngine.Rect(p.x + 10, p.y - 10, 200, 22), (i + 1) + " " + (it.confidence * 100).ToString("0") + "%");
                }
            GUI.Label(new UnityEngine.Rect(10, r.yMax + 10, w - 20, h - r.yMax - 20), labelText + "\n" + status);
        }
    }
}
