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

        public IEnumerator Scan(Action<LabelScan> done)
        {
            yield return w.Delay(w.AtController ? 1.2f : 1.6f);
            CableLabel label;
            if (w.AtController && !w.LabelAttached) done(new LabelScan { Status = LabelScanStatus.NotFound });
            else if (!CableLabel.TryParse(w.Layout.cableLabel, out label)) done(new LabelScan { Status = LabelScanStatus.Unreadable });
            else done(new LabelScan { Status = LabelScanStatus.Found, Label = label.AtEnd(w.AtController ? CableEnd.Controller : CableEnd.Device) });
        }
    }

    /// <summary>Stands in for the VLM wire reading (layer 1).</summary>
    public sealed class WozWireReader : IWireReader
    {
        readonly WozWorld w;
        public WozWireReader(WozWorld world) { w = world; }

        public IEnumerator ReadDeviceEnd(UsageRule rule, Action<DeviceEndReading> done)
        {
            yield return w.Delay(w.ReaderReads == 0 ? 2.4f : 1.8f);
            int n = rule.DeviceTerminals.Length;
            var colours = new int[n];
            var conf = new float[n];
            for (int i = 0; i < n; i++)
            {
                colours[i] = Mathf.Clamp(Get(w.Layout.readerColours, i, i), 0, WireColours.Names.Length - 1);
                bool unsure = w.ReaderReads == 0 && i == w.Layout.uncertainReaderTerminal;
                conf[i] = unsure ? 0.71f : 0.93f + 0.01f * ((i * 7) % 6);
            }
            w.ReaderReads++;
            done(new DeviceEndReading(colours, conf));
        }

        public IEnumerator ReadControllerEnd(UsageRule rule, Action<ControllerReading> done)
        {
            yield return w.Delay(w.ControllerChecks == 0 ? 2.2f : 1.6f);
            int n = rule.Connector.Count;
            var conf = new float[n];
            for (int i = 0; i < n; i++) conf[i] = 0.92f + 0.01f * ((i * 5) % 7);
            w.ControllerChecks++;
            done(new ControllerReading(w.ControllerPins(n), conf));
        }

        static int Get(int[] a, int i, int fallback) { return a != null && i < a.Length ? a[i] : fallback; }
    }

    /// <summary>Stands in for the independent VLM comparison (layer 3). It disagrees once, on the scripted pin.</summary>
    public sealed class WozSecondOpinion : ISecondOpinion
    {
        readonly WozWorld w;
        public WozSecondOpinion(WozWorld world) { w = world; }

        public IEnumerator Compare(CableRecord record, Action<Opinion[]> done)
        {
            yield return w.Delay(w.Rewired ? 2.0f : 2.8f);
            int n = record.Rule.Connector.Count;
            var pins = w.ControllerPins(n);
            var ops = new Opinion[n];
            for (int p = 1; p <= n; p++) ops[p - 1] = record.ExpectedOnPin(p) == pins[p - 1] ? Opinion.Match : Opinion.Mismatch;
            int d = w.Scenario != null ? w.Scenario.aiDisagreesOnPin : 0;
            if (!w.Rewired && d >= 1 && d <= n) ops[d - 1] = ops[d - 1] == Opinion.Match ? Opinion.Mismatch : Opinion.Match;
            done(ops);
        }
    }
}
