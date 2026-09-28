namespace AbioticEditor.Core.WorldSaves;

/// <summary>Who decides where a saved object stands when the world loads.</summary>
public enum PlacementAuthority
{
    /// <summary>
    /// The save spawns the object (its map key is a GUID and it carries a position), so the saved
    /// position is what the game uses. Moving the saved position moves the object.
    /// </summary>
    SaveSpawned,

    /// <summary>
    /// The object is placed by the cooked level; the save keeps only its state, keyed by the level
    /// actor path, and stores no position. There is nothing to move in the save.
    /// </summary>
    LevelPlacedStateOnly,

    /// <summary>
    /// The object is placed by the cooked level but the save also records a transform for it. The
    /// fixtures prove the value is stored; they do not prove the game re-applies it on load, so the
    /// editor treats it as informational.
    /// </summary>
    LevelPlacedSavedPosition,

    /// <summary>The position is an unused reserve member (always the origin in the fixtures).</summary>
    PositionUnused,
}

/// <summary>How one saved map relates to placement.</summary>
/// <param name="Map">The top-level map property.</param>
/// <param name="Authority">Who decides the position.</param>
/// <param name="PositionLeaf">The entry member holding a position, or null when none exists.</param>
/// <param name="Evidence">What in the fixtures backs the classification.</param>
public sealed record MapPlacement(string Map, PlacementAuthority Authority, string? PositionLeaf, string Evidence);

/// <summary>
/// Which saved positions are authoritative and which objects are only located by their level.
/// The classification is exactly what the fixture census proves (map key shape, and whether an entry
/// member holds a position); see docs/research/world-and-placed-object-state.md and
/// <c>WorldObjectPlacementCatalogTests</c>.
/// </summary>
/// <remarks>
/// <c>DeployedObjectMap</c> and <c>VehicleMap</c> mix both kinds in one map: GUID-keyed entries are
/// spawned from the save, actor-path-keyed entries are level actors. <see cref="ForEntry"/> makes
/// that split from the key.
/// </remarks>
public static class WorldObjectPlacementCatalog
{
    /// <summary>Every classified map.</summary>
    public static IReadOnlyList<MapPlacement> Maps { get; } =
    [
        new("DroppedItemMap", PlacementAuthority.SaveSpawned, "ItemLocation",
            "all 2435 entries GUID-keyed with a non-zero ItemLocation"),
        new("PetNPC", PlacementAuthority.SaveSpawned, "Location",
            "all 27 entries GUID-keyed with a non-zero Location"),
        new("DeployedObjectMap", PlacementAuthority.SaveSpawned, "Transform",
            "2528 GUID-keyed player-built entries (authoritative); 7402 level-path entries carry a Transform too (see ForEntry)"),
        new("VehicleMap", PlacementAuthority.SaveSpawned, "Transform",
            "2 GUID-keyed entries (authoritative); 55 level-path entries carry a Transform too (see ForEntry)"),

        new("ResourceNodeMap", PlacementAuthority.LevelPlacedSavedPosition, "CurrentPosition",
            "all 12457 entries level-path keyed; CurrentPosition is the origin for 3317 of them"),

        new("NarrativeNPCMap", PlacementAuthority.PositionUnused, "Location",
            "all 534 entries have Location at the origin"),

        new("SimpleDoorMap", PlacementAuthority.LevelPlacedStateOnly, null, "level-path keys, no position member"),
        new("SecurityDoorMap", PlacementAuthority.LevelPlacedStateOnly, null, "level-path keys, no position member"),
        new("ButtonMap", PlacementAuthority.LevelPlacedStateOnly, null, "level-path keys, no position member"),
        new("CorpseMap", PlacementAuthority.LevelPlacedStateOnly, null, "level-path keys, no position member"),
        new("DecalMap", PlacementAuthority.LevelPlacedStateOnly, null, "level-path keys, no position member"),
        new("DestructibleMap", PlacementAuthority.LevelPlacedStateOnly, null, "level-path keys, no position member"),
        new("ElevatorMap", PlacementAuthority.LevelPlacedStateOnly, null, "level-path keys, no position member"),
        new("NPCSpawnMap", PlacementAuthority.LevelPlacedStateOnly, null, "level-path keys, no position member"),
        new("PortalMap", PlacementAuthority.LevelPlacedStateOnly, null, "level-path keys, no position member"),
        new("TramMap", PlacementAuthority.LevelPlacedStateOnly, null,
            "level-path keys; only LastStation (a level actor reference) and ContainerInventories"),
        new("TriggerMap", PlacementAuthority.LevelPlacedStateOnly, null, "trigger id keys, no position member"),
        new("PowerSocketMap", PlacementAuthority.LevelPlacedStateOnly, null,
            "level-path or socket-id keys, no position member"),
    ];

    /// <summary>The classification of a map, or null when it is not covered.</summary>
    public static MapPlacement? ForMap(string map)
        => Maps.FirstOrDefault(m => string.Equals(m.Map, map, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Classifies one entry of a mixed map from its key: a level actor path is level-placed (its saved
    /// position is informational), anything else is spawned by the save.
    /// </summary>
    public static PlacementAuthority ForEntry(string map, string key)
    {
        var placement = ForMap(map);
        if (placement is null) return PlacementAuthority.LevelPlacedStateOnly;

        var mixed = placement.Map is "DeployedObjectMap" or "VehicleMap";
        if (!mixed) return placement.Authority;

        return key.Contains("PersistentLevel.", StringComparison.Ordinal)
            ? PlacementAuthority.LevelPlacedSavedPosition
            : PlacementAuthority.SaveSpawned;
    }
}
