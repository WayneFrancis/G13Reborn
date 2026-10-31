namespace G13Reborn.Device.Hid;

/// <summary>Owns a read-only Windows HID input stream.</summary>
public sealed class G13InputReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly int _reportLength;

    private G13InputReader(FileStream stream, int reportLength)
    {
        _stream = stream;
        _reportLength = reportLength;
    }

    public static G13InputReader Open(G13DeviceInfo device)
    {
        var handle = NativeHid.OpenForRead(device.Path);
        try
        {
            var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: true);
            return new G13InputReader(stream, Math.Max(8, device.InputReportLength));
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public async Task ReadAsync(Action<byte[]> onReport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onReport);

        while (!cancellationToken.IsCancellationRequested)
        {
            byte[] buffer = new byte[_reportLength];
            int count = await _stream.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);

            if (count == 0)
                throw new EndOfStreamException("The G13 input stream ended.");
            if (count != buffer.Length)
                Array.Resize(ref buffer, count);

            onReport(buffer);
        }
    }

    public void Dispose() => _stream.Dispose();
}
