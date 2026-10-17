using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Microsoft.Win32.SafeHandles;
namespace DS4Windows
{
    public class HidDevice : IDisposable
    {
        public enum ReadStatus
        {
            Success = 0,
            WaitTimedOut = 1,
            WaitFail = 2,
            NoDataRead = 3,
            ReadError = 4,
            NotConnected = 5
        }

        private const int ERROR_IO_PENDING = 997;
        private const int ERROR_INVALID_HANDLE = 6;
        private const int WAIT_TIMEOUT = 258;
        private const uint INFINITE = uint.MaxValue;

        private readonly string _description;
        private readonly string _devicePath;
        private readonly string _parentPath;
        private readonly HidDeviceAttributes _deviceAttributes;

        private readonly HidDeviceCapabilities _deviceCapabilities;
        //private bool _monitorDeviceEvents;
        private string serial = null;
        private volatile SafeFileHandle safeReadHandle;
        private volatile bool isOpen;
        private bool isExclusive;
        private readonly object handleLock = new object();
        private const string BLANK_SERIAL = "00:00:00:00:00:00";

        // Reusable OVERLAPPED structures. Input and output reports are normally
        // performed on different threads so each direction gets its own context.
        // Avoids creating a kernel event object for every single report
        private readonly OverlappedContext readContext = new OverlappedContext();
        private readonly OverlappedContext writeContext = new OverlappedContext();

        internal HidDevice(string devicePath, string description = null, string parentPath = null)
        {
            _devicePath = devicePath;
            _description = description;
            _parentPath = parentPath;

            try
            {
                using var hidHandle = OpenHandle(_devicePath, false, enumerate: true);

                _deviceAttributes = GetDeviceAttributes(hidHandle);
                _deviceCapabilities = GetDeviceCapabilities(hidHandle);
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                throw new Exception(string.Format("Error querying HID device '{0}'.", devicePath), exception);
            }
        }

        public SafeFileHandle SafeReadHandle { get => safeReadHandle; private set => safeReadHandle = value; }
        public bool IsOpen { get => isOpen; private set => isOpen = value; }
        public bool IsExclusive { get => isExclusive; private set => isExclusive = value; }
        public bool IsConnected { get { return HidDevices.IsConnected(_devicePath); } }
        public string Description { get { return _description; } }
        public HidDeviceCapabilities Capabilities { get { return _deviceCapabilities; } }
        public HidDeviceAttributes Attributes { get { return _deviceAttributes; } }
        public string DevicePath { get { return _devicePath; } }
        public string ParentPath { get => _parentPath; }

        public override string ToString()
        {
            return string.Format("VendorID={0}, ProductID={1}, Version={2}, DevicePath={3}",
                                _deviceAttributes.VendorHexId,
                                _deviceAttributes.ProductHexId,
                                _deviceAttributes.Version,
                                _devicePath);
        }

        public void OpenDevice(bool exclusive)
        {
            lock (handleLock)
            {
                if (IsOpen) return;
                try
                {
                    SafeFileHandle current = SafeReadHandle;
                    if (current == null || current.IsInvalid || current.IsClosed)
                    {
                        current?.Dispose();
                        SafeReadHandle = OpenHandle(_devicePath, exclusive, enumerate: false);
                    }
                }
                catch (Exception exception)
                {
                    IsOpen = false;
                    throw new Exception("Error opening HID device.", exception);
                }

                IsOpen = !SafeReadHandle.IsInvalid;
                IsExclusive = exclusive;
            }
        }

        public void CloseDevice()
        {
            SafeFileHandle handle;
            lock (handleLock)
            {
                if (!IsOpen) return;

                IsOpen = false;
                handle = SafeReadHandle;
            }

            if (handle != null && !handle.IsInvalid && !handle.IsClosed)
            {
                // Wake up any thread blocked in a read or write first. The
                // SafeHandle defers the real CloseHandle call until all pending
                // P/Invoke calls using the handle have released it
                CancelAllIo(handle);
                handle.Dispose();
            }
        }

        public void Dispose()
        {
            CancelIO();
            CloseDevice();
            GC.SuppressFinalize(this);
        }

        public void CancelIO()
        {
            if (IsOpen)
                CancelAllIo(SafeReadHandle);
        }

        private static void CancelAllIo(SafeFileHandle handle)
        {
            if (handle == null || handle.IsInvalid || handle.IsClosed)
                return;

            bool addedRef = false;
            try
            {
                handle.DangerousAddRef(ref addedRef);
                NativeMethods.CancelIoEx(handle.DangerousGetHandle(), IntPtr.Zero);
            }
            catch (ObjectDisposedException) { }
            finally
            {
                if (addedRef) handle.DangerousRelease();
            }
        }

