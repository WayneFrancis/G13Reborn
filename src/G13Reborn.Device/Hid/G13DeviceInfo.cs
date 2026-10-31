namespace G13Reborn.Device.Hid;

public sealed record G13DeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort Version,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength);
