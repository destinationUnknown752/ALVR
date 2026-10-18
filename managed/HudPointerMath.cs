using UnityEngine;

namespace AmanatsuLocationVR
{
    internal static class HudPointerMath
    {

        internal static bool TryProjection(Vector3 eyeOffsetInBase, float widthUnits,
            float heightUnits, float distanceUnits, float near, float far, out Matrix4x4 projection)
        {
            projection = default(Matrix4x4);
            if (!Finite(eyeOffsetInBase.x) || !Finite(eyeOffsetInBase.y) || !Finite(eyeOffsetInBase.z) ||
                !Finite(widthUnits) || !Finite(heightUnits) || !Finite(distanceUnits) ||
                !Finite(near) || !Finite(far) || widthUnits <= 0f || heightUnits <= 0f ||
                distanceUnits <= 0f || near <= 0f || far <= near) return false;

            float depth = distanceUnits - eyeOffsetInBase.z;

            if (!Finite(depth) || depth <= near) return false;

            var bounds = new[]
            {
                (-widthUnits * 0.5f - eyeOffsetInBase.x) / depth,
                ( widthUnits * 0.5f - eyeOffsetInBase.x) / depth,
                (-heightUnits * 0.5f - eyeOffsetInBase.y) / depth,
                ( heightUnits * 0.5f - eyeOffsetInBase.y) / depth
            };
            foreach (float value in bounds) if (!Finite(value)) return false;
            if (bounds[1] <= bounds[0] || bounds[3] <= bounds[2]) return false;
            projection = StereoMath.Projection(bounds, near, far);
            return Finite(projection.m00) && Finite(projection.m11) && Finite(projection.m02) &&
                Finite(projection.m12) && Finite(projection.m22) && Finite(projection.m23);
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
