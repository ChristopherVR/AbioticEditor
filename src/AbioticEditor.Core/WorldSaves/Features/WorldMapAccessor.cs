using System.Globalization;
using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.WorldSaves.Features;

/// <summary>
/// Read/write helpers shared by every <see cref="IWorldMapFeature"/>. Mirrors the private
/// idiom inside <see cref="WorldSaveReader"/>/<see cref="WorldSaveWriter"/> (map = entries of
/// <c>StructProperty -&gt; PropertiesStruct</c>; leaves found by prefix; missing leaves created
/// on demand because AF delta-serializes default values) but exposed publicly so feature
/// modules stay self-contained and never have to edit the big reader/writer files.
///
/// <para>Every setter patches an existing leaf in place (or appends a new tag when the leaf
/// is absent), so untouched data re-serializes byte-perfect.</para>
/// </summary>
public static class WorldMapAccessor
{
    /// <summary>True when <paramref name="save"/> carries a <c>MapProperty</c> named <paramref name="mapName"/>.</summary>
    public static bool HasMap(SaveGame save, string mapName)
        => WorldSaveReader.GetMapPairs(save?.Properties, mapName) is not null;

    /// <summary>The raw key/value pairs of the named map, or null when absent.</summary>
    public static IList<KeyValuePair<FProperty, FProperty>>? GetPairs(SaveGame save, string mapName)
        => WorldSaveReader.GetMapPairs(save?.Properties, mapName);

