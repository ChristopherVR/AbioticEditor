using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using AbioticEditor.Web.Models;
using static AbioticEditor.Tests.BaseEditTestSupport;

namespace AbioticEditor.Tests;

/// <summary>
/// Moving a base between two worlds through the editor sessions (GitHub issue 69): the other worlds
/// with the same area are found next to this one, a move stages the copy in the other world and the
/// removal here, undo drops both halves, and saving writes the pieces into the other world and
/// takes them out of this one. Skips when the dedicated-server Facility fixture is absent.
/// </summary>
public sealed class BaseMoveSessionTests
{
    private sealed class TwoWorlds : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "abf-base-move-" + Guid.NewGuid().ToString("N"));
        public string Here { get; }
        public string There { get; }

        public TwoWorlds()
        {
            Here = Path.Combine(Root, "Cascade", "WorldSave_Facility.sav");
            There = Path.Combine(Root, "NewWorld", "WorldSave_Facility.sav");
            Directory.CreateDirectory(Path.GetDirectoryName(Here)!);
            Directory.CreateDirectory(Path.GetDirectoryName(There)!);
            Directory.CreateDirectory(Path.Combine(Root, "NoFacility"));
            File.WriteAllBytes(Here, OriginalBytes);
            File.WriteAllBytes(There, OriginalBytes);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
        }
    }

    private static WorldSaveSession Open(string path) => new(WorldSaveReader.ReadFromFile(path), path);

    /// <summary>A handful of player-built pieces of the biggest base that nothing outside the group names.</summary>
    private static List<string> MovablePieces(WorldSaveSession session, WorldSaveData data)
    {
        var bases = BaseDetector.Detect(data.Deployables).OrderByDescending(b => b.Deployables.Count);
        foreach (var b in bases)
        {
            var keys = b.Deployables.Select(d => d.Id)
                .Where(k => session.FindPlacedObject(k) is { DeployedByPlayer: true, Transform.Translation: not null }).ToList();
            if (keys.Count >= 3 && session.PreviewMoveAway(keys, leaveDanglingLinks: false).CanApply) return keys;
        }
        // Fall back to single quiet pieces.
        return PlayerBuilt(data).Select(o => o.Key)
            .Where(k => session.PreviewMoveAway([k], false).CanApply).Take(5).ToList();
    }

    [Fact]
    public void Other_worlds_with_the_same_area_are_listed()
    {
        if (!HasServerFacility) return;
        using var worlds = new TwoWorlds();
        var session = Open(worlds.Here);

        var others = session.OtherWorldsWithThisArea();

        Assert.Equal("Cascade", session.WorldName);
        var only = Assert.Single(others);
        Assert.Equal("NewWorld", only.World);
        Assert.Equal(Path.GetFullPath(worlds.There), Path.GetFullPath(only.FilePath));
    }

    [Fact]
    public async Task A_move_writes_the_pieces_into_the_other_world_and_takes_them_out_of_this_one()
    {
        if (!HasServerFacility) return;
        using var worlds = new TwoWorlds();
        var here = Open(worlds.Here);
        var there = Open(worlds.There);
        var keys = MovablePieces(here, WorldSaveReader.ReadFromFile(worlds.Here));
        Assert.NotEmpty(keys);
        var package = here.CreateBasePackage(keys, out _);
        var thereBefore = there.PlacedObjects.Count;

        var copy = here.StageMoveTo(there, package, new PlacedVector(0, 0, 0), 0, removeHere: true, leaveDanglingLinks: false);

        Assert.True(here.IsDirty);
        Assert.True(there.IsDirty);
        Assert.Single(here.StagedBaseMoves);
        Assert.Equal(keys.Count, there.PreviewBaseEdits().Duplications.Count);
        Assert.Equal(keys.Count, here.PreviewBaseEdits().Deletions.Count);

        // The world that gets the pieces is written first (the workspace does the same).
        await there.SaveAsync();
        await here.SaveAsync();

        var hereAfter = WorldSaveReader.ReadFromFile(worlds.Here);
        var thereAfter = WorldSaveReader.ReadFromFile(worlds.There);
        Assert.All(keys, k => Assert.Null(WorldMapAccessor.FindEntry(hereAfter.Raw, "DeployedObjectMap", k)));
        Assert.All(copy.NewKeys.Values, k => Assert.NotNull(WorldMapAccessor.FindEntry(thereAfter.Raw, "DeployedObjectMap", k)));
        Assert.Equal(thereBefore + keys.Count, PlacedObjectCensus.Build(thereAfter).Objects!.Count);
        Assert.True(File.Exists(worlds.Here + ".bak") || Directory.EnumerateFiles(Path.GetDirectoryName(worlds.Here)!, "*.bak*").Any());
    }

    [Fact]
    public void Undoing_a_move_drops_both_halves()
    {
        if (!HasServerFacility) return;
        using var worlds = new TwoWorlds();
        var here = Open(worlds.Here);
        var there = Open(worlds.There);
        var keys = MovablePieces(here, WorldSaveReader.ReadFromFile(worlds.Here));
        here.StageMoveTo(there, here.CreateBasePackage(keys, out _), new PlacedVector(0, 0, 0), 0, true, false);

        here.UndoMove(here.StagedBaseMoves.Single());

        Assert.False(here.HasStagedBaseEdits);
        Assert.False(there.HasStagedBaseEdits);
        Assert.Empty(here.StagedBaseMoves);
    }

    [Fact]
    public void Reverting_this_world_also_drops_the_copy_waiting_in_the_other_world()
    {
        if (!HasServerFacility) return;
        using var worlds = new TwoWorlds();
        var here = Open(worlds.Here);
        var there = Open(worlds.There);
        var keys = MovablePieces(here, WorldSaveReader.ReadFromFile(worlds.Here));
        here.StageMoveTo(there, here.CreateBasePackage(keys, out _), new PlacedVector(0, 0, 0), 0, true, false);

        here.RevertAllBaseEdits();

        Assert.False(there.HasStagedBaseEdits);
    }

    [Fact]
    public void An_imported_base_follows_drags_and_turns_as_one_group()
    {
        if (!HasServerFacility) return;
        using var worlds = new TwoWorlds();
        var here = Open(worlds.Here);
        var keys = MovablePieces(here, WorldSaveReader.ReadFromFile(worlds.Here));
        var package = BasePackage.Parse(here.CreateBasePackage(keys, out _).ToJson());
        var there = Open(worlds.There);

        var copy = there.StageBaseTransplant(package, new PlacedVector(0, 0, 0), 0);
        var newKeys = copy.NewKeys.Values.ToList();
        Assert.Same(copy, there.StagedCopyOfAll(newKeys));
        Assert.Null(there.StagedCopyOfAll([.. newKeys, keys[0]]));

        Assert.True(there.MoveStagedCopy(copy.Id, 300, 0, 0));
        Assert.True(there.TurnStagedCopy(copy.Id, 90));
        var now = there.StagedPlacedDuplications.Single();
        Assert.Equal(300, now.Offset.X);
        Assert.Equal(90, now.YawDegrees);
        Assert.Equal(copy.NewKeys, now.NewKeys);
        Assert.True(there.PreviewBaseEdits().CanApply);
    }

    [Fact]
    public async Task Links_from_the_worlds_other_saves_are_found_before_anything_is_written()
    {
        if (!HasServerFacility) return;
        using var worlds = new TwoWorlds();
        // The story save and another area name pieces of the biggest base (a containment record, sockets).
        var folder = Path.GetDirectoryName(worlds.Here)!;
        foreach (var name in new[] { "WorldSave_MetaData.sav", "WorldSave_Facility_Dam_Waterfall.sav" })
        {
            var from = Path.Combine(Fixtures.ServerWorldsDir!, name);
            if (File.Exists(from)) File.Copy(from, Path.Combine(folder, name));
        }
        var here = Open(worlds.Here);
        await here.LoadOtherSavesAsync();
        var data = WorldSaveReader.ReadFromFile(worlds.Here);
        var keys = BaseDetector.Detect(data.Deployables).OrderByDescending(b => b.Deployables.Count).First()
            .Deployables.Select(d => d.Id).Where(k => here.FindPlacedObject(k) is { DeployedByPlayer: true }).ToList();

        var strict = here.PreviewMoveAway(keys, leaveDanglingLinks: false);
        Assert.Contains(strict.Issues, i => i.IsBlocking && i.Code is "external-reference" or "cross-save-link");
        Assert.True(here.PreviewMoveAway(keys, leaveDanglingLinks: true).CanApply);

        // Staged strictly, the check that runs before a linked save refuses, and nothing is written.
        var there = Open(worlds.There);
        here.StageMoveTo(there, here.CreateBasePackage(keys, out _), new PlacedVector(0, 0, 0), 0, true, leaveDanglingLinks: false);
        Assert.Throws<BaseEditsRefusedException>(here.EnsureBaseEditsCanApply);
        there.EnsureBaseEditsCanApply();
        Assert.Equal(OriginalBytes, File.ReadAllBytes(worlds.There));
    }
}
