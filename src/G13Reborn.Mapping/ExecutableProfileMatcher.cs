namespace G13Reborn.Mapping;

/// <summary>
/// Pure case-insensitive executable-to-profile matcher. Does not activate
/// profiles by itself; the Windows host supplies the foreground process name.
/// </summary>
public static class ExecutableProfileMatcher
{
    public static string NormalizeExecutableName(string input)
    {
        string value = (input ?? "").Trim();
        if (value.Length == 0)
            throw new ArgumentException("An executable name cannot be empty.");
        if (!value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            value += ".exe";
        // Only executable file names, never paths, wildcards or arbitrary regex.
        if (value.Length > 255 ||
            value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')) ||
            value.StartsWith('.') || value == ".exe")
            throw new ArgumentException(
                $"Invalid executable name '{input}'. Use a file name such as notepad.exe.");
        return value.ToLowerInvariant();
    }

    public static MappingProfile Resolve(
        ProfilesDocument document, string fallbackName, string? foregroundExecutable,
        bool automatic)
    {
        ArgumentNullException.ThrowIfNull(document);
        MappingProfile fallback = document.Profiles.First(p =>
            string.Equals(p.Name, fallbackName, StringComparison.OrdinalIgnoreCase));
        if (!automatic || string.IsNullOrWhiteSpace(foregroundExecutable))
            return fallback;

        string executable;
        try { executable = NormalizeExecutableName(foregroundExecutable); }
        catch (ArgumentException) { return fallback; }

        return document.Profiles.FirstOrDefault(p => p.ExecutableNames.Any(name =>
            string.Equals(NormalizeExecutableName(name), executable,
                StringComparison.OrdinalIgnoreCase))) ?? fallback;
    }
}

/// <summary>
/// On profile change, suppress already-held physical controls until release.
/// Prevents a key held in one app from triggering a new action in another.
/// </summary>
public sealed class ButtonReleaseGate
{
    private readonly HashSet<int> _heldDuringSwitch = new();

    public void BlockCurrentlyHeld(G13Reborn.Device.G13InputReport? lastReport)
    {
        _heldDuringSwitch.Clear();
        if (lastReport is null) return;
        for (int bit = 0; bit < 40; bit++)
            if (lastReport.IsPhysicalBitPressed(bit))
                _heldDuringSwitch.Add(bit);
    }

    public G13Reborn.Device.G13InputReport Filter(G13Reborn.Device.G13InputReport input)
    {
        if (_heldDuringSwitch.Count == 0) return input;

        foreach (int bit in _heldDuringSwitch.ToArray())
            if (!input.IsPhysicalBitPressed(bit))
                _heldDuringSwitch.Remove(bit);

        return _heldDuringSwitch.Count == 0
            ? input
            : input.WithoutButtons(_heldDuringSwitch);
    }

    public void Clear() => _heldDuringSwitch.Clear();
}
