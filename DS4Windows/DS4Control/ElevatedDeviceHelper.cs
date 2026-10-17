/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace DS4Windows
{
    /// <summary>
    /// Keeps a single elevated DS4Windows child process alive for the whole
    /// session so that re-enabling a controller (needed to grab it in
    /// exclusive mode) only triggers one UAC prompt instead of one per
    /// connection. The child serves requests over a named pipe restricted to
    /// the current user and exits as soon as the parent closes the pipe.
    /// </summary>
    public class ElevatedDeviceHelper : IDisposable
    {
        public const string HELPER_ARG = "device-helper";
        private const int ERROR_CANCELLED = 1223;
        private const int CONNECT_TIMEOUT_MS = 30000;
        private const int MAX_INSTANCE_ID_LENGTH = 512;

        public const int STATUS_SUCCESS = 0;
        public const int STATUS_FAILURE = 1;
        public const int STATUS_INVALID_REQUEST = 2;

        private readonly object lockObj = new object();
        private Process helperProcess;
        private NamedPipeClientStream pipe;
        private StreamReader reader;
        private StreamWriter writer;
        private bool userDeclined;

        /// <summary>
        /// True once the user refused the UAC prompt. No further prompt is
        /// shown until DS4Windows is restarted
        /// </summary>
        public bool UserDeclined => userDeclined;

        /// <summary>
        /// Ask the elevated helper to disable then re-enable a HID device.
        /// Starts the helper (single UAC prompt) on first use
        /// </summary>
        /// <returns>STATUS_SUCCESS when the device was cycled</returns>
        public int ReEnableDevice(string instanceId)
        {
            lock (lockObj)
            {
                if (userDeclined)
                    return STATUS_FAILURE;

                // A dead helper (killed, crashed) gets restarted once
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!EnsureHelper())
                        return STATUS_FAILURE;

                    try
                    {
                        writer.WriteLine(instanceId);
                        string response = reader.ReadLine();
                        if (response != null && int.TryParse(response, out int status))
                            return status;
                    }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { }

                    CloseHelper();
                }

                return STATUS_FAILURE;
            }
        }

        private bool EnsureHelper()
        {
            if (pipe != null && pipe.IsConnected && helperProcess != null && !helperProcess.HasExited)
                return true;

            CloseHelper();

            string pipeName = $"DS4Windows-DeviceHelper-{Guid.NewGuid():N}";
            ProcessStartInfo startInfo = new ProcessStartInfo(Global.exelocation)
            {
                Verb = "runas",
                UseShellExecute = true,
                Arguments = $"{HELPER_ARG} {pipeName}",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            try
            {
                helperProcess = Process.Start(startInfo);
            }
            catch (Win32Exception e) when (e.NativeErrorCode == ERROR_CANCELLED)
            {
                userDeclined = true;
                AppLogger.LogToGui("Administrator rights were declined. Exclusive mode will not " +
                    "re-enable controllers until DS4Windows is restarted. Consider using HidHide instead.", true);
                return false;
            }
            catch (Exception)
            {
                return false;
            }

            if (helperProcess == null)
                return false;

            try
            {
                pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                pipe.Connect(CONNECT_TIMEOUT_MS);
                reader = new StreamReader(pipe, new UTF8Encoding(false));
                writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                return true;
            }
            catch (Exception)
            {
                CloseHelper();
                return false;
            }
        }

        private void CloseHelper()
        {
            // Closing the pipe makes the helper exit on its own
            try { writer?.Dispose(); } catch { }
            try { reader?.Dispose(); } catch { }
            try { pipe?.Dispose(); } catch { }
            writer = null;
            reader = null;
            pipe = null;

            if (helperProcess != null)
            {
                try
                {
                    if (!helperProcess.HasExited && !helperProcess.WaitForExit(2000))
                        helperProcess.Kill();
                }
                catch { }

                helperProcess.Dispose();
                helperProcess = null;
            }
        }

        public void Dispose()
        {
            lock (lockObj)
            {
                CloseHelper();
            }
        }

        /// <summary>
        /// Entry point of the elevated child process. Serves re-enable
        /// requests from the parent until the pipe is closed
        /// </summary>
        public static void RunHelper(string pipeName)
        {
            if (string.IsNullOrEmpty(pipeName) || !Global.IsAdministrator())
                return;

            // Only the user that launched DS4Windows may talk to the helper.
            // The elevated token shares the same user SID as the parent
            SecurityIdentifier userSid = WindowsIdentity.GetCurrent().User;
            PipeSecurity security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(userSid,
                PipeAccessRights.ReadWrite, AccessControlType.Allow));

            using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(pipeName,
                PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                0, 0, security);

            // Give up if the parent never connects
            if (!server.WaitForConnectionAsync().Wait(CONNECT_TIMEOUT_MS))
                return;

            using StreamReader reader = new StreamReader(server, new UTF8Encoding(false));
            using StreamWriter writer = new StreamWriter(server, new UTF8Encoding(false)) { AutoFlush = true };

            try
            {
                string instanceId;
                while ((instanceId = reader.ReadLine()) != null)
                {
                    int status;
                    if (!IsValidInstanceId(instanceId))
                    {
                        status = STATUS_INVALID_REQUEST;
                    }
                    else
                    {
                        try
                        {
                            DS4Devices.reEnableDevice(instanceId);
                            status = STATUS_SUCCESS;
                        }
                        catch (Exception)
                        {
                            status = STATUS_FAILURE;
                        }
                    }

                    writer.WriteLine(status);
                }
            }
            catch (IOException) { } // Parent went away
        }

        private static bool IsValidInstanceId(string instanceId)
        {
            // reEnableDevice already restricts the lookup to the HID class.
            // Reject anything that is not a plain HID instance id
            return !string.IsNullOrEmpty(instanceId) &&
                instanceId.Length <= MAX_INSTANCE_ID_LENGTH &&
                instanceId.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase) &&
                instanceId.IndexOfAny(new[] { '\r', '\n', '\0' }) < 0;
        }
    }
}
