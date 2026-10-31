namespace G13Reborn.Device;

/// <summary>G13 8-byte input report (ID 0x01). Values are uncalibrated.</summary>
public sealed class G13InputReport
{
    private readonly byte[] _buttons;

    private G13InputReport(byte x, byte y, byte[] buttons)
    {
        X = x;
        Y = y;
        _buttons = buttons;
    }

    public byte X { get; }
    public byte Y { get; }
    public ReadOnlyMemory<byte> ButtonBytes => _buttons;

    public IEnumerable<int> PressedGKeys
    {
        get
        {
            for (int index = 0; index < 22; index++)
                if ((_buttons[index / 8] & (1 << (index % 8))) != 0)
                    yield return index + 1;
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out G13InputReport? report)
    {
        report = null;
        if (data.Length < 8 || data[0] != 0x01)
            return false;
        report = new G13InputReport(data[1], data[2], data.Slice(3, 5).ToArray());
        return true;
    }
}
