﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    public class VideoConferenceConfigProperties
    {
        public VideoConferenceConfigProperties()
        {
            this.MuteCameraAndMicrophoneHotkey = new KeyboardKeysProperty(
                new HotkeySettings()
                {
                    Win = true,
                    Ctrl = false,
                    Alt = false,
                    Shift = true,
                    Key = "Q",
                    Code = 81,
                });

            this.MuteMicrophoneHotkey = new KeyboardKeysProperty(
                new HotkeySettings()
                {
                    Win = true,
                    Ctrl = false,
                    Alt = false,
                    Shift = true,
                    Key = "A",
                    Code = 65,
                });

            this.PushToTalkMicrophoneHotkey = new KeyboardKeysProperty(
                new HotkeySettings()
                {
                    Win = true,
                    Ctrl = false,
                    Alt = false,
                    Shift = true,
                    Key = "I",
                    Code = 73,
                });

            this.MuteCameraHotkey = new KeyboardKeysProperty(
            new HotkeySettings()
            {
                Win = true,
                Ctrl = false,
                Alt = false,
                Shift = true,
                Key = "O",
                Code = 79,
            });

            this.PushToReverseEnabled = new BoolProperty(false);
        }

        [JsonPropertyName("mute_camera_and_microphone_hotkey")]
        public KeyboardKeysProperty MuteCameraAndMicrophoneHotkey { get; set; }

        [JsonPropertyName("mute_microphone_hotkey")]
        public KeyboardKeysProperty MuteMicrophoneHotkey { get; set; }

        [JsonPropertyName("push_to_talk_microphone_hotkey")]
        public KeyboardKeysProperty PushToTalkMicrophoneHotkey { get; set; }

        [JsonPropertyName("push_to_reverse_enabled")]
        public BoolProperty PushToReverseEnabled { get; set; }

        [JsonPropertyName("mute_camera_hotkey")]
        public KeyboardKeysProperty MuteCameraHotkey { get; set; }

        [JsonPropertyName("selected_camera")]
        public StringProperty SelectedCamera { get; set; } = string.Empty;

        [JsonPropertyName("selected_mic")]
        public StringProperty SelectedMicrophone { get; set; } = string.Empty;

        [JsonPropertyName("toolbar_position")]
        public StringProperty ToolbarPosition { get; set; } = "Top right corner";

        [JsonPropertyName("toolbar_monitor")]
        public StringProperty ToolbarMonitor { get; set; } = "Main monitor";

        [JsonPropertyName("camera_overlay_image_path")]
        public StringProperty CameraOverlayImagePath { get; set; } = string.Empty;

        [JsonPropertyName("theme")]
        public StringProperty Theme { get; set; }

        [JsonPropertyName("toolbar_hide")]
        public StringProperty ToolbarHide { get; set; } = "When both camera and microphone are unmuted";

        // converts the current to a json string.
        public string ToJsonString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
