using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Host-neutral boundary for an open VEHICLES editing session, implemented by
/// <see cref="WorldSaveSession"/> (staged) and <see cref="LiveVehiclesSession"/> (immediate,
/// against a running game). See <see cref="IWorldBasesSession"/> for the pattern this copies.
/// </summary>
public interface IWorldVehiclesSession
{
    /// <summary>Every vehicle known to this session (staged edits included, for the file session).</summary>
    IReadOnlyList<WorldVehicle> Vehicles { get; }

    /// <summary>True when a mutator here takes effect in the running game immediately (live).</summary>
    bool AppliesImmediately { get; }

    /// <summary>
    /// False when this session has no confirmed way to change whether a vehicle is wrecked - the
    /// live vehicle blueprint exposes a direct <c>VehicleDriveable</c> property (grounded, see
    /// <c>LiveVehiclesChannel</c>) but the save's <c>Destroyed</c> flag is computed at save time
    /// from state this probe found no exposed live property for, so the "wrecked" checkbox hides
    /// itself live rather than silently doing nothing.
    /// </summary>
    bool SupportsWreckedState { get; }

    /// <summary>
    /// Updates a vehicle. <paramref name="destroyed"/> is ignored by a session whose
    /// <see cref="SupportsWreckedState"/> is false.
    /// </summary>
    Task SetVehicleAsync(string id, bool driveable, bool destroyed, double x, double y, double z,
        CancellationToken cancellationToken = default);
}
