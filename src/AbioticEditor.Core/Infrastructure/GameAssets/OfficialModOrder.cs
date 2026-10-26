namespace AbioticEditor.Core.Assets;

/// <summary>Stable ordering for the official configuration's mod.io LoadAfter IDs.</summary>
internal static class OfficialModOrder
{
    internal sealed record Entry(string Path, long? Id, int Order, IReadOnlyList<long> LoadAfter);

    internal static IReadOnlyList<Entry> Sort(IEnumerable<Entry> entries)
    {
        var pending = entries.OrderBy(entry => entry.Order).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var result = new List<Entry>();
        while (pending.Count > 0)
        {
            var next = pending.FindIndex(entry => !pending.Any(other => other.Id is { } id && entry.LoadAfter.Contains(id)));
            if (next < 0) break; // Conflicting data must not win by an arbitrary directory order.
            result.Add(pending[next]);
            pending.RemoveAt(next);
        }
        return result;
    }
}
