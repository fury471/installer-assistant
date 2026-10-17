using System;

namespace InstallerAssistant.Verification
{
    /// <summary>One pin of a terminal block, named as it is printed on the board.</summary>
    public sealed class PinSpec
    {
        /// <summary>1-based pin number as in the Axis manual's connector table.</summary>
        public readonly int Number;
        /// <summary>Short name printed on the plug, e.g. "12V", "A", "O2".</summary>
        public readonly string Print;
        /// <summary>Full silkscreen text next to the pin, e.g. "B / D1".</summary>
        public readonly string Silk;
        /// <summary>What the pin does, from the manual.</summary>
        public readonly string Function;

        public PinSpec(int number, string print, string silk, string function)
        {
            Number = number; Print = print; Silk = silk; Function = function;
        }

        /// <summary>"B (pin 4)": how the app names a pin to the installer.</summary>
        public string Label { get { return Print + " (pin " + Number + ")"; } }
    }

    /// <summary>A terminal block on the controller, e.g. the blue 8-pin Reader 1 block.</summary>
    public sealed class ConnectorSpec
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Housing;
        public readonly PinSpec[] Pins;

        public ConnectorSpec(string id, string name, string housing, PinSpec[] pins)
        {
            Id = id; Name = name; Housing = housing; Pins = pins;
        }

        public int Count { get { return Pins.Length; } }

