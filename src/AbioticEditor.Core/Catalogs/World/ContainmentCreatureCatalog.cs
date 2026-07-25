namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// The <c>EDynamicProperty</c> slots a deployed Leyak Containment Unit uses on its item data
/// (<c>ChangableData_.DynamicProperties_</c>).
///
/// The blueprint reaches these through generic Get/SetDynamicProperty calls, so the slot names
/// are not recoverable from the cooked asset; they were pinned down from the saves instead.
/// Across every fixture world, <c>Generic2</c> and <c>Generic3</c> are used by <em>no other</em>
/// deployable class at all, <c>Generic1</c> only ever holds 0..100 (matching the blueprint's
/// <c>MaxStability</c> of 100 and its -14/night decay), and <c>Generic3</c> matches the creature
/// the metadata save assigns to that exact unit in every case observed (0 for each Leyak unit,
/// 1 for each Krasue unit), which is also the order of the blueprint's two-entry
/// <c>LeyakContainmentData</c> array.
/// </summary>
internal static class ContainmentDynamicSlots
{
    /// <summary>Containment stability, 0..<see cref="ContainmentCreatureCatalog.MaxStability"/>.</summary>
    public const string Stability = "Generic1";

    /// <summary>Index into the blueprint's <c>LeyakContainmentData</c> array (0 = Leyak, 1 = Krasue).</summary>
    public const string CreatureIndex = "Generic3";
}

/// <summary>
/// Display names and flavour text for the creatures held in Leyak Containment Units.
/// The save stores only the raw creature row id (e.g. <c>Leyak</c>, <c>LeyakB</c>);
/// these labels and blurbs are wiki-sourced reference data, kept in Core so any
/// frontend can describe a contained entity without re-deriving it.
/// </summary>
public static class ContainmentCreatureCatalog
{
    /// <summary>
    /// The deployable blueprint class of a Leyak Containment Unit, as it appears in a region
    /// save's <c>DeployedObjectMap</c> (<c>Class_</c> soft-object path ends with this). The unit
    /// is player-crafted only: a sweep of every cooked <c>.umap</c> found no level-placed
    /// instance, so the complete set of units in a world is exactly what the saves contain.
    /// </summary>
    public const string UnitClassName = "Deployed_LeyakContainment_C";

    /// <summary>The item-table row a packaged (picked-up) containment unit uses.</summary>
    public const string UnitItemRow = "Leyak_Containment";

    /// <summary>
    /// Full stability, from the blueprint's <c>MaxStability</c>. A unit's stability is stored in
    /// its <c>EDynamicProperty::Generic1</c> slot and drains by <c>StabilityDecreasePerNight</c>
    /// (-14) each night until it is fed the creature's required item.
    /// </summary>
    public const int MaxStability = 100;

    /// <summary>Stability lost per in-game night when the unit is not fed.</summary>
    public const int StabilityDecreasePerNight = 14;

    /// <summary>
    /// One entry of the containment blueprint's <c>LeyakContainmentData</c> array. The array
    /// index is what a deployed unit stores in its <c>EDynamicProperty::Generic3</c> slot, so it
    /// is the unit's own record of which creature it holds.
    /// </summary>
    /// <param name="Row">Creature row name in <c>DT_NPCList</c> (also the
    /// <c>LeyakContainmentIDs</c> map key).</param>
    /// <param name="Index">Index into <c>LeyakContainmentData</c>.</param>
    /// <param name="StabilityItem">Item row the unit must be fed to hold stability.</param>
    /// <param name="BarColorHex">The unit's progress-bar colour, as authored in the blueprint.</param>
    public readonly record struct ContainableCreature(string Row, int Index, string StabilityItem, string BarColorHex);

    /// <summary>
    /// Every creature a containment unit can hold, in blueprint order. Read out of
    /// <c>Deployed_LeyakContainment</c>'s class defaults: the array has exactly two entries, so
    /// only the Leyak and the Krasue are containable.
    /// </summary>
    public static IReadOnlyList<ContainableCreature> Containable { get; } =
    [
        new("Leyak", 0, "food_greyeb", "FF003A"),
        new("Krasue", 1, "food_milk", "00FFFF"),
    ];

    /// <summary>
    /// The <c>LeyakContainmentData</c> index for a creature row, or -1 when the row is not one
    /// the containment unit knows about.
    /// </summary>
    public static int IndexOf(string? creature)
    {
        if (string.IsNullOrWhiteSpace(creature)) return -1;
        foreach (var entry in Containable)
        {
            if (string.Equals(entry.Row, creature, StringComparison.OrdinalIgnoreCase)) return entry.Index;
        }
        return -1;
    }

    /// <summary>The creature row at a <c>LeyakContainmentData</c> index, or null when out of range.</summary>
    public static string? RowAtIndex(int index)
    {
        foreach (var entry in Containable)
        {
            if (entry.Index == index) return entry.Row;
        }
        return null;
    }

    /// <summary>The item row a unit holding <paramref name="creature"/> must be fed; null when unknown.</summary>
    public static string? StabilityItem(string? creature)
    {
        var index = IndexOf(creature);
        return index < 0 ? null : Containable[index].StabilityItem;
    }

    /// <summary>True when <paramref name="className"/> is a containment unit's deployable class.</summary>
    public static bool IsUnitClass(string? className)
        => className?.Contains("Deployed_LeyakContainment", StringComparison.OrdinalIgnoreCase) == true;

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
