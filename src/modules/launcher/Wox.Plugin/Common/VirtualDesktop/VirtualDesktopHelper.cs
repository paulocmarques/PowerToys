﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Wox.Plugin.Common.VirtualDesktop.Interop;
using Wox.Plugin.Common.Win32;
using Wox.Plugin.Logger;
using Wox.Plugin.Properties;

namespace Wox.Plugin.Common.VirtualDesktop.Helper
{
    /// <summary>
    /// Helper class to work with Virtual Desktops.
    /// This helper uses only public available and documented COM-Interfaces or informations from registry.
    /// </summary>
    /// <remarks>
    /// To use this helper you have to create an instance of it and access the method via the helper instance.
    /// We are only allowed to use public documented com interfaces.
    /// </remarks>
    /// <SeeAlso href="https://docs.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager">Documentation of IVirtualDesktopManager interface</SeeAlso>
    /// <SeeAlso href="https://docs.microsoft.com/en-us/archive/blogs/winsdk/virtual-desktop-switching-in-windows-10">CSharp example code for IVirtualDesktopManager</SeeAlso>
    public class VirtualDesktopHelper
    {
        /// <summary>
        /// Instance of "Virtual Desktop Manager"
        /// </summary>
        private readonly IVirtualDesktopManager _virtualDesktopManager;

        /// <summary>
        /// Internal settings to enable automatic update of desktop list.
        /// This will be off by default to avoid to many registry queries.
        /// </summary>
        private readonly bool _desktopListAutoUpdate;

        /// <summary>
        /// List of all available Virtual Desktop in their real order
        /// The order and list in the registry is always up to date
        /// </summary>
        private List<Guid> availableDesktops = new List<Guid>();

        /// <summary>
        /// Id of the current visible Desktop.
        /// </summary>
        private Guid currentDesktop;

        /// <summary>
        /// Initializes a new instance of the <see cref="VirtualDesktopHelper"/> class.
        /// </summary>
        /// <param name="desktopListUpdate">Setting to configure if the list of available desktops should update automatically or only when calling <see cref="UpdateDesktopList"/>. Per default this is set to manual update (false) to have less registry queries.</param>
        public VirtualDesktopHelper(bool desktopListUpdate = false)
        {
            try
            {
                _virtualDesktopManager = (IVirtualDesktopManager)new CVirtualDesktopManager();
            }
            catch (COMException ex)
            {
                Log.Exception("Failed to create an instance of COM interface <IVirtualDesktopManager>.", ex, typeof(VirtualDesktopHelper));
                return;
            }

            _desktopListAutoUpdate = desktopListUpdate;
            UpdateDesktopList();
        }

        /// <summary>
        /// Gets a value indicating whether the Virtual Desktop Manager is initialized successfully
        /// </summary>
        public bool VirtualDesktopManagerInitialized
        {
            get { return _virtualDesktopManager != null; }
        }

        /// <summary>
        /// Method to update the list of Virtual Desktops from Registry
        /// The data in the registry are always up to date
        /// </summary>
        public void UpdateDesktopList()
        {
            // List of all desktops
            RegistryKey allDeskSubKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VirtualDesktops", false);
            byte[] allDeskValue = (byte[])allDeskSubKey.GetValue("VirtualDesktopIDs");
            availableDesktops.Clear();

            if (allDeskValue != null)
            {
                // Each guid has a length of 16 elements
                int numberOfDesktops = allDeskValue.Length / 16;

                for (int i = 0; i < numberOfDesktops; i++)
                {
                    byte[] guidArray = new byte[16];
                    Array.ConstrainedCopy(allDeskValue, i * 16, guidArray, 0, 16);
                    availableDesktops.Add(new Guid(guidArray));
                }
            }
            else
            {
                Log.Error("Failed to read the list of existing desktops form registry.", typeof(VirtualDesktopHelper));
            }

            // Guid for current desktop
            int userSessionId = Process.GetCurrentProcess().SessionId;
            RegistryKey currentDeskSubKey = Registry.CurrentUser.OpenSubKey($"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\SessionInfo\\{userSessionId}\\VirtualDesktops", false);
            var currentDeskValue = currentDeskSubKey.GetValue("CurrentVirtualDesktop");
            if (currentDeskValue != null)
            {
                currentDesktop = new Guid((byte[])currentDeskValue);
            }
            else
            {
                // The registry value is missing when the user hasn't switched the desktop at least one time before reading the registry. In this case we can set it to desktop one.
                Log.Debug("Failed to read the id for the current desktop form registry.", typeof(VirtualDesktopHelper));
                currentDesktop = availableDesktops[0];
            }
        }

