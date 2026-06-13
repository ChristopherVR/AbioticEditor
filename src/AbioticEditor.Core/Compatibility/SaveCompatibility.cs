using AbioticEditor.Core.Compatibility;
using UeSaveGame;

namespace AbioticEditor.Core.Compatibility;

/// <summary>
/// Legacy compatibility surface, kept for existing callers (the editor view models call
/// <see cref="WarningFor"/> when a save is opened). All version knowledge now lives in
/// <see cref="SaveVersionRegistry"/> - bump versions THERE on a new game release. For
/// the full picture (unknown flags/keys/enums, severity), use
/// <see cref="CompatibilityAnalyzer"/> instead.
/// </summary>
public static class SaveCompatibility
{
    /// <summary>Highest validated character-save ABF_SAVE_VERSION (from <see cref="SaveVersionRegistry"/>).</summary>
    public static int KnownGoodCharacterVersion { get; } =
        SaveVersionRegistry.Find(SaveKind.Character)!.MaxKnownVersion!.Value;

    /// <summary>Highest validated world/metadata-save ABF_SAVE_VERSION (from <see cref="SaveVersionRegistry"/>).</summary>
    public static int KnownGoodWorldVersion { get; } =
        SaveVersionRegistry.Find(SaveKind.World)!.MaxKnownVersion!.Value;

    /// <summary>The ABF_SAVE_VERSION header value of a loaded save, or null when the save class is unknown.</summary>
    public static int? GetAbfVersion(SaveGame save) => SaveVersionRegistry.GetAbfVersion(save);

    /// <summary>
    /// The warning to surface for <paramref name="save"/>, or null when it's a known
    /// save class at (or below) the version this editor was built against. This is the
    /// version-only check; unknown-content detection needs <see cref="CompatibilityAnalyzer"/>.
    /// </summary>
    public static string? WarningFor(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var kind = SaveVersionRegistry.KindOf(save);
        var knownGood = SaveVersionRegistry.Find(kind)?.MaxKnownVersion;
        return Compute(
            SaveVersionRegistry.GetAbfVersion(save),
            knownGood,
            save.CustomSaveClass is not null,
            save.SaveClass?.Value);
    }

    /// <summary>
    /// Pure warning computation (separated from <see cref="SaveGame"/> so it can be unit
    /// tested with synthetic versions). Message texts are shared with
    /// <see cref="CompatibilityReport.Warning"/> via <c>CompatibilityMessages</c>.
    /// </summary>
    /// <param name="abfVersion">The save's ABF_SAVE_VERSION, when readable.</param>
    /// <param name="knownGoodVersion">The version this editor was built against for that save kind.</param>
    /// <param name="hasRegisteredSaveClass">Whether the save class mapped to one of our [SaveClassPath] classes.</param>
    /// <param name="saveClassName">The raw save class string from the file, for the unknown-class message.</param>
    public static string? Compute(int? abfVersion, int? knownGoodVersion, bool hasRegisteredSaveClass, string? saveClassName)
    {
        if (!hasRegisteredSaveClass)
        {
            return CompatibilityMessages.UnknownClassWarning(saveClassName);
        }

        if (abfVersion is int v && knownGoodVersion is int known && v > known)
        {
            return CompatibilityMessages.NewerVersionWarning(v, known);
        }

        return null;
    }
}
