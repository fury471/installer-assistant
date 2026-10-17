using System.Collections.Generic;

namespace InstallerAssistant.Verification
{
    public enum FixKind { Swap, Move, Connect, Remove }

    /// <summary>
    /// One physical action that repairs the wiring. Every red mark points to exactly one action, and
    /// the AR view draws it (a two-headed arrow for Swap, an arrow for Move). Text always comes from
    /// the rules, never from the AI, so it is exact.
    /// </summary>
    public sealed class FixAction
    {
        public FixKind Kind;
        public int Colour = WireColours.None;
        /// <summary>Second colour, for Swap.</summary>
        public int Colour2 = WireColours.None;
        public int FromPin;
        public int ToPin;
        /// <summary>Short imperative, e.g. "Swap green and yellow".</summary>
        public string Headline;
        /// <summary>Full instruction naming pins as printed on the board.</summary>
        public string Text;
        /// <summary>Why, from the manuals.</summary>
        public string Why;
        /// <summary>What the installer would see on the powered device.</summary>
        public string Symptom;
        public bool TouchesPower;

        public bool Involves(int pin) { return pin == FromPin || pin == ToPin; }
    }

    /// <summary>The rule-based verdict for one controller pin.</summary>
    public sealed class PinFinding
    {
        public int Pin;
        public int Expected = WireColours.None;
        public int Found = WireColours.None;
        public bool RulesOk { get { return Expected == Found; } }
        /// <summary>True if the pin matters to the installer: a wire is expected or present.</summary>
        public bool Relevant { get { return Expected != WireColours.None || Found != WireColours.None; } }
        /// <summary>Device terminal whose wire belongs here (null = pin must stay empty).</summary>
        public string Terminal;
        public FixAction Fix;
    }

    public sealed class RuleCheck
    {
        public readonly PinFinding[] Pins;
        public readonly List<FixAction> Fixes;
        public RuleCheck(PinFinding[] pins, List<FixAction> fixes) { Pins = pins; Fixes = fixes; }
        public PinFinding Pin(int pin) { return Pins[pin - 1]; }
        public bool AllCorrect { get { return Fixes.Count == 0; } }
    }

    /// <summary>
    /// Layer 2 of the check: compares the controller reading with the record through the usage's pairing rules
    /// and turns every difference into the smallest set of physical actions (swap, move, connect, remove).
    /// Pure and deterministic, so it can be unit-tested and explained.
    /// </summary>
    public static class RuleChecker
    {
        public static RuleCheck Check(CableRecord record, ControllerReading reading)
        {
            var rule = record.Rule;
            int n = rule.Connector.Count;
            var pins = new PinFinding[n];
            for (int p = 1; p <= n; p++)
                pins[p - 1] = new PinFinding
                {
                    Pin = p,
                    Expected = record.ExpectedOnPin(p),
                    Found = reading.ColourOnPin(p),
                    Terminal = rule.TerminalFor(p),
                };

            var fixes = new List<FixAction>();
            var handled = new bool[n + 1];

            // 1. pairs of pins that hold each other's wires -> one swap
            for (int p = 1; p <= n; p++)
            {
                var a = pins[p - 1];
                if (a.RulesOk || handled[p] || a.Found == WireColours.None || a.Expected == WireColours.None) continue;
                for (int q = p + 1; q <= n; q++)
                {
                    var b = pins[q - 1];
                    if (handled[q] || b.RulesOk) continue;
                    if (a.Found == b.Expected && b.Found == a.Expected)
                    {
                        fixes.Add(Swap(rule, a, b));
                        handled[p] = handled[q] = true;
                        break;
                    }
                }
            }

            // 2. a wire on a pin where it does not belong -> move it to its own pin, or remove it
            var targeted = new bool[n + 1];
            for (int p = 1; p <= n; p++)
            {
                var a = pins[p - 1];
                if (a.RulesOk || handled[p] || a.Found == WireColours.None) continue;
                int q = PinExpecting(pins, a.Found);
                if (q > 0 && pins[q - 1].Found != a.Found)
                {
                    fixes.Add(Move(rule, a.Found, p, q));
                    targeted[q] = true;
                }
                else fixes.Add(Remove(rule, a.Found, p));
                handled[p] = true;
            }

            // 3. a pin still missing its wire, with nothing moving onto it -> connect
            for (int p = 1; p <= n; p++)
            {
                var a = pins[p - 1];
                if (a.RulesOk || handled[p] || targeted[p] || a.Expected == WireColours.None) continue;
                fixes.Add(Connect(rule, a.Expected, p));
                handled[p] = true;
            }

            // link every pin to the action that repairs it (first action wins)
            foreach (var f in fixes)
                foreach (var a in pins)
                    if (!a.RulesOk && a.Fix == null && f.Involves(a.Pin)) a.Fix = f;

            return new RuleCheck(pins, fixes);
        }

