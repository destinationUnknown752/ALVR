using AmanatsuLocationVR;
using UnityEngine;

var cases = new (string Name, Action Run)[]
{
    ("Identity pose and forward translation", () => {
        StereoMath.Pose(Pose(1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, -4), out var p, out var q);
        V(p, 2, 3, 4); V(q * new Vector3(0, 0, 1), 0, 0, 1); V(q * new Vector3(0, 1, 0), 0, 1, 0);
    }),
    ("RH yaw +90 maps to Unity looking left", () => {
        StereoMath.Pose(Pose(0, 0, 1, 0, 1, 0, -1, 0, 0), out _, out var q);
        V(q * new Vector3(0, 0, 1), -1, 0, 0); V(q * new Vector3(1, 0, 0), 0, 0, 1);
    }),
    ("RH pitch +90 maps to Unity looking up", () => {
        StereoMath.Pose(Pose(1, 0, 0, 0, 0, -1, 0, 1, 0), out _, out var q);
        V(q * new Vector3(0, 0, 1), 0, 1, 0); V(q * new Vector3(0, 1, 0), 0, 0, -1);
    }),
    ("RH roll +90 retains roll direction", () => {
        StereoMath.Pose(Pose(0, -1, 0, 1, 0, 0, 0, 0, 1), out _, out var q);
        V(q * new Vector3(0, 0, 1), 0, 0, 1); V(q * new Vector3(0, 1, 0), -1, 0, 0);
    }),
    ("Half-turn pose avoids quaternion singularity", () => {
        StereoMath.Pose(Pose(-1, 0, 0, 0, 1, 0, 0, 0, -1), out _, out var q);
        V(q * new Vector3(0, 0, 1), 0, 0, -1); V(q * new Vector3(0, 1, 0), 0, 1, 0);
    }),
    ("Eye transforms preserve left/right IPD", () => {
        StereoMath.Pose(Pose(1, 0, 0, 0, 1, 0, 0, 0, 1, -0.032f), out var l, out _);
        StereoMath.Pose(Pose(1, 0, 0, 0, 1, 0, 0, 0, 1, 0.032f), out var r, out _);
        V(l, -0.032f, 0, 0); V(r, 0.032f, 0, 0); Eq(r.x - l.x, 0.064f);
    }),
    ("Canted eye remains canted relative to turned head", () => {
        float c = MathF.Cos(10 * MathF.PI / 180), s = MathF.Sin(10 * MathF.PI / 180);
        StereoMath.Pose(Pose(c, 0, s, 0, 1, 0, -s, 0, c, -0.032f), out var ep, out var eq);
        StereoMath.Pose(Pose(0, 0, 1, 0, 1, 0, -1, 0, 0, 1, 2, -3), out var hp, out var hq);
        var offset = hq * ep;
        V(new Vector3(hp.x + offset.x, hp.y + offset.y, hp.z + offset.z), 1, 2, 2.968f);
        V(hq * (eq * new Vector3(0, 0, 1)), -c, 0, -s);
    }),
    ("Symmetric 90-degree projection corners and clipping depth", () => {
        var m = StereoMath.Projection([-1, 1, -1, 1], 0.1f, 100);
        Ndc(m, -0.1f, -0.1f, -0.1f, -1, -1, -1);
        Ndc(m, 0.1f, 0.1f, -0.1f, 1, 1, -1);
        Ndc(m, 0, 0, -100, 0, 0, 1);
    }),
    ("Asymmetric vertical/horizontal near-plane corners", () => {
        var m = StereoMath.Projection([-1.4f, 0.8f, -0.7f, 1.3f], 0.2f, 200);

        Ndc(m, -0.28f, -0.14f, -0.2f, -1, -1, -1);
        Ndc(m, 0.16f, 0.26f, -0.2f, 1, 1, -1);
        Ndc(m, -0.06f, 0.06f, -0.2f, 0, 0, -1);
    }),
    ("Asymmetric optical axis matches Valve ComposeProjection", () => {
        var m = StereoMath.Projection([-1.4f, 0.8f, -0.7f, 1.3f], 0.1f, 500);
        Eq(m.m02, -0.6f / 2.2f); Eq(m.m12, 0.6f / 2f);

        Ndc(m, 0, 0, -1, 0.6f / 2.2f, -0.3f, m.m22 * -1 + m.m23);
    }),
    ("Projection FOV does not depend on near distance", () => {
        var a = StereoMath.Projection([-1.2f, 0.9f, -0.8f, 1.1f], 0.01f, 100);
        var b = StereoMath.Projection([-1.2f, 0.9f, -0.8f, 1.1f], 0.4f, 100);
        Eq(a.m00, b.m00); Eq(a.m11, b.m11); Eq(a.m02, b.m02); Eq(a.m12, b.m12);
    }),
    ("Parallel stereo gives correct disparity and depth scaling", () => {
        var m = StereoMath.Projection([-1, 1, -1, 1], 0.1f, 100);
        float nearDisparity = X(m, 0.032f, -1) - X(m, -0.032f, -1);
        float farDisparity = X(m, 0.032f, -4) - X(m, -0.032f, -4);
        Eq(nearDisparity, 0.064f); Eq(farDisparity, 0.016f);
    }),
    ("Desk pitch and roll do not contaminate heading", () => {
        foreach (float yaw in new[] { -120f, -30f, 0f, 80f, 170f })
        foreach (float pitch in new[] { -75f, -35f, 0f, 45f, 75f })
        foreach (float roll in new[] { -120f, -45f, 0f, 60f, 150f })
        {
            var heading = StereoMath.Heading(Rotation(yaw, pitch, roll));
            float angle = yaw * MathF.PI / 180;
            V(heading * Vector3.forward, MathF.Sin(angle), 0, MathF.Cos(angle));
            V(heading * Vector3.up, 0, 1, 0);
        }
    }),
    ("Pitched game camera supplies heading while head stays level", () => {
        var camera = Rotation(70, -50, 25);
        var deskReference = Rotation(-30, 65, -80);
        var wornNeutral = Rotation(-30, 0, 0);
        var world = StereoMath.Heading(camera) * Quaternion.Inverse(StereoMath.Heading(deskReference)) * wornNeutral;
        float heading = 70 * MathF.PI / 180;
        V(world * Vector3.forward, MathF.Sin(heading), 0, MathF.Cos(heading));
        V(world * Vector3.up, 0, 1, 0);

        var oldWorld = camera * Quaternion.Inverse(deskReference) * wornNeutral;
        if (Math.Abs((oldWorld * Vector3.up).y - 1) < 0.1f)
            throw new Exception("Regression fixture no longer exercises a tilted tracking frame");
    }),
    ("Horizontal physical displacement remains horizontal with tilted references", () => {
        var alignment = StereoMath.Heading(Rotation(80, -50, 25)) *
            Quaternion.Inverse(StereoMath.Heading(Rotation(20, 65, -80)));
        var physicalForward = alignment * new Vector3(0, 0, 0.25f) * 10;
        V(physicalForward, 2.5f * MathF.Sin(MathF.PI / 3), 0, 2.5f * MathF.Cos(MathF.PI / 3));
        var physicalRight = alignment * new Vector3(0.25f, 0, 0) * 10;
        V(physicalRight, 2.5f * MathF.Cos(MathF.PI / 3), 0, -2.5f * MathF.Sin(MathF.PI / 3));
        V(alignment * new Vector3(0, 0.25f, 0) * 10, 0, 2.5f, 0);
    }),
    ("Rotation-only eye midpoint stays at the game camera", () => {
        var anchor = new Vector3(0, 14, 21.8f);
        var alignment = StereoMath.Heading(Rotation(170, -30, 0)) *
            Quaternion.Inverse(StereoMath.Heading(Rotation(-30, 65, -80)));
        foreach (var head in new[] { Rotation(-30, 0, 0), Rotation(20, -40, 15), Rotation(-90, 25, -30) })
        {
            var world = alignment * head;
            var left = anchor + world * new Vector3(-0.032f, 0, 0) * 10;
            var right = anchor + world * new Vector3(0.032f, 0, 0) * 10;
            V((left + right) * 0.5f, 0, 14, 21.8f);
            Eq((right - left).sqrMagnitude, 0.64f * 0.64f);
        }
    }),
    ("Vertical gaze uses a deterministic finite heading from right", () => {
        foreach (float pitch in new[] { -90f, 90f })
        foreach (float yaw in new[] { -120f, 0f, 45f })
        {
            var heading = StereoMath.Heading(Rotation(yaw, pitch, 0));
            float angle = yaw * MathF.PI / 180;
            V(heading * Vector3.forward, MathF.Sin(angle), 0, MathF.Cos(angle));
            V(heading * Vector3.up, 0, 1, 0);
        }

        var rolledUp = StereoMath.Heading(Rotation(0, 90, 30));
        V(rolledUp * Vector3.forward, -0.5f, 0, MathF.Sqrt(3) / 2);
        V(rolledUp * Vector3.up, 0, 1, 0);
    }),
};

