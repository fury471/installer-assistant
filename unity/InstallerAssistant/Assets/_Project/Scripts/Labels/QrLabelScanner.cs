using System;
using System.Collections;
using System.Collections.Generic;
using InstallerAssistant.Detection;
using InstallerAssistant.Verification;
using UnityEngine;

namespace InstallerAssistant.Labels
{
    /// <summary>
    /// Finds and decodes the cable label's QR code in a camera frame. Owner: package A (Perception and AI).
    ///
    /// Decoding uses ZXing.Net when it is in the project:
    ///   1. Download ZXing.Net (Apache 2.0) and copy zxing.unity.dll (or the netstandard2.0 zxing.dll) into
    ///      Assets/_Project/ThirdParty/ZXing/.
    ///   2. Player Settings → Other Settings → Scripting Define Symbols: add ZXING_NET.
    /// Without it, the scanner finds nothing, except a test payload set in DebugPayload (handy for trying the
    /// live flow before decoding works). The label format is described in CableLabel.
    /// </summary>
    public sealed class QrLabelScanner : ILabelScanner
    {
        /// <summary>Test only: if set, every scan "finds" this label (e.g. "IA|C01|D1|A1610.READER1|A4020E|DEV").</summary>
        public static string DebugPayload = "";

        public IEnumerator Scan(CameraFrame frame, Action<LabelScan> done)
        {
            yield return null;   // one frame, so the UI can update before decoding
            long id = frame != null ? frame.Id : 0;
            CableLabel label;
            if (!string.IsNullOrEmpty(DebugPayload))
            {
                if (CableLabel.TryParse(DebugPayload, out label))
                    done(new LabelScan { Status = LabelScanStatus.Found, Label = label, FrameId = id, Corners = Box(0.5f, 0.6f, 0.15f, 0.08f) });
                else
                {
                    Debug.LogWarning("[InstallerAssistant] Test Label \"" + DebugPayload + "\" is not a cable label. " +
                                     "Format: IA|C01|D1|A1610.READER1|A4020E (see CableLabel).");
                    done(new LabelScan { Status = LabelScanStatus.Unreadable, FrameId = id });
                }
                yield break;
            }
            string text = null; ImagePoint[] corners = new ImagePoint[0]; bool found = false;
            if (frame != null && frame.Format == FramePixels.Rgba32)
            {
                // decode on a worker thread, so the camera picture stays smooth while ZXing searches the frame
                var task = System.Threading.Tasks.Task.Run(() =>
                {
                    string t; ImagePoint[] c;
                    found = Decode(frame, out t, out c);
                    text = t; corners = c;
                });
                while (!task.IsCompleted) yield return null;
                if (task.IsFaulted) { Debug.LogWarning("[InstallerAssistant] QR decoding failed: " + task.Exception); found = false; }
            }
            else found = Decode(frame, out text, out corners);   // JPEG frames need Texture2D: main thread only
            if (!found) { done(new LabelScan { Status = LabelScanStatus.NotFound, FrameId = id }); yield break; }
            if (!CableLabel.TryParse(text, out label))
            {
                Debug.Log("[InstallerAssistant] QR code is not a cable label: " + text);
                done(new LabelScan { Status = LabelScanStatus.Unreadable, FrameId = id, Corners = corners });
                yield break;
            }
            done(new LabelScan { Status = LabelScanStatus.Found, Label = label, FrameId = id, Corners = corners });
        }

        /// <summary>Decodes a QR code in the frame; corners are normalised to the frame (x right, y down).
        /// Safe on a worker thread for RGBA32 frames (no Unity objects are touched on that path).</summary>
        public static bool Decode(CameraFrame frame, out string text, out ImagePoint[] corners)
        {
            text = null; corners = new ImagePoint[0];
            byte[] rgba; int w, h;
            if (!TopDownRgba(frame, out rgba, out w, out h)) return false;
#if ZXING_NET
            var reader = new ZXing.BarcodeReaderGeneric();
            reader.Options.PossibleFormats = new List<ZXing.BarcodeFormat> { ZXing.BarcodeFormat.QR_CODE };
            reader.Options.TryHarder = true;
            var source = new ZXing.RGBLuminanceSource(rgba, w, h, ZXing.RGBLuminanceSource.BitmapFormat.RGBA32);
            var result = reader.Decode(source);
            if (result == null || string.IsNullOrEmpty(result.Text)) return false;
            text = result.Text;
            // ZXing returns the finder pattern centres; the label frame is their bounding box, a little larger
            float x0 = 1, x1 = 0, y0 = 1, y1 = 0;
            if (result.ResultPoints != null)
                foreach (var p in result.ResultPoints)
                {
                    float x = p.X / w, y = p.Y / h;
                    x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                }
            if (x1 > x0 && y1 > y0)
            {
                float mx = (x1 - x0) * 0.25f, my = (y1 - y0) * 0.25f;
                corners = Box((x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2 + mx, (y1 - y0) / 2 + my);
            }
            return true;
#else
            return false;
#endif
        }

        /// <summary>RGBA bytes with row 0 at the top (frames carry Unity's bottom-up order).</summary>
        static bool TopDownRgba(CameraFrame f, out byte[] rgba, out int w, out int h)
        {
            rgba = null; w = 0; h = 0;
            if (f == null || f.Pixels == null || f.Pixels.Length == 0) return false;
            byte[] bottomUp;
            if (f.Format == FramePixels.Rgba32)
            {
                w = f.Width; h = f.Height;
                if (w <= 0 || h <= 0 || f.Pixels.Length < w * h * 4) return false;
                bottomUp = f.Pixels;
            }
            else if (f.Format == FramePixels.Jpeg)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(f.Pixels)) { UnityEngine.Object.Destroy(tex); return false; }
                w = tex.width; h = tex.height;
                bottomUp = tex.GetRawTextureData();
                UnityEngine.Object.Destroy(tex);
            }
            else return false;
            int stride = w * 4;
            rgba = new byte[stride * h];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(bottomUp, (h - 1 - y) * stride, rgba, y * stride, stride);
            return true;
        }

        static ImagePoint[] Box(float cx, float cy, float hx, float hy)
        {
            return new[] { new ImagePoint(cx - hx, cy - hy), new ImagePoint(cx + hx, cy - hy), new ImagePoint(cx + hx, cy + hy), new ImagePoint(cx - hx, cy + hy) };
        }
    }
}
