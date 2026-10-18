#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define AMANATSULOCATIONVR_NATIVE_EXPORTS
#include <Windows.h>
#include <d3d11.h>
#include <cmath>
#include <cstring>
#include "openvr.h"
#include "AmanatsuLocationVRNative.h"

namespace {

SRWLOCK stateLock = SRWLOCK_INIT;
struct Guard {
    Guard() { AcquireSRWLockExclusive(&stateLock); }
    ~Guard() { ReleaseSRWLockExclusive(&stateLock); }
    Guard(const Guard&) = delete;
    Guard& operator=(const Guard&) = delete;
};

vr::IVRSystem* vrSystem = nullptr;
vr::IVRCompositor* compositor = nullptr;
vr::IVRChaperone* chaperone = nullptr;
int trackingConnected = 0;
int trackingPoseValid = 0;
int trackingActivity = vr::k_EDeviceActivityLevel_Unknown;
uint64_t trackingOriginGeneration = 0;
vr::HmdMatrix34_t seatedToStanding{};
bool seatedToStandingValid = false;
bool resetSeatedRequested = false;
ID3D11Texture2D* leftTexture = nullptr;
ID3D11Texture2D* rightTexture = nullptr;
bool invertY = false;
int leftError = vr::VRCompositorError_RequestFailed;
int rightError = vr::VRCompositorError_RequestFailed;
int poseError = vr::VRCompositorError_RequestFailed;
uint64_t submissionCount = 0;
uint64_t poseSerial = 0;
uint64_t submittedSerial = 0;
float cachedPose[12]{1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0};
int cachedPoseResult = -1;
vr::IVROverlay* hudOverlay = nullptr;
vr::VROverlayHandle_t hudHandle = vr::k_ulOverlayHandleInvalid;
ID3D11Texture2D* hudTexture = nullptr;
float hudWidthMetres = 1.4f;
float hudDistanceMetres = 1.5f;
vr::HmdMatrix34_t hudAnchor{};
bool hudAnchorValid = false;
bool hudInvertY = false;
bool hudConfigDirty = true;
bool hudTransformDirty = true;
bool hudCurvatureDirty = true;
float hudVerticalOffsetMetres = 0.0f;
float hudRequestedCurvature = 0.0f;
float hudAppliedCurvature = 0.0f;
int hudCurvatureError = vr::VROverlayError_None;
bool hudVisible = false;
int hudError = vr::VROverlayError_None;
uint64_t hudUpdates = 0;
vr::VROverlayHandle_t subtitleHandle = vr::k_ulOverlayHandleInvalid;
ID3D11Texture2D* subtitleTexture = nullptr;
float subtitleWidthMetres = 1.2f;
float subtitleDistanceMetres = 1.0f;
float subtitleVerticalOffsetMetres = -0.4f;
float subtitleHorizontalOffsetMetres = 0.0f;
bool subtitleInvertY = false;
bool subtitleConfigDirty = true;
bool subtitleVisible = false;
int subtitleError = vr::VROverlayError_None;
uint64_t subtitleUpdates = 0;

struct TimingAccumulator {
    uint64_t ticks = 0;
    uint64_t calls = 0;
};
TimingAccumulator poseTiming;
TimingAccumulator stereoTiming;
TimingAccumulator hudTiming;

constexpr uint32_t kFrameTimingCapacity = 120;
vr::Compositor_FrameTiming frameTimings[kFrameTimingCapacity]{};
uint32_t frameTimingCount = 0, frameTimingNext = 0;
LARGE_INTEGER frameTimingLastSample{}, frameTimingLastDisplayQuery{};
double frameTimingDisplayHz = 0.0;
static_assert(sizeof(RGVR_CompositorTiming) == 160, "Compositor timing C ABI must match managed layout");

void ResetCompositorTimingLocked() {
    frameTimingCount = frameTimingNext = 0;
    frameTimingLastSample = frameTimingLastDisplayQuery = {};
    frameTimingDisplayHz = 0.0;
}

double MillisecondsBetween(LARGE_INTEGER start, LARGE_INTEGER end) {
    LARGE_INTEGER frequency{};
    if (start.QuadPart <= 0 || end.QuadPart < start.QuadPart || !QueryPerformanceFrequency(&frequency) || frequency.QuadPart <= 0)
        return 0.0;
    return static_cast<double>(end.QuadPart - start.QuadPart) * 1000.0 / static_cast<double>(frequency.QuadPart);
}

void SampleCompositorTimingLocked() {
    if (!vrSystem || !compositor || submissionCount < 2) return;
    vr::Compositor_FrameTiming next{};
    next.m_nSize = sizeof(next);
    if (!compositor->GetFrameTiming(&next, 1)) return;
    const double appCpuMs = static_cast<double>(next.m_flNewFrameReadyMs) - next.m_flNewPosesReadyMs;
    const double poseWaitMs = static_cast<double>(next.m_flNewPosesReadyMs) - next.m_flWaitGetPosesCalledMs;
    const double values[]{appCpuMs, poseWaitMs, next.m_flPreSubmitGpuMs, next.m_flPostSubmitGpuMs,
        next.m_flTotalRenderGpuMs, next.m_flCompositorRenderCpuMs, next.m_flCompositorRenderGpuMs,
        next.m_flClientFrameIntervalMs, next.m_flPresentCallCpuMs, next.m_flWaitForPresentCpuMs, next.m_flSubmitFrameMs};
    if (!std::isfinite(next.m_flSystemTimeInSeconds) || next.m_flSystemTimeInSeconds <= 0 || next.m_flClientFrameIntervalMs <= 0)
        return;
    for (const double value : values)
        if (!std::isfinite(value) || value < 0 || value > 10000) return;
    LARGE_INTEGER now{};
    if (!QueryPerformanceCounter(&now)) return;
    if (frameTimingCount) {
        const auto& previous = frameTimings[(frameTimingNext + kFrameTimingCapacity - 1) % kFrameTimingCapacity];
        if (next.m_nFrameIndex == previous.m_nFrameIndex) return;
        if (next.m_flSystemTimeInSeconds <= previous.m_flSystemTimeInSeconds ||
            MillisecondsBetween(frameTimingLastSample, now) > 2000.0)
            frameTimingCount = frameTimingNext = 0;
    }
    frameTimings[frameTimingNext] = next;
    frameTimingNext = (frameTimingNext + 1) % kFrameTimingCapacity;
    if (frameTimingCount < kFrameTimingCapacity) ++frameTimingCount;
    frameTimingLastSample = now;
    if (frameTimingLastDisplayQuery.QuadPart == 0 || MillisecondsBetween(frameTimingLastDisplayQuery, now) >= 2000.0) {
        vr::ETrackedPropertyError error = vr::TrackedProp_Success;
        const float frequency = vrSystem->GetFloatTrackedDeviceProperty(vr::k_unTrackedDeviceIndex_Hmd, vr::Prop_DisplayFrequency_Float, &error);
        frameTimingDisplayHz = error == vr::TrackedProp_Success && std::isfinite(frequency) && frequency > 0 ? frequency : 0.0;
        frameTimingLastDisplayQuery = now;
    }
}

struct EventTimer {
    TimingAccumulator& timing;
    LARGE_INTEGER start{};
    bool valid;
    explicit EventTimer(TimingAccumulator& accumulator) : timing(accumulator), valid(QueryPerformanceCounter(&start) != FALSE) {}
    ~EventTimer() {
        LARGE_INTEGER end{};
        if (valid && QueryPerformanceCounter(&end) && end.QuadPart >= start.QuadPart) {
            timing.ticks += static_cast<uint64_t>(end.QuadPart - start.QuadPart);
            ++timing.calls;
        }
    }
    EventTimer(const EventTimer&) = delete;
    EventTimer& operator=(const EventTimer&) = delete;
};

void ReleaseTextures() {
    if (leftTexture) leftTexture->Release();
    if (rightTexture) rightTexture->Release();
    leftTexture = nullptr;
    rightTexture = nullptr;
}

void Identity(float* target) {
    if (!target) return;
    std::memset(target, 0, 12 * sizeof(float));
    target[0] = target[5] = target[10] = 1.0f;
}

bool IsFinite(const vr::HmdMatrix34_t& matrix) {
    for (const auto& row : matrix.m)
        for (float element : row)
            if (!std::isfinite(element)) return false;
    return true;
}

bool IsRigidPose(const vr::HmdMatrix34_t& matrix) {
    if (!IsFinite(matrix)) return false;

    constexpr float tolerance = 0.01f;
    for (int a = 0; a < 3; ++a) {
        for (int b = a; b < 3; ++b) {
            float dot = 0.0f;
            for (int row = 0; row < 3; ++row) dot += matrix.m[row][a] * matrix.m[row][b];
            if (std::fabs(dot - (a == b ? 1.0f : 0.0f)) > tolerance) return false;
        }
    }
    const auto& m = matrix.m;
    const float determinant = m[0][0] * (m[1][1] * m[2][2] - m[1][2] * m[2][1]) -
        m[0][1] * (m[1][0] * m[2][2] - m[1][2] * m[2][0]) +
        m[0][2] * (m[1][0] * m[2][1] - m[1][1] * m[2][0]);
    return std::fabs(determinant - 1.0f) <= tolerance;
}

bool MatrixChanged(const vr::HmdMatrix34_t& first, const vr::HmdMatrix34_t& second) {
    for (int row = 0; row < 3; ++row)
        for (int column = 0; column < 4; ++column)
            if (std::fabs(first.m[row][column] - second.m[row][column]) > 0.0001f) return true;
    return false;
}

bool IsHmdInUse(int activity) {

    return activity == vr::k_EDeviceActivityLevel_Unknown ||
        activity == vr::k_EDeviceActivityLevel_UserInteraction ||
        activity == vr::k_EDeviceActivityLevel_UserInteraction_Timeout;
}

void UpdateTrackingOriginLocked() {
    bool originEvent = false;
    vr::VREvent_t event{};

    for (int count = 0; count < 128 && vrSystem->PollNextEvent(&event, sizeof(event)); ++count) {
        if (event.eventType == vr::VREvent_SeatedZeroPoseReset ||
            event.eventType == vr::VREvent_ChaperoneUniverseHasChanged ||
            event.eventType == vr::VREvent_ChaperoneRoomSetupCommitted)
            originEvent = true;
    }
    const auto nextOrigin = vrSystem->GetSeatedZeroPoseToStandingAbsoluteTrackingPose();
    if (!IsRigidPose(nextOrigin)) { seatedToStandingValid = false; return; }
    if (!seatedToStandingValid || originEvent || MatrixChanged(seatedToStanding, nextOrigin)) {
        ++trackingOriginGeneration;

        hudAnchorValid = false;
        hudTransformDirty = true;
    }
    seatedToStanding = nextOrigin;
    seatedToStandingValid = true;
}

bool IsUsableTexture(ID3D11Texture2D* texture) {
    D3D11_TEXTURE2D_DESC desc{};
    texture->GetDesc(&desc);

    return desc.Width > 0 && desc.Height > 0 && desc.ArraySize == 1 &&
        desc.MipLevels == 1 && desc.SampleDesc.Count == 1 &&
        (desc.BindFlags & D3D11_BIND_RENDER_TARGET) != 0;
}

int AcquirePoseLocked() {
    Identity(cachedPose);
    cachedPoseResult = -1;
    const bool previousPoseValid = trackingPoseValid != 0;
    trackingConnected = trackingPoseValid = 0;
    trackingActivity = vr::k_EDeviceActivityLevel_Unknown;
    if (!vrSystem || !compositor) {
        poseError = vr::VRCompositorError_RequestFailed;
        return cachedPoseResult;
    }
    trackingActivity = vrSystem->GetTrackedDeviceActivityLevel(vr::k_unTrackedDeviceIndex_Hmd);
    trackingConnected = vrSystem->IsTrackedDeviceConnected(vr::k_unTrackedDeviceIndex_Hmd) ? 1 : 0;
    if (resetSeatedRequested) {
        resetSeatedRequested = false;
        if (previousPoseValid && trackingConnected && IsHmdInUse(trackingActivity) && chaperone)
            chaperone->ResetZeroPose(vr::TrackingUniverseSeated);
    }
    vr::TrackedDevicePose_t poses[vr::k_unMaxTrackedDeviceCount]{};
    poseError = compositor->WaitGetPoses(poses, vr::k_unMaxTrackedDeviceCount, nullptr, 0);
    UpdateTrackingOriginLocked();
    if (poseError != vr::VRCompositorError_None) return cachedPoseResult;
    ++poseSerial;
    const auto& hmd = poses[vr::k_unTrackedDeviceIndex_Hmd];
    trackingConnected = hmd.bDeviceIsConnected ? 1 : 0;
    trackingPoseValid = hmd.bDeviceIsConnected && hmd.bPoseIsValid && seatedToStandingValid && IsRigidPose(hmd.mDeviceToAbsoluteTracking) ? 1 : 0;
    if (!trackingPoseValid) {
        cachedPoseResult = 0;
        return cachedPoseResult;
    }
    std::memcpy(cachedPose, hmd.mDeviceToAbsoluteTracking.m, sizeof(cachedPose));
    cachedPoseResult = 1;
    return cachedPoseResult;
}

void ReleaseHudTextureLocked() {
    if (hudTexture) hudTexture->Release();
    hudTexture = nullptr;
}

void DestroyHudOverlayLocked() {
    if (!hudOverlay || hudHandle == vr::k_ulOverlayHandleInvalid) {
        hudVisible = false;
        return;
    }
    const auto hideError = hudOverlay->HideOverlay(hudHandle);
    if (hideError == vr::VROverlayError_None) hudVisible = false;
    const auto clearError = hudOverlay->ClearOverlayTexture(hudHandle);
    const auto destroyError = hudOverlay->DestroyOverlay(hudHandle);
    hudError = hideError != vr::VROverlayError_None ? hideError :
        (clearError != vr::VROverlayError_None ? clearError : destroyError);
    if (destroyError == vr::VROverlayError_None || destroyError == vr::VROverlayError_InvalidHandle ||
        destroyError == vr::VROverlayError_UnknownOverlay) {
        hudHandle = vr::k_ulOverlayHandleInvalid;
        hudVisible = false;
        hudAppliedCurvature = 0.0f;
    }
}

bool CheckHudErrorLocked(vr::EVROverlayError error) {
    if (error == vr::VROverlayError_None) return true;
    hudError = error;
    hudConfigDirty = true;
    hudTransformDirty = hudCurvatureDirty = true;
    if (hudOverlay && hudHandle != vr::k_ulOverlayHandleInvalid &&
        hudOverlay->HideOverlay(hudHandle) == vr::VROverlayError_None)
        hudVisible = false;
    return false;
}

void UpdateHudLocked() {
    if (!hudTexture) {
        DestroyHudOverlayLocked();
        return;
    }
    if (!vrSystem || !compositor) return;
    if (!hudOverlay) hudOverlay = vr::VROverlay();
    if (!hudOverlay) {
        hudError = vr::VROverlayError_RequestFailed;
        return;
    }

    if (!hudAnchorValid || !trackingConnected || !trackingPoseValid || !IsHmdInUse(trackingActivity) ||
        compositor->GetCurrentSceneFocusProcess() != GetCurrentProcessId() ||
        hudOverlay->IsDashboardVisible()) {
        if (hudHandle != vr::k_ulOverlayHandleInvalid) {
            if (!CheckHudErrorLocked(hudOverlay->HideOverlay(hudHandle))) return;
            hudVisible = false;
        }
        return;
    }
    if (hudHandle == vr::k_ulOverlayHandleInvalid) {
        if (!CheckHudErrorLocked(hudOverlay->CreateOverlay("local.amanatsulocationvr.hud", "Amanatsu Location VR HUD", &hudHandle)))
            return;
        hudConfigDirty = true;
        hudTransformDirty = hudCurvatureDirty = true;
        hudAppliedCurvature = 0.0f;
    }
    if (hudConfigDirty) {
        const vr::VRTextureBounds_t bounds{0.0f, hudInvertY ? 1.0f : 0.0f, 1.0f, hudInvertY ? 0.0f : 1.0f};
        const vr::HmdVector2_t mouseScale{{1.0f, 1.0f}};
        if (!CheckHudErrorLocked(hudOverlay->SetOverlayInputMethod(hudHandle, vr::VROverlayInputMethod_None)) ||
            !CheckHudErrorLocked(hudOverlay->SetOverlayMouseScale(hudHandle, &mouseScale)) ||
            !CheckHudErrorLocked(hudOverlay->SetOverlayFlag(hudHandle, vr::VROverlayFlags_VisibleInDashboard, false)) ||
            !CheckHudErrorLocked(hudOverlay->SetOverlayFlag(hudHandle, vr::VROverlayFlags_IgnoreTextureAlpha, false)) ||
            !CheckHudErrorLocked(hudOverlay->SetOverlayFlag(hudHandle, vr::VROverlayFlags_IsPremultiplied, true)) ||
            !CheckHudErrorLocked(hudOverlay->SetOverlayWidthInMeters(hudHandle, hudWidthMetres)) ||
            !CheckHudErrorLocked(hudOverlay->SetOverlayTextureBounds(hudHandle, &bounds)))
            return;
        hudConfigDirty = false;
    }
    if (hudCurvatureDirty) {
        const auto error = hudOverlay->SetOverlayCurvature(hudHandle, hudRequestedCurvature);
        hudCurvatureError = error;
        if (error == vr::VROverlayError_None) {
            hudAppliedCurvature = hudRequestedCurvature;
        } else {

            const auto fallbackError = hudOverlay->SetOverlayCurvature(hudHandle, 0.0f);
            if (fallbackError != vr::VROverlayError_None && hudAppliedCurvature != 0.0f) {
                if (!CheckHudErrorLocked(fallbackError)) return;
            }
            hudAppliedCurvature = 0.0f;
        }
        hudCurvatureDirty = false;
    }
    if (hudTransformDirty) {

        vr::HmdMatrix34_t transform = hudAnchor;
        for (int row = 0; row < 3; ++row)
            transform.m[row][3] += hudVerticalOffsetMetres * hudAnchor.m[row][1] - hudDistanceMetres * hudAnchor.m[row][2];
        if (!CheckHudErrorLocked(hudOverlay->SetOverlayTransformAbsolute(hudHandle, vr::TrackingUniverseSeated, &transform))) return;
        hudTransformDirty = false;
    }
    const vr::Texture_t texture{hudTexture, vr::TextureType_DirectX, vr::ColorSpace_Gamma};
    if (!CheckHudErrorLocked(hudOverlay->SetOverlayTexture(hudHandle, &texture))) return;
    if (!hudVisible && !CheckHudErrorLocked(hudOverlay->ShowOverlay(hudHandle))) return;
    hudVisible = true;
    hudError = vr::VROverlayError_None;
    ++hudUpdates;
}

void ReleaseSubtitleTextureLocked() {
    if (subtitleTexture) subtitleTexture->Release();
    subtitleTexture = nullptr;
}

void DestroySubtitleOverlayLocked() {
    if (!hudOverlay || subtitleHandle == vr::k_ulOverlayHandleInvalid) {
        subtitleVisible = false;
        return;
    }
    const auto hideError = hudOverlay->HideOverlay(subtitleHandle);
    if (hideError == vr::VROverlayError_None) subtitleVisible = false;
    const auto clearError = hudOverlay->ClearOverlayTexture(subtitleHandle);
    const auto destroyError = hudOverlay->DestroyOverlay(subtitleHandle);
    subtitleError = hideError != vr::VROverlayError_None ? hideError :
        (clearError != vr::VROverlayError_None ? clearError : destroyError);
    if (destroyError == vr::VROverlayError_None || destroyError == vr::VROverlayError_InvalidHandle ||
        destroyError == vr::VROverlayError_UnknownOverlay) {
        subtitleHandle = vr::k_ulOverlayHandleInvalid;
        subtitleVisible = false;
    }
}

bool CheckSubtitleErrorLocked(vr::EVROverlayError error) {
    if (error == vr::VROverlayError_None) return true;
    subtitleError = error;
    subtitleConfigDirty = true;
    if (error == vr::VROverlayError_InvalidHandle || error == vr::VROverlayError_UnknownOverlay) {
        subtitleHandle = vr::k_ulOverlayHandleInvalid;
        subtitleVisible = false;
    } else if (hudOverlay && subtitleHandle != vr::k_ulOverlayHandleInvalid &&
        hudOverlay->HideOverlay(subtitleHandle) == vr::VROverlayError_None) {
        subtitleVisible = false;
    }
    return false;
}

void UpdateSubtitleLocked() {
    if (!subtitleTexture) {
        DestroySubtitleOverlayLocked();
        return;
    }
    if (!vrSystem || !compositor) return;
    if (!hudOverlay) hudOverlay = vr::VROverlay();
    if (!hudOverlay) {
        subtitleError = vr::VROverlayError_RequestFailed;
        return;
    }

    if (compositor->GetCurrentSceneFocusProcess() != GetCurrentProcessId() || hudOverlay->IsDashboardVisible()) {
        if (subtitleHandle != vr::k_ulOverlayHandleInvalid) {
            if (!CheckSubtitleErrorLocked(hudOverlay->HideOverlay(subtitleHandle))) return;
            subtitleVisible = false;
        }
        return;
    }
    if (subtitleHandle == vr::k_ulOverlayHandleInvalid) {
        if (!CheckSubtitleErrorLocked(hudOverlay->CreateOverlay("local.amanatsulocationvr.subtitles", "Amanatsu Location VR Subtitles", &subtitleHandle)))
            return;
        subtitleConfigDirty = true;
    }
    if (subtitleConfigDirty) {
        vr::HmdMatrix34_t transform{};
        transform.m[0][0] = transform.m[1][1] = transform.m[2][2] = 1.0f;
        transform.m[0][3] = subtitleHorizontalOffsetMetres;
        transform.m[1][3] = subtitleVerticalOffsetMetres;
        transform.m[2][3] = -subtitleDistanceMetres;
        const vr::VRTextureBounds_t bounds{0.0f, subtitleInvertY ? 1.0f : 0.0f, 1.0f, subtitleInvertY ? 0.0f : 1.0f};
        if (!CheckSubtitleErrorLocked(hudOverlay->SetOverlayInputMethod(subtitleHandle, vr::VROverlayInputMethod_None)) ||
            !CheckSubtitleErrorLocked(hudOverlay->SetOverlayFlag(subtitleHandle, vr::VROverlayFlags_VisibleInDashboard, false)) ||
            !CheckSubtitleErrorLocked(hudOverlay->SetOverlayFlag(subtitleHandle, vr::VROverlayFlags_IgnoreTextureAlpha, false)) ||
            !CheckSubtitleErrorLocked(hudOverlay->SetOverlayFlag(subtitleHandle, vr::VROverlayFlags_IsPremultiplied, true)) ||
            !CheckSubtitleErrorLocked(hudOverlay->SetOverlayWidthInMeters(subtitleHandle, subtitleWidthMetres)) ||
            !CheckSubtitleErrorLocked(hudOverlay->SetOverlayTransformTrackedDeviceRelative(subtitleHandle, vr::k_unTrackedDeviceIndex_Hmd, &transform)) ||
            !CheckSubtitleErrorLocked(hudOverlay->SetOverlayTextureBounds(subtitleHandle, &bounds)))
            return;
        subtitleConfigDirty = false;
    }
    const vr::Texture_t texture{subtitleTexture, vr::TextureType_DirectX, vr::ColorSpace_Gamma};
    if (!CheckSubtitleErrorLocked(hudOverlay->SetOverlayTexture(subtitleHandle, &texture))) return;
    if (!subtitleVisible && !CheckSubtitleErrorLocked(hudOverlay->ShowOverlay(subtitleHandle))) return;
    subtitleVisible = true;
    subtitleError = vr::VROverlayError_None;
    ++subtitleUpdates;
}

void __stdcall OnRenderEvent(int eventId) {
    if (eventId != 1 && eventId != 2) return;
    Guard guard;
    if (eventId == 2) {
        {
            EventTimer timer(poseTiming);
            AcquirePoseLocked();
        }
        SampleCompositorTimingLocked();
        return;
    }
    if (compositor && leftTexture && rightTexture && poseSerial != submittedSerial) {
        EventTimer timer(stereoTiming);
        const vr::Texture_t left{leftTexture, vr::TextureType_DirectX, vr::ColorSpace_Auto};
        const vr::Texture_t right{rightTexture, vr::TextureType_DirectX, vr::ColorSpace_Auto};
        const vr::VRTextureBounds_t bounds{0.0f, invertY ? 1.0f : 0.0f, 1.0f, invertY ? 0.0f : 1.0f};
        leftError = compositor->Submit(vr::Eye_Left, &left, &bounds);
        rightError = compositor->Submit(vr::Eye_Right, &right, &bounds);
        submittedSerial = poseSerial;
        ++submissionCount;

    }

    {
        EventTimer timer(hudTiming);
        UpdateHudLocked();
        UpdateSubtitleLocked();
    }
}
}