int failures = 0;
foreach (var (name, run) in cases)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
}
Console.WriteLine($"{cases.Length - failures}/{cases.Length} math scenarios passed.");
return failures == 0 ? 0 : 1;

static float[] Pose(float a, float b, float c, float d, float e, float f, float g, float h, float i,
    float x = 0, float y = 0, float z = 0) => [a, b, c, x, d, e, f, y, g, h, i, z];
static Quaternion Rotation(float yaw, float pitch, float roll)
{
    const float radians = MathF.PI / 180;

    var y = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, yaw * radians);
    var x = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, pitch * radians);
    var z = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, roll * radians);
    return new Quaternion(y * x * z);
}
static void Eq(float actual, float expected)
{
    if (!float.IsFinite(actual) || Math.Abs(actual - expected) > 0.00002f)
        throw new Exception($"expected {expected:F6}, got {actual:F6}");
}
static void V(Vector3 value, float x, float y, float z) { Eq(value.x, x); Eq(value.y, y); Eq(value.z, z); }
static float X(Matrix4x4 m, float x, float z) => (m.m00 * x + m.m02 * z + m.m03) / (m.m32 * z + m.m33);
static void Ndc(Matrix4x4 m, float x, float y, float z, float ex, float ey, float ez)
{
    float w = m.m30 * x + m.m31 * y + m.m32 * z + m.m33;
    Eq((m.m00 * x + m.m01 * y + m.m02 * z + m.m03) / w, ex);
    Eq((m.m10 * x + m.m11 * y + m.m12 * z + m.m13) / w, ey);
    Eq((m.m20 * x + m.m21 * y + m.m22 * z + m.m23) / w, ez);
}
