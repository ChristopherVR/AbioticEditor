namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// One entry of a region save's <c>VehicleMap</c> - a spawned vehicle actor (forklift,
/// security cart, etc.). The map key and <see cref="VehicleId"/> are the spawner actor's
/// object path, which is also how the editor resolves the original spawn transform for
/// "reset to spawn".
/// </summary>
/// <param name="Id">Map key - the <c>VehicleSpawn_*</c> actor object path.</param>
/// <param name="VehicleId">The stored <c>VehicleID_</c> string (normally equal to <paramref name="Id"/>).</param>
/// <param name="VehicleClass">Blueprint class soft path, e.g.
/// <c>/Game/Blueprints/Vehicles/ABF_Vehicle_Forklift.ABF_Vehicle_Forklift_C</c>.</param>
/// <param name="Driveable">Whether the vehicle can currently be driven ("unlocked").</param>
/// <param name="Destroyed">Whether the vehicle is wrecked.</param>
/// <param name="InventoryItemCount">Non-empty on-board item count (0 when it carries no storage).</param>
/// <param name="HasInventory">Whether the vehicle has any on-board container.</param>
/// <param name="ContainerId">
/// The id to open in the CONTAINERS tab for this vehicle's on-board storage, or null when
/// <paramref name="HasInventory"/> is false. Null (the default) also covers the file session,
/// where the container is embedded in this same <c>VehicleMap</c> entry and so shares this
/// record's own <paramref name="Id"/> - <c>WorldVehiclesTab</c> falls back to <see cref="Id"/>
/// when this is null. The live session sets this explicitly: the on-board container is a
/// separate actor there (the vehicle's <c>StorageContainer</c> ChildActorComponent's resolved
/// child, a real <c>Deployed_Container_ParentBP_C</c> instance), with its own, different id - see
/// <c>LiveVehiclesSession</c>.
/// </param>
public sealed record WorldVehicle(
    string Id,
    string? VehicleId,
    string? VehicleClass,
    bool Driveable,
    bool Destroyed,
    double X,
    double Y,
    double Z,
    double QuatX,
    double QuatY,
    double QuatZ,
    double QuatW,
    int InventoryItemCount,
    bool HasInventory,
    string? ContainerId = null)
{
    /// <summary>Class tail without the <c>_C</c> suffix (e.g. <c>ABF_Vehicle_Forklift</c>).</summary>
    public string ShortClass
    {
        get
        {
            if (string.IsNullOrEmpty(VehicleClass)) return string.Empty;
            var tail = VehicleClass![(VehicleClass.LastIndexOf('.') + 1)..];
            return tail.EndsWith("_C", StringComparison.Ordinal) ? tail[..^2] : tail;
        }
    }

    /// <summary>Friendly name from the catalog (e.g. "Forklift"), else the class tail.</summary>
    public string DisplayName => VehicleCatalog.FriendlyName(VehicleClass) ?? ShortClass;

    /// <summary>
    /// The region/level this vehicle lives in, parsed from the actor path
    /// (e.g. <c>Facility_MFWest</c> from <c>/Game/Maps/Facility_MFWest.Facility_MFWest:...</c>).
    /// </summary>
    public string Region
    {
        get
        {
            if (string.IsNullOrEmpty(Id)) return string.Empty;
            var maps = Id.IndexOf("/Maps/", StringComparison.OrdinalIgnoreCase);
            if (maps < 0) return string.Empty;
            var rest = Id[(maps + "/Maps/".Length)..];
            var dot = rest.IndexOf('.');
            return dot >= 0 ? rest[..dot] : rest;
        }
    }
}
