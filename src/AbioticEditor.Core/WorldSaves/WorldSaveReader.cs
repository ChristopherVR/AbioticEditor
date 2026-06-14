using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Parses an Abiotic Factor <c>WorldSave_*.sav</c> file into typed models.
///
/// Like player saves, world-save property names carry hash suffixes from the blueprint
/// compiler - e.g. <c>ContainerInventories_110_2B3F...</c>. We match by prefix everywhere
/// so the reader survives suffix changes between game patches.
///
/// First pass models the obvious editable category: <em>containers</em> (deployables in
/// <c>DeployedObjectMap</c> with non-empty <c>ContainerInventories_</c>, plus entries of
/// <c>CustomInventoryMap</c>). The raw save tree is preserved on
/// <see cref="WorldSaveData.Raw"/> so unedited properties round-trip byte-perfect.
/// </summary>
public static class WorldSaveReader
{
    static WorldSaveReader()
    {
        AbioticSaveClasses.EnsureLoaded();
    }

    /// <summary>
    /// Loads a world save from <paramref name="path"/> and returns a typed view.
    /// </summary>
    public static WorldSaveData ReadFromFile(string path)
    {
        Diagnostics.EditorLog.Info("WorldSave", $"Parsing {Path.GetFileName(path)}");
        try
        {
            using var fs = File.OpenRead(path);
            return ReadFromStream(fs);
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Error("WorldSave", $"Failed to parse {Path.GetFileName(path)}", ex);
            throw;
        }
    }

    public static WorldSaveData ReadFromStream(Stream stream)
    {
        var save = SaveGame.LoadFrom(stream);

        var containers = new List<WorldContainer>();
        containers.AddRange(ReadDeployedContainers(save));
        containers.AddRange(ReadCustomInventoryContainers(save));
        containers.AddRange(ReadVehicleContainers(save));

        var flags = ReadWorldFlags(save);
        var doors = ReadDoors(save);

        // Metadata-save extras: quest chapter + playtime + global recipes. Absent on
        // per-region saves.
        var story = save.Properties.FindByPrefix("StoryProgressionRow")?.Property?.Value?.ToString();
        int? minutes = save.Properties.FindByPrefix("MinutesPassed")?.Property?.Value is int m ? m : null;
        var globalRecipes = ReadGlobalRecipes(save);
        var droppedItems = ReadDroppedItems(save);
        var npcs = ReadNpcs(save);
        var pets = ReadPets(save);
        var vehicles = ReadVehicles(save);
        var deployables = ReadDeployables(save);

        LogUnmodeledKeys(save);

        return new WorldSaveData(save, containers, flags, doors, story, minutes, globalRecipes, droppedItems, npcs, deployables, pets, vehicles);
    }

    // Top-level prefixes this reader consumes. Anything else in a save is data the
    // editor has NO visibility on - surfaced via the diagnostic log so format changes
    // in game updates are traceable.
    private static readonly string[] ConsumedPrefixes =
    {
        "DeployedObjectMap", "CustomInventoryMap", "WorldFlags",
        // The door maps' real key names; a bare "Door" prefix matches neither.
        "SimpleDoorMap", "SecurityDoorMap",
        "StoryProgressionRow", "MinutesPassed", "GlobalRecipesUnlocked", "GlobalRecipesResearched",
        "DroppedItemMap", "NarrativeNPCMap", "LevelGUID",
        "TimeOfDay", "DayDiscovered", "LeyakContainmentIDs",
        "PetNPC", "GlobalUnlocks", "LastPlayed",
        // Understood bookkeeping, intentionally not editable: the owning server/world
        // id and the raw engine-side save version int.
        "SaveIdentifier", "SaveVersion",
    };

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

