namespace G13Reborn.Device.Hid;

/// <summary>K8850 (514C:8850) interrupt OUT protocol. USB packets are 64 bytes,
/// including report ID 03. See github.com/kriomant/ch57x-keyboard-tool,
/// src/keyboard/k8850_4x4.rs and mod.rs.</summary>
public static class K8850Protocol
{
    public const int ReportLength = 64;
    public const ushort VendorId = 0x514c, ProductId = 0x8850;
    public enum KnobAction { Counterclockwise, Press, Clockwise }

    public static byte Button(int index) => index is >= 1 and <= 16
        ? (byte)index : throw new ArgumentOutOfRangeException(nameof(index));

    public static byte Knob(int index, KnobAction action)
    {
        if (index is < 1 or > 3 || (int)action is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(index));
        return (byte)(17 + 3 * (index - 1) + (int)action);
    }

    private static byte[] Header(int slot, int layer, byte kind)
    {
        if (slot is < 1 or > 25) throw new ArgumentOutOfRangeException(nameof(slot));
        if (layer is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(layer));
        var r = new byte[ReportLength];
        r[0] = 3; r[1] = 0xfd; r[2] = (byte)slot;
        r[3] = (byte)layer; r[4] = kind;
        return r;
    }

    // USB keyboard usage codes; F1-F8 hexadecimal are protocol modifier tokens.
    public static byte[] Keyboard(int slot, int layer, params byte[] tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Length is < 1 or > 18) throw new ArgumentOutOfRangeException(nameof(tokens));
        var r = Header(slot, layer, 1);
        r[6] = (byte)tokens.Length;
        for (int i = 0; i < tokens.Length; i++) r[9 + 3*i] = tokens[i];
        return r;
    }

    public static byte[] Media(int slot, int layer, ushort usage)
    {
        var r = Header(slot, layer, 2);
        r[6] = 2; r[9] = (byte)usage; r[12] = (byte)(usage >> 8);
        return r;
    }

    public static byte[] Mouse(int slot, int layer, byte buttons = 0,
        sbyte dx = 0, sbyte dy = 0, sbyte wheel = 0, byte modifier = 0)
    {
        if (buttons > 7 || (modifier != 0 && modifier is < 0xf1 or > 0xf3))
            throw new ArgumentOutOfRangeException(nameof(buttons));
        if ((buttons != 0 ? 1 : 0) + (dx != 0 || dy != 0 ? 1 : 0) + (wheel != 0 ? 1 : 0) != 1)
            throw new ArgumentException("Specify exactly one mouse action.");
        var r = Header(slot, layer, 3);
        r[5] = 1; r[6] = 4; r[9] = modifier; r[12] = buttons;
        r[15] = unchecked((byte)dx); r[18] = unchecked((byte)dy);
        r[21] = unchecked((byte)wheel);
        return r;
    }

    public static byte[] Finish()
    {
        var r = new byte[ReportLength];
        r[0] = 3; r[1] = 0xfd; r[2] = 0xfe; r[3] = 0xff;
        return r;
    }
}
