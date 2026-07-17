using AbioticEditor.Core.Compatibility;
using UeSaveGame;

namespace AbioticEditor.App.Services;

/// <summary>
/// App-only localized version of <see cref="SaveCompatibility.WarningFor"/> (the warning bar
/// shown when a save is newer than the editor or has an unknown class). Core's message text
/// stays English (the CLI source of truth); this reproduces the same two-branch check from
/// the same public registry inputs with resx-backed text, mirroring the
/// EquipSlotLocalization pattern.
/// </summary>
public static class CompatibilityLocalization
{
    private static LocalizationResourceManager Loc => LocalizationResourceManager.Instance;

    /// <summary>Localized warning for a loaded save, or null when it is fully known.</summary>
    public static string? WarningFor(SaveGame save)
    {
        if (save.CustomSaveClass is null)
        {
            return Loc.Format("Compat_UnknownClassWarning", save.SaveClass?.Value ?? Loc["Compat_NoClass"]);
        }

        var knownGood = SaveVersionRegistry.Find(SaveVersionRegistry.KindOf(save))?.MaxKnownVersion;
        if (SaveVersionRegistry.GetAbfVersion(save) is int seen && knownGood is int known && seen > known)
        {
            return Loc.Format("Compat_NewerVersionWarning", seen, known, SaveVersionRegistry.ValidatedGameBuild);
        }

        return null;
    }
}
