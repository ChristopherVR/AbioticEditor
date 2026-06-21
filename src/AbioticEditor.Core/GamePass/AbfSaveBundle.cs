using System.Text;

namespace AbioticEditor.Core.GamePass;

/// <summary>
/// One save inside a Game Pass <c>ABF_SAVE_VERSION</c> bundle: its in-game relative path, its
/// UE save class, a flag (1 for the text <c>SandboxSettings.ini</c> member, else 0) and the raw
/// member bytes (a headerless GVAS body for save members - see <see cref="GamePassMemberCodec"/>).
/// </summary>
public sealed class AbfMember
{
    public required string Path { get; init; }
    public required string SaveClass { get; init; }
    public int Flag { get; init; }
    public required byte[] Body { get; set; }

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

    private AbfSaveBundle(int version, uint field1, uint field2, List<AbfMember> members)
    {
        Version = version;
        Field1 = field1;
        Field2 = field2;
        Members = members;
    }

    public int Version { get; }

    /// <summary>Opaque header fields preserved verbatim across a round-trip.</summary>
    public uint Field1 { get; }
    public uint Field2 { get; }

    public IReadOnlyList<AbfMember> Members { get; }

    /// <summary>Builds a new bundle from members (used when converting a Steam world to Game Pass).
    /// The header fields match what the game writes for a fresh bundle.</summary>
    public static AbfSaveBundle Create(IReadOnlyList<AbfMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        return new AbfSaveBundle(version: 3, field1: 0, field2: 16, members.ToList());
    }

    /// <summary>True when the bytes start with the <c>ABF_SAVE_VERSION</c> marker.</summary>
    public static bool LooksLikeBundle(ReadOnlySpan<byte> data)
        => data.Length > 21 && ReadMarker(data) == Marker;

    private static string? ReadMarker(ReadOnlySpan<byte> data)
    {
        var len = BitConverter.ToInt32(data);
        if (len is < 1 or > 64 || 4 + len > data.Length) return null;
        return Encoding.ASCII.GetString(data.Slice(4, len)).TrimEnd('\0');
    }

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

        return new AbfSaveBundle(version, field1, field2, members);
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

    // UE FString: int32 length (incl. null), then ASCII bytes incl. the trailing null.
    private static string ReadUnrealString(byte[] d, ref int pos)
    {
        var len = ReadInt(d, ref pos);
        if (len == 0) return string.Empty;
        if (len < 0 || pos + len > d.Length)
        {
            throw new InvalidDataException("Corrupt FString in bundle.");
        }
        var s = Encoding.ASCII.GetString(d, pos, len).TrimEnd('\0');
        pos += len;
        return s;
    }

    private static void WriteUnrealString(BinaryWriter w, string s)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        w.Write(bytes.Length + 1);
        w.Write(bytes);
        w.Write((byte)0);
    }
}
