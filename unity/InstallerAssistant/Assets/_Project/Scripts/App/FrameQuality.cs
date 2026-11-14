using InstallerAssistant.Detection;

namespace InstallerAssistant.App
{
    /// <summary>
    /// How sharp a camera frame is, so the app can send the reader the sharpest of a short burst instead of the
    /// frame that happened to be there when the installer tapped (package C). On 14 Nov, 13 of 17 reads were
    /// refused by the service as blurred: the phone was still moving when the single frame was taken.
    /// The measure is the variance of the Laplacian (a standard focus measure; Pertuz et al., 2013), the same the
    /// service uses, on every fourth pixel of the middle of the frame. Only frames can be compared that show the
    /// same scene, which a burst of half a second does. Plain C#, no Unity types.
    /// </summary>
    public static class FrameQuality
    {
        /// <summary>Variance of the Laplacian of the frame's middle (60 % of each side), or -1 when the frame has no
        /// RGBA pixels to measure.</summary>
        public static double Sharpness(CameraFrame frame)
        {
            if (frame == null || frame.Format != FramePixels.Rgba32 || frame.Pixels == null ||
                frame.Width < 64 || frame.Height < 64 || frame.Pixels.Length < frame.Width * frame.Height * 4) return -1;
            const int step = 4;
            int w = frame.Width, h = frame.Height;
            int x0 = w / 5, x1 = w - w / 5, y0 = h / 5, y1 = h - h / 5;
            int gw = (x1 - x0) / step, gh = (y1 - y0) / step;
            if (gw < 8 || gh < 8) return -1;
            var grey = new int[gw * gh];
            var p = frame.Pixels;
            for (int y = 0; y < gh; y++)
            {
                int row = (y0 + y * step) * w;
                for (int x = 0; x < gw; x++)
                {
                    int i = (row + x0 + x * step) * 4;
                    grey[y * gw + x] = (p[i] + 2 * p[i + 1] + p[i + 2]) >> 2;      // close to luminance, in integers
                }
            }
            double sum = 0, squares = 0;
            int n = 0;
            for (int y = 1; y < gh - 1; y++)
                for (int x = 1; x < gw - 1; x++)
                {
                    int i = y * gw + x;
                    double l = grey[i - 1] + grey[i + 1] + grey[i - gw] + grey[i + gw] - 4 * grey[i];
                    sum += l; squares += l * l; n++;
                }
            double mean = sum / n;
            return squares / n - mean * mean;
        }
    }
}
