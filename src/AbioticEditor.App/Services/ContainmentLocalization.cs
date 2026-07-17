using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.Services;

/// <summary>
/// App-only localized override for <see cref="ContainmentCreatureCatalog"/> (contained-creature
/// display names and lore blurbs). The Core catalog stays English (the CLI source of truth);
/// this maps the stable creature row / lore bucket to a resx-backed translation, mirroring the
/// DoorLocalization pattern. Unknown rows fall back to Core's text.
/// </summary>
public static class ContainmentLocalization
{
    private static LocalizationResourceManager Loc => LocalizationResourceManager.Instance;

    /// <summary>Localized display name for a contained creature row.</summary>
    public static string DisplayName(string creature)
    {
        var native = ContainmentCreatureCatalog.DisplayName(creature);
        // Only known rows have keys; the key id is the normalized row the catalog matched.
        var keyId = creature switch
        {
            "Leyak" => "Leyak",
            "LeyakB" or "Leyak_B" => "LeyakB",
            "Krasue" => "Krasue",
            _ => null,
        };
        return keyId is null ? native : Loc.GetOrNull($"WorldContainment_Creature_{keyId}_DisplayName") ?? native;
    }

    /// <summary>Localized lore blurb, keyed on the same StartsWith buckets as Core.</summary>
    public static string Lore(string creature)
    {
        var bucket = creature.StartsWith("Krasue", StringComparison.OrdinalIgnoreCase) ? "Krasue"
            : creature.StartsWith("Leyak", StringComparison.OrdinalIgnoreCase) ? "Leyak"
            : "Default";
        return Loc.GetOrNull($"WorldContainment_Creature_Lore_{bucket}") ?? ContainmentCreatureCatalog.Lore(creature);
    }
}
