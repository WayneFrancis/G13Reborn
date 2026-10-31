using System.Windows.Forms;
using G13Reborn.Device;

namespace G13Reborn.Mapping;

public enum InputEventKind { KeyDown, KeyUp, MouseDown, MouseUp, Wheel }

public enum MouseControl { Left = 1, Right = 2, Middle = 3 }

public readonly record struct InputEvent(InputEventKind Kind, int Code);

/// <summary>
/// Turns full HID button state snapshots into edge-triggered keyboard/mouse
/// events. Shared modifiers and buttons use union semantics. Wheel fires once
/// on the transition from released to pressed. Not thread safe.
/// </summary>
public sealed class ButtonTransitionEngine
{
    private readonly IReadOnlyDictionary<int, ButtonAction> _actions;
    private readonly Dictionary<int, int[]> _parsedChords = new();
    private HashSet<int> _heldKeys = new();
    private HashSet<int> _heldMouse = new();
    private HashSet<int> _previousWheelBits = new();

    public ButtonTransitionEngine(MappingProfile profile)
    {
        profile.Validate();
        _actions = profile.Buttons.ToDictionary(
            p => p.Key, p => new ButtonAction
            {
                Kind = p.Value.Kind, Parameter = p.Value.Parameter
            });
        foreach (var (bit, action) in _actions)
        {
            if (action.Kind == MappingKind.Keyboard)
                _parsedChords[bit] = KeyboardChord.Parse(action.Parameter);
        }
    }

    public IReadOnlyList<InputEvent> Apply(G13InputReport report)
    {
        var keys = new HashSet<int>();
        var mouse = new HashSet<int>();
        var wheelBits = new HashSet<int>();
        var events = new List<InputEvent>();

        foreach (var (bit, action) in _actions)
        {
            if (!report.IsPhysicalBitPressed(bit))
                continue;
            switch (action.Kind)
            {
                case MappingKind.Keyboard:
                    keys.UnionWith(_parsedChords[bit]);
                    break;
                case MappingKind.MouseLeft:
                    mouse.Add((int)MouseControl.Left); break;
                case MappingKind.MouseRight:
                    mouse.Add((int)MouseControl.Right); break;
                case MappingKind.MouseMiddle:
                    mouse.Add((int)MouseControl.Middle); break;
                case MappingKind.WheelUp:
                case MappingKind.WheelDown:
                    wheelBits.Add(bit); break;
            }
        }

        // Key-up main keys first, then modifiers. Key-down modifiers first.
        foreach (int code in _heldKeys.Except(keys)
            .OrderBy(k => KeyboardChord.IsModifier(k) ? 1 : 0).ThenBy(k => k))
            events.Add(new InputEvent(InputEventKind.KeyUp, code));
        foreach (int code in keys.Except(_heldKeys)
            .OrderBy(k => KeyboardChord.IsModifier(k) ? 0 : 1).ThenBy(k => k))
            events.Add(new InputEvent(InputEventKind.KeyDown, code));
        foreach (int button in _heldMouse.Except(mouse).Order())
            events.Add(new InputEvent(InputEventKind.MouseUp, button));
        foreach (int button in mouse.Except(_heldMouse).Order())
            events.Add(new InputEvent(InputEventKind.MouseDown, button));

        foreach (int bit in wheelBits.Except(_previousWheelBits).Order())
        {
            int delta = _actions[bit].Kind == MappingKind.WheelUp ? 120 : -120;
            events.Add(new InputEvent(InputEventKind.Wheel, delta));
        }

        _heldKeys = keys;
        _heldMouse = mouse;
        _previousWheelBits = wheelBits;
        return events;
    }

    public IReadOnlyList<InputEvent> ReleaseAll()
    {
        var result = new List<InputEvent>();
        foreach (int code in _heldKeys
            .OrderBy(k => KeyboardChord.IsModifier(k) ? 1 : 0).ThenBy(k => k))
            result.Add(new InputEvent(InputEventKind.KeyUp, code));
        foreach (int button in _heldMouse.Order())
            result.Add(new InputEvent(InputEventKind.MouseUp, button));
        _heldKeys.Clear();
        _heldMouse.Clear();
        _previousWheelBits.Clear();
        return result;
    }
}

public static class KeyboardChord
{
    /// <summary>Returns one main Windows virtual-key code and zero or more modifiers.</summary>
    public static int[] Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Keyboard mapping needs a key, e.g. Ctrl+F1.");

        var keys = new List<int>();
        int primaryCount = 0;
        foreach (string raw in input.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0)
                throw new ArgumentException($"Invalid keyboard chord '{input}'.");
            string token = raw.ToUpperInvariant();
            int code = token switch
            {
                "CTRL" or "CONTROL" => 0x11,
                "SHIFT" => 0x10,
                "ALT" => 0x12,
                "WIN" or "WINDOWS" => 0x5B,
                "ESC" => 0x1B,
                "RETURN" => 0x0D,
                "PGUP" => 0x21,
                "PGDN" => 0x22,
                "BACKSPACE" => 0x08,
                "SPACE" => 0x20,
                "DEL" => 0x2E,
                "INS" => 0x2D,
                _ => ParseKeyName(token)
            };
            if (!keys.Contains(code))
            {
                keys.Add(code);
                if (!IsModifier(code)) primaryCount++;
            }
        }

        if (primaryCount != 1)
            throw new ArgumentException(
                $"'{input}' must have exactly one non-modifier key.");
        return keys.ToArray();
    }

    private static int ParseKeyName(string key)
    {
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z')
            return key[0];
        if (key.Length == 1 && key[0] is >= '0' and <= '9')
            return key[0];
        if (Enum.TryParse<Keys>(key, true, out var vk) &&
            Enum.IsDefined(vk) &&
            (int)vk is > 0 and <= 255)
            return (int)vk;
        throw new ArgumentException(
            $"Unknown keyboard key '{key}'. Use e.g. F1, A, 5, Space, Enter, Left, or NumPad1.");
    }

    public static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or 0x5B;
}
