using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>What kind of place a <see cref="BaseDestination"/> is.</summary>
public enum BaseDestinationKind
{
    /// <summary>The spot the pieces stood on in their old world (another world of the same map).</summary>
    SameSpot,

    /// <summary>Beside a sector's respawn terminal (a named part of the Facility: the Office Sector, Manufacturing West, the Dam...).</summary>
    Sector,

    /// <summary>Beside one of the bases already standing in the receiving world.</summary>
    BesideBase,

    /// <summary>The middle of the area's own furniture and fittings (for an area with no named sectors, a portal world say).</summary>
    AreaMiddle,
}

/// <summary>
/// A place a moved base can be put down: where the middle of the pieces goes (X, Y) and the floor they
/// stand on (Z). <see cref="OffsetFor"/> turns it into the shift a staged copy uses.
/// </summary>
/// <param name="NearSector">For a base beside which the pieces go: the Facility sector it stands in, when known.</param>
public sealed record BaseDestination(BaseDestinationKind Kind, string Name, PlacedVector Point, string? NearSector = null)
{
    /// <summary>The shift that puts the package's middle on <see cref="Point"/>, its lowest piece on the floor there.</summary>
    public PlacedVector OffsetFor(BasePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (Kind == BaseDestinationKind.SameSpot || package.Centre is not { } centre || package.FloorZ is not { } floor)
        {
            return new PlacedVector(0, 0, 0);
        }
        return new PlacedVector(Math.Round(Point.X - centre.X), Math.Round(Point.Y - centre.Y), Math.Round(Point.Z - floor));
    }
}

/// <summary>
/// The places a moved base can go in a receiving world: the same spot (when it is the same map), the
/// Facility's named sectors (next to each sector's respawn terminal, a spot the game itself stands
/// players on), and beside each base already built there. Reads only.
/// </summary>
public static class BaseDestinations
{
    /// <summary>How far from a terminal's sitting spot the middle of a base is put (cm), so it does not cover the terminal.</summary>
    private const double SectorClearance = 1200;

    /// <summary>The gap left between an existing base and one put beside it (cm).</summary>
    private const double BaseGap = 800;

    public static IReadOnlyList<BaseDestination> For(BasePackage package, WorldSaveData target)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(target);
        var list = new List<BaseDestination>();
        var otherMap = package.IsOtherMap(target);
        if (!otherMap && package.Centre is { } centre && package.FloorZ is { } floor)
        {
            list.Add(new BaseDestination(BaseDestinationKind.SameSpot, "", new PlacedVector(centre.X, centre.Y, floor)));
        }

        // The respawn terminals stand in the Facility level, so they only mean something there.
        if (IsFacility(target))
        {
            foreach (var t in RespawnTerminalCatalog.All)
            {
                var yaw = t.Yaw * Math.PI / 180;
                var spot = t.SpawnPosition;
                list.Add(new BaseDestination(BaseDestinationKind.Sector, t.LocationName, new PlacedVector(
                    spot.X + (Math.Cos(yaw) * SectorClearance), spot.Y + (Math.Sin(yaw) * SectorClearance), t.Z)));
            }
        }

        // An area of another map with no named sectors: the middle of what the level itself put there,
        // standing on the floor most of it stands on (the median height), so there is always a place to pick.
        if (!IsFacility(target) && AreaMiddle(target) is { } middle)
        {
            list.Add(new BaseDestination(BaseDestinationKind.AreaMiddle, "", middle));
        }

        // Beside each base already there (a group around a crafting bench; stray pieces are not a base).
        var halfWidth = HalfWidth(package);
        var facility = IsFacility(target);
        foreach (var b in BaseDetector.Detect(target.Deployables))
        {
            if (b.Deployables.Count == 0 || b.BenchCount == 0) continue;
            var reach = b.Deployables.Max(d => Math.Max(Math.Abs(d.X - b.CenterX), Math.Abs(d.Y - b.CenterY)));
            var z = b.Deployables.Min(d => d.Z);
            list.Add(new BaseDestination(BaseDestinationKind.BesideBase, b.Name,
                new PlacedVector(b.CenterX + reach + BaseGap + halfWidth, b.CenterY, z),
                facility ? RespawnTerminalCatalog.NearestTo(b.CenterX, b.CenterY, z).LocationName : null));
        }
        return list;
    }

    /// <summary>Half the package's width along X (cm), so a base put beside another clears it.</summary>
    private static double HalfWidth(BasePackage package)
    {
        var xs = package.Pieces.Where(p => p.Position is not null).Select(p => p.Position!.Value.X).ToList();
        return xs.Count == 0 ? 0 : (xs.Max() - xs.Min()) / 2;
    }

    private static PlacedVector? AreaMiddle(WorldSaveData target)
    {
        var points = WorldMapAccessor.Entries(target.Raw, "DeployedObjectMap")
            .Select(e => PlacedObjectCensus.ReadTransform(e.Props)?.Translation)
            .Where(p => p is not null)
            .Select(p => p!.Value)
            .ToList();
        if (points.Count == 0) return null;
        static double Median(IEnumerable<double> values)
        {
            var sorted = values.Order().ToList();
            return sorted[sorted.Count / 2];
        }
        return new PlacedVector(Median(points.Select(p => p.X)), Median(points.Select(p => p.Y)), Median(points.Select(p => p.Z)));
    }

    private static bool IsFacility(WorldSaveData target)
        => WorldMapAccessor.Entries(target.Raw, "DeployedObjectMap")
            .Select(e => WorldMapAccessor.GetSoftObjectPath(e.Props, "ActorPath_"))
            .Any(p => p is { } path
                && string.Equals(DoorIdParser.Parse(PlacedObjectCensus.FullSoftPath(path.Package, path.Asset, path.SubPath) ?? string.Empty).Map,
                    WorldLocationIndex.PersistentLevel, StringComparison.OrdinalIgnoreCase));
}
