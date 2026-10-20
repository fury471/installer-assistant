using System;
using System.Collections;
using InstallerAssistant.Labels;
using InstallerAssistant.Verification;

namespace InstallerAssistant.Detection
{
    /// <summary>
    /// Layer 1 of the check: reads which wire colour sits on each terminal, with a confidence, and where each
    /// terminal is in the frame. Owner: package A (Perception and AI).
    /// It also follows each wire back from its terminal and says whether it runs to the scanned cable's label
    /// ('cable'), to another cable, or cannot tell (Traces in the readings): the bundle check uses it, because
    /// different cables often share colours. Leave the traces Unknown when the reader cannot follow the wires.
    /// Real implementation: locate the module in the frame, crop beside each terminal and classify the colour
    /// (classical colour model and/or a vision-language model). Wizard-of-Oz implementation: scripted colours and
    /// the hand-placed positions of the demo photos.
    /// </summary>
    public interface IWireReader
    {
        /// <summary>Colour and position of each device terminal, in the rule's terminal order.</summary>
        IEnumerator ReadDeviceEnd(UsageRule rule, CableLabel cable, CameraFrame frame, Action<DeviceEndReading> done);

        /// <summary>
        /// Colour (or none) and position of every pin of the controller module the label names (rule.Connector).
        /// Also report the controller's other modules seen in the same frame (AxisCatalog.OtherModules(rule)) in
        /// ControllerReading.OtherModules: whether each one is in the picture and which pins hold wires. The zone check
        /// uses them to tell the installer when the cable sits in another module. Leave them out to skip the zone check.
        /// </summary>
        IEnumerator ReadControllerEnd(UsageRule rule, CableLabel cable, CameraFrame frame, Action<ControllerReading> done);
    }
}
