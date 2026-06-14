namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Display names and flavour text for the creatures held in Leyak Containment Units.
/// The save stores only the raw creature row id (e.g. <c>Leyak</c>, <c>LeyakB</c>);
/// these labels and blurbs are wiki-sourced reference data, kept in Core so any
/// frontend can describe a contained entity without re-deriving it.
/// </summary>
public static class ContainmentCreatureCatalog
{
    /// <summary>A friendlier label for a known creature row, or the raw id otherwise.</summary>
    public static string DisplayName(string creature) => creature switch
    {
        "Leyak" => "Leyak",
        "LeyakB" or "Leyak_B" => "Leyak (Alpha)",
        "Krasue" => "Krasue",
        _ => creature,
    };

    /// <summary>Short flavour blurb for the containment detail card.</summary>
    public static string Lore(string creature) =>
        creature.StartsWith("Krasue", StringComparison.OrdinalIgnoreCase)
            ? "A frost variant of the Leyak: a severed-head entity that only manifests when its target is freezing, then locks them in place. Captured in a containment unit; releasing it frees it back into the world."
            : creature.StartsWith("Leyak", StringComparison.OrdinalIgnoreCase)
                ? "A floating, disembodied head from the Anteverse that stalks players in the dark. A Leyak Containment Unit traps it; releasing it sends it roaming the facility again."
                : "A contained entity. Releasing it removes the containment link so it roams free on next load.";

    /// <summary>
    /// Ordered in-pak compendium texture refs to try for a creature, most-specific first
    /// (<c>/Game/...</c> object paths for
    /// <see cref="Assets.GameAssetProvider.ExtractTextureByGameRef"/>). Empty when the game
    /// ships no bestiary portrait for it - the Krasue's only in-pak art is a sleep-minigame
    /// pixel sprite - in which case a frontend should fall back to its own bundled image.
    /// Crucially this never substitutes one creature's art for another's: an unmatched row
    /// returns its own best-guess ref, not a Leyak.
    /// </summary>
    public static IReadOnlyList<string> TextureRefs(string creature)
    {
        const string dir = "/Game/Textures/GUI/Compendium/Entries/";
        return creature switch
        {
            "Leyak" => [dir + "T_Compendium_Leyak"],
            "LeyakB" or "Leyak_B" => [dir + "T_Compendium_LeyakB", dir + "T_Compendium_Leyak"],
            "LeyakPest" or "Leyak_Pest" => [dir + "T_Compendium_LeyakPest"],
            "Krasue" => [],
            _ => [dir + "T_Compendium_" + creature],
        };
    }
}
