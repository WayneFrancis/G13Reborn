namespace G13Reborn.Device.Hid;

/// <summary>
/// Explicit, one-shot G13 hardware output. It never starts its own
/// background writer and never touches an input reader's handle.
/// </summary>
public static class G13OutputDevice
{
    public static void SetRgb(G13DeviceInfo device, byte red, byte green, byte blue) =>
        SendFeature(device, G13HardwareReports.Rgb(red, green, blue));

    public static void SetMacroLeds(
        G13DeviceInfo device, bool m1, bool m2, bool m3, bool mr) =>
        SendFeature(device, G13HardwareReports.MacroLeds(m1, m2, m3, mr));

    /// <summary>
    /// Return the device's current M1/M2/M3/MR LED mask, not our last
    /// requested mask. Useful for detecting a competing controller.
    /// </summary>
    public static byte ReadMacroLedMask(G13DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        CheckG13(device);
        if (device.FeatureReportLength < G13HardwareReports.FeatureLength ||
            device.FeatureReportLength > 4096)
            throw new NotSupportedException("Selected HID interface cannot read G13 LED reports.");

        byte[] report = new byte[device.FeatureReportLength];
        report[0] = G13HardwareReports.MacroLedReportId;
        using var handle = NativeHid.OpenForFeatureRead(device.Path);
        NativeHid.GetFeature(handle, report);
        return G13HardwareReports.ReadMacroLedMask(report);
    }

    /// <summary>
    /// Read an explicitly known, already documented G13 feature report.
    /// No SET operations or unsupported report IDs are permitted.
    /// </summary>
    public static byte[] ReadKnownFeature(G13DeviceInfo device, byte reportId)
    {
        ArgumentNullException.ThrowIfNull(device);
        CheckG13(device);
        if (reportId is not G13HardwareReports.MacroLedReportId and
            not G13HardwareReports.RgbReportId)
            throw new ArgumentOutOfRangeException(nameof(reportId),
                "Only verified G13 feature report IDs 05 and 07 are readable.");
        if (device.FeatureReportLength is < G13HardwareReports.FeatureLength or > 4096)
            throw new NotSupportedException("The HID collection does not expose a valid G13 feature report.");

        var report = new byte[device.FeatureReportLength];
        report[0] = reportId;
        using var handle = NativeHid.OpenForFeatureRead(device.Path);
        NativeHid.GetFeature(handle, report);
        if (report[0] != reportId)
            throw new InvalidDataException(
                $"Unexpected returned report ID 0x{report[0]:X2}; expected 0x{reportId:X2}.");
        return report;
    }

    private static void SendFeature(G13DeviceInfo device, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(device);
        CheckG13(device);
        if (device.FeatureReportLength < G13HardwareReports.FeatureLength ||
            device.FeatureReportLength > 4096)
            throw new NotSupportedException(
                $"This HID collection's feature report is {device.FeatureReportLength} bytes; " +
                "a G13 LED control requires at least 5 bytes.");

        byte[] report = new byte[device.FeatureReportLength];
        payload.CopyTo(report, 0);
        using var handle = NativeHid.OpenForWrite(device.Path, asynchronous: false);
        NativeHid.SendFeature(handle, report);
    }

    public static async Task SendLcdFrameAsync(
        G13DeviceInfo device, byte[] frame, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(frame);
        CheckG13(device);
        if (device.OutputReportLength != G13HardwareReports.LcdReportLength)
            throw new NotSupportedException(
                $"This HID collection's output report is {device.OutputReportLength} bytes, " +
                $"not the G13 LCD's {G13HardwareReports.LcdReportLength}.");
        if (frame.Length != G13HardwareReports.LcdReportLength ||
            frame[0] != G13HardwareReports.LcdReportId)
            throw new ArgumentException("Invalid G13 LCD report.", nameof(frame));

        using var handle = NativeHid.OpenForWrite(device.Path, asynchronous: true);
        using var stream = new FileStream(handle, FileAccess.Write, 4096, isAsync: true);
        await stream.WriteAsync(frame, cancel).ConfigureAwait(false);
    }

    private static void CheckG13(G13DeviceInfo device)
    {
        if (device.VendorId != G13DeviceDiscovery.VendorId ||
            device.ProductId != G13DeviceDiscovery.ProductId ||
            !device.Path.Contains("vid_046d&pid_c21c",
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Selected device is not a Logitech G13.");
    }
}
