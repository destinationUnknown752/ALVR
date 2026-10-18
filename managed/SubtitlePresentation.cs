using System;
using BepInEx.Logging;
using UnityEngine;

namespace AmanatsuLocationVR
{

    internal sealed class SubtitlePresentation : IDisposable
    {
        readonly ManualLogSource log;
        readonly SubtitleSource subtitleSource;
        readonly UiHud captionCapture;
        UiHud mainHud;
        Camera sourceCamera;
        Canvas target;
        RenderTexture registeredTexture;
        IntPtr registeredPointer;
        float submittedWidth, submittedDistance, submittedOffset, submittedHorizontalOffset;
        bool hudEnabled, visible, failed, disposed, nativeAvailable;
        bool failureLogged, cleanupFailureLogged, horizontalOffsetSubmitted;

        public SubtitlePresentation(ManualLogSource logger)
        {
            log = logger;
            subtitleSource = new SubtitleSource(logger);
            captionCapture = new UiHud(logger, subtitlesOnly: true);
        }

        public RenderTexture Texture
        {
            get
            {
                if (disposed || failed || !visible) return null;
                var texture = captionCapture.Texture;
                return texture && texture == registeredTexture ? texture : null;
            }
        }

        public void Prepare(Camera source, UiHud hud, bool showHud, bool subtitlesEnabled,
            float width, float distance, float offset, int fontSize = 36, float horizontalOffset = 0f, bool showSubtitles = true)
        {
            if (disposed) return;
            sourceCamera = source;
            mainHud = hud;
            hudEnabled = showHud;
            nativeAvailable = true;
            try
            {
                if (!subtitlesEnabled)
                {
                    failed = failureLogged = false;
                    ReleaseCapture(resetFailure: true);
                    PrepareMainFallback();
                    return;
                }
                if (failed)
                {
                    ReleaseCapture(resetFailure: false);
                    PrepareMainFallback();
                    return;
                }
                if (captionCapture.Failed)
                    throw new InvalidOperationException("The subtitle UI render pass failed.");
                if (registeredPointer != IntPtr.Zero &&
                    (!captionCapture.Texture || captionCapture.Texture != registeredTexture))
                    throw new InvalidOperationException("The registered subtitle capture texture was lost.");
                CheckNativeStatus();
                if (!Finite(width) || !Finite(distance) || !Finite(offset) || !Finite(horizontalOffset) ||
                    width <= 0f || distance <= 0f || horizontalOffset < -1f || horizontalOffset > 1f)
                    throw new ArgumentOutOfRangeException("Subtitle panel dimensions must be finite, with positive width and distance.");

                var next = subtitleSource.Discover();
                if (!next)
                {

                    ReleaseCapture(resetFailure: false);
                    PrepareMainFallback();
                    return;
                }
                if (target != next)
                {
                    ReleaseCapture(resetFailure: false);
                    target = next;
                }

                mainHud?.SetExcludedCanvas(target);
                mainHud?.Prepare(sourceCamera, hudEnabled);
                if (!subtitleSource.Prepare(target, fontSize))
                {

                    ReleaseCapture(resetFailure: false);
                    PrepareMainFallback();
                    return;
                }

                visible = showSubtitles && subtitleSource.HasVisibleText;
                if (!visible) ClearNativeTexture();
                captionCapture.PrepareSubtitle(sourceCamera, target, visible);
                if (captionCapture.Failed)
                    throw new InvalidOperationException("The subtitle UI capture could not prepare.");
                var texture = captionCapture.Texture;
                if (visible && !texture)
                {

                    ReleaseCapture(resetFailure: false);
                    PrepareMainFallback();
                    return;
                }
                if (!visible || !texture)
                {
                    visible = false;
                    ClearNativeTexture();

                    return;
                }
                RegisterTexture(texture, width, distance, offset, horizontalOffset);
            }
            catch (Exception exception) { FailOptional(exception); }
        }

        public void CheckAfterRender()
        {
            if (disposed || failed || !nativeAvailable) return;
            try
            {
                if (captionCapture.Failed || (visible &&
                    (!captionCapture.Texture || captionCapture.Texture != registeredTexture)))
                    throw new InvalidOperationException("The subtitle capture was lost during rendering.");
                if (visible && !subtitleSource.HasVisibleText)
                {
                    visible = false;
                    ClearNativeTexture();
                }
            }
            catch (Exception exception) { FailOptional(exception); }
        }

