using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Moving a base (GitHub issue 69): lifting player-built pieces out of this world into a
/// <see cref="BasePackage"/>, putting a package down here, and nudging the pieces of a staged copy
/// around before SAVE. A move to another world on this computer stages the copy in that world's
/// session and the removal in this one; the workspace links the two so one SAVE writes both, the
/// world receiving the pieces first.
/// </summary>
public sealed partial class WorldSaveSession
{
    /// <summary>Another world on this computer that has this same area (so the pieces can land on the same spot).</summary>
    public sealed record OtherWorldArea(string World, string FilePath);

    /// <summary>A move to another world staged from this session, so it can be shown and undone.</summary>
    public sealed record StagedBaseMove(WorldSaveSession Target, int TargetCopyId, IReadOnlyList<string> Keys, int Pieces, int Items);

    private readonly List<StagedBaseMove> _baseMoves = [];

    /// <summary>Moves to other worlds staged from here (cleared by a save or a revert of the removals).</summary>
    public IReadOnlyList<StagedBaseMove> StagedBaseMoves
    {
        get
        {
            // A move whose removal was reverted here is no longer a move: the other world does not
            // get a second copy either. (After a save the other world has already written its copy.)
            foreach (var gone in _baseMoves.Where(m => !m.Keys.Any(k => _baseEdits.Deletions.ContainsKey(k))).ToList())
            {
                gone.Target.RevertPlacedDuplication(gone.TargetCopyId);
                _baseMoves.Remove(gone);
            }
            return _baseMoves;
        }
    }

    /// <summary>Drops the other world's half of every staged move (on a revert here, or after both were written).</summary>
    private void ReleaseBaseMoves()
    {
        foreach (var move in _baseMoves) move.Target.RevertPlacedDuplication(move.TargetCopyId);
        _baseMoves.Clear();
    }

    /// <summary>The loaded save, for read-only views (pictures, place lists). Never edited through this.</summary>
    public WorldSaveData ReadOnlyData => _data;

    /// <summary>The name of the world this save belongs to (its folder), for labels.</summary>
    public string WorldName
    {
        get
        {
            var folder = System.IO.Path.GetDirectoryName(_path);
            var name = string.IsNullOrEmpty(folder) ? null : System.IO.Path.GetFileName(folder);
            return string.IsNullOrEmpty(name) ? FileNameOf(_path) : name;
        }
    }

    /// <summary>True when other worlds can be reached from here (a folder on this computer, not a browser copy).</summary>
    public bool CanReachOtherWorlds => _files is null || _files.HasLocalPaths;

    /// <summary>
    /// The player's other worlds that have this same area save (read from the folder next to this
    /// world's). Empty when the save is not in a folder on this computer.
    /// </summary>
    public IReadOnlyList<OtherWorldArea> OtherWorldsWithThisArea()
    {
        var found = new List<OtherWorldArea>();
        if (!CanReachOtherWorlds) return found;
        try
        {
            if (System.IO.Path.GetDirectoryName(_path) is not { Length: > 0 } dir
                || System.IO.Path.GetDirectoryName(dir) is not { Length: > 0 } worlds || !Directory.Exists(worlds)) return found;
            var fileName = System.IO.Path.GetFileName(_path);
            foreach (var other in Directory.EnumerateDirectories(worlds).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(System.IO.Path.GetFullPath(other), System.IO.Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase)) continue;
                var file = System.IO.Path.Combine(other, fileName);
                if (File.Exists(file)) found.Add(new OtherWorldArea(System.IO.Path.GetFileName(other), file));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AbioticEditor.Core.Diagnostics.EditorLog.Warn("BaseMove", $"Could not list the other worlds: {ex.Message}");
        }
        return found;
    }

    /// <summary>One area save a base can be moved to or brought from: this world's other areas, or any area of another world.</summary>
    /// <param name="World">The world's folder name.</param>
    /// <param name="Area">The area's name (from its save file: "Facility", "V Alps").</param>
    /// <param name="FilePath">The area's save file.</param>
    /// <param name="IsThisWorld">True for another area of the world this save belongs to.</param>
    public sealed record AreaChoice(string World, string Area, string FilePath, bool IsThisWorld);

    /// <summary>
    /// Every area a base can go to or come from, other than this save: the other areas of this world and
    /// every area of the player's other worlds on this computer. Sectors that are part of another area's
    /// map are left out (a save named <c>Facility_Dam</c> is a sector of <c>Facility</c>: what is built
    /// there is kept in the Facility save), as is the story save. Empty when the folder is out of reach.
    /// </summary>
    public IReadOnlyList<AreaChoice> OtherAreas()
    {
        if (!CanReachOtherWorlds || System.IO.Path.GetDirectoryName(_path) is not { Length: > 0 } dir) return [];
        return WorldAreas.List(dir, _path, message => AbioticEditor.Core.Diagnostics.EditorLog.Warn("BaseMove", message))
            .Select(a => new AreaChoice(a.World, a.Area, a.FilePath, a.IsThisWorld))
            .ToList();
    }

