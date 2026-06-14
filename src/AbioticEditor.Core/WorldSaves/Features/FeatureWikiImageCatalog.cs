namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Maps a world-state feature (by <see cref="IWorldMapFeature.Id"/>) to candidate
/// abioticfactor.wiki.gg image file names, so a feature tab can show one representative picture
/// of that structure type. The image is per <i>type</i> (all elevators share the elevator image),
/// since these are fixed level actors with no per-entry art.
///
/// <para>Researched 2026-06 (same finding as <see cref="AbioticEditor.Core.WorldSaves.DoorWikiImageCatalog"/>):
/// the wiki pictures the placed <b>teleporter</b> items but has no clean structure renders for
/// elevators, buttons or power sockets - "Teleporter" is a disambiguation page, and elevators/
/// buttons appear only in walkthrough prose. So the elevator/button/socket entries are best-effort
/// candidate names that usually miss; <see cref="AbioticEditor.Core.Assets.WikiImageCache"/>
/// resolves the first that exists and the UI shows an honest "no image" caption otherwise. If the
/// wiki later adds art under one of these names it lights up with no code change.</para>
/// </summary>
public static class FeatureWikiImageCatalog
{
    private static readonly Dictionary<string, string[]> ByFeatureId =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Placed teleporter pads - the Personal Teleporter item (verified file names).
            ["teleporter-pads"] = ["Itemicon_personalteleporter.png", "Named_personal_teleporter.png"],
            // Fixed world teleporters: no dedicated render exists; the teleporter item icon is the
            // closest representative picture.
            ["portals"] = ["Itemicon_personalteleporter.png", "Named_personal_teleporter.png"],
            // Best-effort (usually miss - the wiki has no elevator/button/socket structure art).
            ["elevators"] = ["Elevator.png", "Facility_Elevator.png", "Elevator_Shaft.png"],
            ["buttons"] = ["Button.png", "Pushable_Button.png", "Switch.png"],
            ["power-sockets"] = ["Itemicon_powersocket.png", "Power_Socket.png", "Wall_Outlet.png"],
        };

    /// <summary>Candidate wiki image file names for a feature, best first. Empty when none known.</summary>
    public static IReadOnlyList<string> CandidatesFor(string featureId)
        => featureId is not null && ByFeatureId.TryGetValue(featureId, out var files)
            ? files
            : Array.Empty<string>();

    /// <summary>Feature ids that have at least one candidate image (for coverage tests).</summary>
    public static IReadOnlyCollection<string> MappedFeatureIds => ByFeatureId.Keys;
}
