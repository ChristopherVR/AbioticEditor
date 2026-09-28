using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>One pending move/rotate for a placed object, in the save's own units.</summary>
/// <param name="Key">The <c>DeployedObjectMap</c> key (the object's identity).</param>
/// <param name="Translation">New location (cm), or null to leave it.</param>
/// <param name="Rotation">New rotation quaternion, or null to leave it.</param>
public sealed record StagedTransform(string Key, PlacedVector? Translation, PlacedQuaternion? Rotation);

/// <summary>Before/after view of one staged change.</summary>
public sealed record TransformPreviewRow(
    string Key,
    string? ClassName,
    bool IsPlayerBuilt,
    PlacedObjectTransform? Before,
    PlacedObjectTransform? After,
    double? DistanceCm,
    double? YawDeltaDegrees,
    IReadOnlyList<string> Warnings)
{
    /// <summary>True when applying this row would be refused by default (see <see cref="Warnings"/>).</summary>
    public bool Blocked { get; init; }

    /// <summary>True when the save itself lacks what the edit needs (no transform / omitted member / no object).</summary>
    public bool StructurallyBlocked { get; init; }
}

/// <summary>Outcome of <see cref="StagedPlacedTransforms.ApplyTo"/>.</summary>
public sealed record StagedTransformApplyResult(
    IReadOnlyList<string> Applied,
    IReadOnlyList<(string Key, string Reason)> Refused);

/// <summary>
/// Pending move/rotate edits for placed objects, keyed by object identity, with revert and a
/// before/after preview. Staging never touches the save; <see cref="ApplyTo"/> is the only call that
/// writes (through <see cref="WorldSaveWriter.ApplyPlacedObjectTransform"/>) and is expected to be
/// followed by <see cref="WorldSaveWriter.WriteToFile"/>.
/// </summary>
/// <remarks>
/// Only player-built objects (<c>DeployedByPlayer</c> true) may be applied by default: level-placed
/// statics are re-placed by the level itself and moving them is unverified. Coordinates are the save's
/// own; whether the game accepts them for a given streamed level is unproven (see
/// docs/reference/research/base-building-coordinate-spaces.md).
/// </remarks>
public sealed class StagedPlacedTransforms
{
    private readonly Dictionary<string, StagedTransform> _pending = new(StringComparer.Ordinal);

    /// <summary>Pending edits in staging order of first stage.</summary>
    public IReadOnlyCollection<StagedTransform> Pending => _pending.Values;

    /// <summary>True when nothing is staged.</summary>
    public bool IsEmpty => _pending.Count == 0;

