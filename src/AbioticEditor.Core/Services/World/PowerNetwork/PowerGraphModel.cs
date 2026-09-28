namespace AbioticEditor.Core.WorldSaves;

// Power graph model. Three kinds of statement are kept as DISTINCT types on purpose:
//   STORED    - a relationship that is written in a save file (StoredPowerLink).
//   LIVE      - what a running game reported about a socket right now (PowerLiveState).
//   PREDICTED - what the editor thinks the result would be (PredictedPowerResult). No electrical
//               rules are verified, so today this is always Unknown, with the reason spelled out.
// See docs/reference/research/research-power-network-links.md for the fixture evidence behind
// every rule encoded here.

/// <summary>Where one record lives: a save file, the top-level map, and the map key.</summary>
/// <param name="FileName">The world-save file name (for example <c>WorldSave_Facility.sav</c>).</param>
/// <param name="MapName"><c>DeployedObjectMap</c> for devices, <c>PowerSocketMap</c> for sockets.</param>
/// <param name="Key">The map key: a 32-hex GUID for hub devices, an actor path for level-placed things.</param>
public sealed record PowerLocation(string FileName, string MapName, string Key);

/// <summary>What a device is, going only by its deployable class name. Anything that is not one of
/// the known power infrastructure classes is <see cref="Other"/> (benches, fridges, teleporters and
/// so on: things that get plugged in). No switch class was found in any fixture, so none is modeled.</summary>
public enum PowerDeviceRole
{
    /// <summary>The device record was not found in any supplied save, so nothing is known about it.</summary>
    Unknown,
    Battery,
    PlugStrip,
    Plugboard,
    CableReroute,
    LaserPowerConverter,
    /// <summary>A deployable that is not power infrastructure (a consumer, as far as the data shows).</summary>
    Other,
}

/// <summary>How a <c>PowerSocketMap</c> key was recognised.</summary>
public enum PowerSocketKind
{
    /// <summary>The key is a level actor path (<c>...PersistentLevel.PowerSocket_ParentBP_C_9</c>): a fixed socket placed in the map.</summary>
    LevelPlaced,
    /// <summary>The key is a device GUID (32 hex) plus a socket number: an outlet that belongs to a deployed device.</summary>
    DeviceOwned,
    /// <summary>Neither shape; kept rather than dropped.</summary>
    Unrecognized,
}

/// <summary>Whether an endpoint of a stored link could be found.</summary>
public enum PowerEndpointStatus
{
    /// <summary>Found in the same save as the record that names it.</summary>
    Resolved,
    /// <summary>Found, but in a different save file than the record that names it (cross-save / cross-region).</summary>
    ResolvedInOtherSave,
    /// <summary>Not present in any save that was supplied. It may live in a save that was not loaded, or be stale.</summary>
    MissingInSuppliedSaves,
}

/// <summary>Which stored field a link came from.</summary>
public enum PowerLinkSlot
{
    /// <summary><c>PluggedInDeviceAssetID_</c>: the device drawing power from the socket.</summary>
    PluggedIn,
    /// <summary><c>ExtraPoweredDeviceAssetIDs_</c>: never non-empty in any fixture, so its meaning is unverified.</summary>
    ExtraPowered,
}

/// <summary>A device (a <c>DeployedObjectMap</c> entry), or a placeholder for one that is missing.</summary>
public sealed record PowerDeviceNode(
    string Id, string? ClassName, PowerDeviceRole Role, string FriendlyName, bool IsContainer,
    PowerLocation? Location)
{
    /// <summary>True when the device record was not found in any supplied save.</summary>
    public bool IsMissing => Location is null;
}

