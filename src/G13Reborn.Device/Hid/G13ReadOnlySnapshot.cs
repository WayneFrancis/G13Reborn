using System.Text.Json;

namespace G13Reborn.Device.Hid;

/// <summary>One HID interface and its safely readable, documented feature reports.</summary>
public sealed record G13InterfaceSnapshot(
    string HidPath,
    ushort VendorId,
    ushort ProductId,
    ushort DeviceVersion,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength,
    ushort UsagePage,
    ushort Usage,
    IReadOnlyList<G13KnownFeatureSnapshot> KnownFeatureReports);

public sealed record G13KnownFeatureSnapshot(
    string Name,
    byte ReportId,
    bool ReadSucceeded,
    string? HexBytes,
    string? Error);

/// <summary>
/// A read-only *diagnostic* snapshot, NOT a firmware or onboard-memory dump.
/// Only documented feature IDs 0x05 (M LEDs) and 0x07 (RGB) are queried.
/// </summary>
public sealed record G13ReadOnlySnapshot(
    int SchemaVersion,
    DateTimeOffset CapturedUtc,
    string Scope,
    IReadOnlyList<G13InterfaceSnapshot> Interfaces)
{
    public const string SnapshotScope =
        "READ-ONLY HID interface inventory and known live feature state. " +
        "Does NOT contain stored onboard profiles or firmware memory.";

    public static G13ReadOnlySnapshot Capture()
    {
        var interfaces = new List<G13InterfaceSnapshot>();
        foreach (var info in G13DeviceDiscovery.Enumerate())
        {
            // Report IDs below are already documented and tested on a G13.
            // Never enumerate arbitrary feature IDs: some firmware commands
            // may have side effects even when their purpose is unknown.
            var features = new List<G13KnownFeatureSnapshot>();
            if (info.FeatureReportLength is >= G13HardwareReports.FeatureLength and <= 4096)
            {
                features.Add(ReadKnown(info, "M1/M2/M3/MR indicator LEDs",
                    G13HardwareReports.MacroLedReportId));
                features.Add(ReadKnown(info, "Live RGB backlight",
                    G13HardwareReports.RgbReportId));
            }

            (ushort usagePage, ushort usage) = G13DeviceDiscovery.TryGetUsage(info);
            interfaces.Add(new G13InterfaceSnapshot(
                info.Path, info.VendorId, info.ProductId, info.Version,
                info.InputReportLength, info.OutputReportLength,
                info.FeatureReportLength, usagePage, usage, features));
        }
        return new G13ReadOnlySnapshot(1, DateTimeOffset.UtcNow,
            SnapshotScope, interfaces);
    }

    private static G13KnownFeatureSnapshot ReadKnown(
        G13DeviceInfo info, string name, byte reportId)
    {
        try
        {
            byte[] raw = G13OutputDevice.ReadKnownFeature(info, reportId);
            return new G13KnownFeatureSnapshot(
                name, reportId, true, Convert.ToHexString(raw), null);
        }
        catch (Exception e) when (
            e is System.ComponentModel.Win32Exception or IOException or
            UnauthorizedAccessException or NotSupportedException or
            ArgumentException)
        {
            return new G13KnownFeatureSnapshot(name, reportId, false,
                null, e.Message);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this,
        new JsonSerializerOptions { WriteIndented = true });
}
