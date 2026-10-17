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
        CableLabel l; CableLabel.TryParse("IA|D1|READER|C01", out l);
        return new CableRecord(l, AxisCatalog.OsdpReaderOnReader1, c, new bool[4], DateTime.Now);
    }
    static ControllerReading Pins(params int[] c) { return new ControllerReading(c, Enumerable.Repeat(0.95f, c.Length).ToArray()); }

    static int Main()
    {
        CableLabel lab;
        Eq(CableLabel.TryParse("IA|D1|READER|C01", out lab), true, "parse QR payload");
        Eq(lab.Display, "D1 · READER · C01", "display text");
        Eq(CableLabel.TryParse("D1 · reader · C01", out lab) && lab.Usage == "READER", true, "parse printed text");
        Eq(CableLabel.TryParse("hello", out lab), false, "reject junk");
        Eq(AxisCatalog.Lookup("reader") == AxisCatalog.OsdpReaderOnReader1, true, "rule lookup");

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


        // either end first: the label says which end it is, the key pairs the two ends
        CableLabel dev, ctrl, old;
        Eq(CableLabel.TryParse("IA|D1|READER|C01|DEV", out dev) && dev.End == CableEnd.Device, true, "label with device end");
        Eq(CableLabel.TryParse("D1 · READER · C01 · CTRL", out ctrl) && ctrl.End == CableEnd.Controller, true, "printed label with controller end");
        Eq(CableLabel.TryParse("IA|D1|READER|C01", out old) && old.End == CableEnd.Unknown, true, "label without end still parses");
        Eq(CableLabel.TryParse("IA|D1|READER|C01|XYZ", out old), false, "reject unknown end code");
        Eq(dev.Key == ctrl.Key && dev.Pairs(ctrl), true, "two ends of one cable pair");
        Eq(dev.Pairs(dev), false, "the same end does not pair with itself");
        CableLabel other; CableLabel.TryParse("IA|D2|READER|C02|CTRL", out other);
        Eq(dev.Pairs(other), false, "ends of different cables do not pair");
        Eq(dev.DoorName, "Door 1", "door name from the label");
        Eq(dev.AtEnd(CableEnd.Controller).Payload, "IA|D1|READER|C01|CTRL", "payload of the other end");

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

        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        return fails;
    }
}
