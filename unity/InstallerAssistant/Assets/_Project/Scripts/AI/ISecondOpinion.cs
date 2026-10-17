using System;
using System.Collections;
using InstallerAssistant.Verification;

namespace InstallerAssistant.AI
{
    /// <summary>
    /// Layer 3 of the check: an independent AI comparison of the two ends.
    /// Real implementation: a separate VLM call that gets the reader-end snapshot, the controller crop and the
    /// pairing rule, and answers "match" / "mismatch" per pin in JSON. It never sees layer 1's reading, so its
    /// errors are less likely to coincide with layer 1's. Wizard-of-Oz implementation: scripted answers.
    /// </summary>
    public interface ISecondOpinion
    {
        /// <summary>One opinion per pin of the controller block (index = pin - 1).</summary>
        IEnumerator Compare(CableRecord record, Action<Opinion[]> done);
    }
}
