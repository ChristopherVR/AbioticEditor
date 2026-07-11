using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.WorldSaves;

// WorldSaveReader - vehicle reads (state, transform, on-board storage).
public static partial class WorldSaveReader
{
    /// <summary>
    /// Reads <c>VehicleMap</c> (region saves): spawned vehicle actors with class, transform,
    /// driveable/destroyed flags, and on-board inventory count. The on-board storage itself is
    /// surfaced as <see cref="WorldContainerSource.Vehicle"/> containers (see
    /// <see cref="ReadVehicleContainers"/>) so it reuses the full container slot editor.
    /// </summary>
    private static List<WorldVehicle> ReadVehicles(SaveGame save)
    {
        var result = new List<WorldVehicle>();
        var pairs = GetMapPairs(save.Properties, "VehicleMap");
        if (pairs is null) return result;

        foreach (var kvp in pairs)
        {
            var id = ExtractMapKeyString(kvp.Key);
            if (id is null) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var p = ps.Properties;
            var vehicleId = p.GetString("VehicleID_");
            var vehicleClass = p.FindByPrefix("Class_")?.Property?.Value?.ToString();
            var driveable = p.TryGetBool("VehicleDriveable_") ?? false;
            var destroyed = p.TryGetBool("VehicleDestroyed_") ?? false;
            var (x, y, z, qx, qy, qz, qw) = ReadTransform(p);

            var inventories = ReadContainerInventoriesArray(p);
            var itemCount = inventories.Sum(inv => inv.Slots.Count(s => !s.IsEmpty && s.ItemId != "Empty"));

            result.Add(new WorldVehicle(
                id, vehicleId, vehicleClass, driveable, destroyed,
                x, y, z, qx, qy, qz, qw, itemCount, inventories.Count > 0));
        }
        return result;
    }

    /// <summary>Vehicle on-board storage as editable containers (mirrors <see cref="ReadDeployedContainers"/>).</summary>
    private static IEnumerable<WorldContainer> ReadVehicleContainers(SaveGame save)
    {
        var pairs = GetMapPairs(save.Properties, "VehicleMap");
        if (pairs is null) yield break;

        foreach (var kvp in pairs)
        {
            var key = ExtractMapKeyString(kvp.Key);
            if (key is null) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var inventories = ReadContainerInventoriesArray(ps.Properties);
            if (inventories.Count == 0) continue;

            var className = ExtractClassName(ps.Properties);
            yield return new WorldContainer(key, WorldContainerSource.Vehicle, className, inventories);
        }
    }

    /// <summary>Reads a <c>Transform_</c> struct's translation (vector) and rotation (quaternion).</summary>
    private static (double X, double Y, double Z, double QX, double QY, double QZ, double QW) ReadTransform(
        IList<FPropertyTag> props)
    {
        double x = 0, y = 0, z = 0, qx = 0, qy = 0, qz = 0, qw = 1;
        if (props.FindByPrefix("Transform_")?.Property is StructProperty tsp && tsp.Value is PropertiesStruct tps)
        {
            if (tps.Properties.FindByPrefix("Translation")?.Property is StructProperty trsp && trsp.Value is VectorStruct vec)
            {
                x = vec.Value.X;
                y = vec.Value.Y;
                z = vec.Value.Z;
            }
            if (tps.Properties.FindByPrefix("Rotation")?.Property is StructProperty rsp && rsp.Value is QuatStruct q)
            {
                qx = q.Value.X;
                qy = q.Value.Y;
                qz = q.Value.Z;
                qw = q.Value.W;
            }
        }
        return (x, y, z, qx, qy, qz, qw);
    }
}
