namespace AbioticEditor.Core.Codex;

/// <summary>
/// Maps fish (DT_Fish rows) to their image file names on abioticfactor.wiki.gg.
///
/// Researched 2026-06: each fish's wiki page (and the species table on
/// <c>/wiki/Fishing</c>) uses the GAME'S ITEM ICON as the infobox image - file names
/// like <c>Itemicon_antefish.png</c> or <c>Item Icon - Gem Crab.png</c> - NOT
/// <c>&lt;PageName&gt;.png</c>. The names are too irregular to derive (mixed casing,
/// <c>rare01</c>/<c>rare_1</c> suffixes, internal names like <c>FogFish</c> for the
/// Chordfish), so the tables below are curated.
///
/// Primary key is the DT_Fish ROW ID (e.g. <c>Fogfish_rare1</c>), because rare
/// variants reuse their base species' display name in-game while the wiki gives each
/// rare its own art (Oxidious/Osseous/Saxe/...). Display name is the fallback for
/// future rows, then a best-effort guess in both wiki upload conventions.
/// </summary>
public static class FishWikiImages
{
    /// <summary>DT_Fish row id -> wiki file (ids surveyed from the live DT_Fish table).</summary>
    private static readonly Dictionary<string, string> ByRowId =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Antefish"]              = "Itemicon_antefish.png",
            ["Antefish_rare1"]        = "Itemicon_antefish_rare.png",          // Oxidious Antefish
            ["Fogfish"]               = "Itemicon_FogFish.png",                // Chordfish
            ["Fogfish_rare1"]         = "Itemicon_fogfish_rare.png",           // Osseous Chordfish
            ["DarkwaterFish"]         = "Itemicon_DarkWaterFIsh.png",
            ["DarkwaterFish_rare1"]   = "Itemicon_darkwaterfish_rare01.png",   // Saxe Darkwater Fish
            ["IceFish"]               = "Itemicon_fish_ice.png",               // Frigid Queenfish
            ["IceFish_rare1"]         = "Itemicon_fish_ice_rare.png",          // Adiabatic Queenfish
            ["GemCrab"]               = "Item Icon - Gem Crab.png",
            ["GemCrab_rare1"]         = "Item Icon - Opal Crab.png",
            ["Eel"]                   = "Itemicon_eel.png",                    // Gutfish Eel
            ["Eel_rare1"]             = "Itemicon_eel_rare.png",               // Nacreous Gutfish Eel
            ["Eel_rare2"]             = "Itemicon_eel_translucent.png",        // Wraith Gutfish Eel
            ["Eel_rare3"]             = "Itemicon_eel_goldentail.png",         // Auric Gutfish Eel
            ["ReaperFish"]            = "Itemicon_InkFish.png",                // Inkfish
            ["ReaperFish_rare1"]      = "Itemicon_InkFish_rare1.png",          // Sable Inkfish
            ["IS0098"]                = "Itemicon_IS98FIsh.png",
            ["IS0098_rare1"]          = "Itemicon_IS98fish_rare01.png",        // Amaranthic IS-0098
            ["MoonFish"]              = "Itemicon_moonfish.png",
            ["MoonFish_AllDay"]       = "Itemicon_moonfish.png",
            ["MoonFish_rare1"]        = "Itemicon_moonfish_rare01.png",        // Pelagic Moon Fish
            ["MoonFish_rare1_AllDay"] = "Itemicon_moonfish_rare01.png",
            ["UmbraFish"]             = "Item Icon - Penumbra.png",
            ["UmbraFish_rare1"]       = "Item Icon - Crepuscular Penumbra.png",
            ["Portalfish"]            = "Itemicon_fish_portal.png",
            ["Portalfish_rare1"]      = "Itemicon_portalfish_rare_1.png",      // Corruscating Portal Fish
            ["Portalfish_rare2"]      = "Itemicon_portalfish_rare_2.png",      // Entropic Portal Fish
            ["Portalfish_rare_torii"] = "Itemicon_portalfish_rare_torii.png",  // Portal Koi
            ["Radfish"]               = "Itemicon_RadFish.png",
            ["Radfish_rare1"]         = "Itemicon_radfish_rare01.png",         // Caustic Radfish
        };

    /// <summary>
    /// Display name -> wiki file, from the species table on /wiki/Fishing. Covers
    /// fish the row-id table may not know yet (newer game builds); note rare
    /// variants can't be resolved this way because they share the base display name.
    /// </summary>
    private static readonly Dictionary<string, string> ByDisplayName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Antefish"]                 = "Itemicon_antefish.png",
            ["Oxidious Antefish"]        = "Itemicon_antefish_rare.png",
            ["Portal Fish"]              = "Itemicon_fish_portal.png",
            ["Corruscating Portal Fish"] = "Itemicon_portalfish_rare_1.png",
            ["Entropic Portal Fish"]     = "Itemicon_portalfish_rare_2.png",
            ["Portal Koi"]               = "Itemicon_portalfish_rare_torii.png",
            ["Moon Fish"]                = "Itemicon_moonfish.png",
            ["Pelagic Moon Fish"]        = "Itemicon_moonfish_rare01.png",
            ["Chordfish"]                = "Itemicon_FogFish.png",
            ["Osseous Chordfish"]        = "Itemicon_fogfish_rare.png",
            ["Radfish"]                  = "Itemicon_RadFish.png",
            ["Caustic Radfish"]          = "Itemicon_radfish_rare01.png",
            ["Gem Crab"]                 = "Item Icon - Gem Crab.png",
            ["Opal Crab"]                = "Item Icon - Opal Crab.png",
            ["IS-0098"]                  = "Itemicon_IS98FIsh.png",
            ["Amaranthic IS-0098"]       = "Itemicon_IS98fish_rare01.png",
            ["Inkfish"]                  = "Itemicon_InkFish.png",
            ["Sable Inkfish"]            = "Itemicon_InkFish_rare1.png",
            ["Gutfish Eel"]              = "Itemicon_eel.png",
            ["Nacreous Gutfish Eel"]     = "Itemicon_eel_rare.png",
            ["Wraith Gutfish Eel"]       = "Itemicon_eel_translucent.png",
            ["Auric Gutfish Eel"]        = "Itemicon_eel_goldentail.png",
            ["Darkwater Fish"]           = "Itemicon_DarkWaterFIsh.png",
            ["Saxe Darkwater Fish"]      = "Itemicon_darkwaterfish_rare01.png",
            ["Frigid Queenfish"]         = "Itemicon_fish_ice.png",
            ["Adiabatic Queenfish"]      = "Itemicon_fish_ice_rare.png",
            ["Rudd Fish"]                = "Item Icon - Rudd Fish.png",
            ["Silken Betta"]             = "Item Icon - Silken Betta.png",
            ["Gossamer Betta"]           = "Item Icon - Gossamer Betta.png",
            ["Penumbra"]                 = "Item Icon - Penumbra.png",
            ["Crepuscular Penumbra"]     = "Item Icon - Crepuscular Penumbra.png",
        };

    /// <summary>The verified row-id -> wiki-file table (for coverage tests).</summary>
    public static IReadOnlyDictionary<string, string> KnownRows => ByRowId;

    /// <summary>The verified display-name -> wiki-file table (for coverage tests).</summary>
    public static IReadOnlyDictionary<string, string> KnownFish => ByDisplayName;

    /// <summary>
    /// Wiki image file names to try, in order, for a DT_Fish row. The row id is
    /// authoritative (it distinguishes rare variants); the display name covers rows
    /// the id table doesn't know; the trailing guesses follow the two wiki upload
    /// conventions and simply resolve to no image when wrong.
    /// </summary>
    public static IReadOnlyList<string> CandidatesFor(string? fishId, string? displayName)
    {
        var candidates = new List<string>(3);
        if (fishId is not null && ByRowId.TryGetValue(fishId, out var byId))
        {
            candidates.Add(byId);
        }
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            if (ByDisplayName.TryGetValue(displayName, out var byName))
            {
                if (!candidates.Contains(byName, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(byName);
                }
            }
            else
            {
                candidates.Add($"Item Icon - {displayName}.png");
                candidates.Add($"Itemicon_{displayName.Replace(' ', '_')}.png");
            }
        }
        return candidates;
    }
}
