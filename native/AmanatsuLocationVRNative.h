#pragma once
#include <stdint.h>

#ifdef AMANATSULOCATIONVR_NATIVE_EXPORTS
#define RGVR_EXPORT extern "C" __declspec(dllexport)
#else
#define RGVR_EXPORT extern "C" __declspec(dllimport)
#endif

RGVR_EXPORT int __cdecl RGVR_Init();
RGVR_EXPORT int __cdecl RGVR_GetRecommendedSize(uint32_t* width, uint32_t* height);
RGVR_EXPORT int __cdecl RGVR_GetEye(int eye, float* eyeToHead12, float* projectionRaw4);
RGVR_EXPORT int __cdecl RGVR_GetPose(float* tracking3x4_12);
RGVR_EXPORT int __cdecl RGVR_GetCachedPose(float* tracking3x4_12);

RGVR_EXPORT int __cdecl RGVR_GetTrackingState(int* connected, int* poseValid, int* activityLevel, uint64_t* originGeneration);

RGVR_EXPORT int __cdecl RGVR_ResetSeatedOrigin();
RGVR_EXPORT int __cdecl RGVR_SetTextures(void* leftD3D11Texture, void* rightD3D11Texture, int flipY);
RGVR_EXPORT int __cdecl RGVR_SetHudTexture(void* hudD3D11Texture, float widthMetres, float distanceMetres, int flipY);
RGVR_EXPORT int __cdecl RGVR_SetHudAnchor(const float* referenceTracking3x4_12);
RGVR_EXPORT int __cdecl RGVR_SetHudGeometry(float verticalOffsetMetres, float curvature);

RGVR_EXPORT void __cdecl RGVR_GetHudGeometry(float* verticalOffsetMetres, float* appliedCurvature, int* curvatureError);

RGVR_EXPORT int __cdecl RGVR_GetHudPoint(float u, float v, float* seatedPoint3);
RGVR_EXPORT void __cdecl RGVR_GetHudStats(int* overlayError, int* visible, uint64_t* updates);

RGVR_EXPORT int __cdecl RGVR_SetSubtitleTexture(void* subtitleD3D11Texture, float widthMetres, float distanceMetres, float verticalOffsetMetres, int flipY);
RGVR_EXPORT int __cdecl RGVR_SetSubtitleHorizontalOffset(float horizontalOffsetMetres);
RGVR_EXPORT void __cdecl RGVR_GetSubtitleStats(int* overlayError, int* visible, uint64_t* updates);
RGVR_EXPORT const char* __cdecl RGVR_HudErrorName(int overlayError);
RGVR_EXPORT void* __cdecl RGVR_GetRenderEventFunc();
RGVR_EXPORT void __cdecl RGVR_GetStats(int* leftError, int* rightError, uint64_t* submissions);
RGVR_EXPORT void __cdecl RGVR_GetTimingStats(double* poseMilliseconds, double* stereoMilliseconds, double* hudMilliseconds);

struct RGVR_CompositorTiming {
    uint32_t Version, SampleCount, LatestFrameIndex, ReprojectionFlags;
    uint32_t ReusedFrameCount, CpuLateFrameCount, GpuLateFrameCount, MotionSmoothedFrameCount;
    uint32_t ThrottledFrameCount, DroppedFrameCount, MisPresentedCount, Reserved;
    double SampleAgeMs, DisplayHz, AppCpuWallMs, PreSubmitGpuMs, PostSubmitGpuMs, TotalRenderGpuMs;
    double CompositorCpuMs, CompositorGpuMs, ClientFrameIntervalMs, PresentCallMs, WaitForPresentMs;
    double SubmitMs, WaitGetPosesMs, MaxPreSubmitGpuMs;
};

RGVR_EXPORT int __cdecl RGVR_GetCompositorTiming(RGVR_CompositorTiming* timing, uint32_t size);
RGVR_EXPORT int __cdecl RGVR_GetPoseError();
RGVR_EXPORT const char* __cdecl RGVR_ErrorName(int initError);
RGVR_EXPORT void __cdecl RGVR_Shutdown();
