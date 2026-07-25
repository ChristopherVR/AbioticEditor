using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.WorldSaves;

// WorldSaveReader - world-state reads: quest flags, doors, clock, recipes, global unlocks.
public static partial class WorldSaveReader
{
    /// <summary>
    /// The world clock from the Facility save's <c>TimeOfDay</c> struct: in-game
    /// seconds of the current day (0..86400) + the day counter. Null on saves
    /// without the struct (regions, metadata).
    /// </summary>
    public static (double Seconds, int Day)? ReadWorldClock(SaveGame save)
    {
        if (save.Properties?.FindByPrefix("TimeOfDay")?.Property is not StructProperty sp
            || sp.Value is not PropertiesStruct ps)
        {
            return null;
        }
        var seconds = ps.Properties.FindByPrefix("TimeOfDaySeconds")?.Property?.Value is double d ? d : 0;
        var day = ps.Properties.FindByPrefix("CurrentDay")?.Property?.Value is int i ? i : 0;
        return (seconds, day);
    }

    /// <summary>The in-game day this region was first entered (region saves only).</summary>
    public static int? ReadDayDiscovered(SaveGame save)
        => save.Properties?.FindByPrefix("DayDiscovered")?.Property?.Value is int d ? d : null;

    /// <summary>
    /// Contained entities from the metadata save's <c>LeyakContainmentIDs</c> map:
    /// creature row name (Leyak, Krasue, …) -> the containment unit's
    /// DeployedObjectMap GUID (same linking scheme as the teleporter sync).
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ReadLeyakContainments(SaveGame save)
    {
        if (save.Properties?.FindByPrefix("LeyakContainmentIDs")?.Property is not MapProperty mp
            || mp.Value is null)
        {
            return Array.Empty<KeyValuePair<string, string>>();
        }

        var result = new List<KeyValuePair<string, string>>();
        foreach (var kvp in mp.Value)
        {
            var creature = ExtractMapKeyString(kvp.Key);
            var id = kvp.Value?.Value switch
            {
                FString fs => fs.Value,
                string s => s,
                var v => v?.ToString(),
            };
            if (creature is not null && id is not null)
            {
                result.Add(new KeyValuePair<string, string>(creature, id));
            }
        }
        return result;
    }

    /// <summary>
    /// Every deployed Leyak Containment Unit in one region save, read from
    /// <c>DeployedObjectMap</c>. Occupied and empty units both come back - occupancy is not
    /// stored on the unit, it is the metadata save's <c>LeyakContainmentIDs</c> map that links
    /// a creature to a unit GUID, so <see cref="WorldContainmentUnit.Creature"/> is left null
    /// here and filled in by <see cref="ContainmentSurvey"/>.
    /// </summary>
    public static IReadOnlyList<WorldContainmentUnit> ReadContainmentUnits(SaveGame save, string? regionSaveFileName = null)
    {
        var pairs = GetMapPairs(save.Properties, "DeployedObjectMap");
        if (pairs is null) return Array.Empty<WorldContainmentUnit>();

        var result = new List<WorldContainmentUnit>();
        foreach (var kvp in pairs)
        {
            var id = ExtractMapKeyString(kvp.Key);
            if (id is null) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;
            if (!ContainmentCreatureCatalog.IsUnitClass(ExtractClassName(ps.Properties))) continue;

            double x = 0, y = 0, z = 0;
            if (ps.Properties.FindByPrefix("Transform_")?.Property is StructProperty tsp
                && tsp.Value is PropertiesStruct tps
                && tps.Properties.FindByPrefix("Translation")?.Property is StructProperty trsp
                && trsp.Value is VectorStruct vec)
            {
                x = vec.Value.X;
                y = vec.Value.Y;
                z = vec.Value.Z;
            }

            int? stability = null, creatureIndex = null;
            if (ps.Properties.FindByPrefix("ChangableData_")?.Property is StructProperty cd
                && cd.Value is PropertiesStruct cdps)
            {
                stability = ReadOptionalDynamicInt(cdps.Properties, ContainmentDynamicSlots.Stability);
                creatureIndex = ReadOptionalDynamicInt(cdps.Properties, ContainmentDynamicSlots.CreatureIndex);
            }

            result.Add(new WorldContainmentUnit(id, regionSaveFileName ?? string.Empty, x, y, z, stability, creatureIndex));
        }
        return result;
    }

    /// <summary>
    /// Reads one <c>DynamicProperties_</c> int by <c>EDynamicProperty::*</c> tail, returning null
    /// (rather than 0) when the slot is absent - the containment reads need to tell "stability 0"
    /// apart from "this unit never wrote a stability slot".
    /// </summary>
    internal static int? ReadOptionalDynamicInt(IList<FPropertyTag> props, string keySuffix)
    {
        if (props.FindByPrefix("DynamicProperties_")?.Property is not ArrayProperty ap || ap.Value is null)
            return null;

        for (var i = 0; i < ap.Value.Length; i++)
        {
            if (ap.Value.GetValue(i) is not StructProperty esp || esp.Value is not PropertiesStruct eps) continue;
            var key = eps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString();
            if (key is null || !key.EndsWith("::" + keySuffix, StringComparison.Ordinal)) continue;
            return eps.Properties.FindByPrefix("Value")?.Property?.Value switch
            {
                int ii => ii,
                long ll => (int)ll,
                _ => null,
            };
        }
        return null;
    }

