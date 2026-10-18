using InstallerAssistant.Detection;
using InstallerAssistant.Labels;
using InstallerAssistant.Verification;
using UnityEngine;

namespace InstallerAssistant.AR
{
    /// <summary>
    /// What the check screen shows behind its markers, and where things are on it. Owner: package B (AR runtime).
    ///
    /// The app never touches the camera directly. It asks the view for frames (to hand to the readers), tells it
    /// where the readers found things in those frames (Anchor), and asks every frame where those things are now
    /// (TryGetPosition). Two implementations:
    ///   PhotoCameraView (WizardOfOz)  a photo per end; a frame is the photo, so positions map 1:1.
    ///   ArCameraView    (AR)          the live AR camera; positions become world anchors that stay on the
    ///                                 terminals while the phone moves, projected back to the screen each frame.
    /// </summary>
    public interface ICameraView : ICameraFrameSource
    {
        /// <summary>True when the camera image is drawn behind the UI (live AR): the app keeps its check screen transparent.</summary>
        bool SeeThrough { get; }

        /// <summary>Builds the view under the full-screen 'parent' and returns the layer markers are placed in.</summary>
        RectTransform Attach(RectTransform parent);

        /// <summary>Removes what Attach built (the app is leaving the check screen).</summary>
        void Detach();

        /// <summary>
        /// The installer is now at this end. Wizard of Oz: shows that end's photo (Unknown = the default photo).
        /// Live: nothing to do, the camera already shows it; clears anchors of the previous end.
        /// </summary>
        void ShowEnd(CableEnd end);

        /// <summary>Remembers where 'key' was found in 'frame' (normalised). Live: create a world anchor there.</summary>
        void Anchor(string key, ImagePoint where, CameraFrame frame);

        /// <summary>Forgets every anchor.</summary>
        void ClearAnchors();

        /// <summary>Where 'key' is now, normalised to the marker layer (x right, y down). False if unknown or out of view.</summary>
        bool TryGetPosition(string key, out Vector2 position);

        /// <summary>Where to suggest attaching a missing label, normalised to the marker layer.</summary>
        Vector2 LabelHint { get; }

        /// <summary>Scale that keeps markers the same size on screen whatever the zoom (1 = no zoom).</summary>
        float MarkerScale { get; }

        /// <summary>
        /// A still image of what the camera shows now, with the positions just read, kept as the snapshot of this end
        /// (shown at the other end and in the report). Positions are normalised to the image (index = terminal or pin - 1).
        /// </summary>
        EndSnapshot Snapshot(CableEnd end, ImagePoint[] positions);

        /// <summary>Called by the app every frame: zoom, hand sway (photo) or anchor projection (live).</summary>
        void Tick();
    }

    /// <summary>A still image of one end with its terminal positions, kept with the record of that end.</summary>
    public sealed class EndSnapshot
    {
        public CableEnd End;
        /// <summary>The image (null if the view could not take one).</summary>
        public Texture2D Image;
        /// <summary>Terminal (device end) or pin (controller end, index = pin - 1) positions in Image, normalised, y down.</summary>
        public Vector2[] Points = new Vector2[0];
    }

    /// <summary>The keys the app and the views use for anchors, so both sides name things the same way.</summary>
    public static class CameraKeys
    {
        public const string Label = "label";
        /// <summary>A device terminal by its index in the rule's terminal order (0 = first).</summary>
        public static string Terminal(int index) { return "terminal:" + index; }
        /// <summary>A controller pin by its number (1-based, as printed).</summary>
        public static string Pin(int pin) { return "pin:" + pin; }
        /// <summary>The key of mark index i at the given end (device: terminal i; controller: pin i + 1).</summary>
        public static string Of(CableEnd end, int index) { return end == CableEnd.Controller ? Pin(index + 1) : Terminal(index); }
    }
}
