using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Live VEHICLES editing session, implementing the same <see cref="IWorldVehiclesSession"/> the
/// file session does so <c>WorldVehiclesTab</c> renders unchanged for either host. Whether a
/// vehicle is wrecked has no evidenced live property (see <c>areas/vehicles.lua</c>'s own
/// comment): every mapped <see cref="AbioticEditor.Core.WorldSaves.WorldVehicle"/> reports
/// <c>Destroyed = false</c> and <see cref="SupportsWreckedState"/> is always false, so the
/// shared tab hides that control rather than showing a value this session cannot read or write.
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

    private void Apply(LiveVehicleDirectory directory)
    {
        // Destroyed/wrecked has no evidenced live property (see areas/vehicles.lua) - every
        // mapped vehicle reports false, and SupportsWreckedState below tells the shared tab to
        // hide that control rather than show a value this session cannot actually read.
        Vehicles = directory.Vehicles
            .Select(v => new WorldVehicle(v.Id, v.VehicleId, v.VehicleClass, v.Driveable, Destroyed: false,
                v.X, v.Y, v.Z, QuatX: 0, QuatY: 0, QuatZ: 0, QuatW: 1, InventoryItemCount: 0, HasInventory: false))
            .ToList();
        IsHost = directory.IsHost;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
        => Apply(await _channel.GetAsync(cancellationToken).ConfigureAwait(false));

    public async Task SetVehicleAsync(string id, bool driveable, double x, double y, double z,
        CancellationToken cancellationToken = default)
    {
        await _channel.SetAsync(id, driveable, x, y, z, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    bool IWorldVehiclesSession.AppliesImmediately => true;
    bool IWorldVehiclesSession.SupportsWreckedState => false;
    Task IWorldVehiclesSession.SetVehicleAsync(string id, bool driveable, bool destroyed, double x, double y, double z,
        CancellationToken cancellationToken)
        => SetVehicleAsync(id, driveable, x, y, z, cancellationToken);
}
