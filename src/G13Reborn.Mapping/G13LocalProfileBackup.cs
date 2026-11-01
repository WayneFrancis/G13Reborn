using System.IO.Compression;
using System.Text.Json;

namespace G13Reborn.Mapping;

/// <summary>
/// Creates an explicitly requested backup of HOST-SIDE G13Reborn and LGS
/// profile files. This never accesses the G13's onboard profile memory.
/// </summary>
public static class G13LocalProfileBackup
{
    public const string BackupScope =
        "HOST FILE BACKUP ONLY: includes local G13Reborn and/or " +
        "Logitech Gaming Software profile files. " +
        "Does NOT contain G13 firmware or onboard-memory profile slots.";

    public static string DefaultLgsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Logitech", "Logitech Gaming Software", "Profiles");

    public sealed record Result(int FileCount, long OriginalBytes, string ArchivePath);

    /// <param name="destination">New zip file chosen by the user.</param>
    /// <param name="g13File">Optional override for test fixtures.</param>
    /// <param name="lgsDirectory">Optional override for test fixtures.</param>
    public static Result Create(
        string destination, string? g13File = null, string? lgsDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        string output = Path.GetFullPath(destination);
        g13File ??= ProfileStore.FilePath;
        lgsDirectory ??= DefaultLgsDirectory;

        var files = new List<(string Source, string ArchiveName, long Size)>();
        AddIfAvailable(g13File, "G13Reborn/profiles.json");
        AddIfAvailable(g13File + ".bak", "G13Reborn/profiles.json.bak");

        if (Directory.Exists(lgsDirectory))
        {
            // Never follow links/junctions that could point outside the
            // source profile folder.
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true
            };
            foreach (string source in Directory.EnumerateFiles(
                         lgsDirectory, "*", options))
            {
                string relative = Path.GetRelativePath(lgsDirectory, source);
                if (relative.StartsWith("..", StringComparison.Ordinal) ||
                    Path.IsPathRooted(relative))
                    continue;
                AddIfAvailable(source,
                    "Logitech Gaming Software/Profiles/" +
                    relative.Replace('\\', '/'));
                if (files.Count > 2000)
                    throw new IOException("More than 2,000 local profile files; backup stopped to avoid excessive copying.");
            }
        }

        if (files.Count == 0)
            throw new FileNotFoundException(
                "No G13Reborn or LGS profile files were found on this computer.");

        long totalBytes = files.Sum(x => x.Size);
        if (totalBytes > 256L * 1024 * 1024)
            throw new IOException("Local profile files exceed the 256 MB safety limit.");

        string? targetFolder = Path.GetDirectoryName(output);
        if (!string.IsNullOrWhiteSpace(targetFolder))
            Directory.CreateDirectory(targetFolder);

        // Build to a separate temporary file, then move into place only
        // after every entry has been copied successfully.
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    ZipArchiveEntry entry = zip.CreateEntry(
                        file.ArchiveName, CompressionLevel.Optimal);
                    using var from = new FileStream(
                        file.Source, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using Stream to = entry.Open();
                    from.CopyTo(to);
                }

                ZipArchiveEntry manifest = zip.CreateEntry(
                    "backup-manifest.json", CompressionLevel.Optimal);
                using var writer = new StreamWriter(manifest.Open());
                writer.Write(JsonSerializer.Serialize(new
                {
                    SchemaVersion = 1,
                    CapturedUtc = DateTimeOffset.UtcNow,
                    Scope = BackupScope,
                    FileCount = files.Count,
                    OriginalBytes = totalBytes,
                    Entries = files.Select(x => x.ArchiveName).ToArray()
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            File.Move(temporary, output, overwrite: true);
            return new Result(files.Count, totalBytes, output);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        void AddIfAvailable(string source, string archiveName)
        {
            if (!File.Exists(source) ||
                string.Equals(Path.GetFullPath(source), output,
                    StringComparison.OrdinalIgnoreCase))
                return;
            var info = new FileInfo(source);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                return;
            if (info.Length > 64L * 1024 * 1024)
                throw new IOException($"Local profile file is larger than 64 MB: {info.Name}");
            files.Add((source, archiveName, info.Length));
        }
    }
}
