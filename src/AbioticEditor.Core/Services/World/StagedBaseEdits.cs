using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>The combined before/after view of every staged base edit. Reads only.</summary>
/// <param name="Transforms">Per-object move/rotate rows (see <see cref="StagedPlacedTransforms.Preview"/>).</param>
/// <param name="Deletions">Per-object deletion rows: contents, references and what would happen to each.</param>
/// <param name="Duplications">Per-copy rows: new identity, new place, outlet remaps, contents.</param>
/// <param name="ProximityHints">Objects that would end up close together. HINTS ONLY, not collision.</param>
/// <param name="Issues">Every finding across all edits, blocking ones first.</param>
/// <param name="ObjectsBefore">Placed-object entries in the save now.</param>
/// <param name="ObjectsAfter">Placed-object entries after applying (when nothing blocks).</param>
/// <param name="ItemsDeleted">Stored items deleted with their objects.</param>
/// <param name="ItemsCopied">Stored items duplicated by copies made with <see cref="ContentsMode.Copy"/>.</param>
/// <param name="ReferencesAffected">Links (outlets, plugs, other references) touched by deletions and copies.</param>
public sealed record BaseEditPreview(
    IReadOnlyList<TransformPreviewRow> Transforms,
    IReadOnlyList<DeletionPreviewRow> Deletions,
    IReadOnlyList<DuplicationPreviewRow> Duplications,
    IReadOnlyList<ProximityHint> ProximityHints,
    IReadOnlyList<BaseEditIssue> Issues,
    int ObjectsBefore,
    int ObjectsAfter,
    int ItemsDeleted,
    int ItemsCopied,
    int ReferencesAffected)
{
    /// <summary>True when nothing blocks applying every staged edit.</summary>
    public bool CanApply => !Issues.Any(i => i.IsBlocking);

    /// <summary>Staged power plug changes, in staging order.</summary>
    public IReadOnlyList<PowerLinkPreviewRow> PowerLinks { get; init; } = [];

    /// <summary>Leftover outlet records staged for removal.</summary>
    public IReadOnlyList<string> SocketCleanups { get; init; } = [];
}

/// <summary>One copy made by an apply.</summary>
public sealed record CreatedCopy(string SourceKey, string NewKey);

/// <summary>What <see cref="StagedBaseEdits.ApplyTo"/> did.</summary>
/// <param name="Applied">True when the edits were written into the in-memory save (not to disk).</param>
/// <param name="Issues">The findings; when <paramref name="Applied"/> is false, at least one is blocking.</param>
/// <param name="Transformed">Keys moved or rotated.</param>
/// <param name="Deleted">Keys deleted.</param>
/// <param name="Created">Source key and new key of each copy made.</param>
/// <param name="SocketRecordsRemoved">Outlet records removed by deletions.</param>
/// <param name="SocketRecordsCreated">Outlet records created by copies.</param>
/// <param name="ObjectsBefore">Entries before.</param>
/// <param name="ObjectsAfter">Entries after (equal to before when refused).</param>
public sealed record BaseEditApplyResult(
    bool Applied,
    IReadOnlyList<BaseEditIssue> Issues,
    IReadOnlyList<string> Transformed,
    IReadOnlyList<string> Deleted,
    IReadOnlyList<CreatedCopy> Created,
    int SocketRecordsRemoved,
    int SocketRecordsCreated,
    int ObjectsBefore,
    int ObjectsAfter)
{
    /// <summary>Socket records whose plugged device changed (plugs, unplugs and feeds moved).</summary>
    public int PowerLinksChanged { get; init; }
}

