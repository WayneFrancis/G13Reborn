using System.Globalization;
using G13Reborn.Device.Hid;

namespace G13Reborn.Config;

internal readonly record struct K8850MouseAction(byte Buttons, sbyte X, sbyte Y, sbyte Wheel, byte Modifier)
{
    public byte[] Encode(int slot, int layer) => K8850Protocol.Mouse(slot, layer, Buttons, X, Y, Wheel, Modifier);

    public string Binding
    {
        get
        {
            string prefix = Modifier switch { 0 => "", 0xf1 => "Ctrl+", 0xf2 => "Shift+", 0xf3 => "Alt+",
                _ => throw new InvalidOperationException("Unsupported mouse modifier.") };
            string action = Buttons != 0 ? Buttons switch {
                1 => "Left Click", 2 => "Right Click", 4 => "Middle Click",
                _ => throw new InvalidOperationException("Unsupported mouse button.")
            } : Wheel != 0 ? FormattableString.Invariant($"Wheel {Wheel}") : FormattableString.Invariant($"Move {X} {Y}");
            if (Modifier == 0 && Buttons != 0) return action;
            if (Modifier == 0 && Wheel is 1 or -1) return Wheel == 1 ? "Wheel Up" : "Wheel Down";
            return "Mouse: " + prefix + action;
        }
    }

    public static bool TryParse(string value, out K8850MouseAction action)
    {
        action = default;
        string text = value.Trim();
        bool explicitMouse = text.StartsWith("Mouse:", StringComparison.OrdinalIgnoreCase);
        if (explicitMouse) text = text[6..].Trim();
        else if (text.ToUpperInvariant() is not ("LEFT CLICK" or "RIGHT CLICK" or "MIDDLE CLICK" or "WHEEL UP" or "WHEEL DOWN"))
            return false;

        byte modifier = 0;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            modifier = parts[0].ToUpperInvariant() switch {
                "CTRL" => 0xf1, "SHIFT" => 0xf2, "ALT" => 0xf3,
                _ => throw new FormatException("Mouse actions support one Ctrl, Shift or Alt modifier.")
            };
            text = parts[1];
        }
        else if (parts.Length != 1) throw new FormatException("Mouse actions support at most one modifier.");

        action = text.ToUpperInvariant() switch {
            "LEFT CLICK" => new(1, 0, 0, 0, modifier), "RIGHT CLICK" => new(2, 0, 0, 0, modifier),
            "MIDDLE CLICK" => new(4, 0, 0, 0, modifier), "WHEEL UP" => new(0, 0, 0, 1, modifier),
            "WHEEL DOWN" => new(0, 0, 0, -1, modifier), _ => ParseNumeric(text, modifier)
        };
        action.Encode(1, 1); // Reject no-op motion/wheel and invalid combinations before storing.
        return true;
    }

    private static K8850MouseAction ParseNumeric(string text, byte modifier)
    {
        var args = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        static sbyte Number(string s) => sbyte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n : throw new FormatException("Mouse movement and wheel values must be integers from -128 to 127.");
        if (args.Length == 3 && args[0].Equals("Move", StringComparison.OrdinalIgnoreCase))
            return new(0, Number(args[1]), Number(args[2]), 0, modifier);
        if (args.Length == 2 && args[0].Equals("Wheel", StringComparison.OrdinalIgnoreCase))
            return new(0, 0, 0, Number(args[1]), modifier);
        throw new FormatException("Unknown mouse action. Use Move X Y, Wheel N or a named click.");
    }
}
