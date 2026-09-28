namespace AbioticEditor.Core.WorldSaves;

/// <summary>Where a group rotation turns around.</summary>
public enum PivotKind
{
    /// <summary>The average position of the selected objects.</summary>
    Centroid,

    /// <summary>The position of one chosen object (it may or may not be part of the selection).</summary>
    Piece,

    /// <summary>An explicit point.</summary>
    Point,
}

/// <summary>A pivot for group rotation.</summary>
public readonly record struct GroupPivot(PivotKind Kind, string? Key = null, PlacedVector? Point = null)
{
    /// <summary>Pivot on the centroid of the selection.</summary>
    public static GroupPivot Centroid => new(PivotKind.Centroid);

    /// <summary>Pivot on the position of one object.</summary>
    public static GroupPivot OfObject(string key) => new(PivotKind.Piece, key);

    /// <summary>Pivot on an explicit point.</summary>
    public static GroupPivot At(PlacedVector point) => new(PivotKind.Point, null, point);

    /// <summary>Resolves the pivot to a point, using each object's CURRENT (staged or saved) position.</summary>
    public PlacedVector? Resolve(
        IReadOnlyCollection<string> selection, Func<string, PlacedVector?> positionOf)
    {
        switch (Kind)
        {
            case PivotKind.Point:
                return Point;
            case PivotKind.Piece:
                return Key is null ? null : positionOf(Key);
            default:
                var positions = selection.Select(positionOf).Where(p => p is not null).Select(p => p!.Value).ToList();
                if (positions.Count == 0) return null;
                return new PlacedVector(
                    positions.Average(p => p.X), positions.Average(p => p.Y), positions.Average(p => p.Z));
        }
    }
}

/// <summary>Which objects a group operation staged and which it had to skip.</summary>
public sealed record GroupTransformResult(
    IReadOnlyList<string> Staged,
    IReadOnlyList<(string Key, string Reason)> Skipped);

/// <summary>
/// Group move / rotate / snap / align / distribute built on <see cref="StagedPlacedTransforms"/>. Every
/// call only STAGES (nothing touches a save) and composes with earlier staged edits, because each reads
/// the object's current staged transform. An object that has no saved transform is skipped and reported.
/// </summary>
public static class PlacedGroupTransforms
{
    /// <summary>Stages a move of every selected object by the same delta (centimetres).</summary>
    public static GroupTransformResult MoveBy(
        StagedPlacedTransforms staged, WorldSaveData data, IEnumerable<string> keys, double dx, double dy, double dz)
    {
        var done = new List<string>();
        var skipped = new List<(string, string)>();
        foreach (var key in Distinct(keys))
        {
            if (staged.MoveBy(data, key, dx, dy, dz)) done.Add(key);
            else skipped.Add((key, "Object is not in this save or has no location."));
        }
        return new GroupTransformResult(done, skipped);
    }

    /// <summary>
    /// Stages a yaw rotation (degrees, about world Z) of the whole selection around a pivot: each object's
    /// position swings around the pivot and its own rotation is turned by the same yaw.
    /// </summary>
    public static GroupTransformResult RotateYaw(
        StagedPlacedTransforms staged, WorldSaveData data, IEnumerable<string> keys, double degrees, GroupPivot pivot)
    {
        var list = Distinct(keys).ToList();
        var done = new List<string>();
        var skipped = new List<(string, string)>();
        var resolved = pivot.Resolve(list, k => staged.Current(data, k)?.Translation);
        if (resolved is not { } center)
        {
            skipped.AddRange(list.Select(k => (k, "The pivot could not be resolved (no position).")));
            return new GroupTransformResult(done, skipped);
        }

        foreach (var key in list)
        {
            if (staged.Current(data, key) is not { Translation: { } t } cur)
            {
                skipped.Add((key, "Object is not in this save or has no location."));
                continue;
            }
            // A missing rotation member is staged as identity-composed so the preview reports it as blocked
            // (the editor never creates that member) instead of silently swinging the object without turning it.
            var rotation = PlacementMath.ComposeYaw(cur.EffectiveRotation, degrees);
            staged.Stage(key, PlacementMath.RotateAboutZ(t, center, degrees), rotation);
            done.Add(key);
        }
        return new GroupTransformResult(done, skipped);
    }

    /// <summary>
    /// Snaps positions to a grid and optionally yaw to a step. <paramref name="stepZ"/> null leaves height
    /// alone, <paramref name="yawStepDegrees"/> null leaves rotation alone.
    /// </summary>
    public static GroupTransformResult SnapToGrid(
        StagedPlacedTransforms staged, WorldSaveData data, IEnumerable<string> keys,
        double stepXY, double? stepZ = null, double? yawStepDegrees = null, PlacedVector? origin = null)
    {
        var done = new List<string>();
        var skipped = new List<(string, string)>();
        foreach (var key in Distinct(keys))
        {
            if (staged.Current(data, key) is not { Translation: { } t } cur)
            {
                skipped.Add((key, "Object is not in this save or has no location."));
                continue;
            }
            var pos = PlacementMath.SnapPosition(t, stepXY, stepZ, origin);
            var rot = cur.Rotation;
            if (yawStepDegrees is { } step && rot is { } r)
            {
                rot = PlacementMath.SnapYaw(r, step);
            }
            staged.Stage(key, pos, rot);
            done.Add(key);
        }
        return new GroupTransformResult(done, skipped);
    }

    /// <summary>Stages every selected object's yaw to equal that of <paramref name="referenceKey"/>.</summary>
    public static GroupTransformResult AlignYaw(
        StagedPlacedTransforms staged, WorldSaveData data, IEnumerable<string> keys, string referenceKey)
    {
        var done = new List<string>();
        var skipped = new List<(string, string)>();
        if (staged.Current(data, referenceKey) is not { Rotation: { } reference })
        {
            return new GroupTransformResult(done, [(referenceKey, "The reference object has no rotation to align to.")]);
        }
        foreach (var key in Distinct(keys))
        {
            if (key == referenceKey) continue;
            if (staged.Current(data, key) is not { } cur)
            {
                skipped.Add((key, "Object is not in this save."));
                continue;
            }
            if (cur.Rotation is not { } own)
            {
                skipped.Add((key, "Rotation member is omitted in the save; the editor does not create it."));
                continue;
            }
            staged.Stage(key, cur.Translation, PlacementMath.AlignYaw(own, reference));
            done.Add(key);
        }
        return new GroupTransformResult(done, skipped);
    }

    /// <summary>Stages an even spread of centres along one axis (see <see cref="PlacementMath.DistributeAlong"/>).</summary>
    public static GroupTransformResult Distribute(
        StagedPlacedTransforms staged, WorldSaveData data, IEnumerable<string> keys, PlacementAxis axis)
    {
        var done = new List<string>();
        var skipped = new List<(string, string)>();
        var items = new List<(string Key, PlacedVector Position)>();
        foreach (var key in Distinct(keys))
        {
            if (staged.Current(data, key) is { Translation: { } t }) items.Add((key, t));
            else skipped.Add((key, "Object is not in this save or has no location."));
        }
        var spread = PlacementMath.DistributeAlong(items, axis);
        foreach (var (key, position) in items)
        {
            if (spread[key] == position) continue;
            staged.Stage(key, spread[key], staged.Current(data, key)?.Rotation);
            done.Add(key);
        }
        return new GroupTransformResult(done, skipped);
    }

    private static IEnumerable<string> Distinct(IEnumerable<string> keys)
        => keys.Distinct(StringComparer.Ordinal);
}
