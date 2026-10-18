
namespace BepInEx.Logging
{
    public sealed class ManualLogSource { public void LogWarning(object value) { } }
}
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) => value is not null; }
    public sealed class Camera : Object { }
    public sealed class Canvas : Object { public string Owner = "original"; }
    public sealed class RenderTexture : Object { public IntPtr GetNativeTexturePtr() => new(123); }
}
namespace AmanatsuLocationVR
{
    using UnityEngine;
    using BepInEx.Logging;
    internal static class Boundary
    {
        public static readonly List<string> Events = new();
        public static Canvas Canvas;
        public static bool Visible, Layout;
        public static int FontSize;
        public static void Reset() { Events.Clear(); Canvas = new(); Visible = true; Layout = false; Native.Reset(); }
    }
    internal sealed class SubtitleSource : IDisposable
    {
        public SubtitleSource(ManualLogSource log) { }
        public Canvas Discover() => Boundary.Canvas;
        public bool HasVisibleText => Boundary.Visible;
        public bool Prepare(Canvas value, int fontSize = 36)
        {
            if (value.Owner == "hud") throw new InvalidOperationException("Layout changed before HUD released ownership.");
            Boundary.Events.Add("source.layout"); Boundary.Layout = true; Boundary.FontSize = fontSize; return true;
        }
        public void Restore() { Boundary.Layout = false; Boundary.Events.Add("source.restore"); }
        public void Dispose() => Restore();
    }
    internal sealed class UiHud : IDisposable
    {
        public static UiHud Caption;
        readonly bool subtitle;
        public bool Failed, FailNextPrepare, LoseTexture, Enabled, Rendering;
        public int Restores;
        public Canvas Excluded;
        readonly RenderTexture resource = new();
        public RenderTexture Texture => Enabled && !Failed && !LoseTexture ? resource : null;
        public UiHud(ManualLogSource log, bool subtitlesOnly = false) { subtitle = subtitlesOnly; if (subtitle) Caption = this; }
        public void SetExcludedCanvas(Canvas value) => Excluded = value;
        public void Prepare(Camera source, bool enabled)
        {
            Enabled = enabled;
            if (subtitle) { if (!enabled) { Failed = false; RestoreCanvases(); } return; }
            if ((!enabled || Excluded == Boundary.Canvas) && Boundary.Canvas.Owner == "hud")
            { Boundary.Canvas.Owner = "original"; Boundary.Events.Add("hud.release"); }
            if (enabled && !Excluded) { Boundary.Canvas.Owner = "hud"; Boundary.Events.Add("hud.claim"); }
        }
        public void PrepareSubtitle(Camera source, Canvas value, bool visible)
        {
            if (value.Owner == "hud") throw new InvalidOperationException("Subtitle capture claimed an owned canvas.");
            if (FailNextPrepare) { FailNextPrepare = false; Failed = true; return; }
            value.Owner = "caption"; Enabled = true; Rendering = visible; Boundary.Events.Add("caption.claim");
        }
        public void RestoreCanvases()
        {
            ++Restores; Enabled = Rendering = false;
            if (Boundary.Canvas.Owner == "caption") Boundary.Canvas.Owner = "original";
        }
        public void Dispose() => RestoreCanvases();
    }
    internal static class Native
    {
        public static IntPtr Pointer;
        public static int Registrations, Clears, Error, CallsAfterStop;
        public static int HorizontalCalls;
        public static float HorizontalOffset;
        public static bool Stopped;
        public static void Reset() { Pointer = IntPtr.Zero; Registrations = Clears = Error = CallsAfterStop = HorizontalCalls = 0; HorizontalOffset = 0; Stopped = false; }
        public static int RGVR_SetSubtitleHorizontalOffset(float value)
        { if (Stopped) ++CallsAfterStop; ++HorizontalCalls; HorizontalOffset = value; return 1; }
        public static int RGVR_SetSubtitleTexture(IntPtr pointer, float width, float distance, float offset, int flip)
        {
            if (Stopped) ++CallsAfterStop;
            Pointer = pointer; if (pointer == IntPtr.Zero) ++Clears; else ++Registrations;
            return pointer == IntPtr.Zero ? 0 : 1;
        }
        public static void RGVR_GetSubtitleStats(out int error, out int visible, out ulong updates)
        { if (Stopped) ++CallsAfterStop; error = Error; visible = Pointer == IntPtr.Zero ? 0 : 1; updates = (ulong)Registrations; }
    }
}
