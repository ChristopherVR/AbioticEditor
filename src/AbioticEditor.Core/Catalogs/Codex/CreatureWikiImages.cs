namespace AbioticEditor.Core.Codex;

/// <summary>
/// Maps a creature's display name (see <c>LiveNpcsTab.razor</c>'s own <c>DisplayName</c>, which
/// strips the <c>NPC_Monster_</c>/<c>NPC_</c> class prefix and title-cases what's left) to its
/// image file name on abioticfactor.wiki.gg.
///
/// Researched 2026-09-17 against the roster on <c>/wiki/Creatures</c> (Combat Creatures, Combat
/// Humanoids, Combat Robots, Non-Combat Creatures - roughly 85 entries at the time). Sampled 22
/// pages spread across every category by reading each one's own infobox image reference directly,
/// rather than assuming: unlike <see cref="FishWikiImages"/> (which reuses irregular item-icon
/// file names), most creature pages simply use the page's own title as the image file name
/// (spaces to underscores, a lowercase <c>.png</c> extension, any parenthetical disambiguator like
/// "(Enemy)" dropped) - 16 of the 22 sampled pages matched that exactly (Zombie, Bigfoot,
/// Archivist, Guard, Corpsewalker, Exor_Cha, Electro-Pest, Nyth, Close-Quarters_Combatant,
/// Mother_of_the_Fallow, Mystagogue_Eye, Tainted_Carbuncle, Krasue, Tarasque, Furfur,
/// The_Moving_Box). The rest are genuine exceptions, not sampling noise - a handful of
/// "Containment"-flavored creatures use an internal IS-#### codename as their image instead of
/// their page title (Darkwater Beast/IS-0023, The Wayseeker/IS-0117, IS-0139/Crystalisk - this
/// pattern likely repeats for creatures not sampled here, so more of the guessed candidates below
/// will silently miss for that subset until someone curates them too), and two pages upload their
/// art with an uppercase <c>.PNG</c> extension (Peccary, Symphonist) rather than lowercase.
///
/// <see cref="CandidatesFor"/> tries the curated file first, then the page-title guess, then - for
/// a multi-word name - the words in reverse order, since a live class name's own word order (e.g.
/// <c>NPC_Monster_Pest_Electro_C</c> -> "Pest Electro") does not always match the wiki's page
/// title order (e.g. "Electro-Pest") the way <c>PetCatalog.CompendiumTextureRefs</c> already had
/// to account for with the same family/variant class-naming shape. A wrong guess simply resolves
/// to no image (see <c>WikiImageCache</c>), never an error.
/// </summary>
public static class CreatureWikiImages
{
    /// <summary>Display name (case-insensitive) -> verified wiki file name.</summary>
    private static readonly Dictionary<string, string> ByDisplayName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // "Containment"-flavored creatures whose wiki art uses an internal codename instead
            // of the page title - keyed by both the page title and the codename itself, since it
            // is not evidenced live which one a connected game's class name actually derives to.
            ["IS-0139"] = "Crystalisk.png",
            ["Crystalisk"] = "Crystalisk.png",
            ["Darkwater Beast"] = "IS-0023.png",
            ["IS-0023"] = "IS-0023.png",
            ["The Wayseeker"] = "IS-0117.png",
            ["IS-0117"] = "IS-0117.png",
            ["Security Bot"] = "T1.png",
            // The live class is named base-then-variant (NPC_Monster_Pest_Electro, matching
            // PetCatalog's own curated friendly name "Electro Pest" for the very same class -
            // see PetCatalog.cs), so a derived display name could read either "Pest Electro" or
            // "Electro Pest" depending on which convention the caller applies; the wiki page
            // itself is "Electro-Pest" (hyphenated, not underscored) either way, which neither
            // guess in CandidatesFor would reach on its own.
            ["Pest Electro"] = "Electro-Pest.png",
            ["Electro Pest"] = "Electro-Pest.png",
            // Page-title matches, but uploaded with an uppercase extension - curated rather than
            // relying on CandidatesFor's lowercase guess plus a case-insensitive wiki lookup that
            // is not guaranteed.
            ["Peccary"] = "Peccary.PNG",
            ["Symphonist"] = "Symphonist.PNG",
        };

    /// <summary>The verified display-name -> wiki-file table (for coverage tests).</summary>
    public static IReadOnlyDictionary<string, string> KnownCreatures => ByDisplayName;

    /// <summary>Every verified wiki File name, de-duplicated - used to pre-download the offline
    /// fallback bundle; the speculative page-title/reversed-word guesses in
    /// <see cref="CandidatesFor"/> are excluded, the same way <see cref="FishWikiImages.AllWikiFiles"/>
    /// excludes its own guesses.</summary>
    public static IReadOnlyCollection<string> AllWikiFiles =>
        ByDisplayName.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// Wiki image file names to try, in order, for a creature's display name: the curated file
    /// first, then the page-title guess (spaces to underscores, lowercase <c>.png</c>), then - for
    /// a multi-word name only - the same guess with the words reversed, covering a family/variant
    /// class name whose word order does not match the wiki's own page title. See this class's own
    /// remarks for why a curated entry is still needed for some creatures no guess can reach.
    /// </summary>
    public static IReadOnlyList<string> CandidatesFor(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return Array.Empty<string>();
        var name = displayName.Trim();
        var candidates = new List<string>(3);

        if (ByDisplayName.TryGetValue(name, out var known)) candidates.Add(known);

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return candidates;

        var guess = $"{string.Join('_', words)}.png";
        if (!candidates.Contains(guess, StringComparer.OrdinalIgnoreCase)) candidates.Add(guess);

        if (words.Length > 1)
        {
            var reversedWords = words.Reverse().ToArray();
            var reversedGuess = $"{string.Join('_', reversedWords)}.png";
            if (!candidates.Contains(reversedGuess, StringComparer.OrdinalIgnoreCase)) candidates.Add(reversedGuess);
        }

        return candidates;
    }
}
