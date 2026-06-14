using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Applies container mutations to the underlying <see cref="SaveGame"/> tree of a
/// <see cref="WorldSaveData"/>. Anything not edited re-serializes byte-perfect because
/// we only patch existing property <c>Value</c> fields - never replace structure.
/// </summary>
public static class WorldSaveWriter
{
    /// <summary>
    /// Patches each container in <paramref name="updated"/> back into <paramref name="data"/>'s
    /// raw save tree.
    ///
    /// Containers are looked up by <see cref="WorldContainer.Id"/> and
    /// <see cref="WorldContainer.Source"/> against the original maps. Inventory entries
    /// are matched by ordinal (the array index in <c>ContainerInventories_</c>) and slots
    /// inside an inventory are matched by ordinal too. Out-of-range slots are ignored so
    /// the writer is robust to schema drift.
    /// </summary>
    public static void ApplyContainers(WorldSaveData data, IEnumerable<WorldContainer> updated)
    {
        var deployedById = BuildDeployedLookup(data);
        var customById = BuildCustomLookup(data);

        foreach (var container in updated)
        {
            switch (container.Source)
            {
                case WorldContainerSource.Deployed:
                    if (deployedById.TryGetValue(container.Id, out var deployableProps))
                    {
                        ApplyContainerInventoriesArray(deployableProps, container.Inventories);
                    }
                    break;
                case WorldContainerSource.Custom:
                    if (customById.TryGetValue(container.Id, out var inventoryStructProps)
                        && container.Inventories.Count > 0)
                    {
                        ApplyInventoryStruct(inventoryStructProps, container.Inventories[0]);
                    }
                    break;
                case WorldContainerSource.Vehicle:
                    if (BuildMapLookup(data, "VehicleMap").TryGetValue(container.Id, out var vehicleProps))
                    {
                        ApplyContainerInventoriesArray(vehicleProps, container.Inventories);
                    }
                    break;
            }
        }
    }

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

    private static Dictionary<string, IList<FPropertyTag>> BuildMapLookup(WorldSaveData data, string mapName)
    {
        var result = new Dictionary<string, IList<FPropertyTag>>(StringComparer.Ordinal);
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, mapName);
        if (pairs is null) return result;

        foreach (var kvp in pairs)
        {
            var key = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (key is null) continue;
            if (kvp.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
            {
                result[key] = ps.Properties;
            }
        }
        return result;
    }

    /// <summary>
    /// Replaces the top-level <c>WorldFlags</c> array with <paramref name="flags"/>.
    /// Reuses the existing <see cref="ArrayProperty"/> instance (preserving
    /// <c>ItemType</c> and any struct prototype state) and swaps in a freshly
    /// allocated <see cref="FString"/> array - that's the element type for
    /// Name-typed arrays as deserialized by <c>ArraySerializationHelper</c>.
    /// Returns false when the save carries no <c>WorldFlags</c> array at all
    /// (delta-serialization: untouched portal worlds omit it) - nothing was changed.
    /// </summary>
    public static bool ApplyFlags(WorldSaveData data, IReadOnlyList<string> flags)
    {
        var tag = data.Raw.Properties.FindByPrefix("WorldFlags");
        if (tag?.Property is not ArrayProperty array) return false;

        var items = new FString[flags.Count];
        for (var i = 0; i < flags.Count; i++)
        {
            items[i] = new FString(flags[i] ?? string.Empty);
        }
        array.Value = items;
        return true;
    }

    /// <summary>
    /// Patches existing doors in <c>SimpleDoorMap</c> / <c>SecurityDoorMap</c>
    /// from <paramref name="doors"/>. Only updates sub-property <c>.Value</c>
    /// fields - never adds or removes entries. Doors with no matching id are
    /// silently skipped.
    /// </summary>
    public static void ApplyDoors(WorldSaveData data, IEnumerable<WorldDoor> doors)
    {
        var simpleById = BuildDoorLookup(data, "SimpleDoorMap");
        var securityById = BuildDoorLookup(data, "SecurityDoorMap");

        foreach (var door in doors)
        {
            var lookup = door.Kind == WorldDoorKind.Simple ? simpleById : securityById;
            if (!lookup.TryGetValue(door.Id, out var props)) continue;

            if (door.Kind == WorldDoorKind.Simple)
            {
                if (door.DoorState is not null)
                {
                    SetEnumByte(props, "DoorState_", door.DoorState);
                }
                if (door.Yaw.HasValue)
                {
                    SetDouble(props, "DoorRotationRootYaw_", door.Yaw.Value);
                }
                if (door.OneWayUnlocked.HasValue)
                {
                    SetBool(props, "OneWayDoor_HasBeenUnlocked_", door.OneWayUnlocked.Value);
                }
            }
            else
            {
                if (door.IsDoorOpen.HasValue)
                {
                    SetBool(props, "IsDoorOpen_", door.IsDoorOpen.Value);
                }
            }

            if (door.NoReset.HasValue)
            {
                SetBool(props, "NoReset_", door.NoReset.Value);
            }
        }
    }

