using System;
using System.Collections.Generic;
using InstallerAssistant.Labels;

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

    /// <summary>
    /// A module (terminal block) on the controller, e.g. the blue 8-pin Reader 1 block. Labels name it as
    /// "A1610.READER1" (Id); Kind says which wiring rules can use it (both reader ports share one kind).
    /// </summary>
    public sealed class ConnectorSpec
    {
        /// <summary>As written on labels: device code, dot, module code ("A1610.READER1").</summary>
        public readonly string Id;
        public readonly string Name;
        public readonly string Housing;
        /// <summary>What the module is for, e.g. "reader-port"; wiring templates match on it.</summary>
        public readonly string Kind;
        public readonly PinSpec[] Pins;

        public ConnectorSpec(string id, string name, string housing, string kind, PinSpec[] pins)
        {
            Id = id; Name = name; Housing = housing; Kind = kind; Pins = pins;
        }

        public int Count { get { return Pins.Length; } }

        /// <summary>The module code alone ("READER1").</summary>
        public string Code { get { int i = Id.IndexOf('.'); return i < 0 ? Id : Id.Substring(i + 1); } }

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
    /// A device model the app knows. A controller has several modules (its terminal blocks); a field device here has
    /// one set of terminals, so labels name it without a module ("A4020E").
    /// </summary>
    public sealed class DeviceSpec
    {
        /// <summary>As written on labels, e.g. "A1610".</summary>
        public readonly string Code;
        public readonly string Model;
        public readonly bool IsController;
        /// <summary>Controller: its modules. Empty for a field device.</summary>
        public readonly ConnectorSpec[] Modules;
        /// <summary>Field device: its terminals, in the order used everywhere in the app. Empty for a controller.</summary>
        public readonly TerminalSpec[] Terminals;

        public DeviceSpec(string code, string model, bool isController, ConnectorSpec[] modules, TerminalSpec[] terminals)
        {
            Code = code; Model = model; IsController = isController;
            Modules = modules ?? new ConnectorSpec[0]; Terminals = terminals ?? new TerminalSpec[0];
        }

        /// <summary>The module with this code ("READER2"), or null.</summary>
        public ConnectorSpec Module(string code)
        {
            foreach (var m in Modules) if (string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        public string ModuleCodes
        {
            get
            {
                var codes = new string[Modules.Length];
                for (int i = 0; i < codes.Length; i++) codes[i] = Modules[i].Code;
                return string.Join(", ", codes);
            }
        }
    }

    /// <summary>
    /// What the manuals say about one kind of cable, independent of which module it lands on: an OSDP reader on any
    /// reader port of the A1610 is wired the same way, on Reader 1 or Reader 2. The label names the modules, and
    /// AxisCatalog.Resolve binds a template to them, giving the UsageRule for that cable.
    /// Each template is extracted from the documentation of exactly one use case and names it in Source; a new use
    /// case (a lock on a Relay connector, a door monitor on a Door connector) gets its own template from its own section.
    /// </summary>
    public sealed class WiringTemplate
    {
        /// <summary>Kind of cable, e.g. "READER"; also the word the app uses for the device end.</summary>
        public readonly string Usage;
        public readonly string Title;
        /// <summary>Controller module kind this template fits, e.g. "reader-port".</summary>
        public readonly string ControllerKind;
        /// <summary>Field device this template fits, e.g. "A4020E".</summary>
        public readonly string DeviceCode;
        public readonly string Protocol;
        public readonly Pairing[] Pairs;
        public readonly int[] PowerPins;
        public readonly int[] DataPins;
        public readonly string DataPinsNote;
        public readonly string PolarityNote;
        public readonly string DataSymptom;
        public readonly string PowerSymptom;
        public readonly string Source;

        public WiringTemplate(string usage, string title, string controllerKind, string deviceCode, string protocol, Pairing[] pairs,
            int[] powerPins, int[] dataPins, string dataPinsNote, string polarityNote, string dataSymptom, string powerSymptom, string source)
        {
            Usage = usage; Title = title; ControllerKind = controllerKind; DeviceCode = deviceCode; Protocol = protocol; Pairs = pairs;
            PowerPins = powerPins; DataPins = dataPins; DataPinsNote = dataPinsNote; PolarityNote = polarityNote;
            DataSymptom = dataSymptom; PowerSymptom = powerSymptom; Source = source;
        }
    }

    /// <summary>
    /// Everything the app needs to judge one cable: the wiring template bound to the modules its label names
    /// (which device, which controller module, which terminal goes to which pin, and the documented symptoms).
    /// Made by AxisCatalog.Resolve from the label, never guessed.
    /// </summary>
    public sealed class UsageRule
    {
        public readonly WiringTemplate Template;
        public readonly string Usage;
        public readonly string Title;
        public readonly string DeviceModel;
        public readonly string ControllerModel;
        public readonly string Protocol;
        public readonly TerminalSpec[] DeviceTerminals;
        /// <summary>The controller module the label names (e.g. Reader 2).</summary>
        public readonly ConnectorSpec Connector;
        public readonly ModuleRef ControllerModule;
        public readonly ModuleRef DeviceModule;
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

        public UsageRule(WiringTemplate template, DeviceSpec controller, ConnectorSpec module, DeviceSpec device)
        {
            Template = template;
            Usage = template.Usage; Title = template.Title + " on " + module.Name;
            DeviceModel = device.Model; ControllerModel = controller.Model; Protocol = template.Protocol;
            DeviceTerminals = device.Terminals; Connector = module;
            ControllerModule = new ModuleRef(controller.Code, module.Code); DeviceModule = new ModuleRef(device.Code);
            Pairs = template.Pairs; PowerPins = template.PowerPins; DataPins = template.DataPins;
            DataPinsNote = template.DataPinsNote; PolarityNote = template.PolarityNote;
            DataSymptom = template.DataSymptom; PowerSymptom = template.PowerSymptom; Source = template.Source;
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
    ///   AXIS A1610 Network Door Controller user manual, "Connectors": two 8-pin reader connectors, two 4-pin door
    ///   connectors, two relay connectors, an auxiliary, an external, a power and a backup power connector.
    ///   "Reader connector": pin 1 DC ground, pin 2 +12 V DC out, pins 3-4 RX/TX (half duplex) or D0/D1 (Wiegand),
    ///   pins 5-6 TX (full duplex only) or open-drain outputs (Wiegand), pins 7-8 configurable I/O. Board print on
    ///   Reader 1: "- 12V A B O1 O2 IO7 IO8", silkscreen "A / D0", "B / D1", "A / O1", "B / O2".
    ///   "Door connector": pins 1 and 3 DC ground, pins 2 and 4 inputs (digital or supervised).
    ///   AXIS A4020-E Reader user manual, wiring table: reader + to 12 V, - to -, A to A, B to B for Axis door
    ///   controllers other than the A1001 (which crosses A and B). LED stripe flashing red = "Waiting for controller connection".
    /// The catalogue lists the A1610 modules whose pins and print the team has checked (Reader 1, Reader 2, Door 1,
    /// Door 2; Door 2 print, top to bottom: "REX/IO12 GND DPS/IO11 GND"). Add the relays and the auxiliary connector
    /// the same way once photographed.
    /// </summary>
    public static class AxisCatalog
    {
        // resolved rules, so one plan always gives the same rule object (declared first: field initialisers below use it)
        static readonly Dictionary<string, UsageRule> resolved = new Dictionary<string, UsageRule>();

        public const string ReaderPort = "reader-port";
        public const string DoorPort = "door";

        public static readonly ConnectorSpec A1610Reader1 = ReaderBlock("A1610.READER1", "Reader 1", "blue 8-pin block", "O1", "O2", "IO7", "IO8");
        public static readonly ConnectorSpec A1610Reader2 = ReaderBlock("A1610.READER2", "Reader 2", "green 8-pin block", "O3", "O4", "IO9", "IO10");

        public static readonly ConnectorSpec A1610Door1 = new ConnectorSpec("A1610.DOOR1", "Door 1", "blue 4-pin block", DoorPort, new[]
        {
            new PinSpec(1, "\u2212", "\u2212", "Ground"),
            new PinSpec(2, "IO5", "IO5", "Door monitor input (digital or supervised)"),
            new PinSpec(3, "\u2212", "\u2212", "Ground"),
            new PinSpec(4, "IO6", "IO6", "Door monitor input (digital or supervised)"),
        });

        public static readonly ConnectorSpec A1610Door2 = new ConnectorSpec("A1610.DOOR2", "Door 2", "green 4-pin block", DoorPort, new[]
        {
            new PinSpec(1, "\u2212", "\u2212", "Ground"),
            new PinSpec(2, "IO11", "IO11", "Door monitor input (digital or supervised)"),
            new PinSpec(3, "\u2212", "\u2212", "Ground"),
            new PinSpec(4, "IO12", "IO12", "Door monitor input (digital or supervised)"),
        });

        static ConnectorSpec ReaderBlock(string id, string name, string housing, string o1, string o2, string io1, string io2)
        {
            return new ConnectorSpec(id, name, housing, ReaderPort, new[]
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

        public static readonly DeviceSpec A1610 = new DeviceSpec("A1610", "AXIS A1610 Network Door Controller", true,
            new[] { A1610Reader1, A1610Reader2, A1610Door1, A1610Door2 }, null);
        public static readonly DeviceSpec A4020E = new DeviceSpec("A4020E", "AXIS A4020-E Reader", false, null, A4020Terminals);
        public static readonly DeviceSpec[] Devices = { A1610, A4020E };

        /// <summary>An OSDP reader (AXIS A4020-E) on a reader port of the A1610, powered by the controller.</summary>
        public static readonly WiringTemplate OsdpReader = new WiringTemplate(
            usage: "READER",
            title: "OSDP reader",
            controllerKind: ReaderPort,
            deviceCode: "A4020E",
            protocol: "OSDP over RS485, half duplex",
            pairs: new[] { new Pairing("+", 2), new Pairing("-", 1), new Pairing("A", 3), new Pairing("B", 4) },
            powerPins: new[] { 1, 2 },
            dataPins: new[] { 3, 4 },
            dataPinsNote: "OSDP uses half-duplex RS485 on A (pin 3) and B (pin 4) only.",
            polarityNote: "RS485 is polarity-sensitive: the reader's A must meet the controller's A, and B must meet B.",
            dataSymptom: "When powered, the reader's LED keeps flashing red: \"waiting for controller connection\".",
            powerSymptom: "The reader will not power up correctly. Fix this before powering the controller.",
            source: "AXIS A1610 user manual (Reader connector) · AXIS A4020-E user manual (wiring table, LED)");

        public static readonly WiringTemplate[] Templates = { OsdpReader };

        /// <summary>The MVP cable: an A4020-E on Reader 1.</summary>
        public static readonly UsageRule OsdpReaderOnReader1 = Bind(OsdpReader, A1610, A1610Reader1, A4020E);
        /// <summary>The same cable on Reader 2.</summary>
        public static readonly UsageRule OsdpReaderOnReader2 = Bind(OsdpReader, A1610, A1610Reader2, A4020E);

        static UsageRule Bind(WiringTemplate t, DeviceSpec controller, ConnectorSpec module, DeviceSpec device)
        {
            string key = controller.Code + "." + module.Code + "|" + device.Code;
            UsageRule r;
            if (!resolved.TryGetValue(key, out r)) { r = new UsageRule(t, controller, module, device); resolved[key] = r; }
            return r;
        }

        /// <summary>The device with this label code ("A1610"), or null.</summary>
        public static DeviceSpec Device(string code)
        {
            foreach (var d in Devices) if (string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        /// <summary>The rule for the cable this label describes, or null if the app cannot check it.</summary>
        public static UsageRule Resolve(CableLabel label)
        {
            string problem;
            return Resolve(label, out problem);
        }

        /// <summary>The rule for the cable this label describes; null with a sentence for the installer if the app cannot check it.</summary>
        public static UsageRule Resolve(CableLabel label, out string problem)
        {
            problem = null;
            if (label == null) { problem = "No label."; return null; }
            return Resolve(label.ControllerModule, label.DeviceModule, out problem);
        }

        /// <summary>Binds the template that connects these two modules, e.g. A1610.READER2 and A4020E.</summary>
        public static UsageRule Resolve(ModuleRef controllerModule, ModuleRef deviceModule, out string problem)
        {
            problem = null;
            var controller = Device(controllerModule.Device);
            if (controller == null) { problem = "The app does not know the device \"" + controllerModule.Device + "\"."; return null; }
            if (!controller.IsController)
            {
                problem = controllerModule.Device + " is not a door controller. The label names the controller module first, then the device.";
                return null;
            }
            var module = controller.Module(controllerModule.Module);
            if (module == null)
            {
                problem = "The " + controller.Code + " has no module \"" + controllerModule.Module + "\". It has " + controller.ModuleCodes + ".";
                return null;
            }
            var device = Device(deviceModule.Device);
            if (device == null) { problem = "The app does not know the device \"" + deviceModule.Device + "\"."; return null; }
            if (device.IsController) { problem = deviceModule.Device + " is a controller. The label names the field device second."; return null; }
            if (!string.IsNullOrEmpty(deviceModule.Module))
            {
                problem = "The " + device.Code + " has one set of terminals: write it as \"" + device.Code + "\" on the label.";
                return null;
            }
            foreach (var t in Templates)
                if (t.ControllerKind == module.Kind && string.Equals(t.DeviceCode, device.Code, StringComparison.OrdinalIgnoreCase))
                    return Bind(t, controller, module, device);
            problem = "The app has no wiring rule for an " + device.Model + " on " + module.Name + " of the " + controller.Code + ".";
            return null;
        }

        /// <summary>
        /// Every other module of the rule's controller: the places the wire reader also looks, so the zone check can
        /// tell when the cable sits in another module than its label names.
        /// </summary>
        public static ConnectorSpec[] OtherModules(UsageRule rule)
        {
            var controller = Device(rule.ControllerModule.Device);
            var list = new List<ConnectorSpec>();
            if (controller != null) foreach (var m in controller.Modules) if (m != rule.Connector) list.Add(m);
            return list.ToArray();
        }

        /// <summary>The controller module with this label code ("A1610.READER2"), or null.</summary>
        public static ConnectorSpec Module(string code)
        {
            ModuleRef m;
            if (!ModuleRef.TryParse(code, out m)) return null;
            var d = Device(m.Device);
            return d != null ? d.Module(m.Module) : null;
        }
    }
}
