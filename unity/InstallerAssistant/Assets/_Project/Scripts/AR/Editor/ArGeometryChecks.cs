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
            UnityEngine.Object.DestroyImmediate(go);
        }

        // Set up only the frame cache and camera of the actual ArRig; no AR subsystem or raycast is substituted.
        static ArRig Rig(ArCameraView owner, Camera camera, Matrix4x4 display, Matrix4x4 projection)
        {
            var rig = (ArRig)FormatterServices.GetUninitializedObject(typeof(ArRig));
            Set(rig, "owner", owner); Set(rig, "cam", camera);
            foreach (string name in new[] { "frames", "frameOrder", "frameDepth", "anchors" })
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
