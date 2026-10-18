using System;
using System.Collections.Generic;
using N = System.Numerics;

namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public readonly List<string> Messages = new List<string>();
        public void LogInfo(object value) => Messages.Add(value.ToString());
        public void LogWarning(object value) => Messages.Add(value.ToString());
    }
}
namespace UnityEngine
{
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
        internal N.Vector3 Value => new(x, y, z);
        internal static Vector3 From(N.Vector3 value) => new(value.X, value.Y, value.Z);
        public static Vector3 zero => new(0, 0, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => From(a.Value + b.Value);
        public static Vector3 operator -(Vector3 a, Vector3 b) => From(a.Value - b.Value);
        public static Vector3 operator *(Vector3 a, float b) => From(a.Value * b);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => From(N.Vector3.Lerp(a.Value, b.Value, Math.Clamp(t, 0, 1)));
        public override string ToString() => $"({x},{y},{z})";
    }
    public readonly struct Quaternion(N.Quaternion value)
    {
        internal readonly N.Quaternion Value = value;
        public static Quaternion identity => new(N.Quaternion.Identity);
        public static Quaternion Euler(Vector3 euler) => new(N.Quaternion.CreateFromYawPitchRoll(euler.y * MathF.PI / 180,
            euler.x * MathF.PI / 180, euler.z * MathF.PI / 180));
        public static Quaternion Inverse(Quaternion q) => new(N.Quaternion.Inverse(q.Value));
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => new(N.Quaternion.Slerp(a.Value, b.Value, Math.Clamp(t, 0, 1)));
        public static Quaternion operator *(Quaternion a, Quaternion b) => new(a.Value * b.Value);
        public static Vector3 operator *(Quaternion q, Vector3 p) => Vector3.From(N.Vector3.Transform(p.Value, q.Value));
    }
}
