using AbioticEditor.Core.Saves;
using AbioticEditor.Plugins;

namespace AbioticEditor.Core.Plugins;

/// <summary>
/// Maps a save file to the SDK's <see cref="SaveKind"/> using the same header-only probe
/// the folder scanner uses, so a plugin's <see cref="SaveKind"/> filter agrees with the
/// rest of the editor. Header-only: no property bodies are parsed (cheap, even for the
/// 100+ MB world saves).
/// </summary>
public static class SaveKindDetector
{
    /// <summary>Detects the kind of the save at <paramref name="path"/> from its class header.</summary>
    public static SaveKind Detect(string path)
    {
        string? saveClass;
        try
        {
            saveClass = SaveFolderScanner.ReadSaveClassFromHeader(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            return SaveKind.Any;
        }

        return FromSaveClass(saveClass);
    }

    /// <summary>Maps a save-class string (e.g. from the header) to a <see cref="SaveKind"/>.</summary>
    public static SaveKind FromSaveClass(string? saveClass) => saveClass switch
    {
        var s when s?.Contains("CharacterSave", StringComparison.Ordinal) == true => SaveKind.Player,
        var s when s?.Contains("WorldMetadataSave", StringComparison.Ordinal) == true => SaveKind.Metadata,
        var s when s?.Contains("WorldSave", StringComparison.Ordinal) == true => SaveKind.World,
        var s when s?.Contains("Customization", StringComparison.Ordinal) == true => SaveKind.Customization,
        _ => SaveKind.Any,
    };

    /// <summary>
    /// True when an operation declaring <paramref name="appliesTo"/> may run against a save
    /// of <paramref name="actual"/> kind. <see cref="SaveKind.Any"/> on either side matches.
    /// </summary>
    public static bool Matches(SaveKind appliesTo, SaveKind actual)
        => appliesTo == SaveKind.Any || actual == SaveKind.Any || appliesTo == actual;
}
