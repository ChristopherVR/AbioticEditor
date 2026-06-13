using System.Text;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Rewrites bed-claim owner ids inside world saves when a player's SteamID changes.
/// Claims are stored as <c>&lt;steamid64&gt;}|!|{&lt;name&gt;</c> in deployable
/// <c>CustomTextDisplay_</c> strings. Both ids are 17 digits, so the patch is an
/// in-place, same-length byte replacement: every other byte of the file stays
/// identical, which keeps the round-trip guarantee without re-serializing the save.
/// Strings appear as ASCII or UTF-16LE depending on the claimer's name; both
/// encodings are scanned.
/// </summary>
public static class WorldSteamIdPatcher
{
    /// <summary>
    /// Replaces every claim by <paramref name="oldId"/> with <paramref name="newId"/> in
    /// <paramref name="path"/>. Returns the number of claims rewritten; the file is
    /// untouched (and no .bak written) when there are none.
    /// </summary>
    public static int PatchFile(string path, ulong oldId, ulong newId)
    {
        var oldText = oldId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                      + WorldDeployable.ClaimSeparator;
        var newText = newId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                      + WorldDeployable.ClaimSeparator;
        if (oldText.Length != newText.Length)
        {
            throw new ArgumentException(
                "Old and new SteamID64 must have the same digit count for an in-place patch.");
        }

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

    /// <summary>
    /// Patches every <c>WorldSave_*.sav</c> directly in <paramref name="folder"/>
    /// (backup generations are left alone). Returns total claims rewritten.
    /// </summary>
    public static int PatchFolder(string folder, ulong oldId, ulong newId)
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