    /// <summary>The bases in this save (pieces a player built only), for "bring a base here" from it.</summary>
    public IReadOnlyList<(WorldBase Base, IReadOnlyList<string> Keys)> MovableBases()
        => BaseDetector.Detect(Deployables)
            .Where(b => b.BenchCount > 0)
            .Select(b => (b, MovableKeysOf(b)))
            .Where(x => x.Item2.Count > 0)
            .ToList();

    /// <summary>
    /// The pieces of <paramref name="worldBase"/> a move takes: built by a player, not already being
    /// removed, and on the base's main floor (see <see cref="BaseFloors"/>). Pieces on a floor far above
    /// or below stay where they are.
    /// </summary>
    public IReadOnlyList<string> MovableKeysOf(WorldBase worldBase)
    {
        ArgumentNullException.ThrowIfNull(worldBase);
        var main = BaseFloors.MainFloorIds(worldBase).ToHashSet(StringComparer.Ordinal);
        return worldBase.Deployables.Select(d => d.Id)
            .Where(k => main.Contains(k) && FindPlacedObject(k) is { DeployedByPlayer: true } && !IsPlacedDeletionStaged(k))
            .ToList();
    }

    /// <summary>How many player-built pieces of <paramref name="worldBase"/> stand on another floor and stay put.</summary>
    public int PiecesOnOtherFloors(WorldBase worldBase)
    {
        ArgumentNullException.ThrowIfNull(worldBase);
        var main = BaseFloors.MainFloorIds(worldBase).ToHashSet(StringComparer.Ordinal);
        return worldBase.Deployables.Count(d => !main.Contains(d.Id) && FindPlacedObject(d.Id) is { DeployedByPlayer: true });
    }

    /// <summary>
    /// Lifts the chosen pieces into a package (reads only). Pieces moved in the editor but not saved
    /// travel from where they now stand. Level pieces and unknown keys are left out (see <paramref name="skipped"/>).
    /// </summary>
    public BasePackage CreateBasePackage(IEnumerable<string> keys, out IReadOnlyList<BasePackageSkip> skipped)
        => BasePackage.Create(_data, keys, WorldName, FileNameOf(_path), out skipped, CurrentPlacedTransform);

    /// <summary>True when the package comes from another map than this save (its old spot means nothing here).</summary>
    public bool IsOtherMap(BasePackage package) => package.IsOtherMap(_data);

    /// <summary>The places the package can be put down in this save (same spot, named sectors, beside a base).</summary>
    public IReadOnlyList<BaseDestination> BaseDestinationsFor(BasePackage package) => BaseDestinations.For(package, _data);

    /// <summary>What putting the package down here would do, with everything already staged. Stages nothing.</summary>
    public BasePreviewResult PreviewBaseTransplant(BasePackage package, PlacedVector offset, double yawDegrees)
    {
        var id = -1;
        var preview = PreviewHypothetical(edits => id = edits.StageTransplant(package, offset, yawDegrees).Id);
        return new BasePreviewResult(preview, id);
    }

    /// <summary>Stages putting the package down here (offset from where the pieces stood, turned about their middle).</summary>
    public StagedDuplication StageBaseTransplant(BasePackage package, PlacedVector offset, double yawDegrees)
    {
        var staged = _baseEdits.StageTransplant(package, offset, yawDegrees);
        PlacedTransformsRevision++;
        UpdateStatus();
        return staged;
    }

    /// <summary>The staged copy that makes <paramref name="newKey"/>, or null when it is not a staged copy.</summary>
    public StagedDuplication? StagedCopyOf(string newKey)
        => _baseEdits.Duplications.FirstOrDefault(d => d.NewKeys.Values.Contains(newKey, StringComparer.Ordinal));

    /// <summary>The one staged copy every key belongs to, or null when they are not all copies of the same one.</summary>
    public StagedDuplication? StagedCopyOfAll(IReadOnlyCollection<string> keys)
    {
        if (keys.Count == 0) return null;
        var first = StagedCopyOf(keys.First());
        return first is not null && keys.All(k => first.NewKeys.Values.Contains(k, StringComparer.Ordinal)) ? first : null;
    }

    /// <summary>Sets where a staged copy lands (offset and turn), keeping its new ids. False when it is not staged.</summary>
    public bool RestageCopy(int id, PlacedVector offset, double yawDegrees)
    {
        if (_baseEdits.RestageDuplication(id, offset, yawDegrees) is null) return false;
        PlacedTransformsRevision++;
        UpdateStatus();
        return true;
    }

