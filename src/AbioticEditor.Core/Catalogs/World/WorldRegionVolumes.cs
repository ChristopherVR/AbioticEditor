using System.Numerics;
using System.Text.Json;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>Standing sections for persistent Facility actors, from the cooked streaming brushes.
/// Volumes can overlap at section boundaries; an actor belongs to every containing section.</summary>
public static class WorldRegionVolumes
{
    private static readonly Lazy<Volume[]> Data = new(() =>
    {
        using var stream = typeof(WorldRegionVolumes).Assembly.GetManifestResourceStream("WorldRegionVolumes.json")!;
        return JsonSerializer.Deserialize<Volume[]>(stream)!;
    });

    public static bool HasRegion(string region) => Data.Value.Any(v => v.Region.Equals(region, StringComparison.OrdinalIgnoreCase));
    public static bool Contains(string region, PlacedVector point)
        => Data.Value.Any(v => v.Region.Equals(region, StringComparison.OrdinalIgnoreCase) && v.Contains(point));

    public static string? RegionAt(PlacedVector point)
        => Data.Value.Where(v => v.Contains(point)).OrderBy(v => v.Size).Select(v => v.Region).FirstOrDefault();

    private sealed record Volume(string Region, string Actor, double[] Origin, double[] Rotation,
        double[] Scale, double[] BoundsOrigin, double[] Extent, double[][] Nodes)
    {
        public double Size => Extent[0] * Extent[1] * Extent[2] * Math.Abs(Scale[0] * Scale[1] * Scale[2]);
        public bool Contains(PlacedVector point)
        {
            var delta = new Vector3((float)(point.X - Origin[0]), (float)(point.Y - Origin[1]), (float)(point.Z - Origin[2]));
            var q = new Quaternion((float)Rotation[0], (float)Rotation[1], (float)Rotation[2], (float)Rotation[3]);
            var p = Vector3.Transform(delta, Quaternion.Inverse(q));
            var x = p.X / Scale[0]; var y = p.Y / Scale[1]; var z = p.Z / Scale[2];
            const double tolerance = 0.01;
            if (Math.Abs(x - BoundsOrigin[0]) > Extent[0] + tolerance
                || Math.Abs(y - BoundsOrigin[1]) > Extent[1] + tolerance
                || Math.Abs(z - BoundsOrigin[2]) > Extent[2] + tolerance) return false;
            // Cooked brush BSP planes face outward. A terminal back leaf is inside, a front
            // leaf is outside. Traversing the tree also handles the non-box, concave brushes.
            var index = 0;
            for (var remaining = Nodes.Length; remaining > 0 && index >= 0 && index < Nodes.Length; remaining--)
            {
                var node = Nodes[index];
                var back = x * node[0] + y * node[1] + z * node[2] - node[3] <= tolerance;
                index = (int)node[back ? 5 : 4];
                if (index < 0) return back;
            }
            return false;
        }
    }
}
