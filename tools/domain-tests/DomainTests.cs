// Unit tests for the Installer Assistant domain layer (Scripts/Verification, Labels, Records).
// The domain code has no Unity dependency, so it runs as a plain .NET console app:
//   dotnet run --project tools/domain-tests
// Exit code 0 = all passed. Add a case here whenever a new wiring rule or fault type is added.
using System;
using System.Linq;
using InstallerAssistant.Verification;
using InstallerAssistant.Labels;
using InstallerAssistant.Records;

static class T
{
    static int fails;
    static void Eq<X>(X a, X b, string what) { if (!Equals(a, b)) { fails++; Console.WriteLine("FAIL " + what + ": got " + a + ", want " + b); } else Console.WriteLine("ok   " + what); }
    const int R = 0, K = 1, G = 2, Y = 3, O = 7, N = -1;

    static CableRecord Rec(params int[] c)   // +, -, A, B
    {
        CableLabel l; CableLabel.TryParse("IA|C01|D1|A1610.READER1|A4020E", out l);
        return new CableRecord(l, AxisCatalog.OsdpReaderOnReader1, c, new bool[4], DateTime.Now);
    }
    /// <summary>The device-end record after the installer did every fix at the device end.</summary>
    static CableRecord FixedAtDevice(CableRecord r, RuleCheck check)
    {
        var c = (int[])r.Colours.Clone();
        foreach (var f in check.Fixes)
        {
            var rule = r.Rule;
            if (f.Kind == FixKind.Swap) { c[rule.TerminalIndex(f.DeviceFrom)] = f.Colour2; c[rule.TerminalIndex(f.DeviceTo)] = f.Colour; }
            else c[rule.TerminalIndex(f.DeviceTo)] = f.Colour;
        }
        return new CableRecord(r.Label, r.Rule, c, new bool[c.Length], DateTime.Now);
    }
    static ControllerReading Pins(params int[] c) { return new ControllerReading(c, Enumerable.Repeat(0.95f, c.Length).ToArray()); }
    static float[] Conf(int n) { return Enumerable.Repeat(0.95f, n).ToArray(); }
    static ImagePoint[] Row(int n, float y) { return Enumerable.Range(0, n).Select(i => new ImagePoint(0.2f + 0.05f * i, y)).ToArray(); }
    /// <summary>A controller reading of Reader 1 with the zone survey of Reader 2 and Door 1 in the same frame.</summary>
    static ControllerReading Survey(int[] reader1, int[] reader2, int[] door1, bool reader1InView = true)
    {
        var others = new[]
        {
            new ModuleReading(AxisCatalog.A1610Reader2, true, reader2, Conf(8), Row(8, 0.6f)),
            new ModuleReading(AxisCatalog.A1610Door1, true, door1, Conf(4), Row(4, 0.7f)),
        };
        return new ControllerReading(reader1, Conf(8), reader1InView ? Row(8, 0.5f) : null, 7, others);
    }

