namespace InstallerAssistant.Detection
{
    public enum FramePixels { None, Rgba32, Yuv420, Jpeg }

    /// <summary>
    /// One camera frame, handed from the AR runtime (package B) to the readers (package A).
    /// Plain C#, no Unity types, so the same data can be sent to a perception service as it is.
    /// </summary>
    public sealed class CameraFrame
    {
        /// <summary>Increasing id; readings and label scans refer back to it.</summary>
        public long Id;
        /// <summary>Capture time in seconds (AR session clock).</summary>
        public double Timestamp;
        public int Width, Height;
        public FramePixels Format;
        /// <summary>The image; null in the Wizard-of-Oz build, where nothing reads pixels.</summary>
        public byte[] Pixels;
        /// <summary>Camera intrinsics in pixels: fx, fy, cx, cy (null if unknown).</summary>
        public float[] Intrinsics;
        /// <summary>Camera-to-world pose, 4x4 column-major in AR session space (null if unknown).</summary>
        public float[] Pose;
        /// <summary>Where the frame came from, e.g. "arcore" or "photo:controller_fixed".</summary>
        public string Source;
    }

    /// <summary>Gives the latest camera frame. Owner: package B (AR runtime). Wizard of Oz: the photo on screen.</summary>
    public interface ICameraFrameSource
    {
        CameraFrame Latest { get; }
    }
}