int __cdecl RGVR_Init() {
    Guard guard;
    if (vrSystem && compositor) return vr::VRInitError_None;
    vr::EVRInitError error = vr::VRInitError_None;
    vrSystem = vr::VR_Init(&error, vr::VRApplication_Scene);
    if (error != vr::VRInitError_None || !vrSystem) {
        vrSystem = nullptr;
        compositor = nullptr;
        return error != vr::VRInitError_None ? error : vr::VRInitError_Init_Internal;
    }
    compositor = vr::VRCompositor();
    if (!compositor) {
        vr::VR_Shutdown();
        vrSystem = nullptr;
        return vr::VRInitError_Init_InterfaceNotFound;
    }
    compositor->SetTrackingSpace(vr::TrackingUniverseSeated);
    chaperone = vr::VRChaperone();
    trackingConnected = trackingPoseValid = 0;
    trackingActivity = vr::k_EDeviceActivityLevel_Unknown;
    trackingOriginGeneration = 0;
    seatedToStandingValid = resetSeatedRequested = false;
    poseSerial = submittedSerial = submissionCount = 0;
    ResetCompositorTimingLocked();
    leftError = rightError = poseError = vr::VRCompositorError_RequestFailed;
    Identity(cachedPose);
    cachedPoseResult = -1;
    hudError = vr::VROverlayError_None;
    hudUpdates = 0;
    hudConfigDirty = true;
    hudTransformDirty = hudCurvatureDirty = true;
    hudAppliedCurvature = 0.0f;
    hudCurvatureError = vr::VROverlayError_None;
    hudAnchorValid = false;
    subtitleError = vr::VROverlayError_None;
    subtitleUpdates = 0;
    subtitleConfigDirty = true;
    return vr::VRInitError_None;
}

