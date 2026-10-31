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

    private static readonly string[] AuxiliaryNames =
    {
        // Report byte 6: BD, L1-L4, M1-M3
        "BD", "L1", "L2", "L3", "L4", "M1", "M2", "M3",
        // Report byte 7: MR, LEFT, RIGHT, STICK, remaining light-control bits
        "MR", "Thumb LEFT", "Thumb RIGHT", "Stick press",
        "Light bit 4", "Light bit 5", "Light bit 6", "Light bit 7"
    };

    public IEnumerable<string> PressedAuxiliaryKeys
    {
        get
        {
            for (int bit = 0; bit < AuxiliaryNames.Length; bit++)
            {
                if ((_buttons[3 + bit / 8] & (1 << (bit % 8))) != 0)
                    yield return AuxiliaryNames[bit];
            }
        }
    }

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
