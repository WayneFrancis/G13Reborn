using System.IO.Compression;
using System.Text.Json;

namespace G13Reborn.Config;

internal sealed class MinecraftBindings
{
    private readonly Dictionary<string, List<string>> _actions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _translations = new(StringComparer.Ordinal);
    public int Count { get; private set; }
    public int TranslationCount => _translations.Count;
    public int UnreadableArchives { get; private set; }

    public sealed record ActionEntry(string Id, string Description, string Input, string Signature, int SharedCount);

    public static MinecraftBindings Load(string path)
    {
        var result = Parse(File.ReadLines(path));
        var mods = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "mods");
        if (Directory.Exists(mods))
            result.LoadTranslations(Directory.EnumerateFiles(mods, "*.jar").OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        return result;
    }

    public string Describe(string id) => _translations.GetValueOrDefault(id, id);

    public IEnumerable<ActionEntry> Entries => _actions.SelectMany(pair => pair.Value.Select(id =>
        new ActionEntry(id, Describe(id), pair.Key.TrimEnd(':'), pair.Key, pair.Value.Count)));

    public static string? GetSignature(string binding)
    {
        try { return Signature(K8850Binding.Encode(1, 1, binding)); }
        catch { return null; }
    }

    public void LoadTranslations(IEnumerable<string> archives)
    {
        var wanted = _actions.Values.SelectMany(x => x).ToHashSet(StringComparer.Ordinal);
        foreach (var path in archives)
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("assets/", StringComparison.Ordinal) ||
                        !entry.FullName.EndsWith("/lang/en_us.json", StringComparison.Ordinal) ||
                        entry.Length > 4 * 1024 * 1024) continue;
                    try
                    {
                        using var stream = entry.Open();
                        using var json = JsonDocument.Parse(stream);
                        if (json.RootElement.ValueKind != JsonValueKind.Object) continue;
                        foreach (var property in json.RootElement.EnumerateObject())
                            if (wanted.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.String &&
                                property.Value.GetString() is { } value && !string.IsNullOrWhiteSpace(value))
                                _translations.TryAdd(property.Name, value);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
                    { /* Ignore an unreadable individual language resource. */ }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { UnreadableArchives++; }
        }
    }

    public static MinecraftBindings Parse(IEnumerable<string> lines)
    {
        var result = new MinecraftBindings();
        foreach (string line in lines)
        {
            if (!line.StartsWith("key_", StringComparison.Ordinal)) continue;
            int separator = line.IndexOf(':');
            if (separator <= 4) continue;
            string action = line[4..separator];
            string[] parts = line[(separator + 1)..].Trim().Split(':');
            if (parts.Length > 2) continue;
            string input = parts[0].Trim().ToLowerInvariant();
            if (input.StartsWith("key.scancode.")) input = input[4..];
            if (input == "key.keyboard.unknown" || !(input.StartsWith("key.keyboard.") ||
                input.StartsWith("key.mouse.") || input.StartsWith("scancode."))) continue;
            string modifier = parts.Length == 2 ? parts[1].Trim().ToUpperInvariant() : "";
            if (modifier == "NONE") modifier = "";
            if (modifier is not ("" or "CONTROL" or "SHIFT" or "ALT")) continue;
            string signature = input + ":" + modifier;
            if (!result._actions.TryGetValue(signature, out var actions))
                result._actions[signature] = actions = new();
            if (!actions.Contains(action)) { actions.Add(action); result.Count++; }
        }
        return result;
    }

    public IReadOnlyList<string> Match(string binding)
    {
        string? signature = GetSignature(binding);
        return signature != null && _actions.TryGetValue(signature, out var actions)
            ? actions : Array.Empty<string>();
    }

    public sealed record KeyUsage(string Binding, string Signature, IReadOnlyList<string> ExactActions,
        IReadOnlyList<string> OtherModifierActions, IReadOnlyList<string> ModifierKeyActions);

    public KeyUsage? Inspect(string binding)
    {
        string? signature = GetSignature(binding);
        if (signature == null) return null;
        int separator = signature.LastIndexOf(':');
        string key = signature[..separator];
        var exact = Match(binding);
        var related = _actions.Where(pair => pair.Key.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase) &&
            !pair.Key.Equals(signature, StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value).Distinct(StringComparer.Ordinal).ToArray();

        // Physical modifier presses can also trigger standalone Minecraft actions.
        // Keep sided HID tokens distinct even though NeoForge chord modifiers are not sided.
        var modifierActions = new List<string>();
        var report = K8850Binding.Encode(1, 1, binding);
        if (report[4] == 1 && report[6] == 2)
        {
            string? modifierKey = report[9] switch {
                0xf1 => "key.keyboard.left.control:", 0xf2 => "key.keyboard.left.shift:",
                0xf3 => "key.keyboard.left.alt:", 0xf5 => "key.keyboard.right.control:",
                0xf6 => "key.keyboard.right.shift:", 0xf7 => "key.keyboard.right.alt:", _ => null
            };
            if (modifierKey != null && _actions.TryGetValue(modifierKey, out var actions))
                modifierActions.AddRange(actions);
        }
        return new KeyUsage(binding, signature, exact, related, modifierActions);
    }

    private static string? Signature(byte[] report)
    {
        if (report[4] == 2 && report[12] == 0 && report[9] is 0xb5 or 0xb6)
            return "scancode.256:"; // Observed Minecraft collision, not Windows equivalence.
        if (report[4] == 3 && report[9] == 0 && report[15] == 0 && report[18] == 0 && report[21] == 0)
            return report[12] switch { 1 => "key.mouse.left:", 2 => "key.mouse.right:", 4 => "key.mouse.middle:", _ => null };
        if (report[4] != 1 || report[6] is < 1 or > 2) return null;
        string modifier = "";
        int offset = 9;
        if (report[6] == 2)
        {
            modifier = report[9] switch {
                0xf1 or 0xf5 => "CONTROL", 0xf2 or 0xf6 => "SHIFT", 0xf3 or 0xf7 => "ALT", _ => "?"
            };
            if (modifier == "?") return null;
            offset = 12;
        }
        byte code = report[offset];
        string? key = code switch {
            >= 4 and <= 29 => "key.keyboard." + (char)('a' + code - 4),
            >= 0x1e and <= 0x26 => "key.keyboard." + (code - 0x1e + 1),
            0x27 => "key.keyboard.0",
            >= 0x3a and <= 0x45 => "key.keyboard.f" + (code - 0x3a + 1),
            >= 0x68 and <= 0x73 => "key.keyboard.f" + (code - 0x68 + 13),
            >= 0x87 and <= 0x8c => "scancode." + new[] { 115, 112, 125, 121, 123, 92 }[code - 0x87],
            0x92 => "scancode.120", 0x93 => "scancode.119", 0x94 => "key.keyboard.f24",
            0x28 => "key.keyboard.enter", 0x29 => "key.keyboard.escape",
            0x2a => "key.keyboard.backspace", 0x2b => "key.keyboard.tab", 0x2c => "key.keyboard.space",
            0x49 => "key.keyboard.insert", 0x4a => "key.keyboard.home", 0x4b => "key.keyboard.page.up",
            0x4c => "key.keyboard.delete", 0x4d => "key.keyboard.end", 0x4e => "key.keyboard.page.down",
            0x4f => "key.keyboard.right", 0x50 => "key.keyboard.left", 0x51 => "key.keyboard.down", 0x52 => "key.keyboard.up",
            0x46 => "key.keyboard.print.screen", 0x47 => "key.keyboard.scroll.lock", 0x48 => "key.keyboard.pause",
            0x53 => "key.keyboard.num.lock",
            >= 0x59 and <= 0x61 => "key.keyboard.keypad." + (code - 0x59 + 1),
            0x62 => "key.keyboard.keypad.0",
            0x54 => "key.keyboard.keypad.divide", 0x55 => "key.keyboard.keypad.multiply",
            0x56 => "key.keyboard.keypad.subtract", 0x57 => "key.keyboard.keypad.add",
            0x58 => "key.keyboard.keypad.enter", 0x63 => "key.keyboard.keypad.decimal",
            0x67 => "key.keyboard.keypad.equal", _ => null
        };
        return key == null ? null : key + ":" + modifier;
    }
}