    /// <summary>
    /// One world-wide unlock array from the metadata save's <c>GlobalUnlocks</c> struct
    /// (e.g. <c>GlobalItemsPickedUp_</c>, <c>GlobalEmailsRead_</c>). Empty elsewhere.
    /// </summary>
    public static IReadOnlyList<string> ReadGlobalUnlockArray(SaveGame save, string prefix)
    {
        var props = GetGlobalUnlocksProps(save);
        if (props?.FindByPrefix(prefix)?.Property is not ArrayProperty array || array.Value is null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            var s = array.Value.GetValue(i) switch
            {
                FString fs => fs.Value,
                string raw => raw,
                var v => v?.ToString(),
            };
            if (!string.IsNullOrEmpty(s)) result.Add(s!);
        }
        return result;
    }

    /// <summary>The metadata save's <c>LastPlayed</c> timestamp, formatted (null elsewhere).</summary>
    public static string? ReadLastPlayedText(SaveGame save)
        => save.Properties?.FindByPrefix("LastPlayed")?.Property is StructProperty sp
            ? sp.Value?.ToString()
            : null;

    internal static IList<FPropertyTag>? GetGlobalUnlocksProps(SaveGame save)
        => save.Properties.FindByPrefix("GlobalUnlocks")?.Property is StructProperty sp
           && sp.Value is PropertiesStruct ps ? ps.Properties : null;

    private static IReadOnlyList<string> ReadGlobalRecipes(SaveGame save)
    {
        var props = GetGlobalUnlocksProps(save);
        if (props is null) return Array.Empty<string>();

        var tag = props.FindByPrefix("GlobalRecipesUnlocked_");
        if (tag?.Property is not ArrayProperty array || array.Value is null)
            return Array.Empty<string>();

        var result = new List<string>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            var element = array.Value.GetValue(i);
            var s = element switch
            {
                FString fs => fs.Value,
                string raw => raw,
                _ => element?.ToString(),
            };
            if (!string.IsNullOrEmpty(s)) result.Add(s!);
        }
        return result;
    }

    // ---------- DeployedObjectMap ----------

    /// <summary>
    /// Reads the top-level <c>WorldFlags</c> ArrayProperty (a Name-typed array
    /// of plain flag strings such as <c>Office_NewGameStarted</c>). The
    /// underlying NameProperty is a <see cref="StrProperty"/>-derived simple
    /// property, so the array contains <see cref="FString"/> elements.
    /// </summary>
    private static IReadOnlyList<string> ReadWorldFlags(SaveGame save)
    {
        var tag = save.Properties.FindByPrefix("WorldFlags");
        if (tag?.Property is not ArrayProperty array || array.Value is null)
            return Array.Empty<string>();

        var result = new List<string>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            var element = array.Value.GetValue(i);
            var s = element switch
            {
                FString fs => fs.Value,
                string raw => raw,
                _ => element?.ToString(),
            };
            if (!string.IsNullOrEmpty(s)) result.Add(s!);
        }
        return result;
    }

    // ---------- doors ----------

    private static List<WorldDoor> ReadDoors(SaveGame save)
    {
        var doors = new List<WorldDoor>();
        doors.AddRange(ReadDoorsFromMap(save, "SimpleDoorMap", WorldDoorKind.Simple));
        doors.AddRange(ReadDoorsFromMap(save, "SecurityDoorMap", WorldDoorKind.Security));
        return doors;
    }

    private static IEnumerable<WorldDoor> ReadDoorsFromMap(SaveGame save, string namePrefix, WorldDoorKind kind)
    {
        var pairs = GetMapPairs(save.Properties, namePrefix);
        if (pairs is null) yield break;

        foreach (var kvp in pairs)
        {
            var id = ExtractMapKeyString(kvp.Key);
            if (id is null) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps)
                continue;

            var p = ps.Properties;
            var noReset = p.TryGetBool("NoReset_");

            if (kind == WorldDoorKind.Simple)
            {
                // SimpleDoorMap struct (SaveData_Door_Struct): DoorState (enum byte),
                // DoorRotationRootYaw (double), OneWayDoor_HasBeenUnlocked (bool),
                // NoReset (bool).
                var stateRaw = p.FindByPrefix("DoorState_")?.Property?.Value;
                var stateStr = stateRaw switch
                {
                    FString fs => fs.Value,
                    string s => s,
                    byte b => b.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null => null,
                    _ => stateRaw.ToString(),
                };

                var yaw = p.FindByPrefix("DoorRotationRootYaw_")?.Property?.Value as double?;
                var oneWay = p.TryGetBool("OneWayDoor_HasBeenUnlocked_");

                yield return new WorldDoor(
                    Id: id,
                    Kind: kind,
                    DoorState: stateStr,
                    Yaw: yaw,
                    OneWayUnlocked: oneWay,
                    IsDoorOpen: null,
                    NoReset: noReset);
            }
            else
            {
                // SecurityDoorMap struct (SaveData_SecurityDoor_Struct): IsDoorOpen
                // (bool), NoReset (bool). No state/yaw/one-way fields.
                var isOpen = p.TryGetBool("IsDoorOpen_");

                yield return new WorldDoor(
                    Id: id,
                    Kind: kind,
                    DoorState: null,
                    Yaw: null,
                    OneWayUnlocked: null,
                    IsDoorOpen: isOpen,
                    NoReset: noReset);
            }
        }
    }

    // ---------- shared helpers ----------
}