        static int PinExpecting(PinFinding[] pins, int colour)
        {
            foreach (var x in pins) if (x.Expected == colour) return x.Pin;
            return -1;
        }

        static string P(UsageRule r, int pin) { return r.Connector.Pin(pin).Label; }
        static string C(int c) { return WireColours.Lower(c); }

        static FixAction Swap(UsageRule r, PinFinding a, PinFinding b)
        {
            var f = new FixAction
            {
                Kind = FixKind.Swap, Colour = a.Expected, Colour2 = b.Expected, FromPin = a.Pin, ToPin = b.Pin,
                Headline = "Swap " + C(a.Expected) + " and " + C(b.Expected),
                Text = "Swap the " + C(a.Expected) + " and " + C(b.Expected) + " wires: " + C(a.Expected) + " to " + P(r, a.Pin) +
                       ", " + C(b.Expected) + " to " + P(r, b.Pin) + ".",
            };
            Explain(r, f);
            return f;
        }

        static FixAction Move(UsageRule r, int colour, int from, int to)
        {
            var f = new FixAction
            {
                Kind = FixKind.Move, Colour = colour, FromPin = from, ToPin = to,
                Headline = "Move " + C(colour) + " to " + r.Connector.Pin(to).Print,
                Text = "Move the " + C(colour) + " wire from " + P(r, from) + " to " + P(r, to) + ".",
            };
            Explain(r, f);
            return f;
        }

        static FixAction Connect(UsageRule r, int colour, int to)
        {
            var f = new FixAction
            {
                Kind = FixKind.Connect, Colour = colour, FromPin = to, ToPin = to,
                Headline = "Connect " + C(colour) + " to " + r.Connector.Pin(to).Print,
                Text = "The " + C(colour) + " wire is not connected. Connect it to " + P(r, to) + ".",
            };
            Explain(r, f);
            return f;
        }

        static FixAction Remove(UsageRule r, int colour, int from)
        {
            var f = new FixAction
            {
                Kind = FixKind.Remove, Colour = colour, FromPin = from, ToPin = from,
                Headline = "Remove " + C(colour) + " from " + r.Connector.Pin(from).Print,
                Text = "The " + C(colour) + " wire on " + P(r, from) + " is not part of this cable's record. Remove it, or rescan the reader end.",
            };
            Explain(r, f);
            return f;
        }

        /// <summary>Adds the manual-based reason and the visible symptom for an action.</summary>
        static void Explain(UsageRule r, FixAction f)
        {
            var from = r.Connector.Pin(f.FromPin);
            var to = r.Connector.Pin(f.ToPin);
            bool power = r.IsPowerPin(f.FromPin) || r.IsPowerPin(f.ToPin);
            bool data = r.IsDataPin(f.FromPin) || r.IsDataPin(f.ToPin) || (!power && f.FromPin != f.ToPin);
            f.TouchesPower = power;

            string why;
            if (f.Kind == FixKind.Swap && r.IsDataPin(f.FromPin) && r.IsDataPin(f.ToPin))
                why = r.PolarityNote;
            else if (f.Kind == FixKind.Move && !r.IsDataPin(f.FromPin) && r.IsDataPin(f.ToPin))
                why = from.Label + " is " + Lower(from.Function) + ". " + r.DataPinsNote;
            else if (f.FromPin != f.ToPin)
                why = from.Label + " is " + Lower(from.Function) + "; " + to.Label + " is " + Lower(to.Function) + ".";
            else
                why = to.Label + " is " + Lower(to.Function) + ".";
            f.Why = why;
            f.Symptom = power ? r.PowerSymptom : data ? r.DataSymptom : null;
        }

        /// <summary>Lower-cases the first letter unless the first word is an acronym ("RS485", "DC").</summary>
        static string Lower(string s)
        {
            if (string.IsNullOrEmpty(s) || (s.Length > 1 && !char.IsLower(s[1]))) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }
    }
}
