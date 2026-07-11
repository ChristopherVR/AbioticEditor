using UeSaveGame;

namespace AbioticEditor.Core.Saves;

/// <summary>
/// Prefix-match accessors over UeSaveGame property-tag lists, shared by every reader
/// and writer. Property names in Abiotic Factor saves carry blueprint hash suffixes
/// (e.g. <c>CurrentItemDurability_4_24B4D0E6...</c>), so lookups always prefix-match
/// on the stable part of the name.
/// </summary>
public static class PropertyTagExtensions
{
    /// <summary>First tag whose name starts with <paramref name="prefix"/>, or null.</summary>
    public static FPropertyTag? FindByPrefix(this IEnumerable<FPropertyTag>? tags, string prefix)
    {
        if (tags is null) return null;
        foreach (var t in tags)
        {
            if (t.Name?.Value is { } n && n.StartsWith(prefix, StringComparison.Ordinal))
            {
                return t;
            }
        }
        return null;
    }

    public static double GetDouble(this IEnumerable<FPropertyTag>? tags, string prefix, double defaultValue = 0)
        => tags.FindByPrefix(prefix)?.Property?.Value is double d ? d : defaultValue;

    public static float GetFloat(this IEnumerable<FPropertyTag>? tags, string prefix, float defaultValue = 0)
        => tags.FindByPrefix(prefix)?.Property?.Value switch
        {
            float f => f,
            double d => (float)d,
            _ => defaultValue,
        };

    public static long GetLong(this IEnumerable<FPropertyTag>? tags, string prefix, long defaultValue = 0)
        => tags.FindByPrefix(prefix)?.Property?.Value switch
        {
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            uint ui => ui,
            ulong ul => (long)ul,
            _ => defaultValue,
        };

    public static bool GetBool(this IEnumerable<FPropertyTag>? tags, string prefix)
        => tags.FindByPrefix(prefix)?.Property?.Value is bool b && b;

    /// <summary>Bool when present; null when the tag is absent (delta-serialized saves).</summary>
    public static bool? TryGetBool(this IEnumerable<FPropertyTag>? tags, string prefix)
        => tags.FindByPrefix(prefix)?.Property?.Value is bool b ? b : null;

    public static string? GetString(this IEnumerable<FPropertyTag>? tags, string prefix)
        => tags.FindByPrefix(prefix)?.Property?.Value?.ToString();

    /// <summary>
    /// Enum value rendered as its save string (e.g. <c>E_LiquidType::NewEnumerator1</c>).
    /// Same mechanics as <see cref="GetString"/>; named separately for intent.
    /// </summary>
    public static string? GetEnumString(this IEnumerable<FPropertyTag>? tags, string prefix)
        => tags.GetString(prefix);
}
