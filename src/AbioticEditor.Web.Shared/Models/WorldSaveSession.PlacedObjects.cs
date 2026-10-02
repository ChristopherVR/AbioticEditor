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
/// placed objects and every staged base edit (moves, rotations, deletions, duplications) held in ONE
/// <see cref="StagedBaseEdits"/>. Staging never touches the loaded save; the edits are written, into the
/// same disposable clone every other staged edit goes through, only by <see cref="SaveAsync"/>, so the
/// normal <c>.bak</c> behaviour is unchanged. If the edits cannot be applied (a blocking finding), the
/// save writes nothing and they stay staged.
/// </summary>
public sealed partial class WorldSaveSession
{
    private readonly StagedBaseEdits _baseEdits = new();
    private StagedPlacedTransforms _placedTransforms => _baseEdits.Transforms;
    private BaseEditPreview? _basePreview;
    private int _basePreviewRevision = -1;
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

    /// <summary>Bumps on every change to any staged base edit or the cached census, so a view can tell when to redraw.</summary>
    public int PlacedTransformsRevision { get; private set; }

    /// <summary>Clears the staged edits without touching the status line (used by <see cref="Revert"/>).</summary>
    private void ClearBaseEdits()
    {
        _baseEdits.RevertAll();
        LastBaseEditRefusal = [];
        PlacedTransformsRevision++;
    }

    /// <summary>True when at least one move/rotate is staged.</summary>
    public bool HasStagedPlacedTransforms => !_placedTransforms.IsEmpty;

    /// <summary>The pending edits, in the order they were first staged.</summary>
    public IReadOnlyCollection<StagedTransform> StagedPlacedTransforms => _placedTransforms.Pending;

    /// <summary>The object's transform with its staged edit applied (or the saved one), or null when it has none.</summary>
    public PlacedObjectTransform? CurrentPlacedTransform(string key)
        // The census row holds the saved transform (a dictionary lookup); scanning the save's object map for
        // every object made each 3D scene build quadratic in the number of objects.
        => FindPlacedObject(key) is { } row ? _placedTransforms.Current(row.Transform, key) : _placedTransforms.Current(_data, key);

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

    /// <summary>Drops every staged move/rotate (deletions and copies stay staged).</summary>
    public void RevertAllPlacedTransforms()
    {
        _placedTransforms.RevertAll();
        PlacedTransformsRevision++;
        UpdateStatus();
    }

    // ---------- every kind of base edit ----------

    /// <summary>True when any base edit (move, rotate, delete, copy) is staged.</summary>
    public bool HasStagedBaseEdits => !_baseEdits.IsEmpty;

    /// <summary>Objects staged for deletion, with the policy chosen for each.</summary>
    public IReadOnlyDictionary<string, DeletePolicy> StagedPlacedDeletions => _baseEdits.Deletions;

    /// <summary>Copies staged, in staging order.</summary>
    public IReadOnlyList<StagedDuplication> StagedPlacedDuplications => _baseEdits.Duplications;

    /// <summary>True when <paramref name="key"/> is staged for deletion.</summary>
    public bool IsPlacedDeletionStaged(string key) => _baseEdits.Deletions.ContainsKey(key);

    /// <summary>What blocked the last save of staged base edits (empty when nothing did). Cleared by any staging change.</summary>
    public IReadOnlyList<BaseEditIssue> LastBaseEditRefusal { get; private set; } = [];

    /// <summary>
    /// The combined preview of everything staged (moves, deletions, copies, findings, proximity hints).
    /// Reads only, and cached until the staged edits change.
    /// </summary>
    public BaseEditPreview PreviewBaseEdits()
    {
        if (_basePreview is null || _basePreviewRevision != PlacedTransformsRevision)
        {
            _basePreview = _baseEdits.Preview(_data);
            _basePreviewRevision = PlacedTransformsRevision;
        }
        return _basePreview;
    }

    /// <summary>
    /// Previews what staging more edits WOULD do, on a copy of the staged set: nothing is staged, the
    /// staged edits are included so the findings are the combined ones, and the session does not change.
    /// </summary>
    public BaseEditPreview PreviewHypothetical(Action<StagedBaseEdits> stage)
    {
        var scratch = _baseEdits.Clone();
        stage(scratch);
        return scratch.Preview(_data);
    }

