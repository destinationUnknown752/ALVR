using AmanatsuLocationVR;
using UnityEngine;
using BepInEx.Logging;

int checks = 0;
void Check(bool condition, string message) { ++checks; if (!condition) throw new Exception(message); }
void Scenario(Action<SubtitlePresentation, UiHud, Action<bool, bool>> body)
{
    Boundary.Reset();
    var log = new ManualLogSource(); var camera = new Camera(); var hud = new UiHud(log);
    hud.Prepare(camera, true); Boundary.Events.Clear();
    using var presentation = new SubtitlePresentation(log);
    body(presentation, hud, (showHud, captions) => presentation.Prepare(camera, hud, showHud, captions, 1.2f, 1f, -0.45f));
}

Scenario((p, hud, prepare) =>
{
    prepare(true, true);
    Check(p.Texture && Native.Pointer != IntPtr.Zero, "Visible line was not registered.");
    Check(Boundary.Events.IndexOf("hud.release") < Boundary.Events.IndexOf("source.layout") &&
        Boundary.Events.IndexOf("source.layout") < Boundary.Events.IndexOf("caption.claim"), "Canvas ownership handoff was out of order.");
    var texture = p.Texture; int restores = UiHud.Caption.Restores;
    Boundary.Visible = false; prepare(true, true);
    Check(!p.Texture && Native.Pointer == IntPtr.Zero && Native.Clears == 1, "Line pause did not clear the overlay.");
    Check(Boundary.Canvas.Owner == "caption" && Boundary.Layout && hud.Excluded && UiHud.Caption.Restores == restores,
        "Line pause released cached capture/layout ownership.");
    prepare(true, true);
    Check(Native.Clears == 1, "Idle line repeatedly cleared the native overlay.");
    Boundary.Visible = true; prepare(true, true);
    Check(p.Texture == texture && Native.Registrations == 2, "Line resume failed to reuse its capture.");
    prepare(false, true);
    Check(!hud.Enabled && p.Texture && Native.Clears == 1 && Boundary.Canvas.Owner == "caption", "F11 HUD toggle disabled independent captions.");
    prepare(true, false);
    Check(!p.Texture && !hud.Excluded && !Boundary.Layout && Boundary.Canvas.Owner == "hud" && Native.Pointer == IntPtr.Zero,
        "Subtitle toggle did not restore the original HUD display.");
});
Scenario((p, hud, prepare) =>
{
    prepare(true, true); p.Stop(hud, true);
    Check(Native.Pointer == IntPtr.Zero && !hud.Excluded && !Boundary.Layout && Boundary.Canvas.Owner == "original", "Stop did not restore caption ownership.");
    Native.Stopped = true; p.CheckAfterRender(); p.Dispose(); p.Dispose();
    Check(Native.CallsAfterStop == 0, "Disposed presentation called the stopped native bridge.");
});
foreach (string failure in new[] { "prepare", "overlay", "render", "lost-texture" })
    Scenario((p, hud, prepare) =>
    {
        if (failure == "prepare") UiHud.Caption.FailNextPrepare = true;
        prepare(true, true);
        if (failure == "overlay") { Native.Error = 20; prepare(true, true); }
        if (failure == "render") { UiHud.Caption.Failed = true; p.CheckAfterRender(); }
        if (failure == "lost-texture") { UiHud.Caption.LoseTexture = true; p.CheckAfterRender(); }
        Check(!p.Texture && Native.Pointer == IntPtr.Zero && !hud.Excluded && Boundary.Canvas.Owner == "hud" && !Boundary.Layout,
            failure + " failure did not fall back to the original HUD.");
        int registrations = Native.Registrations; prepare(true, true);
        Check(Native.Registrations == registrations, failure + " failure retried without a toggle.");
    });
Scenario((p, hud, prepare) =>
{
    prepare(true, true); Boundary.Visible = false; p.CheckAfterRender();
    Check(!p.Texture && Native.Pointer == IntPtr.Zero && Boundary.Canvas.Owner == "caption", "Text disappearing during rendering was still submitted.");
});
Scenario((p, hud, prepare) =>
{
    prepare(true, true);
    int restores = UiHud.Caption.Restores; var texture = p.Texture;
    p.Prepare(new Camera(), hud, true, true, 1.2f, 1f, -0.45f, fontSize: 48, horizontalOffset: 0.2f);
    Check(Boundary.FontSize == 48 && Native.HorizontalOffset == 0.2f && Native.HorizontalCalls == 2,
        "Live subtitle settings were not forwarded to their separate boundaries.");
    Check(p.Texture == texture && UiHud.Caption.Restores == restores && Native.Registrations == 1,
        "Horizontal movement recreated/re-registered the capture instead of changing geometry.");
    p.Prepare(new Camera(), hud, true, true, 1.2f, 1f, -0.45f, 48, 0.2f);
    Check(Native.HorizontalCalls == 2 && Native.Registrations == 1, "Unchanged subtitle settings repeated native mutations.");
    p.Stop(hud, true); Native.HorizontalOffset = 0f;
    p.Prepare(new Camera(), hud, true, true, 1.2f, 1f, -0.45f, 48, 0.2f);
    Check(p.Texture && Native.HorizontalCalls == 3 && Native.HorizontalOffset == 0.2f,
        "Restart did not reapply horizontal geometry to the native bridge.");
});
Scenario((p, hud, prepare) =>
{
    prepare(true, true); var texture = p.Texture; int restores = UiHud.Caption.Restores;
    p.Prepare(new Camera(), hud, true, true, 1.2f, 1f, -0.45f, showSubtitles: false);
    p.CheckAfterRender();
    Check(!p.Texture && Native.Pointer == IntPtr.Zero && !UiHud.Caption.Rendering,
        "Show subtitles off did not stop capture and remove both displayed copies.");
    Check(Boundary.Canvas.Owner == "caption" && hud.Excluded == Boundary.Canvas && UiHud.Caption.Restores == restores,
        "Show subtitles off released ownership and leaked original captions back to the HUD.");
    prepare(true, true);
    Check(p.Texture == texture && UiHud.Caption.Rendering && Native.Pointer != IntPtr.Zero && UiHud.Caption.Restores == restores,
        "Showing subtitles again did not resume the existing capture.");
});
Console.WriteLine($"Subtitle presentation checks passed: {checks} assertions across 9 ownership/lifecycle/failure scenarios (production coordinator; fake boundaries only).");
