using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Objects;

namespace AbioticEditor.Core.Items;

/// <summary>
/// The game's distillation table (<c>DT_ItemDistillations</c>): which items can go through a
/// distillery. A character's distillery history (<c>ItemsDistilled_</c>) only ever holds these, so
/// the editor offers only these instead of every item in the game.
/// </summary>
public static class DistillationCatalog
{
    private const string Table = "AbioticFactor/Content/Blueprints/DataTables/DT_ItemDistillations";

    /// <summary>
    /// Every distillable item: the row name is the item that goes in (lower-case item id), with the
    /// distillate it becomes and how many. Empty when the table cannot be read.
    /// </summary>
    public static IReadOnlyList<DistillationRecipe> LoadFrom(GameAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var table = provider.TryLoadDataTable(Table);
        if (table is null) return [];
        var result = new List<DistillationRecipe>();
        foreach (var (row, value) in table.RowMap)
        {
            string? output = null;
            var count = 1;
            foreach (var p in value.Properties)
            {
                var name = p.Name.Text;
                if (name.StartsWith("Item_", StringComparison.Ordinal) && RowNameOf(p.Tag?.GenericValue) is { } o) output = o;
                else if (name.StartsWith("Count_", StringComparison.Ordinal) && p.Tag?.GenericValue is int c) count = c;
            }
            result.Add(new DistillationRecipe(row.Text.ToLowerInvariant(), output, count));
        }
        return result.OrderBy(r => r.Input, StringComparer.Ordinal).ToList();
    }

    private static string? RowNameOf(object? value)
    {
        if (value is FScriptStruct ss) value = ss.StructType;
        if (value is not FStructFallback sf) return null;
        var v = sf.Properties.FirstOrDefault(p => p.Name.Text == "RowName")?.Tag?.GenericValue?.ToString();
        return string.IsNullOrEmpty(v) || v == "None" ? null : v;
    }

    /// <summary>Research view of the table: each row name and its fields, as text.</summary>
    public static IReadOnlyList<(string Row, string Fields)> Describe(GameAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var table = provider.TryLoadDataTable(Table);
        if (table is null) return [];
        return table.RowMap
            .Select(kv => (kv.Key.Text, string.Join("; ", kv.Value.Properties.Select(p => $"{p.Name.Text}={Text(p.Tag?.GenericValue)}"))))
            .ToList();
    }

    private static string Text(object? value) => value switch
    {
        null => "null",
        FStructFallback s => "{" + string.Join(", ", s.Properties.Select(p => $"{p.Name.Text}={Text(p.Tag?.GenericValue)}")) + "}",
        FScriptStruct ss => Text(ss.StructType),
        UScriptArray a => "[" + string.Join(", ", a.Properties.Select(p => Text(p.GenericValue))) + "]",
        _ => value.ToString() ?? "",
    };
}

/// <summary>One distillable item: what goes in, the distillate it becomes, and how many.</summary>
public sealed record DistillationRecipe(string Input, string? Output, int Count);
