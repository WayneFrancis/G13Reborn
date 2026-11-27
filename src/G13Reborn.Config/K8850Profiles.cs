using System.Text.Json;
using G13Reborn.Device.Hid;

namespace G13Reborn.Config;

internal sealed class K8850Profile
{
    public string Name { get; set; } = "Default";
    public Dictionary<string, string> Bindings { get; set; } = new();
}

internal sealed class K8850Profiles
{
    public List<K8850Profile> Profiles { get; set; } = new() { new() };
    public string Active { get; set; } = "Default";
    public Dictionary<string, string> LastSent { get; set; } = new();
    private static string FilePath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "G13Reborn", "k8850-profiles.json");

    public static K8850Profiles Load() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<K8850Profiles>(File.ReadAllText(FilePath))
            ?? throw new InvalidDataException("Invalid K8850 profile file.")
        : new();

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}

internal static class K8850Binding
{
    public static byte[] Encode(int slot, int layer, string value)
    {
        var s = value.Trim();
        if (s.StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
            return K8850Protocol.Keyboard(slot, layer, s[4..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => Convert.ToByte(x, 16)).ToArray());
        var media = s.ToUpperInvariant() switch
        {
            "VOLUME UP" => 0xe9, "VOLUME DOWN" => 0xea, "MUTE" => 0xe2,
            "PLAY/PAUSE" => 0xcd, "NEXT TRACK" => 0xb5, "PREVIOUS TRACK" => 0xb6,
            "STOP MEDIA" => 0xb7, _ => 0
        };
        if (media != 0) return K8850Protocol.Media(slot, layer, (ushort)media);
        switch (s.ToUpperInvariant())
        {
            case "LEFT CLICK": return K8850Protocol.Mouse(slot, layer, buttons: 1);
            case "RIGHT CLICK": return K8850Protocol.Mouse(slot, layer, buttons: 2);
            case "MIDDLE CLICK": return K8850Protocol.Mouse(slot, layer, buttons: 4);
            case "WHEEL UP": return K8850Protocol.Mouse(slot, layer, wheel: 1);
            case "WHEEL DOWN": return K8850Protocol.Mouse(slot, layer, wheel: -1);
        }
        var tokens = new List<byte>();
        foreach (var chord in s.Split(','))
        {
            var parts = chord.Split('+', StringSplitOptions.TrimEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i].ToUpperInvariant();
                byte modifier = p switch { "CTRL" => 0xf1, "SHIFT" => 0xf2, "ALT" => 0xf3,
                    "WIN" => 0xf4, "RCTRL" => 0xf5, "RSHIFT" => 0xf6, "RALT" => 0xf7, "RWIN" => 0xf8, _ => 0 };
                if (i < parts.Length - 1)
                {
                    if (modifier == 0) throw new FormatException("Only modifiers may precede the final key.");
                    tokens.Add(modifier);
                }
                else tokens.Add(Key(p));
            }
        }
        return K8850Protocol.Keyboard(slot, layer, tokens.ToArray());
    }

    private static byte Key(string p)
    {
        if (p.Length == 1 && p[0] is >= 'A' and <= 'Z') return (byte)(4 + p[0] - 'A');
        if (p.Length == 1 && p[0] is >= '1' and <= '9') return (byte)(0x1e + p[0] - '1');
        if (p == "0") return 0x27;
        if (p.StartsWith("F") && int.TryParse(p[1..], out int f) && f is >= 1 and <= 12)
            return (byte)(0x3a + f - 1);
        return p switch { "ENTER" => 0x28, "ESC" or "ESCAPE" => 0x29, "BACKSPACE" => 0x2a,
            "TAB" => 0x2b, "SPACE" => 0x2c, "DELETE" => 0x4c, "HOME" => 0x4a, "END" => 0x4d,
            "PAGEUP" => 0x4b, "PAGEDOWN" => 0x4e, "RIGHT" => 0x4f, "LEFT" => 0x50,
            "DOWN" => 0x51, "UP" => 0x52, _ => throw new FormatException("Unknown key: " + p) };
    }
}
