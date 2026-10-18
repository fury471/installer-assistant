using System;
using System.Collections;
using InstallerAssistant.Verification;

namespace InstallerAssistant.Detection
{
    /// <summary>
    /// Layer 1 of the check: reads which wire colour sits on each terminal, with a confidence, and where each
    /// terminal is in the frame. Owner: package A (Perception and AI).
    /// Real implementation: locate the connector in the frame, crop beside each terminal and classify the colour
    /// (classical colour model and/or a vision-language model). Wizard-of-Oz implementation: scripted colours and
    /// the hand-placed positions of the demo photos.
    /// </summary>
    public interface IWireReader
    {
        /// <summary>Colour and position of each device terminal, in the rule's terminal order.</summary>
        IEnumerator ReadDeviceEnd(UsageRule rule, CameraFrame frame, Action<DeviceEndReading> done);

        /// <summary>Colour (or none) and position of every pin of the rule's controller block.</summary>
        IEnumerator ReadControllerEnd(UsageRule rule, CameraFrame frame, Action<ControllerReading> done);
    }
}
