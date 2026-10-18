using System;
using System.Collections;
using InstallerAssistant.Labels;
using InstallerAssistant.Verification;
using UnityEngine;

namespace InstallerAssistant.Detection
{
    /// <summary>
    /// Layer 1 in live mode: sends the frame to the perception service and turns its answer into a reading.
    /// Owner: package A. The reading model itself lives in Python (ml/perception_service); this class only
    /// converts. If the service fails, every terminal comes back as "no wire" with confidence 0, and the app
    /// asks the installer to read again.
    /// </summary>
    public sealed class PerceptionWireReader : IWireReader
    {
        readonly PerceptionClient client;
        public PerceptionWireReader(PerceptionClient client) { this.client = client; }

        public IEnumerator ReadDeviceEnd(UsageRule rule, CameraFrame frame, Action<DeviceEndReading> done)
        {
            PerceptionClient.ReadResponse res = null;
            yield return client.Read(frame, CableEnd.Device, rule, (r, e) => { res = r; if (e != null) Warn(e); });
            int n = rule.DeviceTerminals.Length;
            int[] colours; float[] conf; ImagePoint[] where;
            Convert(res, n, out colours, out conf, out where);
            done(new DeviceEndReading(colours, conf, where, frame != null ? frame.Id : 0));
        }

        public IEnumerator ReadControllerEnd(UsageRule rule, CameraFrame frame, Action<ControllerReading> done)
        {
            PerceptionClient.ReadResponse res = null;
            yield return client.Read(frame, CableEnd.Controller, rule, (r, e) => { res = r; if (e != null) Warn(e); });
            int n = rule.Connector.Count;
            int[] colours; float[] conf; ImagePoint[] where;
            Convert(res, n, out colours, out conf, out where);
            done(new ControllerReading(colours, conf, where, frame != null ? frame.Id : 0));
        }

        static void Convert(PerceptionClient.ReadResponse res, int n, out int[] colours, out float[] conf, out ImagePoint[] where)
        {
            colours = new int[n]; conf = new float[n]; where = new ImagePoint[n];
            for (int i = 0; i < n; i++)
            {
                var it = res != null && res.items != null && i < res.items.Length ? res.items[i] : null;
                if (it == null) { colours[i] = WireColours.None; continue; }
                colours[i] = it.colour >= 0 && it.colour < WireColours.Names.Length ? it.colour : WireColours.None;
                conf[i] = Mathf.Clamp01(it.confidence);
                if (it.found) where[i] = new ImagePoint(Mathf.Clamp01(it.x), Mathf.Clamp01(it.y));
            }
        }

        static void Warn(string e) { Debug.LogWarning("[InstallerAssistant] Wire reading failed: " + e); }
    }
}
