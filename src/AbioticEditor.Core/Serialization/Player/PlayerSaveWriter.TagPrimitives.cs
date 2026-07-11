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
        => GvasTags.ReplaceNameArray(tags, prefix, values);

    /// <summary>
    /// Finds the property matching <paramref name="prefix"/>. When absent and
    /// <paramref name="createFullName"/> is given, a new <see cref="FPropertyTag"/> of
    /// <paramref name="typeName"/> is created and appended. See <see cref="FullNames"/>
    /// for why creation is needed (AF delta-serializes blueprint-default properties).
    /// </summary>
    private static FProperty? FindOrCreate(IList<FPropertyTag> tags, string prefix, string? createFullName, string typeName)
        => GvasTags.FindOrCreate(tags, prefix, createFullName, typeName);

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
        => GvasTags.SetFloat(tags, prefix, value, createFullName);

    private static void SetDouble(IList<FPropertyTag> tags, string prefix, double value, string? createFullName = null)
        => GvasTags.SetDouble(tags, prefix, value, createFullName);

    private static void SetInt(IList<FPropertyTag> tags, string prefix, int value, string? createFullName = null)
        => GvasTags.SetInt(tags, prefix, value, createFullName);

    private static void SetBool(IList<FPropertyTag> tags, string prefix, bool value, string? createFullName = null)
        => GvasTags.SetBool(tags, prefix, value, createFullName);

    private static void SetString(IList<FPropertyTag> tags, string prefix, string? value, string? createFullName = null)
        => GvasTags.SetString(tags, prefix, value, createFullName);

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
        => GvasTags.SetName(tags, prefix, value, createFullName);
}
