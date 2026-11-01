namespace G13Reborn.Device.Hid;

/// <summary>
/// Verified G13 report layouts. These methods only prepare bytes: they
/// never open a device or send output without explicit user action.
/// </summary>
public static class G13HardwareReports
{
    public const int FeatureLength = 5;
    public const int LcdWidth = 160;
    public const int LcdHeight = 43;
    public const int LcdReportLength = 992;
    public const byte LcdReportId = 0x03;
    public const byte RgbReportId = 0x07;
    public const byte MacroLedReportId = 0x05;

    public static byte[] Rgb(byte red, byte green, byte blue) =>
        new byte[] { RgbReportId, red, green, blue, 0 };

    /// <summary>M1 = bit 0, M2 = bit 1, M3 = bit 2, MR = bit 3.</summary>
    public static byte[] MacroLeds(bool m1, bool m2, bool m3, bool mr) =>
        new byte[]
        {
            MacroLedReportId,
            (byte)((m1 ? 1 : 0) | (m2 ? 2 : 0) |
                   (m3 ? 4 : 0) | (mr ? 8 : 0)),
            0, 0, 0
        };

    /// <summary>
    /// LCD uses six 8-pixel-tall bands. A set bit means a dark/active
    /// pixel. Byte 0 is 3, bytes 1..31 are 0, pixels start at offset 32.
    /// </summary>
    public static byte[] LcdFrame(Func<int, int, bool> pixelIsDark)
    {
        ArgumentNullException.ThrowIfNull(pixelIsDark);
        var report = new byte[LcdReportLength];
        report[0] = LcdReportId;
        for (int y = 0; y < LcdHeight; y++)
        for (int x = 0; x < LcdWidth; x++)
        {
            if (pixelIsDark(x, y))
                report[32 + (y / 8) * LcdWidth + x] |=
                    (byte)(1 << (y % 8));
        }
        return report;
    }

    public static byte[] BlankLcd() => LcdFrame((_, _) => false);

    /// <summary>
    /// Safe diagnostic image: border, center crosshair and several
    /// vertical/horizontal markers. No continuous output loop.
    /// </summary>
    public static byte[] TestPattern() => LcdFrame((x, y) =>
        x is 0 or (LcdWidth - 1) ||
        y is 0 or (LcdHeight - 1) ||
        (x == LcdWidth / 2 && y >= 8 && y < LcdHeight - 8) ||
        (y == LcdHeight / 2 && x >= 12 && x < LcdWidth - 12) ||
        (x is 20 or 40 or 60 or 100 or 120 or 140 && y >= 5 && y <= 9));
}
