using System.Text;

namespace AbioticEditor.Core.GamePass;

/// <summary>
/// One save inside a Game Pass <c>ABF_SAVE_VERSION</c> bundle: its in-game relative path, its
/// UE save class, a flag (1 for the text <c>SandboxSettings.ini</c> member, else 0) and the raw
/// member bytes (a headerless GVAS body for save members - see <see cref="GamePassMemberCodec"/>).
/// </summary>
public sealed class AbfMember
{
    /// <summary>The <see cref="Flag"/> value marking the text <c>SandboxSettings.ini</c> member
    /// (every GVAS save member uses 0).</summary>
    public const int IniFlag = 1;

    /// <summary>
    /// The save's path inside the bundle. Settable because a player save can be re-homed to a
    /// different account id, which renames the file - and the bundle's own table of contents is
    /// what the game reads, so it has to be renamed in step or the old name survives the repack.
    /// </summary>
    public required string Path { get; set; }
    public required string SaveClass { get; init; }
    public int Flag { get; init; }
    public required byte[] Body { get; set; }

    /// <summary>True for the text <c>SandboxSettings.ini</c> member rather than a GVAS save.</summary>
    public bool IsIni => Flag == IniFlag;

    /// <summary>The file name component of <see cref="Path"/>, e.g. <c>Player_2533...</c>.</summary>
    public string Name => System.IO.Path.GetFileName(Path.Replace('\\', '/'));
}

/// <summary>
/// Reads and writes the Game Pass <c>ABF_SAVE_VERSION</c> archive - the blob a world container
/// holds. Layout: <c>"ABF_SAVE_VERSION"</c> marker, three header ints, a member count, a
/// table-of-contents (path + uncompressed size + save class + flag per member), then one
/// Oodle-compressed stream that decompresses to every member body concatenated in TOC order.
/// Re-serialization is faithful for everything untouched; the Oodle bytes differ from the game's
/// compressor but decompress to identical bytes, which is what the game reads.
/// </summary>
public sealed class AbfSaveBundle
{
    private const string Marker = "ABF_SAVE_VERSION";
    private const int PreambleVersion = 4;

    private AbfSaveBundle(int version, uint field1, uint field2, List<AbfMember> members)
    {
        Version = version;
        Field1 = field1;
        Field2 = field2;
        Members = members;
    }

    /// <summary>The newest bundle layout the editor knows (the 1.5 game's).</summary>
    public const int LatestVersion = 4;

    public int Version { get; private set; }

    /// <summary>True when the bundle already uses the newest layout. An older (version 3) bundle
    /// keeps its layout through every edit unless <see cref="UpgradeToLatest"/> is called.</summary>
    public bool IsLatestFormat => Version >= LatestVersion;

    private byte[]? _preamble;

    /// <summary>The engine-version block that version 4 bundles put after the version int (the
    /// GVAS-style save/package versions, engine build and custom-version table), kept verbatim so a
    /// re-serialized bundle still carries it. Empty for version 3.</summary>
    public ReadOnlyMemory<byte> EnginePreamble => _preamble;

    /// <summary>Moves an older bundle to the newest layout by stamping it with the current engine
    /// header. The member bodies are untouched. Only worth doing for a world played on the newest
    /// game, because an older game cannot read the newer layout.</summary>
    public void UpgradeToLatest()
    {
        if (IsLatestFormat) return;
        Version = LatestVersion;
        _preamble = AbfLatestEngineHeader.Bytes;
    }

    /// <summary>Opaque header fields preserved verbatim across a round-trip.</summary>
    public uint Field1 { get; }
    public uint Field2 { get; }

    public IReadOnlyList<AbfMember> Members { get; }

    /// <summary>Builds a new bundle from members (used when converting a Steam world to Game Pass).
    /// The header fields match what the game writes for a fresh bundle.</summary>
    public static AbfSaveBundle Create(IReadOnlyList<AbfMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        return new AbfSaveBundle(LatestVersion, field1: 0, field2: 16, members.ToList())
        {
            _preamble = AbfLatestEngineHeader.Bytes,
        };
    }

