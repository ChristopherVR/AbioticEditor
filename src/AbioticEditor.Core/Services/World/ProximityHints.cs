using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>One object position fed to the proximity search.</summary>
public readonly record struct PlacedPoint(string Key, string? ClassName, PlacedVector Position);

/// <summary>Two objects whose saved positions are close together.</summary>
/// <param name="KeyA">First object key.</param>
/// <param name="KeyB">Second object key.</param>
/// <param name="ClassA">Class of the first object.</param>
/// <param name="ClassB">Class of the second object.</param>
/// <param name="DistanceCm">Centre-to-centre distance of the saved positions.</param>
/// <param name="HeightDifferenceCm">Absolute Z difference; a large value means a different floor rather than an overlap.</param>
public sealed record ProximityHint(
    string KeyA, string KeyB, string? ClassA, string? ClassB, double DistanceCm, double HeightDifferenceCm);

/// <summary>
/// Reports which objects sit within a distance of each other. These are HINTS ONLY: a centre-to-centre
/// distance between saved positions. They are not a collision test, do not know object sizes or
/// meshes, and say nothing about whether a placement is valid in the game (the roadmap forbids
/// inferring collision or placement validity from meshes).
/// </summary>
public static class ProximityHints
{
    /// <summary>Text a UI or CLI should show next to any hint list.</summary>
    public const string Disclaimer =
        "Hints only: centre-to-centre distance between saved positions. This is not a collision test and does not know object sizes.";

    /// <summary>
    /// Finds pairs within <paramref name="maxDistanceCm"/>. When <paramref name="focus"/> is given only
    /// pairs where at least one object is in it are returned (for example the objects being moved).
    /// Uses a coarse grid so a large region stays fast. Nearest pairs first.
    /// </summary>
    public static IReadOnlyList<ProximityHint> Find(
        IReadOnlyList<PlacedPoint> points, double maxDistanceCm, IReadOnlyCollection<string>? focus = null)
    {
        var result = new List<ProximityHint>();
        if (!(maxDistanceCm > 0) || points.Count < 2) return result;

        var cell = maxDistanceCm;
        var focusSet = focus is null ? null : new HashSet<string>(focus, StringComparer.Ordinal);
        var grid = new Dictionary<(long, long, long), List<int>>();
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i].Position;
            var c = ((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell), (long)Math.Floor(p.Z / cell));
            if (!grid.TryGetValue(c, out var list))
            {
                list = [];
                grid[c] = list;
            }
            list.Add(i);
        }

        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var (cx, cy, cz) = ((long)Math.Floor(a.Position.X / cell), (long)Math.Floor(a.Position.Y / cell), (long)Math.Floor(a.Position.Z / cell));
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var bucket)) continue;
                        foreach (var j in bucket)
                        {
                            if (j <= i) continue;
                            var b = points[j];
                            if (focusSet is not null && !focusSet.Contains(a.Key) && !focusSet.Contains(b.Key)) continue;
                            var d = PlacementMath.Distance(a.Position, b.Position);
                            if (d > maxDistanceCm) continue;
                            result.Add(new ProximityHint(
                                a.Key, b.Key, a.ClassName, b.ClassName, d, Math.Abs(a.Position.Z - b.Position.Z)));
                        }
                    }
                }
            }
        }
        return result.OrderBy(h => h.DistanceCm).ThenBy(h => h.KeyA, StringComparer.Ordinal).ThenBy(h => h.KeyB, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Proximity hints for every object of a save that has a saved position. Level-placed objects are
    /// left out unless <paramref name="includeLevelPlaced"/> is set. <paramref name="positionOverride"/>
    /// substitutes a staged position for an object (null keeps the saved one).
    /// </summary>
    public static IReadOnlyList<ProximityHint> FindInSave(
        WorldSaveData data, double maxDistanceCm, IReadOnlyCollection<string>? focus = null,
        bool includeLevelPlaced = false, Func<string, PlacedVector?>? positionOverride = null)
        => Find(CollectPoints(data, includeLevelPlaced, positionOverride), maxDistanceCm, focus);

    /// <summary>Collects positioned objects of a save (see <see cref="FindInSave"/>).</summary>
    public static List<PlacedPoint> CollectPoints(
        WorldSaveData data, bool includeLevelPlaced, Func<string, PlacedVector?>? positionOverride)
    {
        var points = new List<PlacedPoint>();
        foreach (var e in WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap"))
        {
            if (!includeLevelPlaced && e.Props.TryGetBool("DeployedByPlayer_") != true) continue;
            var position = positionOverride?.Invoke(e.Key)
                ?? PlacedObjectCensus.ReadTransform(e.Props)?.Translation;
            if (position is null) continue;
            points.Add(new PlacedPoint(e.Key, PlacedObjectCensus.ClassNameOf(PlacedObjectCensus.ClassPathOf(e.Props)), position.Value));
        }
        return points;
    }
}
