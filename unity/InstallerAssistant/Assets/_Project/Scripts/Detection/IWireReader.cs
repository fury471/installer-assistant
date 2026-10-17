using System;
using System.Collections;
using InstallerAssistant.Verification;

namespace InstallerAssistant.Detection
{
    /// <summary>
    /// Layer 1 of the check: reads which wire colour sits on each terminal, with a confidence.
    /// Real implementation: locate the board (AR image target), crop the terminal block tightly and ask a
    /// vision-language model for a fixed JSON answer. Wizard-of-Oz implementation: scripted colours.
    /// </summary>
    public interface IWireReader
    {
        /// <summary>Colour on each device terminal, in the rule's terminal order.</summary>
        IEnumerator ReadDeviceEnd(UsageRule rule, Action<DeviceEndReading> done);

        /// <summary>Colour (or none) on every pin of the rule's controller block.</summary>
        IEnumerator ReadControllerEnd(UsageRule rule, Action<ControllerReading> done);
    }
}
