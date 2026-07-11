using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.WorldSaves;

// WorldSaveWriter - world-state edits: quest flags, doors, story progression, clock, recipes.
public static partial class WorldSaveWriter
{
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
}