    private static Dictionary<string, IList<FPropertyTag>> BuildDoorLookup(WorldSaveData data, string namePrefix)
    {
        var result = new Dictionary<string, IList<FPropertyTag>>(StringComparer.Ordinal);
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, namePrefix);
        if (pairs is null) return result;

        foreach (var kvp in pairs)
        {
            var key = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (key is null) continue;
            if (kvp.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
            {
                result[key] = ps.Properties;
            }
        }
        return result;
    }

    /// <summary>
    /// Sets the metadata save's <c>StoryProgressionRow</c> NameProperty. No-op when the
    /// property is absent (per-region world saves).
    /// </summary>
    public static void ApplyStoryProgression(WorldSaveData data, string row)
    {
        if (data.Raw.Properties is { } props) SetName(props, "StoryProgressionRow", row);
    }

    /// <summary>Sets the metadata save's <c>MinutesPassed</c> IntProperty (if present).</summary>
    public static void ApplyMinutesPassed(WorldSaveData data, int minutes)
    {
        if (data.Raw.Properties is { } props) SetInt(props, "MinutesPassed", minutes);
    }

    /// <summary>
    /// Replaces both world-wide recipe arrays (<c>GlobalRecipesUnlocked_</c> and
    /// <c>GlobalRecipesResearched_</c>) inside <c>GlobalUnlocks</c> with
    /// <paramref name="recipes"/>. The game keeps the two in lock-step for unlocked
    /// recipes, so we mirror that.
    /// </summary>
    public static void ApplyGlobalRecipes(WorldSaveData data, IReadOnlyList<string> recipes)
    {
        var props = WorldSaveReader.GetGlobalUnlocksProps(data.Raw);
        if (props is null) return;

        ReplaceNameArray(props, "GlobalRecipesUnlocked_", recipes);
        ReplaceNameArray(props, "GlobalRecipesResearched_", recipes);
    }

    private static void ReplaceNameArray(IList<FPropertyTag> tags, string prefix, IReadOnlyList<string> values)
    {
        var tag = tags.FindByPrefix(prefix);
        if (tag?.Property is not ArrayProperty array) return;

        var items = new FString[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            items[i] = new FString(values[i]);
        }
        array.Value = items;
    }

    /// <summary>
    /// Patches <c>NarrativeNPCMap</c> entries (IsDead / NarrativeState / name) by map key.
    /// Pets (the <c>PetNPC</c> map) are handled separately by <see cref="ApplyPets"/>.
    /// </summary>
    public static void ApplyNpcs(WorldSaveData data, IEnumerable<WorldNpc> updated)
    {
        var byId = updated.ToDictionary(n => n.Id, StringComparer.Ordinal);
        ApplyNpcMap(data, "NarrativeNPCMap", byId);
    }

    /// <summary>
    /// Patches <c>PetNPC</c> entries by GUID: life flag, player name, creature class
    /// (the "upgrade / downgrade"), per-limb health, and XP. Every field is patched in
    /// place on the existing struct - the limb map and dynamic-property array keep their
    /// shape, so untouched pets re-serialize byte-perfect. Pets present in the save but
    /// absent from <paramref name="updated"/> are left untouched.
    /// </summary>
    public static void ApplyPets(WorldSaveData data, IEnumerable<WorldPet> updated)
    {
        var byId = updated.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, "PetNPC");
        if (pairs is null) return;

