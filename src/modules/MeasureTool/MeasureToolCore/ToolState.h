#pragma once

#include <array>
#include <functional>
#include <mutex>
#include <vector>
#include <thread>
#include <unordered_map>

#include <windef.h>
#include <d2d1helper.h>
#include <dCommon.h>

#include <common/Display/monitors.h>
#include <common/utils/serialized.h>

//#define DEBUG_OVERLAY

struct OverlayBoxText
{
    std::array<wchar_t, 32> buffer = {};
};

struct CommonState
{
    std::function<void()> sessionCompletedCallback;
    D2D1::ColorF lineColor = D2D1::ColorF::OrangeRed;
    Box toolbarBoundingBox;

    mutable Serialized<OverlayBoxText> overlayBoxText;
    POINT cursorPosSystemSpace = {}; // updated atomically
    std::atomic_bool closeOnOtherMonitors = false;
};

struct BoundsToolState
{
    struct PerScreen
    {
        std::optional<D2D_POINT_2F> currentRegionStart;
        std::vector<D2D1_RECT_F> measurements;
    };
    std::unordered_map<HWND, PerScreen> perScreen;

    CommonState* commonState = nullptr; // required for WndProc
};

struct MeasureToolState
{
    enum class Mode
    {
        Horizontal,
        Vertical,
        Cross
    };

    struct Global
    {
        uint8_t pixelTolerance = 30;
        bool continuousCapture = true;
        bool drawFeetOnCross = true;
        bool perColorChannelEdgeDetection = false;
        Mode mode = Mode::Cross;
    } global;

    struct PerScreen
    {
        bool cursorInLeftScreenHalf = false;
        bool cursorInTopScreenHalf = false;
        RECT measuredEdges = {};
        // While not in a continuous capturing mode, we need to draw captured backgrounds. These are passed
        // directly from a capturing thread.
        winrt::com_ptr<ID3D11Texture2D> capturedScreenTexture;
        // After the drawing thread finds its capturedScreenTexture, it converts it to
        // a Direct2D compatible bitmap and caches it here
        winrt::com_ptr<ID2D1Bitmap> capturedScreenBitmap;
    };
    std::unordered_map<HWND, PerScreen> perScreen;

    CommonState* commonState = nullptr; // required for WndProc
};

// Concurrently accessing Direct2D and Direct3D APIs make the driver go boom
extern std::recursive_mutex gpuAccessLock;