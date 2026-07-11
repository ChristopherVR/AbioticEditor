namespace AbioticEditor.Core.Items;

/// <summary>
/// Process-wide map of item id -> the DataTable object reference the item's row lives in (e.g.
/// <c>/Game/Blueprints/Items/ItemTable_Global.ItemTable_Global</c>). Populated by
/// <see cref="ItemCatalog.LoadFrom"/> as it merges the game's item tables, and read by the save
/// writers so an item dropped into a slot points its row handle at the table that actually holds
/// it - otherwise the game can't resolve the row and renders the item blank.
///
/// Empty until the catalog loads (the CLI without a game install, or tests), in which case
/// <see cref="TableRefFor"/> returns null and callers fall back to the primary table.
/// </summary>
public static class ItemTableIndex
{
    private static IReadOnlyDictionary<string, string> _byId =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Replaces the index (called once when the catalog finishes loading).</summary>
    public static void Set(IReadOnlyDictionary<string, string> idToTableRef)
        => _byId = idToTableRef ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The DataTable object ref holding <paramref name="id"/>'s row, or null if unknown.</summary>
    public static string? TableRefFor(string? id)
        => id is not null && _byId.TryGetValue(id, out var tableRef) ? tableRef : null;

    /// <summary>Number of indexed items (0 until the catalog loads).</summary>
    public static int Count => _byId.Count;
}
