namespace AbioticEditor.Core.WorldSaves;

/// <summary>One world region: its LevelGUID and the save file it came from.</summary>
public sealed record WorldLevel(string LevelGuid, string FileName)
{
    /// <summary>"WorldSave_Facility_Office1" -> "Facility Office1".</summary>
    public string DisplayName
    {
        get
        {
            var name = FileName;
            if (name.StartsWith("WorldSave_", StringComparison.OrdinalIgnoreCase)) name = name[10..];
            return name.Replace('_', ' ');
        }
    }
}

/// <summary>
/// Maps level GUIDs (the player save's <c>LastSafeWorldGUID_</c>) to world save files.
/// Each <c>WorldSave_*.sav</c> carries a top-level <c>LevelGUID</c> StrProperty; rather
/// than fully parsing every save (the Facility one takes seconds) the GUID is found with
/// a raw byte scan - the property name followed by a 32-hex-char run.
/// </summary>
public static class WorldLevelIndex
{
    public static IReadOnlyList<WorldLevel> ScanFolder(string folder)
    {
        var result = new List<WorldLevel>();
        if (!Directory.Exists(folder)) return result;

        foreach (var file in Directory.EnumerateFiles(folder, "WorldSave_*.sav"))
        {
            try
            {
                if (TryReadLevelGuid(file) is { } guid)
                {
                    result.Add(new WorldLevel(guid, Path.GetFileNameWithoutExtension(file)));
                }
            }
            catch
            {
                // Unreadable file just drops out of the index.
            }
        }
        return result;
    }

    // Window a match needs after the needle: the 160-byte value search range plus the
    // 32-hex-char value itself can start at the very end of that range, plus its NUL.
    private const int ValueSearchRange = 160;
    private static readonly byte[] Needle = System.Text.Encoding.ASCII.GetBytes("LevelGUID");
    private static readonly int Overlap = Needle.Length + ValueSearchRange + 1;

    /// <summary>
    /// Raw-scans a GVAS file for its top-level <c>LevelGUID</c> value. Streams the file
    /// in chunks (region saves run 15 MB+ - reading them whole would hit the LOH for
    /// every file in the folder scan).
    /// </summary>
    public static string? TryReadLevelGuid(string path)
    {
        const int ChunkSize = 256 * 1024;
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1);

        var buffer = new byte[ChunkSize];
        var count = 0;
        while (true)
        {
            var read = stream.ReadAtLeast(
                buffer.AsSpan(count), buffer.Length - count, throwOnEndOfStream: false);
            count += read;
            var endOfFile = count < buffer.Length;

            // Matches too close to the chunk end may have a truncated value window;
            // they are carried over and re-evaluated with the next chunk.
            var evalLimit = endOfFile ? count : count - Overlap;
            if (ScanForGuid(buffer, count, evalLimit) is { } guid) return guid;
            if (endOfFile) return null;

            var keep = Math.Min(Overlap, count);
            Buffer.BlockCopy(buffer, count - keep, buffer, 0, keep);
            count = keep;
        }
    }

    /// <summary>
    /// Scans buffer[0..count) for the needle (matches starting before
    /// <paramref name="evalLimit"/>) followed by a 32-hex-char run terminated by NUL.
    /// </summary>
    private static string? ScanForGuid(byte[] buffer, int count, int evalLimit)
    {
        var span = buffer.AsSpan(0, count);
        var searchStart = 0;
        while (searchStart < evalLimit)
        {
            var rel = span[searchStart..].IndexOf(Needle);
            if (rel < 0) break;
            var idx = searchStart + rel;
            if (idx >= evalLimit) break;

            // The value follows within the property header - look for a 32-hex-char run.
            var end = Math.Min(count, idx + Needle.Length + ValueSearchRange);
            var run = 0;
            for (var i = idx + Needle.Length; i < end; i++)
            {
                var c = (char)buffer[i];
                var isHex = c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';
                if (!isHex)
                {
                    run = 0;
                    continue;
                }
                if (++run == 32)
                {
                    // Require a terminator after - property names can contain hex chars,
                    // but the value is exactly 32 chars followed by NUL.
                    if (i + 1 < count && buffer[i + 1] == 0)
                    {
                        return System.Text.Encoding.ASCII.GetString(buffer, i - 31, 32);
                    }
                    run = 0;
                }
            }
            searchStart = idx + 1;
        }
        return null;
    }
}
