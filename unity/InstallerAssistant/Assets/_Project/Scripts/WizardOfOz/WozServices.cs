using System;
using System.Collections;
using InstallerAssistant.AI;
using InstallerAssistant.Detection;
using InstallerAssistant.Labels;
using InstallerAssistant.Verification;
using UnityEngine;

namespace InstallerAssistant.WizardOfOz
{
    /// <summary>
    /// The scripted world behind the Wizard-of-Oz services: which fault is planted, whether the
    /// installer has attached the label and fixed the wiring. Only the sensing is faked;
    /// the flow, the rules and the verdicts are the app's real code.
    /// </summary>
    public sealed class WozWorld
    {
        public DemoLayout Layout;
        public DemoLayout.Scenario Scenario;
        public CableRecord Record;
        public bool AtController;
        public bool LabelAttached = true;
        public bool Rewired;
        /// <summary>The installer fixed the device end instead: its terminals now match the controller's pins.</summary>
        public bool DeviceRewired;
        /// <summary>The photo view on screen now: its hand-placed points stand in for perception.</summary>
        public DemoLayout.PhotoView View;
        public int ReaderReads;
        public int ControllerChecks;
        public float Speed = 1f;

        /// <summary>Colours on Reader 1 pins 1-8 as the camera would see them now.</summary>
        public int[] ControllerPins(int pinCount)
        {
            var pins = new int[pinCount];
            for (int p = 1; p <= pinCount; p++)
            {
                if (Rewired && Record != null) pins[p - 1] = Record.ExpectedOnPin(p);
                else pins[p - 1] = Scenario != null && Scenario.pinColours != null && p - 1 < Scenario.pinColours.Length
                    ? Scenario.pinColours[p - 1] : WireColours.None;
            }
            return pins;
        }

        /// <summary>Colours on the device terminals as the camera would see them now (rule order).</summary>
        public int[] DeviceColours(UsageRule rule)
        {
            int n = rule.DeviceTerminals.Length;
            var c = new int[n];
            var pins = DeviceRewired ? ControllerPins(rule.Connector.Count) : null;
            for (int i = 0; i < n; i++)
                c[i] = pins != null ? pins[rule.PinFor(rule.DeviceTerminals[i].Id) - 1]
                     : Mathf.Clamp(Layout.readerColours != null && i < Layout.readerColours.Length ? Layout.readerColours[i] : i, 0, WireColours.Names.Length - 1);
            return c;
        }

        public static ImagePoint[] Points(DemoLayout.PhotoView v, int count)
        {
            var pts = new ImagePoint[count];
            for (int i = 0; i < count; i++)
                if (v != null && v.points != null && i < v.points.Length) pts[i] = new ImagePoint(v.points[i].x, v.points[i].y);
            return pts;
        }