    /// <summary>Stands single pieces of a staged copy at the given heights (by source key). False when it is not staged.</summary>
    public bool SetStagedCopyHeights(int id, IReadOnlyDictionary<string, double> heights)
    {
        if (_baseEdits.SetPieceHeights(id, heights) is null) return false;
        PlacedTransformsRevision++;
        UpdateStatus();
        return true;
    }

    /// <summary>Shifts every piece of a staged copy by a step (cm).</summary>
    public bool MoveStagedCopy(int id, double dx, double dy, double dz)
        => _baseEdits.Duplications.FirstOrDefault(d => d.Id == id) is { } d
           && RestageCopy(id, new PlacedVector(d.Offset.X + dx, d.Offset.Y + dy, d.Offset.Z + dz), d.YawDegrees);

    /// <summary>
    /// Turns a staged copy about its own pivot (the middle of the group, or the piece itself for a
    /// single placed piece). The pivot does not move when the group turns, so the offset stays.
    /// </summary>
    public bool TurnStagedCopy(int id, double degrees)
        => _baseEdits.Duplications.FirstOrDefault(d => d.Id == id) is { } d
           && RestageCopy(id, d.Offset, Wrap(d.YawDegrees + degrees));

    private static double Wrap(double degrees)
    {
        var d = degrees % 360;
        if (d > 180) d -= 360;
        if (d <= -180) d += 360;
        return d;
    }

    /// <summary>What removing the pieces after a move would do. Stages nothing.</summary>
    public BaseEditPreview PreviewMoveAway(IReadOnlyList<string> keys, bool leaveDanglingLinks)
        => PreviewHypothetical(edits => edits.StageDelete(keys, MoveAwayPolicy(leaveDanglingLinks)));

    private static DeletePolicy MoveAwayPolicy(bool leaveDanglingLinks)
        => leaveDanglingLinks
            ? DeletePolicy.MovedAway with { OtherReferences = ReferencePolicy.Keep, CrossSaveLinks = ReferencePolicy.Keep }
            : DeletePolicy.MovedAway;

    /// <summary>
    /// Throws <see cref="BaseEditsRefusedException"/> when the staged base edits could not be applied, so
    /// a linked save (a base move) can be checked before ANY of the linked worlds is written.
    /// </summary>
    public void EnsureBaseEditsCanApply()
    {
        if (_baseEdits.IsEmpty) return;
        var preview = _baseEdits.Preview(_data);
        if (preview.CanApply) return;
        var blocking = preview.Issues.Where(i => i.IsBlocking).ToList();
        LastBaseEditRefusal = blocking;
        PlacedTransformsRevision++;
        throw new BaseEditsRefusedException(blocking);
    }

    /// <summary>Stages removing pieces that were exported or moved, with the move policy.</summary>
    public PlacedGroupStageResult StageMoveAway(IReadOnlyList<string> keys, bool leaveDanglingLinks)
        => StagePlacedDelete(keys, MoveAwayPolicy(leaveDanglingLinks));

    /// <summary>
    /// Stages a whole move to <paramref name="target"/> (another world's session): the package is put
    /// down there and, when <paramref name="removeHere"/>, the pieces are removed here. Nothing is written
    /// until SAVE, which the caller links so it writes both. Returns the copy staged in the target.
    /// </summary>
    public StagedDuplication StageMoveTo(
        WorldSaveSession target, BasePackage package, PlacedVector offset, double yawDegrees, bool removeHere, bool leaveDanglingLinks)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(package);
        var copy = target.StageBaseTransplant(package, offset, yawDegrees);
        if (removeHere)
        {
            var keys = package.Pieces.Select(p => p.Key).ToList();
            StageMoveAway(keys, leaveDanglingLinks);
            _baseMoves.Add(new StagedBaseMove(target, copy.Id, keys, package.Pieces.Count, package.StoredItems));
        }
        return copy;
    }

    /// <summary>Undoes a staged move: the pieces stay here, and the other world does not get them.</summary>
    public void UndoMove(StagedBaseMove move)
    {
        ArgumentNullException.ThrowIfNull(move);
        move.Target.RevertPlacedDuplication(move.TargetCopyId);
        foreach (var key in move.Keys) _baseEdits.RevertDeletion(key);
        _baseMoves.Remove(move);
        PlacedTransformsRevision++;
        UpdateStatus();
    }
}

/// <summary>A hypothetical preview and the id the previewed copy had in it.</summary>
public sealed record BasePreviewResult(BaseEditPreview Preview, int CopyId)
{
    /// <summary>The rows of the previewed copy only.</summary>
    public IReadOnlyList<DuplicationPreviewRow> Rows => Preview.Duplications.Where(r => r.DuplicationId == CopyId).ToList();
}