        public PinSpec Pin(int number)
        {
            if (number < 1 || number > Pins.Length) throw new ArgumentOutOfRangeException("number", number, Name + " has pins 1-" + Pins.Length);
            return Pins[number - 1];
        }
    }

    /// <summary>A terminal on the field device (the reader).</summary>
    public sealed class TerminalSpec
    {
        /// <summary>Stable id used in configs and pairings: "+", "-", "A", "B".</summary>
        public readonly string Id;
        /// <summary>As printed on the device, e.g. the minus sign.</summary>
        public readonly string Print;
        public readonly string Function;
        public TerminalSpec(string id, string print, string function) { Id = id; Print = print; Function = function; }
    }

    /// <summary>"Device terminal X goes to controller pin N", from the manuals.</summary>
    public sealed class Pairing
    {
        public readonly string DeviceTerminal;
        public readonly int ControllerPin;
        public Pairing(string deviceTerminal, int controllerPin) { DeviceTerminal = deviceTerminal; ControllerPin = controllerPin; }
    }

    /// <summary>
    /// Everything the app needs to know to judge one kind of cable ("usage"): which device,
    /// which controller block, which terminal goes to which pin, and the documented symptoms.
    /// The QR label names the usage; the rule is looked up here, never guessed.
    /// </summary>
    public sealed class UsageRule
    {
        public readonly string Usage;
        public readonly string Title;
        public readonly string DeviceModel;
        public readonly string ControllerModel;
        public readonly string Protocol;
        public readonly TerminalSpec[] DeviceTerminals;
        public readonly ConnectorSpec Connector;
        public readonly Pairing[] Pairs;
        /// <summary>Pins that carry power; moving wires on them needs power off first.</summary>
        public readonly int[] PowerPins;
        /// <summary>Pins that carry this usage's data signals.</summary>
        public readonly int[] DataPins;
        /// <summary>Which pins the protocol uses, shown in the fix card.</summary>
        public readonly string DataPinsNote;
        /// <summary>Why the data pair must not be swapped.</summary>
        public readonly string PolarityNote;
        /// <summary>What the installer will see on the device if the data wiring is wrong.</summary>
        public readonly string DataSymptom;
        /// <summary>What the installer will see if a power wire is wrong or missing.</summary>
        public readonly string PowerSymptom;
        /// <summary>Where the rule comes from.</summary>
        public readonly string Source;

        public UsageRule(string usage, string title, string deviceModel, string controllerModel, string protocol,
            TerminalSpec[] deviceTerminals, ConnectorSpec connector, Pairing[] pairs, int[] powerPins, int[] dataPins,
            string dataPinsNote, string polarityNote, string dataSymptom, string powerSymptom, string source)
        {
            Usage = usage; Title = title; DeviceModel = deviceModel; ControllerModel = controllerModel; Protocol = protocol;
            DeviceTerminals = deviceTerminals; Connector = connector; Pairs = pairs; PowerPins = powerPins; DataPins = dataPins;
            DataPinsNote = dataPinsNote; PolarityNote = polarityNote; DataSymptom = dataSymptom; PowerSymptom = powerSymptom; Source = source;
        }

        public int TerminalIndex(string terminal)
        {
            for (int i = 0; i < DeviceTerminals.Length; i++) if (DeviceTerminals[i].Id == terminal) return i;
            return -1;
        }

        /// <summary>Controller pin the given device terminal must land on.</summary>
        public int PinFor(string terminal)
        {
            foreach (var p in Pairs) if (p.DeviceTerminal == terminal) return p.ControllerPin;
            return -1;
        }

        /// <summary>Device terminal paired with this pin, or null if the pin must stay empty.</summary>
        public TerminalSpec Terminal(string id)
        {
            int i = TerminalIndex(id);
            return i < 0 ? null : DeviceTerminals[i];
        }

        public string TerminalFor(int pin)
        {
            foreach (var p in Pairs) if (p.ControllerPin == pin) return p.DeviceTerminal;
            return null;
        }

        public bool IsPowerPin(int pin) { return Array.IndexOf(PowerPins, pin) >= 0; }
        public bool IsDataPin(int pin) { return Array.IndexOf(DataPins, pin) >= 0; }
    }

    /// <summary>
    /// Device facts taken from the Axis documentation and checked against the team's photos of the lent kit.
    ///   AXIS A1610 Network Door Controller user manual, "Reader connector": two 8-pin blocks; pin 1 DC ground,
    ///   pin 2 +12 V DC out, pins 3-4 RX/TX (half duplex) or D0/D1 (Wiegand), pins 5-6 TX (full duplex only) or
    ///   open-drain outputs (Wiegand), pins 7-8 configurable I/O. Board print on Reader 1: "- 12V A B O1 O2 IO7 IO8",
    ///   silkscreen "A / D0", "B / D1", "A / O1", "B / O2".
    ///   AXIS A4020-E Reader user manual, wiring table: reader + to 12 V, - to -, A to A, B to B for Axis door
    ///   controllers other than the A1001 (which crosses A and B). LED stripe flashing red = "Waiting for controller connection".
    /// </summary>
    public static class AxisCatalog
    {
        public static readonly ConnectorSpec A1610Reader1 = ReaderBlock("A1610/READER1", "Reader 1", "blue 8-pin block", "O1", "O2", "IO7", "IO8");
        public static readonly ConnectorSpec A1610Reader2 = ReaderBlock("A1610/READER2", "Reader 2", "green 8-pin block", "O3", "O4", "IO9", "IO10");

        public static readonly ConnectorSpec A1610Door1 = new ConnectorSpec("A1610/DOOR1", "Door 1", "blue 4-pin block", new[]
        {
            new PinSpec(1, "\u2212", "\u2212", "Ground"),
            new PinSpec(2, "IO5", "IO5", "Door monitor input (digital or supervised)"),
            new PinSpec(3, "\u2212", "\u2212", "Ground"),
            new PinSpec(4, "IO6", "IO6", "Door monitor input (digital or supervised)"),
        });

        static ConnectorSpec ReaderBlock(string id, string name, string housing, string o1, string o2, string io1, string io2)
        {
            return new ConnectorSpec(id, name, housing, new[]
            {
                new PinSpec(1, "\u2212", "\u2212", "DC ground (0 V) for the reader"),
                new PinSpec(2, "12V", "+12V", "+12 V DC output that powers the reader"),
                new PinSpec(3, "A", "A / D0", "RS485 A, half-duplex data (Wiegand: D0)"),
                new PinSpec(4, "B", "B / D1", "RS485 B, half-duplex data (Wiegand: D1)"),
                new PinSpec(5, o1, "A / " + o1, "only used for full-duplex RS485 (TX A) or as a Wiegand output"),
                new PinSpec(6, o2, "B / " + o2, "only used for full-duplex RS485 (TX B) or as a Wiegand output"),
                new PinSpec(7, io1, io1, "Configurable input or output"),
                new PinSpec(8, io2, io2, "Configurable input or output"),
            });
        }

        /// <summary>Device terminal order used everywhere in the app: +, -, A, B.</summary>
        public static readonly TerminalSpec[] A4020Terminals =
        {
            new TerminalSpec("+", "+", "12 V DC in"),
            new TerminalSpec("-", "\u2212", "0 V"),
            new TerminalSpec("A", "A", "RS485 A"),
            new TerminalSpec("B", "B", "RS485 B"),
        };

        /// <summary>MVP usage: an OSDP reader (AXIS A4020-E) on the A1610's Reader 1 block, powered by the controller.</summary>
        public static readonly UsageRule OsdpReaderOnReader1 = new UsageRule(
            usage: "READER",
            title: "OSDP reader on Reader 1",
            deviceModel: "AXIS A4020-E Reader",
            controllerModel: "AXIS A1610 Network Door Controller",
            protocol: "OSDP over RS485, half duplex",
            deviceTerminals: A4020Terminals,
            connector: A1610Reader1,
            pairs: new[] { new Pairing("+", 2), new Pairing("-", 1), new Pairing("A", 3), new Pairing("B", 4) },
            powerPins: new[] { 1, 2 },
            dataPins: new[] { 3, 4 },
            dataPinsNote: "OSDP uses half-duplex RS485 on A (pin 3) and B (pin 4) only.",
            polarityNote: "RS485 is polarity-sensitive: the reader's A must meet the controller's A, and B must meet B.",
            dataSymptom: "When powered, the reader's LED keeps flashing red: \"waiting for controller connection\".",
            powerSymptom: "The reader will not power up correctly. Fix this before powering the controller.",
            source: "AXIS A1610 user manual (Reader connector) · AXIS A4020-E user manual (wiring table, LED)");

        public static readonly UsageRule[] Rules = { OsdpReaderOnReader1 };

        /// <summary>The rule for a label's usage, or null if the app does not know it.</summary>
        public static UsageRule Lookup(string usage)
        {
            foreach (var r in Rules) if (string.Equals(r.Usage, usage, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }
    }
}
