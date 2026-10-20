using System;

namespace InstallerAssistant.Labels
{
    /// <summary>Which end of the cable a label is fixed to.</summary>
    public enum CableEnd { Unknown, Device, Controller }

    /// <summary>
    /// One module (terminal block) of one device, as written on a label: the device code, a dot and the module code,
    /// e.g. "A1610.READER2". A device with a single terminal block is written without a module: "A4020E".
    /// Codes are upper case letters, digits, '-' and '_'.
    /// </summary>
    public struct ModuleRef : IEquatable<ModuleRef>
    {
        /// <summary>Device code, e.g. "A1610" or "A4020E".</summary>
        public readonly string Device;
        /// <summary>Module code on that device, e.g. "READER2"; empty when the device has one terminal block.</summary>
        public readonly string Module;

        public ModuleRef(string device, string module = "")
        {
            Device = (device ?? "").Trim().ToUpperInvariant();
            Module = (module ?? "").Trim().ToUpperInvariant();
        }

        public bool IsEmpty { get { return string.IsNullOrEmpty(Device); } }

        /// <summary>As written on the label: "A1610.READER2", or "A4020E".</summary>
        public string Code { get { return IsEmpty ? "" : string.IsNullOrEmpty(Module) ? Device : Device + "." + Module; } }

        public override string ToString() { return Code; }
        public bool Equals(ModuleRef other) { return Code == other.Code; }
        public override bool Equals(object obj) { return obj is ModuleRef && Equals((ModuleRef)obj); }
        public override int GetHashCode() { return Code.GetHashCode(); }

        /// <summary>Parses "A1610.READER2" or "A4020E" (case does not matter).</summary>
        public static bool TryParse(string text, out ModuleRef module)
        {
            module = new ModuleRef();
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Trim().Split('.');
            if (parts.Length > 2) return false;
            foreach (var p in parts) if (!IsCode(p)) return false;
            module = new ModuleRef(parts[0], parts.Length == 2 ? parts[1] : "");
            return true;
        }

        static bool IsCode(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return false;
            return true;
        }
    }

    /// <summary>
    /// The cable label: the plan for one cable, printed as a QR code at both ends.
    ///   QR payload  "IA|C01|D1|A1610.READER2|A4020E|CTRL"
    ///               prefix | cable id | door | controller module | device module | this end (CTRL or DEV)
    ///   printed     "C01 · D1 · A1610.READER2 · A4020E · CTRL" (the same fields; the parser accepts both)
    ///
    /// The label says where the cable goes: from which module of the controller to which module of the device,
    /// always in that order. The app checks the installation against this plan and never guesses the module from
    /// the door, since the plan may put Door 1's reader on Reader 2. The door is for people only ("-" = none).
    /// Both labels of one cable carry the same plan and differ only in the last field. The key (id, door and both
    /// modules) pairs the two ends. The end field is optional: without it the app asks which end the installer is at.
    /// The label is the plan: a wrongly printed label makes the app check against the wrong plan (an assumption
    /// listed in docs/design-rationale.md).
    /// </summary>
    public sealed class CableLabel
    {
        public const string Prefix = "IA";
        /// <summary>Cable id, unique on the site, e.g. "C01".</summary>
        public readonly string CableId;
        /// <summary>Door the cable serves, e.g. "D1"; "-" when none. Shown to people only, never used by the logic.</summary>
        public readonly string Door;
        /// <summary>Where the cable lands on the controller, e.g. A1610.READER2.</summary>
        public readonly ModuleRef ControllerModule;
        /// <summary>Where the cable lands on the field device, e.g. A4020E.</summary>
        public readonly ModuleRef DeviceModule;
        public readonly CableEnd End;

        public CableLabel(string cableId, string door, ModuleRef controllerModule, ModuleRef deviceModule, CableEnd end = CableEnd.Unknown)
        {
            CableId = (cableId ?? "").Trim();
            Door = string.IsNullOrEmpty(door) ? "-" : door.Trim();
            ControllerModule = controllerModule; DeviceModule = deviceModule; End = end;
        }

        /// <summary>The same cable, seen from the given end.</summary>
        public CableLabel AtEnd(CableEnd end) { return new CableLabel(CableId, Door, ControllerModule, DeviceModule, end); }

        /// <summary>The module the given end lands on.</summary>
        public ModuleRef ModuleAt(CableEnd end)
        {
            return end == CableEnd.Controller ? ControllerModule : end == CableEnd.Device ? DeviceModule : new ModuleRef();
        }

        /// <summary>Identity of the cable and its plan, the same at both ends: this is what pairs the two scans.</summary>
        public string Key { get { return CableId + "|" + Door + "|" + ControllerModule.Code + "|" + DeviceModule.Code; } }
        public string Payload { get { return Prefix + "|" + Key + (End == CableEnd.Unknown ? "" : "|" + EndCode(End)); } }
        /// <summary>Short name for people: "C01 · D1", or "C01" without a door.</summary>
        public string Display { get { return HasDoor ? CableId + " · " + Door : CableId; } }
        /// <summary>The plan in label codes: "A1610.READER2 → A4020E".</summary>
        public string Route { get { return ControllerModule.Code + " → " + DeviceModule.Code; } }
        public override string ToString() { return Display; }

        public bool HasDoor { get { return Door != "-"; } }

        /// <summary>True if the two labels are the two ends of one cable: same id, door and plan, and not the same end.</summary>
        public bool Pairs(CableLabel other)
        {
            return other != null && other.Key == Key && (End == CableEnd.Unknown || other.End == CableEnd.Unknown || other.End != End);
        }

        /// <summary>True if the two labels name the same cable (id and door) but a different plan: a misprint.</summary>
        public bool SameCableOtherPlan(CableLabel other)
        {
            return other != null && other.CableId == CableId && other.Door == Door && other.Key != Key;
        }

        /// <summary>"D1" -> "Door 1"; other door codes as printed; "" when there is no door.</summary>
        public string DoorName
        {
            get
            {
                if (!HasDoor) return "";
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
        /// Accepts "IA|C01|D1|A1610.READER2|A4020E", the same with "|CTRL" or "|DEV" at the end, and the printed
        /// form with "·" between the fields. Anything else (including the old "IA|D1|READER|C01" format) is rejected.
        /// </summary>
        public static bool TryParse(string text, out CableLabel label)
        {
            label = null;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split(new[] { '|', '·' });
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            int start = parts.Length > 0 && parts[0].ToUpperInvariant() == Prefix ? 1 : 0;
            int n = parts.Length - start;
            if (n != 4 && n != 5) return false;
            for (int i = start; i < parts.Length; i++) if (parts[i].Length == 0) return false;
            ModuleRef ctrl, dev;
            if (!ModuleRef.TryParse(parts[start + 2], out ctrl) || !ModuleRef.TryParse(parts[start + 3], out dev)) return false;
            if (string.IsNullOrEmpty(ctrl.Module)) return false;   // a controller has several modules: the label must name one
            var end = CableEnd.Unknown;
            if (n == 5)
            {
                end = ParseEnd(parts[start + 4]);
                if ((int)end < 0) return false;
            }
            label = new CableLabel(parts[start], parts[start + 1], ctrl, dev, end);
            return true;
        }
    }
}
