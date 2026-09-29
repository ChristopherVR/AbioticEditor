namespace AbioticEditor.Core.WorldSaves;

/// <summary>An axis of the world frame (Unreal: X forward, Y right, Z up).</summary>
public enum PlacementAxis
{
    X,
    Y,
    Z,
}

/// <summary>
/// Pure math for placing objects: grid snapping (position and yaw), aligning yaw to a reference, and
/// distributing along an axis. No save access and no game rules: every result is only a number the
/// caller may stage. Distances and grid steps are in the save's own units (centimetres).
/// </summary>
/// <remarks>
/// Yaw is rotation about the world vertical (Z) axis, composed onto the existing quaternion in world
/// space so pitch and roll are preserved. Whether the in-game yaw sign matches is unverified (see
/// docs/reference/research/base-building-coordinate-spaces.md).
/// </remarks>
public static class PlacementMath
{
    private const double Epsilon = 1e-9;

    /// <summary>Snaps one coordinate to the nearest multiple of <paramref name="step"/> (measured from <paramref name="origin"/>).</summary>
    public static double SnapValue(double value, double step, double origin = 0)
    {
        if (!(step > 0) || double.IsNaN(value) || double.IsInfinity(value)) return value;
        return (Math.Round((value - origin) / step, MidpointRounding.AwayFromZero) * step) + origin;
    }

    /// <summary>
    /// Snaps a position to a grid: <paramref name="stepXY"/> for X and Y, and <paramref name="stepZ"/>
    /// for Z (null leaves Z alone).
    /// </summary>
    public static PlacedVector SnapPosition(
        PlacedVector position, double stepXY, double? stepZ = null, PlacedVector? origin = null)
    {
        var o = origin ?? new PlacedVector(0, 0, 0);
        return new PlacedVector(
            SnapValue(position.X, stepXY, o.X),
            SnapValue(position.Y, stepXY, o.Y),
            stepZ is { } sz ? SnapValue(position.Z, sz, o.Z) : position.Z);
    }

    /// <summary>Composes a world-space yaw (degrees) onto a quaternion; pitch and roll are kept.</summary>
    public static PlacedQuaternion ComposeYaw(PlacedQuaternion q, double degrees)
    {
        if (Math.Abs(degrees) < Epsilon) return q;
        var half = degrees * Math.PI / 360.0;
        var (sz, cz) = (Math.Sin(half), Math.Cos(half));
        // Hamilton product qz * q: apply q, then a world-space yaw.
        return new PlacedQuaternion(
            (cz * q.X) - (sz * q.Y),
            (cz * q.Y) + (sz * q.X),
            (cz * q.Z) + (sz * q.W),
            (cz * q.W) - (sz * q.Z));
    }

    /// <summary>Snaps the yaw of a quaternion to a multiple of <paramref name="stepDegrees"/>.</summary>
    public static PlacedQuaternion SnapYaw(PlacedQuaternion q, double stepDegrees)
    {
        if (!(stepDegrees > 0)) return q;
        var yaw = q.YawDegrees;
        var target = Math.Round(yaw / stepDegrees, MidpointRounding.AwayFromZero) * stepDegrees;
        return ComposeYaw(q, Normalize180(target - yaw));
    }

    /// <summary>Rotates <paramref name="q"/> so its yaw equals the yaw of <paramref name="reference"/>.</summary>
    public static PlacedQuaternion AlignYaw(PlacedQuaternion q, PlacedQuaternion reference)
        => ComposeYaw(q, Normalize180(reference.YawDegrees - q.YawDegrees));

    /// <summary>Rotates a point about a vertical axis through <paramref name="pivot"/>; Z is unchanged.</summary>
    public static PlacedVector RotateAboutZ(PlacedVector point, PlacedVector pivot, double degrees)
    {
        var r = degrees * Math.PI / 180.0;
        var (s, c) = (Math.Sin(r), Math.Cos(r));
        var dx = point.X - pivot.X;
        var dy = point.Y - pivot.Y;
        return new PlacedVector(pivot.X + (dx * c) - (dy * s), pivot.Y + (dx * s) + (dy * c), point.Z);
    }

    /// <summary>Straight-line distance in centimetres.</summary>
    public static double Distance(PlacedVector a, PlacedVector b)
        => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    /// <summary>Reads one component of a vector.</summary>
    public static double Component(PlacedVector v, PlacementAxis axis)
        => axis switch { PlacementAxis.X => v.X, PlacementAxis.Y => v.Y, _ => v.Z };

    /// <summary>Returns <paramref name="v"/> with one component replaced.</summary>
    public static PlacedVector WithComponent(PlacedVector v, PlacementAxis axis, double value)
        => axis switch
        {
            PlacementAxis.X => v with { X = value },
            PlacementAxis.Y => v with { Y = value },
            _ => v with { Z = value },
        };

    /// <summary>
    /// Spreads the objects so their centres are evenly spaced along <paramref name="axis"/>. The two
    /// outermost objects stay where they are; the others get equal gaps between neighbours (ordered by
    /// their current coordinate on that axis). The other two coordinates are unchanged. Centre spacing
    /// only: object sizes are not known, so equal centre gaps are not equal free gaps.
    /// </summary>
    public static IReadOnlyDictionary<string, PlacedVector> DistributeAlong(
        IReadOnlyList<(string Key, PlacedVector Position)> items, PlacementAxis axis)
    {
        var result = new Dictionary<string, PlacedVector>(StringComparer.Ordinal);
        var ordered = items.OrderBy(i => Component(i.Position, axis)).ThenBy(i => i.Key, StringComparer.Ordinal).ToList();
        foreach (var i in ordered) result[i.Key] = i.Position;
        if (ordered.Count < 3) return result;

        var first = Component(ordered[0].Position, axis);
        var last = Component(ordered[^1].Position, axis);
        var gap = (last - first) / (ordered.Count - 1);
        for (var n = 1; n < ordered.Count - 1; n++)
        {
            result[ordered[n].Key] = WithComponent(ordered[n].Position, axis, first + (gap * n));
        }
        return result;
    }

    /// <summary>Wraps an angle into (-180, 180].</summary>
    public static double Normalize180(double degrees)
    {
        var d = degrees % 360.0;
        if (d > 180) d -= 360;
        if (d <= -180) d += 360;
        return d;
    }
}