int __cdecl RGVR_GetRecommendedSize(uint32_t* width, uint32_t* height) {
    if (width) *width = 0;
    if (height) *height = 0;
    if (!width || !height) return 0;
    Guard guard;
    if (!vrSystem) return 0;
    vrSystem->GetRecommendedRenderTargetSize(width, height);
    return *width > 0 && *height > 0 ? 1 : 0;
}

int __cdecl RGVR_GetEye(int eye, float* eyeToHead12, float* projectionRaw4) {
    Identity(eyeToHead12);
    if (projectionRaw4) std::memset(projectionRaw4, 0, 4 * sizeof(float));
    if (!eyeToHead12 || !projectionRaw4 || (eye != 0 && eye != 1)) return 0;
    Guard guard;
    if (!vrSystem) return 0;
    const auto selectedEye = static_cast<vr::EVREye>(eye);
    const auto matrix = vrSystem->GetEyeToHeadTransform(selectedEye);
    if (!IsRigidPose(matrix)) return 0;
    std::memcpy(eyeToHead12, matrix.m, sizeof(matrix.m));
    vrSystem->GetProjectionRaw(selectedEye, &projectionRaw4[0], &projectionRaw4[1],
        &projectionRaw4[2], &projectionRaw4[3]);
    for (int i = 0; i < 4; ++i)
        if (!std::isfinite(projectionRaw4[i])) return 0;
    return 1;
}

