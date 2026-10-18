using System;
using System.Collections;
using InstallerAssistant.Detection;
using InstallerAssistant.Verification;

namespace InstallerAssistant.AI
{
    /// <summary>
    /// Layer 3 of the check: an independent AI comparison of the two ends. Owner: package A (Perception and AI).
    /// Real implementation: a separate model call that gets a frame of each end and the pairing rule, and answers
    /// "match" / "mismatch" per pin in JSON. It never sees layer 1's reading or the rule check, so its errors are
    /// less likely to coincide with theirs. Wizard-of-Oz implementation: scripted answers.
    /// </summary>
    public interface ISecondOpinion
    {
        /// <summary>One opinion per pin of the controller block (index = pin - 1). Either frame may be null.</summary>
        IEnumerator Compare(CableRecord record, CameraFrame deviceEnd, CameraFrame controllerEnd, Action<Opinion[]> done);
    }
}
