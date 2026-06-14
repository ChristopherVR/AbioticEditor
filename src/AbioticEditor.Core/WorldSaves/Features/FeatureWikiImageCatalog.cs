namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Maps a world-state feature (by <see cref="IWorldMapFeature.Id"/>) to candidate
/// abioticfactor.wiki.gg image file names, so a feature tab can show one representative picture
/// of that structure type. The image is per <i>type</i> (every tram shares the tram image).
///
/// <para><b>Researched 2026-06 (same finding as
/// <see cref="AbioticEditor.Core.WorldSaves.DoorWikiImageCatalog"/>): the wiki does not picture
/// these fixed world-state structures.</b> "Teleporter" is a disambiguation page; the only
/// teleporter images are the Personal Teleporter <i>item</i> icon and a <i>tooltip</i> screenshot
/// (neither is the placed pad, so showing them would be misleading). "Power" is a disambiguation
/// page with no socket/outlet render. Elevators and buttons appear only in walkthrough prose, and
/// triggers are invisible logic volumes that can't be pictured anywhere. So those features map to
/// nothing and the UI shows an honest "no wiki image" note instead of a wrong picture.</para>
///
/// <para>The one fixed structure the wiki actually pictures is the <b>Tram</b>
/// (<c>Vehicle_-_Tram.png</c>, the same render the vehicle and door catalogs use). If the wiki
/// later adds real structure art, add its verified file name here and it lights up with no other
/// code change.</para>
/// </summary>
public static class FeatureWikiImageCatalog
{
    private static readonly Dictionary<string, string[]> ByFeatureId =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // The only fixed world-state structure with a real wiki render.
            ["trams"] = ["Vehicle_-_Tram.png"],
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
