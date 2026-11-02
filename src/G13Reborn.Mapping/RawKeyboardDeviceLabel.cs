namespace G13Reborn.Mapping;

/// <summary>
/// A descriptive hint only. VID/PID C232 distinguishes Logitech virtual
/// keyboard HID interfaces from ordinary keyboards, but cannot by itself
/// distinguish Logitech Gaming Software from G HUB virtual buses.
/// </summary>
public static class RawKeyboardDeviceLabel
{
    public static string Describe(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return "Unidentified keyboard";
        if (deviceName.Contains("VID_046D&PID_C232",
                StringComparison.OrdinalIgnoreCase))
            return "Logitech virtual keyboard (C232; LGS/G HUB unknown)";
        if (deviceName.Contains("VID_046D",
                StringComparison.OrdinalIgnoreCase))
            return "Other Logitech HID keyboard";
        return "Other keyboard";
    }
}
