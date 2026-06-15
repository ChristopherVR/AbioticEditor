namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Maps a world-state feature (by <see cref="IWorldMapFeature.Id"/>) to candidate
/// abioticfactor.wiki.gg image file names, so a feature tab can show one representative picture
/// of that structure type. The image is per <i>type</i> (every tram shares the tram image).
///
/// <para>Mapped structures: the <b>Teleporter Pad</b> (<c>/wiki/Teleporter_Pad</c>), the
/// <b>Tram</b> (<c>/wiki/Tram</c>), the <b>Power Socket</b> (the Energy page's
/// <c>Outlet.png</c> render), and the <b>Elevator</b> (best-effort - the wiki has no dedicated
/// elevator render, so this falls back through the keypad/lever icons that operate the lifts).
/// The remaining fixed world-state structures (buttons, fixed world teleporters, triggers) have
/// no wiki render - "Teleporter"/"Power" are disambiguation pages, buttons appear only in
/// walkthrough prose, and triggers are invisible logic volumes - so they map to nothing and the
/// feature tab simply shows no image. Candidates are tried best-first; an unresolved name is
/// skipped, so adding a speculative name never breaks the graceful no-image fallback. Add a
/// verified file name here if the wiki later pictures one.</para>
/// </summary>
public static class FeatureWikiImageCatalog
{
    private static readonly Dictionary<string, string[]> ByFeatureId =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Placed teleporter pads - the crafted teleporter (lodestone) infobox image.
            ["teleporter-pads"] = ["Itemicon_craftedteleporter_lodestone.png"],
            // The tram - the same render the vehicle/door catalogs use.
            ["trams"] = ["Vehicle_-_Tram.png"],
            // Power sockets - the wall outlet render from the Energy page, with the plug-strips
            // item icon as a backup.
            ["power-sockets"] = ["Outlet.png", "Itemicon_plugstrips.png"],
            // Elevators - no dedicated wiki render exists, so fall back through the keypad/lever
            // icons that operate the lifts; any that don't resolve are skipped.
            ["elevators"] = ["Itemicon_lever1.png", "Itemicon_keypad.png"],
        };

    /// <summary>Candidate wiki image file names for a feature, best first. Empty when the wiki
    /// has no picture of this structure type (the common case - see class docs).</summary>
    public static IReadOnlyList<string> CandidatesFor(string featureId)
        => featureId is not null && ByFeatureId.TryGetValue(featureId, out var files)
            ? files
            : Array.Empty<string>();

    /// <summary>Feature ids that have at least one candidate image (for coverage tests).</summary>
    public static IReadOnlyCollection<string> MappedFeatureIds => ByFeatureId.Keys;
}
