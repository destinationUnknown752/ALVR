using System;
using UnityEngine;

namespace AmanatsuLocationVR
{

    internal static class HudSurfaceMath
    {
        public const int HorizontalSegments = 64;
        public static float ScreenToSurfaceV(float screenV, bool flipY) => flipY ? screenV : 1f - screenV;

        public static bool TryLocalPoint(float u, float v, float width, float distance, float aspect,
            float verticalOffset, float curvature, out Vector3 metres)
        {
            metres = Vector3.zero;
            if (!Finite(u) || !Finite(v) || !Finite(width) || !Finite(distance) || !Finite(aspect) ||
                !Finite(verticalOffset) || !Finite(curvature) || u < 0f || u > 1f || v < 0f || v > 1f ||
                width <= 0f || distance <= 0f || aspect <= 0f || curvature < 0f || curvature > 1f) return false;
            double y = (v - .5) * width / aspect + verticalOffset;
            double x, z;
            if (curvature < .00001f)
            {
                x = (u - .5) * width;
                z = distance;
            }
            else
            {
                double segment = u * HorizontalSegments;
                int left = Math.Min(HorizontalSegments - 1, (int)segment);
                double t = segment - left;
                double arc = curvature * Math.PI;
                double radius = width / arc;
                double a0 = ((double)left / HorizontalSegments - .5) * arc;
                double a1 = ((double)(left + 1) / HorizontalSegments - .5) * arc;
                x = radius * (Math.Sin(a0) * (1 - t) + Math.Sin(a1) * t);
                z = distance + radius * ((Math.Cos(a0) - 1) * (1 - t) + (Math.Cos(a1) - 1) * t);
            }
            metres = new Vector3((float)x, (float)y, (float)z);
            return Finite(metres.x) && Finite(metres.y) && Finite(metres.z);
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
