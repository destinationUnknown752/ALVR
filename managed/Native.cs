using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace AmanatsuLocationVR
{
    internal static class Native
    {
        const string Lib = "AmanatsuLocationVRNative.dll";
        static IntPtr module;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);

        internal static void Load(string directory)
        {
            if (module != IntPtr.Zero) return;
            var path = Path.Combine(directory, Lib);
            if (!File.Exists(path)) throw new FileNotFoundException("Missing native bridge", path);

            module = LoadLibraryEx(path, IntPtr.Zero, 0x00000100 | 0x00001000);
            if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), path);
        }
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_Init();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_GetRecommendedSize(out uint width, out uint height);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_GetEye(int eye, [Out] float[] eyeToHead, [Out] float[] projectionRaw);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_GetPose([Out] float[] pose);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_GetCachedPose([Out] float[] pose);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_GetTrackingState(out int connected, out int poseValid, out int activityLevel, out ulong originGeneration);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_ResetSeatedOrigin();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_SetTextures(IntPtr left, IntPtr right, int flipY);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_SetHudTexture(IntPtr texture, float widthMetres, float distanceMetres, int flipY);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_SetHudAnchor([In] float[] pose);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_SetHudGeometry(float verticalOffsetMetres, float curvature);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern void RGVR_GetHudGeometry(out float verticalOffset, out float appliedCurvature, out int curvatureError);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_GetHudPoint(float u, float v, [Out] float[] seatedPoint);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_SetSubtitleTexture(IntPtr texture, float widthMetres, float distanceMetres, float verticalOffsetMetres, int flipY);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_SetSubtitleHorizontalOffset(float horizontalOffsetMetres);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern void RGVR_GetSubtitleStats(out int overlayError, out int visible, out ulong updates);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern void RGVR_GetHudStats(out int overlayError, out int visible, out ulong updates);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern void RGVR_GetTimingStats(out double poseMs, out double stereoMs, out double hudMs);

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct CompositorTiming
        {
            public const uint Size = 160;
            public uint Version, SampleCount, LatestFrameIndex, ReprojectionFlags;
            public uint ReusedFrameCount, CpuLateFrameCount, GpuLateFrameCount, MotionSmoothedFrameCount;
            public uint ThrottledFrameCount, DroppedFrameCount, MisPresentedCount, Reserved;
            public double SampleAgeMs, DisplayHz, AppCpuWallMs, PreSubmitGpuMs, PostSubmitGpuMs, TotalRenderGpuMs;
            public double CompositorCpuMs, CompositorGpuMs, ClientFrameIntervalMs, PresentCallMs, WaitForPresentMs;
            public double SubmitMs, WaitGetPosesMs, MaxPreSubmitGpuMs;
        }
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern int RGVR_GetCompositorTiming(out CompositorTiming timing, uint size);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr RGVR_HudErrorName(int error);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr RGVR_GetRenderEventFunc();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern void RGVR_GetStats(out int leftError, out int rightError, out ulong submissions);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr RGVR_ErrorName(int error);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] internal static extern void RGVR_Shutdown();
    }
}
