﻿#pragma once

#include "Core.g.h"
#include "ToolState.h"
#include "OverlayUI.h"
#include "Settings.h"

#include <common/utils/serialized.h>

namespace winrt::PowerToys::MeasureToolCore::implementation
{
    struct Core : CoreT<Core>
    {
        Core();
        ~Core();
        void StartBoundsTool();
        void StartMeasureTool(const bool horizontal, const bool vertical);
        void SetToolCompletionEvent(ToolSessionCompleted sessionCompletedTrigger);
        void SetToolbarBoundingBox(const uint32_t fromX, const uint32_t fromY, const uint32_t toX, const uint32_t toY);
        void ResetState();
        float GetDPIScaleForWindow(uint64_t windowHandle);
        void MouseCaptureThread();

        std::thread _mouseCaptureThread;
        std::vector<std::thread> _screenCaptureThreads;
        wil::shared_event _stopMouseCaptureThreadSignal;
        
        std::vector<std::unique_ptr<OverlayUIState>> _overlayUIStates;
        Serialized<MeasureToolState> _measureToolState;
        BoundsToolState _boundsToolState;
        CommonState _commonState;
        Settings _settings;
    };
}

namespace winrt::PowerToys::MeasureToolCore::factory_implementation
{
    struct Core : CoreT<Core, implementation::Core>
    {
    };
}
