using System;
using AmanatsuLocationVR;
using UnityEngine;
using N = System.Numerics;

static class Program
{
    static int checks;
    static Vector3 V(float x, float y = 0, float z = 0) => new(x, y, z);
    static Quaternion Q(float pitch = 0, float yaw = 0, float roll = 0) => Quaternion.Euler(pitch, yaw, roll);
    static void Check(bool condition, string message) { ++checks; if (!condition) throw new Exception(message); }
    static void Near(Vector3 actual, Vector3 expected, string message) => Check(
        N.Vector3.Distance(actual.Value, expected.Value) < .0002f, message + $": {actual} versus {expected}");
    static void Near(Quaternion actual, Quaternion expected, string message) => Check(
        Math.Abs(N.Quaternion.Dot(actual.Value, expected.Value)) > .99999f, message);
    static bool Accept(TrackingReference t, Vector3 p, Quaternion q, ulong generation = 1,
        bool connected = true, bool valid = true, int activity = 1) => t.Accept(p, q, connected, valid, activity, generation);
    static void Main()
    {
        StartupTiltAndGravity(); OriginGenerations(); LostTrackingAndRemoval(); PinReleaseAnchor();
        AllHudCombinations(); MatrixRoundTrips(); PhysicalEyeSeparation(); LifecycleReset();
        Console.WriteLine($"PASS: {checks} tracking assertions across 8 scenarios.");
    }
    static void StartupTiltAndGravity()
    {
        foreach (float pitch in new[] { -85f, -35f, 0f, 70f })
        foreach (float roll in new[] { -90f, -20f, 0f, 110f })
        {
            var t = new TrackingReference(); var desk = Q(pitch, 127, roll);
            Check(Accept(t, V(2, .8f, -3), desk), "First usable pose anchors position.");
            t.Applied(true, true, false, out var p, out var q);
            Near(p, Vector3.zero, "Startup XYZ is locally anchored.");
            Near(q, desk, "Startup orientation must retain actual pitch/roll/yaw.");
            var standing = Q(0, 127, 0);
            Accept(t, V(2, 1.7f, -3), standing);
            t.Applied(true, true, false, out p, out q);
            Near(q, standing, "Standing upright removes physical startup tilt.");
            Near(q * Vector3.up, Vector3.up, "Runtime gravity remains upright without a startup quaternion correction.");
            Near(p, V(0, .9f, 0), "Standing translation is physical delta when unpinned.");
        }
    }
    static void OriginGenerations()
    {
        var t = new TrackingReference(); Accept(t, V(1, 2, 3), Q(20, 30, 40), 7);
        Check(!Accept(t, V(2, 4, 6), Q(-10, 80, 25), 7), "Same generation updates without reanchoring.");
        t.Applied(true, true, false, out var p, out var q); Near(p, V(1, 2, 3), "Same-origin XYZ movement is retained.");
        Near(q, Q(-10, 80, 25), "Orientation never becomes startup-relative.");
        var resetQ = Q(12, -135, -17);
        Check(Accept(t, V(-8, .4f, 2), resetQ, 8), "Runtime generation change reanchors.");
        t.Applied(true, true, false, out p, out q); Near(p, Vector3.zero, "New runtime origin zeros only XYZ.");
        Near(q, resetQ, "Generation change preserves new runtime orientation.");
        Check(t.OriginGeneration == 8, "Accepted generation is recorded.");
        t.RequestPositionAnchor();
        Check(!Accept(t, V(999), Q(90), 9, valid: false), "Invalid sample cannot consume pending position reset.");
        Check(t.OriginGeneration == 8, "Invalid generation is not adopted.");
        var recovered = Q(-8, 175, 2);
        Check(Accept(t, V(4, 5, 6), recovered, 9), "Recovery adopts latest origin and consumes pending reset.");
        t.Applied(true, true, false, out p, out q); Near(p, Vector3.zero, "Recovery position is anchored at the live pose.");
        Near(q, recovered, "Recovery never anchors rotation.");
    }
    static void LostTrackingAndRemoval()
    {
        var t = new TrackingReference(); var last = V(1, 2, 3); var orientation = Q(25, 80, -40);
        Check(!Accept(t, last, orientation, valid: false) && !t.Ready, "Invalid startup remains unready.");
        Accept(t, last, orientation);
        foreach (var rejected in new (bool connected, bool valid, int activity)[] {
            (false, true, 1), (true, false, 1),
            (true, true, 0), (true, true, 3), (true, true, 4) })
        {
            Check(!Accept(t, V(200), Q(90, 90, 90), 2, rejected.connected, rejected.valid, rejected.activity), "Unusable pose rejected.");
            Near(t.Position, last, "Invalid/removed HMD freezes last accepted position.");
            Near(t.Rotation, orientation, "Invalid/removed HMD freezes last accepted rotation.");
            Check(t.Ready && t.OriginGeneration == 1, "Loss retains last usable reference rather than adopting invalid origin.");
        }
        Check(Accept(t, V(4), Q(0, 20), 2, activity: -1), "Drivers without activity data accept valid recovery.");
        Check(!Accept(t, V(5), Q(0, 30), 2, activity: 2), "Recent interaction updates normally without extra anchoring.");
        Near(t.Position, V(5), "Recent-interaction pose stays live.");
    }
    static void PinReleaseAnchor()
    {
        var t = new TrackingReference(); Accept(t, V(0), Q());
        var turned = Q(22, 63, -13); Accept(t, V(.8f, .2f, -.3f), turned);
        t.Applied(true, true, true, out var p, out var q);
        Near(p, Vector3.zero, "Pinned lean is not applied."); Near(q, turned, "Pinning does not suppress orientation.");
        t.AnchorCurrentPosition(); t.Applied(true, true, false, out p, out q);
        Near(p, Vector3.zero, "Releasing pin begins at current XYZ without old-lean jump.");
        Near(q, turned, "Pin-release anchoring cannot change orientation.");
        Accept(t, V(.9f, .15f, -.1f), Q(30, 80, -5));
        t.Applied(true, true, false, out p, out q);
        Near(p, V(.1f, -.05f, .2f), "New physical translation after releasing pin follows delta.");
        Near(q, Q(30, 80, -5), "Post-release orientation remains raw runtime orientation.");
    }
    static void AllHudCombinations()
    {
        var points = new[] { V(0, -.2f, 1.3f), V(-.6f, .25f, 1.3f), V(.4f, -.4f, 1.3f), V(.1f, .2f, -.7f) };
        for (int poseCase = 0; poseCase < 12; ++poseCase)
        foreach (bool rotationEnabled in new[] { false, true })
        foreach (bool positionEnabled in new[] { false, true })
        foreach (bool pin in new[] { false, true })
        {
            var t = new TrackingReference();
            Accept(t, V(.3f, .8f, -1), Q(-42, 20, 73));
            var rawPosition = V(.6f + poseCase * .07f, 1.4f, -.5f);
            var rawRotation = Q(-80 + poseCase * 15, 127 - poseCase * 13, -35 + poseCase * 7);
            Accept(t, rawPosition, rawRotation);
            t.Applied(rotationEnabled, positionEnabled, pin, out var appliedPosition, out var appliedRotation);
            var m = new float[12]; TrackingReference.HudAnchor(rawPosition, rawRotation, appliedPosition, appliedRotation, m);
            StereoMath.Pose(m, out var anchorPosition, out var anchorRotation);

            foreach (var point in points)
            {
                var renderedPanelInHead = N.Vector3.Transform(point.Value - appliedPosition.Value,
                    N.Quaternion.Inverse(appliedRotation.Value));
                var runtimePoint = N.Vector3.Transform(point.Value, anchorRotation.Value) + anchorPosition.Value;
                var overlayPanelInHead = N.Vector3.Transform(runtimePoint - rawPosition.Value, N.Quaternion.Inverse(rawRotation.Value));
                Check(N.Vector3.Distance(renderedPanelInHead, overlayPanelInHead) < .0002f,
                    $"HUD invariant failed: pose={poseCase}, rotation={rotationEnabled}, position={positionEnabled}, pin={pin}.");
            }
            var raw = N.Matrix4x4.CreateFromQuaternion(rawRotation.Value) * N.Matrix4x4.CreateTranslation(rawPosition.Value);
            var anchor = N.Matrix4x4.CreateFromQuaternion(anchorRotation.Value) * N.Matrix4x4.CreateTranslation(anchorPosition.Value);
            var applied = N.Matrix4x4.CreateFromQuaternion(appliedRotation.Value) * N.Matrix4x4.CreateTranslation(appliedPosition.Value);
            var rawInvertible = N.Matrix4x4.Invert(raw, out var inverseRaw);
            var appliedInvertible = N.Matrix4x4.Invert(applied, out var inverseApplied);
            Check(rawInvertible && appliedInvertible, "Pose matrices invert.");

            var error = anchor * inverseRaw - inverseApplied;
            Check(MaxAbs(error) < .0002f, "Full rigid matrix HUD invariant holds.");
        }
    }
    static float MaxAbs(N.Matrix4x4 m)
    {
        float max = 0;
        foreach (var x in new[] {m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44})
            max = Math.Max(max, Math.Abs(x));
        return max;
    }
    static void MatrixRoundTrips()
    {
        foreach (var q in new[] { Q(), Q(90), Q(-90), Q(0,180), Q(0,0,180), Q(40,-160,70), Q(-89,179,-120) })
        {
            var expected = V(-2.4f, 1.71f, 3.2f); var m = new float[12]; TrackingReference.ToOpenVr(expected, q, m);
            StereoMath.Pose(m, out var p, out var decoded);
            Near(p, expected, "Unity/OpenVR position round trip."); Near(decoded, q, "Unity/OpenVR rotation round trip, including vertical and inverted poses.");
            Check(Math.Abs(m[3] - expected.x) < 1e-6f && Math.Abs(m[11] + expected.z) < 1e-6f, "OpenVR matrix applies Z handedness conversion.");
            var right = new N.Vector3(m[0],m[4],m[8]); var up = new N.Vector3(m[1],m[5],m[9]); var forward = new N.Vector3(m[2],m[6],m[10]);
            Check(Math.Abs(N.Vector3.Dot(N.Vector3.Cross(right, up), forward) - 1) < .0002f, "OpenVR rigid matrix determinant stays positive one.");
        }
    }
    static void PhysicalEyeSeparation()
    {
        var t = new TrackingReference(); Accept(t, V(0), Q()); Accept(t, V(2, 3, 4), Q(25, 70, -15));
        var basePosition = V(8, 15, 22); var baseRotation = Q(-15, 110, 13); const float units = 10;
        var leftMatrix = new float[12]; var rightMatrix = new float[12];
        TrackingReference.ToOpenVr(V(-.032f), Q(), leftMatrix); TrackingReference.ToOpenVr(V(.032f), Q(), rightMatrix);
        StereoMath.Pose(leftMatrix, out var leftEyeOffset, out _); StereoMath.Pose(rightMatrix, out var rightEyeOffset, out _);
        foreach (bool rotationEnabled in new[] { false, true })
        foreach (bool positionEnabled in new[] { false, true })
        {
            t.Applied(rotationEnabled, positionEnabled, true, out var translation, out var rotation);
            var left = basePosition + baseRotation * (translation * units + rotation * (leftEyeOffset * units));
            var right = basePosition + baseRotation * (translation * units + rotation * (rightEyeOffset * units));
            Check(Math.Abs(N.Vector3.Distance(left.Value, right.Value) - .064f * units) < .0002f, "Pinning never removes physical IPD.");
            Near((left + right) * .5f, basePosition, "Pinned binocular center remains at game base.");
        }
    }
    static void LifecycleReset()
    {
        var t = new TrackingReference(); t.AnchorCurrentPosition(); Check(!t.Ready, "Position anchor cannot invent a startup pose.");
        Accept(t, V(4, 5, 6), Q(25, 70, 35), 10); t.Reset();
        Check(!t.Ready && t.OriginGeneration == 0, "Stop/restart clears reference readiness.");
        Check(!Accept(t, V(1), Q(), valid: false) && !t.Ready, "Invalid first pose after restart cannot reactivate old pose.");
        Check(Accept(t, V(8, 1, 2), Q(-25, 15, -40), 1), "Restart accepts a fresh runtime generation.");
        t.Applied(true, true, false, out var p, out var q); Near(p, Vector3.zero, "Restart anchors current position.");
        Near(q, Q(-25, 15, -40), "Restart does not retain old orientation offset.");
    }
}
