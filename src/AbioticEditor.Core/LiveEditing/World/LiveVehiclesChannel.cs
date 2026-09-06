namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live vehicle editing: lists every vehicle currently loaded (<c>ABF_Vehicle_ParentBP</c> and
/// its subclasses) and lets a host toggle whether it's driveable, whether it's wrecked, and move
/// it - see <c>vehicles.list</c>/<c>vehicles.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/vehicles.lua</c>. Wrecked is grounded in
/// the class's own <c>PendingDestroy</c> property (round 77); whether flipping it alone updates
/// the vehicle's wreck visuals live is unverified against the running game - see that module's
/// comment.
/// </summary>
public sealed class LiveVehiclesChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveVehicleDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("vehicles.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var vehicles = (wire.Vehicles ?? [])
            .Select(v => new LiveVehicle(v.Id, v.VehicleId, v.VehicleClass, v.Driveable, v.Wrecked, v.X, v.Y, v.Z))
            .ToList();
        return new LiveVehicleDirectory(vehicles, wire.IsHost, wire.SupportsWreckedState);
    }

    /// <summary>Updates a vehicle's driveable/wrecked state and/or position immediately. Host only.</summary>
    public Task SetAsync(string id, bool? driveable, bool? wrecked, double? x, double? y, double? z,
        CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("vehicles.set", new SetWire(id, driveable, wrecked, x, y, z), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<VehicleWire>? Vehicles, bool IsHost, bool SupportsWreckedState);
    private sealed record VehicleWire(string Id, string? VehicleId, string? VehicleClass, bool Driveable, bool Wrecked,
        double X, double Y, double Z);
    private sealed record SetWire(string Id, bool? Driveable, bool? Wrecked, double? X, double? Y, double? Z);
}

/// <summary>One loaded vehicle. <paramref name="Id"/> is the game's own full object name for this
/// exact actor; <paramref name="VehicleClass"/> is its class name (e.g.
/// <c>ABF_Vehicle_Forklift_C</c>).</summary>
public sealed record LiveVehicle(string Id, string? VehicleId, string? VehicleClass, bool Driveable, bool Wrecked,
    double X, double Y, double Z);

/// <summary>Every loaded vehicle, whether this process has host authority to change them, and
/// whether the wrecked/destroyed state can be edited live (yes, since round 77 - grounded in
/// <c>PendingDestroy</c>, see the module's own comment for what remains unverified).</summary>
public sealed record LiveVehicleDirectory(IReadOnlyList<LiveVehicle> Vehicles, bool IsHost, bool SupportsWreckedState);
