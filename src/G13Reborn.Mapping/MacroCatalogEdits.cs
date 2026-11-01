namespace G13Reborn.Mapping;

/// <summary>
/// Pure macro catalog operations used by the editor and by the profile save
/// boundary. New macros are deep copies; renames are resolved against the
/// *original* name at the start of the editor session, not an intermediate
/// name typed while editing.
/// </summary>
public static class MacroCatalogEdits
{
    public static MacroDefinition Duplicate(
        MacroDefinition original, IEnumerable<MacroDefinition> existing)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(existing);
        var names = existing.Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int suffix = 1; suffix < 10000; suffix++)
        {
            string ending = suffix == 1 ? " copy" : $" copy {suffix}";
            string prefix = original.Name[..Math.Min(original.Name.Length, 100 - ending.Length)];
            string candidate = prefix.TrimEnd() + ending;
            if (names.Contains(candidate)) continue;
            MacroDefinition copy = original.Clone();
            copy.Name = candidate;
            copy.Validate(allowEmptySteps: true);
            return copy;
        }
        throw new InvalidOperationException("Could not find a unique duplicate macro name.");
    }

    public static IReadOnlyDictionary<string, string> BuildRenames(
        IReadOnlyList<string?> originalNames,
        IReadOnlyList<MacroDefinition> edited)
    {
        ArgumentNullException.ThrowIfNull(originalNames);
        ArgumentNullException.ThrowIfNull(edited);
        if (originalNames.Count != edited.Count)
            throw new ArgumentException("Macro identity list and edited macros differ in length.");

        var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < edited.Count; i++)
        {
            string? before = originalNames[i];
            string after = edited[i].Name;
            if (before is not null &&
                !string.Equals(before, after, StringComparison.Ordinal))
                renames.Add(before, after);
        }
        return renames;
    }

    public static BindingReconciliation ReconcileBindings(
        IReadOnlyDictionary<int, ButtonAction> bindings,
        IReadOnlyCollection<string> originalMacroNames,
        IReadOnlyList<MacroDefinition> newMacros,
        IReadOnlyDictionary<string, string> renames)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(originalMacroNames);
        ArgumentNullException.ThrowIfNull(newMacros);
        ArgumentNullException.ThrowIfNull(renames);

        var originals = originalMacroNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = newMacros.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newBindings = new Dictionary<int, ButtonAction>();
        var cleared = new List<int>();
        var renamed = new List<int>();
        foreach (var (bit, action) in bindings)
        {
            ButtonAction replacement = new()
            {
                Kind = action.Kind,
                Parameter = action.Parameter
            };
            if (action.Kind == MappingKind.Macro)
            {
                if (renames.TryGetValue(action.Parameter, out string? newName))
                {
                    replacement.Parameter = newName;
                    if (!string.Equals(action.Parameter, newName,
                        StringComparison.Ordinal))
                        renamed.Add(bit);
                }
                else if (originals.Contains(action.Parameter) &&
                         !current.Contains(action.Parameter))
                {
                    // Deleting a macro must not leave a broken reference that
                    // prevents the profile from validating on its next save.
                    replacement.Kind = MappingKind.None;
                    replacement.Parameter = "";
                    cleared.Add(bit);
                }
            }
            newBindings.Add(bit, replacement);
        }
        return new BindingReconciliation(newBindings, renamed, cleared);
    }
}

public sealed record BindingReconciliation(
    Dictionary<int, ButtonAction> Buttons,
    IReadOnlyList<int> RenamedBits,
    IReadOnlyList<int> ClearedBits);
