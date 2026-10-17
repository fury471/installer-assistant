using UnityEngine;

namespace InstallerAssistant.WizardOfOz
{
    /// <summary>
    /// Photos, positions and scripted faults for the Wizard-of-Oz demo.
    /// Create one with: Project window > right-click > Create > Installer Assistant > Demo Layout.
    /// Positions are fractions of the photo: (0,0) = top-left corner, (1,1) = bottom-right corner.
    /// In Play mode press K to set them by clicking on the photo; the asset keeps the new values.
    /// Leave every photo empty to run on the built-in mock images.
    ///
    /// In the real app the same positions come from tracking the board (Scripts/AR + Scripts/Detection);
    /// everything else in the demo is the app's real logic (Scripts/Verification).
    /// </summary>
    [CreateAssetMenu(menuName = "Installer Assistant/Demo Layout", fileName = "DemoLayout")]
    public class DemoLayout : ScriptableObject
    {
        /// <summary>One camera view: a photo plus where the label and the terminals are on it.</summary>
        [System.Serializable]
        public class PhotoView
        {
            public Texture2D photo;
            [Tooltip("Reader end: the 4 terminals in the order +, -, A, B.\nController end: Reader 1 pins 1-8 (-, 12V, A, B, O1, O2, IO7, IO8).")]
            public Vector2[] points = new Vector2[0];
            public Vector2 labelPos = new Vector2(0.5f, 0.7f);
            [Tooltip("Label frame size as a fraction of the photo. Zero = square frame.")]
            public Vector2 labelSize;
            [Range(0.5f, 3f)] public float zoom = 1f;
            [Tooltip("Moves the photo, in fractions of its size. +Y moves it up, +X moves it left.")]
            public Vector2 pan;

            public PhotoView() { }
            public PhotoView(Vector2[] points, Vector2 labelPos) { this.points = points; this.labelPos = labelPos; }
        }

        /// <summary>A planted fault at the controller end.</summary>
        [System.Serializable]
        public class Scenario
        {
            public string name = "A/B swapped";
            [Tooltip("Colour on Reader 1 pins 1-8 before the fix. 0 Red, 1 Black, 2 Green, 3 Yellow, 4 White, 5 Brown, 6 Blue, 7 Orange, -1 empty.")]
            public int[] pinColours = { 1, 0, 3, 2, -1, -1, -1, -1 };
            [Tooltip("Photo of the controller with this fault.")]
            public PhotoView wiring = new PhotoView();
            [Tooltip("Start the controller end without a label, to show the label guidance.")]
            public bool startWithMissingLabel;
            [Tooltip("Optional photo of the same fault without the cable label.")]
            public PhotoView noLabel = new PhotoView();
            [Tooltip("Pin (1-8) where the AI second opinion disagrees with the rules on the first check. 0 = none.")]
            public int aiDisagreesOnPin;
        }

        [Header("Cable")]
        public string doorName = "Door 1";
        [Tooltip("Cable identity: QR payload \"IA|D1|READER|C01\" or printed text \"D1 · READER · C01\". The usage (READER) selects the wiring rule. The demo adds the end (DEV or CTRL) from the photo shown.")]
        public string cableLabel = "IA|D1|READER|C01";

        [Header("Reader end (AXIS A4020-E)")]
        public PhotoView reader = new PhotoView(new[]
        {
            new Vector2(0.4000f, 0.420f), new Vector2(0.4667f, 0.420f), new Vector2(0.5333f, 0.420f), new Vector2(0.6000f, 0.420f)
        }, new Vector2(0.5f, 0.701f));
        [Tooltip("Colour on the reader terminals +, -, A, B. 0 Red, 1 Black, 2 Green, 3 Yellow, 4 White, 5 Brown, 6 Blue, 7 Orange.")]
        public int[] readerColours = { 0, 1, 2, 3 };
        [Tooltip("Reader terminal (0 = +, 1 = -, 2 = A, 3 = B) the AI is unsure about on the first read. -1 = none.")]
        public int uncertainReaderTerminal = 3;

        [Header("Controller end (AXIS A1610, Reader 1)")]
        [Tooltip("Photo after the installer has fixed the wiring.")]
        public PhotoView controllerFixed = new PhotoView(MockPins(), new Vector2(0.5f, 0.701f));
        public Scenario[] scenarios =
        {
            new Scenario
            {
                name = "A/B swapped", pinColours = new[] { 1, 0, 3, 2, -1, -1, -1, -1 },
                wiring = new PhotoView(MockPins(), new Vector2(0.5f, 0.701f)),
                noLabel = new PhotoView(MockPins(), new Vector2(0.5f, 0.701f)),
                startWithMissingLabel = true, aiDisagreesOnPin = 1,
            },
            new Scenario
            {
                name = "B on O2 (wrong pin)", pinColours = new[] { 1, 0, 2, -1, -1, 3, -1, -1 },
                wiring = new PhotoView(MockPins(), new Vector2(0.5f, 0.701f)),
                noLabel = new PhotoView(MockPins(), new Vector2(0.5f, 0.701f)),
            },
        };

        [Header("Display")]
        [Tooltip("Use cable-label wording for photographed handwritten labels instead of QR labels.")]
        public bool handwrittenLabels;
        [Range(0.5f, 1f)] public float markerScale = 1f;
        [Range(0.5f, 1f)] public float labelTextScale = 1f;
        public bool snapshotOnLeft;
        public float snapshotInset = 40f;
        public float snapshotTopInset = 250f;
        public bool readerTagsBelowTerminals;

        /// <summary>Pin positions of the built-in mock controller (MockPhotos).</summary>
        public static Vector2[] MockPins()
        {
            var p = new Vector2[8];
            for (int k = 0; k < 8; k++) p[k] = new Vector2((MockPhotos.PinX0 + MockPhotos.PinPitch * k) / MockPhotos.W, MockPhotos.PinY / MockPhotos.H);
            return p;
        }

        /// <summary>True when no photo is assigned anywhere: the demo then draws mock images.</summary>
        public bool UsesMockImages { get { return reader.photo == null; } }

        /// <summary>
        /// Fills pins 5-8 by continuing the spacing of pins 1-4 (the block's pins are evenly spaced).
        /// Use it from the asset's ⋮ menu after calibrating pins 1-4 on a new photo.
        /// </summary>
        [ContextMenu("Extrapolate pins 5-8 from pins 1-4")]
        void ExtrapolateAll()
        {
            Extrapolate(controllerFixed);
            foreach (var s in scenarios) { Extrapolate(s.wiring); Extrapolate(s.noLabel); }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        public static void Extrapolate(PhotoView v)
        {
            if (v == null || v.points == null || v.points.Length < 4) return;
            var p = new Vector2[8];
            System.Array.Copy(v.points, p, 4);
            Vector2 step = (v.points[3] - v.points[0]) / 3f;
            for (int k = 4; k < 8; k++) p[k] = v.points[0] + step * k;
            v.points = p;
        }

        /// <summary>Spreads pins 1-8 evenly between two clicked end pins (used by calibration).</summary>
        public static Vector2[] Interpolate(Vector2 pin1, Vector2 pin8)
        {
            var p = new Vector2[8];
            for (int k = 0; k < 8; k++) p[k] = Vector2.Lerp(pin1, pin8, k / 7f);
            return p;
        }
    }
}
