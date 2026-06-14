using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using UnityEngine;

public class JoystickVibrationHidOutput : MonoBehaviour
{
    [Header("Joystick HID")]
    [SerializeField] private int vendorId = 0xCAFE;
    [SerializeField] private int productId = 0x4004;
    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private float reconnectInterval = 1f;

    [Header("Motor report")]
    [SerializeField, Range(0, 255)] private int reportIdPrefix = 0x00;
    [SerializeField, Range(0, 255)] private int motorOnValue = 0x01;
    [SerializeField, Range(0, 255)] private int motorOffValue = 0x00;
    [SerializeField] private bool turnOffOnDisable = true;

    [Header("Debug")]
    [SerializeField] private bool logConnection = true;

    private WindowsHidOutputDevice _device;
    private float _nextReconnectTime;
    private bool _requestedState;
    private bool? _sentState;
    private bool _loggedMissingDevice;
    private bool _loggedWriteFailure;

    public bool IsConnected => _device != null && _device.IsOpen;
    public bool MotorEnabled => _sentState == true;

    private ushort VendorIdValue => (ushort)Mathf.Clamp(vendorId, 0, 0xFFFF);
    private ushort ProductIdValue => (ushort)Mathf.Clamp(productId, 0, 0xFFFF);

    private void Start()
    {
        if (!connectOnStart) return;

        TryConnect();
        TrySendRequestedState(force: true);
    }

    private void Update()
    {
        if (_sentState.HasValue && _sentState.Value == _requestedState) return;

        TrySendRequestedState();
    }

    private void OnDisable()
    {
        if (turnOffOnDisable)
        {
            _requestedState = false;
            TrySendRequestedState(force: true);
        }

        CloseDevice();
    }

    private void OnApplicationQuit()
    {
        if (!turnOffOnDisable) return;

        _requestedState = false;
        TrySendRequestedState(force: true);
    }

    public bool SetMotor(bool enabled)
    {
        _requestedState = enabled;
        return TrySendRequestedState();
    }

    [ContextMenu("Motor ON")]
    private void DebugMotorOn()
    {
        SetMotor(true);
    }

    [ContextMenu("Motor OFF")]
    private void DebugMotorOff()
    {
        SetMotor(false);
    }

    private bool TrySendRequestedState(bool force = false)
    {
        if (!force && _sentState.HasValue && _sentState.Value == _requestedState)
            return true;

        if (!EnsureDevice())
            return false;

        byte reportId = (byte)Mathf.Clamp(reportIdPrefix, 0, 255);
        byte value = (byte)(_requestedState ? motorOnValue : motorOffValue);

        if (_device.WriteOutputReport(reportId, value))
        {
            _sentState = _requestedState;
            _loggedWriteFailure = false;
            return true;
        }

        if (logConnection && !_loggedWriteFailure)
        {
            Debug.LogWarning("[JoystickVibrationHidOutput] No se pudo escribir el reporte HID del motor.");
            _loggedWriteFailure = true;
        }

        CloseDevice();
        ScheduleReconnect();
        return false;
    }

    private bool EnsureDevice()
    {
        if (IsConnected) return true;

        if (Application.isPlaying && Time.unscaledTime < _nextReconnectTime)
            return false;

        return TryConnect();
    }

    private bool TryConnect()
    {
        CloseDevice();

        _device = WindowsHidOutputDevice.Open(VendorIdValue, ProductIdValue);
        if (IsConnected)
        {
            _sentState = null;
            _loggedMissingDevice = false;

            if (logConnection)
                Debug.Log($"[JoystickVibrationHidOutput] Joystick HID conectado VID=0x{VendorIdValue:X4}, PID=0x{ProductIdValue:X4}.");

            return true;
        }

        if (logConnection && !_loggedMissingDevice)
        {
            Debug.LogWarning($"[JoystickVibrationHidOutput] No se encontro el joystick HID VID=0x{VendorIdValue:X4}, PID=0x{ProductIdValue:X4}.");
            _loggedMissingDevice = true;
        }

        ScheduleReconnect();
        return false;
    }

    private void CloseDevice()
    {
        _device?.Dispose();
        _device = null;
        _sentState = null;
    }

    private void ScheduleReconnect()
    {
        _nextReconnectTime = Application.isPlaying
            ? Time.unscaledTime + Mathf.Max(0.1f, reconnectInterval)
            : 0f;
    }

