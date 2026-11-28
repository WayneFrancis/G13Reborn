namespace G13Reborn.Device.Hid;

public static class K8850Device
{
    public sealed record Interface(string Path, int OutputLength);

    public static IReadOnlyList<Interface> Discover()
    {
        var result = new List<Interface>();
        foreach (var path in NativeHid.EnumeratePaths())
        {
            if (!path.Contains("vid_514c&pid_8850&mi_00", StringComparison.OrdinalIgnoreCase))
                continue;
            using var handle = NativeHid.OpenMetadata(path);
            if (handle.IsInvalid || NativeHid.Usage(handle).UsagePage < 0xff00) continue;
            result.Add(new Interface(path, NativeHid.ReportLengths(handle).Output));
        }
        return result;
    }

    public sealed record InterfaceDetails(string Path, ushort UsagePage, ushort Usage,
        int InputLength, int OutputLength, int FeatureLength, ushort? Version);

    // Metadata only: never open keyboard/mouse collections for report I/O.
    public static IReadOnlyList<InterfaceDetails> DescribeInterfaces()
    {
        var result = new List<InterfaceDetails>();
        foreach (var path in NativeHid.EnumeratePaths())
        {
            if (!path.Contains("vid_514c&pid_8850", StringComparison.OrdinalIgnoreCase)) continue;
            using var handle = NativeHid.OpenMetadata(path);
            if (handle.IsInvalid) continue;
            var usage = NativeHid.Usage(handle);
            var lengths = NativeHid.ReportLengths(handle);
            ushort? version = NativeHid.TryGetVersion(handle, out ushort v) ? v : null;
            result.Add(new(path, usage.UsagePage, usage.Usage, lengths.Input, lengths.Output, lengths.Feature, version));
        }
        return result;
    }

    // Passive diagnostics: no outbound commands, no feature reports and no
    // ordinary keyboard or mouse reports. A separate vendor program may initiate traffic.
    public static async Task ListenAsync(Interface device, Action<byte[]> received,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(received);
        var current = DescribeInterfaces().SingleOrDefault(x =>
            x.Path.Equals(device.Path, StringComparison.OrdinalIgnoreCase) &&
            x.Path.Contains("vid_514c&pid_8850&mi_00", StringComparison.OrdinalIgnoreCase) &&
            x.UsagePage >= 0xff00)
            ?? throw new IOException("K8850 vendor configuration interface not available.");
        if (current.InputLength is < 1 or > 1024)
            throw new NotSupportedException($"Unexpected vendor input report length: {current.InputLength}.");
        using var handle = NativeHid.OpenK8850Input(current.Path);
        using var stream = new FileStream(handle, FileAccess.Read, 1, isAsync: true);
        var buffer = new byte[current.InputLength];
        for (int i = 0; i < 250; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0) return;
            received(buffer.AsSpan(0, count).ToArray());
        }
    }

    public static async Task UploadAsync(Interface device, byte[] binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Length != K8850Protocol.ReportLength || binding[0] != 3 ||
            binding[1] != 0xfd || binding[2] is < 1 or > 25 ||
            binding[3] is < 1 or > 16 || binding[4] is < 1 or > 3)
            throw new ArgumentException("Invalid binding report.", nameof(binding));
        var current = Discover().SingleOrDefault(x => x.Path == device.Path)
            ?? throw new IOException("K8850 configuration interface no longer available.");
        if (current.OutputLength != 64 && current.OutputLength != 65)
            throw new NotSupportedException($"Unexpected output report length: {current.OutputLength}.");
        byte[] Pad(byte[] packet)
        {
            var report = new byte[current.OutputLength];
            packet.CopyTo(report, 0);
            return report;
        }
        var data = Pad(binding);
        var finish = Pad(K8850Protocol.Finish());
        using var handle = NativeHid.OpenK8850Output(current.Path);
        using var stream = new FileStream(handle, FileAccess.Write, 1, isAsync: true);
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(finish, cancellationToken).ConfigureAwait(false);
    }
}