        /// <summary>
        /// Returns an ordered list with the ids of all existing desktops. The list is ordered in the same way as the existing desktops.
        /// </summary>
        /// <returns>List of desktop ids or an empty list on failure.</returns>
        public List<Guid> GetDesktopIdList()
        {
            if (_desktopListAutoUpdate)
            {
                UpdateDesktopList();
            }

            return availableDesktops;
        }

        /// <summary>
        /// Returns an ordered list with of all existing desktops and their properties. The list is ordered in the same way as the existing desktops.
        /// </summary>
        /// <returns>List of desktops or an empty list on failure.</returns>
        public List<VDesktop> GetDesktopList()
        {
            if (_desktopListAutoUpdate)
            {
                UpdateDesktopList();
            }

            List<VDesktop> list = new List<VDesktop>();
            foreach (Guid d in availableDesktops)
            {
                list.Add(CreateVDesktopInstance(d));
            }

            return list;
        }

        /// <summary>
        /// Returns the count of existing desktops
        /// </summary>
        /// <returns>Number of existing desktops or zero on failure.</returns>
        public int GetDesktopCount()
        {
            if (_desktopListAutoUpdate)
            {
                UpdateDesktopList();
            }

            return availableDesktops.Count;
        }

        /// <summary>
        /// Returns the id of the desktop that is currently visible to the user.
        /// </summary>
        /// <returns>Guid of the current desktop or an empty guid on failure.</returns>
        public Guid GetCurrentDesktopId()
        {
            if (_desktopListAutoUpdate)
            {
                UpdateDesktopList();
            }

            return currentDesktop;
        }

        /// <summary>
        /// Returns an instance of <see cref="VDesktop"/> for the desktop that is currently visible to the user.
        /// </summary>
        /// <returns>An instance of <see cref="VDesktop"/> for the current desktop, or an empty instance of <see cref="VDesktop"/> on failure.</returns>
        public VDesktop GetCurrentDesktop()
        {
            if (_desktopListAutoUpdate)
            {
                UpdateDesktopList();
            }

            return CreateVDesktopInstance(currentDesktop);
        }

        /// <summary>
        /// Checks if a desktop is currently visible to the user.
        /// </summary>
        /// <param name="desktop">The guid of the desktop to check.</param>
        /// <returns>A value indicating if the guid belongs to the currently visible desktop.</returns>
        public bool IsDesktopVisible(Guid desktop)
        {
            if (_desktopListAutoUpdate)
            {
                UpdateDesktopList();
            }

            return currentDesktop == desktop;
        }

        /// <summary>
        /// Returns the number (position) of a desktop.
        /// </summary>
        /// <param name="desktop">The guid of the desktop.</param>
        /// <returns>Number of the desktop, if found. Otherwise a value of zero.</returns>
        public int GetDesktopNumber(Guid desktop)
        {
            if (_desktopListAutoUpdate)
            {
                UpdateDesktopList();
            }

            // Adding +1 because index starts with zero and humans start counting with one.
            return availableDesktops.IndexOf(desktop) + 1;
        }

