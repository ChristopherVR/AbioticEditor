using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// One static respawn (punch-card) terminal baked into the Facility level, with its
/// root-component world position (a guaranteed-walkable spawn anchor).
/// </summary>
public sealed record RespawnTerminal(string TerminalGuid, string LocationName, double X, double Y, double Z);

/// <summary>
/// The player save's <c>TerminalRespawnID_</c> is the <c>SpawnedAssetID</c> of a static
/// <c>Deployed_PunchCardTerminal_C</c> actor in <c>Maps/Facility.umap</c>; each actor
/// carries a human-readable <c>LocationName</c> and a world transform. Table extracted
/// from the cooked map (see docs/research-respawn-terminals.md) - editor-assigned
/// GUIDs, stable across updates unless the devs re-place an actor.
/// </summary>
public static class RespawnTerminalCatalog
{
    public static readonly IReadOnlyList<RespawnTerminal> All =
    [
        new("AFB31D8E4DFBB5BE74BEAAADD681A636", "Manufacturing West", -4352, 24123, 810),
        new("95CAED254C17360B69B3738E468CD49C", "Hydroplant", -27352, -2498, 529),
        new("E57CB02C4853F46D2BB7CA80303EB6A3", "Cascade Laboratories", 9173, 9599, -1181),
        new("35DCF84F4AC366B8DCBB61A93D9C83C0", "Security Sector", -3660, -3650, 534),
        new("7CCB5D3A4072BAE875ECA2A05F35AF0F", "Power Services", -29671, -544, -9812),
        new("7996635040409DC197A441922A831284", "The Office Sector", -15691, 20611, 2510),
        new("AC917C804463D66ABBBB3FB89A0174AB", "The Reactors", -13238, 21451, -16451),
        new("35BDEE3649830558D65FCDBDC194C725", "The Mines", 7510, 44947, 755),
        new("601B417D44683BA9E7D422AF8AE457D8", "Residence Sector", -22764, 25001, 135),
        new("476CCD1247914D2A067353BCF0DCC849", "Shopping District", -34192, 55638, -987),
    ];

    /// <summary>Location name for a terminal GUID, or null when unknown.</summary>
    public static string? NameFor(string? terminalGuid)
        => Find(terminalGuid)?.LocationName;

    /// <summary>Full terminal record for a GUID, or null when unknown.</summary>
    public static RespawnTerminal? Find(string? terminalGuid)
        => terminalGuid is null
            ? null
            : All.FirstOrDefault(t => string.Equals(t.TerminalGuid, terminalGuid, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The terminal closest to a Facility-level world position - a cheap "which sector
    /// is this?" heuristic for actors that carry no region of their own (deployables,
    /// containment units). Distances are huge (sectors are kilometres apart in UU), so
    /// nearest-terminal is reliable for anything placed inside a sector proper.
    /// </summary>
    public static RespawnTerminal NearestTo(double x, double y, double z)
        => All.MinBy(t =>
        {
            var dx = t.X - x;
            var dy = t.Y - y;
            var dz = t.Z - z;
            return dx * dx + dy * dy + dz * dz;
        })!;

    /// <summary>
    /// Best terminal anchor for a friendly region label (loose contains-match either
    /// direction, e.g. "Office" ↔ "The Office Sector"). Null when the region has no
    /// punch-card terminal (vignettes, Anteverse...).
    /// </summary>
    public static RespawnTerminal? ForRegionLabel(string? regionLabel)
    {
        if (string.IsNullOrWhiteSpace(regionLabel)) return null;
        return All.FirstOrDefault(t =>
            t.LocationName.Contains(regionLabel, StringComparison.OrdinalIgnoreCase)
            || regionLabel.Contains(t.LocationName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The story chapter row where each terminal's sector opens (mirrors
    /// <see cref="FlagGate"/>'s area-to-chapter table, restricted to sectors that actually
    /// have a punch-card terminal).</summary>
    private static readonly Dictionary<string, string> ChapterTerminalRegion = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Office"] = "The Office Sector",
        ["MF"] = "Manufacturing West",
        ["MFMines"] = "The Mines",
        ["Labs"] = "Cascade Laboratories",
        ["PostLabs"] = "Security Sector",
        ["EndSecurity"] = "Hydroplant",
        ["PowerServices"] = "Power Services",
        ["ReactorsEntry"] = "The Reactors",
        ["Residence"] = "Residence Sector",
    };

    /// <summary>
    /// The best respawn terminal for a story chapter row: its own sector's terminal if the
    /// chapter opens one, else the nearest EARLIER chapter's. Portal-world/vignette chapters
    /// (Flathill, Voussoir, Anteverse C, Fracture, Botanical, DarkLens, SouthIsland, EndGame)
    /// have no dedicated terminal of their own, since punch-card terminals only exist in the
    /// Facility proper - they fall back to whichever sector leads into them. Null only when
    /// the row is unknown to <see cref="StoryProgressionCatalog"/>.
    /// </summary>
    public static RespawnTerminal? ForChapter(string chapterRow)
    {
        var index = StoryProgressionCatalog.IndexOf(chapterRow);
        if (index < 0) return null;
        for (var i = index; i >= 0; i--)
        {
            if (ChapterTerminalRegion.TryGetValue(StoryProgressionCatalog.Chapters[i].Row, out var region))
            {
                return All.FirstOrDefault(t => string.Equals(t.LocationName, region, StringComparison.OrdinalIgnoreCase));
            }
        }
        return null;
    }
}
