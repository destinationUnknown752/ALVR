using UnityEngine;

namespace AmanatsuLocationVR
{
    internal static class StereoMath
    {

        internal static void Pose(float[] m, out Vector3 position, out Quaternion rotation)
        {
            position = new Vector3(m[3], m[7], -m[11]);
            rotation = Quaternion.LookRotation(new Vector3(-m[2], -m[6], m[10]), new Vector3(m[1], m[5], -m[9]));
        }
        internal static Quaternion Heading(Quaternion rotation)
        {
            var forward = rotation * Vector3.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
            {
                var right = rotation * Vector3.right;
                forward = new Vector3(-right.z, 0f, right.x);
            }
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
        internal static Matrix4x4 Projection(float[] raw, float near, float far)
        {

            float left = raw[0] * near, right = raw[1] * near;
            float bottom = raw[2] * near, top = raw[3] * near;
            var m = new Matrix4x4();
            m.m00 = 2f * near / (right - left);
            m.m02 = (right + left) / (right - left);
            m.m11 = 2f * near / (top - bottom);
            m.m12 = (top + bottom) / (top - bottom);
            m.m22 = -(far + near) / (far - near);
            m.m23 = -2f * far * near / (far - near);
            m.m32 = -1f;
            return m;
        }
    }
}