    /// <summary>True when the bytes start with the <c>ABF_SAVE_VERSION</c> marker.</summary>
    public static bool LooksLikeBundle(ReadOnlySpan<byte> data)
        => data.Length > 21 && ReadMarker(data) == Marker;

    /// <summary>The bundle layout version, read from the header alone (nothing is unpacked), or null
    /// when the bytes are not a bundle.</summary>
    public static int? PeekVersion(ReadOnlySpan<byte> data)
    {
        if (!LooksLikeBundle(data)) return null;
        var versionAt = 4 + BitConverter.ToInt32(data);
        return versionAt + 4 <= data.Length ? BitConverter.ToInt32(data[versionAt..]) : null;
    }

    private static string? ReadMarker(ReadOnlySpan<byte> data)
    {
        var len = BitConverter.ToInt32(data);
        if (len is < 1 or > 64 || 4 + len > data.Length) return null;
        return Encoding.ASCII.GetString(data.Slice(4, len)).TrimEnd('\0');
    }

    /// <summary>The member holding the world's <c>SandboxSettings.ini</c>, or null when the bundle
    /// has none. Identified by its flag rather than its path, because the game records that member
    /// under the absolute path it had on the machine that wrote it.</summary>
    public AbfMember? SandboxSettings
        => Members.FirstOrDefault(m => m.Flag == AbfMember.IniFlag);

    public static AbfSaveBundle Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var pos = 0;
        var marker = ReadUnrealString(data, ref pos);
        if (marker != Marker)
        {
            throw new InvalidDataException($"Not an ABF_SAVE_VERSION bundle (marker '{marker}').");
        }

        var version = ReadInt(data, ref pos);
        byte[]? preamble = null;
        if (version >= PreambleVersion)
        {
            var preambleStart = pos;
            pos = SkipPreamble(data, pos);
            preamble = data[preambleStart..pos];
        }
        var field1 = (uint)ReadInt(data, ref pos);
        var field2 = (uint)ReadInt(data, ref pos);
        var count = ReadInt(data, ref pos);
        if (count is < 0 or > 100_000)
        {
            throw new InvalidDataException($"Implausible bundle member count {count}.");
        }

        var toc = new List<(string Path, int Size, string Class, int Flag)>(count);
        var total = 0L;
        for (var i = 0; i < count; i++)
        {
            var path = ReadUnrealString(data, ref pos);
            var size = ReadInt(data, ref pos);
            var cls = ReadUnrealString(data, ref pos);
            var flag = ReadInt(data, ref pos);
            if (size < 0) throw new InvalidDataException($"Negative member size for '{path}'.");
            toc.Add((path, size, cls, flag));
            total += size;
        }

        var method = ReadInt(data, ref pos);
        var compSize = ReadInt(data, ref pos);
        if (method != 1)
        {
            throw new InvalidDataException($"Unsupported bundle payload method {method} (expected 1=Oodle).");
        }
        if (compSize < 0 || pos + compSize > data.Length)
        {
            throw new InvalidDataException("Bundle compressed payload is truncated.");
        }

        var raw = OodleCodec.Decompress(data.AsSpan(pos, compSize), checked((int)total));

        var members = new List<AbfMember>(count);
        var off = 0;
        foreach (var (path, size, cls, flag) in toc)
        {
            members.Add(new AbfMember
            {
                Path = path,
                SaveClass = cls,
                Flag = flag,
                Body = raw[off..(off + size)],
            });
            off += size;
        }

