using System.Text.Json;
using G13Reborn.VirtualController;

namespace G13Reborn.Diagnostics;

internal static class ButtonMappingStore
{
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "G13Reborn", "button-mappings.json");

    public static G13ButtonMappings Load()
    {
        if (!File.Exists(FilePath))
            return G13ButtonMappings.CreateDefault();

        var saved = JsonSerializer.Deserialize<Dictionary<int, int>>(File.ReadAllText(FilePath))
            ?? throw new InvalidDataException("Button mappings JSON was empty.");

        return new G13ButtonMappings(saved);
    }

    public static void Save(G13ButtonMappings mappings)
    {
        string directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        string temp = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(
                mappings.ToDictionary(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
