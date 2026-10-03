namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Verified character portraits for named story characters and holograms. These are character references,
/// not claims that every recording uses the same scene or costume. File names are from the
/// official Abiotic Factor Wiki's Compendium Images category and Abe Stern infobox,
/// except <c>Hologram.PNG</c>, which is named on the Derek Manse page, and the Unlost entries,
/// which are the infobox images of their own wiki pages.
/// </summary>
public static class HologramPortraitCatalog
{
    private static readonly (string Name, string[] Files)[] Entries =
    [
        ("Dr. Manse", ["Hologram.PNG", "T_Compendium_Manse.png"]),
        ("Dr. Derek Manse", ["Hologram.PNG", "T_Compendium_Manse.png"]),
        ("Derek Manse", ["Hologram.PNG", "T_Compendium_Manse.png"]),
        ("Dr. Riggs", ["T_Compendium_Riggs.png"]),
        ("Dr. Stern", ["T_Compendium_Abe.png"]),
        ("Dr. Abe Stern", ["T_Compendium_Abe.png"]),
        ("Abe", ["T_Compendium_Abe.png"]),
        ("Dr. Cahn", ["T_Compendium_Cahn.png"]),
        ("Janet", ["T_Compendium_Janet.png"]),
        ("Hasta Tria", ["T_Compendium_HastaTria.png"]),
        ("Frake", ["T_Compendium_Frake.png"]),
        ("Hank", ["T_Compendium_Hank.png"]),
        ("Dr. Hill", ["T_Compendium_KHill.png"]),
        ("Dr. Hoff", ["T_Compendium_Hoff.png"]),
        ("Dr. Houston", ["T_Compendium_Houston.png"]),
        ("Dr. Newman", ["T_Compendium_Newman.png"]),
        ("Dr. Pendleton", ["T_Compendium_Pendleton.png"]),
        ("Isaiah", ["T_Compendium_IsaiahDeal.png"]),
        ("Jimmy", ["T_Compendium_Jimmy.png"]),
        ("Jonas", ["T_Compendium_JonasConti.png"]),
        ("Kylie", ["T_Compendium_Kylie.png"]),
        ("Marion", ["T_Compendium_Marion.png"]),
        ("Order Interfector", ["Order_Interfector.png"]),
        ("Interfector", ["Order_Interfector.png"]),
        ("Sister of the Unlost", ["Sister_of_the_Unlost.png"]),
        ("Mystagogue Eye", ["Mystagogue_Eye_of_the_Unlost.png"]),
        ("Mystagogue", ["Mage_of_the_Unlost.png"]),
    ];

    /// <summary>Every verified file used by this catalog, for the offline image bundle.</summary>
    public static IReadOnlyCollection<string> AllWikiFiles => Entries
        .SelectMany(e => e.Files)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>Returns verified image names for a named hologram, or an empty list.</summary>
    public static IReadOnlyList<string> CandidatesFor(string? characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return Array.Empty<string>();
        if (characterName.Contains("UnlostMage", StringComparison.OrdinalIgnoreCase)) return ["Mage_of_the_Unlost.png"];
        if (characterName.Contains("HammeringHank", StringComparison.OrdinalIgnoreCase)) return ["T_Compendium_Hank.png"];
        if (characterName.Contains("NarrativeNPC_Exor_ParentBP", StringComparison.OrdinalIgnoreCase)) return ["Exor.png"];
        foreach (var (name, files) in Entries)
        {
            if (characterName.Contains(name, StringComparison.OrdinalIgnoreCase)) return files;
        }
        return Array.Empty<string>();
    }
}
