using System.Text;
using UeSaveGame;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.Saves;

public static class SaveFolderScanner
{
    static SaveFolderScanner()
    {
        AbioticSaveClasses.EnsureLoaded();
    }

    /// <summary>
    /// Enumerate every <c>*.sav</c> file under <paramref name="folder"/> (recursively) and
    /// return a summary for each. Files that fail to parse are still returned with their
    /// <see cref="SaveFileSummary.LoadError"/> populated so the UI can surface them.
    /// <para>
    /// <c>Backups</c> subtrees are skipped: the game keeps rotated copies of the same saves
    /// there, and listing them flooded the file sidebar with stale duplicates (and let the
    /// editor open a backup instead of the live save).
    /// </para>
    /// </summary>
    public static IReadOnlyList<SaveFileSummary> Scan(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return Array.Empty<SaveFileSummary>();
        }

        var results = new List<SaveFileSummary>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.sav", SearchOption.AllDirectories)
                     .Where(p => !IsUnderBackups(p, folder))
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(Probe(path, folder));
        }

        var failures = results.Count(r => r.LoadError is not null);
        Diagnostics.EditorLog.Info("Scan", $"Scanned {folder}: {results.Count} save(s), {failures} failed to probe.");
        return results;
    }

    /// <summary>
    /// True if <paramref name="path"/> lies under a <c>Backups</c> directory somewhere below
    /// (and including) <paramref name="root"/>. Only the portion of the path inside the scanned
    /// folder is inspected, so a scanned folder that itself happens to be named "Backups" still
    /// lists its saves.
    /// </summary>
    private static bool IsUnderBackups(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Equals("Backups", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static SaveFileSummary Probe(string path, string root)
    {
        var size = new FileInfo(path).Length;
        var display = Path.GetRelativePath(root, path);
        try
        {
            // Header-only read: parsing the 13 MB Facility save just to list it takes
            // seconds; the save class lives a few dozen bytes into the file.
            var saveClass = ReadSaveClassFromHeader(path);
            return new SaveFileSummary(
                FullPath: path,
                DisplayName: display,
                SizeBytes: size,
                SaveClass: saveClass,
                PropertyCount: 0,
                LoadError: null);
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("Scan", $"Failed to probe {display}", ex);
            return new SaveFileSummary(
                FullPath: path,
                DisplayName: display,
                SizeBytes: size,
                SaveClass: null,
                PropertyCount: 0,
                LoadError: ex.Message);
        }
    }

    /// <summary>
    /// Header-only probe of the save class AND the ABF_SAVE_VERSION from the custom
    /// save-class header that immediately follows it (see <c>AbioticCharacterSave</c> /
    /// <c>AbioticWorldSave</c> for the two layouts). Version is null for classes we
    /// don't recognize. Same cost profile as <see cref="ReadSaveClassFromHeader"/>:
    /// a few dozen bytes, never the property body.
    /// </summary>
    public static (string? SaveClass, int? AbfVersion) ReadHeaderInfo(string path)
    {
        using var fs = File.OpenRead(path);
        using var reader = new BinaryReader(fs, Encoding.ASCII);

        var saveClass = ReadSaveClassCore(reader);
        int? version = null;
        if (saveClass is not null)
        {
            if (saveClass.Contains("Abiotic_CharacterSave", StringComparison.Ordinal))
            {
                // Character custom header: version (i32) + data length (i32).
                version = reader.ReadInt32();
            }
            else if (saveClass.Contains("Abiotic_WorldSave", StringComparison.Ordinal)
                     || saveClass.Contains("Abiotic_WorldMetadataSave", StringComparison.Ordinal))
            {
                // World custom header: "ABF_SAVE_VERSION" marker + version + id + length.
                if (ReadUnrealString(reader) == "ABF_SAVE_VERSION")
                {
                    version = reader.ReadInt32();
                }
            }
        }
        return (saveClass, version);
    }

    /// <summary>
    /// Reads just the GVAS header up to the save class name:
    /// magic, SaveGameVersion, package version(s), engine version, custom formats.
    /// </summary>
    public static string? ReadSaveClassFromHeader(string path)
    {
        using var fs = File.OpenRead(path);
        using var reader = new BinaryReader(fs, Encoding.ASCII);
        return ReadSaveClassCore(reader);
    }

    private static string? ReadSaveClassCore(BinaryReader reader)
    {
        var fs = reader.BaseStream;

        if (reader.ReadUInt32() != 0x53415647) // "GVAS"
        {
            throw new InvalidDataException("Save game header is missing or invalid.");
        }

        var saveGameVersion = reader.ReadInt32();
        reader.ReadUInt32();                              // UE4 package version
        if (saveGameVersion >= 3) reader.ReadUInt32();    // UE5 package version

        // EngineVersion: major/minor/patch (i16) + build (i32) + build id (FString)
        reader.ReadInt16(); reader.ReadInt16(); reader.ReadInt16();
        reader.ReadInt32();
        SkipUnrealString(reader);

        // CustomFormatData: version (i32) + count (u32) + count × (guid + i32)
        reader.ReadInt32();
        var formatCount = reader.ReadUInt32();
        fs.Seek(formatCount * 20L, SeekOrigin.Current);

        return ReadUnrealString(reader);
    }

    private static void SkipUnrealString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length == 0) return;
        reader.BaseStream.Seek(length < 0 ? -length * 2L : length, SeekOrigin.Current);
    }

    private static string? ReadUnrealString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length == 0) return null;
        if (length < 0)
        {
            var bytes = reader.ReadBytes(-length * 2);
            return Encoding.Unicode.GetString(bytes, 0, bytes.Length - 2);
        }
        var ascii = reader.ReadBytes(length);
        return Encoding.ASCII.GetString(ascii, 0, ascii.Length - 1);
    }
}
