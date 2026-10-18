using System;
using BepInEx.Logging;
using AmanatsuLocationVR;
using UnityEngine;
using N = System.Numerics;

static class Program
{
    static int checks;
    static readonly Quaternion Q = Quaternion.identity;
    static Vector3 V(float x, float y = 0, float z = 0) => new Vector3(x, y, z);
    static AnimationSample Sample(int phase = 1, string animation = "a", bool stable = true, bool pov = true,
        int source = 1, int character = 1) => new AnimationSample(source, 2, character, animation, phase, 0, pov, true, stable);
    static CameraFrame.Settings Options(DetachedTransitionPolicy policy = DetachedTransitionPolicy.Rebase, bool stabilize = false)
        => new CameraFrame.Settings { TransitionPolicy = policy, Stabilize = stabilize, PositionTauSeconds = .08f,
            RotationTauSeconds = .12f, StabilizationStrength = 1, PovPositionOffset = Vector3.zero, PovRotationEuler = Vector3.zero };
    static void Check(bool condition, string message) { ++checks; if (!condition) throw new Exception(message); }
    static void Near(Vector3 actual, Vector3 expected, string message)
        => Check(N.Vector3.Distance(actual.Value, expected.Value) < .0002f, message + ": " + actual + " versus " + expected);
    static void Near(Quaternion actual, Quaternion expected, string message)
        => Check(Math.Abs(N.Quaternion.Dot(actual.Value, expected.Value)) > .99999f, message);
    static void Eval(CameraFrame frame, Vector3 position, AnimationSample sample, CameraFrame.Settings options,
        out Vector3 output, out Quaternion rotation, Quaternion? liveRotation = null, float dt = .1f)
        => frame.Evaluate(position, liveRotation ?? Q, sample, dt, options, out output, out rotation);
    static void Main()
    {
        FreezeAndCurrentReattach(); OffsetsAndNonPov(); ExponentialRateIndependence(); FilteredFreeze();
        PhaseRebase(); FallbackPoliciesAndContext(); LiveOffsetsWhileDetached();
        ExitDropsAllPovRotationState();
        Console.WriteLine("PASS: " + checks + " camera reference assertions across 8 core scenarios.");
    }
    static void FreezeAndCurrentReattach()
    {
        var frame = new CameraFrame(new ManualLogSource()); var opt = Options();
        Eval(frame, V(0), Sample(), opt, out _, out _);
        frame.ToggleDetach(); Eval(frame, V(1), Sample(), opt, out var p, out _);
        Check(frame.Detached, "L must detach at final sample."); Near(p, V(1), "Detach captures current base.");
        Eval(frame, V(7), Sample(), opt, out p, out _); Near(p, V(1), "Detached game motion must not move output.");
        Near(frame.LatestLivePosition, V(7), "Detached state must still observe the live game pose.");
        frame.ToggleDetach(); var turned = Quaternion.Euler(V(0, 60));
        Eval(frame, V(9), Sample(), opt, out p, out var q, turned);
        Check(!frame.Detached, "Second L must reattach."); Near(p, V(9), "Reattach uses latest position, not cached detach position.");
        Near(q, turned, "Reattach uses latest orientation.");
    }
    static void OffsetsAndNonPov()
    {
        var frame = new CameraFrame(null); var opt = Options(stabilize: true);
        opt.PovPositionOffset = V(0, 0, 2); opt.PovRotationEuler = V(0, 15);
        var facing = Quaternion.Euler(V(0, 90));
        Eval(frame, V(0), Sample(), opt, out var p, out var q, facing);
        Near(p, V(2), "Position offset uses POV local axes."); Near(q, facing * Quaternion.Euler(V(0, 15)), "Euler offset follows base rotation.");
        Eval(frame, V(10), Sample(pov: false), opt, out p, out q, facing);
        Near(p, V(10), "Non-POV mouse camera must have no offsets or smoothing."); Near(q, facing, "Non-POV rotation remains direct.");
        frame.ToggleDetach(); Eval(frame, V(20), Sample(pov: false), opt, out p, out _);
        Check(!frame.Detached, "L outside POV must be ignored."); Near(p, V(20), "Non-POV movement remains current.");
    }
    static void ExponentialRateIndependence()
    {
        var one = new CameraFrame(null); var ten = new CameraFrame(null); var opt = Options(stabilize: true);
        Eval(one, V(0), Sample(), opt, out _, out _); Eval(ten, V(0), Sample(), opt, out _, out _);
        var turned = Quaternion.Euler(V(0, 90));
        Eval(one, V(10), Sample(), opt, out var p1, out var q1, turned, .1f);
        Vector3 p10 = default; Quaternion q10 = Q;
        for (int i = 0; i < 10; ++i) Eval(ten, V(10), Sample(), opt, out p10, out q10, turned, .01f);
        Near(p1, p10, "Exponential position filtering must be independent of frame subdivision.");
        Near(q1, q10, "Exponential rotation filtering must be independent of frame subdivision.");
        Near(p1, V(10 * (1 - MathF.Exp(-.1f / .08f))), "Position tau must match exponential response.");
        Near(q1, Quaternion.Slerp(Q, turned, 1 - MathF.Exp(-.1f / .12f)), "Rotation uses its separate tau.");
        opt.StabilizationStrength = 0; Eval(one, V(20), Sample(), opt, out var direct, out _, turned);
        Near(direct, V(20), "Zero strength must give raw base pose.");
    }
    static void LiveOffsetsWhileDetached()
    {
        var frame = new CameraFrame(null); var opt = Options(stabilize: true);
        var live = V(4, 2, 3); var facing = Quaternion.Euler(V(10, 90, 15));
        Eval(frame, live, Sample(), opt, out _, out _, facing);
        opt.PovPositionOffset = V(0, 0, 2); opt.PovRotationEuler = V(0, 25);
        frame.ToggleDetach(); Eval(frame, live, Sample(), opt, out var p, out var q, facing);
        Near(p, live + facing * opt.PovPositionOffset, "Detach includes current offset once at final output.");
        Near(q, facing * Quaternion.Euler(opt.PovRotationEuler), "Detached rotation includes current local offset.");
        opt.PovPositionOffset = V(.5f, -.2f, 1); opt.PovRotationEuler = V(-15, 60, 10);
        Eval(frame, V(100), Sample(), opt, out p, out q, Q);
        Near(p, live + facing * opt.PovPositionOffset, "Detached position slider previews against the frozen raw game frame.");
        Near(q, facing * Quaternion.Euler(opt.PovRotationEuler), "Detached rotation slider previews immediately.");
        opt.PovPositionOffset = Vector3.zero; opt.PovRotationEuler = Vector3.zero;
        Eval(frame, V(200), Sample(), opt, out p, out q, Q);
        Near(p, live, "Reset offsets restores exact detached frame without accumulated displacement.");
        Near(q, facing, "Reset offsets restores exact detached rotation.");
        opt.PovPositionOffset = V(0, 0, 3); opt.PovRotationEuler = V(0, -30);
        var newPosition = V(20, 3, 7); var newRotation = Quaternion.Euler(V(-12, 45, 4));
        Eval(frame, newPosition, Sample(phase: 2), opt, out p, out q, newRotation);
        Near(p, newPosition + newRotation * opt.PovPositionOffset, "Phase rebase does not carry stale user offsets into its anchor.");
        Near(q, newRotation * Quaternion.Euler(opt.PovRotationEuler), "Phase rebase applies the latest rotation offset exactly once.");
        Check(frame.Detached, "Offset changes and phase rebase leave detach enabled.");
        frame.ToggleDetach(); var latest = V(80, 4, 5);
        Eval(frame, latest, Sample(phase: 2), opt, out p, out q, Q);
        Near(p, latest + opt.PovPositionOffset, "Reattach uses current live game frame plus current offset without filter lag.");
        Near(q, Quaternion.Euler(opt.PovRotationEuler), "Reattach composes rotation offset once.");
    }
    static void FilteredFreeze()
    {
        var frame = new CameraFrame(null); var opt = Options(stabilize: true);
        Eval(frame, V(0), Sample(), opt, out _, out _); frame.ToggleDetach();
        Eval(frame, V(10), Sample(), opt, out var frozen, out _);
        Check(frozen.x > 0 && frozen.x < 10, "Detach freezes filtered base, not unsmoothed live pose.");
        Eval(frame, V(100), Sample(), opt, out var later, out _); Near(later, frozen, "Frozen filtered base remains fixed.");
        frame.ToggleDetach(); Eval(frame, V(200), Sample(), opt, out later, out _);
        Near(later, V(200), "Reattach bypasses filter lag immediately.");
    }
    static void ExitDropsAllPovRotationState()
    {
        foreach (bool detach in new[] { false, true })
        {
            var frame = new CameraFrame(null); var opt = Options(stabilize: true);
            opt.PovPositionOffset = V(.3f, .6f, -.4f); opt.PovRotationEuler = V(25, -40, 16);
            Eval(frame, V(1, 2, 3), Sample(), opt, out _, out _, Quaternion.Euler(V(40, 80, 30)));
            if (detach) frame.ToggleDetach();
            Eval(frame, V(3, 8, 11), Sample(), opt, out _, out _, Quaternion.Euler(V(-60, -100, -50)), .01f);
            Check(frame.Detached == detach, "Exit fixture establishes requested attached/detached state.");
            Eval(frame, V(10, 20, 30), Sample(), opt, out _, out var oldRotation, Quaternion.Euler(V(70, 120, 60)), .01f);
            var restoredPosition = V(50, 60, 70); var restoredRotation = Quaternion.Euler(V(-12, 33, 4));
            Check(Math.Abs(N.Quaternion.Dot(oldRotation.Value, restoredRotation.Value)) < .99f,
                "Exit fixture must distinguish stale POV/filter/offset rotation from restored ordinary camera.");
            Eval(frame, restoredPosition, Sample(pov: false), opt, out var p, out var q, restoredRotation, .001f);
            Check(!frame.Detached, "POVX exit clears detachment on the first ordinary-camera sample.");
            Near(p, restoredPosition, "POVX exit immediately removes filtered/frozen position and user POV offset.");
            Near(q, restoredRotation, "POVX exit immediately removes filtered/frozen rotation and user POV rotation offset.");
            Near(frame.LatestLiveRotation, restoredRotation, "Exit diagnostics retain restored source rotation without POV state.");
        }
    }
    static void PhaseRebase()
    {
        var frame = new CameraFrame(null); var opt = Options();
        Eval(frame, V(0), Sample(), opt, out _, out _); frame.ToggleDetach();
        Eval(frame, V(1), Sample(), opt, out _, out _);
        Eval(frame, V(2), Sample(), opt, out _, out _);
        var turn = Quaternion.Euler(V(0, 90));
        Eval(frame, V(100), Sample(phase: 2, stable: false), opt, out var p, out _, turn);
        Near(p, V(1), "Transition samples must not rebase frozen output.");
        Eval(frame, V(10), Sample(phase: 2), opt, out p, out var q, turn);
        Near(p, V(10), "Phase handoff uses the detach anchor, not later old-phase motion.");
        Near(q, turn, "Phase handoff transfers local orientation."); Check(frame.Detached, "Rebase must stay detached.");
        Eval(frame, V(20), Sample(phase: 2), opt, out var repeated, out _, turn);
        Near(repeated, p, "Repeated loops of the same phase must not trigger another rebase.");
        Eval(frame, V(30), Sample(phase: 1, animation: "b"), opt, out _, out _);
        Check(frame.Detached, "Whole animation rebase must also stay detached.");

        frame = new CameraFrame(null); opt = Options(stabilize: true);
        Eval(frame, V(0), Sample(), opt, out _, out _); frame.ToggleDetach();
        Eval(frame, V(10), Sample(), opt, out var filtered, out _);
        float localOffset = filtered.x - 10;
        Eval(frame, V(500), Sample(), opt, out _, out _);
        Eval(frame, V(20), Sample(phase: 2), opt, out p, out _, turn);
        Near(p, V(20, 0, -localOffset), "Large old-phase motion must not corrupt the filtered offset frozen at detach.");
    }
    static void FallbackPoliciesAndContext()
    {
        var frame = new CameraFrame(null); var opt = Options(DetachedTransitionPolicy.ReattachOnAnimation);
        Eval(frame, V(0), Sample(), opt, out _, out _); frame.ToggleDetach(); Eval(frame, V(1), Sample(), opt, out _, out _);
        Eval(frame, V(10), Sample(phase: 2), opt, out var p, out _); Near(p, V(1), "Fallback ignores phase changes.");
        Eval(frame, V(20), Sample(animation: "b"), opt, out p, out _);
        Check(!frame.Detached, "Fallback reattaches on whole animation change."); Near(p, V(20), "Fallback reattaches to live pose.");
        opt.TransitionPolicy = DetachedTransitionPolicy.Freeze;
        frame.ToggleDetach(); Eval(frame, V(21), Sample(animation: "b"), opt, out _, out _);
        Eval(frame, V(40), Sample(animation: "c"), opt, out p, out _); Near(p, V(21), "Freeze policy ignores animation change.");
        Eval(frame, V(50), Sample(animation: "c", character: 2), opt, out p, out _);
        Check(!frame.Detached, "Character change must always reattach."); Near(p, V(50), "New character pose is current.");
        frame.ToggleDetach(); Eval(frame, V(51), Sample(character: 2), opt, out _, out _);
        Eval(frame, V(60), Sample(source: 2, character: 2), opt, out p, out _);
        Check(!frame.Detached, "Source camera change must reattach.");
        frame.ToggleDetach(); Eval(frame, V(61), Sample(source: 2, character: 2), opt, out _, out _);
        Eval(frame, V(70), Sample(source: 2, character: 2, pov: false), opt, out p, out _);
        Check(!frame.Detached, "POVX exit must reattach."); Near(p, V(70), "POVX exit resumes raw game camera.");
        frame.Reset(); Check(!frame.Detached, "VR lifecycle reset must attach.");
    }
}