        /// <summary>
        /// Returns the name of a desktop
        /// </summary>
        /// <param name="desktop">Guid of the desktop</param>
        /// <returns>Returns the name of the desktop or an empty string on failure.</returns>
        public string GetDesktopName(Guid desktop)
        {
            if (desktop == Guid.Empty || !GetDesktopIdList().Contains(desktop))
            {
                Log.Debug($"GetDesktopName() failed. Parameter contains an invalid desktop guid ({desktop}) that doesn't belongs to an available desktop. Maybe the guid belongs to the generic 'AllDesktops' view.", typeof(VirtualDesktopHelper));
                return string.Empty;
            }

            // If the desktop name was not changed by the user, it isn't saved to the registry. Then we need the default name for the desktop.
            var defaultName = string.Format(System.Globalization.CultureInfo.InvariantCulture, Resources.VirtualDesktopHelper_Desktop, GetDesktopNumber(desktop));

            string registryPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VirtualDesktops\\Desktops\\{" + desktop.ToString().ToUpper(System.Globalization.CultureInfo.InvariantCulture) + "}";
            RegistryKey deskSubKey = Registry.CurrentUser.OpenSubKey(registryPath, false);
            var desktopName = deskSubKey?.GetValue("Name");

            return (desktopName != null) ? (string)desktopName : defaultName;
        }

        /// <summary>
        /// Returns the position type for a desktop.
        /// </summary>
        /// <param name="desktop">Guid of the desktop.</param>
        /// <returns>Type of desktop position.</returns>
        public VirtualDesktopPosition GetDesktopPositionType(Guid desktop)
        {
            int desktopNumber = GetDesktopNumber(desktop);
            int desktopCount = GetDesktopCount();

            if (desktopNumber == 1)
            {
                return VirtualDesktopPosition.FirstDesktop;
            }
            else if (desktopNumber == desktopCount)
            {
                return VirtualDesktopPosition.LastDesktop;
            }
            else if (desktopNumber > 1 & desktopNumber < desktopCount)
            {
                return VirtualDesktopPosition.BetweenOtherDesktops;
            }
            else
            {
                return VirtualDesktopPosition.NotApplicable;
            }
        }

        /// <summary>
        /// Returns the desktop id for a window.
        /// </summary>
        /// <param name="hWindow">Handle of the window.</param>
        /// <param name="desktopId">The guid of the desktop, where the window is shown.</param>
        /// <returns>HResult of the called method.</returns>
        public int GetWindowDesktopId(IntPtr hWindow, out Guid desktopId)
        {
            if (_virtualDesktopManager == null)
            {
                Log.Error("GetWindowDesktopId() failed: The instance of <IVirtualDesktopHelper> isn't available.", typeof(VirtualDesktopHelper));
                desktopId = Guid.Empty;
                return unchecked((int)HRESULT.E_UNEXPECTED);
            }

            return _virtualDesktopManager.GetWindowDesktopId(hWindow, out desktopId);
        }

        /// <summary>
        /// Returns an instance of <see cref="VDesktop"/> for the desktop where the window is assigned to.
        /// </summary>
        /// <param name="hWindow">Handle of the window.</param>
        /// <returns>An instance of <see cref="VDesktop"/> for the desktop where the window is assigned to, or an empty instance of <see cref="VDesktop"/> on failure.</returns>
        public VDesktop GetWindowDesktop(IntPtr hWindow)
        {
            if (_virtualDesktopManager == null)
            {
                Log.Error("GetWindowDesktopId() failed: The instance of <IVirtualDesktopHelper> isn't available.", typeof(VirtualDesktopHelper));
                return CreateVDesktopInstance(Guid.Empty);
            }

            int hr = _virtualDesktopManager.GetWindowDesktopId(hWindow, out Guid desktopId);
            return (hr != (int)HRESULT.S_OK || desktopId == Guid.Empty) ? VDesktop.Empty : CreateVDesktopInstance(desktopId, hWindow);
        }