    /// <summary>Which of <paramref name="keys"/> a delete or copy can work on, and why the rest cannot.</summary>
    public (List<string> Accepted, List<(string Key, PlacedTransformRefusal Reason)> Refused) SplitEditableSelection(
        IEnumerable<string> keys, bool needsTransform)
    {
        var accepted = new List<string>();
        var refused = new List<(string, PlacedTransformRefusal)>();
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            if (FindPlacedObject(key) is not { } obj) refused.Add((key, PlacedTransformRefusal.NotFound));
            else if (obj.DeployedByPlayer != true) refused.Add((key, PlacedTransformRefusal.LevelPlaced));
            else if (needsTransform && obj.Transform?.Translation is null) refused.Add((key, PlacedTransformRefusal.MemberOmitted));
            else accepted.Add(key);
        }
        return (accepted, refused);
    }

    /// <summary>
    /// Drops staged transforms that landed exactly on the saved value, so a snap of an already-snapped
    /// object does not light the unsaved-changes indicator for nothing (same rule as the single edit).
    /// </summary>
    private void PruneNoOpTransforms(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            var pending = _placedTransforms.Pending.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal));
            if (pending is null || FindPlacedObject(key)?.Transform is not { } saved) continue;
            PlacedVector? t = pending.Translation is { } pt && saved.Translation is { } st && pt == st ? null : pending.Translation;
            PlacedQuaternion? r = pending.Rotation is { } pr && saved.Rotation is { } sr && pr == sr ? null : pending.Rotation;
            if (t is null && r is null) _placedTransforms.Revert(key);
            else if (t != pending.Translation || r != pending.Rotation) _placedTransforms.Stage(key, t, r);
        }
    }

    /// <summary>Stages a move of every selected player-built object by the same delta (cm).</summary>
    public PlacedGroupStageResult StageGroupMove(IEnumerable<string> keys, double dx, double dy, double dz)
        => StageTransforms(keys, accepted => _baseEdits.MoveBy(_data, accepted, dx, dy, dz));

    /// <summary>Stages a yaw turn of the selection about a pivot (default: the centroid).</summary>
    public PlacedGroupStageResult StageGroupRotate(IEnumerable<string> keys, double degrees, GroupPivot? pivot = null)
        => StageTransforms(keys, accepted => _baseEdits.RotateYaw(_data, accepted, degrees, pivot));

    /// <summary>Stages snapping positions (and optionally yaw) to a grid.</summary>
    public PlacedGroupStageResult StageGroupSnap(IEnumerable<string> keys, double stepXY, double? stepZ, double? yawStepDegrees)
        => StageTransforms(keys, accepted => _baseEdits.SnapToGrid(_data, accepted, stepXY, stepZ, yawStepDegrees));

    /// <summary>Stages turning every selected object to the yaw of <paramref name="referenceKey"/>.</summary>
    public PlacedGroupStageResult StageGroupAlignYaw(IEnumerable<string> keys, string referenceKey)
        => StageTransforms(keys, accepted => _baseEdits.AlignYaw(_data, accepted, referenceKey));

    /// <summary>Stages an even spread of the selection along an axis.</summary>
    public PlacedGroupStageResult StageGroupDistribute(IEnumerable<string> keys, PlacementAxis axis)
        => StageTransforms(keys, accepted => _baseEdits.Distribute(_data, accepted, axis));

    private PlacedGroupStageResult StageTransforms(IEnumerable<string> keys, Func<IReadOnlyList<string>, GroupTransformResult> op)
    {
        var (accepted, refused) = SplitEditableSelection(keys, needsTransform: true);
        IReadOnlyList<(string Key, string Reason)> skipped = [];
        var staged = 0;
        if (accepted.Count > 0)
        {
            var result = op(accepted);
            skipped = result.Skipped;
            staged = result.Staged.Count;
            PruneNoOpTransforms(result.Staged);
        }
        PlacedTransformsRevision++;
        UpdateStatus();
        return new PlacedGroupStageResult(staged, refused, skipped);
    }

    /// <summary>Stages deleting the selected player-built objects with <paramref name="policy"/>.</summary>
    public PlacedGroupStageResult StagePlacedDelete(IEnumerable<string> keys, DeletePolicy? policy = null)
    {
        var (accepted, refused) = SplitEditableSelection(keys, needsTransform: false);
        if (accepted.Count > 0) _baseEdits.StageDelete(accepted, policy);
        PlacedTransformsRevision++;
        UpdateStatus();
        return new PlacedGroupStageResult(accepted.Count, refused, []);
    }

    /// <summary>Stages copying the selected player-built objects (offset in cm, yaw in degrees about the pivot).</summary>
    public PlacedGroupStageResult StagePlacedDuplicate(
        IEnumerable<string> keys, PlacedVector offset, double yawDegrees = 0, GroupPivot? pivot = null, DuplicatePolicy? policy = null)
    {
        var (accepted, refused) = SplitEditableSelection(keys, needsTransform: true);
        if (accepted.Count > 0) _baseEdits.StageDuplicate(accepted, offset, yawDegrees, pivot, policy);
        PlacedTransformsRevision++;
        UpdateStatus();
        return new PlacedGroupStageResult(accepted.Count, refused, []);
    }

    /// <summary>
    /// Stages a new object: a copy of <paramref name="donorKey"/> (a player-built object of the wanted kind)
    /// standing at <paramref name="at"/> (cm), turned by <paramref name="yawDegrees"/> about itself. It is a
    /// one-object duplicate, so it starts empty and unplugged and keeps every member the game wrote for the
    /// donor. Returns the staged duplication, or null when the donor cannot be copied.
    /// </summary>
    public StagedDuplication? StagePlacedNew(string donorKey, PlacedVector at, double yawDegrees = 0)
    {
        if (CurrentPlacedTransform(donorKey)?.Translation is not { } from) return null;
        var result = StagePlacedDuplicate([donorKey], new PlacedVector(at.X - from.X, at.Y - from.Y, at.Z - from.Z), yawDegrees,
            GroupPivot.OfObject(donorKey), DuplicatePolicy.Default);
        return result.Staged > 0 ? StagedPlacedDuplications[^1] : null;
    }

    /// <summary>Drops the staged deletion of one object.</summary>
    public bool RevertPlacedDeletion(string key)
    {
        var removed = _baseEdits.RevertDeletion(key);
        if (removed) { PlacedTransformsRevision++; UpdateStatus(); }
        return removed;
    }

    /// <summary>Drops one staged copy (all the objects it would create).</summary>
    public bool RevertPlacedDuplication(int id)
    {
        var removed = _baseEdits.RevertDuplication(id);
        if (removed) { PlacedTransformsRevision++; UpdateStatus(); }
        return removed;
    }

    /// <summary>Drops every staged base edit of every kind.</summary>
    public void RevertAllBaseEdits()
    {
        ClearBaseEdits();
        UpdateStatus();
    }

    // ---------- kinds built in the player's other worlds ----------

    /// <summary>A kind of player-built object found in another world on this machine (the donor of a placed copy).</summary>
    public sealed record OtherWorldKind(string World, string FilePath, string ClassPath, string? ClassName, int Count, string DonorKey);

    /// <summary>Kinds built in the player's other worlds (same save file name, so the same map); empty until loaded.</summary>
    public IReadOnlyList<OtherWorldKind> OtherWorldKinds { get; private set; } = [];

    /// <summary>True once the other worlds have been scanned.</summary>
    public bool OtherWorldsLoaded { get; private set; }

    private Task? _otherWorldsTask;
    private readonly Dictionary<string, WorldSaveData> _donorSaves = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Scans the other worlds next to this one (their save of the same name, read only) for player-built
    /// objects, so a kind never built in this world can still be placed. Only the list of kinds is kept;
    /// a world's save is read again when one of its kinds is placed.
    /// </summary>
    public Task LoadOtherWorldKindsAsync() => _otherWorldsTask ??= RunLoadOtherWorldKindsAsync();

    private async Task RunLoadOtherWorldKindsAsync()
    {
        var kinds = new List<OtherWorldKind>();
        try
        {
            if ((_files is null || _files.HasLocalPaths)
                && System.IO.Path.GetDirectoryName(_path) is { Length: > 0 } dir
                && System.IO.Path.GetDirectoryName(dir) is { Length: > 0 } worlds && Directory.Exists(worlds))
            {
                var fileName = System.IO.Path.GetFileName(_path);
                foreach (var other in Directory.EnumerateDirectories(worlds).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.Equals(System.IO.Path.GetFullPath(other), System.IO.Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) continue;
                    var file = System.IO.Path.Combine(other, fileName);
                    if (!File.Exists(file)) continue;
                    try
                    {
                        var census = await Task.Run(() => PlacedObjectCensus.Build(WorldSaveReader.ReadFromFile(file), file, includeObjects: true)).ConfigureAwait(false);
                        var world = System.IO.Path.GetFileName(other);
                        foreach (var g in (census.Objects ?? [])
                                     .Where(o => o.DeployedByPlayer == true && o.Key.Length == 32 && o.ClassPath is { Length: > 0 } && o.Transform?.Translation is not null)
                                     .GroupBy(o => o.ClassPath!, StringComparer.OrdinalIgnoreCase))
                        {
                            var donor = g.OrderBy(o => o.Transform?.Rotation is null ? 1 : 0).ThenBy(o => o.Key, StringComparer.Ordinal).First();
                            kinds.Add(new OtherWorldKind(world, file, g.Key, donor.ClassName, g.Count(), donor.Key));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or NotSupportedException or FormatException or EndOfStreamException)
                    {
                        AbioticEditor.Core.Diagnostics.EditorLog.Warn("BaseEdits", $"Could not read {file} for other-world kinds: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AbioticEditor.Core.Diagnostics.EditorLog.Warn("BaseEdits", $"Could not list other worlds: {ex.Message}");
        }
        OtherWorldKinds = kinds;
        OtherWorldsLoaded = true;
    }

    private readonly Dictionary<string, OtherWorldKind?> _donorsByClass = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A piece of the given kind the player built somewhere, to copy a level-placed one from: first
    /// this world's other areas, then the same area in their other worlds, then any area of those.
    /// Stops at the first one found (each save read is a few hundred milliseconds to seconds). Null
    /// when they have never built one.
    /// </summary>
    public async Task<OtherWorldKind?> FindDonorAsync(string classPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classPath);
        if (_donorsByClass.TryGetValue(classPath, out var known)) return known;
        OtherWorldKind? found = null;
        if ((_files is null || _files.HasLocalPaths) && System.IO.Path.GetDirectoryName(_path) is { Length: > 0 } dir && Directory.Exists(dir))
        {
            var fileName = System.IO.Path.GetFileName(_path);
            var worldsDir = System.IO.Path.GetDirectoryName(dir);
            var others = worldsDir is not null && Directory.Exists(worldsDir)
                ? Directory.EnumerateDirectories(worldsDir).Where(d => !string.Equals(System.IO.Path.GetFullPath(d), System.IO.Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList()
                : [];
            static IEnumerable<string> Regions(string folder) => Directory.EnumerateFiles(folder, "WorldSave_*.sav")
                .Where(f => !System.IO.Path.GetFileName(f).Equals("WorldSave_MetaData.sav", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
            var order = Regions(dir).Where(f => !System.IO.Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                .Concat(others.Select(o => System.IO.Path.Combine(o, fileName)).Where(File.Exists))
                .Concat(others.SelectMany(o => Regions(o).Where(f => !System.IO.Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase))));
            foreach (var file in order)
            {
                try
                {
                    var census = await Task.Run(() => PlacedObjectCensus.Build(WorldSaveReader.ReadFromFile(file), file, includeObjects: true)).ConfigureAwait(false);
                    var matches = (census.Objects ?? [])
                        .Where(o => o.DeployedByPlayer == true && o.Key.Length == 32 && o.Transform?.Translation is not null
                                    && string.Equals(o.ClassPath, classPath, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (matches.Count == 0) continue;
                    var donor = matches.OrderBy(o => o.Transform?.Rotation is null ? 1 : 0).ThenBy(o => o.Key, StringComparer.Ordinal).First();
                    var folder = System.IO.Path.GetDirectoryName(file)!;
                    var world = string.Equals(System.IO.Path.GetFullPath(folder), System.IO.Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)
                        ? System.IO.Path.GetFileNameWithoutExtension(file)["WorldSave_".Length..]
                        : System.IO.Path.GetFileName(folder);
                    found = new OtherWorldKind(world, file, classPath, donor.ClassName, matches.Count, donor.Key);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or NotSupportedException or FormatException or EndOfStreamException)
                {
                    AbioticEditor.Core.Diagnostics.EditorLog.Warn("BaseEdits", $"Could not read {file} while looking for a piece to copy: {ex.Message}");
                }
            }
        }
        _donorsByClass[classPath] = found;
        return found;
    }

    /// <summary>
    /// Stages a new object copied from another world: <paramref name="kind"/>'s donor, standing at
    /// <paramref name="at"/> (cm) and turned by <paramref name="yawDegrees"/>. It starts empty, unplugged
    /// and untagged. Returns the staged copy, or null when the other world's save cannot be read.
    /// </summary>
    public async Task<StagedDuplication?> StagePlacedImportAsync(OtherWorldKind kind, PlacedVector at, double yawDegrees = 0)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (!_donorSaves.TryGetValue(kind.FilePath, out var donor))
        {
            try { donor = await Task.Run(() => WorldSaveReader.ReadFromFile(kind.FilePath)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                AbioticEditor.Core.Diagnostics.EditorLog.Warn("BaseEdits", $"Could not read {kind.FilePath}: {ex.Message}");
                return null;
            }
            _donorSaves[kind.FilePath] = donor;
        }
        if (WorldMapAccessor.FindEntry(donor.Raw, "DeployedObjectMap", kind.DonorKey) is not { } props
            || PlacedObjectCensus.ReadTransform(props)?.Translation is not { } from) return null;
        var staged = _baseEdits.StageImport(donor, kind.World, kind.DonorKey,
            new PlacedVector(at.X - from.X, at.Y - from.Y, at.Z - from.Z), yawDegrees);
        PlacedTransformsRevision++;
        UpdateStatus();
        return staged;
    }

    // ---------- references in the other saves of this world ----------

    /// <summary>True once the sibling saves have been read (or there were none to read).</summary>
    public bool OtherSavesLoaded { get; private set; }

    /// <summary>File names of the other saves scanned for references into deleted objects.</summary>
    public IReadOnlyList<string> OtherSaveNames { get; private set; } = [];

    /// <summary>Other saves that could not be read (the scan is then incomplete).</summary>
    public IReadOnlyList<string> OtherSaveFailures { get; private set; } = [];

    private Task? _otherSavesTask;

    /// <summary>
    /// Reads the other world saves that sit next to this one (read only, never written) so the
    /// delete preview can see references from other regions and the metadata save. Runs once; where
    /// the workspace has no siblings to read (a lone save, a browser folder that did not list them) it
    /// finishes with an empty set and the preview says only this save was scanned.
    /// </summary>
    public Task LoadOtherSavesAsync() => _otherSavesTask ??= RunLoadOtherSavesAsync();

    private async Task RunLoadOtherSavesAsync()
    {
        var paths = new List<string>();
        try
        {
            if ((_files is null || _files.HasLocalPaths) && System.IO.Path.GetDirectoryName(_path) is { Length: > 0 } dir && Directory.Exists(dir))
            {
                paths.AddRange(Directory.EnumerateFiles(dir, "WorldSave_*.sav")
                    .Where(f => !string.Equals(f, _path, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                paths.AddRange(_siblingWorldSavePaths);
                if (_siblingMetadataPath is not null) paths.Add(_siblingMetadataPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AbioticEditor.Core.Diagnostics.EditorLog.Warn("BaseEdits", $"Could not list sibling saves: {ex.Message}");
        }

        var loaded = new List<(string Name, WorldSaveData Data)>();
        var failed = new List<string>();
        foreach (var path in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var name = FileNameOf(path);
            try
            {
                var bytes = _files is null
                    ? await File.ReadAllBytesAsync(path).ConfigureAwait(false)
                    : await _files.ReadAllBytesAsync(path).ConfigureAwait(false);
                var save = await Task.Run(() => WorldSaveReader.ReadFromStream(new MemoryStream(bytes, writable: false))).ConfigureAwait(false);
                loaded.Add((name, save));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or NotSupportedException)
            {
                AbioticEditor.Core.Diagnostics.EditorLog.Warn("BaseEdits", $"Could not read {name} for the reference scan: {ex.Message}");
                failed.Add(name);
            }
        }
        _baseEdits.OtherSaves = loaded;
        _baseEdits.PrimaryName = FileNameOf(_path);
        OtherSaveNames = loaded.Select(l => l.Name).ToArray();
        OtherSaveFailures = failed;
        OtherSavesLoaded = true;
        PlacedTransformsRevision++;
    }

    /// <summary>Lets go of the parsed sibling saves (they are large). The next preview that needs them reads them again.</summary>
    private void ReleaseOtherSaves()
    {
        _baseEdits.OtherSaves = [];
        OtherSaveNames = [];
        OtherSaveFailures = [];
        OtherSavesLoaded = false;
        _otherSavesTask = null;
    }

    // ---------- the save ----------

    /// <summary>
    /// Applies every staged base edit to <paramref name="workingData"/> (the disposable clone about to be
    /// written), all or nothing. Works on a copy of the staged set, so a refusal or a failed disk write
    /// leaves everything staged. A refusal throws <see cref="BaseEditsRefusedException"/> BEFORE anything is
    /// written, carrying the blocking findings.
    /// </summary>
    private BaseEditApplyResult? ApplyStagedBaseEdits(WorldSaveData workingData)
    {
        if (_baseEdits.IsEmpty) return null;
        var copy = _baseEdits.Clone();
        var result = copy.ApplyTo(workingData);
        if (!result.Applied)
        {
            var blocking = result.Issues.Where(i => i.IsBlocking).ToList();
            LastBaseEditRefusal = blocking;
            PlacedTransformsRevision++;
            throw new BaseEditsRefusedException(blocking);
        }
        LastBaseEditRefusal = [];
        return result;
    }

    /// <summary>After a successful write: clears the staged edits and refreshes what was cached from the old tree.</summary>
    private void CommitBaseEdits(BaseEditApplyResult? result, WorldSaveData workingData)
    {
        var otherSavesUsed = _baseEdits.OtherSaves.Count > 0;
        ClearBaseEdits();
        _placedObjects = null;
        _placedByKey = null;
        if (result is null) return;
        if (result.Deleted.Count > 0 || result.Created.Count > 0)
        {
            // Objects came or went, so the derived lists are rebuilt from the tree that was just
            // written (every other staged edit is already in it); no bytes are re-read.
            var fresh = WorldSaveReader.ReadFromSave(workingData.Raw);
            _deployables = fresh.Deployables.ToDictionary(d => d.Id, StringComparer.Ordinal);
            _containers = fresh.Containers.ToDictionary(ContainerKey, StringComparer.Ordinal);
        }
        foreach (var key in result.Transformed)
        {
            if (WorldMapAccessor.FindEntry(workingData.Raw, "DeployedObjectMap", key) is not { } props
                || PlacedObjectCensus.ReadTransform(props) is not { Translation: { } t }) continue;
            if (_deployables.TryGetValue(key, out var d)) _deployables[key] = d with { X = t.X, Y = t.Y, Z = t.Z };
            if (_originalDeployables.TryGetValue(key, out var o)) _originalDeployables[key] = o with { X = t.X, Y = t.Y, Z = t.Z };
        }
        if (otherSavesUsed) ReleaseOtherSaves();
    }
}

/// <summary>What a group operation staged, and which objects it left alone and why.</summary>
/// <param name="Staged">Objects the operation now has staged.</param>
/// <param name="Refused">Objects the session refused (level-placed, unknown, no saved location).</param>
/// <param name="Skipped">Objects the operation itself skipped, with its reason.</param>
public sealed record PlacedGroupStageResult(
    int Staged,
    IReadOnlyList<(string Key, PlacedTransformRefusal Reason)> Refused,
    IReadOnlyList<(string Key, string Reason)> Skipped)
{
    /// <summary>Total objects not touched.</summary>
    public int NotStaged => Refused.Count + Skipped.Count;
}

/// <summary>
/// Saving was refused because staged base edits have blocking findings. Nothing was written and the
/// edits are still staged. The message lists the findings in plain language for the save toast.
/// </summary>
public sealed class BaseEditsRefusedException : InvalidOperationException
{
    public BaseEditsRefusedException(IReadOnlyList<BaseEditIssue> issues)
        : base(BuildMessage(issues))
    {
        Issues = issues;
    }

    /// <summary>The blocking findings.</summary>
    public IReadOnlyList<BaseEditIssue> Issues { get; }

    private static string BuildMessage(IReadOnlyList<BaseEditIssue> issues)
    {
        var lines = issues.Select(i => i.Message).Distinct(StringComparer.Ordinal).Take(3).ToList();
        var more = issues.Count > lines.Count ? $" (and {issues.Count - lines.Count} more)" : "";
        return "Nothing was saved because the staged base edits cannot be applied: " + string.Join(" ", lines) + more
            + " Fix or revert them in the 3D view.";
    }
}
