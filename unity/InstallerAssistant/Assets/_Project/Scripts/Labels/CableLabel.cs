using System;
using System.Collections;

namespace InstallerAssistant.Labels
{
    /// <summary>Which end of the cable a label is fixed to.</summary>
    public enum CableEnd { Unknown, Device, Controller }

    /// <summary>
    /// The cable label. Both ends carry the same cable identity; each end also says which end it is,
    /// so the installer never has to know or remember it:
    ///   QR payload  "IA|D1|READER|C01|DEV"   (prefix, door, usage, cable id, end)
    ///   printed     "D1 · READER · C01 · DEV"
    /// The usage selects the wiring rule; the key (door, usage, id) pairs the two ends.
    /// The end field is optional: a label without it parses with End = Unknown, and the app then asks.
    /// </summary>
    public sealed class CableLabel
    {
        public const string Prefix = "IA";
        public readonly string Door;
        public readonly string Usage;
        public readonly string CableId;
        public readonly CableEnd End;

        public CableLabel(string door, string usage, string cableId, CableEnd end = CableEnd.Unknown)
        {
            Door = door; Usage = usage; CableId = cableId; End = end;
        }

        /// <summary>The same cable, seen from the given end.</summary>
        public CableLabel AtEnd(CableEnd end) { return new CableLabel(Door, Usage, CableId, end); }

        /// <summary>Identity of the cable, the same at both ends: this is what pairs the two scans.</summary>
        public string Key { get { return Door + "|" + Usage + "|" + CableId; } }
        public string Payload { get { return Prefix + "|" + Key + (End == CableEnd.Unknown ? "" : "|" + EndCode(End)); } }
        public string Display { get { return Door + " · " + Usage + " · " + CableId; } }
        public override string ToString() { return Display; }

        /// <summary>True if the two labels are the two ends of one cable.</summary>
        public bool Pairs(CableLabel other)
        {
            return other != null && other.Key == Key && (End == CableEnd.Unknown || other.End == CableEnd.Unknown || other.End != End);
        }

        /// <summary>"D1" -> "Door 1". Other door codes are shown as printed.</summary>
        public string DoorName
        {
            get
            {
                int n;
                if (Door.Length > 1 && (Door[0] == 'D' || Door[0] == 'd') && int.TryParse(Door.Substring(1), out n)) return "Door " + n;
                return Door;
            }
        }

        public static string EndCode(CableEnd end) { return end == CableEnd.Device ? "DEV" : end == CableEnd.Controller ? "CTRL" : ""; }

        static CableEnd ParseEnd(string s)
        {
            switch (s.Trim().ToUpperInvariant())
            {
                case "DEV": case "DEVICE": case "D": return CableEnd.Device;
                case "CTRL": case "CONTROLLER": case "C": return CableEnd.Controller;
                default: return (CableEnd)(-1);
            }
        }

        /// <summary>
        /// Accepts "IA|D1|READER|C01", "IA|D1|READER|C01|CTRL", "D1 · READER · C01" or "D1 · READER · C01 · DEV".
        /// </summary>
        public static bool TryParse(string text, out CableLabel label)
        {
            label = null;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split(new[] { '|', '·' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            int start = parts.Length > 0 && parts[0] == Prefix ? 1 : 0;
            int n = parts.Length - start;
            if (n != 3 && n != 4) return false;
            for (int i = start; i < parts.Length; i++) if (parts[i].Length == 0) return false;
            var end = CableEnd.Unknown;
            if (n == 4)
            {
                end = ParseEnd(parts[start + 3]);
                if ((int)end < 0) return false;
            }
            label = new CableLabel(parts[start], parts[start + 1].ToUpperInvariant(), parts[start + 2], end);
            return true;
        }
    }

    public enum LabelScanStatus { Found, NotFound, Unreadable }

    public sealed class LabelScan
    {
        public LabelScanStatus Status;
        public CableLabel Label;
    }

    /// <summary>
    /// Finds and decodes the cable label in the camera view.
    /// Real implementation: decode QR codes from the AR camera image (e.g. ZXing.Net on the CPU image).
    /// Wizard-of-Oz implementation: returns the scripted label after a delay.
    /// </summary>
    public interface ILabelScanner
    {
        IEnumerator Scan(Action<LabelScan> done);
    }
}
