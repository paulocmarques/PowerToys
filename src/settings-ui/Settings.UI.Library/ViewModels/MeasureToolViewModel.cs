﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.CompilerServices;
using Microsoft.PowerToys.Settings.UI.Library.Helpers;
using Microsoft.PowerToys.Settings.UI.Library.Interfaces;

namespace Microsoft.PowerToys.Settings.UI.Library.ViewModels
{
    public class MeasureToolViewModel : Observable
    {
        private ISettingsUtils SettingsUtils { get; set; }

        private GeneralSettings GeneralSettingsConfig { get; set; }

        private MeasureToolSettings Settings { get; set; }

        public MeasureToolViewModel(ISettingsUtils settingsUtils, ISettingsRepository<GeneralSettings> settingsRepository, ISettingsRepository<MeasureToolSettings> measureToolSettingsRepository, Func<string, int> ipcMSGCallBackFunc)
        {
            SettingsUtils = settingsUtils;

            if (settingsRepository == null)
            {
                throw new ArgumentNullException(nameof(settingsRepository));
            }

            GeneralSettingsConfig = settingsRepository.SettingsConfig;

            if (measureToolSettingsRepository == null)
            {
                throw new ArgumentNullException(nameof(measureToolSettingsRepository));
            }

            Settings = measureToolSettingsRepository.SettingsConfig;

            SendConfigMSG = ipcMSGCallBackFunc;
        }

        public bool IsEnabled
        {
            get => GeneralSettingsConfig.Enabled.MeasureTool;
            set
            {
                if (GeneralSettingsConfig.Enabled.MeasureTool != value)
                {
                    GeneralSettingsConfig.Enabled.MeasureTool = value;
                    OnPropertyChanged(nameof(IsEnabled));

                    OutGoingGeneralSettings outgoing = new OutGoingGeneralSettings(GeneralSettingsConfig);
                    SendConfigMSG(outgoing.ToString());

                    NotifyPropertyChanged();
                }
            }
        }

        public bool ContinuousCapture
        {
            get
            {
                return Settings.Properties.ContinuousCapture;
            }

            set
            {
                if (Settings.Properties.ContinuousCapture != value)
                {
                    Settings.Properties.ContinuousCapture = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public bool DrawFeetOnCross
        {
            get
            {
                return Settings.Properties.DrawFeetOnCross;
            }

            set
            {
                if (Settings.Properties.DrawFeetOnCross != value)
                {
                    Settings.Properties.DrawFeetOnCross = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public string CrossColor
        {
            get
            {
                return Settings.Properties.MeasureCrossColor.Value;
            }

            set
            {
                value = (value != null) ? SettingsUtilities.ToRGBHex(value) : "#FF4500";
                if (!value.Equals(Settings.Properties.MeasureCrossColor.Value, StringComparison.OrdinalIgnoreCase))
                {
                    Settings.Properties.MeasureCrossColor.Value = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public bool PerColorChannelEdgeDetection
        {
            get
            {
                return Settings.Properties.PerColorChannelEdgeDetection;
            }

            set
            {
                if (Settings.Properties.PerColorChannelEdgeDetection != value)
                {
                    Settings.Properties.PerColorChannelEdgeDetection = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public int PixelTolerance
        {
            get
            {
                return Settings.Properties.PixelTolerance.Value;
            }

            set
            {
                if (Settings.Properties.PixelTolerance.Value != value)
                {
                    Settings.Properties.PixelTolerance.Value = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public HotkeySettings ActivationShortcut
        {
            get
            {
                return Settings.Properties.ActivationShortcut;
            }

            set
            {
                if (Settings.Properties.ActivationShortcut != value)
                {
                    Settings.Properties.ActivationShortcut = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            OnPropertyChanged(propertyName);
            SettingsUtils.SaveSettings(Settings.ToJsonString(), MeasureToolSettings.ModuleName);
        }

        private Func<string, int> SendConfigMSG { get; }
    }
}
