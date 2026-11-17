namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Splits a base's pieces by height. <see cref="BaseDetector"/> groups pieces by distance on the map
/// alone, so a "base" can hold pieces on floors far apart (a bench up top and a few crates on a level
/// far below). Moving such a group whole leaves pieces hanging in the air or buried in rock, so a move
/// takes the main floor only: the pieces stacked with the crafting benches.
/// </summary>
public static class BaseFloors
{
    /// <summary>
    /// The height gap (cm) that separates two floors of pieces: 15 m. A tall building of the player's
    /// own (several storeys a few metres apart) stays one floor; separate levels of the map do not.
    /// </summary>
    public const double FloorGap = 1500;

    /// <summary>The pieces grouped into floors, lowest first (a gap of more than <see cref="FloorGap"/> starts a new floor).</summary>
    public static IReadOnlyList<IReadOnlyList<T>> Split<T>(IEnumerable<T> pieces, Func<T, double> z)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(z);
        var floors = new List<IReadOnlyList<T>>();
        var current = new List<T>();
        double? last = null;
        foreach (var p in pieces.OrderBy(z))
        {
            var h = z(p);
            if (last is { } previous && h - previous > FloorGap)
            {
                floors.Add(current);
                current = [];
            }
            current.Add(p);
            last = h;
        }
        if (current.Count > 0) floors.Add(current);
        return floors;
    }

    /// <summary>
    /// The floor a base lives on: the one with the most crafting benches (the base's anchors), then the
    /// one with the most pieces.
    /// </summary>
    public static IReadOnlyList<T> Main<T>(IEnumerable<T> pieces, Func<T, double> z, Func<T, bool> isAnchor)
    {
        ArgumentNullException.ThrowIfNull(isAnchor);
        var floors = Split(pieces, z);
        return floors.Count == 0
            ? []
            : floors.OrderByDescending(f => f.Count(isAnchor)).ThenByDescending(f => f.Count).First();
    }

    /// <summary>The ids of a detected base's pieces on its main floor.</summary>
    public static IReadOnlyList<string> MainFloorIds(WorldBase worldBase)
    {
        ArgumentNullException.ThrowIfNull(worldBase);
        return Main(worldBase.Deployables, d => d.Z, d => d.IsCraftingBench).Select(d => d.Id).ToList();
    }
}
