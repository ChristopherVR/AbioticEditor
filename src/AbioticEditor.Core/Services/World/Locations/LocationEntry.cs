namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// What a location entry's coordinates mean. The kinds are kept apart on purpose: a fixed
/// placement, a spawn point, a remembered save position and a live position answer different
/// questions and must never be drawn as if they were the same thing.
/// </summary>
public enum LocationKind
{
    /// <summary>Baked into a level package (for example a door). Read from game assets.</summary>
    StaticPlacement,

    /// <summary>Where something may spawn. Not a confirmed present entity.</summary>
    SpawnPoint,

    /// <summary>The position written into the save file the last time the game saved.</summary>
    LastSavedPosition,

    /// <summary>A position read from the running game.</summary>
    CurrentLivePosition,

    /// <summary>Carried by or stored in an owner. Points at the owner and has no floor position of its own.</summary>
    CarriedOrContained,

    /// <summary>No trustworthy position exists. See <see cref="LocationEntry.Reason"/>.</summary>
    Unresolved,
}

/// <summary>Why an entry could not be given a position.</summary>
public enum UnresolvedReason
{
    None,

    /// <summary>The level geometry / placement asset is not available (no game install or not extracted).</summary>
    MissingGeometry,

    /// <summary>The actor is not loaded in the running game, so it has no live position.</summary>
    UnloadedActor,

    /// <summary>The actor path names a level or class the current game data no longer has.</summary>
    ObsoletePath,

    /// <summary>The actor uses an asset type the location layer cannot read yet.</summary>
    UnsupportedAsset,

    /// <summary>The save holds no position for this entry (the field was omitted or is unset).</summary>
    NoSavedPosition,
}

/// <summary>Which editor family an entry belongs to.</summary>
public enum LocatedCategory
{
    Npc,
    Creature,
    Door,
    DroppedItem,
    Container,
    ResourceNode,
    Socket,
    Deployable,
    Vehicle,
}

/// <summary>
/// One locatable entity. <see cref="X"/>/<see cref="Y"/>/<see cref="Z"/> are non-null only
/// when <see cref="HasPosition"/> is true; unresolved and carried entries never carry
/// coordinates (an invented (0,0,0) origin is never produced).
/// </summary>
public sealed record LocationEntry(
    string Region,
    string Level,
    string ActorPath,
    string ActorName,
    string? SaveIdentity,
    LocatedCategory Category,
    string? ClassName,
    LocationKind Kind,
    double? X = null,
    double? Y = null,
    double? Z = null,
    string? OwnerActorPath = null,
    UnresolvedReason Reason = UnresolvedReason.None,
    string? Note = null)
{
    /// <summary>True when the entry has a real floor position that a map may mark.</summary>
    public bool HasPosition => X is not null && Y is not null && Z is not null
        && Kind is not (LocationKind.Unresolved or LocationKind.CarriedOrContained);

    /// <summary>The unique key: region, level and full actor path (duplicate actor names differ by level).</summary>
    public string Key => MakeKey(Region, Level, ActorPath);

    public static string MakeKey(string region, string level, string actorPath)
        => string.Concat(region, "|", level, "|", actorPath);
}