int __cdecl RGVR_GetPose(float* tracking3x4_12) {
    Identity(tracking3x4_12);
    if (!tracking3x4_12) return -1;
    Guard guard;
    const int result = AcquirePoseLocked();
    std::memcpy(tracking3x4_12, cachedPose, sizeof(cachedPose));
    return result;
}

int __cdecl RGVR_GetCachedPose(float* tracking3x4_12) {
    Identity(tracking3x4_12);
    if (!tracking3x4_12) return -1;
    Guard guard;
    std::memcpy(tracking3x4_12, cachedPose, sizeof(cachedPose));
    return cachedPoseResult;
}

int __cdecl RGVR_GetTrackingState(int* connected, int* poseValid, int* activityLevel, uint64_t* originGeneration) {
    Guard guard;
    if (connected) *connected = trackingConnected;
    if (poseValid) *poseValid = trackingPoseValid;
    if (activityLevel) *activityLevel = trackingActivity;
    if (originGeneration) *originGeneration = trackingOriginGeneration;
    return vrSystem && compositor ? 1 : 0;
}

int __cdecl RGVR_ResetSeatedOrigin() {
    Guard guard;
    if (!vrSystem || !compositor || !chaperone || !trackingConnected || !trackingPoseValid || !IsHmdInUse(trackingActivity)) return 0;
    resetSeatedRequested = true;
    return 1;
}