        foreach (var kvp in pairs)
        {
            var id = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (id is null || !byId.TryGetValue(id, out var pet)) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var p = ps.Properties;
            SetBool(p, "IsDead_", pet.IsDead);
            SetTextNone(p, "CustomName_", pet.CustomName ?? string.Empty);
            if (!string.IsNullOrEmpty(pet.NpcClass)) SetSoftObject(p, "NPCClass_", pet.NpcClass!);
            ApplyLimbHealth(p, pet.LimbHealth);
            ApplyDynamicInt(p, "XP", pet.Xp);
        }
    }

    /// <summary>
    /// Removes one <c>PetNPC</c> entry by GUID - the editor equivalent of deleting the pet
    /// from the world. Returns true when the entry existed. (Mirror of
    /// <see cref="RemoveDroppedItem"/>.)
    /// </summary>
    public static bool RemovePet(WorldSaveData data, string id)
    {
        if (data.Raw.Properties.FindByPrefix("PetNPC")?.Property is not MapProperty mp || mp.Value is null)
        {
            return false;
        }
        for (var i = mp.Value.Count - 1; i >= 0; i--)
        {
            if (string.Equals(WorldSaveReader.ExtractMapKeyString(mp.Value[i].Key), id, StringComparison.Ordinal))
            {
                mp.Value.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Per-limb health written when no explicit total is given (the game clamps to the level-scaled max).</summary>
    private const double FullLimbHealthOnArrival = 1000;

    /// <summary>
    /// Adds a pet to the world's <c>PetNPC</c> map by <b>cloning an existing entry</b> (so the
    /// struct layout is byte-identical to what the game writes) and overwriting only the fields
    /// that define this pet: a fresh GUID key, class, name, health, XP, and location. When a pet
    /// of the same class already exists it is preferred as the clone template (so the limb set
    /// matches). <paramref name="totalHealth"/> (e.g. a carried pet's durability) is distributed
    /// across the template's limbs to keep the total HP; null fills every limb. Returns the new
    /// GUID, or null when the map has no entry to clone. Mirrors <see cref="AddDroppedItem"/>.
    /// </summary>
    public static string? AddPet(WorldSaveData data, WorldPet pet, double? totalHealth = null)
    {
        if (data.Raw.Properties?.FindByPrefix("PetNPC")?.Property is not MapProperty mp
            || mp.Value is null || mp.Value.Count == 0)
        {
            return null;
        }

        SaveGame clone;
        using (var buffer = new MemoryStream())
        {
            data.Raw.WriteTo(buffer);
            buffer.Position = 0;
            clone = SaveGame.LoadFrom(buffer);
        }
        if (clone.Properties?.FindByPrefix("PetNPC")?.Property is not MapProperty cloneMap
            || cloneMap.Value is null || cloneMap.Value.Count == 0)
        {
            return null;
        }

        // Prefer a template of the same class (matching limb structure), else the first entry.
        var wantShort = PetCatalog.ShortOf(pet.NpcClass);
        var template = cloneMap.Value.FirstOrDefault(kv =>
            kv.Value is StructProperty s && s.Value is PropertiesStruct p
            && string.Equals(PetCatalog.ShortOf(p.Properties.FindByPrefix("NPCClass_")?.Property?.Value?.ToString()),
                wantShort, StringComparison.OrdinalIgnoreCase));
        if (template.Key is null) template = cloneMap.Value[0];

        var key = template.Key;
        var value = template.Value;

        var existingKey = WorldSaveReader.ExtractMapKeyString(key);
        var newId = FormatGuidLike(existingKey, Guid.NewGuid());
        key.Value = new FString(newId);

        if (value is StructProperty sp && sp.Value is PropertiesStruct ps)
        {
            SetBool(ps.Properties, "IsDead_", false);
            SetTextNone(ps.Properties, "CustomName_", pet.CustomName ?? string.Empty);
            if (!string.IsNullOrEmpty(pet.NpcClass)) SetSoftObject(ps.Properties, "NPCClass_", pet.NpcClass!);

            if (ps.Properties.FindByPrefix("Location_")?.Property is StructProperty locSp && locSp.Value is VectorStruct vec)
            {
                var v = vec.Value;
                v.X = pet.X;
                v.Y = pet.Y;
                v.Z = pet.Z;
                vec.Value = v;
            }
            DistributeLimbHealth(ps.Properties, totalHealth);
            ApplyDynamicInt(ps.Properties, "XP", pet.Xp);
        }

        mp.Value.Add(new KeyValuePair<FProperty, FProperty>(key, value));
        return newId;
    }

    /// <summary>
    /// Writes a pet's per-limb health. With <paramref name="totalHealth"/>, distributes it across
    /// the template's tracked (non-zero) limbs in proportion to their existing values so the sum
    /// equals the total (best-effort 1:1 with a carried pet's single durability); without it,
    /// fills every limb to <see cref="FullLimbHealthOnArrival"/>.
    /// </summary>
    private static void DistributeLimbHealth(IList<FPropertyTag> props, double? totalHealth)
    {
        if (props.FindByPrefix("CurrentHealthMap_")?.Property is not MapProperty hm || hm.Value is null) return;

        if (totalHealth is not { } total || total <= 0)
        {
            foreach (var kv in hm.Value) if (kv.Value is not null) kv.Value.Value = FullLimbHealthOnArrival;
            return;
        }

        var weights = hm.Value.Select(kv => kv.Value?.Value is double d ? d : 0).ToList();
        var sum = weights.Sum();
        for (var i = 0; i < hm.Value.Count; i++)
        {
            if (hm.Value[i].Value is not { } slot) continue;
            slot.Value = sum > 0 ? total * (weights[i] / sum) : total / hm.Value.Count;
        }
    }

    /// <summary>
    /// Patches existing <c>CurrentHealthMap_</c> limb values in place (matched by the full
    /// <c>EBodyLimbs::*</c> enum key). Never adds or removes limbs.
    /// </summary>
    private static void ApplyLimbHealth(IList<FPropertyTag> props, IReadOnlyDictionary<string, double> limbs)
    {
        if (limbs.Count == 0) return;
        if (props.FindByPrefix("CurrentHealthMap_")?.Property is not MapProperty mp || mp.Value is null) return;

        foreach (var kv in mp.Value)
        {
            var key = kv.Key?.Value?.ToString();
            if (key is not null && kv.Value is not null && limbs.TryGetValue(key, out var v))
            {
                kv.Value.Value = v;
            }
        }
    }

    /// <summary>
    /// Patches one int inside <c>DynamicProperties_</c> (matched by <c>EDynamicProperty::*</c>
    /// enum tail, e.g. "XP"). No-op when the pet has no such entry - the writer never
    /// fabricates an array element, which would risk an unloadable save.
    /// </summary>
    private static void ApplyDynamicInt(IList<FPropertyTag> props, string keySuffix, int value)
    {
        if (props.FindByPrefix("DynamicProperties_")?.Property is not ArrayProperty ap || ap.Value is null) return;

        for (var i = 0; i < ap.Value.Length; i++)
        {
            if (ap.Value.GetValue(i) is not StructProperty esp || esp.Value is not PropertiesStruct eps) continue;
            var key = eps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString();
            if (key is not null && key.EndsWith("::" + keySuffix, StringComparison.Ordinal))
            {
                var valProp = eps.Properties.FindByPrefix("Value")?.Property;
                if (valProp is not null) valProp.Value = value;
                return;
            }
        }
    }

    /// <summary>
    /// Sets a <see cref="SoftObjectProperty"/> value from a full
    /// <c>Package.Asset</c> path, splitting on the last dot. Preserves the existing
    /// property instance; no-op when the tag is absent or not a soft-object property.
    /// </summary>
    private static void SetSoftObject(IList<FPropertyTag> tags, string prefix, string fullPath)
    {
        if (tags.FindByPrefix(prefix)?.Property is not SoftObjectProperty sop) return;

        var path = sop.Value ?? new SoftObjectPath();
        var dot = fullPath.LastIndexOf('.');
        if (dot > 0)
        {
            path.PackageName = new FString(fullPath[..dot]);
            path.AssetName = new FString(fullPath[(dot + 1)..]);
        }
        else
        {
            path.PackageName = new FString(fullPath);
            path.AssetName = null;
        }
        path.SubPathString = null;
        sop.Value = path;
    }

    private static void ApplyNpcMap(WorldSaveData data, string prefix, Dictionary<string, WorldNpc> byId)
    {
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, prefix);
        if (pairs is null) return;

        foreach (var kvp in pairs)
        {
            var id = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (id is null || !byId.TryGetValue(id, out var npc)) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            SetBool(ps.Properties, "IsDead_", npc.IsDead);
            if (!string.IsNullOrEmpty(npc.State))
            {
                SetEnumByte(ps.Properties, "NarrativeState_", npc.State!);
            }
            SetTextNone(ps.Properties, "CustomName_", npc.CustomName ?? string.Empty);
        }
    }

    /// <summary>
    /// Sets a deployable's <c>CustomTextDisplay_</c> (sign text / bed claim) by
    /// DeployedObjectMap key. Bed claims use the <c>&lt;steamid64&gt;}|!|{&lt;name&gt;</c>
    /// format (<see cref="WorldDeployable.ClaimSeparator"/>). Returns false when the
    /// deployable or its text property doesn't exist.
    /// </summary>
    public static bool ApplyDeployableCustomText(WorldSaveData data, string deployableId, string text)
    {
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, "DeployedObjectMap");
        if (pairs is null) return false;

        foreach (var kvp in pairs)
        {
            if (!string.Equals(WorldSaveReader.ExtractMapKeyString(kvp.Key), deployableId, StringComparison.Ordinal))
            {
                continue;
            }
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) return false;

            var tag = ps.Properties.FindByPrefix("CustomTextDisplay_");
            switch (tag?.Property)
            {
                case StrProperty str:
                    str.Value = new FString(text);
                    return true;
                case TextProperty tp when tp.Value is UeSaveGame.DataTypes.FText ft
                    && ft.HistoryType == UeSaveGame.TextData.TextHistoryType.None:
                    if (ft.Value is not UeSaveGame.TextData.TextData_None none)
                    {
                        none = new UeSaveGame.TextData.TextData_None();
                        ft.Value = none;
                    }
                    none.Value = text.Length == 0 ? null : new FString(text);
                    return true;
                default:
                    return false;
            }
        }
        return false;
    }

    /// <summary>
    /// Sets a TextProperty carrying player-entered text (HistoryType None). Both pet
    /// names in the fixtures use that shape; other history types (localized texts) are
    /// left alone rather than risk corrupting their format data.
    /// </summary>
    private static void SetTextNone(IList<FPropertyTag> tags, string prefix, string value)
    {
        if (tags.FindByPrefix(prefix)?.Property is not TextProperty tp) return;
        if (tp.Value is not UeSaveGame.DataTypes.FText text) return;
        if (text.HistoryType != UeSaveGame.TextData.TextHistoryType.None) return;
        if (text.Value is not UeSaveGame.TextData.TextData_None data)
        {
            data = new UeSaveGame.TextData.TextData_None();
            text.Value = data;
        }
        data.Value = value.Length == 0 ? null : new FString(value);
    }

    /// <summary>
    /// Replaces one of the metadata save's <c>GlobalUnlocks</c> name arrays (e.g.
    /// <c>GlobalItemsPickedUp_</c>). Returns false when the struct is absent.
    /// </summary>
    public static bool ApplyGlobalUnlockArray(WorldSaveData data, string prefix, IReadOnlyList<string> values)
    {
        var props = WorldSaveReader.GetGlobalUnlocksProps(data.Raw);
        if (props is null) return false;
        ReplaceNameArray(props, prefix, values);
        return true;
    }

    /// <summary>
    /// Removes one <c>DroppedItemMap</c> entry - the editor equivalent of picking the
    /// item up off the ground. Returns true when the entry existed.
    /// </summary>
    public static bool RemoveDroppedItem(WorldSaveData data, string id)
    {
        if (data.Raw.Properties.FindByPrefix("DroppedItemMap")?.Property is not MapProperty mp
            || mp.Value is null)
        {
            return false;
        }
        for (var i = mp.Value.Count - 1; i >= 0; i--)
        {
            if (string.Equals(WorldSaveReader.ExtractMapKeyString(mp.Value[i].Key), id, StringComparison.Ordinal))
            {
                mp.Value.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Adds a new ground item to <c>DroppedItemMap</c> by <b>cloning an existing entry</b> -
    /// so the entry's struct layout is byte-for-byte what the game itself writes - and changing
    /// only the four things that make it a different drop: a fresh GUID map key, the item slot,
    /// the world location, and the no-despawn flag. Returns the new entry's id, or null when the
    /// map has no entry to clone (the writer never fabricates the struct from scratch, which
    /// would risk an unloadable save). The caller writes the save afterwards (keeping a .bak).
    /// </summary>
    public static string? AddDroppedItem(
        WorldSaveData data, InventoryItemSlot slot, double x, double y, double z, bool noDespawn = true)
    {
        if (data.Raw.Properties?.FindByPrefix("DroppedItemMap")?.Property is not MapProperty mp
            || mp.Value is null || mp.Value.Count == 0)
        {
            return null;
        }

        // Clone the whole save to a fresh object graph, then lift one entry out of the clone:
        // that entry shares no references with the live map, so grafting it back in (with new
        // leaf values) can't alias or corrupt the existing entries.
        SaveGame clone;
        using (var buffer = new MemoryStream())
        {
            data.Raw.WriteTo(buffer);
            buffer.Position = 0;
            clone = SaveGame.LoadFrom(buffer);
        }
        if (clone.Properties?.FindByPrefix("DroppedItemMap")?.Property is not MapProperty cloneMap
            || cloneMap.Value is null || cloneMap.Value.Count == 0)
        {
            return null;
        }

        var template = cloneMap.Value[0];
        var key = template.Key;
        var value = template.Value;

        // Re-key with a fresh GUID, formatted like the keys already in this save.
        var existingKey = WorldSaveReader.ExtractMapKeyString(key);
        var newId = FormatGuidLike(existingKey, Guid.NewGuid());
        key.Value = new FString(newId);

        // Swap in the dropped item, its location, and the despawn flag; everything else stays
        // exactly as the cloned (game-authored) entry had it.
        if (value is StructProperty sp && sp.Value is PropertiesStruct ps)
        {
            if (ps.Properties.FindByPrefix("ItemData_")?.Property is StructProperty slotSp
                && slotSp.Value is PropertiesStruct slotPs)
            {
                ApplySlot(slotPs.Properties, slot);
            }
            if (ps.Properties.FindByPrefix("ItemLocation_")?.Property is StructProperty locSp
                && locSp.Value is VectorStruct vec)
            {
                var v = vec.Value;
                v.X = x;
                v.Y = y;
                v.Z = z;
                vec.Value = v;
            }
            SetBool(ps.Properties, "NoDespawn_", noDespawn);
        }

        mp.Value.Add(new KeyValuePair<FProperty, FProperty>(key, value));
        return newId;
    }

    /// <summary>
    /// Formats <paramref name="guid"/> to match the spelling of the save's existing dropped-item
    /// keys (hyphenated vs 32-char "N", upper vs lower case), so a new key looks native.
    /// </summary>
    private static string FormatGuidLike(string? sample, Guid guid)
    {
        var hasDashes = sample?.Contains('-') == true;
        var formatted = guid.ToString(hasDashes ? "D" : "N");
        var upper = sample is not null && sample.Any(char.IsLetter) && !sample.Any(char.IsLower);
        return upper ? formatted.ToUpperInvariant() : formatted;
    }

    /// <summary>
    /// Patches the item slot inside existing <c>DroppedItemMap</c> entries (matched by
    /// map key). Location/rotation/despawn flags are untouched.
    /// </summary>
    public static void ApplyDroppedItems(WorldSaveData data, IEnumerable<WorldDroppedItem> updated)
    {
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, "DroppedItemMap");
        if (pairs is null) return;

        var byId = updated.ToDictionary(d => d.Id, StringComparer.Ordinal);
        foreach (var kvp in pairs)
        {
            var id = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (id is null || !byId.TryGetValue(id, out var item)) continue;
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var itemData = ps.Properties.FindByPrefix("ItemData_");
            if (itemData?.Property is StructProperty slotSp && slotSp.Value is PropertiesStruct slotPs)
            {
                ApplySlot(slotPs.Properties, item.Slot);
            }
            SetBool(ps.Properties, "NoDespawn_", item.NoDespawn);
        }
    }

    /// <summary>
    /// Deletes entries from <c>DroppedItemMap</c> by map key. This is the one writer that
    /// removes structure rather than patching values - the map re-serializes with its new
    /// count. Used for world cleanup (a long-lived save can accumulate 1000+ dropped
    /// items, which costs real in-game performance).
    /// </summary>
    /// <summary>
    /// Patches the Facility save's <c>TimeOfDay</c> struct (world clock): seconds of
    /// day + day counter. Returns false when the save carries no clock.
    /// </summary>
    public static bool ApplyWorldClock(WorldSaveData data, double seconds, int day)
    {
        if (data.Raw.Properties.FindByPrefix("TimeOfDay")?.Property is not StructProperty sp
            || sp.Value is not PropertiesStruct ps)
        {
            return false;
        }
        SetDouble(ps.Properties, "TimeOfDaySeconds", seconds);
        SetInt(ps.Properties, "CurrentDay", day);
        return true;
    }

    /// <summary>Patches a region save's <c>DayDiscovered</c> counter.</summary>
    public static bool ApplyDayDiscovered(WorldSaveData data, int day)
    {
        var p = data.Raw.Properties.FindByPrefix("DayDiscovered")?.Property;
        if (p is not IntProperty) return false;
        p.Value = day;
        return true;
    }

    /// <summary>
    /// Removes a creature's entry from <c>LeyakContainmentIDs</c> - the editor
    /// equivalent of releasing it from its containment unit. Returns true on removal.
    /// </summary>
    public static bool RemoveLeyakContainment(WorldSaveData data, string creature)
    {
        if (data.Raw.Properties.FindByPrefix("LeyakContainmentIDs")?.Property is not MapProperty mp
            || mp.Value is null)
        {
            return false;
        }
        for (var i = mp.Value.Count - 1; i >= 0; i--)
        {
            if (string.Equals(WorldSaveReader.ExtractMapKeyString(mp.Value[i].Key), creature, StringComparison.OrdinalIgnoreCase))
            {
                mp.Value.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    public static int RemoveDroppedItems(WorldSaveData data, IReadOnlyCollection<string> ids)
    {
        var tag = data.Raw.Properties.FindByPrefix("DroppedItemMap");
        if (tag?.Property is not MapProperty mp || mp.Value is null || ids.Count == 0) return 0;

        var idSet = ids as ISet<string> ?? new HashSet<string>(ids, StringComparer.Ordinal);
        var removed = 0;
        for (var i = mp.Value.Count - 1; i >= 0; i--)
        {
            var key = WorldSaveReader.ExtractMapKeyString(mp.Value[i].Key);
            if (key is not null && idSet.Contains(key))
            {
                mp.Value.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    /// <summary>
    /// Writes <paramref name="data"/>'s raw save to disk. The previous file content is
    /// preserved as <c>&lt;path&gt;.bak</c> so one bad write can't destroy a save.
    /// </summary>
    public static void WriteToFile(WorldSaveData data, string path)
    {
        Diagnostics.EditorLog.Info("WorldSave", $"Writing {path} (previous content kept as {Path.GetFileName(path)}.bak)");
        try
        {
            Saves.SaveBackup.CreateFor(path);
            using var fs = File.Create(path);
            data.Raw.WriteTo(fs);
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Error("WorldSave", $"Failed to write {path}", ex);
            throw;
        }
    }

    // ---------- lookup builders ----------

    private static Dictionary<string, IList<FPropertyTag>> BuildDeployedLookup(WorldSaveData data)
    {
        var result = new Dictionary<string, IList<FPropertyTag>>(StringComparer.Ordinal);
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, "DeployedObjectMap");
        if (pairs is null) return result;

        foreach (var kvp in pairs)
        {
            var key = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (key is null) continue;
            if (kvp.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
            {
                result[key] = ps.Properties;
            }
        }
        return result;
    }

    private static Dictionary<string, IList<FPropertyTag>> BuildCustomLookup(WorldSaveData data)
    {
        var result = new Dictionary<string, IList<FPropertyTag>>(StringComparer.Ordinal);
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, "CustomInventoryMap");
        if (pairs is null) return result;

        foreach (var kvp in pairs)
        {
            var key = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (key is null) continue;
            if (kvp.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
            {
                result[key] = ps.Properties;
            }
        }
        return result;
    }

    // ---------- container / inventory writers ----------

    private static void ApplyContainerInventoriesArray(IList<FPropertyTag> deployableProps, IReadOnlyList<WorldInventory> updated)
    {
        var tag = deployableProps.FindByPrefix("ContainerInventories_");
        if (tag?.Property is not ArrayProperty array || array.Value is null) return;

        for (var i = 0; i < array.Value.Length && i < updated.Count; i++)
        {
            if (array.Value.GetValue(i) is not StructProperty outer || outer.Value is not PropertiesStruct ps)
                continue;
            ApplyInventoryStruct(ps.Properties, updated[i]);
        }
    }

    private static void ApplyInventoryStruct(IList<FPropertyTag> inventoryStructProps, WorldInventory inv)
    {
        var content = inventoryStructProps.FindByPrefix("InventoryContent_");
        if (content?.Property is not ArrayProperty array || array.Value is null) return;

        for (var i = 0; i < array.Value.Length && i < inv.Slots.Count; i++)
        {
            if (array.Value.GetValue(i) is not StructProperty outer || outer.Value is not PropertiesStruct ps)
                continue;
            ApplySlot(ps.Properties, inv.Slots[i]);
        }
    }

    /// <summary>
    /// Slot mutator. Kept private and parallel to <c>PlayerSaveWriter.ApplySlot</c> rather
    /// than reaching into it - the slot struct is shared but the writer surface isn't.
    /// </summary>
    private static void ApplySlot(IList<FPropertyTag> slotProps, InventoryItemSlot newSlot)
    {
        if (!string.IsNullOrEmpty(newSlot.ItemId))
        {
            var rowHandle = slotProps.FindByPrefix("ItemDataTable_");
            if (rowHandle?.Property is StructProperty rhSp && rhSp.Value is PropertiesStruct rhPs)
            {
                SetName(rhPs.Properties, "RowName", newSlot.ItemId);
            }
        }

        var changeable = slotProps.FindByPrefix("ChangeableData_");
        if (changeable?.Property is not StructProperty cSp || cSp.Value is not PropertiesStruct cPs) return;

        // Same sparse-field handling as PlayerSaveWriter.ApplySlot: the game omits
        // default-valued ChangeableData members, so each tag is created when absent
        // (the inner member names are identical between player and world saves).
        var p = cPs.Properties;
        SetInt(p, "CurrentStack_", newSlot.Count, PlayerSaveWriter.FullNames.CurrentStack);
        SetDouble(p, "CurrentItemDurability_", newSlot.Durability, PlayerSaveWriter.FullNames.CurrentItemDurability);
        SetDouble(p, "MaxItemDurability_", newSlot.MaxDurability, PlayerSaveWriter.FullNames.MaxItemDurability);
        SetInt(p, "CurrentAmmoInMagazine_", newSlot.AmmoInMagazine, PlayerSaveWriter.FullNames.CurrentAmmoInMagazine);
        SetInt(p, "LiquidLevel_", newSlot.LiquidLevel, PlayerSaveWriter.FullNames.LiquidLevel);
        SetBool(p, "DynamicState_", newSlot.DynamicState, PlayerSaveWriter.FullNames.DynamicState);
        // Null means "no player text" - never create a tag just to hold null.
        SetString(p, "PlayerMadeString_", newSlot.PlayerMadeString,
            newSlot.PlayerMadeString is null ? null : PlayerSaveWriter.FullNames.PlayerMadeString);
    }

    // ---------- primitive setters ----------

    /// <summary>
    /// Finds the property matching <paramref name="prefix"/>; when absent and
    /// <paramref name="createFullName"/> is given, creates and appends a fresh tag of
    /// <paramref name="typeName"/> (mirror of <c>PlayerSaveWriter.FindOrCreate</c>;
    /// AF delta-serializes, so default-valued members are missing from healthy saves).
    /// </summary>
    private static FProperty? FindOrCreate(IList<FPropertyTag> tags, string prefix, string? createFullName, string typeName)
    {
        var existing = tags.FindByPrefix(prefix)?.Property;
        if (existing is not null || createFullName is null)
        {
            return existing;
        }

        var name = new FString(createFullName);
        var type = new FPropertyTypeName(name: new FString(typeName));
        var property = FProperty.Create(name, type);
        tags.Add(new FPropertyTag(name, type, EPropertyTagFlags.None) { Property = property });
        return property;
    }

    private static void SetDouble(IList<FPropertyTag> tags, string prefix, double value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(DoubleProperty));
        if (p is not null) p.Value = value;
    }

    private static void SetInt(IList<FPropertyTag> tags, string prefix, int value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(IntProperty));
        if (p is not null) p.Value = value;
    }

    private static void SetBool(IList<FPropertyTag> tags, string prefix, bool value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(BoolProperty));
        if (p is not null) p.Value = value;
    }

    private static void SetString(IList<FPropertyTag> tags, string prefix, string? value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(StrProperty));
        if (p is not null) p.Value = value is null ? null : (object)new FString(value);
    }

    private static void SetName(IList<FPropertyTag> tags, string prefix, string value)
    {
        var p = tags.FindByPrefix(prefix)?.Property;
        if (p is not null) p.Value = new FString(value);
    }

    /// <summary>
    /// Setter for an enum <see cref="ByteProperty"/>. ByteProperty serializes as
    /// either a single byte or a length-prefixed FString depending on the
    /// underlying Value type - we preserve whichever variant the save already
    /// uses so the serialized layout is unchanged.
    /// </summary>
    private static void SetEnumByte(IList<FPropertyTag> tags, string prefix, string value)
    {
        var p = tags.FindByPrefix(prefix)?.Property;
        if (p is null) return;

        switch (p.Value)
        {
            case byte:
                // Caller passed an enum value name but this slot is the compact
                // byte variant - try to parse, fall back to leaving it alone.
                if (byte.TryParse(value, out var b)) p.Value = b;
                break;
            case FString:
            case null:
            default:
                p.Value = new FString(value);
                break;
        }
    }
}
