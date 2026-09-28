namespace AbioticEditor.Core.WorldSaves;

/// <summary>Which editor surface owns a navigation target.</summary>
public enum PowerNavigationSurface
{
    /// <summary>The Power Sockets list of the named save (<c>PowerSocketMap</c>).</summary>
    PowerSockets,
    /// <summary>A deployable in the named save (<c>DeployedObjectMap</c>); a container can be opened in the CONTAINERS tab.</summary>
    Deployable,
}

/// <summary>Where the editor can take the user to see one end of a connection. The file may be a
/// different save than the one being inspected; a null <see cref="FileName"/> never happens for a
/// resolved endpoint, and a missing endpoint has no target at all.</summary>
public sealed record PowerNavigationTarget(
    PowerNavigationSurface Surface, string FileName, string Key, string Label, bool IsContainer);

/// <summary>One connection of an inspected thing, seen from that thing.</summary>
/// <param name="Link">The stored link (its owning record is <see cref="StoredPowerLink.OwningRecord"/>).</param>
/// <param name="IsOutgoing">True when the inspected thing supplies the far end; false when the far end supplies it.</param>
/// <param name="FarEndLabel">A readable name for the other end.</param>
/// <param name="FarEndStatus">Whether the other end was found.</param>
/// <param name="FarEnd">Navigation target for the other end, or null when it is missing.</param>
/// <param name="SocketRecord">Navigation target for the socket record that stores the link.</param>
public sealed record PowerConnection(
    StoredPowerLink Link, bool IsOutgoing, string FarEndLabel, PowerEndpointStatus FarEndStatus,
    PowerNavigationTarget? FarEnd, PowerNavigationTarget SocketRecord);

/// <summary>Everything the inspector shows for one device or socket. The three kinds of power
/// statement are separate members: <see cref="Connections"/> is STORED, <see cref="Live"/> is LIVE,
/// <see cref="Prediction"/> is PREDICTED.</summary>
public sealed record PowerInspection(
    string Id, string Label, PowerDeviceNode? Device, PowerSocketNode? Socket,
    IReadOnlyList<PowerConnection> Connections,
    IReadOnlyList<PowerSocketNode> OwnedSockets,
    IReadOnlyList<PowerIssue> Issues,
    PowerLiveState Live,
    PredictedPowerResult Prediction);

/// <summary>
/// Read-only inspector: lists a device's or socket's stored connections with navigation targets to
/// each endpoint (including endpoints in other save files) and the validator findings that mention it.
/// </summary>
public static class PowerConnectionInspector
{
    /// <summary>Inspects a device GUID or a socket key. Returns null when the id names neither.</summary>
    public static PowerInspection? Inspect(
        PowerGraph graph, string id, IReadOnlyList<PowerIssue>? issues = null, PowerLiveOverlay? live = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var allIssues = issues ?? PowerGraphValidator.Validate(graph);
        var relevantIssues = allIssues
            .Where(i => i.SubjectIds.Any(s => string.Equals(s, id, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (graph.FindSocket(id) is { } socket)
        {
            var connections = new List<PowerConnection>();
            foreach (var link in graph.LinksOfSocket(socket.Key))
            {
                connections.Add(Outgoing(graph, link));
            }
            return new PowerInspection(socket.Key, SocketLabel(graph, socket), null, socket, connections, [],
                relevantIssues, PowerGraph.LiveStateOf(socket.Key, live), PowerGraph.Predict(socket.Key));
        }

        if (graph.FindDevice(id) is { } device)
        {
            var connections = new List<PowerConnection>();
            foreach (var link in graph.LinksIntoDevice(device.Id))
            {
                connections.Add(Incoming(graph, link));
            }
            foreach (var link in graph.LinksOutOfDevice(device.Id))
            {
                connections.Add(Outgoing(graph, link));
            }
            return new PowerInspection(device.Id, device.FriendlyName, device, null, connections,
                graph.SocketsOwnedBy(device.Id), relevantIssues, PowerLiveState.Unobserved, PowerGraph.Predict(device.Id));
        }
        return null;
    }

    /// <summary>Navigation target for a device, or null when the device is missing.</summary>
    public static PowerNavigationTarget? TargetOf(PowerDeviceNode device)
        => device.Location is { } location
            ? new PowerNavigationTarget(PowerNavigationSurface.Deployable, location.FileName, location.Key,
                device.FriendlyName, device.IsContainer)
            : null;

    /// <summary>Navigation target for a socket record.</summary>
    public static PowerNavigationTarget TargetOf(PowerGraph graph, PowerSocketNode socket)
        => new(PowerNavigationSurface.PowerSockets, socket.Location.FileName, socket.Key,
            SocketLabel(graph, socket), false);

    private static PowerConnection Outgoing(PowerGraph graph, StoredPowerLink link)
    {
        var device = graph.FindDevice(link.DeviceId);
        return new PowerConnection(link, true,
            device is { IsMissing: false } ? device.FriendlyName : "Missing device",
            link.DeviceStatus, device is null ? null : TargetOf(device), TargetOf(graph, link.Socket));
    }

    private static PowerConnection Incoming(PowerGraph graph, StoredPowerLink link)
    {
        // Far end = whatever owns the supplying socket; for a level-placed socket, the socket itself.
        PowerNavigationTarget? farEnd;
        string label;
        PowerEndpointStatus status;
        if (link.Socket.OwnerDeviceId is { } ownerId)
        {
            var owner = graph.FindDevice(ownerId);
            farEnd = owner is null ? null : TargetOf(owner);
            label = owner is { IsMissing: false } ? owner.FriendlyName : "Missing device";
            status = link.Socket.OwnerStatus ?? PowerEndpointStatus.MissingInSuppliedSaves;
        }
        else
        {
            farEnd = TargetOf(graph, link.Socket);
            label = SocketLabel(graph, link.Socket);
            status = PowerEndpointStatus.Resolved;
        }
        return new PowerConnection(link, false, label, status, farEnd, TargetOf(graph, link.Socket));
    }

    private static string SocketLabel(PowerGraph graph, PowerSocketNode socket)
    {
        switch (socket.Kind)
        {
            case PowerSocketKind.LevelPlaced:
                return $"Level socket {socket.LevelActorName}";
            case PowerSocketKind.DeviceOwned:
                var owner = socket.OwnerDeviceId is null ? null : graph.FindDevice(socket.OwnerDeviceId);
                var name = owner is { IsMissing: false } ? owner.FriendlyName : "missing device";
                return $"Outlet {socket.SocketNumber} of {name}";
            default:
                return "Unrecognized socket";
        }
    }
}