        /// <summary>
        /// Return the handle only when it is usable for I/O
        /// </summary>
        private SafeFileHandle UsableHandle()
        {
            SafeFileHandle handle = SafeReadHandle;
            if (handle == null || handle.IsInvalid || handle.IsClosed)
                return null;

            return handle;
        }

        [Obsolete("Unused.")]
        public bool ReadInputReport(byte[] data)
        {
            SafeFileHandle handle = UsableHandle();
            if (handle == null) return false;

            try
            {
                return NativeMethods.HidD_GetInputReport(handle, data, data.Length);
            }
            catch (ObjectDisposedException) { return false; }
        }

        public bool WriteFeatureReport(byte[] data)
        {
            SafeFileHandle handle = UsableHandle();
            if (!IsOpen || handle == null) return false;

            try
            {
                return NativeMethods.HidD_SetFeature(handle, data, data.Length);
            }
            catch (ObjectDisposedException) { return false; }
        }

        public bool SetNumInputBuffers(int numberBuffers)
        {
            SafeFileHandle handle = UsableHandle();
            if (handle == null) return false;

            try
            {
                return NativeMethods.HidD_SetNumInputBuffers(handle, numberBuffers);
            }
            catch (ObjectDisposedException) { return false; }
        }

        private static HidDeviceAttributes GetDeviceAttributes(SafeFileHandle hidHandle)
        {
            var deviceAttributes = default(NativeMethods.HIDD_ATTRIBUTES);
            deviceAttributes.Size = Marshal.SizeOf(deviceAttributes);
            if (!hidHandle.IsInvalid)
                NativeMethods.HidD_GetAttributes(hidHandle.DangerousGetHandle(), ref deviceAttributes);
            return new HidDeviceAttributes(deviceAttributes);
        }

        private static HidDeviceCapabilities GetDeviceCapabilities(SafeFileHandle hidHandle)
        {
            var capabilities = default(NativeMethods.HIDP_CAPS);
            var preparsedDataPointer = default(IntPtr);

            if (hidHandle.IsInvalid ||
                !NativeMethods.HidD_GetPreparsedData(hidHandle.DangerousGetHandle(), ref preparsedDataPointer))
                return new HidDeviceCapabilities(capabilities);

            NativeMethods.HidP_GetCaps(preparsedDataPointer, ref capabilities);
            NativeMethods.HidD_FreePreparsedData(preparsedDataPointer);

            return new HidDeviceCapabilities(capabilities);
        }

        [Obsolete("Unused.")]
        public void flush_Queue()
        {
            SafeFileHandle handle = UsableHandle();
            if (handle != null)
            {
                NativeMethods.HidD_FlushQueue(handle);
            }
        }

        /// <summary>
        /// Read an input report. A finite timeout (in ms) cancels the pending read
        /// and returns <see cref="ReadStatus.WaitTimedOut"/> if no report arrived.
        /// </summary>
        public ReadStatus ReadFile(Span<byte> inputBuffer, uint timeout = INFINITE)
        {
            return PerformOverlappedIo(readContext, inputBuffer, timeout, write: false);
        }

        public bool WriteOutputReportViaControl(byte[] outputBuffer)
        {
            SafeFileHandle handle = UsableHandle();
            if (handle == null) return false;

            try
            {
                return NativeMethods.HidD_SetOutputReport(handle, outputBuffer, outputBuffer.Length);
            }
            catch (ObjectDisposedException) { return false; }
        }

        /// <summary>
        /// Write an output report through the interrupt endpoint. The operation
        /// always waits for completion (the timeout parameter is kept for API
        /// compatibility). It gets aborted when the device is closed.
        /// </summary>
        public bool WriteOutputReportViaInterrupt(byte[] outputBuffer, int timeout)
        {
            return PerformOverlappedIo(writeContext, outputBuffer, INFINITE, write: true) == ReadStatus.Success;
        }