/// <summary>
/// One staged model for base editing: moves and rotations (through <see cref="StagedPlacedTransforms"/>),
/// deletions and duplications of placed objects. Staging never touches a save. <see cref="Preview"/> shows
/// exactly what would change; <see cref="ApplyTo"/> validates everything first and then applies it in one
/// pass, all or nothing: any blocking finding leaves the save untouched. Every edit can be reverted before
/// applying, individually or all at once.
/// </summary>
/// <remarks>
/// Apply order: moves and rotations, then duplications (a copy is made from an object's staged transform, so
/// "move, then duplicate" copies from the moved place), then deletions. The caller writes the result with
/// <see cref="WorldSaveWriter.WriteToFile"/>. Nothing here is verified in a running game; see
/// docs/reference/research/base-building-group-operations.md.
/// </remarks>
public sealed class StagedBaseEdits
{
    private readonly Dictionary<string, DeletePolicy> _deletions = new(StringComparer.Ordinal);
    private readonly List<StagedDuplication> _duplications = [];
    private readonly List<StagedPowerLink> _powerLinks = [];
    private readonly List<string> _cleanups = [];
    private readonly Func<string> _idFactory;
    private int _nextId = 1;

    /// <summary>Creates the model. <paramref name="idFactory"/> mints 32-hex ids (default: random GUIDs); tests inject a counter.</summary>
    public StagedBaseEdits(Func<string>? idFactory = null)
    {
        _idFactory = idFactory ?? (() => Guid.NewGuid().ToString("N").ToUpperInvariant());
    }

    /// <summary>The staged moves and rotations (also usable directly).</summary>
    public StagedPlacedTransforms Transforms { get; } = new();

    /// <summary>Pending deletions with their policies.</summary>
    public IReadOnlyDictionary<string, DeletePolicy> Deletions => _deletions;

    /// <summary>Pending duplications in staging order.</summary>
    public IReadOnlyList<StagedDuplication> Duplications => _duplications;

    /// <summary>
    /// A copy of everything staged (moves, deletions, duplications with the identities already minted for
    /// them, and the settings). Lets a caller apply to the copy, and keep the original staged when the
    /// apply is refused or the write that follows fails. Nothing is shared between the two.
    /// </summary>
    public StagedBaseEdits Clone()
    {
        var copy = new StagedBaseEdits(_idFactory)
        {
            OverlapHintCm = OverlapHintCm,
            PrimaryName = PrimaryName,
            OtherSaves = OtherSaves,
            _nextId = _nextId,
        };
        foreach (var t in Transforms.Pending) copy.Transforms.Stage(t.Key, t.Translation, t.Rotation);
        foreach (var kv in _deletions) copy._deletions[kv.Key] = kv.Value;
        copy._duplications.AddRange(_duplications);
        copy._powerLinks.AddRange(_powerLinks);
        copy._cleanups.AddRange(_cleanups);
        return copy;
    }

    /// <summary>Distance under which two objects are reported as a proximity hint (centimetres).</summary>
    public double OverlapHintCm { get; set; } = 25;

    /// <summary>A label for the edited save, used in reference reports.</summary>
    public string PrimaryName { get; set; } = "primary";

    /// <summary>Other saves to scan for references into deleted objects (read only; never written).</summary>
    public IReadOnlyList<(string Name, WorldSaveData Data)> OtherSaves { get; set; } = [];

    /// <summary>True when nothing is staged.</summary>
    public bool IsEmpty => Transforms.IsEmpty && _deletions.Count == 0 && _duplications.Count == 0 && _powerLinks.Count == 0 && _cleanups.Count == 0;

    /// <summary>Pending power plug changes in staging order.</summary>
    public IReadOnlyList<StagedPowerLink> PowerLinks => _powerLinks;

    /// <summary>Leftover outlet records pending removal.</summary>
    public IReadOnlyList<string> SocketCleanups => _cleanups;

    // ---------- staging: transforms ----------

    /// <summary>Stages a group move by a delta (cm).</summary>
    public GroupTransformResult MoveBy(WorldSaveData data, IEnumerable<string> keys, double dx, double dy, double dz)
        => PlacedGroupTransforms.MoveBy(Transforms, data, keys, dx, dy, dz);

    /// <summary>Stages a group yaw rotation around a pivot.</summary>
    public GroupTransformResult RotateYaw(WorldSaveData data, IEnumerable<string> keys, double degrees, GroupPivot? pivot = null)
        => PlacedGroupTransforms.RotateYaw(Transforms, data, keys, degrees, pivot ?? GroupPivot.Centroid);