int __cdecl RGVR_SetTextures(void* left, void* right, int flipY) {
    Guard guard;
    if (!left && !right) {
        ReleaseTextures();
        return 0;
    }
    if (!left || !right) return -1;

    ID3D11Texture2D* nextLeft = nullptr;
    ID3D11Texture2D* nextRight = nullptr;
    const HRESULT leftResult = static_cast<IUnknown*>(left)->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&nextLeft));
    const HRESULT rightResult = static_cast<IUnknown*>(right)->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&nextRight));
    if (FAILED(leftResult) || FAILED(rightResult) || !nextLeft || !nextRight ||
        !IsUsableTexture(nextLeft) || !IsUsableTexture(nextRight)) {
        if (nextLeft) nextLeft->Release();
        if (nextRight) nextRight->Release();
        return -1;
    }

    ReleaseTextures();
    leftTexture = nextLeft;
    rightTexture = nextRight;
    invertY = flipY != 0;
    return 1;
}

int __cdecl RGVR_SetHudTexture(void* texture, float widthMetres, float distanceMetres, int flipY) {
    Guard guard;
    if (!texture) {
        ReleaseHudTextureLocked();
        hudConfigDirty = true;
        return 0;
    }
    if (!std::isfinite(widthMetres) || !std::isfinite(distanceMetres) || widthMetres <= 0 || distanceMetres <= 0) {
        hudError = vr::VROverlayError_InvalidParameter;
        return -1;
    }
    ID3D11Texture2D* nextTexture = nullptr;
    const HRESULT result = static_cast<IUnknown*>(texture)->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&nextTexture));
    if (FAILED(result) || !nextTexture || !IsUsableTexture(nextTexture)) {
        if (nextTexture) nextTexture->Release();
        hudError = vr::VROverlayError_InvalidTexture;
        return -1;
    }

    ReleaseHudTextureLocked();
    hudTexture = nextTexture;
    hudWidthMetres = widthMetres;
    if (hudDistanceMetres != distanceMetres) hudTransformDirty = true;
    hudDistanceMetres = distanceMetres;
    hudInvertY = flipY != 0;
    hudConfigDirty = true;
    hudError = vr::VROverlayError_None;
    return 1;
}

