namespace InstallerAssistant.Verification
{
    /// <summary>The AI second opinion's answer for one pin.</summary>
    public enum Opinion { Match, Mismatch, Unknown }

    /// <summary>What the installer sees on a pin.</summary>
    public enum Verdict
    {
        /// <summary>Green: rules and second opinion agree that it is right.</summary>
        Correct,
        /// <summary>Red: rules and second opinion agree that it is wrong; comes with a fix.</summary>
        Wrong,
        /// <summary>Amber: the automation is not sure; the installer looks.</summary>
        Check,
        /// <summary>The installer looked at an amber pin and confirmed it is right.</summary>
        ConfirmedByInstaller,
    }

    public enum CheckReason { None, SecondOpinionDisagrees, LowConfidence }

    /// <summary>Everything the UI needs for one pin at the controller end.</summary>
    public sealed class PinResult
    {
        public PinFinding Finding;
        public Opinion Opinion;
        public float Confidence;
        public Verdict Verdict;
        public CheckReason Reason;
        public int Pin { get { return Finding.Pin; } }
    }

    /// <summary>
    /// Combines layer 2 (rules) and layer 3 (AI second opinion) per pin, following the design's decision table:
    ///   rules correct + match      -> Correct
    ///   rules wrong   + mismatch   -> Wrong (fix from the rules)
    ///   rules and AI disagree      -> Check ("check this pin against the snapshot")
    ///   low confidence / no answer -> Check ("can't confirm automatically")
    /// The second opinion never overrules the rules on its own; disagreement hands the decision to the installer.
    /// </summary>
    public static class VerdictCombiner
    {
        /// <summary>Default reading confidence below which a pin turns amber. Tune it in the accuracy test.</summary>
        public const float DefaultThreshold = 0.80f;

        public static Verdict Combine(bool rulesOk, Opinion opinion, float confidence, float threshold, out CheckReason reason)
        {
            reason = CheckReason.None;
            if (opinion == Opinion.Unknown || confidence < threshold) { reason = CheckReason.LowConfidence; return Verdict.Check; }
            if (rulesOk && opinion == Opinion.Match) return Verdict.Correct;
            if (!rulesOk && opinion == Opinion.Mismatch) return Verdict.Wrong;
            reason = CheckReason.SecondOpinionDisagrees;
            return Verdict.Check;
        }

        /// <summary>Per-pin results for every relevant pin of the block (expected wire or wire found).</summary>
        public static PinResult[] Evaluate(RuleCheck rules, ControllerReading reading, Opinion[] opinions, float threshold)
        {
            var list = new System.Collections.Generic.List<PinResult>();
            foreach (var f in rules.Pins)
            {
                if (!f.Relevant) continue;
                var r = new PinResult
                {
                    Finding = f,
                    Opinion = opinions != null && f.Pin - 1 < opinions.Length ? opinions[f.Pin - 1] : Opinion.Unknown,
                    Confidence = reading.ConfidenceOnPin(f.Pin),
                };
                r.Verdict = Combine(f.RulesOk, r.Opinion, r.Confidence, threshold, out r.Reason);
                list.Add(r);
            }
            return list.ToArray();
        }
    }
}
