namespace G13Reborn.Mapping;

/// <summary>Editor parameter kinds for macro sequence steps.</summary>
public enum MacroStepParameterType { None, Keyboard, MouseButton }

/// <summary>
/// Keeps the editor's parameter column consistent with the action.
/// This does not inject input or modify stored profiles.
/// </summary>
public static class MacroStepParameterChoices
{
    public static IReadOnlyList<string> MouseButtons { get; } =
        Array.AsReadOnly(new[] { "Left", "Right", "Middle", "XButton1", "XButton2" });

    public static MacroStepParameterType TypeFor(MacroStepKind kind) => kind switch
    {
        MacroStepKind.KeyDown or MacroStepKind.KeyUp or MacroStepKind.KeyPress =>
            MacroStepParameterType.Keyboard,
        MacroStepKind.MouseDown or MacroStepKind.MouseUp or
        MacroStepKind.MouseClick or MacroStepKind.MouseDoubleClick =>
            MacroStepParameterType.MouseButton,
        MacroStepKind.Delay or MacroStepKind.WheelUp or MacroStepKind.WheelDown =>
            MacroStepParameterType.None,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>
    /// Preserve parameters when changing between actions of the same type;
    /// clear incompatible keyboard/mouse values on other transitions.
    /// Existing valid mouse values are canonicalized for the dropdown.
    /// An empty mouse value requires explicit selection before saving.
    /// </summary>
    public static string ParameterFor(
        MacroStepKind newKind, string? previousParameter,
        MacroStepKind? previousKind = null)
    {
        var newType = TypeFor(newKind);
        if (newType == MacroStepParameterType.None)
            return "";

        if (previousKind.HasValue && TypeFor(previousKind.Value) != newType)
            return "";

        if (newType == MacroStepParameterType.Keyboard)
            return previousParameter ?? "";

        return MouseButtons.FirstOrDefault(button =>
            string.Equals(button, previousParameter?.Trim(),
                StringComparison.OrdinalIgnoreCase)) ?? "";
    }
}
