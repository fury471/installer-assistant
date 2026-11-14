using System;
using UnityEngine;

namespace InstallerAssistant.AR
{
    /// <summary>Image coordinates are top-left, as returned by perception; viewports are Unity's bottom-left.
    /// Invert the same row-vector transform that ARCoreBackground.shader uses to sample the camera image.
    /// Intrinsics describe the sensor, not the portrait display, and cannot replace this rotation/crop.</summary>
    public static class CameraImageGeometry
    {
        public static bool TryViewport(Vector2 image, Matrix4x4 display, out Vector2 viewport)
        {
            viewport = Vector2.zero;
            // textureUV = mul(float4(viewport.x, viewport.y, 1, 0), display).xy
            float a = display.m00, b = display.m10, c = display.m01, d = display.m11;
            float det = a * d - b * c;
            if (Mathf.Abs(det) < 1e-6f) return false;
            float x = image.x - display.m20, y = image.y - display.m21;
            viewport = new Vector2((d * x - b * y) / det, (a * y - c * x) / det);
            return Finite(viewport.x) && Finite(viewport.y);
        }

        public static bool TryCameraRay(Vector2 image, Matrix4x4 display, Matrix4x4 projection, out Vector3 ray)
        {
            ray = Vector3.zero;
            Vector2 vp;
            if (!TryViewport(image, display, out vp) || Mathf.Abs(projection.determinant) < 1e-8f) return false;
            Vector4 p = projection.inverse * new Vector4(2f * vp.x - 1f, 2f * vp.y - 1f, 1f, 1f);
            if (Mathf.Abs(p.w) < 1e-8f) return false;
            ray = new Vector3(p.x, p.y, p.z) / p.w;
            return Finite(ray.x) && Finite(ray.y) && Finite(ray.z) && ray.z < -1e-6f;
        }

        /// <summary>
        /// The camera ray through an image point from the CPU image's own intrinsics (pinhole model; Hartley and
        /// Zisserman, 2004, ch. 6), with only the quarter-turn and mirror taken from the display transform. The
        /// display transform maps the screen to the GPU texture; ARCore's CPU image may cover a different field of
        /// view than that texture, and then the display path misplaces every point away from the centre, while
        /// the intrinsics belong to the image the reader saw. image: normalised, top-left, x right, y down.
        /// intrinsics: fx, fy, cx, cy in pixels of a width x height image. Ray in camera space (-z forward).
        /// </summary>
        public static bool TryIntrinsicsRay(Vector2 image, int width, int height, float[] intrinsics, Matrix4x4 display, out Vector3 ray)
        {
            ray = Vector3.zero;
            int sxa, sxb, sya, syb;
            if (intrinsics == null || intrinsics.Length < 4 || width <= 0 || height <= 0 ||
                intrinsics[0] <= 0 || intrinsics[1] <= 0 || !QuarterTurn(display, out sxa, out sxb, out sya, out syb)) return false;
            float a = (image.x * width - intrinsics[2]) / intrinsics[0];     // tangent along the image's x
            float b = (image.y * height - intrinsics[3]) / intrinsics[1];    // tangent along the image's y (down)
            ray = new Vector3(sxa * a + sxb * b, sya * a + syb * b, -1f);
            return Finite(ray.x) && Finite(ray.y);
        }

        /// <summary>The inverse of TryIntrinsicsRay: where a camera-space direction lands in the image (normalised).</summary>
        public static bool TryIntrinsicsImage(Vector3 ray, int width, int height, float[] intrinsics, Matrix4x4 display, out Vector2 image)
        {
            image = Vector2.zero;
            int sxa, sxb, sya, syb;
            if (ray.z > -1e-6f || intrinsics == null || intrinsics.Length < 4 || width <= 0 || height <= 0 ||
                !QuarterTurn(display, out sxa, out sxb, out sya, out syb)) return false;
            float tx = ray.x / -ray.z, ty = ray.y / -ray.z;
            // the signed permutation is its own inverse transposed
            float a = sxa * tx + sya * ty, b = sxb * tx + syb * ty;
            image = new Vector2((a * intrinsics[0] + intrinsics[2]) / width, (b * intrinsics[1] + intrinsics[3]) / height);
            return Finite(image.x) && Finite(image.y);
        }

        /// <summary>
        /// The signed permutation between the image axes and the camera's x right / y up, read from the display
        /// transform: view tangent x = sxa * a + sxb * b, y = sya * a + syb * b, where a and b are the tangents along
        /// the image's x and y. The display transform's 2 x 2 part maps viewport to texture; its inverse maps image
        /// steps to viewport steps with the same signs and arrangement (the scales are positive per axis).
        /// </summary>
        public static bool QuarterTurn(Matrix4x4 display, out int sxa, out int sxb, out int sya, out int syb)
        {
            sxa = sxb = sya = syb = 0;
            // textureUV = (vx, vy) * [[m00, m01], [m10, m11]] + (m20, m21): J = [[m00, m10], [m01, m11]] (column form)
            float j00 = display.m00, j01 = display.m10, j10 = display.m01, j11 = display.m11;
            float det = j00 * j11 - j01 * j10;
            if (Mathf.Abs(det) < 1e-6f) return false;
            float k00 = j11 / det, k01 = -j01 / det, k10 = -j10 / det, k11 = j00 / det;   // J^-1: image step -> viewport step
            if (Mathf.Abs(k00) + Mathf.Abs(k11) >= Mathf.Abs(k01) + Mathf.Abs(k10))
            {
                sxa = Math.Sign(k00); syb = Math.Sign(k11);
                return sxa != 0 && syb != 0;
            }
            sxb = Math.Sign(k01); sya = Math.Sign(k10);
            return sxb != 0 && sya != 0;
        }

        /// <summary>Use the displayed RawImage's real UV rectangle and transform, including rotation, mirroring,
        /// and envelope crop. The marker layer itself is not rotated.</summary>
        public static bool TryWebcamViewport(Vector2 image, Rect uv, RectTransform picture, RectTransform markers, out Vector2 position)
        {
            position = Vector2.zero;
            if (picture == null || markers == null || Mathf.Abs(uv.width) < 1e-6f || Mathf.Abs(uv.height) < 1e-6f ||
                markers.rect.width <= 0 || markers.rect.height <= 0) return false;
            float u = (image.x - uv.x) / uv.width, v = (1f - image.y - uv.y) / uv.height;
            Vector3 world = picture.TransformPoint(new Vector3(picture.rect.xMin + u * picture.rect.width,
                                                              picture.rect.yMin + v * picture.rect.height, 0));
            Vector3 local = markers.InverseTransformPoint(world);
            position = new Vector2((local.x - markers.rect.xMin) / markers.rect.width,
                                  1f - (local.y - markers.rect.yMin) / markers.rect.height);
            return Finite(position.x) && Finite(position.y);
        }

        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
