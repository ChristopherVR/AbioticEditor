namespace AbioticEditor.Web.Services;

/// <summary>
/// Turns a sequence into stable, collision-proof <c>@key</c> values for Blazor <c>@foreach</c>
/// loops. Blazor's diff builder throws (and kills the whole page circuit - no ErrorBoundary can
/// catch it) when two sibling elements in the same render render with the same <c>@key</c>. That
/// happened for real on the live TRIGGERS tab (round 122): several trigger volumes placed in the
/// level share one game-authored <c>UniqueTriggerID</c>, so keying rows straight off that id
/// produced duplicate keys the moment two volumes with the same id were both loaded.
///
/// <para>Any loop keyed by data this app does not fully control - live-agent rows, save-file
/// entries, catalog/game-registry ids - can in principle repeat, even when today's known data
/// looks unique. Route those loops through <see cref="With{T}"/> instead of using the id
/// directly: the first item with a given id keeps that id as its key; the 2nd, 3rd, ... item
/// sharing the same id gets <c>"{id}#2"</c>, <c>"{id}#3"</c>, and so on, so keys stay stable
/// across re-renders for the common (already-unique) case and simply cannot collide for the rare
/// one.</para>
/// </summary>
public static class RenderKeys
{
    /// <summary>
    /// Pairs each item in <paramref name="items"/> with a unique render key derived from
    /// <paramref name="idSelector"/>. Materializes the result (a <c>@key</c> value must be stable
    /// for the whole render, not recomputed from a lazy iterator), so call this once per render
    /// pass and reuse the list, the same way callers already cache a prepared row list.
    /// </summary>
    public static List<(T Item, string Key)> With<T>(IEnumerable<T> items, Func<T, string?> idSelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(idSelector);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<(T Item, string Key)>();
        foreach (var item in items)
        {
            var id = idSelector(item) ?? string.Empty;
            result.Add((item, UniqueKey(id, seen)));
        }
        return result;
    }

    /// <summary>
    /// Same as <see cref="With{T}"/> but for a loop that has already built its own row/view-model
    /// list and just needs a key string per row, keyed by <paramref name="idSelector"/> and looked
    /// up during render (e.g. from a dictionary built alongside the row list).
    /// </summary>
    public static Dictionary<T, string> KeyLookup<T>(IEnumerable<T> items, Func<T, string?> idSelector) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(idSelector);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new Dictionary<T, string>();
        foreach (var item in items)
        {
            var id = idSelector(item) ?? string.Empty;
            result[item] = UniqueKey(id, seen);
        }
        return result;
    }

    private static string UniqueKey(string id, Dictionary<string, int> seen)
    {
        if (!seen.TryGetValue(id, out var count))
        {
            seen[id] = 1;
            return id;
        }
        count++;
        seen[id] = count;
        return $"{id}#{count}";
    }
}
