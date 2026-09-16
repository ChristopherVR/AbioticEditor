using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.Saves;

/// <summary>
/// Shared low-level GVAS tag mutators used by the save writers. Everything operates on
/// an existing tag found by prefix (property names carry blueprint-compiler hash
/// suffixes), optionally creating a missing tag from its exact full hash-suffixed name.
///
/// Why creation is needed: Abiotic Factor delta-serializes - properties at their
/// blueprint default are omitted from the file, so a prefix lookup can legitimately
/// fail on a healthy save. Without creation the edit would silently no-op. New tags use
/// <see cref="EPropertyTagFlags.None"/>, matching every game-written primitive tag
/// observed in fixture saves.
/// </summary>
internal static class GvasTags
{
    /// <summary>Replaces the contents of an existing Name/Str array property; no-op when absent.</summary>
    /// <summary>
    /// Replaces a string array's contents. When the array is absent and
    /// <paramref name="createFullName"/> is given it is created first, because the game
    /// delta-serializes an empty array away entirely: a world that has never unlocked
    /// anything simply has no such tag, and silently doing nothing there loses the edit.
    /// </summary>
    public static void ReplaceNameArray(
        IList<FPropertyTag> tags, string prefix, IReadOnlyList<string> values, string? createFullName = null)
    {
        if (tags.FindByPrefix(prefix)?.Property is not ArrayProperty array)
        {
            if (createFullName is null) return;
            array = CreateNameArray(tags, createFullName);
        }

        var items = new FString[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            items[i] = new FString(values[i]);
        }
        array.Value = items;
    }

    /// <summary>
    /// Snapshot of a Name/Str array's current string values; empty (never null) when the
    /// tag is absent. Used to diff an array's old and new contents (e.g. syncing the
    /// "NEW" badge arrays to a recipe/codex/journal/fish unlock edit).
    /// </summary>
    public static IReadOnlyList<string> ReadNameArray(IList<FPropertyTag> tags, string prefix)
    {
        if (tags.FindByPrefix(prefix)?.Property is not ArrayProperty array || array.Value is null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>(array.Value.Length);
        for (var i = 0; i < array.Value.Length; i++)
        {
            if (array.Value.GetValue(i) is { } v) result.Add(v.ToString() ?? string.Empty);
        }
        return result;
    }

    /// <summary>
    /// Appends an empty <c>ArrayProperty</c> of <c>NameProperty</c>. The element type has to
    /// live in the tag's type PARAMETERS, not just on the property: that is where the reader
    /// takes it from, so an array created without it writes a file that cannot be read back
    /// ("Failed to read item type for ArrayProperty").
    /// </summary>
    private static ArrayProperty CreateNameArray(IList<FPropertyTag> tags, string fullName)
    {
        var name = new FString(fullName);
        var itemType = new FPropertyTypeName(new FString("NameProperty"));
        var type = new FPropertyTypeName(new FString("ArrayProperty"), [itemType]);
        var array = (ArrayProperty)FProperty.Create(name, type);
        array.ItemType = itemType;
        tags.Add(new FPropertyTag(name, type, EPropertyTagFlags.None) { Property = array });
        return array;
    }

    /// <summary>
    /// Finds the property matching <paramref name="prefix"/>. When absent and
    /// <paramref name="createFullName"/> is given, a new <see cref="FPropertyTag"/> of
    /// <paramref name="typeName"/> is created and appended (the trailing <c>None</c>
    /// terminator is emitted by the serializer, so appending is safe).
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

    public static void SetFloat(IList<FPropertyTag> tags, string prefix, float value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(FloatProperty));
        p?.Value = value;
    }

    public static void SetDouble(IList<FPropertyTag> tags, string prefix, double value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(DoubleProperty));
        p?.Value = value;
    }

    public static void SetInt(IList<FPropertyTag> tags, string prefix, int value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(IntProperty));
        p?.Value = value;
    }

    public static void SetBool(IList<FPropertyTag> tags, string prefix, bool value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(BoolProperty));
        p?.Value = value;
    }

    public static void SetString(IList<FPropertyTag> tags, string prefix, string? value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(StrProperty));
        // StrProperty stores null differently than empty string; preserve null.
        p?.Value = value is null ? null : (object)new FString(value);
    }

    /// <summary>Sets an existing (or, with <paramref name="createFullName"/>, freshly created)
    /// NameProperty's value. NameProperty stores FString values.</summary>
    public static void SetName(IList<FPropertyTag> tags, string prefix, string value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(NameProperty));
        p?.Value = new FString(value);
    }

    /// <summary>
    /// Sets a top-level Rotator StructProperty (e.g. <c>LastControlRotation_</c>) - stored the
    /// same way the player save stores <c>LastSafeWorldLocation_</c> Vector structs, just with
    /// <c>StructType</c> "Rotator" instead of "Vector" (both deserialize to the same
    /// <see cref="VectorStruct"/> data, X/Y/Z holding pitch/yaw/roll). Creates the tag with
    /// <paramref name="createFullName"/> when absent.
    /// </summary>
    public static void SetRotator(
        IList<FPropertyTag> tags, string prefix, double pitch, double yaw, double roll, string? createFullName = null)
    {
        if (tags.FindByPrefix(prefix)?.Property is StructProperty existing)
        {
            existing.Value = new VectorStruct { Value = new FVector { X = pitch, Y = yaw, Z = roll } };
            return;
        }
        if (createFullName is null) return;

        var name = new FString(createFullName);
        var structType = new FPropertyTypeName(new FString("Rotator"));
        var type = new FPropertyTypeName(new FString(nameof(StructProperty)), new[] { structType });
        var property = new StructProperty(name)
        {
            StructType = structType,
            StructGuid = Guid.Empty,
            Value = new VectorStruct { Value = new FVector { X = pitch, Y = yaw, Z = roll } },
        };
        tags.Add(new FPropertyTag(name, type, EPropertyTagFlags.None) { Property = property });
    }
}
