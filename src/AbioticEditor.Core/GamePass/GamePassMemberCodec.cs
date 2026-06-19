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
/// re-serializes them faithfully. On write we strip the same-length header back off. The custom
/// header's data-length field is recomputed by the writer and ignored on read, so the splice is
/// lossless.</para>
/// </summary>
public static class GamePassMemberCodec
{
    public const string CharacterSaveClass = "/Game/Blueprints/Saves/Abiotic_CharacterSave.Abiotic_CharacterSave_C";
    public const string WorldSaveClass = "/Game/Blueprints/Saves/Abiotic_WorldSave.Abiotic_WorldSave_C";
    public const string WorldMetadataSaveClass = "/Game/Blueprints/Saves/Abiotic_WorldMetadataSave.Abiotic_WorldMetadataSave_C";

    /// <summary>True when a member of this save class is a GVAS save the editor understands.</summary>
    public static bool IsEditableSaveClass(string? saveClass) => HeaderFor(saveClass) is not null;

    /// <summary>
    /// Reconstructs a full GVAS save from a headerless member body by prepending the class-matched
    /// header template.
    /// </summary>
    public static byte[] ToGvas(string saveClass, ReadOnlySpan<byte> memberBody)
    {
        var header = HeaderFor(saveClass)
            ?? throw new NotSupportedException($"No GVAS header template for save class '{saveClass}'.");
        var result = new byte[header.Length + memberBody.Length];
        header.CopyTo(result, 0);
        memberBody.CopyTo(result.AsSpan(header.Length));
        return result;
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