int __cdecl RGVR_SetHudAnchor(const float* referenceTracking3x4_12) {
    Guard guard;
    if (!referenceTracking3x4_12) {
        hudAnchorValid = false;
        hudTransformDirty = true;
        return 0;
    }
    vr::HmdMatrix34_t nextAnchor{};
    std::memcpy(nextAnchor.m, referenceTracking3x4_12, sizeof(nextAnchor.m));
    if (!IsRigidPose(nextAnchor)) {
        hudError = vr::VROverlayError_InvalidParameter;
        return -1;
    }
    if (!hudAnchorValid || MatrixChanged(hudAnchor, nextAnchor)) hudTransformDirty = true;
    hudAnchor = nextAnchor;
    hudAnchorValid = true;
    return 1;
}

int __cdecl RGVR_SetHudGeometry(float verticalOffsetMetres, float curvature) {
    Guard guard;
    if (!std::isfinite(verticalOffsetMetres) || !std::isfinite(curvature) || curvature < 0.0f || curvature > 1.0f) return -1;
    if (hudVerticalOffsetMetres != verticalOffsetMetres) hudTransformDirty = true;
    if (hudRequestedCurvature != curvature) hudCurvatureDirty = true;
    hudVerticalOffsetMetres = verticalOffsetMetres;
    hudRequestedCurvature = curvature;
    return 1;
}

void __cdecl RGVR_GetHudGeometry(float* verticalOffsetMetres, float* appliedCurvature, int* curvatureError) {
    Guard guard;
    if (verticalOffsetMetres) *verticalOffsetMetres = hudVerticalOffsetMetres;
    if (appliedCurvature) *appliedCurvature = hudAppliedCurvature;
    if (curvatureError) *curvatureError = hudCurvatureError;
}

