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
    /// In the Editor, or with arCamera off (stage 0): a webcam fallback. It shows a WebCamTexture full screen, hands
    /// its pixels to the readers as RGBA32 frames, and keeps anchors as 2D image points, so marks stay where they were
    /// read on the screen, not on the pins. Enough to run the whole live flow with a laptop camera and the service.
    ///
    /// Stage 1 (ArRig.cs), the AR Foundation 6.5 camera, keeping this class's public behaviour.
    ///   Frames    ARCameraManager.TryAcquireLatestCpuImage(out XRCpuImage img); img.Convert(new
    ///             XRCpuImage.ConversionParams(img, TextureFormat.RGBA32, XRCpuImage.Transformation.MirrorY), buffer)
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
        public int requestedWidth = 1280, requestedHeight = 720;

        [Header("AR camera (on the phone)")]
        [Tooltip("On the phone, use the AR camera (ARCore through AR Foundation): marks stay on the pins while the phone moves. Off = the webcam fallback, as in the Editor.")]
        public bool arCamera = true;
        [Tooltip("Largest camera image width the AR camera may give the readers (the perception service works at 1920).")]
        public int arMaxImageWidth = 1920;
        [Tooltip("Turn on if, on the phone, the marks land mirrored left to right (left pins marked on the right).")]
        public bool arFlipImageX = false;
        [Tooltip("Turn on if, on the phone, the marks land mirrored top to bottom (top pins marked at the bottom).")]
        public bool arFlipImageY = false;

        ArRig ar;

        WebCamTexture cam;
        RectTransform root, imageRT, markers;
        RawImage image;
        TextMeshProUGUI notice;
        readonly Dictionary<string, Vector2> anchors = new Dictionary<string, Vector2>();   // in raw image coordinates
        long frameId;
        Color32[] buffer;
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
                if (buffer == null || buffer.Length != w * h) buffer = new Color32[w * h];
                cam.GetPixels32(buffer);
                var bytes = new byte[w * h * 4];
                for (int i = 0, k = 0; i < buffer.Length; i++, k += 4)
                {
                    var c = buffer[i];
                    bytes[k] = c.r; bytes[k + 1] = c.g; bytes[k + 2] = c.b; bytes[k + 3] = c.a;
                }
                f.Width = w; f.Height = h; f.Format = FramePixels.Rgba32; f.Pixels = bytes;
                f.Source = "webcam:" + cam.deviceName;
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
            if (root != null) Destroy(root.gameObject);
            root = imageRT = markers = null;
            image = null; notice = null;
        }

        public void ShowEnd(CableEnd end) { anchors.Clear(); if (ar != null) ar.Clear(); }

        public void Anchor(string key, ImagePoint where, CameraFrame frame)
        {
            if (ar != null) { ar.Anchor(key, where, frame); return; }
            if (where.Valid) anchors[key] = new Vector2(where.X, where.Y);
        }

        public void ClearAnchors() { anchors.Clear(); if (ar != null) ar.Clear(); }

        public bool TryGetPosition(string key, out Vector2 position)
        {
            if (ar != null) return ar.TryGetPosition(key, out position);
            position = Vector2.zero;
            Vector2 raw;
            if (!anchors.TryGetValue(key, out raw) || layerSize.x <= 0 || layerSize.y <= 0) return false;
            Vector2 d = ToDisplay(raw);
            position = new Vector2(0.5f + (d.x - 0.5f) * displaySize.x / layerSize.x, 0.5f + (d.y - 0.5f) * displaySize.y / layerSize.y);
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
        }

        // ================================================================== webcam

        bool Ready { get { return cam != null && cam.isPlaying && cam.width > 32 && cam.height > 32; } }

        /// <summary>Raw image point (x right, y down) to the displayed, rotated image (x right, y down).</summary>
        Vector2 ToDisplay(Vector2 p)
        {
            switch (rotation)
            {
                case 90: return new Vector2(1f - p.y, p.x);
                case 180: return new Vector2(1f - p.x, 1f - p.y);
                case 270: return new Vector2(p.y, 1f - p.x);
                default: return p;
            }
        }

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
            if (cam != null) { cam.Stop(); Destroy(cam); }
        }
    }
}
