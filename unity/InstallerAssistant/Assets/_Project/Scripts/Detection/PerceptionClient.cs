using System;
using System.Collections;
using System.Text;
using InstallerAssistant.Labels;
using InstallerAssistant.Verification;
using UnityEngine;
using UnityEngine.Networking;

namespace InstallerAssistant.Detection
{
    /// <summary>
    /// Talks to the perception service in ml/perception_service over HTTP and JSON. Owner: package A.
    /// The JSON contract is documented in ml/perception_service/README.md and mirrored by the classes below;
    /// change both sides together.
    ///
    ///   GET  /health    {"status":"ok","model":"..."}
    ///   POST /read      ReadRequest    → ReadResponse     (layer 1: colour, confidence and position per terminal,
    ///                                                       plus the zone survey of the controller's other modules)
    ///   POST /compare   CompareRequest → CompareResponse  (layer 3: independent match/mismatch per pin)
    ///
    /// On a phone, "localhost" is the phone: use the laptop's address (same Wi-Fi), e.g. http://192.168.1.20:8000,
    /// and allow plain HTTP in Player Settings → Other Settings → "Allow downloads over HTTP".
    /// Option for later: run the model on the phone (ONNX with Unity Sentis) behind the same IWireReader.
    /// </summary>
    public sealed class PerceptionClient
    {
        public readonly string BaseUrl;
        public int TimeoutSeconds = 20;
        public int JpegQuality = 85;

        public PerceptionClient(string baseUrl) { BaseUrl = (baseUrl ?? "").TrimEnd('/'); }

        // ------------------------------------------------------------------ JSON contract (snake_case on the wire)

        [Serializable]
        public sealed class ImagePayload
        {
            public int width, height;
            /// <summary>"jpeg"; empty data_b64 means "no image" (JsonUtility cannot write null objects).</summary>
            public string format = "jpeg";
            public string data_b64 = "";
            /// <summary>fx, fy, cx, cy in pixels; empty if unknown.</summary>
            public float[] intrinsics = new float[0];
        }

        [Serializable]
        public sealed class ModulePayload
        {
            /// <summary>Module as written on labels, e.g. "A1610.READER2".</summary>
            public string module;
            /// <summary>Its terminal names as printed, pin 1..n.</summary>
            public string[] terminals;
        }

        [Serializable]
        public sealed class ReadRequest
        {
            public long frame_id;
            /// <summary>"device" or "controller".</summary>
            public string end;
            /// <summary>The module to read, as the label names it: "A1610.READER1" or "A4020E". Selects the reference template.</summary>
            public string module;
            /// <summary>Names as printed, in the order the response must use (device: rule order; controller: pin 1..n).</summary>
            public string[] terminals;
            /// <summary>Controller only: the controller's other modules, to survey in the same frame (zone check). Empty at the device.</summary>
            public ModulePayload[] other_modules = new ModulePayload[0];
            /// <summary>The scanned cable label, without the end ("IA|C01|D1|A1610.READER1|A4020E"): each wire is traced back to it.</summary>
            public string label = "";
            public ImagePayload image;
        }

        [Serializable]
        public sealed class ReadItem
        {
            /// <summary>Wire colour code: 0 Red, 1 Black, 2 Green, 3 Yellow, 4 White, 5 Brown, 6 Blue, 7 Orange, -1 none.</summary>
            public int colour = -1;
            public float confidence;
            /// <summary>Terminal position, normalised to the image: x right, y down.</summary>
            public float x, y;
            /// <summary>False when the terminal could not be located.</summary>
            public bool found;
            /// <summary>Where the wire runs back to: "this_cable" (the scanned label), "other_cable", or "unknown".</summary>
            public string trace = "unknown";
        }

        [Serializable]
        public sealed class ModuleItems
        {
            public string module;
            /// <summary>False when the module was not found in the frame.</summary>
            public bool located;
            /// <summary>One per terminal of that module, in the order of its ModulePayload.terminals.</summary>
            public ReadItem[] items = new ReadItem[0];
        }

        [Serializable]
        public sealed class ReadResponse
        {
            public long frame_id;
            public ReadItem[] items = new ReadItem[0];
            /// <summary>One per requested other module, in the same order; empty if the service does not survey.</summary>
            public ModuleItems[] other_blocks = new ModuleItems[0];
            public string model = "";
            public string error = "";
        }

        [Serializable]
        public sealed class PairPayload
        {
            public string terminal;
            public int pin;
        }

        [Serializable]
        public sealed class CompareRequest
        {
            /// <summary>The two modules from the label, e.g. "A1610.READER2" and "A4020E".</summary>
            public string controller_module;
            public string device_module;
            public int pin_count;
            public PairPayload[] pairs;
            public ImagePayload device_image;
            public ImagePayload controller_image;
        }

        [Serializable]
        public sealed class CompareResponse
        {
            /// <summary>One per pin (index = pin - 1): "match", "mismatch" or "unknown".</summary>
            public string[] opinions = new string[0];
            public string model = "";
            public string error = "";
        }

        // ------------------------------------------------------------------ calls