    private static void LogUnmodeledKeys(SaveGame save)
    {
        if (save.Properties is null) return;
        foreach (var tag in save.Properties)
        {
            var name = tag.Name?.Value;
            if (name is null) continue;
            if (ConsumedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            // A registered world-map feature (Features/) now models this map - editable, not unknown.
            if (Features.WorldMapFeatures.IsKnownMap(name)) continue;
            Diagnostics.EditorLog.UnknownData("WorldSave", name,
                $"unmodeled top-level property ({tag.Property?.GetType().Name ?? "?"}) - preserved verbatim, not editable in the UI");
        }
    }

    /// <summary>
    /// Lightweight pass over <c>DeployedObjectMap</c>: class + world position + inventory
    /// presence for every deployable (the base manager needs benches and furniture too,
    /// not just containers).
    /// </summary>
    private static IReadOnlyList<WorldDeployable> ReadDeployables(SaveGame save)
    {
        var pairs = GetMapPairs(save.Properties, "DeployedObjectMap");
        if (pairs is null) return Array.Empty<WorldDeployable>();

        var result = new List<WorldDeployable>(pairs.Count);
        foreach (var kvp in pairs)
        {
            var id = ExtractMapKeyString(kvp.Key);
            if (id is null) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var className = ExtractClassName(ps.Properties);

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

            var hasInventory = false;
            var itemCount = 0;
            if (ps.Properties.FindByPrefix("ContainerInventories_")?.Property is ArrayProperty invArray
                && invArray.Value is { Length: > 0 })
            {
                hasInventory = true;
                for (var i = 0; i < invArray.Value.Length; i++)
                {
                    if (invArray.Value.GetValue(i) is StructProperty invSp && invSp.Value is PropertiesStruct invPs)
                    {
                        var inv = ReadInventoryStruct(invPs.Properties);
                        itemCount += inv?.Slots.Count(s => !s.IsEmpty && s.ItemId != "Empty") ?? 0;
                    }
                }
            }

            var customName = ps.Properties.FindByPrefix("CustomTextDisplay_")?.Property?.Value?.ToString();

            result.Add(new WorldDeployable(id, className, x, y, z, hasInventory, itemCount,
                string.IsNullOrWhiteSpace(customName) ? null : customName));
        }
        return result;
    }

    /// <summary>
    /// Reads <c>NarrativeNPCMap</c> (story NPCs / traders). Tamed companions live in
    /// <c>PetNPC</c> and are read separately by <see cref="ReadPets"/>; both maps share
    /// the same <c>SaveData_NPCState_Struct</c>, but pets fill the pet-specific fields.
    /// </summary>
    private static List<WorldNpc> ReadNpcs(SaveGame save)
    {
        var result = new List<WorldNpc>();
        ReadNpcMap(save, "NarrativeNPCMap", isPet: false, result);
        return result;
    }

    /// <summary>
    /// Reads the <c>PetNPC</c> map: per-pet name, life flag, creature class, location,
    /// per-limb health (<c>CurrentHealthMap_</c>) and XP (<c>DynamicProperties_</c>).
    /// </summary>
    private static List<WorldPet> ReadPets(SaveGame save)
    {
        var result = new List<WorldPet>();
        var pairs = GetMapPairs(save.Properties, "PetNPC");
        if (pairs is null) return result;

        foreach (var kvp in pairs)
        {
            var id = ExtractMapKeyString(kvp.Key);
            if (id is null) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var p = ps.Properties;
            var isDead = p.TryGetBool("IsDead_") ?? false;
            var state = p.FindByPrefix("NarrativeState_")?.Property?.Value?.ToString();
            var customName = p.FindByPrefix("CustomName_")?.Property?.Value?.ToString();
            var npcClass = p.FindByPrefix("NPCClass_")?.Property?.Value?.ToString();

            double x = 0, y = 0, z = 0;
            if (p.FindByPrefix("Location_")?.Property is StructProperty locSp && locSp.Value is VectorStruct loc)
            {
                x = loc.Value.X;
                y = loc.Value.Y;
                z = loc.Value.Z;
            }

            var limbs = ReadLimbHealth(p);
            var xp = ReadDynamicInt(p, "XP");

            result.Add(new WorldPet(
                id, isDead, npcClass, x, y, z,
                string.IsNullOrEmpty(customName) ? null : customName,
                limbs, xp, state));
        }
        return result;
    }

    /// <summary>
    /// Reads <c>CurrentHealthMap_</c>: a map of <c>EBodyLimbs::*</c> (EnumProperty) to a
    /// DoubleProperty current-health value. Keyed by the full enum string.
    /// </summary>
    private static Dictionary<string, double> ReadLimbHealth(IList<FPropertyTag> props)
    {
        var dict = new Dictionary<string, double>(StringComparer.Ordinal);
        if (props.FindByPrefix("CurrentHealthMap_")?.Property is MapProperty mp && mp.Value is not null)
        {
            foreach (var kv in mp.Value)
            {
                var key = kv.Key?.Value?.ToString();
                if (key is null) continue;
                dict[key] = kv.Value?.Value is double d ? d : 0;
            }
        }
        return dict;
    }

    /// <summary>
    /// Reads one int from <c>DynamicProperties_</c> - an array of {Key (EnumProperty
    /// <c>EDynamicProperty::*</c>), Value (IntProperty)} structs. Matches by enum tail
    /// (e.g. <paramref name="keySuffix"/> = "XP"); returns 0 when absent.
    /// </summary>
    private static int ReadDynamicInt(IList<FPropertyTag> props, string keySuffix)
    {
        if (props.FindByPrefix("DynamicProperties_")?.Property is not ArrayProperty ap || ap.Value is null)
            return 0;

        for (var i = 0; i < ap.Value.Length; i++)
        {
            if (ap.Value.GetValue(i) is not StructProperty esp || esp.Value is not PropertiesStruct eps) continue;
            var key = eps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString();
            if (key is not null && key.EndsWith("::" + keySuffix, StringComparison.Ordinal))
            {
                return eps.Properties.FindByPrefix("Value")?.Property?.Value switch
                {
                    int ii => ii,
                    long ll => (int)ll,
                    _ => 0,
                };
            }
        }
        return 0;
    }

    private static void ReadNpcMap(SaveGame save, string prefix, bool isPet, List<WorldNpc> result)
    {
        var pairs = GetMapPairs(save.Properties, prefix);
        if (pairs is null) return;

        foreach (var kvp in pairs)
        {
            var id = ExtractMapKeyString(kvp.Key);
            if (id is null) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var isDead = ps.Properties.TryGetBool("IsDead_") ?? false;
            var state = ps.Properties.FindByPrefix("NarrativeState_")?.Property?.Value?.ToString();
            var customName = ps.Properties.FindByPrefix("CustomName_")?.Property?.Value?.ToString();
            // Pets: take the class from NPCClass_ (inside NarrativeNPCMap that field
            // serializes as None - there the map key carries the class instead).
            var npcClass = ps.Properties.FindByPrefix("NPCClass_")?.Property?.Value?.ToString();
            double x = 0, y = 0, z = 0;
            if (ps.Properties.FindByPrefix("Location_")?.Property is StructProperty locSp
                && locSp.Value is VectorStruct loc)
            {
                x = loc.Value.X;
                y = loc.Value.Y;
                z = loc.Value.Z;
            }
            result.Add(new WorldNpc(id, isDead, state, x, y, z, isPet, customName, npcClass));
        }
    }

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

    /// <summary>
    /// Reads <c>DroppedItemMap</c>: GUID -> struct with <c>ItemLocation_</c>,
    /// <c>ItemRotation_</c>, <c>ItemData_</c> (a standard inventory slot struct) and
    /// <c>NoDespawn_</c>.
    /// </summary>
    private static IReadOnlyList<WorldDroppedItem> ReadDroppedItems(SaveGame save)
    {
        var pairs = GetMapPairs(save.Properties, "DroppedItemMap");
        if (pairs is null) return Array.Empty<WorldDroppedItem>();

        var result = new List<WorldDroppedItem>(pairs.Count);
        var index = 0;
        foreach (var kvp in pairs)
        {
            var id = ExtractMapKeyString(kvp.Key);
            if (id is null) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var itemData = ps.Properties.FindByPrefix("ItemData_");
            var slot = ReadSlot(index++, itemData?.Property);
            var noDespawn = ps.Properties.TryGetBool("NoDespawn_") ?? false;

            double x = 0, y = 0, z = 0;
            if (ps.Properties.FindByPrefix("ItemLocation_")?.Property is StructProperty locSp
                && locSp.Value is VectorStruct loc)
            {
                x = loc.Value.X;
                y = loc.Value.Y;
                z = loc.Value.Z;
            }
            result.Add(new WorldDroppedItem(id, slot, noDespawn, x, y, z));
        }
        return result;
    }

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

    private static IEnumerable<WorldContainer> ReadDeployedContainers(SaveGame save)
    {
        var pairs = GetMapPairs(save.Properties, "DeployedObjectMap");
        if (pairs is null) yield break;

        foreach (var kvp in pairs)
        {
            var key = ExtractMapKeyString(kvp.Key);
            if (key is null) continue;

            // Each value is a StructProperty around a SaveData_Deployable_Struct.
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps)
                continue;

            var className = ExtractClassName(ps.Properties);
            var inventories = ReadContainerInventoriesArray(ps.Properties);
            if (inventories.Count == 0) continue;

            yield return new WorldContainer(key, WorldContainerSource.Deployed, className, inventories);
        }
    }

