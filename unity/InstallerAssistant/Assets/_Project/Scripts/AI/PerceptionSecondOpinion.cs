using System;
using System.Collections;
using InstallerAssistant.Detection;
using InstallerAssistant.Verification;
using UnityEngine;

namespace InstallerAssistant.AI
{
    /// <summary>
    /// Layer 3 in live mode: the perception service's /compare call (a separate model call on both frames and the
    /// pairing rule; it never gets layer 1's reading). Owner: package A.
    /// If the service fails, every pin is "unknown", so the app shows amber and asks the installer to look.
    /// </summary>
    public sealed class PerceptionSecondOpinion : ISecondOpinion
    {
        readonly PerceptionClient client;
        public PerceptionSecondOpinion(PerceptionClient client) { this.client = client; }

        public IEnumerator Compare(CableRecord record, CameraFrame deviceEnd, CameraFrame controllerEnd, Action<Opinion[]> done)
        {
            PerceptionClient.CompareResponse res = null;
            yield return client.Compare(record.Rule, deviceEnd, controllerEnd, (r, e) =>
            {
                res = r;
                if (e != null) Debug.LogWarning("[InstallerAssistant] Second opinion failed: " + e);
            });
            int n = record.Rule.Connector.Count;
            var ops = new Opinion[n];
            for (int i = 0; i < n; i++)
            {
                string o = res != null && res.opinions != null && i < res.opinions.Length ? res.opinions[i] : null;
                ops[i] = o == "match" ? Opinion.Match : o == "mismatch" ? Opinion.Mismatch : Opinion.Unknown;
            }
            done(ops);
        }
    }
}
