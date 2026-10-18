#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <d3d11.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include "AmanatsuLocationVRNative.h"

int main(int argc, char** argv) {
    wchar_t path[MAX_PATH]{};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    wchar_t* slash = wcsrchr(path, L'\\');
    if (!slash) return 1;
    wcscpy_s(slash + 1, MAX_PATH - static_cast<size_t>(slash + 1 - path), L"AmanatsuLocationVRNative.dll");
    HMODULE module = LoadLibraryExW(path, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!module) { std::printf("LoadLibrary failed: %lu\n", GetLastError()); return 1; }
#define RESOLVE(name, type) const auto name = reinterpret_cast<type>(GetProcAddress(module, #name)); if (!name) return 2
    RESOLVE(RGVR_Init, int (__cdecl*)());
    RESOLVE(RGVR_GetRecommendedSize, int (__cdecl*)(uint32_t*, uint32_t*));
    RESOLVE(RGVR_GetEye, int (__cdecl*)(int, float*, float*));
    RESOLVE(RGVR_GetPose, int (__cdecl*)(float*));
    RESOLVE(RGVR_GetCachedPose, int (__cdecl*)(float*));
    RESOLVE(RGVR_GetTrackingState, int (__cdecl*)(int*, int*, int*, uint64_t*));
    RESOLVE(RGVR_ResetSeatedOrigin, int (__cdecl*)());
    RESOLVE(RGVR_SetTextures, int (__cdecl*)(void*, void*, int));
    RESOLVE(RGVR_SetHudTexture, int (__cdecl*)(void*, float, float, int));
    RESOLVE(RGVR_SetHudAnchor, int (__cdecl*)(const float*));
    RESOLVE(RGVR_SetHudGeometry, int (__cdecl*)(float, float));
    RESOLVE(RGVR_GetHudGeometry, void (__cdecl*)(float*, float*, int*));
    RESOLVE(RGVR_GetHudPoint, int (__cdecl*)(float, float, float*));
    RESOLVE(RGVR_GetHudStats, void (__cdecl*)(int*, int*, uint64_t*));
    RESOLVE(RGVR_SetSubtitleTexture, int (__cdecl*)(void*, float, float, float, int));
    RESOLVE(RGVR_SetSubtitleHorizontalOffset, int (__cdecl*)(float));
    RESOLVE(RGVR_GetSubtitleStats, void (__cdecl*)(int*, int*, uint64_t*));
    RESOLVE(RGVR_HudErrorName, const char* (__cdecl*)(int));
    RESOLVE(RGVR_GetRenderEventFunc, void* (__cdecl*)());
    RESOLVE(RGVR_GetStats, void (__cdecl*)(int*, int*, uint64_t*));
    RESOLVE(RGVR_GetTimingStats, void (__cdecl*)(double*, double*, double*));
    RESOLVE(RGVR_GetCompositorTiming, int (__cdecl*)(RGVR_CompositorTiming*, uint32_t));
    RESOLVE(RGVR_GetPoseError, int (__cdecl*)());
    RESOLVE(RGVR_ErrorName, const char* (__cdecl*)(int));
    RESOLVE(RGVR_Shutdown, void (__cdecl*)());
    uint32_t width = 999, height = 999;
    static_assert(sizeof(RGVR_CompositorTiming) == 160, "Timing ABI size changed");
    RGVR_CompositorTiming frameTiming{};
    std::memset(&frameTiming, 0x7f, sizeof(frameTiming));
    if (RGVR_GetCompositorTiming(&frameTiming, sizeof(frameTiming)) != 0 || frameTiming.Version != 1 ||
        frameTiming.SampleCount != 0 || frameTiming.SampleAgeMs != 0 || frameTiming.PreSubmitGpuMs != 0 ||
        frameTiming.ReprojectionFlags != 0 || frameTiming.ThrottledFrameCount != 0) return 64;
    const RGVR_CompositorTiming emptyFrameTiming = frameTiming;
    if (RGVR_GetCompositorTiming(nullptr, sizeof(frameTiming)) != -1 ||
        RGVR_GetCompositorTiming(&frameTiming, 0) != -1 ||
        RGVR_GetCompositorTiming(&frameTiming, sizeof(frameTiming) - 1) != -1 ||
        RGVR_GetCompositorTiming(&frameTiming, sizeof(frameTiming) + 1) != -1 ||
        std::memcmp(&emptyFrameTiming, &frameTiming, sizeof(frameTiming)) != 0) return 65;
    int connected = 99, poseValid = 99, activity = 99;
    uint64_t originGeneration = 99;
    if (RGVR_GetTrackingState(&connected, &poseValid, &activity, &originGeneration) != 0 ||
        connected != 0 || poseValid != 0 || activity != -1 || originGeneration != 0 || RGVR_ResetSeatedOrigin() != 0) return 50;
    if (RGVR_GetTrackingState(nullptr, nullptr, nullptr, nullptr) != 0) return 51;
    RGVR_GetHudGeometry(nullptr, nullptr, nullptr);
    float verticalOffset = 99, appliedCurvature = 99;
    int curvatureError = 99;
    if (RGVR_SetHudGeometry(-0.25f, 0.15f) != 1) return 52;
    RGVR_GetHudGeometry(&verticalOffset, &appliedCurvature, &curvatureError);
    if (verticalOffset != -0.25f || appliedCurvature != 0 || curvatureError != 0) return 53;
    const float geometryNan = std::numeric_limits<float>::quiet_NaN();
    const float geometryInfinity = std::numeric_limits<float>::infinity();
    if (RGVR_SetSubtitleHorizontalOffset(-1.0f) != 1 || RGVR_SetSubtitleHorizontalOffset(1.0f) != 1 ||
        RGVR_SetSubtitleHorizontalOffset(geometryNan) != -1 || RGVR_SetSubtitleHorizontalOffset(geometryInfinity) != -1 ||
        RGVR_SetSubtitleHorizontalOffset(-1.01f) != -1 || RGVR_SetSubtitleHorizontalOffset(1.01f) != -1 ||
        RGVR_SetSubtitleHorizontalOffset(0.0f) != 1) return 63;
    const float invalidGeometry[][2] = {{geometryNan, 0}, {geometryInfinity, 0}, {0, geometryNan},
        {0, geometryInfinity}, {0, -0.01f}, {0, 1.01f}};
    for (const auto& geometry : invalidGeometry)
        if (RGVR_SetHudGeometry(geometry[0], geometry[1]) != -1) return 54;
    RGVR_GetHudGeometry(&verticalOffset, &appliedCurvature, &curvatureError);
    if (verticalOffset != -0.25f || appliedCurvature != 0) return 55;
    if (RGVR_SetHudGeometry(0, 1) != 1 || RGVR_SetHudGeometry(0, 0) != 1) return 56;
    float hudPoint[3]{99, 99, 99};
    if (RGVR_GetHudPoint(0.5f, 0.5f, hudPoint) != 0 || hudPoint[0] != 0 || hudPoint[1] != 0 || hudPoint[2] != 0 ||
        RGVR_GetHudPoint(0.5f, 0.5f, nullptr) != 0) return 57;
    const float invalidUv[][2] = {{geometryNan, 0.5f}, {0.5f, geometryInfinity}, {-0.01f, 0.5f},
        {1.01f, 0.5f}, {0.5f, -0.01f}, {0.5f, 1.01f}};
    for (const auto& uv : invalidUv)
        if (RGVR_GetHudPoint(uv[0], uv[1], hudPoint) != 0) return 58;

    const float identityPose[12]{1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0};
    const float yawPose[12]{0, 0, 1, 3, 0, 1, 0, -2, -1, 0, 0, 1};
    const float pitchRollPose[12]{0, -1, 0, 0, 0, 0, -1, 0, 1, 0, 0, 0};
    if (RGVR_SetHudAnchor(identityPose) != 1 || RGVR_SetHudAnchor(yawPose) != 1 || RGVR_SetHudAnchor(pitchRollPose) != 1) return 59;
    const int corruptIndices[]{0, 1, 0, 3, 5};
    const float corruptValues[]{2, 0.25f, -1, geometryNan, geometryInfinity};
    for (int index = 0; index < 5; ++index) {
        float invalidPose[12]{};
        std::memcpy(invalidPose, identityPose, sizeof(invalidPose));
        invalidPose[corruptIndices[index]] = corruptValues[index];
        if (RGVR_SetHudAnchor(invalidPose) != -1) return 60;
    }
    float degeneratePose[12]{};
    if (RGVR_SetHudAnchor(degeneratePose) != -1 || RGVR_SetHudAnchor(nullptr) != 0) return 61;
    if (RGVR_GetRecommendedSize(&width, &height) != 0 || width || height) return 3;
    float pose[12]{}, raw[4]{};
    if (RGVR_GetPose(pose) != -1 || pose[0] != 1 || pose[5] != 1 || pose[10] != 1) return 4;
    if (RGVR_GetCachedPose(pose) != -1 || pose[0] != 1 || pose[5] != 1 || pose[10] != 1) return 16;
    if (RGVR_GetCachedPose(nullptr) != -1) return 17;
    if (RGVR_GetEye(0, pose, raw) != 0 || RGVR_GetEye(2, pose, raw) != 0) return 5;
    if (RGVR_SetTextures(nullptr, nullptr, 0) != 0) return 6;
    if (RGVR_SetHudTexture(nullptr, 0, 0, 0) != 0 || RGVR_SetHudAnchor(nullptr) != 0 ||
        RGVR_SetSubtitleTexture(nullptr, 0, 0, 0, 0) != 0) return 30;
    RGVR_GetHudStats(nullptr, nullptr, nullptr);
    RGVR_GetSubtitleStats(nullptr, nullptr, nullptr);
    RGVR_GetTimingStats(nullptr, nullptr, nullptr);
    int overlayError = -1, visible = -1;
    uint64_t updates = 999;
    RGVR_GetSubtitleStats(&overlayError, &visible, &updates);
    if (overlayError != 0 || visible != 0 || updates != 0 || !RGVR_HudErrorName(overlayError)) return 31;
    const auto callback = reinterpret_cast<void (__stdcall*)(int)>(RGVR_GetRenderEventFunc());
    if (!callback) return 7;
    callback(1);
    callback(2);
    callback(999);
    if (RGVR_GetCompositorTiming(&frameTiming, sizeof(frameTiming)) != 0 || frameTiming.SampleCount != 0) return 66;
    if (RGVR_GetTrackingState(&connected, &poseValid, &activity, &originGeneration) != 0 ||
        connected != 0 || poseValid != 0 || activity != -1 || originGeneration != 0 || RGVR_ResetSeatedOrigin() != 0) return 62;
    if (RGVR_GetCachedPose(pose) != -1 || pose[0] != 1 || pose[5] != 1 || pose[10] != 1) return 18;
    int left = 0, right = 0;
    uint64_t count = 999;
    RGVR_GetStats(&left, &right, &count);
    if (count != 0 || left != 1 || right != 1 || RGVR_GetPoseError() != 1) return 8;
    RGVR_GetSubtitleStats(&overlayError, &visible, &updates);
    if (overlayError != 0 || visible != 0 || updates != 0) return 32;
    RGVR_Shutdown();
    RGVR_Shutdown();
    if (RGVR_GetCompositorTiming(&frameTiming, sizeof(frameTiming)) != 0 || frameTiming.SampleCount != 0 || frameTiming.Version != 1) return 67;
    std::printf("Headless ABI checks passed (all exports, no-init calls, no-texture callback, idempotent shutdown).\n");
    std::printf("Tracking/HUD geometry checks passed (no-init/reset state, rigid pose acceptance/rejection, curvature validation, unavailable point queries).\n");
    std::printf("Compositor timing ABI checks passed (160-byte layout, unavailable cached state, invalid-size rejection, no-init callbacks/shutdown).\n");

    ID3D11Device* device = nullptr;
    const HRESULT createDevice = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0,
        nullptr, 0, D3D11_SDK_VERSION, &device, nullptr, nullptr);
    if (FAILED(createDevice)) { std::printf("D3D11 WARP device failed: %lx\n", createDevice); return 9; }
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = desc.Height = 64;
    desc.MipLevels = desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    ID3D11Texture2D* texture = nullptr;
    if (FAILED(device->CreateTexture2D(&desc, nullptr, &texture))) return 10;
    if (RGVR_SetTextures(texture, texture, 0) != 1) return 11;
    texture->Release();
    if (RGVR_SetTextures(texture, texture, 1) != 1) return 12;
    if (RGVR_SetTextures(nullptr, nullptr, 0) != 0) return 13;
    desc.MipLevels = 2;
    if (FAILED(device->CreateTexture2D(&desc, nullptr, &texture))) return 14;
    if (RGVR_SetTextures(texture, texture, 0) != -1) return 15;
    texture->Release();
    desc.MipLevels = 1;
    if (FAILED(device->CreateTexture2D(&desc, nullptr, &texture))) return 33;
    const auto references = [](IUnknown* object) { object->AddRef(); return object->Release(); };
    const ULONG initialReferences = references(texture);
    if (RGVR_SetHudTexture(texture, 1.4f, 1.0f, 0) != 1 || references(texture) != initialReferences + 1) return 34;
    if (RGVR_SetSubtitleTexture(texture, 1.2f, 1.0f, -0.4f, 0) != 1 || references(texture) != initialReferences + 2) return 35;
    if (RGVR_SetSubtitleTexture(texture, 1.1f, 0.9f, -0.35f, 1) != 1 || references(texture) != initialReferences + 2) return 36;
    const float nan = std::numeric_limits<float>::quiet_NaN();
    const float infinity = std::numeric_limits<float>::infinity();
    const float invalidDimensions[][3] = {
        {0, 1, -0.4f}, {-1, 1, -0.4f}, {nan, 1, -0.4f}, {infinity, 1, -0.4f},
        {1, 0, -0.4f}, {1, -1, -0.4f}, {1, nan, -0.4f}, {1, infinity, -0.4f},
        {1, 1, nan}, {1, 1, infinity}
    };
    for (const auto& dimensions : invalidDimensions) {
        if (RGVR_SetSubtitleTexture(texture, dimensions[0], dimensions[1], dimensions[2], 0) != -1 ||
            references(texture) != initialReferences + 2) return 37;
        RGVR_GetSubtitleStats(&overlayError, &visible, &updates);
        if (overlayError == 0 || visible != 0 || updates != 0) return 38;
    }
    if (RGVR_SetSubtitleTexture(device, 1.2f, 1.0f, -0.4f, 0) != -1) return 39;
    ID3D11Texture2D* invalidTexture = nullptr;
    desc.MipLevels = 2;
    if (FAILED(device->CreateTexture2D(&desc, nullptr, &invalidTexture))) return 40;
    const ULONG invalidReferences = references(invalidTexture);
    if (RGVR_SetSubtitleTexture(invalidTexture, 1.2f, 1.0f, -0.4f, 0) != -1 ||
        references(invalidTexture) != invalidReferences) return 41;
    invalidTexture->Release();
    if (RGVR_SetSubtitleTexture(texture, 1.2f, 1.0f, -0.4f, 0) != 1) return 42;
    callback(1);
    RGVR_GetSubtitleStats(&overlayError, &visible, &updates);
    if (overlayError != 0 || visible != 0 || updates != 0) return 43;
    RGVR_GetHudStats(&overlayError, &visible, &updates);
    if (overlayError != 0 || visible != 0 || updates != 0) return 44;

    if (RGVR_SetSubtitleTexture(nullptr, nan, nan, nan, 0) != 0 || references(texture) != initialReferences + 1) return 45;
    callback(1);
    if (RGVR_SetHudTexture(nullptr, 0, 0, 0) != 0 || references(texture) != initialReferences) return 46;
    if (RGVR_SetSubtitleTexture(texture, 1.2f, 1.0f, -0.4f, 0) != 1) return 47;
    texture->Release();
    if (RGVR_SetSubtitleTexture(texture, 1.3f, 1.1f, -0.5f, 1) != 1) return 48;
    texture->AddRef();
    RGVR_Shutdown();
    if (references(texture) != initialReferences) return 49;
    RGVR_Shutdown();
    texture->Release();
    device->Release();
    std::printf("D3D11 texture checks passed (real texture, retained references, same-pointer replacement, mip rejection, clear).\n");
    std::printf("Subtitle overlay checks passed (independent ownership, invalid geometry/interface/texture rejection, no-init callbacks, clear, shutdown).\n");
    int runtimeResult = 0;
    if (argc > 1 && std::strcmp(argv[1], "--init") == 0) {
        const int error = RGVR_Init();
        std::printf("RGVR_Init: %d (%s)\n", error, RGVR_ErrorName(error));
        if (error) runtimeResult = 20;
        if (!error) {
            RGVR_GetRecommendedSize(&width, &height);
            std::printf("Recommended per-eye dimensions: %u x %u\n", width, height);
            for (int eye = 0; eye < 2; ++eye) {
                const int validEye = RGVR_GetEye(eye, pose, raw);
                if (!validEye) runtimeResult = 21;
                std::printf("Eye %d valid=%d offset metres=(%.5f, %.5f, %.5f), raw projection=(%.4f, %.4f, %.4f, %.4f)\n",
                    eye, validEye, pose[3], pose[7], pose[11], raw[0], raw[1], raw[2], raw[3]);
            }
            const int validPose = RGVR_GetPose(pose);
            if (validPose != 1) runtimeResult = 22;
            std::printf("Pose: %d, compositor error: %d\n", validPose, RGVR_GetPoseError());
            if (validPose == 1)
                for (int row = 0; row < 3; ++row)
                    std::printf("  %.5f %.5f %.5f %.5f\n", pose[row*4], pose[row*4+1], pose[row*4+2], pose[row*4+3]);
        }
        RGVR_Shutdown();
    }
    FreeLibrary(module);
    return runtimeResult;
}
