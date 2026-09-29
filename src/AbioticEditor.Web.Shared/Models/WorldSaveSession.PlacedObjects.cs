using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>Outcome of asking the session to stage a move or rotation for one placed object.</summary>
/// <param name="Staged">True when the edit is now pending.</param>
/// <param name="Refusal">Why it was refused (already worded for the player), or null when staged.</param>
public sealed record PlacedTransformStageResult(bool Staged, PlacedTransformRefusal? Refusal = null)
{
    public static PlacedTransformStageResult Ok { get; } = new(true);

    public static PlacedTransformStageResult Refused(PlacedTransformRefusal reason) => new(false, reason);
}

/// <summary>Why a placed-object edit was not staged.</summary>
public enum PlacedTransformRefusal
{
    /// <summary>The object is not in this save.</summary>
    NotFound,

    /// <summary>The object is placed by the level itself, not built by a player.</summary>
    LevelPlaced,

    /// <summary>The save omits the location or rotation member, and the editor never creates one.</summary>
    MemberOmitted,
}

/// <summary>
/// Placed-object (3D base view) part of the world session: a cached census of the region's
/// placed objects and the staged move/rotate edits held in a <see cref="StagedPlacedTransforms"/>.
/// Staging never touches the loaded save; the edits are written, into the same disposable clone
/// every other staged edit goes through, only by <see cref="SaveAsync"/>, so the normal
/// <c>.bak</c> behaviour is unchanged.
/// </summary>
public sealed partial class WorldSaveSession
{
    private readonly StagedPlacedTransforms _placedTransforms = new();
    private IReadOnlyList<PlacedObjectSummary>? _placedObjects;
    private Dictionary<string, PlacedObjectSummary>? _placedByKey;

    /// <summary>Every object in this region's <c>DeployedObjectMap</c> as saved (staged edits are not reflected).</summary>
    public IReadOnlyList<PlacedObjectSummary> PlacedObjects
        => _placedObjects ??= PlacedObjectCensus.Build(_data, _path, includeObjects: true).Objects ?? [];

    /// <summary>One placed object by its map key, or null.</summary>
    public PlacedObjectSummary? FindPlacedObject(string key)
    {
        _placedByKey ??= PlacedObjects
            .GroupBy(o => o.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _placedByKey.GetValueOrDefault(key);
    }

    /// <summary>Bumps on every change to the staged edits or the cached census, so a view can tell when to redraw.</summary>
    public int PlacedTransformsRevision { get; private set; }

    /// <summary>Clears the staged edits without touching the status line (used by <see cref="Revert"/>).</summary>
    private void ClearPlacedTransforms()
    {
        _placedTransforms.RevertAll();
        PlacedTransformsRevision++;
    }

    /// <summary>True when at least one move/rotate is staged.</summary>
    public bool HasStagedPlacedTransforms => !_placedTransforms.IsEmpty;

    /// <summary>The pending edits, in the order they were first staged.</summary>
    public IReadOnlyCollection<StagedTransform> StagedPlacedTransforms => _placedTransforms.Pending;

    /// <summary>The object's transform with its staged edit applied (or the saved one), or null when it has none.</summary>
    public PlacedObjectTransform? CurrentPlacedTransform(string key) => _placedTransforms.Current(_data, key);

    /// <summary>True when <paramref name="key"/> has a staged edit.</summary>
    public bool IsPlacedTransformStaged(string key) => _placedTransforms.Pending.Any(p => string.Equals(p.Key, key, StringComparison.Ordinal));

    /// <summary>Before/after rows for every staged edit. Reads only.</summary>
    public IReadOnlyList<TransformPreviewRow> PlacedTransformPreview() => _placedTransforms.Preview(_data);

    /// <summary>
    /// Stages an absolute location and/or rotation (save space, centimetres and a quaternion) for a
    /// player-built object. Level-placed objects are refused, and so is an object whose save omits
    /// a member the edit needs (the writer never creates one).
    /// </summary>
    public PlacedTransformStageResult StagePlacedTransform(string key, PlacedVector? translation, PlacedQuaternion? rotation)
    {
        if (FindPlacedObject(key) is not { } obj) return PlacedTransformStageResult.Refused(PlacedTransformRefusal.NotFound);
        if (obj.DeployedByPlayer != true) return PlacedTransformStageResult.Refused(PlacedTransformRefusal.LevelPlaced);
        if (obj.Transform is null
            || (translation is not null && obj.Transform.Translation is null)
            || (rotation is not null && obj.Transform.Rotation is null))
        {
            return PlacedTransformStageResult.Refused(PlacedTransformRefusal.MemberOmitted);
        }

        var pending = _placedTransforms.Pending.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));
        var effectiveT = translation ?? pending?.Translation;
        var effectiveR = rotation is { } r ? PlacedSceneSpace.Normalize(r) : pending?.Rotation;