        return new AbfSaveBundle(version, field1, field2, members) { _preamble = preamble };
    }

    // Version 4 bundles carry an engine-version block between the version int and the size fields:
    // [int PackageVersionUe4][int PackageVersionUe5][u16 major][u16 minor][u16 patch][u32 changelist]
    // [FString branch][int customFormat][int count][count x (16-byte GUID + int version)].
    // The same block sits at offset 8 of a full GVAS header, which is how a member's header is
    // rebuilt to match the bundle it came from (see GamePassMemberCodec). Returns where it ends.
    internal static int SkipPreamble(byte[] d, int pos)
    {
        pos += 4 + 4 + 2 + 2 + 2 + 4;
        if (pos > d.Length) throw new InvalidDataException("Bundle header is truncated.");
        ReadUnrealString(d, ref pos);
        pos += 4;
        if (pos + 4 > d.Length) throw new InvalidDataException("Bundle header is truncated.");
        var entries = ReadInt(d, ref pos);
        if (entries is < 0 or > 10_000 || pos + (long)entries * 20 > d.Length)
        {
            throw new InvalidDataException($"Implausible bundle custom version count {entries}.");
        }
        return pos + entries * 20;
    }

    /// <summary>Serializes the bundle back to a blob: TOC + one Oodle-compressed member stream.</summary>
    public byte[] Serialize()
    {
        // Concatenate member bodies in order; that is exactly what the stream decompresses to.
        var totalBody = Members.Sum(m => (long)m.Body.Length);
        var raw = new byte[checked((int)totalBody)];
        var o = 0;
        foreach (var m in Members)
        {
            Buffer.BlockCopy(m.Body, 0, raw, o, m.Body.Length);
            o += m.Body.Length;
        }
        var compressed = OodleCodec.Compress(raw);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        WriteUnrealString(w, Marker);
        w.Write(Version);
        if (_preamble is not null) w.Write(_preamble);
        // Field1 is the total uncompressed size; the game passes it verbatim to OodleLZ_Decompress
        // as rawLen. It must equal totalBody after edits change member sizes.
        w.Write((uint)totalBody);
        w.Write(Field2);
        w.Write(Members.Count);
        foreach (var m in Members)
        {
            WriteUnrealString(w, m.Path);
            w.Write(m.Body.Length);
            WriteUnrealString(w, m.SaveClass);
            w.Write(m.Flag);
        }
        w.Write(1); // method = Oodle
        w.Write(compressed.Length);
        w.Flush();
        ms.Write(compressed, 0, compressed.Length);
        return ms.ToArray();
    }

    private static int ReadInt(byte[] d, ref int pos)
    {
        var v = BitConverter.ToInt32(d, pos);
        pos += 4;
        return v;
    }

    // UE FString: int32 length (incl. the null terminator), then the characters. A POSITIVE
    // length counts ASCII bytes; a NEGATIVE one counts UTF-16 characters, which is what the
    // engine writes as soon as a string holds anything outside ASCII - an accented or
    // non-Latin world name, for instance, which is otherwise perfectly legal to name a save.
    private static string ReadUnrealString(byte[] d, ref int pos)
    {
        var len = ReadInt(d, ref pos);
        if (len == 0) return string.Empty;

        if (len < 0)
        {
            if (len == int.MinValue) throw new InvalidDataException("Corrupt FString in bundle.");
            var chars = -len;
            var bytes = chars * 2;
            if (pos + bytes > d.Length) throw new InvalidDataException("Corrupt FString in bundle.");
            var wide = Encoding.Unicode.GetString(d, pos, bytes).TrimEnd('\0');
            pos += bytes;
            return wide;
        }

        if (pos + len > d.Length)
        {
            throw new InvalidDataException("Corrupt FString in bundle.");
        }
        var s = Encoding.ASCII.GetString(d, pos, len).TrimEnd('\0');
        pos += len;
        return s;
    }

    private static void WriteUnrealString(BinaryWriter w, string s)
    {
        // Stay byte-identical to the game (and to what we read) for the ASCII case; only reach
        // for UTF-16 when the string genuinely needs it, exactly as the engine does.
        if (IsAscii(s))
        {
            var bytes = Encoding.ASCII.GetBytes(s);
            w.Write(bytes.Length + 1);
            w.Write(bytes);
            w.Write((byte)0);
            return;
        }

        var wide = Encoding.Unicode.GetBytes(s);
        w.Write(-(s.Length + 1));
        w.Write(wide);
        w.Write((ushort)0);
    }

    private static bool IsAscii(string s)
    {
        foreach (var c in s)
        {
            if (c > '\x007f') return false;
        }
        return true;
    }
}
