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
    /// Strips the class-matched header from a full GVAS save (as produced by the editor's writer)
    /// to recover the headerless member body the bundle stores. The header length is fixed per
    /// class, so the data-length difference in the custom header (recomputed on write) is discarded
    /// with the rest of the header.
    /// </summary>
    public static byte[] ToMemberBody(string saveClass, ReadOnlySpan<byte> gvas)
    {
        var header = HeaderFor(saveClass)
            ?? throw new NotSupportedException($"No GVAS header template for save class '{saveClass}'.");
        if (gvas.Length < header.Length)
        {
            throw new InvalidDataException("Re-serialized save is shorter than the header template.");
        }
        return gvas[header.Length..].ToArray();
    }

    private static byte[]? HeaderFor(string? saveClass) => saveClass switch
    {
        CharacterSaveClass => GvasHeaderTemplates.CharacterSave,
        WorldSaveClass => GvasHeaderTemplates.WorldSave,
        WorldMetadataSaveClass => GvasHeaderTemplates.WorldMetadataSave,
        _ => null,
    };
}
