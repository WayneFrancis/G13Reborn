using G13Reborn.Device;

namespace G13Reborn.VirtualController;

/// <summary>A physical G13 button bit in report bytes 3-7.</summary>
public sealed record G13Button(int Bit, string Name, int DefaultVJoyButton = 0);

/// <summary>
/// A snapshot of mappings from physical input bits to vJoy button numbers.
/// Zero denotes unassigned. One or more physical buttons can map to the same
/// virtual button; that virtual button remains pressed until all are released.
/// </summary>
public sealed class G13ButtonMappings
{
    public const int MaxVJoyButton = 32;

    public static IReadOnlyList<G13Button> PhysicalButtons { get; } = CreatePhysicalButtons();

    private readonly int[] _buttons;

    public G13ButtonMappings(IReadOnlyDictionary<int, int> assignments)
    {
        _buttons = new int[40];
        foreach (var (bit, virtualButton) in assignments)
        {
            if (!PhysicalButtons.Any(b => b.Bit == bit))
                throw new ArgumentOutOfRangeException(nameof(assignments), $"Unknown physical button bit {bit}.");
            if (virtualButton < 0 || virtualButton > MaxVJoyButton)
                throw new ArgumentOutOfRangeException(nameof(assignments),
                    $"vJoy button must be between 0 and {MaxVJoyButton}.");
            _buttons[bit] = virtualButton;
        }
    }

    public int GetAssignment(int physicalBit) =>
        physicalBit is >= 0 and < 40 ? _buttons[physicalBit] :
            throw new ArgumentOutOfRangeException(nameof(physicalBit));

    public Dictionary<int, int> ToDictionary() =>
        PhysicalButtons.ToDictionary(b => b.Bit, b => _buttons[b.Bit]);

    /// <summary>Return the first 32 virtual button states as a bit mask.</summary>
    public uint GetPressedMask(G13InputReport input, int availableVJoyButtons)
    {
        ArgumentNullException.ThrowIfNull(input);
        uint mask = 0;
        int limit = Math.Clamp(availableVJoyButtons, 0, MaxVJoyButton);
        foreach (var physical in PhysicalButtons)
        {
            int vJoyNumber = _buttons[physical.Bit];
            if (vJoyNumber > 0 && vJoyNumber <= limit && input.IsPhysicalBitPressed(physical.Bit))
                mask |= 1u << (vJoyNumber - 1);
        }
        return mask;
    }

    public static G13ButtonMappings CreateDefault() =>
        new(PhysicalButtons.ToDictionary(b => b.Bit, b => b.DefaultVJoyButton));

    private static IReadOnlyList<G13Button> CreatePhysicalButtons()
    {
        var buttons = new List<G13Button>();
        for (int i = 1; i <= 22; i++)
            buttons.Add(new G13Button(i - 1, $"G{i}", i));

        // Two bits between G22 and BD are currently left unknown/unassigned.
        string[] auxiliary =
        {
            "BD", "L1", "L2", "L3", "L4", "M1", "M2", "M3",
            "MR", "Thumb LEFT", "Thumb RIGHT", "Stick press"
        };
        for (int i = 0; i < auxiliary.Length; i++)
        {
            int assigned = i switch { 9 => 23, 10 => 24, 11 => 25, _ => 0 };
            buttons.Add(new G13Button(24 + i, auxiliary[i], assigned));
        }
        return buttons.AsReadOnly();
    }
}
