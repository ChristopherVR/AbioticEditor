using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>One piece of a base being put down: its key and where it would stand (save space, cm).</summary>
public sealed record BaseFloorPiece(string Key, PlacedVector At);

/// <summary>
/// What <see cref="BaseFloorFit.FitAsync"/> found: the height each piece should stand at (by key), the
/// floor the base stands on, and the pieces with no floor of their own near that floor (over a drop,
/// a pit or the edge of a walkway).
/// </summary>
public sealed record BaseFloorFitResult(IReadOnlyDictionary<string, double> Heights, double? Floor, IReadOnlyList<string> OverDrop, int Stood)
{
    /// <summary>True when the level could not be read there (nothing loaded under the base).</summary>
    public bool NoLevel => Floor is null;
}

/// <summary>
/// Stands a base on the floor at its new place without losing its shape or its pieces. Found in the
/// game: a base put down beside a sector terminal (the Reactor) stood partly over a drop, and pieces
/// were stood on the bottom of the drop where nobody can reach them. So:
/// <list type="bullet">
/// <item>The base's floor is the floor most of its pieces find under them (not the floor under its
/// middle, which may be a hole).</item>
/// <item>Each piece keeps its height above the base's floor (a wall lamp stays on the wall, a lamp on
/// a table stays on the table).</item>
/// <item>A piece whose own floor is not within <see cref="Reach"/> of the base's floor is not lowered
/// or raised to it: it stays level with the rest and is reported in
/// <see cref="BaseFloorFitResult.OverDrop"/>, so the player can move the base.</item>
/// </list>
/// </summary>
public static class BaseFloorFit
{
    /// <summary>How far a piece's own floor may sit above or below the base's floor and still count as the same floor (cm).</summary>
    public const double Reach = 200;

    /// <summary>How far above the pieces the floor is looked for (cm), so a base sunk a little into the floor still finds it.</summary>
    public const double LookFrom = 250;

    /// <summary>Floors closer than this are the same floor (cm).</summary>
    private const double SameFloor = 50;

    /// <param name="pieces">The pieces where they would stand now (moved and turned, before any floor fit).</param>
    /// <param name="floorUnder">The floor (save Z) under a point, looking down from that point; null where none is known.</param>
    public static async Task<BaseFloorFitResult> FitAsync(IReadOnlyList<BaseFloorPiece> pieces, Func<PlacedVector, Task<double?>> floorUnder)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(floorUnder);
        if (pieces.Count == 0) return new BaseFloorFitResult(new Dictionary<string, double>(), null, [], 0);

        // The base's own floor height before the fit: low, but not the lowest piece (a pipe under the floor).
        var zs = pieces.Select(p => p.At.Z).Order().ToList();
        var baseZ = zs[(zs.Count - 1) / 4];

        var floors = new Dictionary<string, double?>(StringComparer.Ordinal);
        foreach (var p in pieces)
        {
            floors[p.Key] = await floorUnder(p.At with { Z = baseZ + LookFrom }).ConfigureAwait(false);
        }

        var found = floors.Values.Where(f => f is not null).Select(f => f!.Value).ToList();
        if (found.Count == 0) return new BaseFloorFitResult(new Dictionary<string, double>(), null, [], 0);
        var floor = MostCommon(found);

        var heights = new Dictionary<string, double>(StringComparer.Ordinal);
        var overDrop = new List<string>();
        var stood = 0;
        foreach (var p in pieces)
        {
            var above = p.At.Z - baseZ;
            if (floors[p.Key] is { } own && Math.Abs(own - floor) <= Reach)
            {
                heights[p.Key] = Math.Round(own + above);
                stood++;
            }
            else
            {
                heights[p.Key] = Math.Round(floor + above);
                overDrop.Add(p.Key);
            }
        }
        return new BaseFloorFitResult(heights, floor, overDrop, stood);
    }

    /// <summary>The floor height most pieces share (the highest of equally common ones: a walkway over a pit, not the pit).</summary>
    private static double MostCommon(List<double> floors)
    {
        var best = floors[0];
        var bestCount = 0;
        foreach (var f in floors)
        {
            var group = floors.Where(o => Math.Abs(o - f) <= SameFloor).ToList();
            var at = group.Average();
            if (group.Count > bestCount || (group.Count == bestCount && at > best))
            {
                bestCount = group.Count;
                best = at;
            }
        }
        return best;
    }
}
