using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.App.Services;

/// <summary>
/// App-only localized override for <see cref="TraitCatalog"/>'s display names (traits and
/// backgrounds/PhDs). The Core catalog stays English (the CLI source of truth); this maps a
/// known id to a resx-backed translation, mirroring the DoorLocalization pattern.
/// </summary>
public static class TraitLocalization
{
    private static LocalizationResourceManager Loc => LocalizationResourceManager.Instance;

    public static string DisplayNameFor(string id)
        => TraitCatalog.Traits.ContainsKey(id) || TraitCatalog.Backgrounds.ContainsKey(id)
            ? Loc[$"Trait_{id}_DisplayName"]
            : TraitCatalog.DisplayNameFor(id);
}
