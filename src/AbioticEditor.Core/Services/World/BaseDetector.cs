namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// Groups deployables into "bases": crafting benches act as anchors and everything
/// within <see cref="ClusterRadius"/> of an anchor (transitively) joins that base.
/// Unanchored deployables far from any bench are collected into an "Ungrouped" bucket.
/// </summary>
public static class BaseDetector
{
    /// <summary>Cluster radius in unreal units (100 uu = 1 m) - 30 m around a bench.</summary>
    public const double ClusterRadius = 3000;

    public static IReadOnlyList<WorldBase> Detect(IReadOnlyList<WorldDeployable> deployables)
    {
        var anchors = deployables.Where(d => d.IsCraftingBench).ToList();
        if (anchors.Count == 0)
        {
            return deployables.Count == 0
                ? Array.Empty<WorldBase>()
                : new[] { new WorldBase("All deployables (no crafting bench found)", 0, 0, deployables) };
        }

        // Union benches whose radii overlap into one base.
        var anchorGroups = new List<List<WorldDeployable>>();
        foreach (var anchor in anchors)
        {
            var group = anchorGroups.FirstOrDefault(g =>
                g.Any(a => Distance2D(a, anchor) <= ClusterRadius * 2));
            if (group is null)
            {
                group = new List<WorldDeployable>();
                anchorGroups.Add(group);
            }
            group.Add(anchor);
        }

        var bases = new List<WorldBase>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var index = 1;
        foreach (var group in anchorGroups.OrderByDescending(g => g.Count))
        {
            var members = deployables
                .Where(d => !claimed.Contains(d.Id)
                            && group.Any(a => Distance2D(a, d) <= ClusterRadius))
                .ToList();
            foreach (var m in members) claimed.Add(m.Id);

            var cx = group.Average(a => a.X);
            var cy = group.Average(a => a.Y);
            bases.Add(new WorldBase($"Base {index++}", cx, cy, members));
        }

        var leftovers = deployables.Where(d => !claimed.Contains(d.Id)).ToList();
        if (leftovers.Count > 0)
        {
            bases.Add(new WorldBase("Outside any base", 0, 0, leftovers));
        }
        return bases;
    }

    private static double Distance2D(WorldDeployable a, WorldDeployable b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
