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
    /// Frames   the latest CPU image converted to RGBA32 in Unity's bottom-up order (MirrorX), the camera
    ///          configuration with the largest image up to maxImageWidth (the service works at 1920), auto-focus on.
    ///          For each frame handed out, the camera pose, projection and display matrix of that moment are kept,
    ///          because a reading comes back 1 to 3 s later, when the phone has moved.
    /// Anchor   image point -> inverse of the background shader's display transform -> viewport -> inverse projection
    ///          from the camera pose of that frame -> raycast against planes and
    ///          feature points -> a world point. Without a hit yet, the point is put on the ray at the depth of
    ///          another pin of the same frame (or 30 cm) and the raycast is tried again every frame for 10 s, as
    ///          ARCore finds the surface.
    /// Position every frame the world point projected with the camera of now: marks stay on the pins while the
    ///          phone moves.
    /// The display matrix includes the sensor-to-display rotation, crop and texture y flip. Sensor intrinsics alone
    /// do not: applying them to the portrait camera pose turned the reader and controller marks by 90 degrees.
    /// MirrorX reverses rows for Unity's bottom-up texture storage; MirrorY reverses columns and is not that conversion.
    /// </summary>
    public sealed class ArRig
    {
        const float DefaultDepth = 0.30f;            // metres: about where the installer holds the phone
        const float RetrySeconds = 10f;
        const int KeepFrames = 40;
        const TrackableType Surfaces = TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated | TrackableType.FeaturePoint;

        struct FrameView
        {
            public Matrix4x4 CameraToWorld, Projection, Display;
            public bool HasDisplay;
            public int W, H;
        }

        sealed class Anchored
        {
            public Vector3 Point;
            public Ray Ray;
            public long Frame;
            public bool Estimated;
            public float Since;
            public FrameView View;
            public string Key;
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
        long loggedAnchorFrame = -1;
        Matrix4x4 projection;

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
            if (e.projectionMatrix.HasValue) projection = e.projectionMatrix.Value;
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
                // MirrorX reverses rows. The resulting Texture2D/JPEG has the native CPU image's orientation;
                // MirrorY flips columns instead and would rotate the JPEG 180 degrees after bottom-up decoding.
                var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32, XRCpuImage.Transformation.MirrorX);
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
            {
                // the intrinsics belong to the camera image they were measured on; ARCore may report them for the
                // full resolution while the CPU image we hand out is smaller, so scale them to that image
                float sx = k.resolution.x > 0 ? f.Width / (float)k.resolution.x : 1f;
                float sy = k.resolution.y > 0 ? f.Height / (float)k.resolution.y : 1f;
                f.Intrinsics = new[]
                {
                    k.focalLength.x * sx, k.focalLength.y * sy,
                    k.principalPoint.x * sx, k.principalPoint.y * sy,
                };
            }
            var m = cam.transform.localToWorldMatrix;
            f.Pose = new float[16];
            for (int i = 0; i < 16; i++) f.Pose[i] = m[i];          // Matrix4x4[i] is column-major
            Remember(id, new FrameView { CameraToWorld = cam.cameraToWorldMatrix, Projection = projection, Display = display,
                                         HasDisplay = hasDisplay, W = f.Width, H = f.Height });
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
            Ray ray;
            if (!TryRay(where, v, out ray)) { anchors.Remove(key); return; }
            var a = new Anchored { Ray = ray, Frame = frame.Id, Since = Time.unscaledTime, View = v, Key = key };
            anchors[key] = a;
            if (!DepthFromSpacing(frame.Id)) Place(a);      // a single point (the label): raycast, or 30 cm
            else Refit(frame.Id);
            if (Debug.isDebugBuild && loggedAnchorFrame != frame.Id)
            {
                loggedAnchorFrame = frame.Id;
                Debug.Log("[ArRig] anchor frame=" + frame.Id + " image=" + v.W + "x" + v.H +
                    " rows=MirrorX display=" + v.Display.ToString("F5") + " projection=" + v.Projection.ToString("F5"));
            }
        }

        /// <summary>
        /// The depth of a frame's pins from how far apart their rays lie on the unit camera plane:
        /// depth = pin spacing (m) / ray spacing. The spacing is known for each device (5.0 mm on the A1610 /
        /// A1601 blocks, 5.8 mm on the A4020-E; measured on session-1 photos against the 21.5 mm QR labels), so no
        /// surface has to be found first: on 1 Nov the raycasts missed the board and marks were left over the floor.
        /// </summary>
        bool DepthFromSpacing(long frame)
        {
            var pts = new List<Anchored>();
            foreach (var a in anchors.Values) if (a.Frame == frame && !a.Key.StartsWith("module:") && a.Key != "label") pts.Add(a);
            if (pts.Count < 2) return false;
            var v = pts[0].View;
            var gaps = new List<float>();
            Vector3 forward = -((Vector3)v.CameraToWorld.GetColumn(2)).normalized;
            Vector3 right = v.CameraToWorld.GetColumn(0), up = v.CameraToWorld.GetColumn(1);
            foreach (var a in pts)
            {
                float best = float.MaxValue;
                foreach (var b in pts)
                {
                    if (a == b) continue;
                    // Compare rays in camera-plane units. This already includes rotation/crop and both focal axes.
                    Vector3 ra = a.Ray.direction / Vector3.Dot(a.Ray.direction, forward);
                    Vector3 rb = b.Ray.direction / Vector3.Dot(b.Ray.direction, forward);
                    Vector3 delta = ra - rb;
                    float d = new Vector2(Vector3.Dot(delta, right), Vector3.Dot(delta, up)).magnitude;
                    if (d > 1e-6f && d < best) best = d;
                }
                if (best < float.MaxValue) gaps.Add(best);
            }
            if (gaps.Count == 0) return false;
            gaps.Sort();
            float spacing = gaps[gaps.Count / 2];
            float pitch = (pts[0].Key.StartsWith("pin:") ? owner.arControllerPinSpacingMm : owner.arDevicePinSpacingMm) / 1000f;
            float z = pitch / spacing;                           // along the camera's forward axis
            foreach (var a in anchors.Values)
            {
                if (a.Frame != frame) continue;
                float cos = Mathf.Max(0.2f, Vector3.Dot(a.Ray.direction, forward));
                a.Point = a.Ray.GetPoint(z / cos);
                a.Estimated = false;
            }
            frameDepth[frame] = z;
            return true;
        }

        /// <summary>Raycast the anchor's ray; without a surface yet, put it at the frame's depth (or 30 cm).
        /// Only for single points (the label): pins get their depth from their spacing, never from a plane, which may
        /// be the table the controller stands on.</summary>
        void Place(Anchored a)
        {
            Vector3 hit;
            if (Cast(a.Ray, out hit))
            {
                a.Point = hit;
                a.Estimated = false;
                Vector3 fw = -((Vector3)a.View.CameraToWorld.GetColumn(2)).normalized;
                frameDepth[a.Frame] = Vector3.Dot(hit - a.Ray.origin, fw);
            }
            else
            {
                float d;
                a.Estimated = true;
                a.Since = Time.unscaledTime;
                Vector3 fw = -((Vector3)a.View.CameraToWorld.GetColumn(2)).normalized;
                a.Point = a.Ray.GetPoint((frameDepth.TryGetValue(a.Frame, out d) ? d : DefaultDepth) / Mathf.Max(0.2f, Vector3.Dot(a.Ray.direction, fw)));
            }
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
                frameDepth[a.Frame] = Vector3.Dot(hit - a.Ray.origin, -((Vector3)a.View.CameraToWorld.GetColumn(2)).normalized);
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
            {
                if (!a.Estimated || a.Frame != frame) continue;
                Vector3 forward = -((Vector3)a.View.CameraToWorld.GetColumn(2)).normalized;
                a.Point = a.Ray.GetPoint(d / Mathf.Max(0.2f, Vector3.Dot(a.Ray.direction, forward)));
            }
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
        /// Use the display and projection matrices saved with the read frame, not the phone's pose after the
        /// service returns. Invalid transforms hide the mark rather than guessing an identity orientation.
        /// </summary>
        bool TryRay(ImagePoint p, FrameView v, out Ray ray)
        {
            ray = default;
            Vector3 direction;
            Vector2 image = new Vector2(owner.arFlipImageX ? 1f - p.X : p.X, owner.arFlipImageY ? 1f - p.Y : p.Y);
            if (!CameraImageGeometry.TryCameraRay(image, v.Display, v.Projection, out direction)) return false;
            ray = new Ray(v.CameraToWorld.MultiplyPoint(Vector3.zero), v.CameraToWorld.MultiplyVector(direction).normalized);
            return true;
        }

    }
}
