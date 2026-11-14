using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using InstallerAssistant.Detection;
using InstallerAssistant.Verification;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace InstallerAssistant.AR.Editor
{
    /// <summary>Batch-mode regression checks of the production anchor path. No scene or contract is changed.
    /// Run with -executeMethod InstallerAssistant.AR.Editor.ArGeometryChecks.Run.</summary>
    public static class ArGeometryChecks
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static int passed;
        static readonly List<string> checks = new List<string>();

        public static void Run()
        {
            try
            {
                CheckArAnchors();
                CheckIntrinsicsRays();
                CheckFusion();
                foreach (string line in PlaneTrackerChecks.Run()) { checks.Add(line); passed++; }   // the webcam view's tracker
                CheckWebcam();
                Vector2 vp;
                Assert(!CameraImageGeometry.TryViewport(Vector2.one, Matrix4x4.zero, out vp), "invalid display hides marks");
                Vector3 ray;
                Assert(!CameraImageGeometry.TryCameraRay(Vector2.one, Matrix4x4.identity, Matrix4x4.zero, out ray), "invalid projection hides marks");
                string folder = OutputFolder();
                Directory.CreateDirectory(folder);
                RenderSavedFrame("20261102-143840-676_device_A4020E_f58", "reader-replay.png");
                RenderSavedFrame("20261102-144620-780_controller_A1610.READER1_f165", "controller-replay.png");
                File.WriteAllText(Path.Combine(folder, "checks.txt"), string.Join("\n", checks) + "\nALL PASSED: " + passed + "\n");
                Debug.Log("[ArGeometryChecks] ALL PASSED: " + passed);
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void CheckArAnchors()
        {
            var go = new GameObject("AR geometry check camera", typeof(Camera), typeof(ArCameraView));
            var camera = go.GetComponent<Camera>();
            camera.pixelRect = new Rect(0, 0, 1080, 2400);
            camera.nearClipPlane = 0.05f; camera.farClipPlane = 20f;
            // Deliberately asymmetric: catches the crop/offset that sensor intrinsics alone miss.
            Matrix4x4 projection = Matrix4x4.Frustum(-0.020f, 0.025f, -0.050f, 0.050f, 0.05f, 20f);
            camera.projectionMatrix = projection;
            camera.transform.SetPositionAndRotation(new Vector3(1, 2, 3), Quaternion.Euler(7, 23, 0));

            // These are the background shader's viewport -> texture fixtures (row-vector, translation in row 2).
            // Expected viewport coordinates below are fixed independently of the inverse implementation.
            Matrix4x4[] displays = {
                Display(1, 0, 0, -1, 0, 1),
                Display(0, 1, 1, 0, 0, 0),
                Display(-1, 0, 0, 1, 1, 0),
                Display(0, -1, -1, 0, 1, 1),
                Display(0, -1, -0.8f, 0, 1, 0.9f), // portrait envelope crop
            };
            var points = new[] { new Vector2(0.35f, 0.4f), new Vector2(0.35f, 0.6f) };
            Vector2[][] expected = {
                new[] { new Vector2(.35f,.6f), new Vector2(.35f,.4f) },
                new[] { new Vector2(.4f,.35f), new Vector2(.6f,.35f) },
                new[] { new Vector2(.65f,.4f), new Vector2(.65f,.6f) },
                new[] { new Vector2(.6f,.65f), new Vector2(.4f,.65f) },
                new[] { new Vector2(.625f,.65f), new Vector2(.375f,.65f) },
            };
            for (int i = 0; i < displays.Length; i++)
            {
                var rig = Rig(go.GetComponent<ArCameraView>(), camera, displays[i], projection);
                rig.Anchor("terminal:0", new ImagePoint(points[0].x, points[0].y), new CameraFrame { Id = 1 });
                rig.Anchor("terminal:1", new ImagePoint(points[1].x, points[1].y), new CameraFrame { Id = 1 });
                for (int j = 0; j < points.Length; j++)
                {
                    Vector2 actual;
                    Assert(rig.TryGetPosition("terminal:" + j, out actual), "AR mark visible rotation " + i + "/" + j);
                    Near(actual, new Vector2(expected[i][j].x, 1f - expected[i][j].y), "reader row aligned rotation " + i + "/" + j);
                }
            }

            // Controller: horizontal in the sensor image, vertical in a portrait viewport.
            var controller = Rig(go.GetComponent<ArCameraView>(), camera, displays[3], projection);
            controller.Anchor("pin:1", new ImagePoint(.4f, .5f), new CameraFrame { Id = 1 });
            controller.Anchor("pin:2", new ImagePoint(.6f, .5f), new CameraFrame { Id = 1 });
            Vector2 a, b;
            Assert(controller.TryGetPosition("pin:1", out a) && controller.TryGetPosition("pin:2", out b), "controller marks visible");
            controller.TryGetPosition("pin:2", out b);
            Near(a, new Vector2(.5f,.4f), "controller first slot");
            Near(b, new Vector2(.5f,.6f), "controller second slot");

            // A response is delayed: anchor using the captured pose, then move the current camera. With a known
            // 5 mm pitch and fixed slots, expected depth is 5 mm / (0.2 viewport * 2 / projection.m11) = 12.5 mm.
            float z = .005f / (.2f * 2f / projection.m11);
            Vector3 savedOrigin = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            camera.transform.position = savedOrigin + camera.transform.right * .001f;
            Vector3 worldSlot = savedOrigin + savedRotation * new Vector3((.0f + projection.m02) * z / projection.m00,
                (.2f + projection.m12) * z / projection.m11, z);
            // Unity's transform space uses +z forward; the projection's camera space uses -z.
            Vector3 expectedNow = camera.WorldToViewportPoint(worldSlot);
            Assert(controller.TryGetPosition("pin:1", out a), "delayed anchor remains visible after camera move");
            Near(a, new Vector2(expectedNow.x, 1f - expectedNow.y), "captured pose retained after camera move");
            camera.transform.SetPositionAndRotation(savedOrigin, savedRotation);
            controller.TryGetPosition("pin:1", out a);
            var beforeLeaving = a;
            camera.transform.rotation = savedRotation * Quaternion.Euler(0, 180, 0);
            Assert(!controller.TryGetPosition("pin:1", out a), "looking away hides the world overlay");
            camera.transform.SetPositionAndRotation(savedOrigin, savedRotation);
            Assert(controller.TryGetPosition("pin:1", out a), "returning to wiring restores the world overlay");
            Near(a, beforeLeaving, "returning overlay retains its anchored position");
            AddFrame(controller, 99, camera, displays[3], Matrix4x4.zero, null, 1920, 1080);
            controller.Anchor("pin:1", new ImagePoint(.4f, .5f), new CameraFrame { Id = 99 });
            Assert(controller.TryGetPosition("pin:1", out a), "invalid quiet camera update preserves the world anchor");
            Near(a, beforeLeaving, "invalid quiet update cannot move the world anchor");
            // Asynchronous delivery retains acquisition metadata even if the camera moves before it is read.
            var cache = (System.Collections.IDictionary)typeof(ArRig).GetField("frames", Fields).GetValue(controller);
            Set(controller, "readyView", cache[1L]);
            var completed = new CameraFrame { Timestamp = Time.unscaledTimeAsDouble, Width = 2, Height = 2,
                Format = FramePixels.Rgba32, Pixels = new byte[16], Source = "arcore" };
            Set(controller, "readyFrame", completed);
            camera.transform.position += Vector3.right;
            var delivered = controller.Latest(100);
            Assert(delivered.Id == 100 && ReferenceEquals(delivered.Pixels, completed.Pixels),
                "asynchronous frame delivery reuses completed immutable pixels");
            var savedView = cache[100L];
            Assert((Matrix4x4)savedView.GetType().GetField("CameraToWorld").GetValue(savedView) != camera.cameraToWorldMatrix,
                "asynchronous image keeps acquisition pose after camera movement");
            completed.Timestamp = Time.unscaledTimeAsDouble - 2;
            Assert(controller.Latest(101).Format == FramePixels.None, "stalled conversion cannot serve an old image indefinitely");
            camera.transform.SetPositionAndRotation(savedOrigin, savedRotation);
            UnityEngine.Object.DestroyImmediate(go);
        }

        // Set up only the frame cache and camera of the actual ArRig; no AR subsystem or raycast is substituted.
        static ArRig Rig(ArCameraView owner, Camera camera, Matrix4x4 display, Matrix4x4 projection)
        {
            var rig = (ArRig)FormatterServices.GetUninitializedObject(typeof(ArRig));
            Set(rig, "owner", owner); Set(rig, "cam", camera);
            foreach (string name in new[] { "frames", "frameOrder", "anchors", "pending", "lastRejected", "hits" })
            {
                FieldInfo f = typeof(ArRig).GetField(name, Fields);
                f.SetValue(rig, Activator.CreateInstance(f.FieldType));
            }
            Type type = typeof(ArRig).GetNestedType("FrameView", BindingFlags.NonPublic);
            object view = Activator.CreateInstance(type);
            type.GetField("CameraToWorld").SetValue(view, camera.cameraToWorldMatrix);
            type.GetField("Projection").SetValue(view, projection);
            type.GetField("Display").SetValue(view, display);
            type.GetField("HasDisplay").SetValue(view, true);
            type.GetField("W").SetValue(view, 1920); type.GetField("H").SetValue(view, 1080);
            // Populated in the original buggy branch; needed to reproduce its intrinsics shortcut.
            foreach (string name in new[] { "Fx", "Fy", "Cx", "Cy" })
            {
                var f = type.GetField(name);
                if (f != null) f.SetValue(view, name == "Fx" || name == "Fy" ? 1416f : name == "Cx" ? 968f : 545f);
            }
            typeof(ArRig).GetMethod("Remember", Fields).Invoke(rig, new[] { (object)1L, view });
            return rig;
        }

        /// <summary>Adds a frame seen from the camera's current pose, as ArRig.Latest would remember it.</summary>
        static void AddFrame(ArRig rig, long id, Camera camera, Matrix4x4 display, Matrix4x4 projection, float[] intrinsics, int w, int h)
        {
            Type type = typeof(ArRig).GetNestedType("FrameView", BindingFlags.NonPublic);
            object view = Activator.CreateInstance(type);
            type.GetField("CameraToWorld").SetValue(view, camera.cameraToWorldMatrix);
            type.GetField("Projection").SetValue(view, projection);
            type.GetField("Display").SetValue(view, display);
            type.GetField("HasDisplay").SetValue(view, true);
            type.GetField("W").SetValue(view, w); type.GetField("H").SetValue(view, h);
            type.GetField("Intrinsics").SetValue(view, intrinsics);
            typeof(ArRig).GetMethod("Remember", Fields).Invoke(rig, new[] { (object)id, view });
        }

        /// <summary>Where a world point is in the CPU image when the image is the texture the background shows:
        /// the background shader's forward transform of its viewport position (independent of ArRig's inverse).</summary>
        static Vector2 ImageOf(Camera camera, Matrix4x4 display, Vector3 world)
        {
            Vector3 vp = camera.WorldToViewportPoint(world);
            return new Vector2(vp.x * display.m00 + vp.y * display.m10 + display.m20, vp.x * display.m01 + vp.y * display.m11 + display.m21);
        }

        /// <summary>
        /// The intrinsics ray. Fixture: a portrait screen (1080 x 2400) showing a landscape CPU image of the same
        /// aspect (1920 x 864) through the quarter-turn display transform, with the projection built from the
        /// image's intrinsics (P00 = 2 fy / H, P02 = 2 cy / H - 1, P11 = 2 fx / W, P12 = 2 cx / W - 1 for this
        /// turn). (1) Same field of view: the image point comes from the shader's forward transform, so the
        /// intrinsics path must land exactly where the display path does. (2) The CPU image covers 80 % of the
        /// texture's view at its own intrinsics: only the intrinsics path still lands on the point.
        /// </summary>
        static void CheckIntrinsicsRays()
        {
            var go = new GameObject("AR intrinsics check camera", typeof(Camera), typeof(ArCameraView));
            var camera = go.GetComponent<Camera>();
            var owner = go.GetComponent<ArCameraView>();
            camera.pixelRect = new Rect(0, 0, 1080, 2400);
            camera.transform.SetPositionAndRotation(new Vector3(.2f, 1.1f, -.4f), Quaternion.Euler(-12, 30, 0));
            const int W = 1920, H = 864;
            float fx = 1500, fy = 1500, cx = W / 2f + 14, cy = H / 2f - 9;
            var display = Display(0, -1, -1, 0, 1, 1);
            var projection = Matrix4x4.Frustum(-1, 1, -1, 1, .05f, 20f);
            projection.m00 = 2 * fy / H; projection.m02 = 2 * cy / H - 1;
            projection.m11 = 2 * fx / W; projection.m12 = 2 * cx / W - 1;
            camera.projectionMatrix = projection;
            var intrinsics = new[] { fx, fy, cx, cy };

            var world = new[] { camera.transform.TransformPoint(new Vector3(.03f, -.05f, .32f)), camera.transform.TransformPoint(new Vector3(-.06f, .09f, .41f)) };
            foreach (bool useIntrinsics in new[] { false, true })
            {
                owner.arIntrinsicsRays = useIntrinsics;
                var rig = Rig(owner, camera, display, projection);
                AddFrame(rig, 2, camera, display, projection, intrinsics, W, H);
                for (int i = 0; i < world.Length; i++)
                {
                    Vector2 image = ImageOf(camera, display, world[i]);
                    rig.Anchor("label", new ImagePoint(image.x, image.y), new CameraFrame { Id = 2 });
                    Vector2 actual;
                    Assert(rig.TryGetPosition("label", out actual), "intrinsics fixture mark visible " + useIntrinsics + "/" + i);
                    Vector3 vp = camera.WorldToViewportPoint(world[i]);
                    Near(actual, new Vector2(vp.x, 1f - vp.y), (useIntrinsics ? "intrinsics" : "display") + " ray, same field of view, point " + i);
                }
            }

            // the CPU image sees 80 % of the texture's view: same centre, focal length scaled up by 1 / 0.8
            float s = 0.8f;
            var narrow = new[] { fx / s, fy / s, W / 2f + (cx - W / 2f) / s, H / 2f + (cy - H / 2f) / s };
            Vector2 truth = Vector2.zero, viaDisplay = Vector2.zero;
            for (int pass = 0; pass < 2; pass++)
            {
                owner.arIntrinsicsRays = pass == 1;
                var rig = Rig(owner, camera, display, projection);
                AddFrame(rig, 3, camera, display, projection, narrow, W, H);
                // the image point from the CPU image's own intrinsics; this display's turn maps image x to -view y, image y to -view x
                Vector3 c = camera.worldToCameraMatrix.MultiplyPoint(world[0]);
                float tx = c.x / -c.z, ty = c.y / -c.z;
                var image = new Vector2((-ty * narrow[0] + narrow[2]) / W, (-tx * narrow[1] + narrow[3]) / H);
                rig.Anchor("label", new ImagePoint(image.x, image.y), new CameraFrame { Id = 3 });
                Vector2 actual;
                Assert(rig.TryGetPosition("label", out actual), "narrow CPU image mark visible " + pass);
                Vector3 vp = camera.WorldToViewportPoint(world[0]);
                truth = new Vector2(vp.x, 1f - vp.y);
                if (pass == 1) Near(actual, truth, "intrinsics ray when the CPU image covers 80 % of the texture's view");
                else viaDisplay = actual;
            }
            Assert(Vector2.Distance(viaDisplay, truth) > .01f, "display-only ray misses when the CPU image covers 80 % of the view (" +
                Vector2.Distance(viaDisplay, truth).ToString("F4") + " of the screen)");
            owner.arIntrinsicsRays = true;
            UnityEngine.Object.DestroyImmediate(go);
        }

        /// <summary>
        /// Two reads of a row tilted 35 degrees, 5 cm apart: with fusion the marks are within a quarter pitch of the
        /// pins from a third viewpoint 10 cm away; one read alone is off by more than a pitch there. A third read
        /// shifted by 3 pitches across the row (a wrong place, not a depth) is refused and the marks stay.
        /// Then the production AnchorFusion runs the simulation table written to fusion-simulation.txt.
        /// </summary>
        static void CheckFusion()
        {
            var go = new GameObject("AR fusion check camera", typeof(Camera), typeof(ArCameraView));
            var camera = go.GetComponent<Camera>();
            var owner = go.GetComponent<ArCameraView>();
            camera.pixelRect = new Rect(0, 0, 1080, 2400);
            var projection = Matrix4x4.Frustum(-.0225f, .0225f, -.05f, .05f, .05f, 20f);   // ~ a phone's portrait view
            camera.projectionMatrix = projection;
            var display = Display(0, -1, -1, 0, 1, 1);
            float pitch = owner.arControllerPinSpacingMm / 1000f;
            Vector3 centre = new Vector3(0, 0, .35f);
            float tilt = 35 * Mathf.Deg2Rad;
            Vector3 along = new Vector3(Mathf.Cos(tilt), 0, Mathf.Sin(tilt));
            var pins = new Vector3[8];
            for (int i = 0; i < pins.Length; i++) pins[i] = centre + along * pitch * (i - 3.5f);
            Vector3[] eyes = { Vector3.zero, new Vector3(.05f, .01f, 0), new Vector3(-.10f, .03f, .02f) };

            foreach (bool fuse in new[] { false, true })
            {
                owner.arFuseReads = fuse;
                var rig = Rig(owner, camera, display, projection);
                for (int view = 0; view < 2; view++)
                {
                    camera.transform.SetPositionAndRotation(eyes[view], Quaternion.LookRotation(centre - eyes[view]));
                    AddFrame(rig, 10 + view, camera, display, projection, null, 1920, 864);
                    for (int i = 0; i < pins.Length; i++)
                    {
                        Vector2 im = ImageOf(camera, display, pins[i]);
                        rig.Anchor("pin:" + (i + 1), new ImagePoint(im.x, im.y), new CameraFrame { Id = 10 + view });
                    }
                    rig.Tick();
                }
                camera.transform.SetPositionAndRotation(eyes[2], Quaternion.LookRotation(centre - eyes[2]));
                float worst = 0;
                for (int i = 0; i < pins.Length; i++)
                {
                    Vector2 actual;
                    Assert(rig.TryGetPosition("pin:" + (i + 1), out actual), "fusion fixture mark visible " + fuse + "/" + i);
                    worst = Mathf.Max(worst, PitchesOff(camera, actual, pins[i], pitch));
                }
                if (fuse) Assert(worst < .25f, "two reads 5 cm apart keep a 35-degree row within a quarter pitch from 10 cm away (" + worst.ToString("F2") + ")");
                else Assert(worst > 1f, "without fusion the second read replaces the first and the depth stays the spacing's (" + worst.ToString("F2") + " pitch)");

                if (!fuse) continue;
                // a read 3 pitches across the row, seen from a third place whose epipolar lines run along the row:
                // that cannot be a depth error, so it is refused and the marks stay
                Vector3 third = new Vector3(-.03f, .005f, 0);
                camera.transform.SetPositionAndRotation(third, Quaternion.LookRotation(centre - third));
                AddFrame(rig, 20, camera, display, projection, null, 1920, 864);
                Vector3 across = Vector3.Cross(along, Vector3.forward).normalized;
                for (int i = 0; i < pins.Length; i++)
                {
                    Vector2 im = ImageOf(camera, display, pins[i] + across * 3 * pitch);
                    rig.Anchor("pin:" + (i + 1), new ImagePoint(im.x, im.y), new CameraFrame { Id = 20 });
                }
                rig.Tick();
                camera.transform.SetPositionAndRotation(eyes[2], Quaternion.LookRotation(centre - eyes[2]));
                float moved = 0;
                for (int i = 0; i < pins.Length; i++)
                {
                    Vector2 now;
                    rig.TryGetPosition("pin:" + (i + 1), out now);
                    moved = Mathf.Max(moved, PitchesOff(camera, now, pins[i], pitch));
                }
                Assert(moved < .25f, "a read 3 pitches across the row is refused (" + moved.ToString("F2") + " pitch)");
            }
            owner.arFuseReads = true;
            UnityEngine.Object.DestroyImmediate(go);
            FusionSimulation(pitch);
        }

        /// <summary>How far a screen position is from a world point, in pitches at the point's distance.</summary>
        static float PitchesOff(Camera camera, Vector2 screen, Vector3 world, float pitch)
        {
            Vector3 vp = camera.WorldToViewportPoint(world);
            Ray a = camera.ViewportPointToRay(new Vector3(screen.x, 1f - screen.y, 0));
            Ray b = camera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0));
            return Vector3.Angle(a.direction, b.direction) * Mathf.Deg2Rad * vp.z / pitch;
        }

        /// <summary>
        /// Monte Carlo of the production AnchorFusion on a row of 8 pins at 35 cm (reader noise 0.08 pitch, as on
        /// the bench): how far the marks are off after the phone moves 10 cm, one read against two. Written to
        /// data/ar-overlay-checks/fusion-simulation.txt for the report.
        /// </summary>
        static void FusionSimulation(float pitch)
        {
            var random = new System.Random(7);
            Func<float> gauss = () => { double u1 = 1 - random.NextDouble(), u2 = random.NextDouble(); return (float)(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2)); };
            var lines = new List<string> { "tilt_deg\treads\tbaseline_cm\tmedian_pitch\tp90_pitch\twithin_half_pitch" };
            Vector3 test = new Vector3(.10f, .03f, .02f);
            float halfAt35Two = 0, halfAt35One = 1;
            foreach (float tiltDeg in new[] { 0f, 20f, 35f })
                foreach (float baseline in new[] { 0f, .02f, .05f })
                {
                    var off = new List<float>();
                    for (int trial = 0; trial < 300; trial++)
                    {
                        float tilt = tiltDeg * Mathf.Deg2Rad;
                        Vector3 along = new Vector3(Mathf.Cos(tilt), 0, Mathf.Sin(tilt)), centre = new Vector3(0, 0, .35f);
                        var pins = new Vector3[8];
                        for (int i = 0; i < 8; i++) pins[i] = centre + along * pitch * (i - 3.5f);
                        var first = SimView(Vector3.zero, centre, pins, pitch, gauss);
                        var second = baseline > 0 ? SimView(new Vector3(baseline, .005f, 0), centre, pins, pitch, gauss) : null;
                        for (int i = 0; i < 8; i++)
                        {
                            var list = new List<RaySighting> { first[i] };
                            if (second != null) list.Add(second[i]);
                            Vector3 x = AnchorFusion.FuseRobust(list, .4f * pitch);
                            off.Add(Vector3.Angle(x - test, pins[i] - test) * Mathf.Deg2Rad * (pins[i] - test).magnitude / pitch);
                        }
                    }
                    off.Sort();
                    float half = off.FindAll(v => v <= .5f).Count / (float)off.Count;
                    lines.Add(tiltDeg + "\t" + (baseline > 0 ? 2 : 1) + "\t" + baseline * 100 + "\t" + off[off.Count / 2].ToString("F2") + "\t" +
                        off[(int)(off.Count * .9f)].ToString("F2") + "\t" + half.ToString("F2"));
                    if (tiltDeg == 35 && baseline == .05f) halfAt35Two = half;
                    if (tiltDeg == 35 && baseline == 0) halfAt35One = half;
                }
            Directory.CreateDirectory(OutputFolder());
            File.WriteAllText(Path.Combine(OutputFolder(), "fusion-simulation.txt"), string.Join("\n", lines) + "\n");
            Assert(halfAt35Two >= .95f && halfAt35One <= .10f, "simulation: 35-degree row, marks within half a pitch after a 10 cm move: one read " +
                halfAt35One.ToString("P0") + ", two reads 5 cm apart " + halfAt35Two.ToString("P0"));
        }

        /// <summary>One simulated read: rays through the noisy image positions, depth from the row spacing.</summary>
        static List<RaySighting> SimView(Vector3 eye, Vector3 look, Vector3[] pins, float pitch, Func<float> gauss)
        {
            Vector3 f = (look - eye).normalized, r = Vector3.Cross(Vector3.up, f).normalized, u = Vector3.Cross(f, r);
            var tan = new List<Vector2>();
            foreach (var p in pins)
            {
                Vector3 v = p - eye;
                float z = Vector3.Dot(v, f);
                tan.Add(new Vector2(Vector3.Dot(v, r) / z, Vector3.Dot(v, u) / z) + new Vector2(gauss(), gauss()) * .08f * pitch / z);
            }
            float spacing;
            AnchorFusion.RowSpacing(tan, out spacing);
            float depth = pitch / spacing;
            var list = new List<RaySighting>();
            foreach (var t in tan)
            {
                Vector3 dir = f + r * t.x + u * t.y;
                float range = depth * dir.magnitude;
                list.Add(new RaySighting { Origin = eye, Direction = dir.normalized, HasRange = true, Range = range,
                    SigmaLateral = .25f * pitch, SigmaRange = Mathf.Max(.005f, .25f * range) });
            }
            return list;
        }

        static void CheckWebcam()
        {
            var root = new GameObject("marker layer", typeof(RectTransform)).GetComponent<RectTransform>();
            root.sizeDelta = new Vector2(1000, 2000);
            var picture = new GameObject("camera image", typeof(RectTransform)).GetComponent<RectTransform>();
            picture.SetParent(root, false);
            picture.anchorMin = picture.anchorMax = new Vector2(.5f,.5f);
            picture.pivot = new Vector2(.5f,.5f);
            picture.sizeDelta = new Vector2(2000, 1000);
            picture.localEulerAngles = new Vector3(0, 0, -90);
            Vector2 p;
            Assert(CameraImageGeometry.TryWebcamViewport(new Vector2(.25f,.3f), new Rect(0,0,1,1), picture, root, out p), "webcam rotated mark visible");
            Near(p, new Vector2(.7f,.25f), "webcam image and marker share rotation");
            CameraImageGeometry.TryWebcamViewport(new Vector2(.25f,.3f), new Rect(0,1,1,-1), picture, root, out p);
            Near(p, new Vector2(.3f,.25f), "webcam vertical mirror follows UV rectangle");
            picture.sizeDelta = new Vector2(2400, 1200);
            CameraImageGeometry.TryWebcamViewport(new Vector2(.25f,.3f), new Rect(0,0,1,1), picture, root, out p);
            Near(p, new Vector2(.74f,.2f), "webcam envelope crop");
            UnityEngine.Object.DestroyImmediate(root.gameObject);
        }

        static Matrix4x4 Display(float a, float b, float c, float d, float x, float y)
        {
            var m = Matrix4x4.identity;
            m.m00 = a; m.m10 = b; m.m01 = c; m.m11 = d; m.m20 = x; m.m21 = y;
            return m;
        }
        static void Set(object target, string field, object value) { target.GetType().GetField(field, Fields).SetValue(target, value); }
        static void Near(Vector2 actual, Vector2 expected, string name)
        { Assert(Vector2.Distance(actual, expected) < .0001f, name + ": " + actual.ToString("F5") + " expected " + expected.ToString("F5")); }
        static void Assert(bool yes, string name)
        {
            if (!yes) throw new Exception("AR geometry regression: " + name);
            checks.Add("PASS " + name); passed++;
        }
        static string OutputFolder() { return Path.GetFullPath(Path.Combine(Application.dataPath, "../../../data/ar-overlay-checks")); }

        [Serializable] sealed class SavedFrame { public SavedResponse response; }
        [Serializable] sealed class SavedResponse { public SavedItem[] items; }
        [Serializable] sealed class SavedItem { public float x, y; public int colour; public bool found; }

        // A replay, not a phone screenshot: sample the saved camera frame with the background shader's forward
        // transform, then draw its recorded detections through the production inverse mapping. The old captures
        // used MirrorY, so undo their 180-degree storage rotation first. No new recognition result is claimed.
        static void RenderSavedFrame(string stem, string output)
        {
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../data/live/2026-11-02-b", stem));
            if (!File.Exists(source + ".json") || !File.Exists(source + ".jpg")) return;
            var saved = JsonUtility.FromJson<SavedFrame>(File.ReadAllText(source + ".json"));
            var texture = new Texture2D(2, 2);
            texture.LoadImage(File.ReadAllBytes(source + ".jpg"));
            const int w = 540, h = 1200;
            var pixels = new Color[w * h];
            // Portrait viewport: native sensor x = 1 - viewport.y, y = .9 - .8 * viewport.x.
            var display = Display(0, -1, -.8f, 0, 1, .9f);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                pixels[y * w + x] = texture.GetPixelBilinear(y / (float)(h - 1), .9f - .8f * x / (w - 1));
            var colours = new[] { Color.red, Color.black, Color.green, Color.yellow, Color.white, new Color(.5f,.25f,0), Color.blue, new Color(1,.5f,0) };
            foreach (var item in saved.response.items)
            {
                if (!item.found) continue;
                Vector2 vp;
                if (!CameraImageGeometry.TryViewport(new Vector2(1f - item.x, 1f - item.y), display, out vp)) continue;
                int cx = Mathf.RoundToInt(vp.x * (w - 1)), cy = Mathf.RoundToInt(vp.y * (h - 1));
                Color ink = item.colour >= 0 && item.colour < colours.Length ? colours[item.colour] : Color.gray;
                for (int dy = -8; dy <= 8; dy++) for (int dx = -8; dx <= 8; dx++)
                {
                    int x = cx + dx, y = cy + dy, r = dx * dx + dy * dy;
                    if (x >= 0 && x < w && y >= 0 && y < h && r <= 64) pixels[y * w + x] = r >= 36 ? Color.white : ink;
                }
            }
            var result = new Texture2D(w, h, TextureFormat.RGB24, false);
            result.SetPixels(pixels); result.Apply();
            File.WriteAllBytes(Path.Combine(OutputFolder(), output), result.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(result);
        }

        public static void BuildAndroid()
        {
            string path = "Builds/InstallerAssistant_ARAlignment_20261102.apk";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/_Project/Scenes/App.unity" }, locationPathName = path,
                target = BuildTarget.Android, options = BuildOptions.Development });
            Debug.Log("[ArGeometryChecks] Android build " + report.summary.result + " " + path);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
