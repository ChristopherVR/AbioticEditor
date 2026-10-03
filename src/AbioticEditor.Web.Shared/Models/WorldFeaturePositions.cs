using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>Known world positions for world-list entries, including actors spawned at runtime.</summary>
public static class WorldFeaturePositions
{
    public static PlacedVector? Find(IWorldFeaturesSession session, string featureId, string key)
        => session switch
        {
            WorldSaveSession file => file.MapEntryPosition(featureId, key),
            LiveNpcSpawnsFeatureSession live => live.Spawners.FirstOrDefault(o => o.Id == key) is { } o ? new(o.X, o.Y, o.Z) : null,
            LiveResourceNodesFeatureSession live => live.Nodes.FirstOrDefault(o => o.Id == key) is { } o ? new(o.X, o.Y, o.Z) : null,
            LivePowerSocketsFeatureSession live => live.Sockets.FirstOrDefault(o => o.Id == key) is { } o ? new(o.X, o.Y, o.Z) : null,
            LiveButtonsFeatureSession live => live.Buttons.FirstOrDefault(o => o.Id == key) is { } o ? new(o.X, o.Y, o.Z) : null,
            LiveDestructiblesFeatureSession live => live.Destructibles.FirstOrDefault(o => o.Id == key) is { } o ? new(o.X, o.Y, o.Z) : null,
            LiveElevatorsFeatureSession live => live.Elevators.FirstOrDefault(o => o.Id == key) is { } o ? new(o.X, o.Y, o.Z) : null,
            LivePortalsFeatureSession live => live.Portals.FirstOrDefault(o => o.Id == key) is { } o ? new(o.X, o.Y, o.Z) : null,
            LiveCorpsesFeatureSession live => live.Corpses.FirstOrDefault(o => o.Id == key) is { } o ? new(o.X, o.Y, o.Z) : null,
            LiveTramsFeatureSession live => live.Trams.FirstOrDefault(o => o.Id == key) is { } o ? new(o.X, o.Y, o.Z) : null,
            LiveDeployedCareSession live => live.Entries.FirstOrDefault(o => o.Id == key) is { } o ? new(o.X, o.Y, o.Z) : null,
            _ => null,
        };

    public static string? Region(IWorldFeaturesSession session, string key)
        => session is LiveTramsFeatureSession trams ? trams.Trams.FirstOrDefault(t => t.Id == key)?.Region : null;

    public static WorldLocateTarget? Target(IWorldFeaturesSession session, string featureId, string key, string label)
        => WorldLocateTarget.ForMapKey(key, label) is { } target
            ? target with { At = Find(session, featureId, key), FeatureId = featureId } : null;
}
