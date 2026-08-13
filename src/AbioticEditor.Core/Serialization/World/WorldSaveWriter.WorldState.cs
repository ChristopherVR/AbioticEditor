using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;

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

            return SetCustomTextDisplay(ps.Properties.FindByPrefix("CustomTextDisplay_")?.Property, text);
        }
        return false;
    }

    /// <summary>
    /// The deployable text slot, which the game writes as either a plain string or a piece of
    /// localizable text depending on how it was set. Both shapes have to be handled here rather
    /// than at the call sites, because the two are indistinguishable from the outside and a save
    /// can carry a mix of them.
    /// </summary>
    private static bool SetCustomTextDisplay(FProperty? property, string text)
    {
        switch (property)
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

    /// <summary>
    /// Re-homes every claim held by <paramref name="oldOwnerId"/> in <c>DeployedObjectMap</c> to
    /// <paramref name="newOwnerId"/> and returns how many were rewritten. Everything from the
    /// separator onwards is carried over untouched, so the claimer's name survives exactly -
    /// including the invisible private-use glyphs some in-game names wrap themselves in, which
    /// the display path strips but the file must keep.
    ///
    /// <para>Only a text that <em>starts</em> with <c>&lt;oldOwnerId&gt;}|!|{</c> counts. The game
    /// reuses the same separator as the line break in sign text, so a hand-typed sign reading
    /// "Stolas}|!|{castle" is two lines rather than a claim by a player called Stolas, and a
    /// match anywhere in the middle of a string is somebody's typing, not an ownership record.</para>
    ///
    /// <para>This is the different-length-safe half of an owner-id change: because the save is
    /// re-serialized afterwards, the FString length prefixes are recomputed and the ids do not
    /// have to be the same size. See <see cref="WorldSteamIdPatcher"/> for the byte-level fast
    /// path used when they are.</para>
    /// </summary>
    public static int RewriteDeployableClaims(WorldSaveData data, string oldOwnerId, string newOwnerId)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrEmpty(oldOwnerId);
        ArgumentException.ThrowIfNullOrEmpty(newOwnerId);

        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, "DeployedObjectMap");
        if (pairs is null) return 0;

        var claimPrefix = oldOwnerId + WorldDeployable.ClaimSeparator;
        var rewritten = 0;
        foreach (var kvp in pairs)
        {
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;

            var property = ps.Properties.FindByPrefix("CustomTextDisplay_")?.Property;
            if (property?.Value?.ToString() is not { } current) continue;
            if (!current.StartsWith(claimPrefix, StringComparison.Ordinal)) continue;

            if (SetCustomTextDisplay(property, newOwnerId + current[oldOwnerId.Length..])) rewritten++;
        }
        return rewritten;
    }

    /// <summary>
    /// Replaces one of the metadata save's <c>GlobalUnlocks</c> name arrays (e.g.
    /// <c>GlobalItemsPickedUp_</c>). Returns false when the struct is absent.
    /// </summary>
    /// <summary>
    /// Exact, hash-suffixed names of the arrays inside <c>SaveData_GlobalUnlocks_Struct</c>.
    /// A world that has never unlocked anything omits the whole struct, and one that has
    /// unlocked only some kinds omits the rest of the arrays, so a prefix lookup legitimately
    /// finds nothing on a perfectly healthy save. Creating the tag needs its full name.
    /// </summary>
    private static readonly Dictionary<string, string> GlobalUnlockFullNames = new(StringComparer.Ordinal)
    {
        ["GlobalItemsPickedUp_"] = "GlobalItemsPickedUp_32_0D99146044C3330A30A4C4AB8980DAF4",
        ["GlobalEmailsRead_"] = "GlobalEmailsRead_34_0A562D184DBED267F898E6A3128557B4",
        ["GlobalJournalEntries_"] = "GlobalJournalEntries_36_0AB6A0E444B128E28D0741917389C897",
        ["GlobalCompendiumEmail_"] = "GlobalCompendiumEmail_38_181999554462F3D8CD3BC7AEF1037A2D",
        ["GlobalCompendiumNarrative_"] = "GlobalCompendiumNarrative_40_EEBC4619442FF6008D8282A684063892",
        ["GlobalCompendiumExploration_"] = "GlobalCompendiumExploration_42_D35947E2407A37D800A2538AB82EDEA5",
    };

    /// <summary>
    /// Writes one of the world-wide discovery lists (items seen, e-mails read, journal pages
    /// found, compendium sections), creating the <c>GlobalUnlocks</c> struct and the array
    /// itself when the world has never recorded that kind of unlock. Returns false only when
    /// the prefix is not one this writer knows how to create.
    /// </summary>
    public static bool ApplyGlobalUnlockArray(WorldSaveData data, string prefix, IReadOnlyList<string> values)
    {
        if (!GlobalUnlockFullNames.TryGetValue(prefix, out var fullName)) return false;

        var props = WorldSaveReader.GetGlobalUnlocksProps(data.Raw) ?? CreateGlobalUnlocksStruct(data.Raw);
        if (props is null) return false;
        ReplaceNameArray(props, prefix, values, fullName);
        return true;
    }

    /// <summary>
    /// Adds an empty <c>GlobalUnlocks</c> struct to a save that has none. The property is a
    /// plain top-level name (no blueprint hash suffix) whose struct type the game records as
    /// <c>SaveData_GlobalUnlocks_Struct</c>.
    /// </summary>
    private static IList<FPropertyTag>? CreateGlobalUnlocksStruct(SaveGame save)
    {
        var name = new FString("GlobalUnlocks");
        var type = new FPropertyTypeName(
            new FString("StructProperty"),
            [new FPropertyTypeName(new FString("SaveData_GlobalUnlocks_Struct"))]);
        if (save.Properties is not { } saveProperties) return null;
        if (FProperty.Create(name, type) is not StructProperty property) return null;

        var body = new PropertiesStruct { Properties = new List<FPropertyTag>() };
        property.Value = body;
        saveProperties.Add(new FPropertyTag(name, type, EPropertyTagFlags.None) { Property = property });
        return body.Properties;
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

    /// <summary>
    /// The exact top-level name of the containment map. Unlike a blueprint variable this one
    /// carries no compiler hash suffix (it is a native property on the save-game class), so the
    /// create-when-missing name is simply the property name. A world where nothing was ever
    /// contained omits the map entirely, which is why creating it has to be possible.
    /// </summary>
    private const string LeyakContainmentIDsName = "LeyakContainmentIDs";

    /// <summary>
    /// Points <paramref name="creature"/> at containment unit <paramref name="unitId"/> in the
    /// metadata save's <c>LeyakContainmentIDs</c> map, replacing any unit it was in before.
    /// Because the map is keyed by creature, a creature can only ever be in one unit - which is
    /// also why swapping two creatures is just swapping the two values.
    ///
    /// Creates the entry (and, on a world that never contained anything, the whole map) when
    /// absent. Returns false only when the save is not a metadata save shape at all.
    /// </summary>
    public static bool SetLeyakContainment(WorldSaveData data, string creature, string unitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(creature);
        ArgumentException.ThrowIfNullOrWhiteSpace(unitId);

        if (data.Raw.Properties is not { } tags) return false;
        var mp = FindOrCreateContainmentMap(tags);
        if (mp?.Value is null) return false;

        foreach (var pair in mp.Value)
        {
            if (!string.Equals(WorldSaveReader.ExtractMapKeyString(pair.Key), creature, StringComparison.OrdinalIgnoreCase)) continue;
            pair.Value.Value = new FString(unitId);
            return true;
        }

        // A fresh pair rather than a clone of an existing one: map elements carry no per-entry
        // type data (the key/value types live on the map tag itself), and the two element types
        // here are the simplest there are.
        var key = new NameProperty(new FString($"{LeyakContainmentIDsName}_Key")) { Value = new FString(creature) };
        var value = new StrProperty(new FString(LeyakContainmentIDsName)) { Value = new FString(unitId) };
        mp.Value.Add(new KeyValuePair<FProperty, FProperty>(key, value));
        return true;
    }

    private static MapProperty? FindOrCreateContainmentMap(IList<FPropertyTag> tags)
    {
        if (tags.FindByPrefix(LeyakContainmentIDsName)?.Property is MapProperty existing)
        {
            existing.Value ??= new List<KeyValuePair<FProperty, FProperty>>();
            return existing;
        }
        if (tags.FindByPrefix(LeyakContainmentIDsName) is not null) return null; // present but not a map

        var name = new FString(LeyakContainmentIDsName);
        var keyType = new FPropertyTypeName(new FString(nameof(NameProperty)));
        var valueType = new FPropertyTypeName(new FString(nameof(StrProperty)));
        var type = new FPropertyTypeName(new FString(nameof(MapProperty)), new[] { keyType, valueType });
        var map = new MapProperty(name)
        {
            KeyType = keyType,
            ValueType = valueType,
            Value = new List<KeyValuePair<FProperty, FProperty>>(),
        };
        tags.Add(new FPropertyTag(name, type, EPropertyTagFlags.None) { Property = map });
        return map;
    }

    /// <summary>
    /// Writes a containment unit's own record of what it holds: the
    /// <c>LeyakContainmentData</c> index in its <c>Generic3</c> slot, and optionally its
    /// stability in <c>Generic1</c>. <paramref name="data"/> is the <em>region</em> save the
    /// unit stands in, not the metadata save.
    ///
    /// Returns false when the unit is not in this save, or when it carries no
    /// <c>DynamicProperties_</c> array to patch (nothing to clone an element prototype from -
    /// see <see cref="PetDynamicProperties"/>), so a caller can report the edit rather than
    /// silently lose it.
    /// </summary>
    public static bool SetContainmentUnitCreatureIndex(WorldSaveData data, string unitId, int creatureIndex, int? stability = null)
    {
        if (WorldMapAccessor.FindEntry(data.Raw, "DeployedObjectMap", unitId) is not { } props) return false;
        if (!ContainmentCreatureCatalog.IsUnitClass(WorldSaveReader.ExtractClassName(props))) return false;
        if (props.FindByPrefix("ChangableData_")?.Property is not StructProperty cd
            || cd.Value is not PropertiesStruct cdps)
        {
            return false;
        }

        var ok = PetDynamicProperties.SetOrAdd(cdps.Properties, ContainmentDynamicSlots.CreatureIndex, creatureIndex);
        if (ok && stability is { } level)
        {
            PetDynamicProperties.SetOrAdd(cdps.Properties, ContainmentDynamicSlots.Stability,
                Math.Clamp(level, 0, ContainmentCreatureCatalog.MaxStability));
        }
        return ok;
    }
}