    private void OnValidate()
    {
        vendorId = Mathf.Clamp(vendorId, 0, 0xFFFF);
        productId = Mathf.Clamp(productId, 0, 0xFFFF);
        reconnectInterval = Mathf.Max(0.1f, reconnectInterval);
        reportIdPrefix = Mathf.Clamp(reportIdPrefix, 0, 255);
        motorOnValue = Mathf.Clamp(motorOnValue, 0, 255);
        motorOffValue = Mathf.Clamp(motorOffValue, 0, 255);
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private sealed class WindowsHidOutputDevice : IDisposable
    {
        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;

        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        private readonly SafeFileHandle _handle;
        private WindowsHidOutputDevice(SafeFileHandle handle)
        {
            _handle = handle;
        }

        public bool IsOpen => _handle != null && !_handle.IsInvalid && !_handle.IsClosed;

        public static WindowsHidOutputDevice Open(ushort vendorId, ushort productId)
        {
            string path = FindDevicePath(vendorId, productId);
            if (string.IsNullOrEmpty(path)) return null;

            SafeFileHandle handle = CreateFile(
                path,
                GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero);

            if (handle == null || handle.IsInvalid)
            {
                handle?.Dispose();
                handle = CreateFile(
                    path,
                    GenericRead | GenericWrite,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    FileAttributeNormal,
                    IntPtr.Zero);
            }

            if (handle == null || handle.IsInvalid)
            {
                handle?.Dispose();
                return null;
            }

            return new WindowsHidOutputDevice(handle);
        }

        public bool WriteOutputReport(byte reportId, byte value)
        {
            if (!IsOpen) return false;

            byte[] report = { reportId, value };
            if (WriteFile(_handle, report, report.Length, out int written, IntPtr.Zero)
                && written == report.Length)
            {
                return true;
            }

            return HidD_SetOutputReport(_handle.DangerousGetHandle(), report, report.Length);
        }

        public void Dispose()
        {
            _handle?.Dispose();
        }

        private static string FindDevicePath(ushort vendorId, ushort productId)
        {
            HidD_GetHidGuid(out Guid hidGuid);
            IntPtr deviceInfoSet = SetupDiGetClassDevs(
                ref hidGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                DigcfPresent | DigcfDeviceInterface);

            if (deviceInfoSet == InvalidHandleValue)
                return null;

            try
            {
                for (uint index = 0; ; index++)
                {
                    SP_DEVICE_INTERFACE_DATA interfaceData = new SP_DEVICE_INTERFACE_DATA
                    {
                        cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA))
                    };

                    if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                        break;

                    string path = GetDevicePath(deviceInfoSet, ref interfaceData);
                    if (string.IsNullOrEmpty(path)) continue;

                    if (DeviceMatches(path, vendorId, productId))
                        return path;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            return null;
        }

        private static string GetDevicePath(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA interfaceData)
        {
            SetupDiGetDeviceInterfaceDetail(
                deviceInfoSet,
                ref interfaceData,
                IntPtr.Zero,
                0,
                out uint requiredSize,
                IntPtr.Zero);

            if (requiredSize == 0) return null;

            IntPtr detailData = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);

                bool ok = SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    detailData,
                    requiredSize,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (!ok) return null;

                IntPtr pathPtr = IntPtr.Add(detailData, 4);
                return Marshal.PtrToStringAuto(pathPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(detailData);
            }
        }

        private static bool DeviceMatches(string path, ushort vendorId, ushort productId)
        {
            SafeFileHandle handle = CreateFile(
                path,
                0,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero);

            using (handle)
            {
                if (handle == null || handle.IsInvalid)
                    return false;

                HIDD_ATTRIBUTES attributes = new HIDD_ATTRIBUTES
                {
                    Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES))
                };

                return HidD_GetAttributes(handle.DangerousGetHandle(), ref attributes)
                    && attributes.VendorID == vendorId
                    && attributes.ProductID == productId;
            }
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetOutputReport(IntPtr hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            IntPtr requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(
            SafeFileHandle file,
            byte[] buffer,
            int numberOfBytesToWrite,
            out int numberOfBytesWritten,
            IntPtr overlapped);

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }
    }
#else
    private sealed class WindowsHidOutputDevice : IDisposable
    {
        public bool IsOpen => false;

        public static WindowsHidOutputDevice Open(ushort vendorId, ushort productId)
        {
            return null;
        }

        public bool WriteOutputReport(byte reportId, byte value)
        {
            return false;
        }

        public void Dispose()
        {
        }
    }
#endif
}
