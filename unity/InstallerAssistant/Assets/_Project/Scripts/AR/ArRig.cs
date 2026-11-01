using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using InstallerAssistant.Detection;
using InstallerAssistant.Verification;
using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace InstallerAssistant.AR
{
    /// <summary>
    /// The AR camera on the phone (stage 1 of ArCameraView; package B). Built at run time, so no scene has to change:
    /// an ARSession, and an XR Origin that takes over the scene's Main Camera with ARCameraManager (the feed and the
    /// CPU images the readers get), ARCameraBackground (draws the feed behind the UI), a TrackedPoseDriver (the phone's
    /// pose), ARRaycastManager, ARPlaneManager and ARPointCloudManager (to put anchors on real surfaces).
    ///
    /// Frames   the latest CPU image converted to RGBA32 in Unity's bottom-up order (MirrorY), the camera
    ///          configuration with the largest image up to maxImageWidth (the service works at 1920), auto-focus on.
    ///          For each frame handed out, the camera pose, projection and display matrix of that moment are kept,
    ///          because a reading comes back 1 to 3 s later, when the phone has moved.
    /// Anchor   image point -> the screen point it was drawn at in that frame (inverse of the display matrix, as
    ///          the ARCore background shader applies it: texture = (viewport x, viewport y, 1) * M) -> a ray from the
    ///          camera pose of that frame -> raycast against planes and feature points -> a world point. Without a hit
    ///          yet, the point is put on the ray at the depth of another pin of the same frame (or 30 cm) and the
    ///          raycast is tried again every frame for 10 s, as ARCore finds the surface.
    /// Position every frame the world point projected with the camera of now: marks stay on the pins while the
    ///          phone moves.
    /// Image y is turned to the texture's bottom-up v (checked on the K80S, 1 Nov). If the marks appear mirrored on
    /// another phone, ArCameraView's arFlipImageX / arFlipImageY turn an image axis.
    /// </summary>
    public sealed class ArRig
    {
        const float DefaultDepth = 0.30f;            // metres: about where the installer holds the phone
        const float RetrySeconds = 10f;
        const int KeepFrames = 40;
        const TrackableType Surfaces = TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated | TrackableType.FeaturePoint;

        struct FrameView { public Matrix4x4 CameraToWorld, Projection, Display; public bool HasDisplay; }

        sealed class Anchored
        {
            public Vector3 Point;
            public Ray Ray;
            public long Frame;
            public bool Estimated;
            public float Since;
        }

        readonly ArCameraView owner;
        readonly Camera cam;
        readonly ARCameraManager cameraManager;
        readonly ARRaycastManager raycasts;
        readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        readonly Dictionary<long, FrameView> frames = new Dictionary<long, FrameView>();
        readonly Queue<long> frameOrder = new Queue<long>();
        readonly Dictionary<long, float> frameDepth = new Dictionary<long, float>();
        readonly Dictionary<string, Anchored> anchors = new Dictionary<string, Anchored>();
        Matrix4x4 display = Matrix4x4.identity;
        bool hasDisplay, configured;

        /// <summary>Builds the AR rig around the Main Camera, or returns null (and logs why) if it cannot.</summary>
        public static ArRig Create(ArCameraView owner)
        {
            try { return new ArRig(owner); }
            catch (Exception e)
            {
                Debug.LogWarning("[ArRig] AR camera not started, using the webcam fallback: " + e.Message);
                return null;
            }
        }

        ArRig(ArCameraView owner)
        {
            this.owner = owner;
            cam = Camera.main;
            if (cam == null) throw new InvalidOperationException("the scene has no Main Camera");

            var sessionGo = new GameObject("AR Session");
            sessionGo.AddComponent<ARSession>();
            sessionGo.AddComponent<ARInputManager>();

            // build the origin inactive, so every component wakes up fully set up
            var originGo = new GameObject("XR Origin");
            originGo.SetActive(false);
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);
            var camGo = cam.gameObject;
            cam.transform.SetParent(offset.transform, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            cameraManager = camGo.AddComponent<ARCameraManager>();
            cameraManager.autoFocusRequested = true;              // labels read at 30-35 cm only when focused
            camGo.AddComponent<ARCameraBackground>();
            var pose = camGo.AddComponent<TrackedPoseDriver>();
            pose.positionInput = new InputActionProperty(new InputAction("AR device position", binding: "<HandheldARInputDevice>/devicePosition"));
            pose.rotationInput = new InputActionProperty(new InputAction("AR device rotation", binding: "<HandheldARInputDevice>/deviceRotation"));
            pose.ignoreTrackingState = true;

            var origin = originGo.AddComponent<XROrigin>();
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;
            raycasts = originGo.AddComponent<ARRaycastManager>();
            var planes = originGo.AddComponent<ARPlaneManager>();
            planes.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            originGo.AddComponent<ARPointCloudManager>();
            originGo.SetActive(true);

            cameraManager.frameReceived += OnFrame;
            Debug.Log("[ArRig] AR camera started");
        }

        /// <summary>What to tell the installer while AR is not tracking yet (empty when it is).</summary>
        public string Status
        {
            get
            {
                switch (ARSession.state)
                {
                    case ARSessionState.SessionTracking: return "";
                    case ARSessionState.Unsupported: return "This phone can't run AR.\nTurn off \"Ar Camera\" on the camera view to use the plain camera.";
                    case ARSessionState.NeedsInstall:
                    case ARSessionState.Installing: return "Installing Google Play Services for AR…";
                    default: return "Starting AR: move the phone slowly";
                }
            }
        }

        public string Describe()
        {
            var c = cameraManager != null ? cameraManager.currentConfiguration : null;
            return "AR camera (ARCore" + (c.HasValue ? ", " + c.Value.width + " x " + c.Value.height : "") + ", " + ARSession.state + ")";
        }

        void OnFrame(ARCameraFrameEventArgs e)
        {
            if (e.displayMatrix.HasValue) { display = e.displayMatrix.Value; hasDisplay = true; }
            if (!configured && ARSession.state >= ARSessionState.SessionInitializing) { configured = true; ChooseConfiguration(); }
        }

        /// <summary>The camera configuration with the largest CPU image whose width is at most maxImageWidth.</summary>
        void ChooseConfiguration()
        {
            try
            {
                using (var configs = cameraManager.GetConfigurations(Allocator.Temp))
                {
                    XRCameraConfiguration? best = null;
                    foreach (var c in configs)
                        if (c.width <= owner.arMaxImageWidth && (best == null || c.width * c.height > best.Value.width * best.Value.height))
                            best = c;
                    var now = cameraManager.currentConfiguration;
                    if (best.HasValue && (!now.HasValue || now.Value != best.Value))
                    {
                        cameraManager.currentConfiguration = best;
                        Debug.Log("[ArRig] camera image " + best.Value.width + " x " + best.Value.height);
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[ArRig] camera configuration not changed: " + e.Message); }
        }

        // ================================================================== frames

        /// <summary>The latest CPU image as an RGBA32 frame (bottom-up rows), with its intrinsics and pose.</summary>
        public CameraFrame Latest(long id)
        {
            var f = new CameraFrame { Id = id, Timestamp = Time.unscaledTimeAsDouble, Format = FramePixels.None, Source = "arcore" };
            XRCpuImage image;
            if (cameraManager == null || !cameraManager.TryAcquireLatestCpuImage(out image)) return f;
            try
            {
                var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.MirrorY);
                int size = image.GetConvertedDataSize(conversion);
                var pixels = new byte[size];             // a new array per frame: readers keep frames while they work
                var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try { image.Convert(conversion, pin.AddrOfPinnedObject(), size); }
                finally { pin.Free(); }
                f.Width = image.width; f.Height = image.height; f.Format = FramePixels.Rgba32; f.Pixels = pixels;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ArRig] camera image not converted: " + e.Message);
                return f;
            }
            finally { image.Dispose(); }

            XRCameraIntrinsics k;
            if (cameraManager.TryGetIntrinsics(out k))
                f.Intrinsics = new[] { k.focalLength.x, k.focalLength.y, k.principalPoint.x, k.principalPoint.y };
            var m = cam.transform.localToWorldMatrix;
            f.Pose = new float[16];
            for (int i = 0; i < 16; i++) f.Pose[i] = m[i];          // Matrix4x4[i] is column-major
            Remember(id, new FrameView { CameraToWorld = cam.cameraToWorldMatrix, Projection = cam.projectionMatrix, Display = display, HasDisplay = hasDisplay });
            return f;
        }

        void Remember(long id, FrameView v)
        {
            frames[id] = v;
            frameOrder.Enqueue(id);
            while (frameOrder.Count > KeepFrames)
            {
                long old = frameOrder.Dequeue();
                frames.Remove(old);
                frameDepth.Remove(old);
            }
        }

        // ================================================================== anchors

        public void Anchor(string key, ImagePoint where, CameraFrame frame)
        {
            FrameView v;
            if (!where.Valid || frame == null || !frames.TryGetValue(frame.Id, out v) || !v.HasDisplay) return;
            var a = new Anchored { Ray = RayOf(where, v), Frame = frame.Id, Since = Time.unscaledTime };
            Vector3 hit;
            if (Cast(a.Ray, out hit))
            {
                a.Point = hit;
                frameDepth[frame.Id] = Vector3.Distance(a.Ray.origin, hit);
            }
            else
            {
                float d;
                a.Estimated = true;
                a.Point = a.Ray.GetPoint(frameDepth.TryGetValue(frame.Id, out d) ? d : DefaultDepth);
            }
            anchors[key] = a;
            if (!a.Estimated) Refit(frame.Id);
        }

        public void Clear() { anchors.Clear(); }

        public bool TryGetPosition(string key, out Vector2 position)
        {
            position = Vector2.zero;
            Anchored a;
            if (!anchors.TryGetValue(key, out a)) return false;
            var vp = cam.WorldToViewportPoint(a.Point);
            if (vp.z <= 0) return false;                          // behind the camera
            position = new Vector2(vp.x, 1f - vp.y);              // marker layer: y down
            return position.x >= 0 && position.x <= 1 && position.y >= 0 && position.y <= 1;
        }

        /// <summary>Called every frame: anchors still without a surface try the raycast again.</summary>
        public void Tick()
        {
            float now = Time.unscaledTime;
            List<long> found = null;
            foreach (var a in anchors.Values)
            {
                if (!a.Estimated || now - a.Since > RetrySeconds) continue;
                Vector3 hit;
                if (!Cast(a.Ray, out hit)) continue;
                a.Point = hit; a.Estimated = false;
                frameDepth[a.Frame] = Vector3.Distance(a.Ray.origin, hit);
                (found ?? (found = new List<long>())).Add(a.Frame);
            }
            if (found != null) foreach (var f in found) Refit(f);
        }

        /// <summary>Estimated anchors of a frame move to the depth that frame's pins were found at.</summary>
        void Refit(long frame)
        {
            float d;
            if (!frameDepth.TryGetValue(frame, out d)) return;
            foreach (var a in anchors.Values)
                if (a.Estimated && a.Frame == frame) a.Point = a.Ray.GetPoint(d);
        }

        bool Cast(Ray ray, out Vector3 point)
        {
            point = Vector3.zero;
            if (raycasts == null || !raycasts.Raycast(ray, hits, Surfaces) || hits.Count == 0) return false;
            var best = hits[0];
            foreach (var h in hits)                               // a plane is steadier than a single feature point
                if ((h.hitType & TrackableType.Planes) != 0) { best = h; break; }
            point = best.pose.position;
            return true;
        }

        /// <summary>
        /// The ray through an image point of a frame. Image (x right, y down, 0..1) = texture coordinates of the camera
        /// feed; the display matrix maps the screen's viewport point to them, texture = (vx, vy, 1) * M as in the
        /// ARCore background shader, so the viewport point is that relation solved backwards.
        /// </summary>
        Ray RayOf(ImagePoint p, FrameView v)
        {
            // the texture's v runs bottom-up while image y runs top-down: 1 - y (bench, 1 Nov: without it the marks
            // were mirrored across the portrait screen); the two flags undo either axis if a phone differs
            float tx = owner.arFlipImageX ? 1f - p.X : p.X;
            float ty = owner.arFlipImageY ? p.Y : 1f - p.Y;
            var m = v.Display;
            float a = m.m00, b = m.m10, c = m.m01, d = m.m11;     // tx = a vx + b vy + m20, ty = c vx + d vy + m21
            float det = a * d - b * c;
            if (Mathf.Abs(det) < 1e-6f) { a = 1; b = 0; c = 0; d = 1; det = 1; }
            float rx = tx - m.m20, ry = ty - m.m21;
            float vx = (d * rx - b * ry) / det, vy = (-c * rx + a * ry) / det;
            Vector4 q = v.Projection.inverse * new Vector4(2f * vx - 1f, 2f * vy - 1f, 1f, 1f);
            var inCamera = new Vector3(q.x, q.y, q.z) / q.w;      // a point on the far plane, camera space
            var origin = v.CameraToWorld.MultiplyPoint(Vector3.zero);
            return new Ray(origin, v.CameraToWorld.MultiplyVector(inCamera).normalized);
        }
    }
}