        /// <summary>
        /// Returns the desktop assignment type for a window.
        /// </summary>
        /// <param name="hWindow">Handle of the window.</param>
        /// <returns>Type of <see cref="DesktopAssignment"/>.</returns>
        public VirtualDesktopAssignmentType GetWindowDesktopAssignmentType(IntPtr hWindow)
        {
            if (_virtualDesktopManager == null)
            {
                Log.Error("GetWindowDesktopAssignmentType() failed: The instance of <IVirtualDesktopHelper> isn't available.", typeof(VirtualDesktopHelper));
                return VirtualDesktopAssignmentType.Unknown;
            }

            _ = _virtualDesktopManager.IsWindowOnCurrentVirtualDesktop(hWindow, out int isOnCurrentDesktop);
            int hResult = GetWindowDesktopId(hWindow, out Guid windowDesktopId);

            if (hResult != (int)HRESULT.S_OK)
            {
                return VirtualDesktopAssignmentType.Unknown;
            }
            else if (windowDesktopId == Guid.Empty)
            {
                return VirtualDesktopAssignmentType.NotAssigned;
            }
            else if (isOnCurrentDesktop == 1 && !GetDesktopIdList().Contains(windowDesktopId))
            {
                // These windows are marked as visible on the current desktop, but the desktop id doesn't belongs to an existing desktop.
                // In this case the desktop id belongs to the generic view 'AllDesktops'.
                return VirtualDesktopAssignmentType.AllDesktops;
            }
            else if (isOnCurrentDesktop == 1)
            {
                return VirtualDesktopAssignmentType.CurrentDesktop;
            }
            else
            {
                return VirtualDesktopAssignmentType.OtherDesktop;
            }
        }

        /// <summary>
        /// Returns a value indicating if the window is assigned to a currently visible desktop.
        /// </summary>
        /// <param name="hWindow">Handle to the top level window.</param>
        /// <returns>True if the desktop with the window is visible or if the window is assigned to all desktops. False if the desktop is not visible and on failure,</returns>
        public bool IsWindowOnVisibleDesktop(IntPtr hWindow)
        {
            return GetWindowDesktopAssignmentType(hWindow) == VirtualDesktopAssignmentType.CurrentDesktop || GetWindowDesktopAssignmentType(hWindow) == VirtualDesktopAssignmentType.AllDesktops;
        }

        /// <summary>
        /// Returns a value indicating if the window is cloaked by VirtualDesktopManager.
        /// (A cloaked window is not visible to the user. But the window is still composed by DWM.)
        /// </summary>
        /// <param name="hWindow">Handle of the window.</param>
        /// <returns>A value indicating if the window is cloaked by Virtual Desktop Manager, because it is moved to an other desktop.</returns>
        public bool IsWindowCloakedByVirtualDesktopManager(IntPtr hWindow)
        {
            // If a window is hidden because it is moved to an other desktop, then DWM returns type "CloakedShell". If DWM returns an other type the window is not cloaked by shell or VirtualDesktopManager.
            _ = NativeMethods.DwmGetWindowAttribute(hWindow, (int)DwmWindowAttributes.Cloaked, out int dwmCloakedState, sizeof(uint));
            return GetWindowDesktopAssignmentType(hWindow) == VirtualDesktopAssignmentType.OtherDesktop && dwmCloakedState == (int)DwmWindowCloakStates.CloakedShell;
        }

