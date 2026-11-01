namespace G13Reborn.Mapping;

/// <summary>
/// Parameter choices for the per-row G13 mapping editor. Macro assignments
/// are references by name; they must be selected from the currently edited
/// profile, not entered as arbitrary key-chord text.
/// </summary>
public static class MappingParameterChoices
{
    public static string[] MacroNames(IEnumerable<MacroDefinition> macros)
    {
        ArgumentNullException.ThrowIfNull(macros);
        return macros.Select(m => m.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Retain a value only if it names a macro in the edited profile.
    /// Return the canonical spelling, including after a case-only rename.
    /// An empty result lets the user deliberately select a macro.
    /// </summary>
    public static string ExistingMacroChoice(
        string? previous, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return names.FirstOrDefault(name =>
            string.Equals(name, previous, StringComparison.OrdinalIgnoreCase))
            ?? "";
    }

    public static string ParameterAfterKindChange(
        MappingKind oldKind, MappingKind newKind, string? previous)
    {
        // Macro names are not key chords or vJoy numbers; neither should
        // old keyboard chords be carried into a macro-name dropdown.
        if ((oldKind == MappingKind.Macro) != (newKind == MappingKind.Macro))
            return "";
        return previous ?? "";
    }
}
