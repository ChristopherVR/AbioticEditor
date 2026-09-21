namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Verified character portraits for named story holograms. These are character references,
/// not claims that every recording uses the same scene or costume. File names are from the
/// official Abiotic Factor Wiki's Compendium Images category and Abe Stern infobox,
/// except <c>Hologram.PNG</c>, which is named on the Derek Manse page.
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
        ("Order Interfector", ["Order_Interfector.png"]),
        ("Interfector", ["Order_Interfector.png"]),
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
        foreach (var (name, files) in Entries)
        {
            if (characterName.Contains(name, StringComparison.OrdinalIgnoreCase)) return files;
        }
        return Array.Empty<string>();
    }
}