        public IEnumerator Delay(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds / Mathf.Max(0.1f, Speed));
        }
    }

    /// <summary>Stands in for QR decoding: "finds" the scripted label of the end the camera shows, unless it is planted as missing.</summary>
    public sealed class WozLabelScanner : ILabelScanner
    {
        readonly WozWorld w;
        public WozLabelScanner(WozWorld world) { w = world; }

        public IEnumerator Scan(CameraFrame frame, Action<LabelScan> done)
        {
            yield return w.Delay(w.AtController ? 1.2f : 1.6f);
            long id = frame != null ? frame.Id : 0;
            CableLabel label;
            if (w.AtController && !w.LabelAttached) done(new LabelScan { Status = LabelScanStatus.NotFound, FrameId = id });
            else if (!CableLabel.TryParse(w.Layout.cableLabel, out label)) done(new LabelScan { Status = LabelScanStatus.Unreadable, FrameId = id });
            else done(new LabelScan
            {
                Status = LabelScanStatus.Found, FrameId = id, Corners = Corners(w.View),
                Label = label.AtEnd(w.AtController ? CableEnd.Controller : CableEnd.Device),
            });
        }

        static ImagePoint[] Corners(DemoLayout.PhotoView v)
        {
            if (v == null) return new ImagePoint[0];
            Vector2 c = v.labelPos, h = (v.labelSize.x > 0 && v.labelSize.y > 0 ? v.labelSize : new Vector2(0.12f, 0.06f)) * 0.5f;
            return new[] { new ImagePoint(c.x - h.x, c.y - h.y), new ImagePoint(c.x + h.x, c.y - h.y),
                           new ImagePoint(c.x + h.x, c.y + h.y), new ImagePoint(c.x - h.x, c.y + h.y) };
        }
    }

    /// <summary>Stands in for the VLM wire reading (layer 1). It does not trace wires or survey other modules, so the bundle and zone checks stay silent.</summary>
    public sealed class WozWireReader : IWireReader
    {
        readonly WozWorld w;
        public WozWireReader(WozWorld world) { w = world; }

        public IEnumerator ReadDeviceEnd(UsageRule rule, CableLabel cable, CameraFrame frame, Action<DeviceEndReading> done)
        {
            yield return w.Delay(w.ReaderReads == 0 ? 2.4f : 1.8f);
            int n = rule.DeviceTerminals.Length;
            var colours = w.DeviceColours(rule);
            var conf = new float[n];
            for (int i = 0; i < n; i++)
            {
                bool unsure = w.ReaderReads == 0 && !w.DeviceRewired && i == w.Layout.uncertainReaderTerminal;
                conf[i] = unsure ? 0.71f : 0.93f + 0.01f * ((i * 7) % 6);
            }
            w.ReaderReads++;
            done(new DeviceEndReading(colours, conf, WozWorld.Points(w.View, n), frame != null ? frame.Id : 0));
        }

        public IEnumerator ReadControllerEnd(UsageRule rule, CableLabel cable, CameraFrame frame, Action<ControllerReading> done)
        {
            yield return w.Delay(w.ControllerChecks == 0 ? 2.2f : 1.6f);
            int n = rule.Connector.Count;
            var conf = new float[n];
            for (int i = 0; i < n; i++) conf[i] = 0.92f + 0.01f * ((i * 5) % 7);
            w.ControllerChecks++;
            done(new ControllerReading(w.ControllerPins(n), conf, WozWorld.Points(w.View, n), frame != null ? frame.Id : 0));
        }
    }

    /// <summary>Stands in for the independent VLM comparison (layer 3). It disagrees once, on the scripted pin.</summary>
    public sealed class WozSecondOpinion : ISecondOpinion
    {
        readonly WozWorld w;
        public WozSecondOpinion(WozWorld world) { w = world; }

        public IEnumerator Compare(CableRecord record, CameraFrame deviceEnd, CameraFrame controllerEnd, Action<Opinion[]> done)
        {
            yield return w.Delay(w.Rewired ? 2.0f : 2.8f);
            int n = record.Rule.Connector.Count;
            var pins = w.ControllerPins(n);
            var ops = new Opinion[n];
            for (int p = 1; p <= n; p++) ops[p - 1] = record.ExpectedOnPin(p) == pins[p - 1] ? Opinion.Match : Opinion.Mismatch;
            int d = w.Scenario != null ? w.Scenario.aiDisagreesOnPin : 0;
            if (!w.Rewired && !w.DeviceRewired && d >= 1 && d <= n) ops[d - 1] = ops[d - 1] == Opinion.Match ? Opinion.Mismatch : Opinion.Match;
            done(ops);
        }
    }

    /// <summary>Stands in for the AR camera: the "frame" is the demo photo on screen (no pixels are read).</summary>
    public sealed class WozFrameSource : ICameraFrameSource
    {
        readonly Func<Texture> current;
        long next;
        public WozFrameSource(Func<Texture> current) { this.current = current; }

        public CameraFrame Latest
        {
            get
            {
                var t = current();
                return new CameraFrame
                {
                    Id = ++next, Timestamp = Time.unscaledTimeAsDouble, Format = FramePixels.None,
                    Width = t != null ? t.width : 0, Height = t != null ? t.height : 0,
                    Source = "photo:" + (t != null ? t.name : "none"),
                };
            }
        }
    }
}
