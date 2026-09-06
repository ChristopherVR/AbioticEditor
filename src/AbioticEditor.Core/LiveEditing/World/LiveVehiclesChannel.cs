namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live vehicle editing: lists every vehicle currently loaded (<c>ABF_Vehicle_ParentBP</c> and
/// its subclasses) and lets a host toggle whether it's driveable and move it - see
/// <c>vehicles.list</c>/<c>vehicles.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/vehicles.lua</c>. Whether a vehicle is
/// wrecked has no evidenced live property (see that module's own comment) and is never reported
/// by this channel; <see cref="LiveVehicleDirectory.SupportsWreckedState"/> is always false.
/// </summary>
public sealed class LiveVehiclesChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveVehicleDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("vehicles.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var vehicles = (wire.Vehicles ?? [])
            .Select(v => new LiveVehicle(v.Id, v.VehicleId, v.VehicleClass, v.Driveable, v.X, v.Y, v.Z))
            .ToList();
        return new LiveVehicleDirectory(vehicles, wire.IsHost, wire.SupportsWreckedState);
    }

    /// <summary>Updates a vehicle's driveable state and/or position immediately. Host only.</summary>
    public Task SetAsync(string id, bool? driveable, double? x, double? y, double? z,
        CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("vehicles.set", new SetWire(id, driveable, x, y, z), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<VehicleWire>? Vehicles, bool IsHost, bool SupportsWreckedState);
    private sealed record VehicleWire(string Id, string? VehicleId, string? VehicleClass, bool Driveable,
        double X, double Y, double Z);
    private sealed record SetWire(string Id, bool? Driveable, double? X, double? Y, double? Z);
}

/// <summary>One loaded vehicle. <paramref name="Id"/> is the game's own full object name for this
/// exact actor; <paramref name="VehicleClass"/> is its class name (e.g.
/// <c>ABF_Vehicle_Forklift_C</c>).</summary>
public sealed record LiveVehicle(string Id, string? VehicleId, string? VehicleClass, bool Driveable,
    double X, double Y, double Z);

/// <summary>Every loaded vehicle, whether this process has host authority to change them, and
/// whether the wrecked/destroyed state can be edited live (never, today - see the module's own
/// comment).</summary>
public sealed record LiveVehicleDirectory(IReadOnlyList<LiveVehicle> Vehicles, bool IsHost, bool SupportsWreckedState);
