using AbioticEditor.Core.LiveEditing.World;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>Which way a trace walks the stored links.</summary>
public enum PowerTraceDirection
{
    /// <summary>Toward what this thing supplies: the devices plugged into the sockets it owns, and onward.</summary>
    Downstream,
    /// <summary>Toward what supplies this thing: the socket it is plugged into, that socket's owner, and onward.</summary>
    Upstream,
}

/// <summary>Why a trace stopped or continued at the far end of a hop.</summary>
public enum PowerTraceOutcome
{
    /// <summary>The trace continues past this hop's far end.</summary>
    Continues,
    /// <summary>The far end has no further stored links in this direction.</summary>
    NoFurtherLinks,
    /// <summary>The far end was already reached earlier in this trace (a loop or a shared branch); not expanded again.</summary>
    RevisitsEarlierNode,
    /// <summary>Upstream only: the supplying socket is a level-placed socket, the outermost thing the saves describe.</summary>
    EndsAtLevelSocket,
    /// <summary>The far end is a record that is in none of the supplied saves.</summary>
    EndpointMissing,
    /// <summary>The trace hit its size limit.</summary>
    LimitReached,
}

/// <summary>One step of a trace: a stored link and what happened at its far end.</summary>
public sealed record PowerTraceHop(int Depth, StoredPowerLink Link, PowerTraceOutcome Outcome);

/// <summary>An upstream or downstream walk of the STORED links from a starting device or socket.</summary>
public sealed record PowerTrace(
    string StartId, PowerTraceDirection Direction, IReadOnlyList<PowerTraceHop> Hops, bool Truncated);

/// <summary>
/// The stored power network of one or more world saves: which socket supplies which device, and
/// which device owns which socket. Built by <see cref="PowerGraphBuilder"/>. Everything here is
/// STORED data; live and predicted results are separate types (see <see cref="PowerLiveOverlay"/>
/// and <see cref="Predict"/>).
/// </summary>
public sealed class PowerGraph
{
    private readonly Dictionary<string, PowerDeviceNode> _devices;
    private readonly Dictionary<string, PowerSocketNode> _sockets;
    private readonly List<StoredPowerLink> _links;
    private readonly Dictionary<string, List<PowerSocketNode>> _socketsByOwner;
    private readonly Dictionary<string, List<StoredPowerLink>> _linksBySocket;
    private readonly Dictionary<string, List<StoredPowerLink>> _linksIntoDevice;

