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
    /// A point in a camera frame, normalised to the frame: X 0..1 left to right, Y 0..1 top to bottom.
    /// Valid is false when the reader could not place the point.
    /// </summary>
    public struct ImagePoint
    {
        public float X, Y;
        public bool Valid;
        public ImagePoint(float x, float y) { X = x; Y = y; Valid = true; }
        public static readonly ImagePoint None = new ImagePoint();
        public override string ToString() { return Valid ? "(" + X.ToString("0.000") + ", " + Y.ToString("0.000") + ")" : "(none)"; }
    }

    /// <summary>
    /// What the wire reader saw at the device end, per device terminal in the rule's terminal order:
    /// the colour, a 0-1 confidence and where the terminal is in the frame that was read.
    /// </summary>
    public sealed class DeviceEndReading
    {
        public readonly int[] Colours;
        public readonly float[] Confidence;
        /// <summary>Terminal position in the frame (normalised); the AR runtime anchors markers here.</summary>
        public readonly ImagePoint[] Positions;
        /// <summary>Id of the camera frame the reading was made from (0 = unknown).</summary>
        public readonly long FrameId;

        public DeviceEndReading(int[] colours, float[] confidence, ImagePoint[] positions = null, long frameId = 0)
        {
            Colours = colours; Confidence = confidence;
            Positions = positions ?? new ImagePoint[colours.Length];
            FrameId = frameId;
        }
    }

    /// <summary>
    /// What the wire reader saw at the controller, per pin of the block (index = pin - 1):
    /// the colour (or None), a 0-1 confidence and where the pin is in the frame that was read.
    /// </summary>
    public sealed class ControllerReading
    {
        public readonly int[] Colours;
        public readonly float[] Confidence;
        /// <summary>Pin position in the frame (normalised); the AR runtime anchors markers here.</summary>
        public readonly ImagePoint[] Positions;
        /// <summary>Id of the camera frame the reading was made from (0 = unknown).</summary>
        public readonly long FrameId;

        public ControllerReading(int[] colours, float[] confidence, ImagePoint[] positions = null, long frameId = 0)
        {
            Colours = colours; Confidence = confidence;
            Positions = positions ?? new ImagePoint[colours.Length];
            FrameId = frameId;
        }

        public int ColourOnPin(int pin) { return Colours[pin - 1]; }
        public float ConfidenceOnPin(int pin) { return Confidence[pin - 1]; }
        public ImagePoint PositionOfPin(int pin) { return Positions[pin - 1]; }
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
