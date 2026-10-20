using System;
using System.Collections.Generic;
using InstallerAssistant.Labels;

namespace InstallerAssistant.Verification
{
    public enum BundleStatus
    {
        /// <summary>The wire reader did not follow the wires back (every trace Unknown): nothing to say.</summary>
        NotTraced,
        /// <summary>No wire was traced to another cable.</summary>
        ThisCable,
        /// <summary>At least one wire runs back to another cable, not to the scanned label.</summary>
        Foreign,
    }

    /// <summary>The bundle check's answer for one reading.</summary>
    public sealed class BundleResult
    {
        public BundleStatus Status;
        /// <summary>Wires that run to another cable, as reading indices (device end: terminal index; controller: pin - 1).</summary>
        public readonly List<int> Foreign = new List<int>();
        /// <summary>Wires the reader followed back to the scanned label.</summary>
        public int Traced;
        /// <summary>A foreign wire sits on a power terminal: take it off with the power off.</summary>
        public bool TouchesPower;
        /// <summary>Short imperative and full sentence, from the rules (never from the AI).</summary>
        public string Headline, Text;

        /// <summary>True when the installer must act, or say "they are this cable's", before the pins are checked.</summary>
        public bool NeedsAttention { get { return Status == BundleStatus.Foreign; } }
    }

    /// <summary>
    /// The bundle check: do the wires being checked belong to the scanned label? Runs after the zone check and before
    /// the pin check, at either end. Several cables often arrive at the same block, and different cables often share
    /// colours, so a wire of the right colour on the right pin may still belong to another cable; the colour check
    /// cannot see that. The wire reader therefore follows each wire from its terminal back to where it comes from
    /// (WireTrace in the readings): to the scanned label, to another cable, or unknown.
    ///
    /// It speaks only about wires traced to another cable. Unknown never counts against the wiring, so a reader that
    /// cannot trace (the Wizard of Oz, or a picture where the label is out of view) changes nothing. Pure and tested.
    /// </summary>
    public static class BundleCheck
    {
        public static BundleResult Check(UsageRule rule, CableLabel cable, DeviceEndReading reading)
        {
            return Check(reading != null ? reading.Colours : null, reading != null ? reading.Traces : null, cable,
                i => rule.DeviceTerminals[i].Print,
                i => rule.IsPowerPin(rule.PinFor(rule.DeviceTerminals[i].Id)),
                "the " + rule.Usage.ToLowerInvariant());
        }

        public static BundleResult Check(UsageRule rule, CableLabel cable, ControllerReading reading)
        {
            return Check(reading != null ? reading.Colours : null, reading != null ? reading.Traces : null, cable,
                i => rule.Connector.Pin(i + 1).Label,
                i => rule.IsPowerPin(i + 1),
                rule.Connector.Name);
        }

        static BundleResult Check(int[] colours, WireTrace[] traces, CableLabel cable, Func<int, string> name, Func<int, bool> power, string block)
        {
            var b = new BundleResult { Status = BundleStatus.NotTraced };
            if (colours == null || traces == null) return b;
            bool anyTraced = false;
            for (int i = 0; i < colours.Length && i < traces.Length; i++)
            {
                if (colours[i] == WireColours.None || traces[i] == WireTrace.Unknown) continue;
                anyTraced = true;
                if (traces[i] == WireTrace.ThisCable) b.Traced++;
                else { b.Foreign.Add(i); b.TouchesPower |= power(i); }
            }
            if (!anyTraced) return b;
            if (b.Foreign.Count == 0) { b.Status = BundleStatus.ThisCable; return b; }

            b.Status = BundleStatus.Foreign;
            string label = cable != null ? "label " + cable.Display : "the scanned label";
            string off = b.TouchesPower ? " with the power off" : "";
            if (b.Foreign.Count == 1)
            {
                int i = b.Foreign[0];
                b.Headline = "The wire on " + Short(name(i)) + " is from another cable";
                b.Text = "The " + WireColours.Lower(colours[i]) + " wire on " + name(i) + " does not run back to " + label +
                         ": it belongs to another cable. Take it off " + block + off + ", then try again. The check then shows which of this cable's wires goes there.";
            }
            else
            {
                var names = new List<string>();
                foreach (int i in b.Foreign) names.Add(name(i));
                b.Headline = b.Foreign.Count + " wires are from another cable";
                b.Text = "The wires on " + And(names) + " do not run back to " + label + ": they belong to another cable. Take them off " + block + off +
                         ", then try again.";
            }
            return b;
        }

        /// <summary>"12V (pin 2)" -> "12V".</summary>
        static string Short(string name) { int k = name.IndexOf(" ("); return k > 0 ? name.Substring(0, k) : name; }

        static string And(List<string> names)
        {
            if (names.Count == 1) return names[0];
            return string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " and " + names[names.Count - 1];
        }
    }
}