        private unsafe ReadStatus PerformOverlappedIo(OverlappedContext sharedContext, Span<byte> buffer,
            uint timeout, bool write)
        {
            SafeFileHandle handle = UsableHandle();
            if (handle == null)
            {
                Marshal.SetLastPInvokeError(ERROR_INVALID_HANDLE);
                return ReadStatus.NotConnected;
            }

            // Normal case: one thread per direction so the shared context is free.
            // Fall back to a temporary context if another thread is using it
            OverlappedContext context = sharedContext;
            bool lockTaken = false;
            bool addedRef = false;
            try
            {
                Monitor.TryEnter(context.SyncRoot, ref lockTaken);
                if (!lockTaken)
                {
                    context = new OverlappedContext();
                }

                handle.DangerousAddRef(ref addedRef);
                IntPtr rawHandle = handle.DangerousGetHandle();
                NativeOverlapped* overlapped = context.Prepare();
                uint transferred = 0;

                // Buffer stays pinned until the I/O operation is fully completed
                // or cancelled, even when a timeout occurs
                fixed (byte* bufferPtr = buffer)
                {
                    bool success = write
                        ? NativeMethods.WriteFileOverlapped(rawHandle, bufferPtr, (uint)buffer.Length, null, overlapped)
                        : NativeMethods.ReadFileOverlapped(rawHandle, bufferPtr, (uint)buffer.Length, null, overlapped);
                    if (success)
                        return ReadStatus.Success;

                    if (Marshal.GetLastPInvokeError() != ERROR_IO_PENDING)
                        return ReadStatus.ReadError;

                    if (NativeMethods.GetOverlappedResultEx(rawHandle, overlapped, &transferred, timeout, false))
                        return ReadStatus.Success;

                    int waitError = Marshal.GetLastPInvokeError();

                    // Whatever the failure, make sure the kernel is done with the buffer
                    // and OVERLAPPED structure before leaving: cancel the request (no-op
                    // if it already completed) and wait for its final status
                    NativeMethods.CancelIoEx(rawHandle, overlapped);
                    bool completed = NativeMethods.GetOverlappedResult(rawHandle, overlapped, &transferred, true);
                    if (waitError == WAIT_TIMEOUT)
                    {
                        if (completed)
                        {
                            // Request completed before the cancellation took effect
                            return ReadStatus.Success;
                        }

                        Marshal.SetLastPInvokeError(WAIT_TIMEOUT);
                        return ReadStatus.WaitTimedOut;
                    }

                    Marshal.SetLastPInvokeError(waitError);
                    return ReadStatus.ReadError;
                }
            }
            catch (ObjectDisposedException)
            {
                Marshal.SetLastPInvokeError(ERROR_INVALID_HANDLE);
                return ReadStatus.NotConnected;
            }
            finally
            {
                if (addedRef) handle.DangerousRelease();
                if (lockTaken) Monitor.Exit(context.SyncRoot);
                else if (!ReferenceEquals(context, sharedContext)) context.Dispose();
            }
        }

        private SafeFileHandle OpenHandle(string devicePathName, bool isExclusive, bool enumerate)
        {
            return PInvoke.CreateFile(
                devicePathName,
                enumerate
                    ? (uint)FILE_ACCESS_RIGHTS.FILE_GENERIC_READ
                    : (uint)(FILE_ACCESS_RIGHTS.FILE_GENERIC_READ | FILE_ACCESS_RIGHTS.FILE_GENERIC_WRITE),
                isExclusive
                    ? 0
                    : FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                null,
                FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_OVERLAPPED,
                null
            );
        }

        public bool readFeatureData(byte[] inputBuffer)
        {
            SafeFileHandle handle = UsableHandle();
            if (handle == null) return false;

            try
            {
                return NativeMethods.HidD_GetFeature(handle, inputBuffer, inputBuffer.Length);
            }
            catch (ObjectDisposedException) { return false; }
        }

        public void resetSerial()
        {
            serial = null;
        }

        public string ReadSerial(byte featureID = 18)
        {
            if (serial != null)
                return serial;

            // Some devices don't have MAC address (especially gamepads with USB only suports in PC). If the serial number reading fails
            // then use dummy zero MAC address, because there is a good chance the gamepad stll works in DS4Windows app (the code would throw
            // an index out of bounds exception anyway without IF-THEN-ELSE checks after trying to read a serial number).

            if (Capabilities.InputReportByteLength == 64)
            {
                byte[] buffer = new byte[64];
                //buffer[0] = 18;
                buffer[0] = featureID;
                if (readFeatureData(buffer))
                    serial = String.Format("{0:X02}:{1:X02}:{2:X02}:{3:X02}:{4:X02}:{5:X02}",
                        buffer[6], buffer[5], buffer[4], buffer[3], buffer[2], buffer[1]);
            }
            else
            {
                byte[] buffer = new byte[126];
                SafeFileHandle handle = UsableHandle();
                bool success = false;
                try
                {
                    success = handle != null &&
                        NativeMethods.HidD_GetSerialNumberString(handle, buffer, (uint)buffer.Length);
                }
                catch (ObjectDisposedException) { }

                if (success)
                {
                    string MACAddr = System.Text.Encoding.Unicode.GetString(buffer).Replace("\0", string.Empty).ToUpper();
                    if (MACAddr.Length == 12)
                    {
                        MACAddr = $"{MACAddr[0]}{MACAddr[1]}:{MACAddr[2]}{MACAddr[3]}:{MACAddr[4]}{MACAddr[5]}:{MACAddr[6]}{MACAddr[7]}:{MACAddr[8]}{MACAddr[9]}:{MACAddr[10]}{MACAddr[11]}";
                        serial = MACAddr;
                    }
                }
            }

            // If serial# reading failed then generate a dummy MAC address based on HID device path (WinOS generated runtime unique value based on connected usb port and hub or BT channel).
            // The device path remains the same as long the gamepad is always connected to the same usb/BT port, but may be different in other usb ports. Therefore this value is unique
            // as long the same device is always connected to the same usb port.
            if (serial == null)
            {
                AppLogger.LogToGui($"WARNING: Failed to read serial# from a gamepad ({this._deviceAttributes.VendorHexId}/{this._deviceAttributes.ProductHexId}). Generating MAC address from a device path. From now on you should connect this gamepad always into the same USB port or BT pairing host to keep the same device path.", true);
                serial = GenerateFakeHwSerial();
            }

            return serial;
        }

