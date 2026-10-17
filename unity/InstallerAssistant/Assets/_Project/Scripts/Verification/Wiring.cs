using System;
using InstallerAssistant.Labels;

namespace InstallerAssistant.Verification
{
    /// <summary>Wire insulation colours. Codes 0-7 are used in configs; -1 means "no wire".</summary>
    public static class WireColours
    {
        public const int None = -1;
        public static readonly string[] Names = { "Red", "Black", "Green", "Yellow", "White", "Brown", "Blue", "Orange" };

        public static string Name(int c) { return c < 0 || c >= Names.Length ? "No wire" : Names[c]; }
        public static string Lower(int c) { return c < 0 || c >= Names.Length ? "no wire" : Names[c].ToLowerInvariant(); }
    }

    /// <summary>
    /// What the wire reader saw at the device end: one colour per device terminal, in the rule's terminal order.
    /// Confidence is 0-1 per terminal.
    /// </summary>
    public sealed class DeviceEndReading
    {
        public readonly int[] Colours;
        public readonly float[] Confidence;
        public DeviceEndReading(int[] colours, float[] confidence) { Colours = colours; Confidence = confidence; }
    }

    /// <summary>What the wire reader saw at the controller: one colour (or None) per pin of the block, index = pin - 1.</summary>
    public sealed class ControllerReading
    {
        public readonly int[] Colours;
        public readonly float[] Confidence;
        public ControllerReading(int[] colours, float[] confidence) { Colours = colours; Confidence = confidence; }
        public int ColourOnPin(int pin) { return Colours[pin - 1]; }
        public float ConfidenceOnPin(int pin) { return Confidence[pin - 1]; }
    }

    /// <summary>
    /// The confirmed state of the device (reader) end of one cable. It, not any colour convention,
    /// defines what the controller end must look like. It may be recorded first or second.
    /// </summary>
    public sealed class CableRecord
    {
        public readonly CableLabel Label;
        public readonly UsageRule Rule;
        /// <summary>Confirmed colour on each device terminal, in the rule's terminal order.</summary>
        public readonly int[] Colours;
        /// <summary>True where the installer corrected the AI reading.</summary>
        public readonly bool[] SetByInstaller;
        public readonly DateTime SavedAt;

        public CableRecord(CableLabel label, UsageRule rule, int[] colours, bool[] setByInstaller, DateTime savedAt)
        {
            if (colours.Length != rule.DeviceTerminals.Length) throw new ArgumentException("one colour per device terminal");
            Label = label; Rule = rule; Colours = colours; SetByInstaller = setByInstaller; SavedAt = savedAt;
        }

        public int ColourOn(string terminal)
        {
            int i = Rule.TerminalIndex(terminal);
            return i < 0 ? WireColours.None : Colours[i];
        }

        /// <summary>Colour that must be on a controller pin, or None if the pin must stay empty.</summary>
        public int ExpectedOnPin(int pin)
        {
            string t = Rule.TerminalFor(pin);
            return t == null ? WireColours.None : ColourOn(t);
        }

        /// <summary>Device terminal that carries this colour in the record, or null.</summary>
        public string TerminalWithColour(int colour)
        {
            if (colour < 0) return null;
            for (int i = 0; i < Colours.Length; i++) if (Colours[i] == colour) return Rule.DeviceTerminals[i].Id;
            return null;
        }

        /// <summary>The design assumes distinct colours inside one cable. Returns false if the record breaks that.</summary>
        public bool ColoursAreDistinct()
        {
            for (int i = 0; i < Colours.Length; i++)
                for (int j = i + 1; j < Colours.Length; j++)
                    if (Colours[i] == Colours[j]) return false;
            return true;
        }
    }

    /// <summary>
    /// The confirmed state of the controller end of one cable, when the installer starts there:
    /// the colour on each pin of the block (index = pin - 1, None = empty).
    /// The check runs when the device end is recorded too.
    /// </summary>
    public sealed class ControllerRecord
    {
        public readonly CableLabel Label;
        public readonly UsageRule Rule;
        public readonly int[] PinColours;
        /// <summary>Reading confidence per pin; 1 where the installer confirmed or corrected it.</summary>
        public readonly float[] Confidence;
        public readonly bool[] SetByInstaller;
        public readonly DateTime SavedAt;

        public ControllerRecord(CableLabel label, UsageRule rule, int[] pinColours, float[] confidence, bool[] setByInstaller, DateTime savedAt)
        {
            if (pinColours.Length != rule.Connector.Count) throw new ArgumentException("one colour per controller pin");
            Label = label; Rule = rule; PinColours = pinColours; Confidence = confidence; SetByInstaller = setByInstaller; SavedAt = savedAt;
        }

        /// <summary>The saved controller end as a reading, so the same rule check runs whichever end came first.</summary>
        public ControllerReading AsReading() { return new ControllerReading((int[])PinColours.Clone(), (float[])Confidence.Clone()); }

        /// <summary>Pin that holds this colour, or 0.</summary>
        public int PinWithColour(int colour)
        {
            if (colour < 0) return 0;
            for (int i = 0; i < PinColours.Length; i++) if (PinColours[i] == colour) return i + 1;
            return 0;
        }
    }
}
