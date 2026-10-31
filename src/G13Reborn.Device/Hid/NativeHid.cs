using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace G13Reborn.Device.Hid;

/// <summary>Read-only Windows SetupAPI / HID interop. Does not send reports.</summary>
internal static class NativeHid
{
    private const uint DigcfPresent = 0x02;
    private const uint DigcfDeviceInterface = 0x10;
    private const uint ShareReadWrite = 0x03;
    private const uint OpenExisting = 3;
    private const uint GenericRead = 0x80000000;
    private const uint FileFlagOverlapped = 0x40000000;
    private const int ErrorNoMoreItems = 259;

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort Version;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint index, ref DeviceInterfaceData interfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref DeviceInterfaceData interfaceData,
        IntPtr detailData, int detailDataSize, out int requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(
        SafeFileHandle handle, ref HidAttributes attributes);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetPreparsedData(
        SafeFileHandle handle, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, IntPtr capabilities);

    internal static IReadOnlyList<string> EnumeratePaths()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows HID enumeration is required.");

        HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = SetupDiGetClassDevs(
            ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed.");

        var paths = new List<string>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var item = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref item))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems) break;
                    throw new Win32Exception(error, "SetupDiEnumDeviceInterfaces failed.");
                }

                SetupDiGetDeviceInterfaceDetail(
                    set, ref item, IntPtr.Zero, 0, out int required, IntPtr.Zero);
                if (required <= 0) continue;

                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(
                        set, ref item, detail, required, out _, IntPtr.Zero))
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(), "Reading HID device path failed.");

                    // DevicePath begins at byte offset 4 on both x86 and x64.
                    string? path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    if (!string.IsNullOrWhiteSpace(path)) paths.Add(path);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return paths;
    }

    internal static SafeFileHandle OpenMetadata(string path) =>
        CreateFile(path, 0, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    internal static SafeFileHandle OpenForRead(string path)
    {
        var handle = CreateFile(path, GenericRead, ShareReadWrite,
            IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error,
                "Could not open the G13 HID interface for reading. " +
                "Logitech software or a driver may hold exclusive access.");
        }
        return handle;
    }

    internal static bool TryGetVersion(SafeFileHandle handle, out ushort version)
    {
        var attrs = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
        bool found = HidD_GetAttributes(handle, ref attrs);
        version = attrs.Version;
        return found;
    }

    internal static (int Input, int Output, int Feature) ReportLengths(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out IntPtr preparsed))
            return (0, 0, 0);

        try
        {
            IntPtr caps = Marshal.AllocHGlobal(128);
            try
            {
                // HIDP_CAPS Input/Output/Feature USHORTs start at offsets 4/6/8.
                if (HidP_GetCaps(preparsed, caps) < 0)
                    return (0, 0, 0);
                return ((ushort)Marshal.ReadInt16(caps, 4),
                    (ushort)Marshal.ReadInt16(caps, 6),
                    (ushort)Marshal.ReadInt16(caps, 8));
            }
            finally { Marshal.FreeHGlobal(caps); }
        }
        finally { HidD_FreePreparsedData(preparsed); }
    }
}
