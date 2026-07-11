namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Maps door blueprint classes (see <see cref="DoorClassCatalog"/>) to image file
/// names on abioticfactor.wiki.gg, for the door detail panel.
///
/// Researched 2026-06: the wiki has NO articles for door types (no Security Door,
/// Blast Door, Hatch, Doors or Keycard(s) pages) and only two files with "door" in the
/// title on the entire wiki - <c>Pet Rock - Door.png</c> (a pet rock posed at a door)
/// and <c>Aim Icon - Door.png</c> (the 64px interaction glyph). File-namespace
/// searches for security/blast/hatch/shutter/vacuum/keycard/gatekey imagery only
/// return item icons and sector maps. In short: door types simply are not pictured on
/// the wiki, so this catalog is an honest near-empty subset; unmapped classes keep the
/// sector-card fallback in the UI with a "no door image available" caption.
///
/// The one defensible mapping: the Tram page's photo for the tram rail door (the image
/// shows the tram vehicle the rail door belongs to).
/// </summary>
public static class DoorWikiImageCatalog
{
    private static readonly Dictionary<string, string> ByClassName =
        new(StringComparer.Ordinal)
        {
            // /wiki/Tram - the tram whose rail door this class controls.
            ["TramRailDoor_C"] = "Vehicle - Tram.png",
        };

    /// <summary>Every class with a wiki image (for coverage tests).</summary>
    public static IReadOnlyDictionary<string, string> MappedClasses => ByClassName;

    /// <summary>
    /// The wiki image file for a door class, or <c>null</c> when the wiki has no
    /// picture of this door type (true for almost all of them - see class docs).
    /// </summary>
    public static string? WikiFileFor(string? className)
        => className is not null && ByClassName.TryGetValue(className, out var file) ? file : null;
}
