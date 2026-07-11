using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.WorldSaves;

// WorldSaveWriter - vehicle state edits (drivable/destroyed flags and world transform).
public static partial class WorldSaveWriter
{
    /// <summary>
    /// Patches <c>VehicleMap</c> entries by key: driveable / destroyed flags and the world
    /// transform (translation + rotation). On-board inventory is patched via
    /// <see cref="ApplyContainers"/> (vehicle containers). Untouched vehicles round-trip byte-perfect.
    /// </summary>
    public static void ApplyVehicles(WorldSaveData data, IEnumerable<WorldVehicle> updated)
    {
        var byId = updated.ToDictionary(v => v.Id, StringComparer.Ordinal);
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, "VehicleMap");
        if (pairs is null) return;

        foreach (var kvp in pairs)
        {
            var id = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (id is null || !byId.TryGetValue(id, out var vehicle)) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            SetBool(ps.Properties, "VehicleDriveable_", vehicle.Driveable);
            SetBool(ps.Properties, "VehicleDestroyed_", vehicle.Destroyed);
            ApplyTransform(ps.Properties, vehicle);
        }
    }

    /// <summary>Writes a vehicle's world transform (translation vector + rotation quaternion) in place.</summary>
    private static void ApplyTransform(IList<FPropertyTag> props, WorldVehicle v)
    {
        if (props.FindByPrefix("Transform_")?.Property is not StructProperty tsp || tsp.Value is not PropertiesStruct tps)
        {
            return;
        }
        if (tps.Properties.FindByPrefix("Translation")?.Property is StructProperty trsp && trsp.Value is VectorStruct vec)
        {
            var fv = vec.Value;
            fv.X = v.X;
            fv.Y = v.Y;
            fv.Z = v.Z;
            vec.Value = fv;
        }
        if (tps.Properties.FindByPrefix("Rotation")?.Property is StructProperty rsp && rsp.Value is QuatStruct q)
        {
            var fq = q.Value;
            fq.X = v.QuatX;
            fq.Y = v.QuatY;
            fq.Z = v.QuatZ;
            fq.W = v.QuatW;
            q.Value = fq;
        }
    }
}