    /// <summary>
    /// Enumerates the map as <c>(key, structProps)</c> for entries whose value is the usual
    /// <c>StructProperty -&gt; PropertiesStruct</c>. Skips malformed entries.
    /// </summary>
    public static IEnumerable<WorldMapEntryProps> Entries(SaveGame save, string mapName)
    {
        var pairs = GetPairs(save, mapName);
        if (pairs is null)
        {
            yield break;
        }
        foreach (var kvp in pairs)
        {
            var key = WorldSaveReader.ExtractMapKeyString(kvp.Key);
            if (key is null)
            {
                continue;
            }
            if (kvp.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
            {
                yield return new WorldMapEntryProps(key, ps.Properties);
            }
        }
    }

    /// <summary>The struct property list for one map entry, or null when the key isn't present.</summary>
    public static IList<FPropertyTag>? FindEntry(SaveGame save, string mapName, string key)
    {
        foreach (var entry in Entries(save, mapName))
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                return entry.Props;
            }
        }
        return null;
    }

    /// <summary>Removes the entry with <paramref name="key"/> from the map. Returns true when removed.</summary>
    public static bool RemoveEntry(SaveGame save, string mapName, string key)
    {
        var pairs = GetPairs(save, mapName);
        if (pairs is null)
        {
            return false;
        }
        for (var i = pairs.Count - 1; i >= 0; i--)
        {
            if (string.Equals(WorldSaveReader.ExtractMapKeyString(pairs[i].Key), key, StringComparison.Ordinal))
            {
                pairs.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    // ---------- a few common struct shapes ----------

    /// <summary>Reads an <c>FVector</c>-typed struct leaf (e.g. a location) as (x,y,z), or null.</summary>
    public static (double X, double Y, double Z)? GetVector(IList<FPropertyTag> props, string prefix)
        => props.FindByPrefix(prefix)?.Property is StructProperty sp && sp.Value is VectorStruct v
            ? (v.Value.X, v.Value.Y, v.Value.Z)
            : null;

    /// <summary>Sets an existing <c>FVector</c> struct leaf in place. Returns false when absent.</summary>
    public static bool SetVector(IList<FPropertyTag> props, string prefix, double x, double y, double z)
    {
        if (props.FindByPrefix(prefix)?.Property is not StructProperty sp || sp.Value is not VectorStruct vec)
        {
            return false;
        }
        var value = vec.Value;
        value.X = x;
        value.Y = y;
        value.Z = z;
        vec.Value = value;
        return true;
    }

    // ---------- soft-object-path leaf (e.g. a tram's LastStation_) ----------

    /// <summary>
    /// Reads the three components of a <c>SoftObjectPath</c> struct leaf (PackageName,
    /// AssetName, SubPathString), or null when the leaf is absent or not a soft path.
    /// </summary>
    /// <remarks>
    /// <c>SoftObjectPathStruct</c> is internal to UeSaveGame, so its public <c>Value</c>
    /// (a <see cref="UeSaveGame.DataTypes.SoftObjectPath"/>) is reached by reflection - the
    /// same approach <c>TramMapFeature</c> uses for reads.
    /// </remarks>
    public static (string? Package, string? Asset, string? SubPath)? GetSoftObjectPath(
        IList<FPropertyTag> props, string prefix)
    {
        if (GetSoftObjectPathValue(props, prefix) is not { } sop)
        {
            return null;
        }
        return (sop.PackageName?.Value, sop.AssetName?.Value, sop.SubPathString?.Value);
    }

    /// <summary>
    /// Sets the <c>SubPathString</c> of an existing <c>SoftObjectPath</c> struct leaf in place,
    /// leaving PackageName/AssetName untouched (they are constant for a given map). Returns false
    /// when the leaf is absent or isn't a soft path. Used to re-park a tram at a different station.
    /// </summary>
    public static bool SetSoftObjectSubPath(IList<FPropertyTag> props, string prefix, string? subPath)
    {
        if (GetSoftObjectPathValue(props, prefix) is not { } sop)
        {
            return false;
        }
        sop.SubPathString = subPath is null ? null : new FString(subPath);
        return true;
    }

    private static UeSaveGame.DataTypes.SoftObjectPath? GetSoftObjectPathValue(
        IList<FPropertyTag> props, string prefix)
    {
        if (props.FindByPrefix(prefix)?.Property is not StructProperty sp || sp.Value is null)
        {
            return null;
        }
        try
        {
            // SoftObjectPathStruct.Value : SoftObjectPath (public property on an internal type).
            var innerProp = sp.Value.GetType().GetProperty("Value");
            return innerProp?.GetValue(sp.Value) as UeSaveGame.DataTypes.SoftObjectPath;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------- primitive setters (create-on-absent) ----------

    /// <summary>
    /// Finds the leaf matching <paramref name="prefix"/>; when absent and
    /// <paramref name="createFullName"/> is supplied, creates and appends a fresh tag of
    /// <paramref name="typeName"/>. Without a full name an absent leaf is left absent.
    /// </summary>
    public static FProperty? FindOrCreate(IList<FPropertyTag> tags, string prefix, string? createFullName, string typeName)
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

    public static bool SetBool(IList<FPropertyTag> tags, string prefix, bool value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(BoolProperty));
        if (p is null)
        {
            return false;
        }
        p.Value = value;
        return true;
    }

    public static bool SetInt(IList<FPropertyTag> tags, string prefix, int value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(IntProperty));
        if (p is null)
        {
            return false;
        }
        p.Value = value;
        return true;
    }

    public static bool SetDouble(IList<FPropertyTag> tags, string prefix, double value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(DoubleProperty));
        if (p is null)
        {
            return false;
        }
        p.Value = value;
        return true;
    }

    public static bool SetFloat(IList<FPropertyTag> tags, string prefix, float value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(FloatProperty));
        if (p is null)
        {
            return false;
        }
        p.Value = value;
        return true;
    }

    /// <summary>Sets a Str/Name leaf's <c>FString</c> value. Returns false when absent (and not created).</summary>
    public static bool SetString(IList<FPropertyTag> tags, string prefix, string? value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(StrProperty));
        if (p is null)
        {
            return false;
        }
        p.Value = value is null ? null : new FString(value);
        return true;
    }

    /// <summary>Sets an existing Name/Str leaf to <paramref name="value"/>. Returns false when absent.</summary>
    public static bool SetName(IList<FPropertyTag> tags, string prefix, string value)
    {
        var p = tags.FindByPrefix(prefix)?.Property;
        if (p is null)
        {
            return false;
        }
        p.Value = new FString(value);
        return true;
    }

    /// <summary>
    /// Sets an enum <see cref="ByteProperty"/>, preserving whichever wire variant the save uses
    /// (compact byte vs length-prefixed string). Returns false when the leaf is absent.
    /// </summary>
    public static bool SetEnumByte(IList<FPropertyTag> tags, string prefix, string value)
    {
        var p = tags.FindByPrefix(prefix)?.Property;
        if (p is null)
        {
            return false;
        }
        switch (p.Value)
        {
            case byte:
                if (byte.TryParse(value, out var b))
                {
                    p.Value = b;
                }
                break;
            default:
                p.Value = new FString(value);
                break;
        }
        return true;
    }

    // ---------- parse helpers for the generic SetField path ----------

    /// <summary>Parses a user/CLI string as a bool (accepts true/false/1/0/yes/no/on/off).</summary>
    public static bool TryParseBool(string? text, out bool value)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        switch (text.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "yes" or "y" or "on":
                value = true;
                return true;
            case "false" or "0" or "no" or "n" or "off":
                value = false;
                return true;
            default:
                return false;
        }
    }

    public static bool TryParseInt(string? text, out int value)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static bool TryParseDouble(string? text, out double value)
        => double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

/// <summary>One map entry's key paired with its struct property list (for reading/patching).</summary>
public readonly record struct WorldMapEntryProps(string Key, IList<FPropertyTag> Props);