    internal PowerGraph(
        IReadOnlyList<string> savesIncluded,
        Dictionary<string, PowerDeviceNode> devices,
        Dictionary<string, PowerSocketNode> sockets,
        List<StoredPowerLink> links)
    {
        SavesIncluded = savesIncluded;
        _devices = devices;
        _sockets = sockets;
        _links = links;
        _socketsByOwner = new Dictionary<string, List<PowerSocketNode>>(StringComparer.OrdinalIgnoreCase);
        foreach (var socket in sockets.Values.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            if (socket.OwnerDeviceId is { } owner)
            {
                Add(_socketsByOwner, owner, socket);
            }
        }
        _linksBySocket = new Dictionary<string, List<StoredPowerLink>>(StringComparer.Ordinal);
        _linksIntoDevice = new Dictionary<string, List<StoredPowerLink>>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            Add(_linksBySocket, link.Socket.Key, link);
            Add(_linksIntoDevice, link.DeviceId, link);
        }
    }

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }
        list.Add(value);
    }

    /// <summary>The save files that were read to build this graph. An endpoint missing from all of
    /// them may still exist in a save that was not supplied.</summary>
    public IReadOnlyList<string> SavesIncluded { get; }

    /// <summary>Every device that a socket or a link mentions, plus every device that owns a socket.
    /// Devices in none of the supplied saves appear as placeholders (<see cref="PowerDeviceNode.IsMissing"/>).</summary>
    public IReadOnlyCollection<PowerDeviceNode> Devices => _devices.Values;

    /// <summary>Every socket record.</summary>
    public IReadOnlyCollection<PowerSocketNode> Sockets => _sockets.Values;

    /// <summary>Every stored link (one per non-empty plugged-in id or extra-powered id).</summary>
    public IReadOnlyList<StoredPowerLink> Links => _links;

    public PowerDeviceNode? FindDevice(string id) => _devices.GetValueOrDefault(id);

    public PowerSocketNode? FindSocket(string key) => _sockets.GetValueOrDefault(key);

    /// <summary>The sockets a device owns, ordered by key.</summary>
    public IReadOnlyList<PowerSocketNode> SocketsOwnedBy(string deviceId)
        => _socketsByOwner.TryGetValue(deviceId, out var list) ? list : [];

    /// <summary>The stored links that a socket carries (at most its plugged-in device plus its extra devices).</summary>
    public IReadOnlyList<StoredPowerLink> LinksOfSocket(string socketKey)
        => _linksBySocket.TryGetValue(socketKey, out var list) ? list : [];

    /// <summary>The links that name this device as the supplied party (the sockets it is plugged into).</summary>
    public IReadOnlyList<StoredPowerLink> LinksIntoDevice(string deviceId)
        => _linksIntoDevice.TryGetValue(deviceId, out var list) ? list : [];

    /// <summary>The links leaving the sockets a device owns (what it supplies).</summary>
    public IReadOnlyList<StoredPowerLink> LinksOutOfDevice(string deviceId)
        => SocketsOwnedBy(deviceId).SelectMany(s => LinksOfSocket(s.Key)).ToList();

    /// <summary>
    /// Walks the stored links from <paramref name="startId"/> (a device GUID or a socket key).
    /// Loops and shared branches are reported once as <see cref="PowerTraceOutcome.RevisitsEarlierNode"/>
    /// and not expanded again, so the walk always ends.
    /// </summary>
    public PowerTrace Trace(string startId, PowerTraceDirection direction, int maxHops = 500)
    {
        var hops = new List<PowerTraceHop>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { startId };
        var truncated = false;

        void Down(string deviceOrSocket, int depth)
        {
            var links = _sockets.ContainsKey(deviceOrSocket) ? LinksOfSocket(deviceOrSocket) : LinksOutOfDevice(deviceOrSocket);
            foreach (var link in links)
            {
                if (hops.Count >= maxHops)
                {
                    truncated = true;
                    return;
                }
                var outcome = LinkOutcomeDown(link, visited);
                hops.Add(new PowerTraceHop(depth, link, outcome));
                if (outcome == PowerTraceOutcome.Continues)
                {
                    Down(link.DeviceId, depth + 1);
                }
            }
        }

        void Up(string deviceId, int depth)
        {
            foreach (var link in LinksIntoDevice(deviceId))
            {
                if (hops.Count >= maxHops)
                {
                    truncated = true;
                    return;
                }
                string? nextDevice = null;
                PowerTraceOutcome outcome;
                if (link.Socket.Kind == PowerSocketKind.LevelPlaced)
                {
                    outcome = PowerTraceOutcome.EndsAtLevelSocket;
                }
                else if (link.Socket.OwnerDeviceId is { } owner)
                {
                    if (link.Socket.OwnerStatus == PowerEndpointStatus.MissingInSuppliedSaves)
                    {
                        outcome = PowerTraceOutcome.EndpointMissing;
                    }
                    else if (!visited.Add(owner))
                    {
                        outcome = PowerTraceOutcome.RevisitsEarlierNode;
                    }
                    else
                    {
                        nextDevice = owner;
                        outcome = LinksIntoDevice(owner).Count == 0 ? PowerTraceOutcome.NoFurtherLinks : PowerTraceOutcome.Continues;
                    }
                }
                else
                {
                    outcome = PowerTraceOutcome.NoFurtherLinks;
                }
                hops.Add(new PowerTraceHop(depth, link, outcome));
                if (nextDevice is not null && outcome == PowerTraceOutcome.Continues)
                {
                    Up(nextDevice, depth + 1);
                }
            }
        }

        if (direction == PowerTraceDirection.Downstream)
        {
            Down(startId, 0);
        }
        else if (_sockets.TryGetValue(startId, out var startSocket))
        {
            // A socket has no incoming link of its own; what supplies it is its owner.
            if (startSocket.OwnerDeviceId is { } owner && startSocket.OwnerStatus != PowerEndpointStatus.MissingInSuppliedSaves)
            {
                visited.Add(owner);
                Up(owner, 0);
            }
        }
        else
        {
            Up(startId, 0);
        }
        return new PowerTrace(startId, direction, hops, truncated);
    }

    private PowerTraceOutcome LinkOutcomeDown(StoredPowerLink link, HashSet<string> visited)
    {
        if (link.DeviceStatus == PowerEndpointStatus.MissingInSuppliedSaves)
        {
            return PowerTraceOutcome.EndpointMissing;
        }
        if (!visited.Add(link.DeviceId))
        {
            return PowerTraceOutcome.RevisitsEarlierNode;
        }
        return LinksOutOfDevice(link.DeviceId).Count == 0 ? PowerTraceOutcome.NoFurtherLinks : PowerTraceOutcome.Continues;
    }

    /// <summary>
    /// PREDICTED power result for a device or socket. Always <see cref="PredictedPowerState.Unknown"/>:
    /// the saves store connections, not capacity, drain, priority or switch behavior, and none of
    /// those rules has been verified, so the editor does not invent an answer.
    /// </summary>
    public static PredictedPowerResult Predict(string id)
        => new(PredictedPowerState.Unknown,
            "The save stores which socket supplies which device, but the game's power rules "
            + "(battery drain, capacity, ordering, switches) are not verified, so no power result is predicted.");

    /// <summary>The LIVE state of a socket from a live overlay, or unobserved when the overlay has nothing.</summary>
    public static PowerLiveState LiveStateOf(string socketKey, PowerLiveOverlay? overlay)
        => overlay?.For(socketKey) ?? PowerLiveState.Unobserved;
}

/// <summary>
/// LIVE socket states from the running game, keyed by socket id. Matching a live socket to a
/// stored one relies on the live <c>GetPowerSocketID()</c> value being the same string as the
/// save's map key; that equality is documented but has not been confirmed in a running game.
/// </summary>
public sealed class PowerLiveOverlay
{
    private readonly Dictionary<string, PowerLiveState> _byId;

    private PowerLiveOverlay(Dictionary<string, PowerLiveState> byId) => _byId = byId;

    /// <summary>Builds an overlay from one live socket directory read.</summary>
    public static PowerLiveOverlay From(LivePowerSocketDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var byId = new Dictionary<string, PowerLiveState>(StringComparer.OrdinalIgnoreCase);
        foreach (var socket in directory.Sockets)
        {
            if (!string.IsNullOrEmpty(socket.SocketId))
            {
                byId[socket.SocketId] = new PowerLiveState(socket.Powered, PowerLiveSource.LiveGame);
            }
        }
        return new PowerLiveOverlay(byId);
    }

    /// <summary>The observed state, or null when this overlay never saw the socket.</summary>
    public PowerLiveState? For(string socketKey) => _byId.GetValueOrDefault(socketKey);
}
