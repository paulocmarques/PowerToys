﻿#include "pch.h"

#include <common/display/dpi_aware.h>
#include <common/display/monitors.h>
#include <common/utils/logger_helper.h>
#include <common/logger/logger.h>

#include "../MeasureToolModuleInterface/trace.h"
#include "constants.h"
#include "PowerToys.MeasureToolCore.h"
#include "Core.g.cpp"
#include "OverlayUI.h"
#include "ScreenCapturing.h"

//#define DEBUG_PRIMARY_MONITOR_ONLY

std::recursive_mutex gpuAccessLock;

namespace winrt::PowerToys::MeasureToolCore::implementation
{
    void Core::MouseCaptureThread()
    {
        while (!_stopMouseCaptureThreadSignal.is_signaled())
        {
            static_assert(sizeof(_commonState.cursorPosSystemSpace) == sizeof(LONG64));
            POINT cursorPos = {};
            GetCursorPos(&cursorPos);
            InterlockedExchange64(reinterpret_cast<LONG64*>(&_commonState.cursorPosSystemSpace), std::bit_cast<LONG64>(cursorPos));
            std::this_thread::sleep_for(consts::TARGET_FRAME_DURATION);
        }
    }

    Core::Core() :
        _mouseCaptureThread{ [this] { MouseCaptureThread(); } },
        _stopMouseCaptureThreadSignal{ wil::EventOptions::ManualReset }
    {
        Trace::RegisterProvider();
        LoggerHelpers::init_logger(L"Measure Tool", L"Core", "Measure Tool");
    }

    Core::~Core()
    {
        _stopMouseCaptureThreadSignal.SetEvent();
        _mouseCaptureThread.join();

        ResetState();
        Trace::UnregisterProvider();
    }

    void Core::ResetState()
    {
        _commonState.closeOnOtherMonitors = true;
        _overlayUIStates.clear();
        _boundsToolState = { .commonState = &_commonState };
        for (auto& thread : _screenCaptureThreads)
        {
            if (thread.joinable())
            {
                thread.join();
            }
        }
        _screenCaptureThreads.clear();
        _measureToolState.Reset();
        _measureToolState.Access([&](MeasureToolState& s) {
            s.commonState = &_commonState;
        });

        _settings = Settings::LoadFromFile();

        _commonState.lineColor.r = _settings.lineColor[0] / 255.f;
        _commonState.lineColor.g = _settings.lineColor[1] / 255.f;
        _commonState.lineColor.b = _settings.lineColor[2] / 255.f;
        _commonState.closeOnOtherMonitors = false;
    }

    void Core::StartBoundsTool()
    {
        ResetState();

#if defined(DEBUG_PRIMARY_MONITOR_ONLY)
        const auto& monitorInfo = MonitorInfo::GetPrimaryMonitor();
#else
        for (const auto& monitorInfo : MonitorInfo::GetMonitors(true))
#endif
        {
            auto overlayUI = OverlayUIState::Create(_boundsToolState, _commonState, monitorInfo);
#if !defined(DEBUG_PRIMARY_MONITOR_ONLY)
            if (!overlayUI)
                continue;
#endif
            _overlayUIStates.push_back(std::move(overlayUI));
        }
        Trace::BoundsToolActivated();
    }

    void Core::StartMeasureTool(const bool horizontal, const bool vertical)
    {
        ResetState();

        _measureToolState.Access([horizontal, vertical, this](MeasureToolState& state) {
            if (horizontal)
                state.global.mode = vertical ? MeasureToolState::Mode::Cross : MeasureToolState::Mode::Horizontal;
            else
                state.global.mode = MeasureToolState::Mode::Vertical;

            state.global.continuousCapture = _settings.continuousCapture;
            state.global.drawFeetOnCross = _settings.drawFeetOnCross;
            state.global.pixelTolerance = _settings.pixelTolerance;
            state.global.perColorChannelEdgeDetection = _settings.perColorChannelEdgeDetection;
        });

#if defined(DEBUG_PRIMARY_MONITOR_ONLY)
        const auto& monitorInfo = MonitorInfo::GetPrimaryMonitor();
#else
        for (const auto& monitorInfo : MonitorInfo::GetMonitors(true))
#endif
        {
            auto overlayUI = OverlayUIState::Create(_measureToolState, _commonState, monitorInfo);
#if !defined(DEBUG_PRIMARY_MONITOR_ONLY)
            if (!overlayUI)
                continue;
#endif
            _screenCaptureThreads.emplace_back(StartCapturingThread(_commonState,
                                                                    _measureToolState,
                                                                    overlayUI->overlayWindowHandle(),
                                                                    monitorInfo));
            _overlayUIStates.push_back(std::move(overlayUI));
        }
        Trace::MeasureToolActivated();
    }

    void MeasureToolCore::implementation::Core::SetToolCompletionEvent(ToolSessionCompleted sessionCompletedTrigger)
    {
        _commonState.sessionCompletedCallback = [trigger = std::move(sessionCompletedTrigger)] {
            trigger();
        };
    }

    void MeasureToolCore::implementation::Core::SetToolbarBoundingBox(const uint32_t fromX,
                                                                      const uint32_t fromY,
                                                                      const uint32_t toX,
                                                                      const uint32_t toY)
    {
        _commonState.toolbarBoundingBox = Box{ RECT{ .left = static_cast<long>(fromX),
                                                     .top = static_cast<long>(fromY),
                                                     .right = static_cast<long>(toX),
                                                     .bottom = static_cast<long>(toY) } };
    }

    float MeasureToolCore::implementation::Core::GetDPIScaleForWindow(uint64_t windowHandle)
    {
        UINT dpi = DPIAware::DEFAULT_DPI;
        DPIAware::GetScreenDPIForWindow(std::bit_cast<HWND>(windowHandle), dpi);
        return static_cast<float>(dpi) / DPIAware::DEFAULT_DPI;
    }
}
