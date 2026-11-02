using System.Buffers.Binary;

namespace G13Reborn.Mapping;

/// <summary>
/// Binary contract for the experimental, administrator-only KMDF/VHF probe.
/// This class never opens drivers, installs software, or injects input.
/// </summary>
public static class VirtualHidKeyboardProtocol
{
    public const string DevicePath = @"\\.\G13RebornVhid";
    // CTL_CODE(FILE_DEVICE_UNKNOWN=0x22, 0x801, METHOD_BUFFERED=0, FILE_WRITE_DATA=2).
    // Keep this value in sync with IOCTL_G13VHF_KEYBOARD in the native driver.
    public const uint IoctlSendKeyboard = 0x0022A004;
    public const uint Magic = 0x33314856; // ASCII "VH13" little-endian
    public const ushort Version = 1;
    public const int ReportLength = 8;
    public const int PacketLength = 16;
    public const byte HidUsageK = 0x0E;

    public static byte[] CreateReport(byte modifier = 0, params byte[] usages)
    {
        ArgumentNullException.ThrowIfNull(usages);
        if (usages.Length > 6)
            throw new ArgumentOutOfRangeException(nameof(usages),
                "Boot-format keyboard reports support no more than six keys.");
        var report = new byte[ReportLength];
        report[0] = modifier;
        for (int i = 0; i < usages.Length; i++)
        {
            if (usages[i] > 0x65)
                throw new ArgumentOutOfRangeException(nameof(usages),
                    "This probe descriptor supports usages 0..101 only.");
            report[i + 2] = usages[i];
        }
        return report;
    }

    public static byte[] Encode(ReadOnlySpan<byte> report)
    {
        if (report.Length != ReportLength)
            throw new ArgumentException("A keyboard report must be exactly eight bytes.",
                nameof(report));
        if (report[1] != 0)
            throw new ArgumentException("Reserved report byte must be zero.",
                nameof(report));
        for (int i = 2; i < ReportLength; i++)
        {
            if (report[i] > 0x65)
                throw new ArgumentException("Unsupported HID keyboard usage.",
                    nameof(report));
        }

        var packet = new byte[PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(
            packet.AsSpan(6, 2), ReportLength);
        report.CopyTo(packet.AsSpan(8));
        return packet;
    }

    public static byte[] ReleaseAll() => Encode(CreateReport());
}
