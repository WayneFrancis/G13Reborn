using System.Text.Json;
using System.Text.Json.Serialization;
using G13Reborn.Device;

namespace G13Reborn.Mapping;

public enum MappingKind
{
    None,
    Keyboard,
    MouseLeft,
    MouseRight,
    MouseMiddle,
    WheelUp,
    WheelDown,
    VJoyButton,
    Macro,
    MouseXButton1,
    MouseXButton2
}

public sealed class ButtonAction
{
    public MappingKind Kind { get; set; }
    /// <summary>For Keyboard: e.g. Ctrl+Shift+F1. For VJoyButton: 1..32.</summary>
    public string Parameter { get; set; } = "";
}

public sealed class MappingProfile
{
    public string Name { get; set; } = "Default";
    public Dictionary<int, ButtonAction> Buttons { get; set; } = new();
    /// <summary>Executable file names, e.g. notepad.exe, that activate this profile.</summary>
    public List<string> ExecutableNames { get; set; } = new();
    /// <summary>Macros are owned by their profile and saved with it.</summary>
    public List<MacroDefinition> Macros { get; set; } = new();
    public int DeadzonePercent { get; set; } = 10;
    public bool InvertY { get; set; }

    public static MappingProfile CreateDefault(string name = "Default")
    {
        var profile = new MappingProfile { Name = name };
        // Initially map physical keys to vJoy, never keyboard/mouse,
        // to avoid unexpected input or duplicate LGS keystrokes.
        for (int bit = 0; bit < 22; bit++)
            profile.Buttons[bit] = new ButtonAction
            {
                Kind = MappingKind.VJoyButton,
                Parameter = (bit + 1).ToString()
            };
        for (int bit = 33; bit <= 35; bit++)
            profile.Buttons[bit] = new ButtonAction
            {
                Kind = MappingKind.VJoyButton,
                Parameter = (bit - 10).ToString()
            };
        return profile;
    }

    public MappingProfile Clone(string name)
    {
        return new MappingProfile
        {
            Name = name,
            DeadzonePercent = DeadzonePercent,
            InvertY = InvertY,
            ExecutableNames = new List<string>(ExecutableNames),
            Macros = Macros.Select(m => m.Clone()).ToList(),
            Buttons = Buttons.ToDictionary(
                entry => entry.Key,
                entry => new ButtonAction
                {
                    Kind = entry.Value.Kind,
                    Parameter = entry.Value.Parameter
                })
        };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Profile name is required.");
        if (DeadzonePercent is < 0 or > 30)
            throw new ArgumentException("Deadzone must be between 0 and 30%.");
        if (ExecutableNames is null)
            throw new ArgumentException("Executable associations cannot be null.");
        if (Macros is null)
            throw new ArgumentException("Macro definitions cannot be null.");
        if (Macros.Select(m => m.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != Macros.Count)
            throw new ArgumentException($"Profile '{Name}' has duplicate macro names.");
        foreach (var macro in Macros)
            macro.Validate();
        var normalizedNames = ExecutableNames
            .Select(ExecutableProfileMatcher.NormalizeExecutableName).ToList();
        if (normalizedNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            normalizedNames.Count)
            throw new ArgumentException($"Profile '{Name}' contains duplicate executable names.");
        foreach (var (bit, action) in Buttons)
        {
            if (bit is < 0 or >= 40)
                throw new ArgumentException($"Invalid physical input bit {bit}.");
            if (action is null)
                throw new ArgumentException($"Button {bit} has no action.");
            if (action.Kind == MappingKind.Keyboard)
                KeyboardChord.Parse(action.Parameter);
            if (action.Kind == MappingKind.Macro &&
                !Macros.Any(m => string.Equals(m.Name, action.Parameter,
                    StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(
                    $"Physical bit {bit}: macro '{action.Parameter}' was not found in profile '{Name}'.");
            if (action.Kind == MappingKind.VJoyButton &&
                (!int.TryParse(action.Parameter, out int n) || n is < 1 or > 32))
                throw new ArgumentException(
                    $"Physical bit {bit}: vJoy button must be 1-32.");
        }
    }

    public uint GetVirtualButtons(G13InputReport report, int availableButtons)
    {
        uint pressed = 0;
        int limit = Math.Min(32, Math.Max(0, availableButtons));
        foreach (var (bit, action) in Buttons)
        {
            if (action.Kind != MappingKind.VJoyButton || !report.IsPhysicalBitPressed(bit))
                continue;
            if (int.TryParse(action.Parameter, out int number) && number is >= 1 and <= 32 &&
                number <= limit)
                pressed |= 1u << (number - 1);
        }
        return pressed;
    }
}

public sealed class ProfilesDocument
{
    public string SelectedProfile { get; set; } = "Default";
    public bool AutoSwitchEnabled { get; set; }
    public List<MappingProfile> Profiles { get; set; } = new() { MappingProfile.CreateDefault() };
}

public static class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "G13Reborn", "profiles.json");

    public static ProfilesDocument Load()
    {
        if (!File.Exists(FilePath))
            return new ProfilesDocument();
        var result = JsonSerializer.Deserialize<ProfilesDocument>(
            File.ReadAllText(FilePath), JsonOptions) ??
            throw new InvalidDataException("The profile file was empty.");
        Validate(result);
        return result;
    }

    public static void Save(ProfilesDocument document)
    {
        Validate(document);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(document, JsonOptions));
            if (File.Exists(FilePath))
                File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static void Validate(ProfilesDocument document)
    {
        if (document.Profiles.Count == 0)
            throw new ArgumentException("At least one profile is required.");
        if (document.Profiles.Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.Profiles.Count)
            throw new ArgumentException("Profile names must be unique.");
        foreach (var profile in document.Profiles)
            profile.Validate();
        var assigned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in document.Profiles)
        foreach (string executable in profile.ExecutableNames)
        {
            string name = ExecutableProfileMatcher.NormalizeExecutableName(executable);
            if (assigned.TryGetValue(name, out string? other))
                throw new ArgumentException(
                    $"Executable '{name}' is assigned to both '{other}' and '{profile.Name}'.");
            assigned.Add(name, profile.Name);
        }
        if (!document.Profiles.Any(p => string.Equals(
            p.Name, document.SelectedProfile, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Selected profile does not exist.");
    }
}
