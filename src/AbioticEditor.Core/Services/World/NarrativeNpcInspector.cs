using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using UeSaveGame.TextData;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>One <c>{Key: EDynamicProperty, Value: ...}</c> element of a saved NPC's dynamic-property list.</summary>
/// <param name="Key">The enum value as stored, for example <c>EDynamicProperty::XP</c>.</param>
/// <param name="ValueType">The GVAS property type of the value (for example <c>IntProperty</c>).</param>
/// <param name="Value">The value rendered as text (invariant culture).</param>
public sealed record NarrativeNpcDynamicProperty(string Key, string ValueType, string Value);

/// <summary>
/// A read-only, complete view of one <c>NarrativeNPCMap</c> (or <c>PetNPC</c>) entry: every member
/// the game saved, including the ones <see cref="WorldNpc"/> does not carry. It exists so the
/// editor can show what a save really holds without offering controls whose meaning is unverified.
/// </summary>
/// <param name="Id">The map key (level actor path, or the pet GUID).</param>
/// <param name="ActorPath">The entry's own <c>ActorPath</c> member, when present.</param>
/// <param name="IsDead">The saved <c>IsDead</c> flag. This is the game's own scripting state, not a verified "alive/dead" control.</param>
/// <param name="NarrativeState">The saved <c>NarrativeState</c> enum string (a compiler-named enumerator, no friendly names exist).</param>
/// <param name="CustomName">The player-given name, or null when the saved text is empty.</param>
/// <param name="NpcClass">The saved <c>NPCClass</c> soft path, or null when it is unset (<c>None</c>).</param>
/// <param name="X">Saved location X (centimetres), 0 when not stored.</param>
/// <param name="Y">Saved location Y (centimetres), 0 when not stored.</param>
/// <param name="Z">Saved location Z (centimetres), 0 when not stored.</param>
/// <param name="LimbHealth">The <c>CurrentHealthMap</c> keyed by limb enum string; empty when the game saved none.</param>
/// <param name="DynamicProperties">The <c>DynamicProperties</c> list; empty when the game saved none.</param>
/// <param name="UnmodeledMembers">Names (hash suffix stripped) of entry members outside the eight known ones.</param>
/// <param name="IsPet">True when the entry came from <c>PetNPC</c>.</param>
public sealed record NarrativeNpcDetails(
    string Id,
    string? ActorPath,
    bool IsDead,
    string? NarrativeState,
    string? CustomName,
    string? NpcClass,
    double X,
    double Y,
    double Z,
    IReadOnlyDictionary<string, double> LimbHealth,
    IReadOnlyList<NarrativeNpcDynamicProperty> DynamicProperties,
    IReadOnlyList<string> UnmodeledMembers,
    bool IsPet = false)
{
    /// <summary>True when the saved location is anything other than the origin.</summary>
    public bool HasLocation => X != 0 || Y != 0 || Z != 0;

    /// <summary>
    /// True when the entry carries anything beyond the dead flag and state: a name, a class, a
    /// location, health, dynamic properties or an unmodeled member.
    /// </summary>
    public bool HasExtraData => CustomName is not null || NpcClass is not null || HasLocation
        || LimbHealth.Count > 0 || DynamicProperties.Count > 0 || UnmodeledMembers.Count > 0;
}

/// <summary>
/// Reads every saved narrative-NPC entry in full, without modelling a write path. Deliberately
/// does not turn <c>IsDead</c> or the script stage into a universal control: the meaning of both
/// is per character (see docs/reference/research/research-narrative-npcs.md).
/// </summary>
public static class NarrativeNpcInspector
{
    private static readonly string[] KnownMembers =
    [
        "ActorPath", "CurrentHealthMap", "CustomName", "DynamicProperties",
        "IsDead", "Location", "NPCClass", "NarrativeState",
    ];

    /// <summary>Reads the <c>NarrativeNPCMap</c> (story NPCs, traders, holograms).</summary>
    public static IReadOnlyList<NarrativeNpcDetails> ReadNarrative(WorldSaveData data)
        => Read(data, "NarrativeNPCMap", isPet: false);

