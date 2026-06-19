using System.Text;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Rewrites bed-claim owner ids inside world saves when a player's owner id changes.
/// Claims are stored as <c>&lt;ownerId&gt;}|!|{&lt;name&gt;</c> in deployable
/// <c>CustomTextDisplay_</c> strings. When the old and new ids have the same length (always
/// true for two SteamID64s) the patch is an in-place, same-length byte replacement: every
/// other byte of the file stays identical, which keeps the round-trip guarantee without
/// re-serializing the save. Strings appear as ASCII or UTF-16LE depending on the claimer's
/// name; both encodings are scanned. A different-length swap (e.g. a Steam id to a shorter
/// non-Steam token) would shift the surrounding FString length prefixes and offsets, so it
/// is refused rather than corrupting the save.
/// </summary>
public static class WorldSteamIdPatcher
{
    /// <inheritdoc cref="PatchFile(string, string, string)"/>
    public static int PatchFile(string path, ulong oldId, ulong newId)
        => PatchFile(path,
            oldId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            newId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Replaces every claim by <paramref name="oldId"/> with <paramref name="newId"/> in
    /// <paramref name="path"/>. Returns the number of claims rewritten; the file is
    /// untouched (and no .bak written) when there are none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The two ids differ in length, which an
    /// in-place patch cannot do safely (the world save would need a full reserialize).</exception>
    public static int PatchFile(string path, string oldId, string newId)
    {
        if (oldId.Length != newId.Length)
        {
            throw new InvalidOperationException(
                "Bed-claim rewrite across different-length owner ids requires a full world-save "
                + "reserialize, which is not yet supported.");
        }

        var oldText = oldId + WorldDeployable.ClaimSeparator;
        var newText = newId + WorldDeployable.ClaimSeparator;

        var data = File.ReadAllBytes(path);
        var count = ReplaceAll(data, Encoding.ASCII.GetBytes(oldText), Encoding.ASCII.GetBytes(newText))
                  + ReplaceAll(data, Encoding.Unicode.GetBytes(oldText), Encoding.Unicode.GetBytes(newText));
        if (count > 0)
        {
            File.Copy(path, path + ".bak", overwrite: true);
            File.WriteAllBytes(path, data);
            Diagnostics.EditorLog.Info(
                "WorldSave", $"{Path.GetFileName(path)}: rewrote {count} bed claim(s) {oldId} -> {newId}.");
        }
        return count;
    }

    /// <inheritdoc cref="PatchFolder(string, string, string)"/>
    public static int PatchFolder(string folder, ulong oldId, ulong newId)
        => PatchFolder(folder,
            oldId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            newId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Patches every <c>WorldSave_*.sav</c> directly in <paramref name="folder"/>
    /// (backup generations are left alone). Returns total claims rewritten.
    /// </summary>
    public static int PatchFolder(string folder, string oldId, string newId)
    {
        if (!Directory.Exists(folder)) return 0;
        var total = 0;
        foreach (var sav in Directory.EnumerateFiles(folder, "WorldSave_*.sav", SearchOption.TopDirectoryOnly))
        {
            try
            {
                total += PatchFile(sav, oldId, newId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diagnostics.EditorLog.Warn(
                    "WorldSave", $"Could not patch claims in {Path.GetFileName(sav)}: {ex.Message}");
            }
        }
        return total;
    }

    private static int ReplaceAll(byte[] data, byte[] pattern, byte[] replacement)
    {
        var count = 0;
        var span = data.AsSpan();
        var offset = 0;
        while (offset <= data.Length - pattern.Length)
        {
            var idx = span[offset..].IndexOf(pattern);
            if (idx < 0) break;
            replacement.CopyTo(span[(offset + idx)..]);
            count++;
            offset += idx + pattern.Length;
        }
        return count;
    }
}