int __cdecl RGVR_GetHudPoint(float u, float v, float* seatedPoint3) {
    if (seatedPoint3) std::memset(seatedPoint3, 0, 3 * sizeof(float));
    if (!seatedPoint3 || !std::isfinite(u) || !std::isfinite(v) || u < 0.0f || u > 1.0f || v < 0.0f || v > 1.0f) return 0;
    Guard guard;
    if (!vrSystem || !compositor || !hudOverlay || !hudVisible || !hudAnchorValid || !hudTexture ||
        !trackingConnected || !trackingPoseValid || !IsHmdInUse(trackingActivity) ||
        hudHandle == vr::k_ulOverlayHandleInvalid || compositor->GetCurrentSceneFocusProcess() != GetCurrentProcessId() ||
        hudOverlay->IsDashboardVisible()) return 0;
    const vr::HmdVector2_t coordinates{{u, v}};
    vr::HmdMatrix34_t pointTransform{};
    if (hudOverlay->GetTransformForOverlayCoordinates(hudHandle, vr::TrackingUniverseSeated, coordinates, &pointTransform) !=
        vr::VROverlayError_None || !IsRigidPose(pointTransform)) return 0;

    for (int side : {-1, 1}) {
        vr::VROverlayIntersectionParams_t ray{};
        ray.eOrigin = vr::TrackingUniverseSeated;
        for (int axis = 0; axis < 3; ++axis) {
            const float normal = pointTransform.m[axis][2] * static_cast<float>(side);
            ray.vSource.v[axis] = pointTransform.m[axis][3] + 0.05f * normal;
            ray.vDirection.v[axis] = -normal;
        }
        vr::VROverlayIntersectionResults_t hit{};
        if (!hudOverlay->ComputeOverlayIntersection(hudHandle, &ray, &hit) ||
            !std::isfinite(hit.vUVs.v[0]) || !std::isfinite(hit.vUVs.v[1]) ||
            std::fabs(hit.vUVs.v[0] - u) > 0.0025f || std::fabs(hit.vUVs.v[1] - v) > 0.0025f) continue;
        float distanceSquared = 0.0f;
        for (int axis = 0; axis < 3; ++axis) {
            const float difference = hit.vPoint.v[axis] - pointTransform.m[axis][3];
            distanceSquared += difference * difference;
        }
        if (!std::isfinite(distanceSquared) || distanceSquared > 0.0001f) continue;
        for (int axis = 0; axis < 3; ++axis) seatedPoint3[axis] = pointTransform.m[axis][3];
        return 1;
    }
    return 0;
}

void __cdecl RGVR_GetHudStats(int* overlayError, int* visible, uint64_t* updates) {
    Guard guard;
    if (overlayError) *overlayError = hudError;
    if (visible) *visible = hudVisible ? 1 : 0;
    if (updates) *updates = hudUpdates;
}

int __cdecl RGVR_SetSubtitleTexture(void* texture, float widthMetres, float distanceMetres, float verticalOffsetMetres, int flipY) {
    Guard guard;
    if (!texture) {
        ReleaseSubtitleTextureLocked();
        subtitleConfigDirty = true;
        return 0;
    }
    if (!std::isfinite(widthMetres) || !std::isfinite(distanceMetres) || !std::isfinite(verticalOffsetMetres) ||
        widthMetres <= 0 || distanceMetres <= 0) {
        subtitleError = vr::VROverlayError_InvalidParameter;
        return -1;
    }
    ID3D11Texture2D* nextTexture = nullptr;
    const HRESULT result = static_cast<IUnknown*>(texture)->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&nextTexture));
    if (FAILED(result) || !nextTexture || !IsUsableTexture(nextTexture)) {
        if (nextTexture) nextTexture->Release();
        subtitleError = vr::VROverlayError_InvalidTexture;
        return -1;
    }

    ReleaseSubtitleTextureLocked();
    subtitleTexture = nextTexture;
    subtitleWidthMetres = widthMetres;
    subtitleDistanceMetres = distanceMetres;
    subtitleVerticalOffsetMetres = verticalOffsetMetres;
    subtitleInvertY = flipY != 0;
    subtitleConfigDirty = true;
    subtitleError = vr::VROverlayError_None;
    return 1;
}

void __cdecl RGVR_GetSubtitleStats(int* overlayError, int* visible, uint64_t* updates) {
    Guard guard;
    if (overlayError) *overlayError = subtitleError;
    if (visible) *visible = subtitleVisible ? 1 : 0;
    if (updates) *updates = subtitleUpdates;
}

int __cdecl RGVR_SetSubtitleHorizontalOffset(float horizontalOffsetMetres) {
    Guard guard;
    if (!std::isfinite(horizontalOffsetMetres) || horizontalOffsetMetres < -1.0f || horizontalOffsetMetres > 1.0f) return -1;
    if (subtitleHorizontalOffsetMetres != horizontalOffsetMetres) subtitleConfigDirty = true;
    subtitleHorizontalOffsetMetres = horizontalOffsetMetres;
    return 1;
}

const char* __cdecl RGVR_HudErrorName(int overlayError) {
    Guard guard;
    if (hudOverlay) return hudOverlay->GetOverlayErrorNameFromEnum(static_cast<vr::EVROverlayError>(overlayError));
    switch (overlayError) {
    case vr::VROverlayError_None: return "VROverlayError_None";
    case vr::VROverlayError_InvalidParameter: return "VROverlayError_InvalidParameter";
    case vr::VROverlayError_InvalidTexture: return "VROverlayError_InvalidTexture";
    case vr::VROverlayError_KeyInUse: return "VROverlayError_KeyInUse";
    case vr::VROverlayError_RequestFailed: return "VROverlayError_RequestFailed";
    default: return "OpenVR overlay error (numeric code available in RGVR_GetHudStats)";
    }
}

void* __cdecl RGVR_GetRenderEventFunc() {
    return reinterpret_cast<void*>(&OnRenderEvent);
}

void __cdecl RGVR_GetStats(int* left, int* right, uint64_t* submissions) {
    Guard guard;
    if (left) *left = leftError;
    if (right) *right = rightError;
    if (submissions) *submissions = submissionCount;
}

void __cdecl RGVR_GetTimingStats(double* poseMilliseconds, double* stereoMilliseconds, double* hudMilliseconds) {
    Guard guard;
    LARGE_INTEGER frequency{};
    const bool validFrequency = QueryPerformanceFrequency(&frequency) != FALSE && frequency.QuadPart > 0;
    const auto average = [validFrequency, frequency](const TimingAccumulator& timing) {
        return validFrequency && timing.calls > 0 ?
            (static_cast<double>(timing.ticks) * 1000.0 / static_cast<double>(frequency.QuadPart) / static_cast<double>(timing.calls)) : 0.0;
    };
    if (poseMilliseconds) *poseMilliseconds = average(poseTiming);
    if (stereoMilliseconds) *stereoMilliseconds = average(stereoTiming);
    if (hudMilliseconds) *hudMilliseconds = average(hudTiming);
    poseTiming = {};
    stereoTiming = {};
    hudTiming = {};
}

