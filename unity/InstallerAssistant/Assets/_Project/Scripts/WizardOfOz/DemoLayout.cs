using UnityEngine;

namespace InstallerAssistant.WizardOfOz
{
    /// <summary>
    /// Photos, positions and scripted results for the Installer Assistant Wizard-of-Oz demo.
    /// Create one with: Project window > right-click > Create > Installer Assistant > Demo Layout.
    /// Positions are fractions of the photo: (0,0) = top-left corner, (1,1) = bottom-right corner.
    /// In Play mode, press K to set them by clicking on the photo; the asset keeps the new values.
    /// Leave the photos empty to use the built-in mock images.
    /// </summary>
    [CreateAssetMenu(menuName = "Installer Assistant/Demo Layout", fileName = "DemoLayout")]
    public class DemoLayout : ScriptableObject
    {
        [Header("Photos (empty = built-in mock images)")]
        public Texture2D readerPhoto;
        public Texture2D controllerPhoto;
        [Tooltip("Optional. Controller photo after the wiring has been fixed; shown after 'Fix & rescan'.")]
        public Texture2D controllerFixedPhoto;

        [Header("Job")]
        public string doorName = "Door 1";
        public string cableLabel = "D1 · READER · C01";

        [Header("Reader end: positions on the photo (press K in Play mode to click them)")]
        public Vector2 readerLabelPos = new Vector2(0.5f, 0.701f);
        public Vector2[] readerTerminalPos =
        {
            new Vector2(0.4000f, 0.420f), new Vector2(0.4667f, 0.420f),
            new Vector2(0.5333f, 0.420f), new Vector2(0.6000f, 0.420f)
        };
        [Range(1f, 3f)] public float readerZoom = 1f;
        [Tooltip("Moves the photo; in fractions of the photo size.")]
        public Vector2 readerPan = Vector2.zero;

        [Header("Controller end: positions on the photo")]
        public Vector2 controllerLabelPos = new Vector2(0.5f, 0.701f);
        public Vector2[] controllerTerminalPos =
        {
            new Vector2(0.4167f, 0.451f), new Vector2(0.4722f, 0.451f),
            new Vector2(0.5278f, 0.451f), new Vector2(0.5833f, 0.451f)
        };
        [Range(1f, 3f)] public float controllerZoom = 1f;
        public Vector2 controllerPan = Vector2.zero;

        [Header("Wire colours: 0 Red, 1 Black, 2 Green, 3 Yellow, 4 White, 5 Brown, 6 Blue, 7 Orange")]
        [Tooltip("Colour on reader terminals +V, 0V, A, B.")]
        public int[] readerColours = { 0, 1, 2, 3 };
        [Tooltip("Colour found on controller terminals 12V, GND, A, B before the fix (the planted error).")]
        public int[] controllerFoundColours = { 0, 1, 3, 2 };

        [Header("Scripted results")]
        [Tooltip("Reader terminal (0-3) the AI is unsure about; the installer checks it.")]
        public int uncertainReaderTerminal = 3;
        [Tooltip("Controller terminal (0-3) where the AI second opinion disagrees with the rules.")]
        public int aiDisagreesOnControllerTerminal = 1;
        [Tooltip("Start the controller end with a missing label, to show the label guidance.")]
        public bool startWithMissingLabel = true;
    }
}
