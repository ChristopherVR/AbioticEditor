namespace AbioticEditor.Web.Models;

/// <summary>
/// Defensive dedupe for a live-agent feature directory's rows, applied where each
/// <c>Live*FeatureSession</c> stores the rows its channel handed back. A repeated render key
/// crashes the whole page circuit (RenderTreeDiffBuilder's "More than one sibling ... has the
/// same key value" - uncatchable by any ErrorBoundary; see round 122's TRIGGERS crash). Every
/// live area but triggers already keys its rows by the actor's own <c>GetFullName()</c>, which
/// the engine guarantees is unique per loaded actor - see each area's own Lua module header - so
/// this should never actually trim anything for them today. It exists as a second, independent
/// safety net alongside <c>RenderKeys</c> on the UI side: if a future area, an engine edge case,
/// or a modded/DLC actor ever hands back two rows sharing an id, the first one wins here rather
/// than ever reaching the render tree at all. <see cref="LiveTriggersFeatureSession"/> is the one
/// area with a real, documented same-id case (several placed volumes can share one
/// <c>UniqueTriggerID</c>); that one is fixed at the source by merging in
/// <c>areas/triggers.lua</c>'s own <c>triggerRows()</c>, and applies this as the same belt-and-
/// braces backstop the other areas get.
/// </summary>
internal static class LiveFeatureRows
{
    public static IReadOnlyList<T> DistinctById<T>(IReadOnlyList<T> rows, Func<T, string> idSelector)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(idSelector);
        if (rows.Count < 2) return rows;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<T>? deduped = null;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (seen.Add(idSelector(row)))
            {
                deduped?.Add(row);
            }
            else
            {
                // First duplicate found: switch to building a filtered copy, carrying over
                // everything already accepted.
                deduped ??= [.. rows.Take(i)];
            }
        }
        return deduped ?? rows;
    }
}
