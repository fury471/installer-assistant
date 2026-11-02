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
