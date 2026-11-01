using G13Reborn.Device;

namespace G13Reborn.Mapping;

/// <summary>
/// Mode keys are bits 29, 30, 31 (M1/M2/M3) in the 40-button G13 HID
/// snapshot. These become reserved selector inputs ONLY when layers
/// are enabled. Rising edges switch layer; releasing does not.
/// </summary>
public static class G13MappingLayers
{
    public const int M1Bit = 29;
    public const int M2Bit = 30;
    public const int M3Bit = 31;
    public static readonly int[] SelectorBits = { M1Bit, M2Bit, M3Bit };

    public static G13InputReport WithoutModeKeys(G13InputReport report) =>
        report.WithoutButtons(SelectorBits);
}

public sealed class G13LayerSelector
{
    private int _previousMask;

    public int? Detect(G13InputReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        int mask = (report.IsPhysicalBitPressed(G13MappingLayers.M1Bit) ? 1 : 0) |
                   (report.IsPhysicalBitPressed(G13MappingLayers.M2Bit) ? 2 : 0) |
                   (report.IsPhysicalBitPressed(G13MappingLayers.M3Bit) ? 4 : 0);
        int newlyPressed = mask & ~_previousMask;
        _previousMask = mask;
        // Prefer the last physical selector if simultaneous edges occur.
        if ((newlyPressed & 4) != 0) return 3;
        if ((newlyPressed & 2) != 0) return 2;
        if ((newlyPressed & 1) != 0) return 1;
        return null;
    }

    public void Reset() => _previousMask = 0;
}