/// <summary>A power socket record (a <c>PowerSocketMap</c> entry).</summary>
/// <param name="Key">The map key, which is also the value of the record's own <c>PowerSocket_</c> leaf.</param>
/// <param name="Kind">How the key was recognised.</param>
/// <param name="Location">The socket record itself.</param>
/// <param name="OwnerDeviceId">For a device-owned socket, the owning device GUID (the first 32 characters of the key).</param>
/// <param name="OwnerStatus">For a device-owned socket, whether the owning device was found; null otherwise.</param>
/// <param name="SocketNumber">For a device-owned socket, the trailing number (the outlet index on the owner).</param>
public sealed record PowerSocketNode(
    string Key, PowerSocketKind Kind, PowerLocation Location,
    string? OwnerDeviceId, PowerEndpointStatus? OwnerStatus, string? SocketNumber)
{
    /// <summary>For a level-placed socket, the actor name (last path segment); null otherwise.</summary>
    public string? LevelActorName => Kind == PowerSocketKind.LevelPlaced ? Key[(Key.LastIndexOf('.') + 1)..] : null;
}

/// <summary>
/// STORED relationship: socket <see cref="Socket"/> supplies device <see cref="DeviceId"/>.
/// The link is owned by exactly one record, the socket record (<see cref="OwningRecord"/>); the
/// device's own record carries no power fields, and no reciprocal record exists anywhere in the
/// saves, so there is nothing to keep in sync on the device side.
/// </summary>
public sealed record StoredPowerLink(
    PowerSocketNode Socket, PowerLinkSlot Slot, string DeviceId, PowerEndpointStatus DeviceStatus)
{
    /// <summary>The one record that stores this link.</summary>
    public PowerLocation OwningRecord => Socket.Location;

    /// <summary>True when the socket record and the device record are in different save files.</summary>
    public bool IsCrossSave => DeviceStatus == PowerEndpointStatus.ResolvedInOtherSave;
}

/// <summary>Where a live observation came from.</summary>
public enum PowerLiveSource
{
    /// <summary>No live data was supplied for this socket.</summary>
    None,
    /// <summary>The running game's <c>IsPowered()</c> answer via the live-editing channel.</summary>
    LiveGame,
}

/// <summary>LIVE state of one socket. <see cref="Powered"/> is null whenever nothing was observed.</summary>
public sealed record PowerLiveState(bool? Powered, PowerLiveSource Source)
{
    public static PowerLiveState Unobserved { get; } = new(null, PowerLiveSource.None);
}

/// <summary>Every value a prediction may take. Only <see cref="Unknown"/> exists because no
/// electrical rule (capacity, drain, priority, switch behavior) has been verified from data.</summary>
public enum PredictedPowerState
{
    Unknown,
}

/// <summary>PREDICTED power result: never derived from stored links alone.</summary>
public sealed record PredictedPowerResult(PredictedPowerState State, string Reason);

/// <summary>Severity of a validator finding.</summary>
public enum PowerIssueSeverity
{
    Info,
    Warning,
}

/// <summary>What a validator finding is about.</summary>
public enum PowerIssueKind
{
    /// <summary>A socket names a plugged-in or extra device that is in none of the supplied saves.</summary>
    LinkedDeviceMissing,
    /// <summary>A device-owned socket whose owning device is in none of the supplied saves (an orphaned socket record).</summary>
    SocketOwnerMissing,
    /// <summary>A socket key of neither known shape.</summary>
    UnrecognizedSocketKey,
    /// <summary>A link whose socket and device live in different save files (informational: normal for the hub save).</summary>
    CrossSaveLink,
    /// <summary>One device is named by more than one socket. Whether that is legal is not known.</summary>
    DeviceFedByMultipleSockets,
    /// <summary>The stored links loop back on themselves.</summary>
    LinkCycle,
    /// <summary>A device is plugged into a socket that it owns.</summary>
    SelfLink,
    /// <summary>A record uses the extra-powered-devices list, whose meaning is unverified.</summary>
    ExtraPoweredDevicesUsed,
    /// <summary>A group of linked things that never reaches a level-placed socket.</summary>
    DisconnectedBranch,
}

/// <summary>One validator finding, with the record that carries the evidence.</summary>
public sealed record PowerIssue(
    PowerIssueKind Kind, PowerIssueSeverity Severity, string Message,
    IReadOnlyList<string> SubjectIds, PowerLocation? Record);
