using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Editor for <c>VehicleMap</c> (region saves): each spawned vehicle actor stores
/// whether it is currently driveable and whether it has been destroyed. The two editable
/// booleans let a player restore a wrecked vehicle or prevent a vehicle from being
/// driven without physically moving it.
///
/// <para>Read-only display fields expose the vehicle's blueprint class, its persistent
/// ID string, and how many on-board container inventories it carries.</para>
///
/// <para>Schema: map key = actor spawn path (e.g.
/// <c>/Game/Maps/Facility_MFWest...VehicleSpawn_Forklift_C_3</c>);
/// value = StructProperty → PropertiesStruct with the following leaves:</para>
/// <list type="bullet">
///   <item><c>Class_</c> (SoftObjectProperty) – vehicle blueprint class path.</item>
///   <item><c>VehicleID_</c> (StrProperty) – stable runtime ID string.</item>
///   <item><c>Transform_</c> (StructProperty Transform) – world transform (read-only).</item>
///   <item><c>ContainerInventories_</c> (ArrayProperty) – on-board storage slots.</item>
///   <item><c>VehicleDriveable_</c> (BoolProperty) – whether the vehicle can be driven.</item>
///   <item><c>VehicleDestroyed_</c> (BoolProperty) – whether the vehicle is wrecked.</item>
/// </list>
/// </summary>
public sealed class VehicleMapFeature : WorldMapFeatureBase
{
    /// <summary>Leaf prefix for the vehicle blueprint class (SoftObjectProperty).</summary>
    private const string ClassPrefix = "Class_";

    /// <summary>Leaf prefix for the vehicle's persistent ID string (StrProperty).</summary>
    private const string VehicleIdPrefix = "VehicleID_";

    /// <summary>Leaf prefix for the on-board container inventory array (ArrayProperty).</summary>
    private const string ContainerInventoriesPrefix = "ContainerInventories_";

    /// <summary>Leaf prefix for the driveable flag (BoolProperty).</summary>
    private const string DriveablePrefix = "VehicleDriveable_";

    /// <summary>Leaf prefix for the destroyed flag (BoolProperty).</summary>
    private const string DestroyedPrefix = "VehicleDestroyed_";

    /// <inheritdoc/>
    public override string Id => "vehicles";

    /// <inheritdoc/>
    public override string MapName => "VehicleMap";

    /// <inheritdoc/>
    public override string DisplayName => "Vehicles";

    /// <inheritdoc/>
    public override string Description =>
        "Repair destroyed vehicles or toggle whether each vehicle can be driven.";

    /// <summary>
    /// Reads the vehicle entry struct into typed display/edit fields.
    /// </summary>
    /// <param name="props">The property list from the vehicle's StructProperty value.</param>
    /// <returns>
    /// Five fields: <c>class</c> (read-only), <c>vehicleId</c> (read-only),
    /// <c>inventories</c> count (read-only), <c>driveable</c> (editable bool),
    /// and <c>destroyed</c> (editable bool).
    /// </returns>
    protected override IReadOnlyList<WorldMapField> ReadFields(IList<FPropertyTag> props)
    {
        // Read-only: blueprint class path (SoftObjectProperty renders as its path string).
        var classValue = props.GetString(ClassPrefix);

        // Read-only: persistent vehicle ID string.
        var vehicleId = props.GetString(VehicleIdPrefix);

        // Read-only: number of on-board container inventories (may be absent → 0).
        var inventoryCount = props.FindByPrefix(ContainerInventoriesPrefix)?.Property is ArrayProperty arr
            ? arr.Value?.Length ?? 0
            : 0;

        // Editable: true = vehicle can be driven; false = vehicle is immobile.
        var driveable = props.TryGetBool(DriveablePrefix) ?? false;

        // Editable: true = vehicle is wrecked; set false to "repair" it.
        var destroyed = props.TryGetBool(DestroyedPrefix) ?? false;

        return new[]
        {
            WorldMapField.ReadOnly("class", "Blueprint class", classValue,
                hint: "Vehicle blueprint class soft-object path (e.g. /Game/Blueprints/...Forklift_C)."),
            WorldMapField.ReadOnly("vehicleId", "Vehicle ID", vehicleId,
                hint: "Stable runtime ID string assigned to this vehicle spawn."),
            WorldMapField.ReadOnly("inventories", "Container inventories", inventoryCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
                hint: "Number of on-board storage containers attached to this vehicle."),
            WorldMapField.Bool("driveable", "Driveable", driveable,
                hint: "true = vehicle can be driven; false = vehicle is immobile (e.g. needs fuel)."),
            WorldMapField.Bool("destroyed", "Destroyed", destroyed,
                hint: "true = vehicle is wrecked; set to false to repair it without physical interaction."),
        };
    }

    /// <summary>
    /// Applies a user edit to one field of a vehicle entry.
    /// Only <c>driveable</c> and <c>destroyed</c> are settable; all others return
    /// <see cref="WorldEditResult.Failure(string)"/>.
    /// </summary>
    /// <param name="props">The property list for the entry being edited.</param>
    /// <param name="fieldId">The field identifier (case-insensitive).</param>
    /// <param name="value">The new value as a string (parsed as a bool).</param>
    protected override WorldEditResult ApplyField(IList<FPropertyTag> props, string fieldId, string? value)
    {
        // Read-only fields are never settable.
        if (string.Equals(fieldId, "class", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fieldId, "vehicleId", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fieldId, "inventories", StringComparison.OrdinalIgnoreCase))
        {
            return WorldEditResult.Failure($"field '{fieldId}' is read-only.");
        }

        if (string.Equals(fieldId, "driveable", StringComparison.OrdinalIgnoreCase))
        {
            if (!WorldMapAccessor.TryParseBool(value, out var wanted))
            {
                return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
            }
            var current = props.TryGetBool(DriveablePrefix) ?? false;
            if (current == wanted)
            {
                return WorldEditResult.NoChange;
            }
            return WorldMapAccessor.SetBool(props, DriveablePrefix, wanted)
                ? WorldEditResult.Success
                : WorldEditResult.Failure("the VehicleDriveable field is missing from this vehicle entry.");
        }

        if (string.Equals(fieldId, "destroyed", StringComparison.OrdinalIgnoreCase))
        {
            if (!WorldMapAccessor.TryParseBool(value, out var wanted))
            {
                return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
            }
            var current = props.TryGetBool(DestroyedPrefix) ?? false;
            if (current == wanted)
            {
                return WorldEditResult.NoChange;
            }
            return WorldMapAccessor.SetBool(props, DestroyedPrefix, wanted)
                ? WorldEditResult.Success
                : WorldEditResult.Failure("the VehicleDestroyed field is missing from this vehicle entry.");
        }

        return WorldEditResult.Failure($"unknown field '{fieldId}' (expected: driveable, destroyed).");
    }
}
