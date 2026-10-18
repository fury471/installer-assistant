using System;
using System.Collections;
using InstallerAssistant.Detection;
using InstallerAssistant.Verification;

namespace InstallerAssistant.Labels
{
    public enum LabelScanStatus { Found, NotFound, Unreadable }

    public sealed class LabelScan
    {
        public LabelScanStatus Status;
        public CableLabel Label;
        /// <summary>The label's four corners in the frame (normalised), clockwise from top-left; empty if not found.</summary>
        public ImagePoint[] Corners = new ImagePoint[0];
        /// <summary>Id of the camera frame the label was found in (0 = unknown).</summary>
        public long FrameId;
    }

    /// <summary>
    /// Finds and decodes the cable label in a camera frame. Owner: package A (Perception and AI).
    /// Real implementation: decode the QR code (e.g. ZXing.Net on the CPU image) and return its corners.
    /// Wizard-of-Oz implementation: returns the scripted label after a delay.
    /// </summary>
    public interface ILabelScanner
    {
        IEnumerator Scan(CameraFrame frame, Action<LabelScan> done);
    }
}
