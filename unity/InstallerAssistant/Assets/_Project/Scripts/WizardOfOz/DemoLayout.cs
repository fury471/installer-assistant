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
        [Tooltip("Optional. Controller photo without the cable label; shown during the 'label missing' step.")]
        public Texture2D controllerNoLabelPhoto;
        [Tooltip("Optional. Controller photo after the wiring has been fixed; shown after 'Fix & rescan'.")]
        public Texture2D controllerFixedPhoto;

        [Header("Job")]
        public string doorName = "Door 1";
        public string cableLabel = "D1 · READER · C01";

        [Header("Terminal names as printed on the boards, in the order: power +, power -, A, B")]
        public string[] readerTerminalNames = { "+", "-", "A", "B" };
        public string[] controllerTerminalNames = { "+12V", "-", "A", "B" };

        [Header("Reader end: positions on the photo (press K in Play mode to click them)")]
        public Vector2 readerLabelPos = new Vector2(0.5f, 0.701f);
        public Vector2[] readerTerminalPos =
        {
            new Vector2(0.4000f, 0.420f), new Vector2(0.4667f, 0.420f),
            new Vector2(0.5333f, 0.420f), new Vector2(0.6000f, 0.420f)
        };
        [Range(0.5f, 3f)] public float readerZoom = 1f;
        [Tooltip("Moves the photo; in fractions of the photo size.")]
        public Vector2 readerPan = Vector2.zero;

        [Header("Controller end: positions on the photo")]
        public Vector2 controllerLabelPos = new Vector2(0.5f, 0.701f);
        public Vector2[] controllerTerminalPos =
        {
            new Vector2(0.4167f, 0.451f), new Vector2(0.4722f, 0.451f),
            new Vector2(0.5278f, 0.451f), new Vector2(0.5833f, 0.451f)
        };
        [Range(0.5f, 3f)] public float controllerZoom = 1f;
        public Vector2 controllerPan = Vector2.zero;

        [Header("Optional photo-specific placement (disabled = use controller defaults)")]
        public ControllerPhotoPlacement controllerNoLabelPlacement = new ControllerPhotoPlacement();
        public ControllerPhotoPlacement controllerFixedPlacement = new ControllerPhotoPlacement();
        [Tooltip("Use cable-label wording for photographed handwritten labels instead of QR labels.")]
        public bool handwrittenLabels;
        [Tooltip("Label frame size as a fraction of the photo. Zero keeps the original square frame.")]
        public Vector2 readerLabelSize;
        public Vector2 controllerLabelSize;
        [Range(0.5f, 1f)] public float markerScale = 1f;
        [Range(0.5f, 1f)] public float labelTextScale = 1f;
        public bool snapshotOnLeft;
        public float snapshotInset = 40f;
        public float snapshotTopInset = 250f;
        public bool readerTagsBelowTerminals;

        [System.Serializable]
        public class ControllerPhotoPlacement
        {
            public bool enabled;
            public Vector2[] terminalPos = new Vector2[4];
            public Vector2 labelPos;
            public Vector2 labelSize;
            [Range(0.5f, 3f)] public float zoom = 1f;
            public Vector2 pan;
        }

        [Header("Wire colours: 0 Red, 1 Black, 2 Green, 3 Yellow, 4 White, 5 Brown, 6 Blue, 7 Orange")]
        [Tooltip("Colour on the reader terminals, in the order +, -, A, B.")]
        public int[] readerColours = { 0, 1, 2, 3 };
        [Tooltip("Colour found on the controller terminals +12V, -, A, B before the fix (the planted error).")]
        public int[] controllerFoundColours = { 0, 1, 3, 2 };

        /// <summary>
        /// Fills in the positions for the bench photos made from the team's reader and A1610 photos
        /// (reader.jpg, controller_*.jpg). Use it from the asset's ⋮ menu in the Inspector.
        /// </summary>
        [ContextMenu("Use bench photo positions")]
        void UseBenchPhotoPositions()
        {
            readerTerminalNames = new[] { "+", "-", "A", "B" };
            controllerTerminalNames = new[] { "+12V", "-", "A", "B" };
            readerLabelPos = new Vector2(0.500f, 0.675f);
            readerTerminalPos = new[]
            {
                new Vector2(0.430f, 0.400f), new Vector2(0.283f, 0.400f),   // +, -
                new Vector2(0.572f, 0.400f), new Vector2(0.717f, 0.400f)    // A, B
            };
            controllerLabelPos = new Vector2(0.306f, 0.670f);
            controllerTerminalPos = new[]
            {
                new Vector2(0.354f, 0.505f), new Vector2(0.354f, 0.547f),   // +12V, -
                new Vector2(0.354f, 0.460f), new Vector2(0.354f, 0.418f)    // A, B
            };
            readerZoom = controllerZoom = 1f;
            readerPan = controllerPan = Vector2.zero;
            readerColours = new[] { 0, 1, 2, 3 };
            controllerFoundColours = new[] { 0, 1, 3, 2 };
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        [Header("Scripted results")]
        [Tooltip("Reader terminal (0-3) the AI is unsure about; the installer checks it.")]
        public int uncertainReaderTerminal = 3;
        [Tooltip("Controller terminal (0-3) where the AI second opinion disagrees with the rules.")]
        public int aiDisagreesOnControllerTerminal = 1;
        [Tooltip("Start the controller end with a missing label, to show the label guidance.")]
        public bool startWithMissingLabel = true;
    }
}
