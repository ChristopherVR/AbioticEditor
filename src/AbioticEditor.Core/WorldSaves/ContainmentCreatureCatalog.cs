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
        creature.StartsWith("Leyak", StringComparison.OrdinalIgnoreCase)
            ? "A floating, disembodied head from the Anteverse that stalks players in the dark. A Leyak Containment Unit traps it; releasing it sends it roaming the facility again."
            : creature.StartsWith("Krasue", StringComparison.OrdinalIgnoreCase)
                ? "A nocturnal severed-head entity. Captured in a containment unit; releasing it frees it back into the world."
                : "A contained entity. Releasing it removes the containment link so it roams free on next load.";
}