        /// <summary>Reads one end. done(response, null) on success, done(null, error) otherwise.</summary>
        public IEnumerator Read(CameraFrame frame, CableEnd end, UsageRule rule, CableLabel cable, Action<ReadResponse, string> done)
        {
            var image = Encode(frame);
            if (image == null) { done(null, "the camera frame has no pixels (" + (frame != null ? frame.Source : "no frame") + ")"); yield break; }
            bool ctrl = end == CableEnd.Controller;
            var others = ctrl ? AxisCatalog.OtherModules(rule) : new ConnectorSpec[0];
            var survey = new ModulePayload[others.Length];
            for (int i = 0; i < others.Length; i++) survey[i] = new ModulePayload { module = others[i].Id, terminals = PinNames(others[i]) };
            var req = new ReadRequest
            {
                frame_id = frame.Id, end = ctrl ? "controller" : "device",
                module = ctrl ? rule.ControllerModule.Code : rule.DeviceModule.Code,
                terminals = ctrl ? PinNames(rule) : TerminalNames(rule), other_modules = survey, image = image,
                label = cable != null ? cable.AtEnd(CableEnd.Unknown).Payload : "",
            };
            string text = null, error = null;
            yield return Post("/read", JsonUtility.ToJson(req), (t, e) => { text = t; error = e; });
            if (error != null) { done(null, error); yield break; }
            ReadResponse res = null;
            try { res = JsonUtility.FromJson<ReadResponse>(text); }
            catch (Exception e) { error = "bad JSON from /read: " + e.Message; }
            if (res != null && !string.IsNullOrEmpty(res.error)) error = res.error;
            done(error == null ? res : null, error);
        }

        /// <summary>Asks for the independent comparison of both ends. Either frame may be null.</summary>
        public IEnumerator Compare(UsageRule rule, CameraFrame deviceEnd, CameraFrame controllerEnd, Action<CompareResponse, string> done)
        {
            var pairs = new PairPayload[rule.Pairs.Length];
            for (int i = 0; i < pairs.Length; i++)
                pairs[i] = new PairPayload { terminal = rule.Terminal(rule.Pairs[i].DeviceTerminal).Print, pin = rule.Pairs[i].ControllerPin };
            var req = new CompareRequest
            {
                controller_module = rule.ControllerModule.Code, device_module = rule.DeviceModule.Code,
                pin_count = rule.Connector.Count, pairs = pairs,
                device_image = Encode(deviceEnd) ?? new ImagePayload(), controller_image = Encode(controllerEnd) ?? new ImagePayload(),
            };
            string text = null, error = null;
            yield return Post("/compare", JsonUtility.ToJson(req), (t, e) => { text = t; error = e; });
            if (error != null) { done(null, error); yield break; }
            CompareResponse res = null;
            try { res = JsonUtility.FromJson<CompareResponse>(text); }
            catch (Exception e) { error = "bad JSON from /compare: " + e.Message; }
            if (res != null && !string.IsNullOrEmpty(res.error)) error = res.error;
            done(error == null ? res : null, error);
        }

        public IEnumerator Health(Action<string, string> done)
        {
            using (var req = UnityWebRequest.Get(BaseUrl + "/health"))
            {
                req.timeout = TimeoutSeconds;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) done(null, req.error);
                else done(req.downloadHandler.text, null);
            }
        }

        IEnumerator Post(string path, string json, Action<string, string> done)
        {
            using (var req = new UnityWebRequest(BaseUrl + path, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = TimeoutSeconds;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                    done(null, BaseUrl + path + ": " + req.error + (req.downloadHandler != null && !string.IsNullOrEmpty(req.downloadHandler.text) ? " · " + req.downloadHandler.text : ""));
                else done(req.downloadHandler.text, null);
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// The frame as a JPEG, or null if it has no pixels. Rgba32 pixels are in Unity texture order (row 0 = bottom,
        /// as Texture2D.LoadRawTextureData expects); the JPEG comes out upright. Yuv420 is not supported here:
        /// the AR runtime converts the CPU image to RGBA32 first.
        /// </summary>
        public ImagePayload Encode(CameraFrame f)
        {
            if (f == null || f.Pixels == null || f.Pixels.Length == 0 || f.Width <= 0 || f.Height <= 0) return null;
            byte[] jpeg;
            if (f.Format == FramePixels.Jpeg) jpeg = f.Pixels;
            else if (f.Format == FramePixels.Rgba32 && f.Pixels.Length >= f.Width * f.Height * 4)
            {
                var tex = new Texture2D(f.Width, f.Height, TextureFormat.RGBA32, false);
                tex.LoadRawTextureData(f.Pixels);
                tex.Apply(false);
                jpeg = tex.EncodeToJPG(JpegQuality);
                UnityEngine.Object.Destroy(tex);
            }
            else return null;
            return new ImagePayload
            {
                width = f.Width, height = f.Height, format = "jpeg", data_b64 = Convert.ToBase64String(jpeg),
                intrinsics = f.Intrinsics ?? new float[0],
            };
        }

        public static string[] TerminalNames(UsageRule rule)
        {
            var n = new string[rule.DeviceTerminals.Length];
            for (int i = 0; i < n.Length; i++) n[i] = rule.DeviceTerminals[i].Print;
            return n;
        }

        public static string[] PinNames(UsageRule rule) { return PinNames(rule.Connector); }

        public static string[] PinNames(ConnectorSpec module)
        {
            var n = new string[module.Count];
            for (int p = 1; p <= n.Length; p++) n[p - 1] = module.Pin(p).Print;
            return n;
        }
    }
}