        void CheckNativeStatus()
        {
            if (registeredPointer == IntPtr.Zero) return;
            Native.RGVR_GetSubtitleStats(out int error, out _, out _);
            if (error != 0)
                throw new InvalidOperationException("SteamVR subtitle overlay failed with error " + error + ".");
        }

        void RegisterTexture(RenderTexture texture, float width, float distance, float offset, float horizontalOffset)
        {
            if (!horizontalOffsetSubmitted || submittedHorizontalOffset != horizontalOffset)
            {
                if (Native.RGVR_SetSubtitleHorizontalOffset(horizontalOffset) != 1)
                    throw new InvalidOperationException("The native bridge rejected the subtitle horizontal offset.");
                submittedHorizontalOffset = horizontalOffset;
                horizontalOffsetSubmitted = true;
            }
            if (texture == registeredTexture && registeredPointer != IntPtr.Zero &&
                width == submittedWidth && distance == submittedDistance && offset == submittedOffset) return;
            IntPtr pointer = texture == registeredTexture && registeredPointer != IntPtr.Zero
                ? registeredPointer : texture.GetNativeTexturePtr();
            if (pointer == IntPtr.Zero)
                throw new InvalidOperationException("The subtitle capture has no native texture.");

            if (Native.RGVR_SetSubtitleTexture(pointer, width, distance, offset, 1) != 1)
                throw new InvalidOperationException("The native bridge rejected the subtitle texture.");
            registeredTexture = texture;
            registeredPointer = pointer;
            submittedWidth = width;
            submittedDistance = distance;
            submittedOffset = offset;
        }

        void ClearNativeTexture()
        {
            visible = false;
            if (nativeAvailable && registeredPointer != IntPtr.Zero &&
                Native.RGVR_SetSubtitleTexture(IntPtr.Zero, 1.2f, 1f, -0.45f, 1) < 0)
                throw new InvalidOperationException("The native bridge could not clear the subtitle texture.");
            registeredPointer = IntPtr.Zero;
            registeredTexture = null;
        }

        void ReleaseCapture(bool resetFailure)
        {

            try { ClearNativeTexture(); }
            catch (Exception exception) { WarnCleanup(exception); }
            try
            {
                if (resetFailure) captionCapture.Prepare(sourceCamera, false);
                else captionCapture.RestoreCanvases();
            }
            catch (Exception exception) { WarnCleanup(exception); }
            try { subtitleSource.Restore(); }
            catch (Exception exception) { WarnCleanup(exception); }
            target = null;
        }

        void PrepareMainFallback()
        {
            mainHud?.SetExcludedCanvas(null);
            mainHud?.Prepare(sourceCamera, hudEnabled);
        }

        void FailOptional(Exception exception)
        {
            failed = true;
            visible = false;
            if (!failureLogged)
                log?.LogWarning("Separate subtitles unavailable; restoring the original HUD subtitle display. " + exception);
            failureLogged = true;
            ReleaseCapture(resetFailure: false);
            try { PrepareMainFallback(); }
            catch (Exception cleanup) { WarnCleanup(cleanup); }
        }

        void WarnCleanup(Exception exception)
        {
            if (!cleanupFailureLogged) log?.LogWarning("Subtitle presentation cleanup will retry: " + exception);
            cleanupFailureLogged = true;
        }

        public void Stop(UiHud hud, bool nativeReady)
        {
            if (hud != null) mainHud = hud;
            nativeAvailable = nativeReady;
            ReleaseCapture(resetFailure: true);
            try { mainHud?.SetExcludedCanvas(null); }
            catch (Exception exception) { WarnCleanup(exception); }
            failed = failureLogged = false;

            nativeAvailable = false;
            horizontalOffsetSubmitted = false;
            sourceCamera = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Stop(mainHud, nativeAvailable);
            disposed = true;
            try { captionCapture.Dispose(); }
            catch (Exception exception) { WarnCleanup(exception); }
            try { subtitleSource.Dispose(); }
            catch (Exception exception) { WarnCleanup(exception); }
            mainHud = null;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
