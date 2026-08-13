using System.Text;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Rewrites bed-claim owner ids inside world saves when a player's owner id changes.
/// Claims are stored as <c>&lt;ownerId&gt;}|!|{&lt;name&gt;</c> in deployable
/// <c>CustomTextDisplay_</c> strings, and the editor takes one of two routes depending on
/// whether the ids are the same length.
///
/// <para><b>Same length</b> (always true for two SteamID64s): an in-place, same-length byte
/// replacement. Every other byte of the file stays identical, which keeps the round-trip
/// guarantee without re-serializing the save at all. Strings appear as ASCII or UTF-16LE
/// depending on the claimer's name; both encodings are scanned.</para>
///
/// <para><b>Different length</b> (an Xbox account id is 16 digits against a SteamID64's 17, so
/// every Game Pass conversion lands here): the byte replacement is impossible, because moving
/// the id would shift the FString length prefix in front of it and every offset after it. The
/// save is parsed, the claims are rewritten through the real serializer, and the whole file is
/// written back - which recomputes those prefixes. Everything the rewrite did not touch still
/// round-trips byte-perfect.</para>
///
/// <para>Either way a file with no claim by the old id is left completely alone: no write, no
/// <c>.bak</c>, no re-serialize.</para>
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
    public static int PatchFile(string path, string oldId, string newId)
    {
        var original = File.ReadAllBytes(path);
        var patched = PatchBytes(original, oldId, newId, out var count);
        if (count == 0) return 0;

        // Through the shared backup writer: a re-serialize is a whole-file rewrite, and a
        // failure halfway must leave the previous save intact rather than a truncated one.
        Saves.SaveBackup.WriteWithBackup(path, stream => stream.Write(patched, 0, patched.Length));
        Diagnostics.EditorLog.Info(
            "WorldSave", $"{Path.GetFileName(path)}: rewrote {count} bed claim(s) {oldId} -> {newId}.");
        return count;
    }

    /// <summary>
    /// The in-memory form of <see cref="PatchFile(string, string, string)"/>, for callers that
    /// hold a world save's bytes rather than a file (packing a Game Pass container, above all).
    /// Returns the patched bytes and, via <paramref name="count"/>, how many claims changed;
    /// when nothing matched the input array is handed straight back so the caller can tell that
    /// there is nothing to write.
    /// </summary>
    /// <remarks>The different-length route parses <paramref name="data"/> as a world save, so
    /// only pass a <c>WorldSave_*.sav</c> here. Player saves carry no claims.</remarks>
    public static byte[] PatchBytes(byte[] data, string oldId, string newId, out int count)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrEmpty(oldId);
        ArgumentException.ThrowIfNullOrEmpty(newId);

        count = 0;
        // Re-homing a player to the id they already have is a no-op, not a rewrite of every
        // claim to its own current value.
        if (string.Equals(oldId, newId, StringComparison.Ordinal)) return data;

        return oldId.Length == newId.Length
            ? ReplaceSameLength(data, oldId, newId, out count)
            : ReserializeClaims(data, oldId, newId, out count);
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // One unreadable region must not cost the player the claims in all the others.
                Diagnostics.EditorLog.Warn(
                    "WorldSave", $"Could not patch claims in {Path.GetFileName(sav)}: {ex.Message}");
            }
        }
        return total;
    }

    private static byte[] ReplaceSameLength(byte[] data, string oldId, string newId, out int count)
    {
        var oldText = oldId + WorldDeployable.ClaimSeparator;
        var newText = newId + WorldDeployable.ClaimSeparator;

        var patched = (byte[])data.Clone();
        count = ReplaceAll(patched, Encoding.ASCII.GetBytes(oldText), Encoding.ASCII.GetBytes(newText))
              + ReplaceAll(patched, Encoding.Unicode.GetBytes(oldText), Encoding.Unicode.GetBytes(newText));
        return count == 0 ? data : patched;
    }

    private static byte[] ReserializeClaims(byte[] data, string oldId, string newId, out int count)
    {
        using var input = new MemoryStream(data, writable: false);
        var world = WorldSaveReader.ReadFromStream(input);
        count = WorldSaveWriter.RewriteDeployableClaims(world, oldId, newId);
        // A save with no claim by this player must come back byte-identical, so a world that
        // matched nothing never gets re-serialized at all.
        if (count == 0) return data;

        using var output = new MemoryStream(data.Length);
        world.Raw.WriteTo(output);
        return output.ToArray();
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
