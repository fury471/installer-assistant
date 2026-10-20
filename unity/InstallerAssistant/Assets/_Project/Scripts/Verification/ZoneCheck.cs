using System.Collections.Generic;

namespace InstallerAssistant.Verification
{
    public enum ZoneStatus
    {
        /// <summary>The wire reader looked only at the module the label names, so there is nothing to compare.</summary>
        NotSurveyed,
        /// <summary>The named module holds wires: the pin check takes over.</summary>
        InPlace,
        /// <summary>The named module is not in the picture.</summary>
        NotInView,
        /// <summary>The named module is empty, and no other module holds wires that could be this cable.</summary>
        Empty,
        /// <summary>The named module is empty, and another module holds wires that may be this cable.</summary>
        Elsewhere,
    }

    /// <summary>The zone check's answer for one controller reading.</summary>
    public sealed class ZoneResult
    {
        public ZoneStatus Status;
        /// <summary>The module the label names.</summary>
        public ConnectorSpec Expected;
        /// <summary>Elsewhere: the one other module that holds wires; null when several do.</summary>
        public ConnectorSpec Found;
        /// <summary>Elsewhere: every other module with wires that no other scanned label plans for.</summary>
        public readonly List<ModuleReading> Candidates = new List<ModuleReading>();
        /// <summary>Elsewhere: wires on the candidate modules.</summary>
        public int WiresFound;
        /// <summary>Elsewhere: every colour of the saved device end is on Found (a firmer hint, never proof).</summary>
        public bool ColoursMatch;
        /// <summary>Moving the cable moves power wires: do it with the power off.</summary>
        public bool TouchesPower;
        /// <summary>Short imperative and full sentence, from the rules (never from the AI).</summary>
        public string Headline, Text;

        /// <summary>True when the installer must act, or say "not this cable", before the pin check means anything.</summary>
        public bool NeedsAttention { get { return Status == ZoneStatus.Elsewhere || Status == ZoneStatus.NotInView; } }
    }

    /// <summary>
    /// The zone check, run at the controller before the pin check: is the cable in the module its label names?
    /// The label is the plan (CableLabel), so the expected module is known; the wire reader also reports the
    /// controller's other modules (ControllerReading.OtherModules). When the named module is empty but another one
    /// holds wires, the app says "move the cable to Reader 1" instead of listing four missing wires.
    ///
    /// It only speaks when the named module is empty or out of view. A module that holds wires goes to the pin check,
    /// which names every wrong pin. Different cables often share colours, so colours never prove which cable sits
    /// where: a colour match only makes the message firmer, and the installer can always answer "not this cable".
    /// Modules that other scanned labels plan for are left out. Pure and deterministic, so it is unit-tested.
    /// </summary>
    public static class ZoneCheck
    {
        /// <param name="deviceEnd">The saved device end, if any: its colours make the message firmer.</param>
        /// <param name="claimedByOthers">Module ids (e.g. "A1610.READER2") that other cables' labels plan for.</param>
        public static ZoneResult Check(UsageRule rule, ControllerReading reading, CableRecord deviceEnd = null, ICollection<string> claimedByOthers = null)
        {
            var z = new ZoneResult { Expected = rule.Connector, TouchesPower = rule.PowerPins.Length > 0 };
            string exp = rule.Connector.Name;
            if (reading == null || reading.OtherModules.Length == 0) { z.Status = ZoneStatus.NotSurveyed; return z; }

            if (!reading.Located)
            {
                z.Status = ZoneStatus.NotInView;
                z.Headline = "Point the camera at " + exp;
                z.Text = "The label says this cable goes to " + exp + ", but the camera can't find it. Point the phone at " + exp +
                         " so the block fills the middle of the screen, then try again.";
                return z;
            }
            if (reading.Wires > 0) { z.Status = ZoneStatus.InPlace; return z; }

            foreach (var m in reading.OtherModules)
                if (m.Wires > 0 && (claimedByOthers == null || !claimedByOthers.Contains(m.Module.Id))) z.Candidates.Add(m);
            if (z.Candidates.Count == 0)
            {
                z.Status = ZoneStatus.Empty;
                z.Headline = exp + " is empty";
                z.Text = "No wires on " + exp + " yet.";
                return z;
            }

            z.Status = ZoneStatus.Elsewhere;
            string power = z.TouchesPower ? " Turn the power off first." : "";
            string lead = "The label says this cable goes to " + exp + ", but " + exp + " is empty. ";
            if (z.Candidates.Count == 1)
            {
                var m = z.Candidates[0];
                z.Found = m.Module;
                z.WiresFound = m.Wires;
                z.ColoursMatch = deviceEnd != null && HasAll(m.Colours, deviceEnd.Colours);
                string wires = m.Wires == 1 ? "1 wire" : m.Wires + " wires";
                if (z.ColoursMatch)
                {
                    z.Headline = "Move the cable to " + exp;
                    z.Text = lead + m.Module.Name + " has " + wires + " in this cable's colours. Move them to " + exp + "." + power + " Then check again.";
                }
                else
                {
                    z.Headline = "Is this cable on " + m.Module.Name + "?";
                    z.Text = lead + m.Module.Name + " has " + wires + ". If they are this cable's, move them to " + exp + "." + power + " Then check again.";
                }
            }
            else
            {
                var names = new List<string>();
                foreach (var m in z.Candidates) { names.Add(m.Module.Name); z.WiresFound += m.Wires; }
                z.Headline = exp + " is empty";
                z.Text = lead + "Wires were found on " + And(names) + ". Move this cable's wires to " + exp + "." + power + " Then check again.";
            }
            return z;
        }

        /// <summary>True if every wire colour of 'wanted' is among 'found'.</summary>
        static bool HasAll(int[] found, int[] wanted)
        {
            var left = new List<int>(found);
            int n = 0;
            foreach (int c in wanted)
            {
                if (c == WireColours.None) continue;
                n++;
                if (!left.Remove(c)) return false;
            }
            return n > 0;
        }

        static string And(List<string> names)
        {
            if (names.Count == 1) return names[0];
            return string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " and " + names[names.Count - 1];
        }
    }
}
