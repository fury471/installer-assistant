using System;
using System.Collections.Generic;
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
    /// pose), ARRaycastManager, ARPlaneManager, ARPointCloudManager (surfaces for the label) and ARAnchorManager.
    ///
    /// Frames   the latest CPU image converted to RGBA32 in Unity's bottom-up order (MirrorX), the camera
    ///          configuration with the largest image up to maxImageWidth (the service works at 1920), auto-focus on.
    ///          For each frame handed out, the camera pose, projection, display matrix and intrinsics of that moment
    ///          are kept, because a reading comes back 1 to 3 s later, when the phone has moved.
    /// Ray      image point -> camera ray from the CPU image's intrinsics, with only the quarter-turn taken from the
    ///          display transform (CameraImageGeometry.TryIntrinsicsRay). The display transform maps the screen to
    ///          the GPU texture, whose field of view may differ from the CPU image's; the old display-only ray is
    ///          the fallback and the difference between the two is recorded.
    /// Depth    a read of a block gives one depth from its pin spacing (5.0 mm A1610, 5.8 mm A4020-E). That depth
    ///          assumes the block faces the camera: 30 degrees of tilt puts it 15 % too far, and the marks slide off
    ///          the pins as soon as the phone moves. So each read is one sighting per pin, and every pin is the
    ///          least-squares point of all its sightings (AnchorFusion): rays tight across, spacing depth loose
    ///          along. Two reads a few centimetres apart triangulate the depth; the app re-reads in the background
    ///          while marks are shown to get them. A read that does not meet the earlier rays (epipolar test) is
    ///          not fused, so a bad read cannot pull the marks off; a new explicit read starts from ClearAnchors.
    /// Anchor   all marks of an end live under one transform held by an ARCore anchor, so ARCore's map
    ///          corrections carry them (ARCore: "Working with anchors").
    /// Position every frame the fused point projected with the camera of now.
    /// Records  ArRecords: one JSON line per read and a 1 Hz sample of where the marks were drawn, plus
    ///          screenshots on a three-finger tap. ml/tools/ar_records.py measures them.
    /// The display matrix includes the sensor-to-display rotation, crop and texture y flip. MirrorX reverses rows
    /// for Unity's bottom-up texture storage; MirrorY reverses columns and is not that conversion.
    /// </summary>
    public sealed class ArRig
    {
        const float DefaultDepth = 0.30f;            // metres: about where the installer holds the phone
        const float RetrySeconds = 10f;
        const int KeepFrames = 40;
        const TrackableType Surfaces = TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated | TrackableType.FeaturePoint;
        const float ReaderErrorPitches = 0.25f;      // how far a reader puts a hole off its true place (bench: median 0.05, worst 0.28)
        const float SpacingDepthError = 0.25f;       // relative: the spacing depth is long by 1/cos(tilt) - 1 (35 degrees: 22 %)
        const int MaxSightings = 8;                  // per pin: the newest views; older ones only slow the fusion
        const float GatePitches = 0.4f;              // a sighting this far off the other rays is a different place, not noise
        const float RangeSigmas = 3f;
        const float GrossRayDisagreement = 0.25f;    // of the image size: the intrinsics ray is wrong, not a crop difference

        struct FrameView
        {
            public Matrix4x4 CameraToWorld, Projection, Display;
            public bool HasDisplay;
            public int W, H;
            public float[] Intrinsics;
        }

        sealed class Anchored
        {
            public string Key;
            public bool IsPin;
            public readonly List<RaySighting> Sightings = new List<RaySighting>();   // in the marks' anchor frame
            public Vector3 Point;                     // in the marks' anchor frame
            public bool Estimated;                    // no depth yet: on the ray at a guessed range, raycast retried
            public float Since;
        }

        struct Pending
        {
            public string Key;
            public ImagePoint Where;
            public Vector3 ViewDir;                   // camera space, -z forward, z = -1
            public Ray World;
            public float RayMethodPx;                 // intrinsics vs display ray, in image pixels (-1 = not both)
        }

        readonly ArCameraView owner;
        readonly Camera cam;
        readonly ARCameraManager cameraManager;
        readonly ARRaycastManager raycasts;
        readonly ARAnchorManager anchorManager;
        readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        readonly Dictionary<long, FrameView> frames = new Dictionary<long, FrameView>();
        readonly Queue<long> frameOrder = new Queue<long>();
        readonly Dictionary<string, Anchored> anchors = new Dictionary<string, Anchored>();
        readonly SortedDictionary<long, Dictionary<string, Pending>> pending = new SortedDictionary<long, Dictionary<string, Pending>>();
        Dictionary<string, RaySighting> lastRejected = new Dictionary<string, RaySighting>();
        Matrix4x4 display = Matrix4x4.identity;
        bool hasDisplay, configured, loggedTexture;
        Matrix4x4 projection;
        Transform root;                               // the marks' frame; parented to an ARAnchor once ARCore gives one
        ARAnchor arAnchor;
        int rootGeneration;
        ArRecords records;
        float nextSample;
        int touchesBefore;

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
            if (owner.arHoldWithArAnchor) anchorManager = originGo.AddComponent<ARAnchorManager>();
            originGo.SetActive(true);

            cameraManager.frameReceived += OnFrame;
            if (owner.arRecords) records = ArRecords.Open();
            records?.Begin("start").Put("device", SystemInfo.deviceModel).Put("screen", Screen.width + "x" + Screen.height)
                .Put("intrinsicsRays", owner.arIntrinsicsRays).Put("fuseReads", owner.arFuseReads)
                .Put("controllerPitchMm", owner.arControllerPinSpacingMm).Put("devicePitchMm", owner.arDevicePinSpacingMm).End();
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
            if (!loggedTexture && e.textures != null && e.textures.Count > 0 && e.textures[0] != null && records != null)
            {
                // the GPU texture the background shows; its aspect against the CPU image tells whether they share a view
                loggedTexture = true;
                var c = cameraManager.currentConfiguration;
                records.Begin("camera").Put("texture", e.textures[0].width + "x" + e.textures[0].height)
                    .Put("cpuImage", c.HasValue ? c.Value.width + "x" + c.Value.height : "").Put("display", display).End();
            }
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
                        loggedTexture = false;
                        Debug.Log("[ArRig] camera image " + best.Value.width + " x " + best.Value.height);
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[ArRig] camera configuration not changed: " + e.Message); }
        }

        // ================================================================== frames

        XRCpuImage.AsyncConversion? imageConversion;
        double conversionStarted;
        CameraFrame convertingFrame, readyFrame;
        FrameView convertingView, readyView;

        // Latest starts at most one conversion and never waits for it on the rendering thread.
        // Every completed image keeps the pose/display/intrinsics from its acquisition, not its completion.
        public CameraFrame Latest(long id)
        {
            PollImage();
            if (!imageConversion.HasValue && cameraManager != null && cameraManager.TryAcquireLatestCpuImage(out var image))
            {
                try
                {
                    var f = new CameraFrame { Timestamp = Time.unscaledTimeAsDouble, Width = image.width,
                        Height = image.height, Source = "arcore", Format = FramePixels.Rgba32 };
                    if (cameraManager.TryGetIntrinsics(out var k))
                    {
                        float sx = k.resolution.x > 0 ? f.Width / (float)k.resolution.x : 1f;
                        float sy = k.resolution.y > 0 ? f.Height / (float)k.resolution.y : 1f;
                        f.Intrinsics = new[] { k.focalLength.x * sx, k.focalLength.y * sy,
                            k.principalPoint.x * sx, k.principalPoint.y * sy };
                    }
                    var m = cam.transform.localToWorldMatrix;
                    f.Pose = new float[16];
                    for (int i = 0; i < 16; i++) f.Pose[i] = m[i];
                    convertingFrame = f;
                    convertingView = new FrameView { CameraToWorld = cam.cameraToWorldMatrix, Projection = projection,
                        Display = display, HasDisplay = hasDisplay, W = f.Width, H = f.Height, Intrinsics = f.Intrinsics };
                    imageConversion = image.ConvertAsync(new XRCpuImage.ConversionParams(image,
                        TextureFormat.RGBA32, XRCpuImage.Transformation.MirrorX));
                    conversionStarted = Time.unscaledTimeAsDouble;
                }
                catch (Exception e) { Debug.LogWarning("[ArRig] camera conversion failed: " + e.Message); }
                finally { image.Dispose(); }
            }
            if (readyFrame == null || Time.unscaledTimeAsDouble - readyFrame.Timestamp > 1)
                return new CameraFrame { Id = id, Format = FramePixels.None, Source = "arcore" };
            var result = new CameraFrame { Id = id, Timestamp = readyFrame.Timestamp, Width = readyFrame.Width,
                Height = readyFrame.Height, Pixels = readyFrame.Pixels, Format = readyFrame.Format,
                Intrinsics = readyFrame.Intrinsics, Pose = readyFrame.Pose, Source = readyFrame.Source };
            Remember(id, readyView);
            return result;
        }

        void PollImage()
        {
            if (!imageConversion.HasValue) return;
            var request = imageConversion.Value;
            if (request.status == XRCpuImage.AsyncConversionStatus.Processing &&
                Time.unscaledTimeAsDouble - conversionStarted < 2) return;
            try
            {
                if (request.status == XRCpuImage.AsyncConversionStatus.Ready)
                {
                    convertingFrame.Pixels = request.GetData<byte>().ToArray();
                    readyFrame = convertingFrame;
                    readyView = convertingView;
                }
            }
            finally { request.Dispose(); imageConversion = null; convertingFrame = null; }
        }

        public void Dispose()
        {
            if (imageConversion.HasValue) imageConversion.Value.Dispose();
            imageConversion = null;
            if (cameraManager != null) cameraManager.frameReceived -= OnFrame;
            Clear();
            records?.Close();
        }

        void Remember(long id, FrameView v)
        {
            frames[id] = v;
            frameOrder.Enqueue(id);
            while (frameOrder.Count > KeepFrames) frames.Remove(frameOrder.Dequeue());
        }

        // ================================================================== anchors

        /// <summary>
        /// Notes where a reader found a terminal in a frame. The sightings of one frame are used together (the pin
        /// spacing needs all of them), on the next Tick or TryGetPosition.
        /// </summary>
        public void Anchor(string key, ImagePoint where, CameraFrame frame)
        {
            FrameView v;
            if (!where.Valid || frame == null || !frames.TryGetValue(frame.Id, out v) || !v.HasDisplay) return;
            Pending p;
            // A transient invalid camera transform must not erase an existing world anchor.
            // Explicit reads already clear their old anchors before placing a replacement set.
            if (!TryRay(where, v, out p)) return;
            p.Key = key;
            Dictionary<string, Pending> batch;
            if (!pending.TryGetValue(frame.Id, out batch)) pending[frame.Id] = batch = new Dictionary<string, Pending>();
            batch[key] = p;
        }

        static bool IsPinKey(string key) { return key.StartsWith("pin:") || key.StartsWith("terminal:"); }

        float PitchOf(string key) { return (key.StartsWith("pin:") ? owner.arControllerPinSpacingMm : owner.arDevicePinSpacingMm) / 1000f; }

        void Flush()
        {
            if (pending.Count == 0) return;
            var ids = new List<long>(pending.Keys);
            foreach (long id in ids)
            {
                var batch = pending[id];
                pending.Remove(id);
                FrameView v;
                if (frames.TryGetValue(id, out v)) Commit(id, v, batch);
            }
        }

        /// <summary>
        /// The pins' spacing in the frame, on the camera's unit plane (tangent units), as the median gap along the
        /// row (AnchorFusion.RowSpacing). Depth along the camera axis = pitch / spacing.
        /// </summary>
        static bool Spacing(List<Pending> pins, out float spacing)
        {
            var pts = new List<Vector2>(pins.Count);
            foreach (var p in pins) pts.Add(new Vector2(p.ViewDir.x, p.ViewDir.y));
            return AnchorFusion.RowSpacing(pts, out spacing);
        }

        void Commit(long frameId, FrameView v, Dictionary<string, Pending> batch)
        {
            var pins = new List<Pending>();
            var others = new List<Pending>();
            foreach (var p in batch.Values) (IsPinKey(p.Key) ? pins : others).Add(p);

            float spacing;
            bool hasDepth = Spacing(pins, out spacing);
            float pitch = pins.Count > 0 ? PitchOf(pins[0].Key) : PitchOf("pin:");
            float z = hasDepth ? pitch / spacing : 0f;           // along the camera's forward axis
            var worldToCamera = v.CameraToWorld.inverse;
            // no spacing in this frame (the zone hints, a block with one pin found): the pins already anchored are
            // on the same board, so their depth beats a raycast, which finds the table the controller lies on
            float boardZ;
            bool hasBoard = BoardDepth(worldToCamera, out boardZ);

            if (root == null) MakeRoot(batch, hasDepth, z, v);
            var toLocal = root.worldToLocalMatrix;

            var rec = records?.Begin("read").Put("frame", frameId).Put("w", (long)v.W).Put("h", (long)v.H)
                .Put("intrinsics", v.Intrinsics).Put("display", v.Display).Put("projection", v.Projection)
                .Put("cameraToWorld", v.CameraToWorld).Put("pitchMm", pitch * 1000f).Put("spacingTan", spacing)
                .Put("depth", z).Put("rootToWorld", root.localToWorldMatrix);

            // 1. does this read agree with what the marks already show? (epipolar test per pin, majority per read)
            int compared = 0, agree = 0;
            var offsets = new List<float>();
            var verdicts = new Dictionary<string, bool>();
            var drawnAt = new Dictionary<string, Vector2>();     // where the mark was, in this frame's image
            foreach (var p in pins)
            {
                Anchored a;
                if (!anchors.TryGetValue(p.Key, out a) || a.Sightings.Count == 0) continue;
                compared++;
                Vector2 seen;
                if (ImageOf(worldToCamera.MultiplyPoint(root.TransformPoint(a.Point)), v, out seen)) drawnAt[p.Key] = seen;
                // where the mark is drawn in this frame against where the reader now finds the pin, in pitches
                if (hasDepth)
                {
                    Vector3 c = worldToCamera.MultiplyPoint(root.TransformPoint(a.Point));
                    if (c.z < -1e-4f) offsets.Add(new Vector2(c.x / -c.z - p.ViewDir.x, c.y / -c.z - p.ViewDir.y).magnitude / spacing);
                }
                float worst; Vector3 fused;
                bool ok = AnchorFusion.Agrees(a.Sightings, Sighting(p, toLocal, hasDepth, z, pitch, frameId),
                    GatePitches * pitch, RangeSigmas, out worst, out fused);
                verdicts[p.Key] = ok;
                if (ok) agree++;
            }
            bool accept = compared == 0 || agree * 2 >= compared;
            string outcome = compared == 0 ? "first" : !owner.arFuseReads ? "replaced" : accept ? "fused" : "rejected";

            // two reads in a row that disagree with the marks but agree with each other: the marks were wrong
            // (the first read found the wrong place), so they move to those two reads. One stray read (a hand in
            // front of the block) never moves them.
            var mine = new Dictionary<string, RaySighting>();
            foreach (var p in pins) mine[p.Key] = Sighting(p, toLocal, hasDepth, z, pitch, frameId);
            bool relocate = false;
            if (outcome == "rejected")
            {
                int both = 0, same = 0;
                foreach (var kv in mine)
                {
                    RaySighting before;
                    if (!lastRejected.TryGetValue(kv.Key, out before)) continue;
                    both++;
                    float worst; Vector3 fused;
                    if (AnchorFusion.Agrees(new List<RaySighting> { before }, kv.Value, GatePitches * pitch, RangeSigmas, out worst, out fused)) same++;
                }
                relocate = both >= 2 && same * 2 > both;
                if (relocate) outcome = "relocated";
            }
            var previousRejected = lastRejected;
            lastRejected = outcome == "rejected" ? mine : new Dictionary<string, RaySighting>();

            // 2. take the sightings
            rec?.Array("points");
            foreach (var p in pins)
            {
                Anchored a;
                bool existed = anchors.TryGetValue(p.Key, out a) && a.Sightings.Count > 0;
                bool ok;
                if (!verdicts.TryGetValue(p.Key, out ok)) ok = true;
                bool take = !existed || !owner.arFuseReads || (accept && ok) || relocate;
                if (take)
                {
                    if (a == null) anchors[p.Key] = a = new Anchored { Key = p.Key, IsPin = true };
                    if (!owner.arFuseReads || relocate) a.Sightings.Clear();
                    RaySighting before;
                    if (relocate && previousRejected.TryGetValue(p.Key, out before)) a.Sightings.Add(before);
                    a.Sightings.Add(mine[p.Key]);
                    if (a.Sightings.Count > MaxSightings) a.Sightings.RemoveAt(0);
                    if (!hasDepth && a.Sightings.Count == 1)
                    {
                        if (hasBoard) { a.Sightings[0] = Sighting(p, toLocal, true, boardZ, pitch, frameId); a.Point = a.Sightings[0].PriorPoint; a.Estimated = false; }
                        else PlaceAlone(a, p, toLocal, v);
                    }
                    else { a.Point = AnchorFusion.FuseRobust(a.Sightings, GatePitches * pitch); a.Estimated = false; }
                }
                if (rec != null)
                {
                    rec.Item().Put("key", p.Key).Put("u", p.Where.X).Put("v", p.Where.Y).Put("taken", take).Put("agrees", ok)
                        .Put("rayMethodPx", p.RayMethodPx);
                    Vector2 was;
                    if (drawnAt.TryGetValue(p.Key, out was)) rec.Put("markU", was.x).Put("markV", was.y);
                    if (a != null) rec.Put("point", a.Point).Put("sightings", (long)a.Sightings.Count).Put("baselineMm", AnchorFusion.Baseline(a.Sightings) * 1000f);
                }
            }
            rec?.EndArray();

            // 3. the label and the modules: one sighting each, at the pins' depth when this frame has pins
            foreach (var p in others)
            {
                var a = new Anchored { Key = p.Key, IsPin = false };
                anchors[p.Key] = a;
                if (hasDepth || hasBoard)
                {
                    a.Sightings.Add(Sighting(p, toLocal, true, hasDepth ? z : boardZ, pitch, frameId));
                    a.Point = a.Sightings[0].PriorPoint;
                }
                else PlaceAlone(a, p, toLocal, v);
            }

            offsets.Sort();
            rec?.Put("compared", (long)compared).Put("agree", (long)agree).Put("outcome", outcome)
                .Put("offsetMedianPitch", offsets.Count > 0 ? offsets[offsets.Count / 2] : -1f)
                .Put("offsetMaxPitch", offsets.Count > 0 ? offsets[offsets.Count - 1] : -1f).End();
            if (Debug.isDebugBuild)
                Debug.Log("[ArRig] read frame=" + frameId + " pins=" + pins.Count + " depth=" + z.ToString("F3") + " " + outcome +
                    " agree=" + agree + "/" + compared + (offsets.Count > 0 ? " offset=" + offsets[offsets.Count / 2].ToString("F2") + " pitch" : ""));
        }

        /// <summary>Where a camera-space point is in a frame's CPU image (normalised, top-left): through the
        /// intrinsics when the frame has them, else through the projection and the background's display transform.</summary>
        bool ImageOf(Vector3 cameraPoint, FrameView v, out Vector2 image)
        {
            image = Vector2.zero;
            if (cameraPoint.z > -1e-4f) return false;
            Vector2 raw;
            if (owner.arIntrinsicsRays && CameraImageGeometry.TryIntrinsicsImage(cameraPoint, v.W, v.H, v.Intrinsics, v.Display, out raw)) { }
            else
            {
                Vector4 clip = v.Projection * new Vector4(cameraPoint.x, cameraPoint.y, cameraPoint.z, 1f);
                if (Mathf.Abs(clip.w) < 1e-8f) return false;
                float vx = (clip.x / clip.w + 1f) / 2f, vy = (clip.y / clip.w + 1f) / 2f;
                raw = new Vector2(vx * v.Display.m00 + vy * v.Display.m10 + v.Display.m20, vx * v.Display.m01 + vy * v.Display.m11 + v.Display.m21);
            }
            image = new Vector2(owner.arFlipImageX ? 1f - raw.x : raw.x, owner.arFlipImageY ? 1f - raw.y : raw.y);
            return true;
        }

        /// <summary>The median depth (along the camera axis of a frame) of the pins already anchored, if any.</summary>
        bool BoardDepth(Matrix4x4 worldToCamera, out float z)
        {
            z = 0;
            if (root == null) return false;
            var zs = new List<float>();
            foreach (var a in anchors.Values)
            {
                if (!a.IsPin || a.Estimated || a.Sightings.Count == 0) continue;
                float d = -worldToCamera.MultiplyPoint(root.TransformPoint(a.Point)).z;
                if (d > 0.05f) zs.Add(d);
            }
            if (zs.Count == 0) return false;
            zs.Sort();
            z = zs[zs.Count / 2];
            return true;
        }

        RaySighting Sighting(Pending p, Matrix4x4 toLocal, bool hasDepth, float z, float pitch, long frame)
        {
            float range = hasDepth ? z * p.ViewDir.magnitude : 0f;   // ViewDir has z = -1, so this is z / cos
            return new RaySighting
            {
                Origin = toLocal.MultiplyPoint(p.World.origin),
                Direction = toLocal.MultiplyVector(p.World.direction).normalized,
                HasRange = hasDepth,
                Range = range,
                SigmaLateral = ReaderErrorPitches * pitch,
                SigmaRange = Mathf.Max(0.005f, SpacingDepthError * range),
                Frame = frame,
            };
        }

        /// <summary>A point with no pin spacing (the label, or a block with one pin found): raycast, else 30 cm.</summary>
        void PlaceAlone(Anchored a, Pending p, Matrix4x4 toLocal, FrameView v)
        {
            a.Since = Time.unscaledTime;
            Vector3 hit;
            if (Cast(p.World, out hit))
            {
                a.Point = toLocal.MultiplyPoint(hit);
                a.Estimated = false;
                return;
            }
            a.Estimated = true;
            a.Point = toLocal.MultiplyPoint(p.World.GetPoint(DefaultDepth * p.ViewDir.magnitude));
            if (a.Sightings.Count == 0)
                a.Sightings.Add(new RaySighting { Origin = toLocal.MultiplyPoint(p.World.origin), Direction = toLocal.MultiplyVector(p.World.direction).normalized,
                    SigmaLateral = 0.001f, Frame = -1 });
        }

        /// <summary>The marks' frame: at the block (or the first point), oriented like the camera of that read.</summary>
        void MakeRoot(Dictionary<string, Pending> batch, bool hasDepth, float z, FrameView v)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var p in batch.Values)
            {
                sum += p.World.GetPoint((hasDepth ? z : DefaultDepth) * p.ViewDir.magnitude);
                n++;
            }
            // cameraToWorldMatrix flips z (OpenGL camera space), so build the rotation from its axes
            var facing = Quaternion.LookRotation(-(Vector3)v.CameraToWorld.GetColumn(2), v.CameraToWorld.GetColumn(1));
            var pose = new Pose(n > 0 ? sum / n : Vector3.zero, facing);
            var go = new GameObject("Installer Assistant marks");
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            root = go.transform;
            rootGeneration++;
            if (anchorManager != null && ARSession.state == ARSessionState.SessionTracking) HoldWithArAnchor(root, pose, rootGeneration);
        }

        /// <summary>Parents the marks' frame to an ARCore anchor, so map corrections move the marks with the board.</summary>
        async void HoldWithArAnchor(Transform target, Pose pose, int generation)
        {
            try
            {
                var result = await anchorManager.TryAddAnchorAsync(pose);
                if (!result.status.IsSuccess() || result.value == null)
                {
                    records?.Begin("anchor").Put("ok", false).Put("status", result.status.ToString()).End();
                    return;
                }
                if (target == null || generation != rootGeneration)
                {
                    anchorManager.TryRemoveAnchor(result.value);
                    return;
                }
                arAnchor = result.value;
                target.SetParent(arAnchor.transform, true);
                records?.Begin("anchor").Put("ok", true).End();
            }
            catch (Exception e) { Debug.LogWarning("[ArRig] no ARCore anchor, marks stay in world space: " + e.Message); }
        }

        void Drop(string key)
        {
            anchors.Remove(key);
            foreach (var batch in pending.Values) batch.Remove(key);
        }

        public void Clear()
        {
            anchors.Clear();
            pending.Clear();
            lastRejected = new Dictionary<string, RaySighting>();
            rootGeneration++;
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null;
            if (arAnchor != null)
            {
                try { anchorManager.TryRemoveAnchor(arAnchor); } catch (Exception) { }
                if (arAnchor != null) UnityEngine.Object.Destroy(arAnchor.gameObject);
            }
            arAnchor = null;
            records?.Begin("clear").End();
        }

        public bool TryGetPosition(string key, out Vector2 position)
        {
            Flush();
            position = Vector2.zero;
            Anchored a;
            if (root == null || !anchors.TryGetValue(key, out a)) return false;
            var vp = cam.WorldToViewportPoint(root.TransformPoint(a.Point));
            if (vp.z <= 0) return false;                          // behind the camera
            position = new Vector2(vp.x, 1f - vp.y);              // marker layer: y down
            return position.x >= 0 && position.x <= 1 && position.y >= 0 && position.y <= 1;
        }

        /// <summary>Called every frame: takes pending sightings, retries raycasts for points without depth,
        /// samples the marks for the records, and saves a screenshot on a three-finger tap.</summary>
        public void Tick()
        {
            PollImage();
            Flush();
            float now = Time.unscaledTime;
            if (root != null)
                foreach (var a in anchors.Values)
                {
                    if (!a.Estimated || a.Sightings.Count == 0 || now - a.Since > RetrySeconds) continue;
                    var s = a.Sightings[0];
                    Vector3 hit;
                    if (!Cast(new Ray(root.TransformPoint(s.Origin), root.TransformDirection(s.Direction)), out hit)) continue;
                    a.Point = root.InverseTransformPoint(hit);
                    a.Estimated = false;
                }
            if (records != null && root != null && anchors.Count > 0 && now >= nextSample)
            {
                nextSample = now + 1f;
                var keys = new List<string>(anchors.Keys);
                var at = new List<Vector2>(keys.Count);
                var on = new List<bool>(keys.Count);
                foreach (var key in keys) { Vector2 p; on.Add(TryGetPosition(key, out p)); at.Add(p); }
                records.Begin("view").Put("cameraToWorld", cam.cameraToWorldMatrix).Put("rootToWorld", root.localToWorldMatrix)
                    .Put("tracking", ARSession.state.ToString()).Array("marks");
                for (int i = 0; i < keys.Count; i++) records.Item().Put("key", keys[i]).Put("on", on[i]).Put("x", at[i].x).Put("y", at[i].y);
                records.EndArray().End();
            }
            CheckScreenshotGesture();
        }

        void CheckScreenshotGesture()
        {
            var screen = Touchscreen.current;
            if (screen == null || records == null) return;
            int down = 0;
            foreach (var t in screen.touches) if (t.press.isPressed) down++;
            if (down >= 3 && touchesBefore < 3)
            {
                string name = "shot-" + DateTime.Now.ToString("HHmmss-fff") + ".png";
                // on the phone a relative name is written under persistentDataPath
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine("ar-records", name));
                records.Begin("shot").Put("file", name).Put("cameraToWorld", cam.cameraToWorldMatrix).End();
                Debug.Log("[ArRig] screenshot ar-records/" + name);
            }
            touchesBefore = down;
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
        /// The camera ray through an image point, with the display, projection, pose and intrinsics saved with the
        /// read frame, not the phone's pose after the service returns. Invalid transforms hide the mark rather
        /// than guessing an orientation.
        /// </summary>
        bool TryRay(ImagePoint where, FrameView v, out Pending p)
        {
            p = default(Pending);
            p.Where = where;
            p.RayMethodPx = -1f;
            Vector2 image = new Vector2(owner.arFlipImageX ? 1f - where.X : where.X, owner.arFlipImageY ? 1f - where.Y : where.Y);
            Vector3 byDisplay, byIntrinsics;
            bool d = CameraImageGeometry.TryCameraRay(image, v.Display, v.Projection, out byDisplay);
            bool k = CameraImageGeometry.TryIntrinsicsRay(image, v.W, v.H, v.Intrinsics, v.Display, out byIntrinsics);
            if (d && k)
            {
                Vector2 back;
                if (CameraImageGeometry.TryIntrinsicsImage(byDisplay, v.W, v.H, v.Intrinsics, v.Display, out back))
                {
                    p.RayMethodPx = new Vector2((back.x - image.x) * v.W, (back.y - image.y) * v.H).magnitude;
                    // a disagreement of a quarter of the image is a sign or axis error, not a crop difference
                    if (Mathf.Abs(back.x - image.x) > GrossRayDisagreement || Mathf.Abs(back.y - image.y) > GrossRayDisagreement) k = false;
                }
            }
            Vector3 dir;
            if (k && owner.arIntrinsicsRays) dir = byIntrinsics;
            else if (d) dir = byDisplay;
            else return false;
            p.ViewDir = dir / -dir.z;
            p.World = new Ray(v.CameraToWorld.MultiplyPoint(Vector3.zero), v.CameraToWorld.MultiplyVector(dir).normalized);
            return true;
        }
    }
}
