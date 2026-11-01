namespace G13Reborn.Mapping;

public enum MacroMode { Text, Steps }

public enum MacroStepKind
{
    Delay,
    KeyDown,
    KeyUp,
    KeyPress,
    MouseDown,
    MouseUp,
    MouseClick,
    MouseDoubleClick,
    WheelUp,
    WheelDown
}

public sealed class MacroStep
{
    public MacroStepKind Kind { get; set; } = MacroStepKind.KeyPress;
    /// <summary>Single keyboard key or left/right/middle mouse button.</summary>
    public string Parameter { get; set; } = "";
    /// <summary>Additional delay AFTER this step, in milliseconds.</summary>
    public int DelayAfterMs { get; set; }

    public MacroStep Clone() => new()
    {
        Kind = Kind, Parameter = Parameter, DelayAfterMs = DelayAfterMs
    };

    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new ArgumentException($"Invalid macro step kind {Kind}.");
        if (DelayAfterMs is < 0 or > 60_000)
            throw new ArgumentException("Step delay must be between 0 and 60000 ms.");
        switch (Kind)
        {
            case MacroStepKind.KeyDown:
            case MacroStepKind.KeyUp:
            case MacroStepKind.KeyPress:
                // One key, including modifiers, not a chord: use explicit steps
                // to hold multiple keys in a sequence.
                ParseKey(Parameter);
                break;
            case MacroStepKind.MouseDown:
            case MacroStepKind.MouseUp:
            case MacroStepKind.MouseClick:
            case MacroStepKind.MouseDoubleClick:
                ParseMouse(Parameter);
                break;
        }
    }

    public static int ParseKey(string parameter)
    {
        string key = (parameter ?? "").Trim();
        if (key.Contains('+'))
            throw new ArgumentException("Macro step keys must be single keys, e.g. Ctrl or F1.");
        string lowered = key.ToUpperInvariant() switch
        {
            "CTRL" => "ControlKey",
            "ALT" => "Menu",
            "SHIFT" => "ShiftKey",
            "WIN" or "WINDOWS" => "LWin",
            _ => key
        };
        // KeyboardChord.Parse requires a non-modifier, so parse a modifier
        // using a dummy F24 main key and discard that dummy.
        if (lowered is "ControlKey" or "Menu" or "ShiftKey" or "LWin")
        {
            string normalized = lowered switch
            {
                "ControlKey" => "Ctrl",
                "Menu" => "Alt",
                "ShiftKey" => "Shift",
                _ => "Win"
            };
            return KeyboardChord.Parse(normalized + "+F24")[0];
        }
        return KeyboardChord.Parse(lowered).Single();
    }

    public static int ParseMouse(string parameter)
    {
        return (parameter ?? "").Trim().ToUpperInvariant() switch
        {
            "LEFT" => (int)MouseControl.Left,
            "RIGHT" => (int)MouseControl.Right,
            "MIDDLE" => (int)MouseControl.Middle,
            _ => throw new ArgumentException(
                "Mouse button must be Left, Right or Middle.")
        };
    }
}

public sealed class MacroDefinition
{
    public string Name { get; set; } = "New macro";
    public MacroMode Mode { get; set; }
    public string Text { get; set; } = "";
    /// <summary>
    /// Delay in milliseconds between text characters.
    /// Zero (the legacy profile-file value) means a safe 25 ms default.
    /// Explicit values 5..100 tune pacing for the focused application.
    /// </summary>
    public int TextDelayMs { get; set; } = 25;
    public List<MacroStep> Steps { get; set; } = new();

    public MacroDefinition Clone() => new()
    {
        Name = Name, Mode = Mode, Text = Text, TextDelayMs = TextDelayMs,
        Steps = Steps.Select(s => s.Clone()).ToList()
    };

    /// <summary>
    /// The macro editor may validate an empty Steps draft immediately before
    /// recording. Saving profiles and executing macros always use strict
    /// validation (allowEmptySteps defaults to false).
    /// </summary>
    public void Validate(bool allowEmptySteps = false)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100)
            throw new ArgumentException("Macro name must contain 1-100 characters.");
        if (!Enum.IsDefined(Mode))
            throw new ArgumentException($"Invalid macro mode {Mode}.");
        if (Text is null || Text.Length > 10_000)
            throw new ArgumentException("Macro text cannot exceed 10000 characters.");
        if (TextDelayMs is < 0 or > 100 ||
            TextDelayMs is > 0 and < 5)
            throw new ArgumentException(
                "Text delay must be 5–100 ms (0 in older profiles uses 25 ms).");
        if (Steps is null || Steps.Count > 200)
            throw new ArgumentException("Macro steps cannot exceed 200.");
        if (Mode == MacroMode.Text)
        {
            if (Text.Length == 0)
                throw new ArgumentException($"Text macro '{Name}' is empty.");
        }
        else
        {
            if (Steps.Count == 0 && !allowEmptySteps)
                throw new ArgumentException($"Sequence macro '{Name}' needs steps.");
            foreach (MacroStep step in Steps)
                step.Validate();
        }
    }
}

/// <summary>
/// A macro plays at most once per transition from released to pressed.
/// Calls to Reset happen on profile change and must be paired with the
/// held-button release gate so held controls cannot retrigger.
/// </summary>
public sealed class MacroTriggerDetector
{
    private readonly Dictionary<int, string> _macroAssignments;
    private readonly HashSet<int> _previous = new();

    public MacroTriggerDetector(MappingProfile profile)
    {
        profile.Validate();
        _macroAssignments = profile.Buttons
            .Where(x => x.Value.Kind == MappingKind.Macro)
            .ToDictionary(x => x.Key, x => x.Value.Parameter);
    }

    public IReadOnlyList<string> Detect(G13Reborn.Device.G13InputReport report)
    {
        var triggered = new List<string>();
        foreach (var (bit, name) in _macroAssignments)
        {
            bool held = report.IsPhysicalBitPressed(bit);
            if (held && _previous.Add(bit))
                triggered.Add(name);
            else if (!held)
                _previous.Remove(bit);
        }
        return triggered;
    }
}
