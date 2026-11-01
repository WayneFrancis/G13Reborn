namespace G13Reborn.Device.Hid;

public static class G13DeviceDiscovery
{
    public const ushort VendorId = 0x046D;
    public const ushort ProductId = 0xC21C;

    /// <summary>Includes interfaces whose metadata cannot be read due to driver access.</summary>
    /// <summary>Read only HID collection identity; unavailable returns zero.</summary>
    public static (ushort UsagePage, ushort Usage) TryGetUsage(G13DeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.VendorId != VendorId || device.ProductId != ProductId ||
            !device.Path.Contains("vid_046d&pid_c21c",
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Not a Logitech G13 collection.", nameof(device));
        using var handle = NativeHid.OpenMetadata(device.Path);
        return handle.IsInvalid ? (0, 0) : NativeHid.Usage(handle);
    }

    public static IReadOnlyList<G13DeviceInfo> Enumerate()
    {
        var result = new List<G13DeviceInfo>();
        foreach (string path in NativeHid.EnumeratePaths())
        {
            if (!path.Contains("vid_046d&pid_c21c", StringComparison.OrdinalIgnoreCase))
                continue;

            int input = 0, output = 0, feature = 0;
            ushort version = 0;
            using var handle = NativeHid.OpenMetadata(path);
            if (!handle.IsInvalid)
            {
                NativeHid.TryGetVersion(handle, out version);
                (input, output, feature) = NativeHid.ReportLengths(handle);
            }

            result.Add(new G13DeviceInfo(
                path, VendorId, ProductId, version, input, output, feature));
        }
        return result;
    }
}