    // ---------- CustomInventoryMap ----------

    private static IEnumerable<WorldContainer> ReadCustomInventoryContainers(SaveGame save)
    {
        var pairs = GetMapPairs(save.Properties, "CustomInventoryMap");
        if (pairs is null) yield break;

        foreach (var kvp in pairs)
        {
            var key = ExtractMapKeyString(kvp.Key);
            if (key is null) continue;

            // The value is itself a single SaveData_Inventories_Struct (not an array of them).
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps)
                continue;

            var inv = ReadInventoryStruct(ps.Properties);
            if (inv is null) continue;

            yield return new WorldContainer(
                key,
                WorldContainerSource.Custom,
                ClassName: null,
                Inventories: new[] { inv });
        }
    }

    // ---------- WorldFlags ----------

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

    /// <summary>
    /// Reads the <c>ContainerInventories_*</c> ArrayProperty (an array of
    /// <c>SaveData_Inventories_Struct</c>) into a flat list of inventories.
    /// </summary>
    private static IReadOnlyList<WorldInventory> ReadContainerInventoriesArray(IList<FPropertyTag> deployableProps)
    {
        var tag = deployableProps.FindByPrefix("ContainerInventories_");
        if (tag?.Property is not ArrayProperty array || array.Value is null || array.Value.Length == 0)
            return Array.Empty<WorldInventory>();

        var result = new List<WorldInventory>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            // Array elements for a struct-typed array are StructProperty wrappers whose Value
            // is the IStructData payload (PropertiesStruct for SaveData_Inventories_Struct).
            if (array.Value.GetValue(i) is not StructProperty outer || outer.Value is not PropertiesStruct ps)
                continue;

            var inv = ReadInventoryStruct(ps.Properties);
            if (inv is not null) result.Add(inv);
        }
        return result;
    }

    /// <summary>
    /// Reads one <c>SaveData_Inventories_Struct</c>: an <c>InventoryContent_*</c>
    /// ArrayProperty of <c>Abiotic_InventoryItemSlotStruct</c> elements.
    /// </summary>
    internal static WorldInventory? ReadInventoryStruct(IList<FPropertyTag> inventoriesStructProps)
    {
        var content = inventoriesStructProps.FindByPrefix("InventoryContent_");
        if (content?.Property is not ArrayProperty array || array.Value is null)
            return null;

        var slots = new List<InventoryItemSlot>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            slots.Add(ReadSlot(i, array.Value.GetValue(i)));
        }
        return new WorldInventory(slots);
    }

    /// <summary>
    /// Reads one slot. Mirrors <c>PlayerSaveReader.ReadSlot</c> - kept private here so we
    /// don't take a dependency on its internals; the underlying struct is identical.
    /// </summary>
    private static InventoryItemSlot ReadSlot(int index, object? element)
    {
        if (element is not StructProperty outer || outer.Value is not PropertiesStruct ps)
            return EmptySlot(index);

        string? itemId = null;
        var rowHandle = ps.Properties.FindByPrefix("ItemDataTable_");
        if (rowHandle?.Property is StructProperty rhSp && rhSp.Value is PropertiesStruct rhPs)
        {
            itemId = rhPs.Properties.GetString("RowName");
        }

        var changeable = ps.Properties.FindByPrefix("ChangeableData_");
        if (changeable?.Property is not StructProperty cSp || cSp.Value is not PropertiesStruct cPs)
        {
            return new InventoryItemSlot(index, itemId, 1, 0, 0, 0, 0, null, false, null, null);
        }

        var p = cPs.Properties;
        return new InventoryItemSlot(
            Index: index,
            ItemId: itemId,
            Count: (int)p.GetLong("CurrentStack_", 1),
            Durability: p.GetDouble("CurrentItemDurability_"),
            MaxDurability: p.GetDouble("MaxItemDurability_"),
            AmmoInMagazine: (int)p.GetLong("CurrentAmmoInMagazine_"),
            LiquidLevel: (int)p.GetLong("LiquidLevel_"),
            LiquidType: p.GetEnumString("CurrentLiquid_"),
            DynamicState: p.GetBool("DynamicState_"),
            PlayerMadeString: p.GetString("PlayerMadeString_"),
            AssetId: p.GetString("AssetID_"));
    }

    private static InventoryItemSlot EmptySlot(int index)
        => new(index, null, 0, 0, 0, 0, 0, null, false, null, null);

    private static string? ExtractClassName(IList<FPropertyTag> deployableProps)
    {
        var classTag = deployableProps.FindByPrefix("Class_");
        if (classTag?.Property?.Value is SoftObjectPath softPath)
        {
            return softPath.AssetName?.Value;
        }
        // Some builds may unbox SoftObjectProperty differently - fall back to ToString.
        return classTag?.Property?.Value?.ToString();
    }

    // ---------- map / primitive accessors ----------

    internal static IList<KeyValuePair<FProperty, FProperty>>? GetMapPairs(
        IList<FPropertyTag>? topLevel,
        string namePrefix)
    {
        if (topLevel is null) return null;
        var tag = topLevel.FindByPrefix(namePrefix);
        if (tag?.Property is MapProperty mp) return mp.Value;
        return null;
    }

    internal static string? ExtractMapKeyString(FProperty key)
    {
        // Map keys here are StrProperty / NameProperty / similar; Value is either an
        // FString or a plain string depending on the property type.
        var v = key.Value;
        return v switch
        {
            FString fs => fs.Value,
            string s => s,
            _ => v?.ToString(),
        };
    }

}
