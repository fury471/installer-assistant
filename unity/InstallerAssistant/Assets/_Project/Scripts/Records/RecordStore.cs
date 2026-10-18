using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using InstallerAssistant.Verification;

namespace InstallerAssistant.Records
{
    /// <summary>
    /// Keeps what was recorded at each end, keyed by the cable label, so the other end can be scanned
    /// later, possibly on another day, and in either order.
    /// InMemoryRecordStore: the Wizard of Oz and tests. JsonRecordStore: live, one JSON file on the phone.
    /// </summary>
    public interface IRecordStore
    {
        void Save(CableRecord deviceEnd);
        void Save(ControllerRecord controllerEnd);
        bool TryGet(string cableKey, out CableRecord deviceEnd);
        bool TryGet(string cableKey, out ControllerRecord controllerEnd);
        /// <summary>Forgets every record (a fresh site visit).</summary>
        void Clear();
    }

    public sealed class InMemoryRecordStore : IRecordStore
    {
        readonly Dictionary<string, CableRecord> devices = new Dictionary<string, CableRecord>();
        readonly Dictionary<string, ControllerRecord> controllers = new Dictionary<string, ControllerRecord>();
        public void Save(CableRecord deviceEnd) { devices[deviceEnd.Label.Key] = deviceEnd; }
        public void Save(ControllerRecord controllerEnd) { controllers[controllerEnd.Label.Key] = controllerEnd; }
        public bool TryGet(string cableKey, out CableRecord deviceEnd) { return devices.TryGetValue(cableKey, out deviceEnd); }
        public bool TryGet(string cableKey, out ControllerRecord controllerEnd) { return controllers.TryGetValue(cableKey, out controllerEnd); }
        public void Clear() { devices.Clear(); controllers.Clear(); }
    }

    /// <summary>
    /// Time-stamped interaction log for the user study (task time, errors shown, installer overrides).
    /// One row per event; written as CSV with the installation report.
    /// </summary>
    public sealed class SessionLog
    {
        public struct Entry
        {
            public double Seconds;
            public string Event;
            public string Detail;
        }

        readonly List<Entry> entries = new List<Entry>();
        readonly Func<double> clock;
        public readonly string SessionId;

        public SessionLog(Func<double> clock, string sessionId)
        {
            this.clock = clock;
            SessionId = sessionId;
        }

        public IList<Entry> Entries { get { return entries.AsReadOnly(); } }

        public void Add(string evt, string detail = "")
        {
            entries.Add(new Entry { Seconds = clock(), Event = evt, Detail = detail ?? "" });
        }

        public int Count(string evt)
        {
            int n = 0;
            foreach (var e in entries) if (e.Event == evt) n++;
            return n;
        }

        public string ToCsv()
        {
            var sb = new StringBuilder("session,seconds,event,detail\n");
            foreach (var e in entries)
                sb.Append(SessionId).Append(',')
                  .Append(e.Seconds.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.Event).Append(',')
                  .Append('"').Append(e.Detail.Replace("\"", "\"\"")).Append("\"\n");
            return sb.ToString();
        }
    }
}
