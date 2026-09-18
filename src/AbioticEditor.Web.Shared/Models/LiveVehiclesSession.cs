using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live VEHICLES editing session, implementing the same <see cref="IWorldVehiclesSession"/> the
/// file session does so <c>WorldVehiclesTab</c> renders unchanged for either host. Wrecked is
/// grounded in the vehicle's own <c>PendingDestroy</c> property (round 77, see
/// <c>areas/vehicles.lua</c>'s own comment) - read/write both go through it, but whether flipping
/// it alone updates the vehicle's wreck visuals live (versus only the value the save later
/// persists) is unverified against the running game. On-board storage's <c>HasInventory</c>/
/// <c>InventoryItemCount</c> are real (coordinator round, see <c>vehicles.lua</c>'s own comment
/// for the class-layout evidence): the "open storage" button reuses <see cref="WorldVehicle.ContainerId"/>,
/// which <c>WorldContainersTab</c>'s existing live <see cref="LiveContainersSession"/> already
/// serves unchanged - no vehicle-specific container code exists anywhere in this stack.
/// </summary>
public sealed class LiveVehiclesSession : IWorldVehiclesSession
{
    private readonly LiveVehiclesChannel _channel;

    private LiveVehiclesSession(LiveVehiclesChannel channel, LiveVehicleDirectory directory)
    {
        _channel = channel;
        Apply(directory);
    }

    public static async Task<LiveVehiclesSession> ConnectAsync(
        LiveVehiclesChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveVehiclesSession(channel, directory);
    }

    public IReadOnlyList<WorldVehicle> Vehicles { get; private set; } = [];
    public bool IsHost { get; private set; }
    public string? Status { get; private set; }

    /// <summary>Always false: a vehicle edit already reached the running game by the time it
    /// returns, so there is never a client-side staged copy.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world and after every
    /// mutation (each of which already ends by refreshing).</summary>
    public event Action? Changed;

    private void Apply(LiveVehicleDirectory directory)
    {
        Vehicles = directory.Vehicles
            .Select(v => new WorldVehicle(v.Id, v.VehicleId, v.VehicleClass, v.Driveable, v.Wrecked,
                v.X, v.Y, v.Z, QuatX: 0, QuatY: 0, QuatZ: 0, QuatW: 1,
                InventoryItemCount: v.InventoryItemCount, HasInventory: v.HasInventory, ContainerId: v.ContainerId))
            .ToList();
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)), which requires a true no-parameter method -
    // the optional parameter below does not count. Without this the loop silently never refreshes
    // this session, so a vehicle moved/changed in the running game never shows up here on its own.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
        => Apply(await _channel.GetAsync(cancellationToken).ConfigureAwait(false));

    public async Task SetVehicleAsync(string id, bool driveable, bool wrecked, double x, double y, double z,
        CancellationToken cancellationToken = default)
    {
        await _channel.SetAsync(id, driveable, wrecked, x, y, z, cancellationToken).ConfigureAwait(false);
        Status = null;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    bool IWorldVehiclesSession.AppliesImmediately => true;
    bool IWorldVehiclesSession.SupportsWreckedState => true;
    Task IWorldVehiclesSession.SetVehicleAsync(string id, bool driveable, bool destroyed, double x, double y, double z,
        CancellationToken cancellationToken)
        => SetVehicleAsync(id, driveable, destroyed, x, y, z, cancellationToken);
}