int __cdecl RGVR_GetPoseError() {
    Guard guard;
    return poseError;
}

int __cdecl RGVR_GetCompositorTiming(RGVR_CompositorTiming* timing, uint32_t size) {
    if (!timing || size != sizeof(RGVR_CompositorTiming)) return -1;
    Guard guard;
    *timing = {};
    timing->Version = 1;
    if (!frameTimingCount) return 0;
    timing->SampleCount = frameTimingCount;
    const auto& latest = frameTimings[(frameTimingNext + kFrameTimingCapacity - 1) % kFrameTimingCapacity];
    timing->LatestFrameIndex = latest.m_nFrameIndex;
    timing->ReprojectionFlags = latest.m_nReprojectionFlags;
    timing->DisplayHz = frameTimingDisplayHz;
    LARGE_INTEGER now{};
    if (QueryPerformanceCounter(&now)) timing->SampleAgeMs = MillisecondsBetween(frameTimingLastSample, now);
    for (uint32_t i = 0; i < frameTimingCount; ++i) {
        const auto& frame = frameTimings[i];
        const auto flags = frame.m_nReprojectionFlags;
        if (frame.m_nNumFramePresents > 1 || (flags & (vr::VRCompositor_ReprojectionReason_Cpu | vr::VRCompositor_ReprojectionReason_Gpu)))
            ++timing->ReusedFrameCount;
        if (flags & vr::VRCompositor_ReprojectionReason_Cpu) ++timing->CpuLateFrameCount;
        if (flags & vr::VRCompositor_ReprojectionReason_Gpu) ++timing->GpuLateFrameCount;
        if (flags & vr::VRCompositor_ReprojectionMotion) ++timing->MotionSmoothedFrameCount;
        if (VR_COMPOSITOR_NUMBER_OF_THROTTLED_FRAMES(frame) > 0) ++timing->ThrottledFrameCount;
        timing->DroppedFrameCount += frame.m_nNumDroppedFrames;
        timing->MisPresentedCount += frame.m_nNumMisPresented;
        timing->AppCpuWallMs += static_cast<double>(frame.m_flNewFrameReadyMs) - frame.m_flNewPosesReadyMs;
        timing->WaitGetPosesMs += static_cast<double>(frame.m_flNewPosesReadyMs) - frame.m_flWaitGetPosesCalledMs;
        timing->PreSubmitGpuMs += frame.m_flPreSubmitGpuMs;
        timing->PostSubmitGpuMs += frame.m_flPostSubmitGpuMs;
        timing->TotalRenderGpuMs += frame.m_flTotalRenderGpuMs;
        timing->CompositorCpuMs += frame.m_flCompositorRenderCpuMs;
        timing->CompositorGpuMs += frame.m_flCompositorRenderGpuMs;
        timing->ClientFrameIntervalMs += frame.m_flClientFrameIntervalMs;
        timing->PresentCallMs += frame.m_flPresentCallCpuMs;
        timing->WaitForPresentMs += frame.m_flWaitForPresentCpuMs;
        timing->SubmitMs += frame.m_flSubmitFrameMs;
        if (frame.m_flPreSubmitGpuMs > timing->MaxPreSubmitGpuMs) timing->MaxPreSubmitGpuMs = frame.m_flPreSubmitGpuMs;
    }
    const double count = frameTimingCount;
    timing->AppCpuWallMs /= count; timing->WaitGetPosesMs /= count;
    timing->PreSubmitGpuMs /= count; timing->PostSubmitGpuMs /= count; timing->TotalRenderGpuMs /= count;
    timing->CompositorCpuMs /= count; timing->CompositorGpuMs /= count; timing->ClientFrameIntervalMs /= count;
    timing->PresentCallMs /= count; timing->WaitForPresentMs /= count; timing->SubmitMs /= count;
    return 1;
}

const char* __cdecl RGVR_ErrorName(int initError) {
    Guard guard;
    return vr::VR_GetVRInitErrorAsEnglishDescription(static_cast<vr::EVRInitError>(initError));
}

void __cdecl RGVR_Shutdown() {
    Guard guard;
    DestroyHudOverlayLocked();
    DestroySubtitleOverlayLocked();
    ReleaseHudTextureLocked();
    ReleaseSubtitleTextureLocked();
    ReleaseTextures();
    if (vrSystem) {
        if (compositor) compositor->ClearLastSubmittedFrame();
        vr::VR_Shutdown();
    }
    vrSystem = nullptr;
    compositor = nullptr;
    chaperone = nullptr;
    trackingConnected = trackingPoseValid = 0;
    trackingActivity = vr::k_EDeviceActivityLevel_Unknown;
    trackingOriginGeneration = 0;
    seatedToStandingValid = resetSeatedRequested = false;
    hudOverlay = nullptr;
    hudHandle = vr::k_ulOverlayHandleInvalid;
    hudVisible = false;
    hudConfigDirty = true;
    hudTransformDirty = hudCurvatureDirty = true;
    hudAppliedCurvature = 0.0f;
    hudAnchorValid = false;
    subtitleHandle = vr::k_ulOverlayHandleInvalid;
    subtitleVisible = false;
    subtitleConfigDirty = true;
    poseSerial = submittedSerial = 0;
    ResetCompositorTimingLocked();
    leftError = rightError = poseError = vr::VRCompositorError_RequestFailed;
    Identity(cachedPose);
    cachedPoseResult = -1;
}
