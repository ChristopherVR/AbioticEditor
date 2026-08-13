using System.Buffers.Binary;
using System.Text;

namespace AbioticEditor.Core.GamePass;

/// <summary>
/// Bridges a headerless Game Pass bundle member to a full GVAS save the editor can read, and back.
///
/// <para>A Game Pass member is only the GVAS <i>property body</i> (it begins at the "unknown byte"
/// that follows the save's custom header); the GVAS magic, versions, custom formats, class name and
/// custom header are all stripped, with the save class recorded in the bundle TOC instead. To make
/// the existing readers/writers work we prepend a class-matched header captured from a real save
/// (<see cref="GvasHeaderTemplates"/>); the body bytes are byte-identical, so the editor parses and
/// re-serializes them faithfully. On write we strip the same-length header back off. The one field
/// in that header that describes the body rather than the format - the custom header's data-length -
/// is rewritten for the body actually being spliced on, because the captured template carries the
/// length of the save it came from.</para>
/// </summary>
public static class GamePassMemberCodec
{
    public const string CharacterSaveClass = "/Game/Blueprints/Saves/Abiotic_CharacterSave.Abiotic_CharacterSave_C";
    public const string WorldSaveClass = "/Game/Blueprints/Saves/Abiotic_WorldSave.Abiotic_WorldSave_C";
    public const string WorldMetadataSaveClass = "/Game/Blueprints/Saves/Abiotic_WorldMetadataSave.Abiotic_WorldMetadataSave_C";

    /// <summary>True when a member of this save class is a GVAS save the editor understands.</summary>
    public static bool IsEditableSaveClass(string? saveClass) => HeaderFor(saveClass) is not null;

    /// <summary>
    /// Decodes the bundle's <c>SandboxSettings.ini</c> member to its real text. The game stores
    /// that member with every byte decremented by one (so <c>[SandboxSettings]</c> is written as
    /// <c>ZR`mcanwRdsshmfr\</c>); it is the only member that is not a GVAS body.
    /// </summary>
    public static string DecodeIniText(ReadOnlySpan<byte> memberBody)
    {
        var plain = new byte[memberBody.Length];
        for (var i = 0; i < memberBody.Length; i++) plain[i] = unchecked((byte)(memberBody[i] + 1));
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Re-encodes ini text into the shifted form the bundle stores (inverse of
    /// <see cref="DecodeIniText"/>).</summary>
    public static byte[] EncodeIniText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var plain = Encoding.UTF8.GetBytes(text);
        var body = new byte[plain.Length];
        for (var i = 0; i < plain.Length; i++) body[i] = unchecked((byte)(plain[i] - 1));
        return body;
    }

    /// <summary>
    /// Reconstructs a full GVAS save from a headerless member body by prepending the class-matched
    /// header template and stamping the body's own length into the template's data-length field.
    /// </summary>
    public static byte[] ToGvas(string saveClass, ReadOnlySpan<byte> memberBody)
    {
        var header = HeaderFor(saveClass)
            ?? throw new NotSupportedException($"No GVAS header template for save class '{saveClass}'.");
        var result = new byte[header.Length + memberBody.Length];
        header.CopyTo(result, 0);
        memberBody.CopyTo(result.AsSpan(header.Length));

        // Every template ends with a custom header whose last field counts the bytes that follow it,
        // and the captured template still carries the count from the one save it was taken from. The
        // editor ignores that field on read, so a wrong value is invisible here, but the game checks
        // it: a converted world whose saves misreport their own length is refused as an incompatible
        // world save. Stamp the real length into our copy - never into the shared template array,
        // which every later call reuses.
        BinaryPrimitives.WriteInt32LittleEndian(
            result.AsSpan(DataLengthOffset(saveClass, header), sizeof(int)), memberBody.Length);
        return result;
    }

    /// <summary>
    /// Where the data-length field sits in a header template: the last four bytes of the custom
    /// header, which is itself the last thing in the template. Located from the class name rather
    /// than assumed, so a template regenerated with anything extra on the end is rejected here
    /// instead of silently having four body bytes overwritten.
    /// </summary>
    private static int DataLengthOffset(string saveClass, byte[] header)
    {
        var (marker, customHeaderSize) = ClassMarker(saveClass)
            ?? throw new NotSupportedException($"Unsupported save class '{saveClass}'.");
        var markerBytes = Encoding.ASCII.GetBytes(marker);
        var idx = header.AsSpan().IndexOf(markerBytes);
        var headerEnd = idx < 0 ? -1 : idx + markerBytes.Length + customHeaderSize;
        if (headerEnd != header.Length)
        {
            throw new InvalidDataException(
                $"The GVAS header template for '{saveClass}' does not end with its custom header "
                + $"(expected {header.Length} bytes, the class name ends its header at {headerEnd}).");
        }
        return header.Length - sizeof(int);
    }

    /// <summary>
    /// Strips a full GVAS save down to the headerless member body the bundle stores. The body
    /// begins at the "unknown byte" right after the save's custom header; that boundary is found by
    /// locating the save class name and skipping its fixed-size custom header, so it is correct for
    /// any save of the class (not only ones the editor just wrote with our header template).
    /// </summary>
    public static byte[] ToMemberBody(string saveClass, ReadOnlySpan<byte> gvas)
    {
        var (marker, customHeaderSize) = ClassMarker(saveClass)
            ?? throw new NotSupportedException($"Unsupported save class '{saveClass}'.");
        var markerBytes = Encoding.ASCII.GetBytes(marker);
        var idx = gvas.IndexOf(markerBytes);
        if (idx < 0)
        {
            throw new InvalidDataException($"Save class name '{marker.TrimEnd('\0')}' not found in the GVAS save.");
        }
        var bodyStart = idx + markerBytes.Length + customHeaderSize;
        if (bodyStart > gvas.Length)
        {
            throw new InvalidDataException("GVAS save is truncated before its property body.");
        }
        return gvas[bodyStart..].ToArray();
    }

    // The save class name (with its FString null terminator) followed by the class's custom-header
    // size: CharacterSave = [int Version][int DataLength] = 8; World/Metadata =
    // [FString "ABF_SAVE_VERSION"][int Version][int Id][int DataLength] = 33.
    private static (string Marker, int CustomHeaderSize)? ClassMarker(string? saveClass) => saveClass switch
    {
        CharacterSaveClass => ("Abiotic_CharacterSave_C\0", 8),
        WorldSaveClass => ("Abiotic_WorldSave_C\0", 33),
        WorldMetadataSaveClass => ("Abiotic_WorldMetadataSave_C\0", 33),
        _ => null,
    };

    private static byte[]? HeaderFor(string? saveClass) => saveClass switch
    {
        CharacterSaveClass => GvasHeaderTemplates.CharacterSave,
        WorldSaveClass => GvasHeaderTemplates.WorldSave,
        WorldMetadataSaveClass => GvasHeaderTemplates.WorldMetadataSave,
        _ => null,
    };
}
