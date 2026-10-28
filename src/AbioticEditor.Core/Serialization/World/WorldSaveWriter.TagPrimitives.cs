using AbioticEditor.Core.PlayerSaves;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.WorldSaves;

// WorldSaveWriter - low-level GVAS tag helpers (find-or-create by prefix, typed setters).
public static partial class WorldSaveWriter
{
    private static void ReplaceNameArray(
        IList<FPropertyTag> tags, string prefix, IReadOnlyList<string> values, string? createFullName = null)
        => GvasTags.ReplaceNameArray(tags, prefix, values, createFullName);

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

    /// <summary>
    /// Sets a TextProperty carrying player-entered text (see <see cref="SetPlayerText"/>).
    /// </summary>
    private static void SetTextNone(IList<FPropertyTag> tags, string prefix, string value)
    {
        var property = tags.FindByPrefix(prefix)?.Property;
        // Native 1.5 saves omit the name of a creature that was never named, so the first name
        // needs the field created.
        if (property is null && value.Length > 0 && NativeSaveSchema.IsNative(tags))
        {
            var name = new FString(NativeSaveSchema.Name(tags, prefix));
            property = new TextProperty(name)
            {
                Value = new FText { HistoryType = UeSaveGame.TextData.TextHistoryType.None, Value = new UeSaveGame.TextData.TextData_None() },
            };
            tags.Add(new FPropertyTag(name, new FPropertyTypeName(new FString(nameof(TextProperty))), EPropertyTagFlags.None) { Property = property });
        }
        if (property is TextProperty { Value: { } text }) SetPlayerText(text, value);
    }

    /// <summary>
    /// Writes player-entered text into an existing FText. Older saves store it with no history
    /// (None); native 1.5 saves store a player-typed name as a Base text whose namespace and key
    /// match no translation, so the game shows its source string. Other history types (real
    /// localized texts) are left alone and return false.
    /// </summary>
    private static bool SetPlayerText(FText text, string value)
    {
        switch (text.HistoryType)
        {
            case UeSaveGame.TextData.TextHistoryType.None:
                if (text.Value is not UeSaveGame.TextData.TextData_None none)
                {
                    none = new UeSaveGame.TextData.TextData_None();
                    text.Value = none;
                }
                none.Value = value.Length == 0 ? null : new FString(value);
                return true;
            case UeSaveGame.TextData.TextHistoryType.Base when text.Value is UeSaveGame.TextData.TextData_Base baseText:
                if (string.Equals(baseText.SourceString?.Value ?? string.Empty, value, StringComparison.Ordinal)) return true;
                baseText.SourceString = new FString(value);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Finds the property matching <paramref name="prefix"/>; when absent and
    /// <paramref name="createFullName"/> is given, creates and appends a fresh tag of
    /// <paramref name="typeName"/> (AF delta-serializes, so default-valued members are
    /// missing from healthy saves). Shared with <c>PlayerSaveWriter</c> via <see cref="GvasTags"/>.
    /// </summary>
    private static FProperty? FindOrCreate(IList<FPropertyTag> tags, string prefix, string? createFullName, string typeName)
        => GvasTags.FindOrCreate(tags, prefix, createFullName, typeName);

    private static void SetDouble(IList<FPropertyTag> tags, string prefix, double value, string? createFullName = null)
        => GvasTags.SetDouble(tags, prefix, value, createFullName);

    private static void SetInt(IList<FPropertyTag> tags, string prefix, int value, string? createFullName = null)
        => GvasTags.SetInt(tags, prefix, value, createFullName);

    private static void SetBool(IList<FPropertyTag> tags, string prefix, bool value, string? createFullName = null)
        => GvasTags.SetBool(tags, prefix, value, createFullName);

    private static void SetString(IList<FPropertyTag> tags, string prefix, string? value, string? createFullName = null)
        => GvasTags.SetString(tags, prefix, value, createFullName);

    private static void SetName(IList<FPropertyTag> tags, string prefix, string value)
        => GvasTags.SetName(tags, prefix, value);

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
