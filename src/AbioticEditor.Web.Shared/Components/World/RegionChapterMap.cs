using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// Maps a sub-level / save-file name token to the story chapter whose card art and region
/// title represent that sector. Shared by the DOORS tab (card art) and the story-events tab
/// (live region filter), so both agree on which sector a token belongs to. Presentation
/// lookup data, not save-editing logic, so it stays out of Core.
/// </summary>
internal static class RegionChapterMap
{
    // Ordered most-specific-first; the first matching token wins.
    private static readonly (string Token, string Row)[] Tokens =
    {
        ("office1", "Office"), ("office2", "Office2"), ("office3", "Office3"), ("office4", "Office3"),
        ("labs_adjustment", "Labs"), ("labs_control", "Tarasque"), ("containment", "Containment"),
        ("helmholtz", "Helmholtz"), ("labs", "Labs"),
        ("security", "PostLabs"), ("canaan", "SecSurfaceElevator"),
        ("dam_hydroplant", "ElectricalStation"), ("hydroplant", "ElectricalStation"),
        ("dam_central", "ElectricalStation"), ("dam_lower", "EndDam"), ("dam_waterfall", "EndDam"),
        ("dam", "ElectricalStation"), ("reservoir", "ElectricalStation"), ("voussoir", "Voussoir"),
        ("mfmines", "MFMines"), ("mfmaggot", "MFMines"), ("mines", "MFMines"),
        ("mfwest", "MF"), ("mfhq", "MF"), ("foundry", "MF"), ("manufacturing", "MF"), ("mf", "MF"),
        ("pens", "Pens"), ("parking", "Office"), ("tram", "Office3"),
        ("df_labs", "Reactors1Labs"), ("dflabs", "Reactors1Labs"),
        ("radwaste", "ReactorsAll"), ("df_war", "ReactorsAll"), ("df_overgrowth", "ReactorsAll"),
        ("df_central", "ReactorsAll"), ("darkfusion", "ReactorsEntry"), ("df", "ReactorsEntry"),
        ("reactor", "ReactorsEntry"), ("shadowgate", "Shadowgate"),
        ("botanical", "Botanical"), ("fracture", "Fracture"), ("southisland", "SouthIsland"),
        ("residence", "Residence"),
        ("powerservices", "PowerServices"), ("plant", "PowerServices"),
        ("flathill", "Flathill"), ("fog", "Flathill"),
        ("facility", "Office"),
    };

    public static StoryChapter? ForToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var lower = token.ToLowerInvariant();
        foreach (var (t, row) in Tokens)
        {
            if (lower.Contains(t, StringComparison.Ordinal)) return StoryProgressionCatalog.Find(row);
        }
        return null;
    }
}