    /// <summary>Reads the <c>PetNPC</c> map (tamed companions), which shares the same entry struct.</summary>
    public static IReadOnlyList<NarrativeNpcDetails> ReadPets(WorldSaveData data)
        => Read(data, "PetNPC", isPet: true);

    private static List<NarrativeNpcDetails> Read(WorldSaveData data, string mapPrefix, bool isPet)
    {
        ArgumentNullException.ThrowIfNull(data);
        var result = new List<NarrativeNpcDetails>();
        var pairs = WorldSaveReader.GetMapPairs(data.Raw.Properties, mapPrefix);
        if (pairs is null) return result;

        foreach (var kvp in pairs)
        {
            var id = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (id is null || kvp.Value is not StructProperty { Value: PropertiesStruct ps }) continue;
            result.Add(ReadEntry(id, ps.Properties, isPet));
        }
        return result;
    }

    private static NarrativeNpcDetails ReadEntry(string id, IList<FPropertyTag> p, bool isPet)
    {
        double x = 0, y = 0, z = 0;
        if (p.FindByPrefix("Location_")?.Property is StructProperty { Value: VectorStruct loc })
        {
            x = loc.Value.X;
            y = loc.Value.Y;
            z = loc.Value.Z;
        }

        var unmodeled = p
            .Select(t => t.Name?.Value)
            .Where(n => n is not null)
            .Select(n => Compatibility.UnmodeledFieldCensus.Normalize(n!))
            .Where(n => !KnownMembers.Contains(n, StringComparer.Ordinal))
            .ToList();

        return new NarrativeNpcDetails(
            id,
            EmptyToNull(p.FindByPrefix("ActorPath_")?.Property?.Value?.ToString()),
            p.TryGetBool("IsDead_") ?? false,
            EmptyToNull(p.FindByPrefix("NarrativeState_")?.Property?.Value?.ToString()),
            EmptyToNull(ReadText(p.FindByPrefix("CustomName_")?.Property)),
            NullIfNone(p.FindByPrefix("NPCClass_")?.Property?.Value?.ToString()),
            x, y, z,
            ReadLimbHealth(p),
            ReadDynamicProperties(p),
            unmodeled,
            isPet);
    }

    private static string? ReadText(FProperty? property)
    {
        if (property is TextProperty { Value: FText ft })
        {
            return ft.Value is TextData_None none ? none.Value?.Value : ft.ToString();
        }
        return property?.Value?.ToString();
    }

    private static Dictionary<string, double> ReadLimbHealth(IList<FPropertyTag> p)
    {
        var dict = new Dictionary<string, double>(StringComparer.Ordinal);
        if (p.FindByPrefix("CurrentHealthMap_")?.Property is MapProperty { Value: { } pairs })
        {
            foreach (var kv in pairs)
            {
                if (kv.Key?.Value?.ToString() is { } key)
                {
                    dict[key] = kv.Value?.Value switch { double d => d, float f => f, int i => i, _ => 0 };
                }
            }
        }
        return dict;
    }

    private static List<NarrativeNpcDynamicProperty> ReadDynamicProperties(IList<FPropertyTag> p)
    {
        var list = new List<NarrativeNpcDynamicProperty>();
        if (p.FindByPrefix("DynamicProperties_")?.Property is not ArrayProperty { Value: { } values }) return list;

        for (var i = 0; i < values.Length; i++)
        {
            if (values.GetValue(i) is not StructProperty { Value: PropertiesStruct eps }) continue;
            var key = eps.Properties.FindByPrefix("Key")?.Property?.Value?.ToString() ?? string.Empty;
            var valueProp = eps.Properties.FindByPrefix("Value")?.Property;
            var text = Convert.ToString(valueProp?.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            list.Add(new NarrativeNpcDynamicProperty(key, valueProp?.GetType().Name ?? "?", text));
        }
        return list;
    }

    private static string? EmptyToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? NullIfNone(string? s)
        => string.IsNullOrWhiteSpace(s) || s == "None" || s == "None.None" ? null : s;
}
