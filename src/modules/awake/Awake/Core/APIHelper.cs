﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.PowerToys.Telemetry;
using Microsoft.Win32;
using NLog;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Console;
using Windows.Win32.System.Power;

namespace Awake.Core
{
    /// <summary>
    /// Helper class that allows talking to Win32 APIs without having to rely on PInvoke in other parts
    /// of the codebase.
    /// </summary>
    public class APIHelper
    {
        private const string BuildRegistryLocation = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

        private static readonly Logger _log;
        private static CancellationTokenSource _tokenSource;
        private static CancellationToken _threadToken;

        private static Task? _runnerThread;
        private static System.Timers.Timer _timedLoopTimer;

        static APIHelper()
        {
            _timedLoopTimer = new System.Timers.Timer();
            _log = LogManager.GetCurrentClassLogger();
            _tokenSource = new CancellationTokenSource();
        }

        internal static void SetConsoleControlHandler(PHANDLER_ROUTINE handler, bool addHandler)
        {
            PInvoke.SetConsoleCtrlHandler(handler, addHandler);
        }

        public static void AllocateConsole()
        {
            _log.Debug("Bootstrapping the console allocation routine.");
            PInvoke.AllocConsole();
            _log.Debug($"Console allocation result: {Marshal.GetLastWin32Error()}");

            var outputFilePointer = PInvoke.CreateFile("CONOUT$", FILE_ACCESS_FLAGS.FILE_GENERIC_READ | FILE_ACCESS_FLAGS.FILE_GENERIC_WRITE, FILE_SHARE_MODE.FILE_SHARE_WRITE, null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, 0, null);
            _log.Debug($"CONOUT creation result: {Marshal.GetLastWin32Error()}");

            PInvoke.SetStdHandle(Windows.Win32.System.Console.STD_HANDLE.STD_OUTPUT_HANDLE, outputFilePointer);
            _log.Debug($"SetStdHandle result: {Marshal.GetLastWin32Error()}");

            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding) { AutoFlush = true });
        }

        /// <summary>
        /// Sets the computer awake state using the native Win32 SetThreadExecutionState API. This
        /// function is just a nice-to-have wrapper that helps avoid tracking the success or failure of
        /// the call.
        /// </summary>
        /// <param name="state">Single or multiple EXECUTION_STATE entries.</param>
        /// <returns>true if successful, false if failed</returns>
        private static bool SetAwakeState(EXECUTION_STATE state)
        {
            try
            {
                var stateResult = PInvoke.SetThreadExecutionState(state);
                return stateResult != 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetAwakeStateBasedOnDisplaySetting(bool keepDisplayOn)
        {
            if (keepDisplayOn)
            {
                return SetAwakeState(EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_CONTINUOUS);
            }
            else
            {
                return SetAwakeState(EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_CONTINUOUS);
            }
        }

        public static void CancelExistingThread()
        {
            _tokenSource.Cancel();

            try
            {
                _log.Info("Attempting to ensure that the thread is properly cleaned up...");

                if (_runnerThread != null && !_runnerThread.IsCanceled)
                {
                    _runnerThread.Wait(_threadToken);
                }

                _log.Info("Thread is clean.");
            }
            catch (OperationCanceledException)
            {
                _log.Info("Confirmed background thread cancellation when disabling explicit keep awake.");
            }

            _tokenSource = new CancellationTokenSource();
            _threadToken = _tokenSource.Token;

            _log.Info("Instantiating of new token source and thread token completed.");
        }

        public static void SetIndefiniteKeepAwake(Action callback, Action failureCallback, bool keepDisplayOn = false)
        {
            PowerToysTelemetry.Log.WriteEvent(new Awake.Telemetry.AwakeIndefinitelyKeepAwakeEvent());

            CancelExistingThread();

            try
            {
                _runnerThread = Task.Run(() => RunIndefiniteJob(keepDisplayOn), _threadToken)
                    .ContinueWith((result) => callback, TaskContinuationOptions.OnlyOnRanToCompletion)
                    .ContinueWith((result) => failureCallback, TaskContinuationOptions.NotOnRanToCompletion);
            }
            catch (Exception ex)
            {
                _log.Error(ex.Message);
            }
        }

        public static void SetNoKeepAwake()
        {
            PowerToysTelemetry.Log.WriteEvent(new Awake.Telemetry.AwakeNoKeepAwakeEvent());

            CancelExistingThread();
        }

        public static void SetExpirableKeepAwake(DateTimeOffset expireAt, Action callback, Action failureCallback, bool keepDisplayOn = true)
        {
            PowerToysTelemetry.Log.WriteEvent(new Awake.Telemetry.AwakeExpirableKeepAwakeEvent());

            CancelExistingThread();

            if (expireAt > DateTime.Now && expireAt != null)
            {
                _runnerThread = Task.Run(() => RunExpiringJob(expireAt, keepDisplayOn), _threadToken)
                    .ContinueWith((result) => callback, TaskContinuationOptions.OnlyOnRanToCompletion)
                    .ContinueWith((result) => failureCallback, TaskContinuationOptions.NotOnRanToCompletion);
            }
            else
            {
                // The target date is not in the future.
                _log.Error("The specified target date and time is not in the future.");
                _log.Error($"Current time: {DateTime.Now}\tTarget time: {expireAt}");
            }
        }

        public static void SetTimedKeepAwake(uint seconds, Action callback, Action failureCallback, bool keepDisplayOn = true)
        {
            PowerToysTelemetry.Log.WriteEvent(new Awake.Telemetry.AwakeTimedKeepAwakeEvent());

            CancelExistingThread();

            _runnerThread = Task.Run(() => RunTimedJob(seconds, keepDisplayOn), _threadToken)
                .ContinueWith((result) => callback, TaskContinuationOptions.OnlyOnRanToCompletion)
                .ContinueWith((result) => failureCallback, TaskContinuationOptions.NotOnRanToCompletion);
        }

        private static void RunExpiringJob(DateTimeOffset expireAt, bool keepDisplayOn = false)
        {
            bool success = false;

            // In case cancellation was already requested.
            _threadToken.ThrowIfCancellationRequested();

            try
            {
                success = SetAwakeStateBasedOnDisplaySetting(keepDisplayOn);

                if (success)
                {
                    _log.Info($"Initiated expirable keep awake in background thread: {PInvoke.GetCurrentThreadId()}. Screen on: {keepDisplayOn}");

                    Observable.Timer(expireAt, Scheduler.CurrentThread).Subscribe(
                    _ =>
                    {
                        _log.Info($"Completed expirable thread in {PInvoke.GetCurrentThreadId()}.");
                        CancelExistingThread();
                    },
                    _tokenSource.Token);
                }
                else
                {
                    _log.Info("Could not successfully set up expirable keep awake.");
                }
            }
            catch (OperationCanceledException ex)
            {
                // Task was clearly cancelled.
                _log.Info($"Background thread termination: {PInvoke.GetCurrentThreadId()}. Message: {ex.Message}");
            }
        }

        private static void RunIndefiniteJob(bool keepDisplayOn = false)
        {
            // In case cancellation was already requested.
            _threadToken.ThrowIfCancellationRequested();

            try
            {
                bool success = SetAwakeStateBasedOnDisplaySetting(keepDisplayOn);

                if (success)
                {
                    _log.Info($"Initiated indefinite keep awake in background thread: {PInvoke.GetCurrentThreadId()}. Screen on: {keepDisplayOn}");

                    WaitHandle.WaitAny(new[] { _threadToken.WaitHandle });
                }
                else
                {
                    _log.Info("Could not successfully set up indefinite keep awake.");
                }
            }
            catch (OperationCanceledException ex)
            {
                // Task was clearly cancelled.
                _log.Info($"Background thread termination: {PInvoke.GetCurrentThreadId()}. Message: {ex.Message}");
            }
        }

        internal static void CompleteExit(int exitCode, ManualResetEvent? exitSignal, bool force = false)
        {
            SetNoKeepAwake();

            HWND windowHandle = GetHiddenWindow();

            if (windowHandle != HWND.Null)
            {
                PInvoke.SendMessage(windowHandle, PInvoke.WM_CLOSE, 0, 0);
            }

            if (force)
            {
                PInvoke.PostQuitMessage(0);
            }

            try
            {
                exitSignal?.Set();
                PInvoke.DestroyWindow(windowHandle);
            }
            catch (Exception ex)
            {
                _log.Info($"Exit signal error ${ex}");
            }
        }

        private static void RunTimedJob(uint seconds, bool keepDisplayOn = true)
        {
            bool success = false;

            // In case cancellation was already requested.
            _threadToken.ThrowIfCancellationRequested();

            try
            {
                success = SetAwakeStateBasedOnDisplaySetting(keepDisplayOn);

                if (success)
                {
                    _log.Info($"Initiated timed keep awake in background thread: {PInvoke.GetCurrentThreadId()}. Screen on: {keepDisplayOn}");

                    Observable.Timer(TimeSpan.FromSeconds(seconds), Scheduler.CurrentThread).Subscribe(
                   _ =>
                   {
                       _log.Info($"Completed timed thread in {PInvoke.GetCurrentThreadId()}.");
                       CancelExistingThread();
                   },
                   _tokenSource.Token);
                }
                else
                {
                    _log.Info("Could not set up timed keep-awake with display on.");
                }
            }
            catch (OperationCanceledException ex)
            {
                // Task was clearly cancelled.
                _log.Info($"Background thread termination: {PInvoke.GetCurrentThreadId()}. Message: {ex.Message}");
            }
        }

        public static string GetOperatingSystemBuild()
        {
            try
            {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(BuildRegistryLocation);
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

                if (registryKey != null)
                {
                    var versionString = $"{registryKey.GetValue("ProductName")} {registryKey.GetValue("DisplayVersion")} {registryKey.GetValue("BuildLabEx")}";
                    return versionString;
                }
                else
                {
                    _log.Info("Registry key acquisition for OS failed.");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _log.Info($"Could not get registry key for the build number. Error: {ex.Message}");
                return string.Empty;
            }
        }

        [SuppressMessage("Performance", "CA1806:Do not ignore method results", Justification = "Function returns DWORD value that identifies the current thread, but we do not need it.")]
        internal static IEnumerable<HWND> EnumerateWindowsForProcess(int processId)
        {
            var handles = new List<HWND>();
            var hCurrentWnd = HWND.Null;

            do
            {
                hCurrentWnd = PInvoke.FindWindowEx(HWND.Null, hCurrentWnd, null as string, null);
                uint targetProcessId = 0;
                unsafe
                {
                    PInvoke.GetWindowThreadProcessId(hCurrentWnd, &targetProcessId);
                }

                if (targetProcessId == processId)
                {
                    handles.Add(hCurrentWnd);
                }
            }
            while (hCurrentWnd != IntPtr.Zero);

            return handles;
        }

        [SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "In this context, the string is only converted to a hex value.")]
        internal static HWND GetHiddenWindow()
        {
            IEnumerable<HWND> windowHandles = EnumerateWindowsForProcess(Environment.ProcessId);
            var domain = AppDomain.CurrentDomain.GetHashCode().ToString("x");
            string targetClass = $"{InternalConstants.TrayWindowId}{domain}";

            unsafe
            {
                var classNameLen = 256;
                Span<char> className = stackalloc char[classNameLen];
                foreach (var handle in windowHandles)
                {
                    fixed (char* ptr = className)
                    {
                        int classQueryResult = PInvoke.GetClassName(handle, ptr, classNameLen);
                        if (classQueryResult != 0 && className.ToString().StartsWith(targetClass, StringComparison.InvariantCultureIgnoreCase))
                        {
                            return handle;
                        }
                    }
                }
            }

            return HWND.Null;
        }

        public static Dictionary<string, int> GetDefaultTrayOptions()
        {
            Dictionary<string, int> optionsList = new Dictionary<string, int>
            {
                { "30 minutes", 1800 },
                { "1 hour", 3600 },
                { "2 hours", 7200 },
            };
            return optionsList;
        }
    }
}
