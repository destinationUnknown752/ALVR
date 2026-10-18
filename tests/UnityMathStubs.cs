

using N = System.Numerics;

namespace UnityEngine;

public struct Vector3(float x, float y, float z)
{
    public float x = x, y = y, z = z;
    public static Vector3 zero => new(0, 0, 0);
    public static Vector3 forward => new(0, 0, 1);
    public static Vector3 right => new(1, 0, 0);
    public static Vector3 up => new(0, 1, 0);
    public float sqrMagnitude => x * x + y * y + z * z;
    public Vector3 normalized => From(N.Vector3.Normalize(Value));
    public static Vector3 operator +(Vector3 a, Vector3 b) => From(a.Value + b.Value);
    public static Vector3 operator -(Vector3 a, Vector3 b) => From(a.Value - b.Value);
    public static Vector3 operator *(Vector3 a, float scale) => From(a.Value * scale);
    internal N.Vector3 Value => new(x, y, z);
    internal static Vector3 From(N.Vector3 v) => new(v.X, v.Y, v.Z);
    public override string ToString() => $"({x:F6}, {y:F6}, {z:F6})";
}

public readonly struct Quaternion(N.Quaternion value)
{
    internal readonly N.Quaternion Value = value;
    public static Quaternion Inverse(Quaternion rotation) => new(N.Quaternion.Inverse(rotation.Value));
    public static Quaternion operator *(Quaternion a, Quaternion b) => new(a.Value * b.Value);
    public static Quaternion LookRotation(Vector3 forward, Vector3 up)
    {

        var z = N.Vector3.Normalize(forward.Value);
        var x = N.Vector3.Normalize(N.Vector3.Cross(up.Value, z));
        var y = N.Vector3.Cross(z, x);
        var m = new N.Matrix4x4(
            x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        return new Quaternion(N.Quaternion.Normalize(N.Quaternion.CreateFromRotationMatrix(m)));
    }
    public static Vector3 operator *(Quaternion rotation, Vector3 point) =>
        Vector3.From(N.Vector3.Transform(point.Value, rotation.Value));
}

public struct Matrix4x4
{
    public float m00, m01, m02, m03;
    public float m10, m11, m12, m13;
    public float m20, m21, m22, m23;
    public float m30, m31, m32, m33;
}
