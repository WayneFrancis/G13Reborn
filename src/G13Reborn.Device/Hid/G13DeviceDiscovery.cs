namespace G13Reborn.Device.Hid;

public static class G13DeviceDiscovery
{
    public const ushort VendorId = 0x046D;
    public const ushort ProductId = 0xC21C;

    /// <summary>Includes interfaces whose metadata cannot be read due to driver access.</summary>
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
