using System;
using System.Collections.Generic;
using System.IO;
using InstallerAssistant.Labels;
using InstallerAssistant.Verification;
using UnityEngine;

namespace InstallerAssistant.Records
{
    /// <summary>
    /// Both ends of every cable, kept in one JSON file on the phone, so the other end can be scanned on another
    /// day or after the app was closed. Owner: package C (App, records and UI).
    ///
    /// Works now. Next steps for C: one file per site (from a site QR code or a setting), the end snapshots as
    /// JPEG files next to it, and an export of the report (CSV/PDF) with the session log.
    /// </summary>
    public sealed class JsonRecordStore : IRecordStore
    {
        [Serializable]
        sealed class DeviceEntry
        {
            public string label;          // payload, e.g. "IA|C01|D1|A1610.READER1|A4020E|DEV"
            public int[] colours;
            public bool[] setByInstaller;
            public long savedAtTicks;
        }

        [Serializable]
        sealed class ControllerEntry
        {
            public string label;
            public int[] pinColours;
            public float[] confidence;
            public bool[] setByInstaller;
            public long savedAtTicks;
        }

        [Serializable]
        sealed class FileData
        {
            public int version = 1;
            public List<DeviceEntry> deviceEnds = new List<DeviceEntry>();
            public List<ControllerEntry> controllerEnds = new List<ControllerEntry>();
        }

        readonly string path;
        readonly Dictionary<string, CableRecord> devices = new Dictionary<string, CableRecord>();
        readonly Dictionary<string, ControllerRecord> controllers = new Dictionary<string, ControllerRecord>();

        public JsonRecordStore(string path)
        {
            this.path = path;
            Load();
        }

        public string FilePath { get { return path; } }

        public void Save(CableRecord deviceEnd) { devices[deviceEnd.Label.Key] = deviceEnd; Write(); }
        public void Save(ControllerRecord controllerEnd) { controllers[controllerEnd.Label.Key] = controllerEnd; Write(); }
        public bool TryGet(string cableKey, out CableRecord deviceEnd) { return devices.TryGetValue(cableKey, out deviceEnd); }
        public bool TryGet(string cableKey, out ControllerRecord controllerEnd) { return controllers.TryGetValue(cableKey, out controllerEnd); }

        public void Clear()
        {
            devices.Clear();
            controllers.Clear();
            Write();
        }

        void Load()
        {
            try
            {
                if (!File.Exists(path)) return;
                var data = JsonUtility.FromJson<FileData>(File.ReadAllText(path));
                if (data == null) return;
                foreach (var d in data.deviceEnds)
                {
                    CableLabel label; UsageRule rule;
                    if (!Parse(d.label, out label, out rule) || d.colours == null || d.colours.Length != rule.DeviceTerminals.Length) continue;
                    devices[label.Key] = new CableRecord(label, rule, d.colours,
                        d.setByInstaller != null && d.setByInstaller.Length == d.colours.Length ? d.setByInstaller : new bool[d.colours.Length], new DateTime(d.savedAtTicks));
                }
                foreach (var c in data.controllerEnds)
                {
                    CableLabel label; UsageRule rule;
                    if (!Parse(c.label, out label, out rule) || c.pinColours == null || c.pinColours.Length != rule.Connector.Count) continue;
                    int n = c.pinColours.Length;
                    controllers[label.Key] = new ControllerRecord(label, rule, c.pinColours,
                        c.confidence != null && c.confidence.Length == n ? c.confidence : new float[n],
                        c.setByInstaller != null && c.setByInstaller.Length == n ? c.setByInstaller : new bool[n], new DateTime(c.savedAtTicks));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[InstallerAssistant] Could not read the records file " + path + ": " + e.Message);
            }
        }

        static bool Parse(string payload, out CableLabel label, out UsageRule rule)
        {
            rule = null;
            if (!CableLabel.TryParse(payload, out label)) return false;
            rule = AxisCatalog.Resolve(label);   // records with an old or unknown label are skipped
            return rule != null;
        }

        void Write()
        {
            var data = new FileData();
            foreach (var d in devices.Values)
                data.deviceEnds.Add(new DeviceEntry { label = d.Label.Payload, colours = d.Colours, setByInstaller = d.SetByInstaller, savedAtTicks = d.SavedAt.Ticks });
            foreach (var c in controllers.Values)
                data.controllerEnds.Add(new ControllerEntry
                {
                    label = c.Label.Payload, pinColours = c.PinColours, confidence = c.Confidence, setByInstaller = c.SetByInstaller, savedAtTicks = c.SavedAt.Ticks,
                });
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);   // write-then-rename, so a crash never leaves half a file
            }
            catch (Exception e)
            {
                Debug.LogWarning("[InstallerAssistant] Could not write the records file " + path + ": " + e.Message);
            }
        }
    }
}