    /// <summary>Stages an absolute location and/or rotation, replacing an earlier stage for the key.</summary>
    public void Stage(string key, PlacedVector? translation, PlacedQuaternion? rotation)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (translation is null && rotation is null)
        {
            _pending.Remove(key);
            return;
        }
        _pending[key] = new StagedTransform(key, translation, rotation);
    }

    /// <summary>
    /// Stages a move of <paramref name="key"/> by a delta relative to its CURRENT staged location (or the
    /// saved one). Returns false when the object is not in <paramref name="data"/> or has no location.
    /// </summary>
    public bool MoveBy(WorldSaveData data, string key, double dx, double dy, double dz)
    {
        if (Current(data, key) is not { Translation: { } t }) return false;
        var rotation = _pending.TryGetValue(key, out var s) ? s.Rotation : null;
        Stage(key, new PlacedVector(t.X + dx, t.Y + dy, t.Z + dz), rotation);
        return true;
    }

    /// <summary>
    /// Stages an additional rotation about the world vertical (Z) axis, in degrees, composed onto the
    /// current staged or saved rotation (Unreal yaw convention: positive yaw is the direction the saved
    /// quaternion's Z component grows). Returns false when the object or its rotation is absent.
    /// </summary>
    public bool RotateYawBy(WorldSaveData data, string key, double degrees)
    {
        if (Current(data, key) is not { Rotation: { } q }) return false;
        var half = degrees * Math.PI / 360.0;
        var (sz, cz) = (Math.Sin(half), Math.Cos(half));
        // Hamilton product qz * q: apply q, then a world-space yaw.
        var r = new PlacedQuaternion(
            (cz * q.X) - (sz * q.Y),
            (cz * q.Y) + (sz * q.X),
            (cz * q.Z) + (sz * q.W),
            (cz * q.W) - (sz * q.Z));
        var translation = _pending.TryGetValue(key, out var s) ? s.Translation : null;
        Stage(key, translation, r);
        return true;
    }

    /// <summary>Drops the staged edit for one object. Returns true when something was staged.</summary>
    public bool Revert(string key) => _pending.Remove(key);

    /// <summary>Drops every staged edit.</summary>
    public void RevertAll() => _pending.Clear();

    /// <summary>The transform an object would have with its staged edit applied (or its saved one).</summary>
    public PlacedObjectTransform? Current(WorldSaveData data, string key)
    {
        var saved = SavedTransform(data, key);
        if (saved is null || !_pending.TryGetValue(key, out var s)) return saved;
        return saved with
        {
            Translation = s.Translation ?? saved.Translation,
            Rotation = s.Rotation ?? saved.Rotation,
        };
    }

    private static PlacedObjectTransform? SavedTransform(WorldSaveData data, string key)
        => WorldMapAccessor.FindEntry(data.Raw, "DeployedObjectMap", key) is { } props
            ? PlacedObjectCensus.ReadTransform(props)
            : null;

    /// <summary>Builds the before/after preview for every staged edit. Reads only.</summary>
    public IReadOnlyList<TransformPreviewRow> Preview(WorldSaveData data)
    {
        var rows = new List<TransformPreviewRow>();
        foreach (var s in _pending.Values)
        {
            var warnings = new List<string>();
            var blocked = false;
            var structural = false;
            var props = WorldMapAccessor.FindEntry(data.Raw, "DeployedObjectMap", s.Key);
            if (props is null)
            {
                rows.Add(new TransformPreviewRow(s.Key, null, false, null, null, null, null,
                    ["Object is not in this save."]) { Blocked = true, StructurallyBlocked = true });
                continue;
            }

            var before = PlacedObjectCensus.ReadTransform(props);
            var playerBuilt = props.TryGetBool("DeployedByPlayer_") == true;
            var className = PlacedObjectCensus.ClassNameOf(PlacedObjectCensus.ClassPathOf(props));

            if (before is null)
            {
                warnings.Add("Object has no saved transform to edit.");
                blocked = structural = true;
            }
            else
            {
                if (s.Translation is not null && before.Translation is null)
                {
                    warnings.Add("Location member is omitted in the save; the editor does not create it.");
                    blocked = structural = true;
                }
                if (s.Rotation is not null && before.Rotation is null)
                {
                    warnings.Add("Rotation member is omitted in the save; the editor does not create it.");
                    blocked = structural = true;
                }
            }
            if (!playerBuilt)
            {
                warnings.Add("Level-placed object: the level re-places it, so moving it is unverified and refused by default.");
                blocked = true;
            }
            warnings.Add("Coordinate conversion to in-game placement is unverified.");

            PlacedObjectTransform? after = before is null
                ? null
                : before with
                {
                    Translation = s.Translation ?? before.Translation,
                    Rotation = s.Rotation ?? before.Rotation,
                };

            double? distance = null;
            if (before?.Translation is { } b && after?.Translation is { } a)
            {
                distance = Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)));
            }
            double? yaw = null;
            if (before?.Rotation is { } rb && after?.Rotation is { } ra)
            {
                yaw = Normalize180(ra.YawDegrees - rb.YawDegrees);
            }

            rows.Add(new TransformPreviewRow(s.Key, className, playerBuilt, before, after, distance, yaw, warnings)
            {
                Blocked = blocked,
                StructurallyBlocked = structural,
            });
        }
        return rows;
    }

    private static double Normalize180(double degrees)
    {
        var d = degrees % 360.0;
        if (d > 180) d -= 360;
        if (d <= -180) d += 360;
        return d;
    }

    /// <summary>
    /// Writes every non-blocked staged edit into <paramref name="data"/>'s raw tree (not to disk) and
    /// clears the ones applied. Refused edits stay staged so the caller can show why.
    /// </summary>
    public StagedTransformApplyResult ApplyTo(WorldSaveData data, bool allowLevelPlaced = false)
    {
        var applied = new List<string>();
        var refused = new List<(string, string)>();
        var previews = Preview(data).ToDictionary(p => p.Key, StringComparer.Ordinal);
        foreach (var s in _pending.Values.ToList())
        {
            var p = previews[s.Key];
            var refuse = p.StructurallyBlocked || (p.Blocked && !allowLevelPlaced);
            if (refuse)
            {
                refused.Add((s.Key, p.Warnings[0]));
                continue;
            }
            if (WorldSaveWriter.ApplyPlacedObjectTransform(data, s.Key, s.Translation, s.Rotation))
            {
                applied.Add(s.Key);
                _pending.Remove(s.Key);
            }
            else
            {
                refused.Add((s.Key, "The save has no matching transform members to rewrite."));
            }
        }
        return new StagedTransformApplyResult(applied, refused);
    }
}
