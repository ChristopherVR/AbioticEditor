using System.Globalization;
using UeSaveGame;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Core.Saves;

/// <summary>
/// Conservative editor for existing, top-level primitive GVAS properties.  It is
/// deliberately not a JSON importer: maps, arrays, structs and object references must
/// be changed through their typed writers so their invariants remain intact.
/// </summary>
public static class RawSavePropertyEditor
{
    public static IReadOnlyList<RawSaveProperty> List(SaveGame save) => (save.Properties ?? [])
        .Select(tag => new RawSaveProperty(tag.Name.Value, tag.Type.Name.Value,
            tag.Property?.Value?.ToString() ?? string.Empty, IsSupported(tag.Property)))
        .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Validates and applies an edit to an existing exact-name primitive tag.</summary>
    public static bool TryApply(SaveGame save, string name, string? value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(name)) { error = "A property name is required."; return false; }
        var tag = (save.Properties ?? []).FirstOrDefault(t => string.Equals(t.Name.Value, name, StringComparison.Ordinal));
        if (tag?.Property is not { } property) { error = "That property no longer exists in this save."; return false; }
        var text = value ?? string.Empty;
        try
        {
            switch (property)
            {
                case StrProperty:
                    property.Value = new FString(text); break;
                case IntProperty:
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) throw new FormatException("Enter a 32-bit integer.");
                    property.Value = integer; break;
                case Int64Property:
                    if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longInteger)) throw new FormatException("Enter a 64-bit integer.");
                    property.Value = longInteger; break;
                case FloatProperty:
                    if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var single)) throw new FormatException("Enter a number.");
                    property.Value = single; break;
                case DoubleProperty:
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) throw new FormatException("Enter a finite number.");
                    property.Value = number; break;
                case BoolProperty:
                    if (!bool.TryParse(text, out var boolean)) throw new FormatException("Enter true or false.");
                    property.Value = boolean; break;
                default:
                    error = $"{tag.Type.Name.Value} is read-only. Use its dedicated editor instead."; return false;
            }
            return true;
        }
        catch (FormatException ex) { error = ex.Message; return false; }
    }

    private static bool IsSupported(FProperty? property) => property is StrProperty or IntProperty
        or Int64Property or FloatProperty or DoubleProperty or BoolProperty;
}

public sealed record RawSaveProperty(string Name, string Type, string Value, bool IsEditable);