    static int Main()
    {
        // the label is the plan: from which controller module to which device module
        CableLabel lab;
        Eq(CableLabel.TryParse("IA|C01|D1|A1610.READER1|A4020E", out lab), true, "parse QR payload");
        Eq(lab.Display, "C01 · D1", "display text");
        Eq(lab.Route, "A1610.READER1 → A4020E", "route text");
        Eq(lab.ControllerModule.Code == "A1610.READER1" && lab.DeviceModule.Code == "A4020E" && lab.End == CableEnd.Unknown, true, "modules from the label");
        Eq(CableLabel.TryParse("C01 · D1 · a1610.reader2 · a4020e · ctrl", out lab) && lab.ControllerModule.Module == "READER2" && lab.End == CableEnd.Controller,
            true, "parse printed text, any case");
        Eq(CableLabel.TryParse("hello", out lab), false, "reject junk");
        Eq(CableLabel.TryParse("IA|D1|READER|C01", out lab) || CableLabel.TryParse("IA|D1|READER|C01|DEV", out lab), false, "reject the old label format");
        Eq(CableLabel.TryParse("IA|C01|D1|A1610|A4020E", out lab), false, "reject a controller without a module");
        Eq(CableLabel.TryParse("IA|C01|D1|A1610.READER1|A4020E|XYZ", out lab), false, "reject unknown end code");
        Eq(CableLabel.TryParse("IA|C01|-|A1610.READER1|A4020E", out lab) && lab.Display == "C01" && lab.DoorName == "", true, "a cable without a door");

        // the catalogue binds the template to the module the label names; the door plays no part
        string problem;
        CableLabel r1, r2, d1onR2;
        CableLabel.TryParse("IA|C01|D1|A1610.READER1|A4020E", out r1);
        CableLabel.TryParse("IA|C02|D2|A1610.READER2|A4020E", out r2);
        CableLabel.TryParse("IA|C03|D1|A1610.READER2|A4020E", out d1onR2);
        Eq(AxisCatalog.Resolve(r1) == AxisCatalog.OsdpReaderOnReader1, true, "Reader 1 label -> Reader 1 rule");
        Eq(AxisCatalog.Resolve(r2) == AxisCatalog.OsdpReaderOnReader2 && AxisCatalog.OsdpReaderOnReader2.Connector == AxisCatalog.A1610Reader2, true, "Reader 2 label -> Reader 2 rule");
        Eq(AxisCatalog.Resolve(d1onR2).Connector.Name, "Reader 2", "Door 1's reader planned on Reader 2 is checked on Reader 2");
        Eq(AxisCatalog.OsdpReaderOnReader2.Title, "OSDP reader on Reader 2", "rule title names the module");
        Eq(AxisCatalog.OsdpReaderOnReader2.ControllerModule.Code + " " + AxisCatalog.OsdpReaderOnReader2.DeviceModule.Code, "A1610.READER2 A4020E", "rule modules");
        CableLabel bad;
        CableLabel.TryParse("IA|C09|D1|A1610.READER9|A4020E", out bad);
        Eq(AxisCatalog.Resolve(bad, out problem) == null && problem.Contains("READER1, READER2, DOOR1, DOOR2"), true, "unknown module -> no rule, names the modules");
        Console.WriteLine("     " + problem);
        CableLabel.TryParse("IA|C09|D1|A4020E.X|A1610", out bad);
        Eq(AxisCatalog.Resolve(bad, out problem) == null, true, "device first -> no rule");
        Console.WriteLine("     " + problem);
        CableLabel.TryParse("IA|C09|D1|A1610.DOOR1|A4020E", out bad);
        Eq(AxisCatalog.Resolve(bad, out problem) == null, true, "reader on a door module -> no rule");
        Console.WriteLine("     " + problem);
        Eq(string.Join(" ", AxisCatalog.OtherModules(AxisCatalog.OsdpReaderOnReader1).Select(m => m.Id)), "A1610.READER2 A1610.DOOR1 A1610.DOOR2", "other modules to survey");

        var rec = Rec(O, K, G, Y);   // orange on +, black on -, green on A, yellow on B (team's Demo2 cable)
        Eq(rec.ExpectedOnPin(1), K, "pin 1 expects black (-)");
        Eq(rec.ExpectedOnPin(2), O, "pin 2 expects orange (+ -> 12V)");
        Eq(rec.ExpectedOnPin(6), N, "pin 6 must be empty");

        // correct
        var ok = RuleChecker.Check(rec, Pins(K, O, G, Y, N, N, N, N));
        Eq(ok.AllCorrect, true, "correct wiring has no fixes");

        // Case 1: A/B swapped
        var c1 = RuleChecker.Check(rec, Pins(K, O, Y, G, N, N, N, N));
        Eq(c1.Fixes.Count, 1, "A/B swap -> one action");
        Eq(c1.Fixes[0].Kind, FixKind.Swap, "action is a swap");
        Eq(c1.Fixes[0].Text, "Swap the green and yellow wires: green to A (pin 3), yellow to B (pin 4).", "swap text");
        Eq(c1.Pin(3).Fix == c1.Fixes[0] && c1.Pin(4).Fix == c1.Fixes[0], true, "both pins point at the swap");
        Eq(c1.Fixes[0].TouchesPower, false, "swap does not touch power");
        Console.WriteLine("     why: " + c1.Fixes[0].Why + "\n     symptom: " + c1.Fixes[0].Symptom);

        // Case 2: B wire on O2 (pin 6)
        var c2 = RuleChecker.Check(rec, Pins(K, O, G, N, N, Y, N, N));
        Eq(c2.Fixes.Count, 1, "wrong pin -> one action");
        Eq(c2.Fixes[0].Kind, FixKind.Move, "action is a move");
        Eq(c2.Fixes[0].Text, "Move the yellow wire from O2 (pin 6) to B (pin 4).", "move text");
        Eq(c2.Fixes[0].Headline, "Move yellow to B", "move headline");
        Eq(c2.Pin(4).Fix == c2.Fixes[0] && c2.Pin(6).Fix == c2.Fixes[0], true, "pins 4 and 6 point at the move");
        Console.WriteLine("     why: " + c2.Fixes[0].Why + "\n     symptom: " + c2.Fixes[0].Symptom);
        Eq(c2.Pins.Count(p => p.Relevant), 5, "relevant pins: 1,2,3,4,6");

        // power reversed
        var c3 = RuleChecker.Check(rec, Pins(O, K, G, Y, N, N, N, N));
        Eq(c3.Fixes.Count == 1 && c3.Fixes[0].Kind == FixKind.Swap && c3.Fixes[0].TouchesPower, true, "power reversed -> swap touching power");
        Console.WriteLine("     text: " + c3.Fixes[0].Text + " | why: " + c3.Fixes[0].Why + " | symptom: " + c3.Fixes[0].Symptom);

        // missing wire
        var c4 = RuleChecker.Check(rec, Pins(N, O, G, Y, N, N, N, N));
        Eq(c4.Fixes.Count == 1 && c4.Fixes[0].Kind == FixKind.Connect && c4.Fixes[0].ToPin == 1, true, "missing black -> connect to pin 1");
        Console.WriteLine("     text: " + c4.Fixes[0].Text);

        // foreign wire
        var c5 = RuleChecker.Check(rec, Pins(K, O, G, Y, N, N, 6, N));
        Eq(c5.Fixes.Count == 1 && c5.Fixes[0].Kind == FixKind.Remove, true, "blue on IO7 -> remove");
        Console.WriteLine("     text: " + c5.Fixes[0].Text);

        // three-way rotation: + on A, A on B, B on 12V
        var c6 = RuleChecker.Check(rec, Pins(K, Y, O, G, N, N, N, N));
        Console.WriteLine("     rotation -> " + string.Join(" / ", c6.Fixes.Select(f => f.Text)));
        Eq(c6.Pins.Where(p => !p.RulesOk).All(p => p.Fix != null), true, "every wrong pin has a fix");

        // fix where you stand: colours carry no meaning, so swaps and rotations among the cable's own pins can be
        // fixed at the device end too; a wrong pin, an extra or a loose wire only at the controller
        Eq(c1.FixableAtDevice, true, "A/B swap can be fixed at the reader");
        Eq(c1.Fixes[0].DeviceText, "Swap the green and yellow wires on the reader: yellow to A, green to B.", "swap text at the reader");
        Eq(RuleChecker.Check(FixedAtDevice(rec, c1), Pins(K, O, Y, G, N, N, N, N)).AllCorrect, true, "A/B swap fixed at the reader -> correct");
        Eq(c3.FixableAtDevice, true, "power reversal can be fixed at the reader");
        Eq(RuleChecker.Check(FixedAtDevice(rec, c3), Pins(O, K, G, Y, N, N, N, N)).AllCorrect, true, "power reversal fixed at the reader -> correct");
        Eq(c6.FixableAtDevice, true, "rotation can be fixed at the reader");
        Console.WriteLine("     rotation at the reader -> " + string.Join(" / ", c6.Fixes.Select(f => f.DeviceText)));
        Eq(RuleChecker.Check(FixedAtDevice(rec, c6), Pins(K, Y, O, G, N, N, N, N)).AllCorrect, true, "rotation fixed at the reader -> correct");
        Eq(c2.FixableAtDevice, false, "B on O2 needs the controller");
        Eq(c4.FixableAtDevice, false, "loose wire at the controller needs the controller");
        Eq(c5.FixableAtDevice, false, "extra wire needs the controller");
        Eq(ok.FixableAtDevice, false, "nothing to fix -> not 'fixable at the reader'");
        var mixed = RuleChecker.Check(rec, Pins(O, K, G, N, N, Y, N, N));   // power reversed and B on O2
        Eq(mixed.FixableAtDevice, false, "one controller-only fix sends every fix to the controller");

        // verdict table
        CheckReason why;
        Eq(VerdictCombiner.Combine(true, Opinion.Match, .9f, .8f, out why), Verdict.Correct, "correct+match -> green");
        Eq(VerdictCombiner.Combine(false, Opinion.Mismatch, .9f, .8f, out why), Verdict.Wrong, "wrong+mismatch -> red");
        Eq(VerdictCombiner.Combine(true, Opinion.Mismatch, .9f, .8f, out why) == Verdict.Check && why == CheckReason.SecondOpinionDisagrees, true, "correct+mismatch -> amber (AI disagrees)");
        Eq(VerdictCombiner.Combine(false, Opinion.Match, .9f, .8f, out why), Verdict.Check, "wrong+match -> amber");
        Eq(VerdictCombiner.Combine(true, Opinion.Match, .6f, .8f, out why) == Verdict.Check && why == CheckReason.LowConfidence, true, "low confidence -> amber");
        Eq(VerdictCombiner.Combine(true, Opinion.Unknown, .9f, .8f, out why), Verdict.Check, "no answer -> amber");

        var ops = new Opinion[8]; for (int i = 0; i < 8; i++) ops[i] = c1.Pins[i].RulesOk ? Opinion.Match : Opinion.Mismatch;
        ops[0] = Opinion.Mismatch; // scripted disagreement on pin 1
        var res = VerdictCombiner.Evaluate(c1, Pins(K, O, Y, G, N, N, N, N), ops, .8f);
        Eq(string.Join(",", res.Select(r => r.Pin + ":" + r.Verdict)), "1:Check,2:Correct,3:Wrong,4:Wrong", "case 1 per-pin verdicts");

        Eq(Rec(R, R, G, Y).ColoursAreDistinct(), false, "duplicate colours detected");

        var log = new SessionLog(() => 1.5, "S1"); log.Add("fix_shown", "Swap \"a\""); 
        Eq(log.ToCsv(), "session,seconds,event,detail\nS1,1.50,fix_shown,\"Swap \"\"a\"\"\"\n", "csv escaping");


        // either end first: the label says which end it is, the key (id, door, plan) pairs the two ends
        CableLabel dev, ctrl, old;
        Eq(CableLabel.TryParse("IA|C01|D1|A1610.READER1|A4020E|DEV", out dev) && dev.End == CableEnd.Device, true, "label with device end");
        Eq(CableLabel.TryParse("C01 · D1 · A1610.READER1 · A4020E · CTRL", out ctrl) && ctrl.End == CableEnd.Controller, true, "printed label with controller end");
        Eq(CableLabel.TryParse("IA|C01|D1|A1610.READER1|A4020E", out old) && old.End == CableEnd.Unknown, true, "label without end still parses");
        Eq(dev.Key == ctrl.Key && dev.Pairs(ctrl), true, "two ends of one cable pair");
        Eq(dev.Pairs(dev), false, "the same end does not pair with itself");
        CableLabel other; CableLabel.TryParse("IA|C02|D2|A1610.READER2|A4020E|CTRL", out other);
        Eq(dev.Pairs(other), false, "ends of different cables do not pair");
        CableLabel misprint; CableLabel.TryParse("IA|C01|D1|A1610.READER2|A4020E|CTRL", out misprint);
        Eq(!dev.Pairs(misprint) && dev.SameCableOtherPlan(misprint), true, "same cable, other plan on the other end: no pairing, flagged as a misprint");
        Eq(dev.DoorName, "Door 1", "door name from the label");
        Eq(dev.ModuleAt(CableEnd.Controller).Code, "A1610.READER1", "module of the controller end");
        Eq(dev.AtEnd(CableEnd.Controller).Payload, "IA|C01|D1|A1610.READER1|A4020E|CTRL", "payload of the other end");

        // controller end recorded first: same verdict as reader first
        var cr = new ControllerRecord(ctrl, AxisCatalog.OsdpReaderOnReader1, new[] { K, O, Y, G, N, N, N, N },
            Enumerable.Repeat(0.95f, 8).ToArray(), new bool[8], DateTime.Now);
        var late = RuleChecker.Check(Rec(O, K, G, Y), cr.AsReading());
        Eq(late.Fixes.Count == 1 && late.Fixes[0].Kind == FixKind.Swap, true, "controller first: A/B swap found");
        Eq(cr.PinWithColour(Y), 3, "controller record: yellow sits on pin 3");
        var store = new InMemoryRecordStore();
        store.Save(cr);
        ControllerRecord back; CableRecord none;
        Eq(store.TryGet(dev.Key, out back) && !store.TryGet(dev.Key, out none), true, "store keeps the controller end under the cable key");
        store.Clear();
        Eq(store.TryGet(dev.Key, out back), false, "a fresh site visit clears the store");
        CableLabel door2;
        CableLabel.TryParse("IA|C02|D2|A1610.READER2|A4020E|DEV", out door2);
        Eq(dev.Pairs(door2) || dev.Pairs(dev.AtEnd(CableEnd.Device)), false, "a different cable or the same end does not pair");

        // readings carry where each terminal is, so live markers can be anchored without hand-placed points
        var placed = new ControllerReading(new[] { K, O, G, Y, N, N, N, N }, Enumerable.Repeat(0.9f, 8).ToArray(),
            new[] { new ImagePoint(0.24f, 0.52f), new ImagePoint(0.24f, 0.49f), new ImagePoint(0.24f, 0.47f), new ImagePoint(0.24f, 0.44f),
                    ImagePoint.None, ImagePoint.None, ImagePoint.None, ImagePoint.None }, 42);
        Eq(placed.PositionOfPin(3).Valid && placed.FrameId == 42, true, "controller reading keeps pin positions and frame id");
        Eq(Pins(K, O, G, Y, N, N, N, N).Positions.Length == 8 && !Pins(K, O, G, Y, N, N, N, N).Positions[0].Valid, true, "positions default to unknown");
        Eq(RuleChecker.Check(Rec(O, K, G, Y), placed).AllCorrect, true, "positions do not change the rule check");

        // the same template on Reader 2: texts name Reader 2's own pins
        var rec2 = new CableRecord(r2, AxisCatalog.OsdpReaderOnReader2, new[] { O, K, G, Y }, new bool[4], DateTime.Now);
        var onO4 = RuleChecker.Check(rec2, Pins(K, O, G, N, N, Y, N, N));
        Eq(onO4.Fixes.Count == 1 ? onO4.Fixes[0].Text : "", "Move the yellow wire from O4 (pin 6) to B (pin 4).", "Reader 2 fix names Reader 2's pins");

        // zone check: is the cable in the module its label names?
        var cable = Rec(O, K, G, Y);
        var rule1 = AxisCatalog.OsdpReaderOnReader1;
        int[] none8 = { N, N, N, N, N, N, N, N }, none4 = { N, N, N, N }, here8 = { K, O, G, Y, N, N, N, N };
        Eq(ZoneCheck.Check(rule1, Pins(here8), cable).Status, ZoneStatus.NotSurveyed, "no survey -> zone check skipped");
        Eq(ZoneCheck.Check(rule1, Survey(here8, none8, none4), cable).Status, ZoneStatus.InPlace, "wires on Reader 1 -> in place");
        Eq(ZoneCheck.Check(rule1, Survey(new[] { K, Y, G, O, N, N, N, N }, here8, none4), cable).Status, ZoneStatus.InPlace,
            "Reader 1 holds wires -> the pin check decides, even if Reader 2 holds this cable's colours");
        var notInView = ZoneCheck.Check(rule1, Survey(none8, here8, none4, false), cable);
        Eq(notInView.Status == ZoneStatus.NotInView && notInView.NeedsAttention, true, "Reader 1 not found in the frame -> point the camera at it");
        Eq(ZoneCheck.Check(rule1, Survey(none8, none8, none4), cable).Status, ZoneStatus.Empty, "everything empty -> empty (the pin check says connect)");
        var moved = ZoneCheck.Check(rule1, Survey(none8, here8, none4), cable);
        Eq(moved.Status == ZoneStatus.Elsewhere && moved.Found == AxisCatalog.A1610Reader2 && moved.ColoursMatch && moved.WiresFound == 4, true,
            "cable on Reader 2 in its own colours -> elsewhere, found Reader 2");
        Eq(moved.Headline, "Move the cable to Reader 1", "zone headline");
        Eq(moved.Text, "The label says this cable goes to Reader 1, but Reader 1 is empty. Reader 2 has 4 wires in this cable's colours. " +
            "Move them to Reader 1. Turn the power off first. Then check again.", "zone text");
        var unsure = ZoneCheck.Check(rule1, Survey(none8, here8, none4));
        Eq(unsure.Status == ZoneStatus.Elsewhere && !unsure.ColoursMatch && unsure.Headline == "Is this cable on Reader 2?", true,
            "controller end first (no colours to compare) -> asks");
        var otherColours = ZoneCheck.Check(rule1, Survey(none8, new[] { K, R, G, Y, N, N, N, N }, none4), cable);
        Eq(otherColours.Status == ZoneStatus.Elsewhere && !otherColours.ColoursMatch, true, "other colours on Reader 2 -> asks, does not claim");
        Eq(ZoneCheck.Check(rule1, Survey(none8, here8, none4), cable, new[] { "A1610.READER2" }).Status, ZoneStatus.Empty,
            "Reader 2 planned for another scanned cable -> not suggested");
        var several = ZoneCheck.Check(rule1, Survey(none8, here8, new[] { K, R, N, N }), cable);
        Eq(several.Status == ZoneStatus.Elsewhere && several.Found == null && several.Text.Contains("Reader 2 and Door 1"), true, "wires on two modules -> names both");
        Console.WriteLine("     " + several.Text);

        // bundle check: do the wires belong to the scanned label? (shared colours fool the colour check)
        const WireTrace U = WireTrace.Unknown, Th = WireTrace.ThisCable, Ot = WireTrace.OtherCable;
        var c01 = rec.Label;
        Eq(BundleCheck.Check(rule1, c01, Pins(here8)).Status, BundleStatus.NotTraced, "no traces -> bundle check silent");
        var traced = new ControllerReading(here8, Conf(8), null, 0, null, new[] { Th, Th, Th, Th, U, U, U, U });
        Eq(BundleCheck.Check(rule1, c01, traced).Status == BundleStatus.ThisCable && BundleCheck.Check(rule1, c01, traced).Traced == 4, true,
            "all four wires run back to the label -> this cable");
        var oneForeign = new ControllerReading(here8, Conf(8), null, 0, null, new[] { Th, Ot, Th, U, U, U, U, U });
        var bf = BundleCheck.Check(rule1, c01, oneForeign);
        Eq(bf.Status == BundleStatus.Foreign && bf.NeedsAttention && bf.Foreign.Count == 1 && bf.Foreign[0] == 1 && bf.TouchesPower, true,
            "orange on 12V from another cable -> foreign, pin 2, power");
        Eq(bf.Headline, "The wire on 12V is from another cable", "bundle headline");
        Console.WriteLine("     " + bf.Text);
        Eq(RuleChecker.Check(rec, oneForeign).AllCorrect, true, "...while the colour check alone sees nothing wrong (that is why the bundle check exists)");
        var twoForeign = BundleCheck.Check(rule1, c01, new ControllerReading(here8, Conf(8), null, 0, null, new[] { Th, Th, Ot, Ot, U, U, U, U }));
        Eq(twoForeign.Foreign.Count == 2 && !twoForeign.TouchesPower && twoForeign.Text.Contains("A (pin 3) and B (pin 4)"), true, "two data wires from another cable");
        Eq(BundleCheck.Check(rule1, c01, new ControllerReading(here8, Conf(8), null, 0, null, new[] { Ot, U, U, U, Ot, U, U, U })).Foreign.Count, 1,
            "a trace on an empty pin is ignored");
        var devForeign = BundleCheck.Check(rule1, c01, new DeviceEndReading(new[] { O, K, G, Y }, Conf(4), null, 0, new[] { Th, Th, Ot, Th }));
        Eq(devForeign.Status == BundleStatus.Foreign && devForeign.Text.Contains("Take it off the reader"), true, "device end: A's wire from another cable");

        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        return fails;
    }
}
