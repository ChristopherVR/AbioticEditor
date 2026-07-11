using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.PlayerSaves;

// PlayerSaveWriter - low-level GVAS tag helpers (find-or-create by full name, typed setters).
public static partial class PlayerSaveWriter
{
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
    /// Finds the property matching <paramref name="prefix"/>. When absent and
    /// <paramref name="createFullName"/> is given, a new <see cref="FPropertyTag"/> of
    /// <paramref name="typeName"/> is created and appended (the trailing <c>None</c>
    /// terminator is emitted by the serializer, so appending is safe).
    ///
    /// Why creation is needed: Abiotic Factor delta-serializes - properties at their
    /// blueprint default are omitted from the file, so a prefix lookup can legitimately
    /// fail on a healthy save (see <see cref="FullNames"/>). Without creation the edit
    /// would silently no-op. New tags use <see cref="EPropertyTagFlags.None"/>, matching
    /// every game-written primitive tag observed in fixture saves.
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

    /// <summary>
    /// Finds a Name-typed <see cref="ArrayProperty"/> matching <paramref name="prefix"/>.
    /// When absent and <paramref name="createFullName"/> is given, a new empty array is
    /// created and appended (element type <c>NameProperty</c>, matching how the game
    /// serializes <see cref="FullNames.Traits"/>-style row-name arrays). Same rationale as
    /// <see cref="FindOrCreate"/>, but arrays need both an item type on the property and a
    /// matching type parameter on the owning tag, so they can't share that helper.
    /// </summary>
    private static ArrayProperty? FindOrCreateNameArray(IList<FPropertyTag> tags, string prefix, string? createFullName)
    {
        if (tags.FindByPrefix(prefix)?.Property is ArrayProperty existing) return existing;
        if (createFullName is null) return null;

        var name = new FString(createFullName);
        var itemType = new FPropertyTypeName(new FString(nameof(NameProperty)));
        var type = new FPropertyTypeName(new FString(nameof(ArrayProperty)), new[] { itemType });
        var property = new ArrayProperty(name, itemType) { Value = Array.Empty<FString>() };
        tags.Add(new FPropertyTag(name, type, EPropertyTagFlags.None) { Property = property });
        return property;
    }

    private static void SetFloat(IList<FPropertyTag> tags, string prefix, float value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(FloatProperty));
        if (p is not null)
        {
            p.Value = value;
        }
    }

    private static void SetDouble(IList<FPropertyTag> tags, string prefix, double value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(DoubleProperty));
        if (p is not null)
        {
            p.Value = value;
        }
    }

    private static void SetInt(IList<FPropertyTag> tags, string prefix, int value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(IntProperty));
        if (p is not null)
        {
            p.Value = value;
        }
    }

    private static void SetBool(IList<FPropertyTag> tags, string prefix, bool value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(BoolProperty));
        if (p is not null)
        {
            p.Value = value;
        }
    }

    private static void SetString(IList<FPropertyTag> tags, string prefix, string? value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(StrProperty));
        if (p is not null)
        {
            // StrProperty stores null differently than empty string; preserve null.
            p.Value = value is null ? null : (object)new FString(value);
        }
    }

    /// <summary>The data table the item catalog is read from. An added item's row handle must
    /// point here (not the empty-slot default) so the game can resolve and render it.</summary>
    internal const string ItemTableGlobalPath = "/Game/Blueprints/Items/ItemTable_Global.ItemTable_Global";

    /// <summary>
    /// Sets a <c>DataTableRowHandle</c>'s <c>DataTable</c> object reference (an
    /// <see cref="ObjectProperty"/> whose path lives in <c>ObjectType</c>). No-op when the
    /// property is absent or is not an object reference.
    /// </summary>
    internal static void SetObjectPath(IList<FPropertyTag> tags, string prefix, string path)
    {
        if (tags.FindByPrefix(prefix)?.Property is ObjectProperty op)
        {
            op.ObjectType = new FString(path);
        }
    }

    private static void SetName(IList<FPropertyTag> tags, string prefix, string value, string? createFullName = null)
    {
        var p = FindOrCreate(tags, prefix, createFullName, nameof(NameProperty));
        if (p is not null)
        {
            // NameProperty stores FString values.
            p.Value = new FString(value);
        }
    }
}
