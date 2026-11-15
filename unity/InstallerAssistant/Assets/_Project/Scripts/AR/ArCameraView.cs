using System.Collections.Generic;
using InstallerAssistant.Detection;
using InstallerAssistant.Labels;
using InstallerAssistant.UI;
using InstallerAssistant.Verification;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static InstallerAssistant.UI.UiKit;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace InstallerAssistant.AR
{
    /// <summary>
    /// The live camera view. Owner: package B (AR runtime).
    ///
    /// On the phone (arCamera on): the AR camera, ArRig (stage 1 below, built at run time). Marks are world anchors and
    /// stay on the pins while the phone moves.
    ///
    /// In the Editor, or with arCamera off (stage 0): a webcam fallback. It shows a WebCamTexture full screen and hands
    /// its pixels to the readers as RGBA32 frames. There is no AR pose here, so the marks are kept on the pins in
    /// the image itself: PlaneTracker follows the picture from one camera image to the next (corners, pyramidal
    /// Lucas-Kanade, a robust affine fit), every anchor moves with it, and an anchor set from a frame that was read
    /// a moment ago is first carried to now. When the picture cannot be followed (blur, the board left the image)
    /// the marks are hidden until the next read, never left where they were. Enough to run the whole live flow
    /// with a laptop camera, or a phone as the laptop's camera, and the service.
    ///
    /// Stage 1 (ArRig.cs), the AR Foundation 6.5 camera, keeping this class's public behaviour.
    ///   Frames    ARCameraManager.TryAcquireLatestCpuImage(out XRCpuImage img); img.Convert(new
    ///             XRCpuImage.ConversionParams(img, TextureFormat.RGBA32, XRCpuImage.Transformation.MirrorX), buffer)
    ///             gives RGBA32 in Unity's bottom-up order (what CameraFrame expects). Dispose the image.
    ///             Intrinsics: ARCameraManager.TryGetIntrinsics(out XRCameraIntrinsics k) → fx fy cx cy (pixels).
    ///             Pose: the AR camera's localToWorldMatrix, column-major.
    ///   Display   An ARCameraBackground on the AR camera draws the feed: return SeeThrough = true, so the app keeps
    ///             its check screen transparent. ARCameraManager.frameReceived gives displayMatrix, which maps image
    ///             coordinates to the screen (portrait rotation, crop).
    ///   Anchor    image point → screen point (displayMatrix) → ARRaycastManager.Raycast(screenPoint, hits,
    ///             TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint) → keep the world point (or an ARAnchor
    ///             from ARAnchorManager). The controller board is a vertical plane: enable vertical plane detection.
    ///   Position  every frame, Camera.WorldToViewportPoint of the stored world point → (x, 1 - y); false when behind
    ///             the camera or outside the screen.
    ///   Snapshot  copy the converted CPU image into a Texture2D.
    /// Keep the webcam path as the Editor fallback (AR Foundation's XR Simulation is the other option).
    /// </summary>
    public sealed class ArCameraView : MonoBehaviour, ICameraView
    {
        [Tooltip("Use a webcam when no AR camera is set up (stage 0; also the Editor fallback).")]
        public bool webcamFallback = true;
        [Tooltip("Webcam name; empty = the first back-facing camera, else the first camera.")]
        public string webcamName = "";
        public int requestedWidth = 1920, requestedHeight = 1080;
        [Tooltip("Webcam only: follow the picture between reads, so the marks stay on the pins while the camera moves. Off = marks stay where they were read on the screen.")]
        public bool webcamTracking = true;
        [Tooltip("Webcam only: the long side of the small grey image the tracker works on (pixels). Larger follows finer detail and costs more time per frame.")]
        [Range(160, 480)] public int webcamTrackingSize = 320;

        [Header("AR camera (on the phone)")]
        [Tooltip("On the phone, use the AR camera (ARCore through AR Foundation): marks stay on the pins while the phone moves. Off = the webcam fallback, as in the Editor.")]
        public bool arCamera = true;
        [Tooltip("Largest camera image width the AR camera may give the readers (the perception service works at 1920).")]
        public int arMaxImageWidth = 1920;
        [Tooltip("Device override only: mirror image x before applying the camera background's display transform.")]
        public bool arFlipImageX = false;
        [Tooltip("Device override only: mirror image y before applying the camera background's display transform.")]
        public bool arFlipImageY = false;
        [Tooltip("Distance between neighbouring pins at the controller (mm): sets how far away the marks are. 5.0 for the A1610 / A1601 terminal blocks (measured against the 21.5 mm QR labels).")]
        public float arControllerPinSpacingMm = 5.0f;
        [Tooltip("Distance between neighbouring terminals at the device end (mm). 5.8 for the AXIS A4020-E reader (measured against the QR labels).")]
        public float arDevicePinSpacingMm = 5.8f;
        [Tooltip("Cast each mark's ray from the CPU image's own intrinsics (the image the reader saw), taking only the quarter-turn from the display transform. Off = the display transform alone, which assumes the CPU image and the screen texture share one field of view.")]
        public bool arIntrinsicsRays = true;
        [Tooltip("Fuse every read of the same end: each pin is triangulated from all the views it was read in, with the pin-spacing depth as a weak prior. Off = each read replaces the marks (the 2 Nov behaviour).")]
        public bool arFuseReads = true;
        [Tooltip("Hold the marks with an ARCore anchor, so ARCore's map corrections move them with the board.")]
        public bool arHoldWithArAnchor = true;
        [Tooltip("Write AR records (one JSON line per read, 1 Hz mark positions, screenshots on a three-finger tap) to persistentDataPath/ar-records. Geometry only, no camera images.")]
        public bool arRecords = true;

        ArRig ar;

        WebCamTexture cam;
        int reportedCameraWidth, reportedCameraHeight;
        RectTransform root, imageRT, markers;
        RawImage image;
        TextMeshProUGUI notice;
        sealed class Mark2D
        {
            public Vector2 At;             // in raw image coordinates (normalised, top-left), as of the latest camera image
            public Vector2 Glide;          // what is left of the step a re-read made: shown as At + Glide, fading out
            public bool Lost;              // the picture could not be followed since this was set: hidden until set again
        }
        struct TrackState { public double[] Motion; public int Epoch; }

        readonly Dictionary<string, Mark2D> anchors = new Dictionary<string, Mark2D>();
        readonly PlaneTracker tracker = new PlaneTracker();
        readonly Dictionary<long, TrackState> frameStates = new Dictionary<long, TrackState>();   // per frame handed out
        readonly Queue<long> frameOrder = new Queue<long>();
        byte[] trackGray;
        int trackW, trackH;
        bool tracking;                     // the tracker has seen the current camera image
        long frameId;
        Color32[] buffer;
        int bufferFrame = -1;              // Time.frameCount of the camera image in buffer
        int rotation;
        Vector2 displaySize, layerSize;

        void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (arCamera) ar = ArRig.Create(this);     // null (webcam fallback) if AR cannot start
#endif
        }

        public string Describe()
        {
            if (ar != null) return ar.Describe();
            if (cam != null) return "webcam \"" + cam.deviceName + "\"";
            return webcamFallback ? "webcam fallback (camera starts on the check screen)" : "none (AR camera not set up yet)";
        }

        // ================================================================== ICameraView

        /// <summary>False while the webcam image is drawn in the UI; true when the AR background renders behind it (stage 1).</summary>
        public bool SeeThrough { get { return ar != null; } }
        public float MarkerScale { get { return 1f; } }
        public Vector2 LabelHint { get { return new Vector2(0.5f, 0.62f); } }

        public CameraFrame Latest
        {
            get
            {
                if (ar != null) return ar.Latest(++frameId);
                var f = new CameraFrame { Id = ++frameId, Timestamp = Time.unscaledTimeAsDouble, Format = FramePixels.None, Source = "none" };
                if (!Ready) return f;
                int w = cam.width, h = cam.height;
                ReadCamera();
                // Color32 is four bytes r, g, b, a in memory: one block copy instead of eight million array writes
                // (measured outside Unity on 1920 x 1080: 3.7 ms against 7.9 ms per frame)
                var bytes = new byte[w * h * 4];
                var pin = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { System.Runtime.InteropServices.Marshal.Copy(pin.AddrOfPinnedObject(), bytes, 0, bytes.Length); }
                finally { pin.Free(); }
                f.Width = w; f.Height = h; f.Format = FramePixels.Rgba32; f.Pixels = bytes;
                f.Source = "webcam:" + cam.deviceName;
                if (tracking)                              // where the picture was when this frame was handed out
                {
                    frameStates[f.Id] = new TrackState { Motion = (double[])tracker.Cumulative.Clone(), Epoch = tracker.Epoch };
                    frameOrder.Enqueue(f.Id);
                    while (frameOrder.Count > 60) frameStates.Remove(frameOrder.Dequeue());
                }
                return f;
            }
        }

        public RectTransform Attach(RectTransform parent)
        {
            Detach();
            if (ar != null)                                  // the AR background is drawn behind the UI: a clear layer
            {
                root = UiKit.Rect("AR camera", parent);
                Stretch(root);
                root.SetAsFirstSibling();
                notice = Txt(root, "", Small, Hex("#F3F4F6"), Face.Regular, TextAlignmentOptions.Center);
                Place(notice.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(900, 300));
                markers = UiKit.Rect("Markers", root);
                Stretch(markers);
                return markers;
            }
            var bg = Img("Camera", parent, null, Hex("#111111"), 0);
            root = bg.rectTransform;
            Stretch(root);
            root.SetAsFirstSibling();
            bg.gameObject.AddComponent<RectMask2D>();
            image = new GameObject("Webcam", typeof(RectTransform)).AddComponent<RawImage>();
            image.transform.SetParent(root, false);
            image.raycastTarget = false;
            imageRT = image.rectTransform;
            imageRT.anchorMin = imageRT.anchorMax = new Vector2(0.5f, 0.5f);
            image.enabled = false;
            notice = Txt(root, "", Small, Hex("#D1D5DB"), Face.Regular, TextAlignmentOptions.Center);
            Place(notice.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(900, 300));
            markers = UiKit.Rect("Markers", root);
            Stretch(markers);
            StartCamera();
            return markers;
        }

        public void Detach()
        {
            if (cam != null && cam.isPlaying) cam.Stop();
            tracking = false; tracker.Reset(); frameStates.Clear(); frameOrder.Clear();
            if (root != null) Destroy(root.gameObject);
            root = imageRT = markers = null;
            image = null; notice = null;
        }

        public void ShowEnd(CableEnd end) { anchors.Clear(); if (ar != null) ar.Clear(); }

        /// <summary>For checks in the Editor: the tracker's state (points followed, points that agree).</summary>
        public string TrackingState { get { return tracking ? tracker.Inliers + "/" + tracker.Tracked + " epoch " + tracker.Epoch : "off"; } }

        public void Anchor(string key, ImagePoint where, CameraFrame frame)
        {
            if (ar != null) { ar.Anchor(key, where, frame); return; }
            if (!where.Valid) return;
            var at = new Vector2(where.X, where.Y);
            // the frame was read a moment ago and the camera has moved since: carry the point to the picture of now
            TrackState then;
            if (tracking && frame != null && frameStates.TryGetValue(frame.Id, out then))
            {
                double[] back;
                if (then.Epoch != tracker.Epoch || !PlaneTracker.Invert(then.Motion, out back)) return;   // not followed since: no place to show
                double x, y;
                PlaneTracker.Apply(PlaneTracker.Compose(tracker.Cumulative, back), at.x * trackW, at.y * trackH, out x, out y);
                at = new Vector2((float)x / trackW, (float)y / trackH);
            }
            Mark2D old;
            var mark = new Mark2D { At = at };
            // a re-read moves a mark by the reader's own scatter: glide there instead of jumping
            if (anchors.TryGetValue(key, out old) && !old.Lost && (old.At + old.Glide - at).magnitude < 0.08f) mark.Glide = old.At + old.Glide - at;
            anchors[key] = mark;
        }

        public void ClearAnchors() { anchors.Clear(); if (ar != null) ar.Clear(); }

        public bool TryGetPosition(string key, out Vector2 position)
        {
            if (ar != null) return ar.TryGetPosition(key, out position);
            position = Vector2.zero;
            Mark2D mark;
            if (!anchors.TryGetValue(key, out mark) || mark.Lost || image == null ||
                !CameraImageGeometry.TryWebcamViewport(mark.At + mark.Glide, image.uvRect, imageRT, markers, out position)) return false;
            return position.x >= 0 && position.x <= 1 && position.y >= 0 && position.y <= 1;
        }

        public EndSnapshot Snapshot(CableEnd end, ImagePoint[] positions)
        {
            var shot = new EndSnapshot { End = end };
            int n = positions != null ? positions.Length : 0;
            shot.Points = new Vector2[n];
            for (int i = 0; i < n; i++) shot.Points[i] = positions[i].Valid ? new Vector2(positions[i].X, positions[i].Y) : new Vector2(0.5f, 0.5f);
            if (ar != null || !Ready) return shot;            // AR: the app keeps the frame that was read as the image
            var tex = new Texture2D(cam.width, cam.height, TextureFormat.RGBA32, false);
            tex.SetPixels32(cam.GetPixels32());
            tex.Apply(false);
            tex.name = "snapshot-" + CableLabel.EndCode(end);
            shot.Image = tex;   // unrotated camera image, like the positions
            return shot;
        }

        public void Tick()
        {
            if (root == null) return;
            layerSize = root.rect.size;
            if (ar != null)
            {
                ar.Tick();
                if (notice != null) notice.text = ar.Status;
                return;
            }
            if (cam == null) { StartCamera(); return; }
            if (!Ready) { if (notice != null) notice.text = "Starting the camera…"; return; }
            if (cam.width != reportedCameraWidth || cam.height != reportedCameraHeight)
            {
                reportedCameraWidth = cam.width; reportedCameraHeight = cam.height;
                string size = $"[ArCameraView] {cam.deviceName}: requested {requestedWidth}x{requestedHeight}, received {cam.width}x{cam.height}.";
                if (cam.width != requestedWidth || cam.height != requestedHeight)
                    Debug.LogWarning(size + " Check the camera application's output resolution and the active ArCameraView settings.");
                else Debug.Log(size);
            }
            if (notice != null) notice.text = "";
            image.enabled = true;
            image.texture = cam;
            rotation = ((Mathf.RoundToInt(cam.videoRotationAngle / 90f) * 90) % 360 + 360) % 360;
            bool swap = rotation == 90 || rotation == 270;
            float camAspect = cam.width / (float)cam.height;
            float dispAspect = swap ? 1f / camAspect : camAspect;
            // like a camera app: the image always fills the screen (envelope), centred
            float w = layerSize.x, h = w / dispAspect;
            if (h < layerSize.y) { h = layerSize.y; w = h * dispAspect; }
            displaySize = new Vector2(w, h);
            imageRT.sizeDelta = swap ? new Vector2(h, w) : new Vector2(w, h);
            imageRT.localEulerAngles = new Vector3(0, 0, -rotation);
            image.uvRect = cam.videoVerticallyMirrored ? new UnityEngine.Rect(0, 1, 1, -1) : new UnityEngine.Rect(0, 0, 1, 1);
            Track();
        }

        // ================================================================== following the picture (webcam)

        /// <summary>Feeds the tracker the new camera image and moves every anchor with the picture.</summary>
        void Track()
        {
            if (!webcamTracking) { tracking = false; return; }
            if (!cam.didUpdateThisFrame && tracking) { Fade(); return; }
            if (!Shrink()) return;
            if (anchors.Count > 0)                           // look for corners around the marks: the board, not the table
            {
                float x0 = 1, y0 = 1, x1 = 0, y1 = 0;
                foreach (var m in anchors.Values) { x0 = Mathf.Min(x0, m.At.x); x1 = Mathf.Max(x1, m.At.x); y0 = Mathf.Min(y0, m.At.y); y1 = Mathf.Max(y1, m.At.y); }
                float mx = Mathf.Max(0.25f, 1.2f * (x1 - x0)), my = Mathf.Max(0.25f, 1.2f * (y1 - y0));
                tracker.Focus(x0 - mx, y0 - my, x1 + mx, y1 + my);
            }
            else tracker.Focus(0, 0, 1, 1);
            bool followed = tracker.Step(trackGray, trackW, trackH);
            tracking = true;
            if (!followed)
            {
                foreach (var m in anchors.Values) m.Lost = true;   // hidden until the next read sets them again
                return;
            }
            var step = tracker.Last;
            foreach (var m in anchors.Values)
            {
                if (m.Lost) continue;
                double x, y;
                PlaneTracker.Apply(step, m.At.x * trackW, m.At.y * trackH, out x, out y);
                m.At = new Vector2((float)x / trackW, (float)y / trackH);
            }
            Fade();
        }

        void Fade()
        {
            float keep = Mathf.Exp(-Time.unscaledDeltaTime / 0.12f);
            foreach (var m in anchors.Values) m.Glide *= keep;
        }

        /// <summary>The camera's pixels in buffer, fetched from the camera at most once per rendered frame: the
        /// tracker and a frame handed to a reader in the same frame share one copy (each fetch moves 8 MB).</summary>
        void ReadCamera()
        {
            int w = cam.width, h = cam.height;
            if (buffer == null || buffer.Length != w * h) { buffer = new Color32[w * h]; bufferFrame = -1; }
            if (bufferFrame == Time.frameCount) return;
            cam.GetPixels32(buffer);
            bufferFrame = Time.frameCount;
        }

        /// <summary>The camera image as a small grey picture, row 0 at the top (as image points count), each pixel
        /// the mean of a few samples of its block.</summary>
        bool Shrink()
        {
            int w = cam.width, h = cam.height;
            ReadCamera();
            int f = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(w, h) / (float)webcamTrackingSize));
            int tw = w / f, th = h / f;
            if (tw < 48 || th < 48) return false;
            if (trackGray == null || trackW != tw || trackH != th) { trackGray = new byte[tw * th]; trackW = tw; trackH = th; tracker.Reset(); }
            int half = f / 2;
            for (int y = 0; y < th; y++)
            {
                int row = (h - 1 - y * f) * w, row2 = (h - 1 - y * f - half) * w;   // the camera's rows are bottom-up
                for (int x = 0; x < tw; x++)
                {
                    int c = x * f;
                    Color32 a = buffer[row + c], b = buffer[row + c + half], d = buffer[row2 + c], e = buffer[row2 + c + half];
                    // green counts twice, red and blue once: close to luminance, in integers
                    trackGray[y * tw + x] = (byte)((2 * (a.g + b.g + d.g + e.g) + a.r + b.r + d.r + e.r + a.b + b.b + d.b + e.b) >> 4);
                }
            }
            return true;
        }

        // ================================================================== webcam

        bool Ready { get { return cam != null && cam.isPlaying && cam.width > 32 && cam.height > 32; } }

        void StartCamera()
        {
            if (!webcamFallback) { if (notice != null) notice.text = "No AR camera yet (package B). Turn on the webcam fallback to test."; return; }
            if (cam != null) { if (!cam.isPlaying) cam.Play(); return; }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera)) { Permission.RequestUserPermission(Permission.Camera); return; }
#endif
            var devices = WebCamTexture.devices;
            if (devices.Length == 0) { if (notice != null) notice.text = "No camera found.\nLive mode needs the AR camera (package B) or a webcam."; return; }
            string name = webcamName;
            if (string.IsNullOrEmpty(name))
            {
                name = devices[0].name;
                foreach (var d in devices) if (!d.isFrontFacing) { name = d.name; break; }
            }
            else
            {
                bool present = false;
                foreach (var d in devices) if (d.name == name) { present = true; break; }
                if (!present)                     // a name saved for the laptop (e.g. iVCam) must not black out the phone
                {
                    Debug.LogWarning($"[ArCameraView] webcam '{name}' is not on this device; using the back camera instead.");
                    name = devices[0].name;
                    foreach (var d in devices) if (!d.isFrontFacing) { name = d.name; break; }
                }
            }
            cam = new WebCamTexture(name, requestedWidth, requestedHeight);
            cam.Play();
        }

        void OnDestroy()
        {
            if (ar != null) ar.Dispose();
            if (cam != null) { cam.Stop(); Destroy(cam); }
        }
    }
}
