using System;
using System.Collections;
using System.Collections.Generic;
using InstallerAssistant.Labels;
using InstallerAssistant.Verification;
using UnityEngine;

namespace InstallerAssistant.Detection
{
    /// <summary>
    /// Layer 1 in live mode: sends the frame to the perception service and turns its answer into a reading.
    /// Owner: package A. The reading model itself lives in Python (ml/perception_service); this class only
    /// converts. At the controller it also passes on the service's survey of the other modules (other_blocks), which
    /// the zone check needs. If the service fails, every terminal comes back as "no wire" with confidence 0, and the
    /// app asks the installer to read again.
    /// </summary>
    public sealed class PerceptionWireReader : IWireReader
    {
        readonly PerceptionClient client;
        public PerceptionWireReader(PerceptionClient client) { this.client = client; }

        public IEnumerator ReadDeviceEnd(UsageRule rule, CableLabel cable, CameraFrame frame, Action<DeviceEndReading> done)
        {
            PerceptionClient.ReadResponse res = null;
            yield return client.Read(frame, CableEnd.Device, rule, cable, (r, e) => { res = r; if (e != null) Warn(e); });
            int n = rule.DeviceTerminals.Length;
            int[] colours; float[] conf; ImagePoint[] where; WireTrace[] traces;
            Convert(res != null ? res.items : null, n, out colours, out conf, out where, out traces);
            done(new DeviceEndReading(colours, conf, where, frame != null ? frame.Id : 0, traces));
        }

        public IEnumerator ReadControllerEnd(UsageRule rule, CableLabel cable, CameraFrame frame, Action<ControllerReading> done)
        {
            PerceptionClient.ReadResponse res = null;
            yield return client.Read(frame, CableEnd.Controller, rule, cable, (r, e) => { res = r; if (e != null) Warn(e); });
            int n = rule.Connector.Count;
            int[] colours; float[] conf; ImagePoint[] where; WireTrace[] traces;
            Convert(res != null ? res.items : null, n, out colours, out conf, out where, out traces);
            done(new ControllerReading(colours, conf, where, frame != null ? frame.Id : 0, Survey(res, rule), traces));
        }

        /// <summary>The service's answer for the controller's other modules (only the modules the app asked about).</summary>
        static ModuleReading[] Survey(PerceptionClient.ReadResponse res, UsageRule rule)
        {
            var list = new List<ModuleReading>();
            if (res == null || res.other_blocks == null) return list.ToArray();
            foreach (var m in AxisCatalog.OtherModules(rule))
                foreach (var b in res.other_blocks)
                {
                    if (b == null || b.module != m.Id) continue;
                    int[] colours; float[] conf; ImagePoint[] where; WireTrace[] traces;
                    Convert(b.items, m.Count, out colours, out conf, out where, out traces);
                    list.Add(new ModuleReading(m, b.located, colours, conf, where));
                    break;
                }
            return list.ToArray();
        }

        static void Convert(PerceptionClient.ReadItem[] items, int n, out int[] colours, out float[] conf, out ImagePoint[] where, out WireTrace[] traces)
        {
            colours = new int[n]; conf = new float[n]; where = new ImagePoint[n]; traces = new WireTrace[n];
            for (int i = 0; i < n; i++)
            {
                var it = items != null && i < items.Length ? items[i] : null;
                if (it == null) { colours[i] = WireColours.None; continue; }
                colours[i] = it.colour >= 0 && it.colour < WireColours.Names.Length ? it.colour : WireColours.None;
                conf[i] = Mathf.Clamp01(it.confidence);
                if (it.found) where[i] = new ImagePoint(Mathf.Clamp01(it.x), Mathf.Clamp01(it.y));
                traces[i] = it.trace == "this_cable" ? WireTrace.ThisCable : it.trace == "other_cable" ? WireTrace.OtherCable : WireTrace.Unknown;
            }
        }

        static void Warn(string e) { Debug.LogWarning("[InstallerAssistant] Wire reading failed: " + e); }
    }
}