        /// <summary>
        /// Moves the window to a specific desktop.
        /// </summary>
        /// <param name="hWindow">Handle of the top level window.</param>
        /// <param name="desktopId">Guid of the target desktop.</param>
        /// <returns>True on success and false on failure.</returns>
        public bool MoveWindowToDesktop(IntPtr hWindow, in Guid desktopId)
        {
            if (_virtualDesktopManager == null)
            {
                Log.Error("MoveWindowToDesktop() failed: The instance of <IVirtualDesktopHelper> isn't available.", typeof(VirtualDesktopHelper));
                return false;
            }

            int hr = _virtualDesktopManager.MoveWindowToDesktop(hWindow, desktopId);
            if (hr != (int)HRESULT.S_OK)
            {
                Log.Exception($"Failed to move the window ({hWindow}) to an other desktop ({desktopId}).", Marshal.GetExceptionForHR(hr), typeof(VirtualDesktopHelper));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Move a window one desktop left.
        /// </summary>
        /// <param name="hWindow">Handle of the top level window.</param>
        /// <returns>True on success and false on failure.</returns>
        public bool MoveWindowOneDesktopLeft(IntPtr hWindow)
        {
            if (GetWindowDesktopAssignmentType(hWindow) == VirtualDesktopAssignmentType.Unknown || GetWindowDesktopAssignmentType(hWindow) == VirtualDesktopAssignmentType.NotAssigned)
            {
                return false;
            }

            int hr = GetWindowDesktopId(hWindow, out Guid windowDesktop);
            if (hr != (int)HRESULT.S_OK)
            {
                Log.Error($"Failed to move the window ({hWindow}) one desktop left: Can't get current desktop of the window.", typeof(VirtualDesktopHelper));
                return false;
            }

            int windowDesktopNumber = GetDesktopIdList().IndexOf(windowDesktop);
            if (windowDesktopNumber == 1)
            {
                Log.Error($"Failed to move the window ({hWindow}) one desktop left: The window is on the first desktop.", typeof(VirtualDesktopHelper));
                return false;
            }

            Guid newDesktop = availableDesktops[windowDesktopNumber - 1];
            return MoveWindowToDesktop(hWindow, newDesktop);
        }

        /// <summary>
        /// Move a window one desktop right.
        /// </summary>
        /// <param name="hWindow">Handle of the top level window.</param>
        /// <returns>True on success and false on failure.</returns>
        public bool MoveWindowOneDesktopRight(IntPtr hWindow)
        {
            if (GetWindowDesktopAssignmentType(hWindow) == VirtualDesktopAssignmentType.Unknown || GetWindowDesktopAssignmentType(hWindow) == VirtualDesktopAssignmentType.NotAssigned)
            {
                return false;
            }

            int hr = GetWindowDesktopId(hWindow, out Guid windowDesktop);
            if (hr != (int)HRESULT.S_OK)
            {
                Log.Error($"Failed to move the window ({hWindow}) one desktop right: Can't get current desktop of the window.", typeof(VirtualDesktopHelper));
                return false;
            }

            int windowDesktopNumber = GetDesktopIdList().IndexOf(windowDesktop);
            if (windowDesktopNumber == GetDesktopCount())
            {
                Log.Error($"Failed to move the window ({hWindow}) one desktop right: The window is on the last desktop.", typeof(VirtualDesktopHelper));
                return false;
            }

            Guid newDesktop = availableDesktops[windowDesktopNumber + 1];
            return MoveWindowToDesktop(hWindow, newDesktop);
        }

        /// <summary>
        /// Returns an instance of VDesktop for a Guid.
        /// </summary>
        /// <param name="desktop">Guid of the desktop.</param>
        /// <param name="hWindow">Handle of the window shown on the desktop. If this parameter is set we can detect if it is the AllDesktops view.</param>
        /// <returns>VDesktop instance.</returns>
        private VDesktop CreateVDesktopInstance(Guid desktop, IntPtr hWindow = default)
        {
            // Can be only detected if method is invoked with window handle parameter.
            bool isAllDesktops = (hWindow != default) && GetWindowDesktopAssignmentType(hWindow) == VirtualDesktopAssignmentType.AllDesktops;

            return new VDesktop()
            {
                Id = desktop,
                Name = isAllDesktops ? Resources.VirtualDesktopHelper_AllDesktops : GetDesktopName(desktop),
                Number = GetDesktopNumber(desktop),
                IsVisible = IsDesktopVisible(desktop) || isAllDesktops,
                IsAllDesktopsView = isAllDesktops,
                Position = GetDesktopPositionType(desktop),
            };
        }
    }

    /// <summary>
    /// Enum to show in which way a window is assigned to a desktop
    /// </summary>
    public enum VirtualDesktopAssignmentType
    {
        Unknown = -1,
        NotAssigned = 0,
        AllDesktops = 1,
        CurrentDesktop = 2,
        OtherDesktop = 3,
    }

    /// <summary>
    /// Enum to show the position of a desktop in the list of all desktops
    /// </summary>
    public enum VirtualDesktopPosition
    {
        FirstDesktop,
        BetweenOtherDesktops,
        LastDesktop,
        NotApplicable,
    }
}