        public string GenerateFakeHwSerial()
        {
            string MACAddr = string.Empty;

            try
            {
                // Substring: \\?\hid#vid_054c&pid_09cc&mi_03#7&1f882A25&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030} -> \\?\hid#vid_054c&pid_09cc&mi_03#7&1f882A25&0&0001#
                int endPos = this.DevicePath.LastIndexOf('{');
                if (endPos < 0)
                    endPos = this.DevicePath.Length;

                // String array: \\?\hid#vid_054c&pid_09cc&mi_03#7&1f882A25&0&0001# -> [0]=\\?\hidvid_054c, [1]=pid_09cc, [2]=mi_037, [3]=1f882A25, [4]=0, [5]=0001
                string[] devPathItems = this.DevicePath.Substring(0, endPos).Replace("#", "").Replace("-", "").Replace("{", "").Replace("}", "").Split('&');

                if (devPathItems.Length >= 3)
                    MACAddr = devPathItems[devPathItems.Length - 3].ToUpper()                   // 1f882A25
                              + devPathItems[devPathItems.Length - 2].ToUpper()                 // 0
                              + devPathItems[devPathItems.Length - 1].TrimStart('0').ToUpper(); // 0001 -> 1
                else if (devPathItems.Length >= 1)
                    // Device and usb hub and port identifiers missing in devicePath string. Fallback to use vendor and product ID values and
                    // take a number from the last part of the devicePath. Hopefully the last part is a usb port number as it usually should be.
                    MACAddr = this._deviceAttributes.VendorId.ToString("X4")
                              + this._deviceAttributes.ProductId.ToString("X4")
                              + devPathItems[devPathItems.Length - 1].TrimStart('0').ToUpper();

                if (!string.IsNullOrEmpty(MACAddr))
                {
                    MACAddr = MACAddr.PadRight(12, '0');
                    MACAddr = $"{MACAddr[0]}{MACAddr[1]}:{MACAddr[2]}{MACAddr[3]}:{MACAddr[4]}{MACAddr[5]}:{MACAddr[6]}{MACAddr[7]}:{MACAddr[8]}{MACAddr[9]}:{MACAddr[10]}{MACAddr[11]}";
                }
                else
                    // Hmm... Shold never come here. Strange format in devicePath because all identifier items of devicePath string are missing.
                    //serial = BLANK_SERIAL;
                    MACAddr = BLANK_SERIAL;
            }
            catch (Exception e)
            {
                AppLogger.LogToGui($"ERROR: Failed to generate runtime MAC address from device path {this.DevicePath}. {e.Message}", true);
                //serial = BLANK_SERIAL;
                MACAddr = BLANK_SERIAL;
            }

            return MACAddr;
        }

        /// <summary>
        /// Unmanaged OVERLAPPED structure plus its manual reset event. Memory is
        /// released by the finalizer once nothing references the context anymore,
        /// which guarantees no I/O request can still be using it.
        /// </summary>
        private sealed unsafe class OverlappedContext : IDisposable
        {
            private NativeOverlapped* overlapped;
            private readonly ManualResetEvent completedEvent = new ManualResetEvent(false);
            public readonly object SyncRoot = new object();

            public OverlappedContext()
            {
                overlapped = (NativeOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(NativeOverlapped));
            }

            public NativeOverlapped* Prepare()
            {
                *overlapped = default;
                overlapped->EventHandle = completedEvent.SafeWaitHandle.DangerousGetHandle();
                return overlapped;
            }

            public void Dispose()
            {
                Free();
                completedEvent.Dispose();
                GC.SuppressFinalize(this);
            }

            private void Free()
            {
                if (overlapped != null)
                {
                    NativeMemory.Free(overlapped);
                    overlapped = null;
                }
            }

            ~OverlappedContext()
            {
                Free();
            }
        }
    }
}
