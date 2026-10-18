using AmanatsuLocationVR;
using UnityEngine;

int count = 0;
foreach (float units in new[] { 1f, 10f, 25f })
foreach (var physicalEye in new[]
{
    new Vector3(0f, 0f, 0f), new Vector3(0.3f, 0.2f, 0.4f),
    new Vector3(-0.2f, -0.15f, -0.3f), new Vector3(1f, 0f, 0f)
})
foreach (float aspect in new[] { 16f / 9f, 4f / 3f, 1f })
{
    var eye = physicalEye * units;
    float width = 1.4f * units, height = width / aspect, distance = 1.2f * units;
    if (!HudPointerMath.TryProjection(eye, width, height, distance, 0.01f * units, 1000f * units, out var p))
        throw new Exception("Valid HUD geometry rejected");
    foreach (var uv in new[] { (0f, 0f), (1f, 1f), (0f, 1f), (1f, 0f), (0.5f, 0.5f), (0.27f, 0.81f) })
    {

        float rayX = ((2f * uv.Item1 - 1f) + p.m02) / p.m00;
        float rayY = ((2f * uv.Item2 - 1f) + p.m12) / p.m11;
        float t = distance - eye.z;
        Close(eye.x + rayX * t, (uv.Item1 - 0.5f) * width);
        Close(eye.y + rayY * t, (uv.Item2 - 0.5f) * height);
        ++count;
    }
}

foreach (var data in new[]
{
    (new Vector3(0, 0, 1.2f), 1.4f, 0.8f, 1.2f, 0.01f, 100f),
    (new Vector3(0, 0, 1.3f), 1.4f, 0.8f, 1.2f, 0.01f, 100f),
    (new Vector3(float.NaN, 0, 0), 1.4f, 0.8f, 1.2f, 0.01f, 100f),
    (new Vector3(0, 0, 0), 0f, 0.8f, 1.2f, 0.01f, 100f),
    (new Vector3(0, 0, 0), 1.4f, float.PositiveInfinity, 1.2f, 0.01f, 100f),
    (new Vector3(0, 0, 0), 1.4f, 0.8f, 1.2f, 0f, 100f),
    (new Vector3(0, 0, 0), 1.4f, 0.8f, 1.2f, 1f, 1f)
})
{
    if (HudPointerMath.TryProjection(data.Item1, data.Item2, data.Item3, data.Item4, data.Item5, data.Item6, out _))
        throw new Exception("Invalid HUD geometry accepted");
}
Console.WriteLine($"PASS {count} HUD pixel/ray landmarks and 7 invalid-input cases.");

static void Close(float actual, float expected)
{
    if (!float.IsFinite(actual) || Math.Abs(actual - expected) > 0.0001f)
        throw new Exception($"HUD pixel mismatch: expected {expected}, got {actual}");
}