        // An edit that lands exactly on the saved value is no edit at all: drop it so the
        // unsaved-changes indicator does not light up for a no-op.
        if (effectiveT is { } t && obj.Transform.Translation is { } savedT && t == savedT) effectiveT = null;
        if (effectiveR is { } q && obj.Transform.Rotation is { } savedQ && q == savedQ) effectiveR = null;

        _placedTransforms.Stage(key, effectiveT, effectiveR);
        PlacedTransformsRevision++;
        UpdateStatus();
        return PlacedTransformStageResult.Ok;
    }

    /// <summary>Drops the staged edit for one object. Returns true when one was staged.</summary>
    public bool RevertPlacedTransform(string key)
    {
        var removed = _placedTransforms.Revert(key);
        if (removed)
        {
            PlacedTransformsRevision++;
            UpdateStatus();
        }
        return removed;
    }

    /// <summary>Drops every staged move/rotate.</summary>
    public void RevertAllPlacedTransforms()
    {
        ClearPlacedTransforms();
        UpdateStatus();
    }

    /// <summary>
    /// Writes the staged edits into <paramref name="workingData"/> (the disposable clone that is about
    /// to be written) and returns each moved object's new transform. Works on a copy of the staged set,
    /// so a failed disk write leaves the edits staged. Any refusal aborts the save with an explanation,
    /// so nothing half-applied is written.
    /// </summary>
    private List<(string Key, PlacedObjectTransform After)> ApplyStagedPlacedTransforms(WorldSaveData workingData)
    {
        if (_placedTransforms.IsEmpty) return [];
        var copy = new StagedPlacedTransforms();
        foreach (var edit in _placedTransforms.Pending) copy.Stage(edit.Key, edit.Translation, edit.Rotation);
        var result = copy.ApplyTo(workingData);
        if (result.Refused.Count > 0)
        {
            var first = result.Refused[0];
            throw new InvalidOperationException($"Could not move object '{first.Key}': {first.Reason}");
        }

        var moved = new List<(string, PlacedObjectTransform)>();
        foreach (var key in result.Applied)
        {
            if (WorldMapAccessor.FindEntry(workingData.Raw, "DeployedObjectMap", key) is { } props
                && PlacedObjectCensus.ReadTransform(props) is { } after)
            {
                moved.Add((key, after));
            }
        }
        return moved;
    }

    /// <summary>After a successful write: clears the staged edits and refreshes what was cached from the old tree.</summary>
    private void CommitPlacedTransforms(List<(string Key, PlacedObjectTransform After)> moved)
    {
        ClearPlacedTransforms();
        _placedObjects = null;
        _placedByKey = null;
        foreach (var (key, after) in moved)
        {
            if (after.Translation is not { } t) continue;
            if (_deployables.TryGetValue(key, out var d)) _deployables[key] = d with { X = t.X, Y = t.Y, Z = t.Z };
            if (_originalDeployables.TryGetValue(key, out var o)) _originalDeployables[key] = o with { X = t.X, Y = t.Y, Z = t.Z };
        }
    }
}