    /// <summary>Stages grid snapping (position and optionally yaw).</summary>
    public GroupTransformResult SnapToGrid(
        WorldSaveData data, IEnumerable<string> keys, double stepXY, double? stepZ = null, double? yawStepDegrees = null)
        => PlacedGroupTransforms.SnapToGrid(Transforms, data, keys, stepXY, stepZ, yawStepDegrees);

    /// <summary>Stages aligning every object's yaw to a reference object.</summary>
    public GroupTransformResult AlignYaw(WorldSaveData data, IEnumerable<string> keys, string referenceKey)
        => PlacedGroupTransforms.AlignYaw(Transforms, data, keys, referenceKey);

    /// <summary>Stages an even spread along an axis.</summary>
    public GroupTransformResult Distribute(WorldSaveData data, IEnumerable<string> keys, PlacementAxis axis)
        => PlacedGroupTransforms.Distribute(Transforms, data, keys, axis);

    // ---------- staging: deletions and duplications ----------

    /// <summary>Stages deletion of objects with one policy (default: the safe <see cref="DeletePolicy.Default"/>).</summary>
    public void StageDelete(IEnumerable<string> keys, DeletePolicy? policy = null)
    {
        foreach (var key in keys)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            _deletions[key] = policy ?? DeletePolicy.Default;
        }
    }

    /// <summary>
    /// Stages a duplication: every source object is copied, the group is turned by <paramref name="yawDegrees"/>
    /// about <paramref name="pivot"/> (default: centroid) and then shifted by <paramref name="offset"/>. New keys
    /// are minted now, so the preview and the apply agree.
    /// </summary>
    public StagedDuplication StageDuplicate(
        IEnumerable<string> keys, PlacedVector offset, double yawDegrees = 0, GroupPivot? pivot = null,
        DuplicatePolicy? policy = null)
    {
        var sources = keys.Distinct(StringComparer.Ordinal).ToList();
        var minted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in sources) minted[s] = _idFactory();
        var dup = new StagedDuplication(
            _nextId++, sources, offset, yawDegrees, pivot ?? GroupPivot.Centroid, policy ?? DuplicatePolicy.Default, minted);
        _duplications.Add(dup);
        return dup;
    }

    /// <summary>
    /// Stages placing a copy of <paramref name="sourceKey"/>, a player-built object of another world's save
    /// (<paramref name="donor"/>), in this save: shifted by <paramref name="offset"/> and turned by
    /// <paramref name="yawDegrees"/> about itself. It starts empty, unplugged and untagged, because nothing it
    /// was linked to exists in this world.
    /// </summary>
    public StagedDuplication StageImport(
        WorldSaveData donor, string donorName, string sourceKey, PlacedVector offset, double yawDegrees = 0)
    {
        ArgumentNullException.ThrowIfNull(donor);
        ArgumentException.ThrowIfNullOrEmpty(sourceKey);
        var dup = new StagedDuplication(
            _nextId++, [sourceKey], offset, yawDegrees, GroupPivot.OfObject(sourceKey), DuplicatePolicy.Default,
            new Dictionary<string, string>(StringComparer.Ordinal) { [sourceKey] = _idFactory() }, donor, donorName);
        _duplications.Add(dup);
        return dup;
    }

    // ---------- staging: power ----------

    /// <summary>
    /// Stages plugging <paramref name="deviceKey"/> into <paramref name="socketId"/> (a wall socket's key, or an
    /// outlet: owner GUID plus outlet digit). The device's current feed is unplugged when this applies.
    /// </summary>
    public StagedPowerLink StagePlug(string socketId, string deviceKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(socketId);
        ArgumentException.ThrowIfNullOrEmpty(deviceKey);
        var link = new StagedPowerLink(_nextId++, socketId, deviceKey);
        _powerLinks.Add(link);
        return link;
    }

    /// <summary>Stages unplugging whatever <paramref name="socketId"/> powers.</summary>
    public StagedPowerLink StageUnplug(string socketId)
    {
        ArgumentException.ThrowIfNullOrEmpty(socketId);
        var link = new StagedPowerLink(_nextId++, socketId, null);
        _powerLinks.Add(link);
        return link;
    }

    /// <summary>Stages removing a leftover (unplugged) outlet record.</summary>
    public void StageSocketCleanup(string socketId)
    {
        ArgumentException.ThrowIfNullOrEmpty(socketId);
        if (!_cleanups.Contains(socketId, StringComparer.Ordinal)) _cleanups.Add(socketId);
    }

    /// <summary>Stages the chosen repairs from <see cref="PowerRepair.Find"/>.</summary>
    public void StageRepairs(IEnumerable<PowerRepairFix> fixes)
    {
        foreach (var fix in fixes)
        {
            if (fix.Kind == PowerRepairKind.RemoveLeftoverRecord) StageSocketCleanup(fix.SocketId);
            else if (!_powerLinks.Any(l => l.SocketId == fix.SocketId && l.DeviceKey is null)) StageUnplug(fix.SocketId);
        }
    }

    /// <summary>Reverts one staged power change.</summary>
    public bool RevertPowerLink(int id) => _powerLinks.RemoveAll(l => l.Id == id) > 0;

    /// <summary>Reverts one staged leftover-record removal.</summary>
    public bool RevertSocketCleanup(string socketId) => _cleanups.Remove(socketId);

    // ---------- revert ----------

    /// <summary>Reverts the staged move/rotation of one object.</summary>
    public bool RevertTransform(string key) => Transforms.Revert(key);

    /// <summary>Reverts the staged deletion of one object.</summary>
    public bool RevertDeletion(string key) => _deletions.Remove(key);

    /// <summary>Reverts one staged duplication (all its copies).</summary>
    public bool RevertDuplication(int id) => _duplications.RemoveAll(d => d.Id == id) > 0;

    /// <summary>Reverts everything staged.</summary>
    public void RevertAll()
    {
        Transforms.RevertAll();
        _deletions.Clear();
        _duplications.Clear();
        _powerLinks.Clear();
        _cleanups.Clear();
    }

    // ---------- preview / validate ----------

    /// <summary>Validates and previews every staged edit against <paramref name="data"/>. Reads only.</summary>
    public BaseEditPreview Preview(WorldSaveData data) => BuildPlans(data).Preview;

    /// <summary>The findings of <see cref="Preview"/>; blocking ones mean <see cref="ApplyTo"/> would refuse.</summary>
    public IReadOnlyList<BaseEditIssue> Validate(WorldSaveData data) => Preview(data).Issues;

    private sealed record Plans(
        BaseEditPreview Preview,
        DeletionPlan? Deletion,
        DuplicationPlan? Duplication,
        IReadOnlyList<TransformPreviewRow> ApplicableTransforms,
        PowerLinkPlan? Power);

    private Plans BuildPlans(WorldSaveData data)
    {
        var issues = new List<BaseEditIssue>();
        var objectsBefore = WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap").Count();

        // Transforms.
        var transformRows = Transforms.Preview(data);
        var applicable = new List<TransformPreviewRow>();
        foreach (var row in transformRows)
        {
            if (_deletions.ContainsKey(row.Key))
            {
                issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "transform-ignored",
                    "This object is also staged for deletion, so its move/rotation is ignored.", row.Key));
                continue;
            }
            if (row.Blocked)
            {
                issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "transform-blocked", row.Warnings[0], row.Key));
                continue;
            }
            applicable.Add(row);
        }
        if (transformRows.Count > 0 || _duplications.Count > 0)
        {
            issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "coordinates-unchecked",
                "Saved coordinates and rotations are written as they are and the game loads them there; it does not check that a piece fits, so look for pieces inside walls or floating."));
        }

        // Deletions.
        DeletionPlan? deletion = null;
        if (_deletions.Count > 0)
        {
            deletion = PlacedObjectDeletion.Plan(
                data, _deletions.Select(kv => (kv.Key, kv.Value)).ToList(), PrimaryName, OtherSaves);
            issues.AddRange(deletion.Issues);
        }

        // Duplications.
        DuplicationPlan? duplication = null;
        if (_duplications.Count > 0)
        {
            duplication = PlacedObjectDuplication.Plan(data, _duplications, key => Transforms.Current(data, key));
            issues.AddRange(duplication.Issues);

            foreach (var row in duplication.Rows)
            {
                foreach (var link in row.Sockets.Where(s =>
                    (s.PluggedAfter is { } p && _deletions.ContainsKey(p)) || s.ExtrasAfter.Any(_deletions.ContainsKey)))
                {
                    issues.Add(new BaseEditIssue(BaseEditSeverity.Blocking, "kept-link-to-deleted",
                        $"A copy's outlet still points at a device that is staged for deletion ({link.NewId[^3..]}).", row.SourceKey));
                }
            }
        }

        // Power plug changes and leftover-record removals, planned against the positions after any
        // staged moves (cable length hints) and refusing anything that touches a deleted object.
        PowerLinkPlan? power = null;
        if (_powerLinks.Count > 0 || _cleanups.Count > 0)
        {
            var deleting = new HashSet<string>(_deletions.Keys, StringComparer.Ordinal);
            power = PowerLinkEdits.Plan(data, _powerLinks, _cleanups, deleting, key => Transforms.Current(data, key)?.Translation, OtherSaves,
                duplication?.Rows);
            issues.AddRange(power.Issues);
        }

        // Proximity hints (hints only).
        var hints = ComputeHints(data, duplication);
        foreach (var h in hints.Take(10))
        {
            issues.Add(new BaseEditIssue(BaseEditSeverity.Warning, "proximity-hint",
                $"Hint only: {Label(h.ClassA)} and {Label(h.ClassB)} would sit {h.DistanceCm:F0} cm apart (centre to centre). This is not a collision check.",
                h.KeyA));
        }
        if (hints.Count > 10)
        {
            issues.Add(new BaseEditIssue(BaseEditSeverity.Info, "proximity-hints-more", $"{hints.Count - 10} more proximity hints not listed as findings."));
        }

        var objectsAfter = objectsBefore + (duplication?.RowPlans.Count ?? 0) - (deletion?.DeleteKeys.Count ?? 0);
        var itemsDeleted = deletion?.Rows.Sum(r => r.Contents.Sum(c => Math.Max(c.Count, 1))) ?? 0;
        var itemsCopied = duplication?.Rows.Where(r => r.Contents == ContentsMode.Copy).Sum(r => r.SourceContents.Sum(c => Math.Max(c.Count, 1))) ?? 0;
        var refs = (deletion?.Rows.Sum(r => r.OwnedSocketIds.Count + r.InboundLinks.Count) ?? 0)
            + (duplication?.Rows.Sum(r => r.Sockets.Count) ?? 0)
            + (power is null ? 0 : power.Rows.Count + power.Cleanups.Count);

        var ordered = issues.OrderByDescending(i => i.Severity).ToList();
        var preview = new BaseEditPreview(
            transformRows, deletion?.Rows ?? [], duplication?.Rows ?? [], hints, ordered,
            objectsBefore, objectsAfter, itemsDeleted, itemsCopied, refs)
        {
            PowerLinks = power?.Rows ?? [],
            SocketCleanups = power?.Cleanups ?? [],
        };
        return new Plans(preview, deletion, duplication, applicable, power);
    }

    private static string Label(string? cls) => cls ?? "an object";

    private List<ProximityHint> ComputeHints(WorldSaveData data, DuplicationPlan? duplication)
    {
        if (Transforms.IsEmpty && (duplication is null || duplication.Rows.Count == 0)) return [];
        var moved = Transforms.Pending.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var points = ProximityHints.CollectPoints(data, false, key => moved.Contains(key) ? Transforms.Current(data, key)?.Translation : null)
            .Where(p => !_deletions.ContainsKey(p.Key)).ToList();
        var focus = new HashSet<string>(moved, StringComparer.Ordinal);
        if (duplication is not null)
        {
            foreach (var row in duplication.Rows.Where(r => r.After?.Translation is not null))
            {
                points.Add(new PlacedPoint(row.NewKey, row.ClassName, row.After!.Translation!.Value));
                focus.Add(row.NewKey);
            }
        }
        return ProximityHints.Find(points, OverlapHintCm, focus).ToList();
    }

    // ---------- apply ----------

    /// <summary>
    /// Validates everything, then applies moves, duplications and deletions to <paramref name="data"/>'s raw
    /// tree in one pass (not to disk; follow with <see cref="WorldSaveWriter.WriteToFile"/>). All or nothing:
    /// when any finding blocks, or a copy fails its own validation, the save is left untouched and the result
    /// lists why. On success everything staged is cleared.
    /// </summary>
    public BaseEditApplyResult ApplyTo(WorldSaveData data)
    {
        var plans = BuildPlans(data);
        var before = plans.Preview.ObjectsBefore;
        if (!plans.Preview.CanApply)
        {
            return Refused(plans.Preview.Issues, before);
        }

        // Build phase: detached copies, validated. Nothing in `data` changes here.
        var built = new List<BuiltDuplicate>();
        if (plans.Duplication is { RowPlans.Count: > 0 } dup)
        {
            var problems = new List<string>();
            try
            {
                foreach (var group in dup.RowPlans.GroupBy(r => r.Duplication.Id))
                {
                    var donor = PlacedObjectCloner.CreateDonor((group.First().Duplication.Donor ?? data).Raw);
                    foreach (var rowPlan in group)
                    {
                        var copy = PlacedObjectCloner.Build(donor, rowPlan, rowPlan.Duplication.Policy.Contents, _idFactory);
                        problems.AddRange(PlacedObjectCloner.Validate(data, rowPlan, copy));
                        built.Add(copy);
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidCastException or ArgumentException or IOException)
            {
                problems.Add(ex.Message);
            }
            if (problems.Count > 0)
            {
                var all = plans.Preview.Issues
                    .Concat(problems.Select(p => new BaseEditIssue(BaseEditSeverity.Blocking, "copy-invalid", p)))
                    .ToList();
                return Refused(all, before);
            }
        }

        // Commit phase.
        var transformed = new List<string>();
        foreach (var row in plans.ApplicableTransforms)
        {
            var staged = Transforms.Pending.First(p => p.Key == row.Key);
            if (WorldSaveWriter.ApplyPlacedObjectTransform(data, staged.Key, staged.Translation, staged.Rotation))
            {
                transformed.Add(staged.Key);
            }
        }

        var created = new List<CreatedCopy>();
        var socketsCreated = 0;
        foreach (var b in built)
        {
            WorldSaveWriter.AppendMapPair(data.Raw, "DeployedObjectMap", b.ObjectPair);
            foreach (var s in b.SocketPairs)
            {
                WorldSaveWriter.AppendMapPair(data.Raw, "PowerSocketMap", s);
                socketsCreated++;
            }
            created.Add(new CreatedCopy(b.SourceKey, b.NewKey));
        }

        var deleted = new List<string>();
        var socketsRemoved = 0;
        if (plans.Deletion is { } del)
        {
            socketsRemoved = del.SocketRecordsToRemove.Count;
            PlacedObjectDeletion.Commit(data, del);
            deleted.AddRange(del.DeleteKeys);
        }

        // Power last: deletions have already dropped the outlets of removed objects.
        var powerChanged = 0;
        if (plans.Power is { } powerPlan)
        {
            var (made, cleaned, changedLinks) = PowerLinkEdits.Commit(data, powerPlan);
            socketsCreated += made;
            socketsRemoved += cleaned;
            powerChanged = changedLinks + made;
        }

        var after = WorldMapAccessor.Entries(data.Raw, "DeployedObjectMap").Count();
        var result = new BaseEditApplyResult(
            true, plans.Preview.Issues, transformed, deleted, created, socketsRemoved, socketsCreated, before, after)
        {
            PowerLinksChanged = powerChanged,
        };
        RevertAll();
        return result;
    }

    private static BaseEditApplyResult Refused(IReadOnlyList<BaseEditIssue> issues, int count)
        => new(false, issues, [], [], [], 0, 0, count, count);
}
