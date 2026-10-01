using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using static AbioticEditor.Tests.BaseEditTestSupport;

namespace AbioticEditor.Tests;

/// <summary>
/// The 3D view's delete, duplicate and group edits, through the world session: one staged model for
/// every kind of base edit, the save path (writes exactly the staged result, refuses without writing),
/// the reference scan over the sibling saves, the scene marks, and the resource wiring. Fixture-backed
/// tests skip when the dedicated-server Facility fixture is absent.
/// </summary>
public sealed class BaseEditing3DSessionTests
{
    private sealed class TempWorld : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "abf-b3d-edit-" + Guid.NewGuid().ToString("N"));
        public string SavePath { get; }

        public TempWorld(params string[] alsoCopy)
        {
            Directory.CreateDirectory(Folder);
            SavePath = Path.Combine(Folder, "WorldSave_Facility.sav");
            File.WriteAllBytes(SavePath, OriginalBytes);
            foreach (var name in alsoCopy) File.Copy(Path.Combine(Fixtures.ServerWorldsDir!, name), Path.Combine(Folder, name));
        }

        public void Dispose()
        {
            try { Directory.Delete(Folder, recursive: true); }
            catch (IOException) { }
        }
    }

    private static WorldSaveSession Open(string path) => new(WorldSaveReader.ReadFromFile(path), path);

    /// <summary>Player-built objects with a full transform that no power outlet owns or feeds and that hold nothing special.</summary>
    private static List<PlacedObjectSummary> FreeObjects(WorldSaveSession session, WorldSaveData data)
    {
        var sockets = Sockets(data);
        var tied = sockets.Select(s => s.Owner).Concat(sockets.Select(s => s.Plugged)).Where(k => k is not null).ToHashSet(StringComparer.Ordinal);
        return session.PlacedObjects
            .Where(o => o.DeployedByPlayer == true && o.Key.Length == 32 && !tied.Contains(o.Key)
                && o.Transform is { Translation: not null, Rotation: not null, Scale3D: not null }
                && o.InventoryCount == 0
                && PlacedObjectCategoryCatalog.Classify(o.ClassName, false) is PlacedObjectCategory.Structure or PlacedObjectCategory.Light or PlacedObjectCategory.Other)
            .Take(6)
            .ToList();
    }

    private static string FindDevicePluggedByAnother(WorldSaveData data)
    {
        var built = PlayerBuilt(data).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        return Sockets(data).First(s => s.Plugged is not null && built.Contains(s.Plugged) && s.Owner != s.Plugged).Plugged!;
    }

    // ---------- staging across kinds ----------

    [Fact]
    public void One_staged_model_holds_moves_deletes_and_copies_and_each_can_be_reverted_alone()
    {
        if (!HasServerFacility) return;
        using var temp = new TempWorld();
        var session = Open(temp.SavePath);
        var free = FreeObjects(session, Load());
        Assert.True(free.Count >= 4, "the fixture must have free player-built objects");
        var (a, b, c, d) = (free[0], free[1], free[2], free[3]);
        Assert.False(session.IsDirty);

        Assert.Equal(1, session.StagePlacedDelete([a.Key]).Staged);
        Assert.True(session.IsDirty);
        Assert.True(session.IsPlacedDeletionStaged(a.Key));
        Assert.False(session.HasStagedPlacedTransforms);

        var dup = session.StagePlacedDuplicate([b.Key], new PlacedVector(300, 0, 0));
        Assert.Equal(1, dup.Staged);
        var staged = Assert.Single(session.StagedPlacedDuplications);

        Assert.Equal(2, session.StageGroupRotate([c.Key, d.Key], 90).Staged);
        Assert.True(session.HasStagedPlacedTransforms);

        var preview = session.PreviewBaseEdits();
        Assert.Single(preview.Deletions);
        Assert.Single(preview.Duplications);
        Assert.Equal(2, preview.Transforms.Count);
        Assert.Equal(preview.ObjectsBefore, preview.ObjectsAfter); // one gone, one added
        Assert.Contains(preview.Issues, i => i.Code == "coordinates-unchecked");

        // Each kind reverts on its own, and the indicator stays lit until the last one is gone.
        Assert.True(session.RevertPlacedDeletion(a.Key));
        Assert.True(session.IsDirty);
        Assert.True(session.RevertPlacedDuplication(staged.Id));
        Assert.True(session.IsDirty);
        Assert.True(session.RevertPlacedTransform(c.Key));
        Assert.True(session.IsDirty);
        Assert.True(session.RevertPlacedTransform(d.Key));
        Assert.False(session.IsDirty);
        Assert.False(session.HasStagedBaseEdits);

        // The workspace REVERT clears every kind at once.
        session.StagePlacedDelete([a.Key]);
        session.StagePlacedDuplicate([b.Key], new PlacedVector(300, 0, 0));
        session.StageGroupMove([c.Key], 10, 0, 0);
        Assert.True(session.IsDirty);
        session.Revert();
        Assert.False(session.IsDirty);
        Assert.False(session.HasStagedBaseEdits);
        Assert.Empty(session.StagedPlacedDeletions);
        Assert.Empty(session.StagedPlacedDuplications);
    }

    [Fact]
    public void Level_placed_and_unknown_objects_are_refused_for_delete_copy_and_group_edits()
    {
        if (!HasServerFacility) return;
        using var temp = new TempWorld();
        var session = Open(temp.SavePath);
        var level = session.PlacedObjects.First(o => o.DeployedByPlayer != true && o.Transform?.Translation is not null);

        var delete = session.StagePlacedDelete([level.Key, "no-such-key"]);
        Assert.Equal(0, delete.Staged);
        Assert.Contains(delete.Refused, r => r.Key == level.Key && r.Reason == PlacedTransformRefusal.LevelPlaced);
        Assert.Contains(delete.Refused, r => r.Reason == PlacedTransformRefusal.NotFound);
        Assert.Equal(0, session.StagePlacedDuplicate([level.Key], new PlacedVector(100, 0, 0)).Staged);
        Assert.Equal(0, session.StageGroupMove([level.Key], 100, 0, 0).Staged);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Group_align_snap_and_distribute_stage_through_the_session()
    {
        if (!HasServerFacility) return;
        using var temp = new TempWorld();
        var session = Open(temp.SavePath);
        var free = FreeObjects(session, Load()).Take(4).ToList();

        var align = session.StageGroupAlignYaw(free.Select(o => o.Key), free[0].Key);
        Assert.True(align.Staged >= 0);
        foreach (var o in free)
        {
            var yaw = session.CurrentPlacedTransform(o.Key)!.Rotation!.Value.YawDegrees;
            Assert.Equal(free[0].Transform!.Rotation!.Value.YawDegrees, yaw, 3);
        }

        var snap = session.StageGroupSnap(free.Select(o => o.Key), 50, null, null);
        Assert.Equal(free.Count, snap.Staged + snap.NotStaged);
        foreach (var o in free)
        {
            var t = session.CurrentPlacedTransform(o.Key)!.Translation!.Value;
            Assert.Equal(0, Math.Abs(t.X / 50 - Math.Round(t.X / 50)), 6);
            Assert.Equal(0, Math.Abs(t.Y / 50 - Math.Round(t.Y / 50)), 6);
        }

        var before = free.Select(o => session.CurrentPlacedTransform(o.Key)!.Translation!.Value.X).ToList();
        var spread = session.StageGroupDistribute(free.Select(o => o.Key), PlacementAxis.X);
        Assert.True(spread.Staged >= 0);
        var xs = free.Select(o => session.CurrentPlacedTransform(o.Key)!.Translation!.Value.X).ToList();
        Assert.Equal(before.Min(), xs.Min(), 3); // distribution keeps the two ends
        Assert.Equal(before.Max(), xs.Max(), 3);
    }

    [Fact]
    public void A_turn_then_duplicate_copies_the_turned_rotation_not_the_saved_one()
    {
        if (!HasServerFacility) return;
        using var temp = new TempWorld();
        var session = Open(temp.SavePath);
        var source = FreeObjects(session, Load())[0];
        var data = Load();
        var edits = new StagedBaseEdits(Counter());
        edits.RotateYaw(data, [source.Key], 90);
        var dup = edits.StageDuplicate([source.Key], new PlacedVector(400, 0, 0)); // no extra yaw on the copy
        var row = Assert.Single(edits.Preview(data).Duplications);
        var turned = edits.Transforms.Current(data, source.Key)!.Rotation!.Value;
        Assert.NotEqual(source.Transform!.Rotation, turned);
        Assert.Equal(turned, row.After!.Rotation);

        Assert.True(edits.ApplyTo(data).Applied);
        var copy = PlacedObjectCensus.ReadTransform(Entry(data, dup.NewKeys[source.Key]))!;
        Assert.Equal(turned, copy.Rotation);

        // With nothing turned, a copy keeps the saved rotation.
        var plain = Load();
        var untouched = new StagedBaseEdits(Counter());
        untouched.StageDuplicate([source.Key], new PlacedVector(400, 0, 0));
        Assert.Equal(source.Transform!.Rotation, Assert.Single(untouched.Preview(plain).Duplications).After!.Rotation);
    }

    // ---------- the save ----------

    [Fact]
    public async Task Save_writes_exactly_the_staged_result_and_the_backup_is_the_original()
    {
        if (!HasServerFacility) return;
        using var temp = new TempWorld();
        var original = File.ReadAllBytes(temp.SavePath);
        var session = Open(temp.SavePath);
        var free = FreeObjects(session, Load());
        var (a, b, c) = (free[0], free[1], free[2]);
        var offset = new PlacedVector(275, -50, 0);

        session.StagePlacedDelete([a.Key]);
        session.StagePlacedDuplicate([b.Key], offset, 90);
        session.StageGroupRotate([c.Key], 45);
        var copyKey = Assert.Single(session.StagedPlacedDuplications).NewKeys[b.Key];

        await session.SaveAsync();

        Assert.False(session.IsDirty);
        Assert.False(session.HasStagedBaseEdits);
        Assert.True(File.Exists(temp.SavePath + ".bak"));
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(temp.SavePath + ".bak")), "the backup must be the pre-save file");

        // The expected bytes: the very same staged edits applied by Core to a fresh copy, with the copy's key fixed.
        var expected = Load();
        var edits = new StagedBaseEdits(() => copyKey);
        edits.StageDelete([a.Key]);
        edits.StageDuplicate([b.Key], offset, 90);
        edits.RotateYaw(expected, [c.Key], 45);
        Assert.True(edits.ApplyTo(expected).Applied);
        var written = File.ReadAllBytes(temp.SavePath);
        Assert.True(Serialize(expected).AsSpan().SequenceEqual(written), "the file must be exactly what the staged edits produce");

        // And read back with Core: the object is gone, the copy is there at its target, the rest is intact.
        var reread = WorldSaveReader.ReadFromFile(temp.SavePath);
        var after = PlacedObjectCensus.Build(reread).Objects!.ToDictionary(o => o.Key, StringComparer.Ordinal);
        Assert.DoesNotContain(a.Key, after.Keys);
        Assert.Contains(copyKey, after.Keys);
        Assert.Equal(b.ClassName, after[copyKey].ClassName);
        Assert.Equal(session.PlacedObjects.Count, after.Count); // session cache refreshed: one gone, one added
        Assert.Contains(session.PlacedObjects, o => o.Key == copyKey);
        Assert.DoesNotContain(session.PlacedObjects, o => o.Key == a.Key);
        Assert.DoesNotContain(session.Deployables, x => x.Id == a.Key);
        Assert.Contains(session.Deployables, x => x.Id == copyKey);
    }

    [Fact]
    public async Task A_refused_save_writes_nothing_keeps_every_edit_staged_and_explains_why()
    {
        if (!HasServerFacility) return;
        using var temp = new TempWorld();
        var original = File.ReadAllBytes(temp.SavePath);
        var session = Open(temp.SavePath);
        var free = FreeObjects(session, Load());
        var device = FindDevicePluggedByAnother(Load());

        session.StageGroupMove([free[0].Key], 100, 0, 0);
        session.StagePlacedDelete([device]); // default policy refuses while another outlet still feeds it

        var preview = session.PreviewBaseEdits();
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Issues, i => i.Code == "inbound-plug" && i.IsBlocking);

        var refusal = await Assert.ThrowsAsync<BaseEditsRefusedException>(async () => await session.SaveAsync());
        Assert.Contains(refusal.Issues, i => i.Code == "inbound-plug");
        Assert.Contains("Nothing was saved", refusal.Message, StringComparison.Ordinal);

        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(temp.SavePath)), "nothing may be written");
        Assert.False(File.Exists(temp.SavePath + ".bak"));
        Assert.True(session.IsDirty);
        Assert.True(session.IsPlacedDeletionStaged(device));
        Assert.True(session.HasStagedPlacedTransforms);
        Assert.Contains(session.LastBaseEditRefusal, i => i.Code == "inbound-plug");

        // Choosing the policy that unplugs the feeders makes the same save go through.
        Assert.True(session.RevertPlacedDeletion(device));
        session.StagePlacedDelete([device], new DeletePolicy
        {
            InboundPlugs = ReferencePolicy.Drop, OtherReferences = ReferencePolicy.Keep,
            BedClaims = ReferencePolicy.Drop, TeleporterPeers = ReferencePolicy.Keep,
        });
        await session.SaveAsync();
        Assert.False(session.IsDirty);
        Assert.Empty(session.LastBaseEditRefusal);
        var written = PlacedObjectCensus.Build(WorldSaveReader.ReadFromFile(temp.SavePath)).Objects!;
        Assert.DoesNotContain(written, o => o.Key == device);
        // The refused attempt left nothing behind in memory either: the move lands once, not twice.
        var moved = written.Single(o => o.Key == free[0].Key).Transform!.Translation!.Value;
        Assert.Equal(free[0].Transform!.Translation!.Value.X + 100, moved.X, 3);
    }

    // ---------- the other saves of the world ----------

    [Fact]
    public async Task Sibling_saves_are_read_only_and_the_scan_scope_reflects_them()
    {
        if (!HasServerFacility) return;
        using var alone = new TempWorld();
        var lone = Open(alone.SavePath);
        await lone.LoadOtherSavesAsync();
        Assert.True(lone.OtherSavesLoaded);
        Assert.Empty(lone.OtherSaveNames);
        var target = FreeObjects(lone, Load())[0].Key;
        Assert.Contains(lone.PreviewHypothetical(e => e.StageDelete([target])).Issues, i => i.Code == "scan-scope"); // says only this save was scanned

        using var withSiblings = new TempWorld("WorldSave_Facility_Botanical.sav", "WorldSave_MetaData.sav");
        var siblingBytes = File.ReadAllBytes(Path.Combine(withSiblings.Folder, "WorldSave_Facility_Botanical.sav"));
        var session = Open(withSiblings.SavePath);
        await session.LoadOtherSavesAsync();
        Assert.Equal(["WorldSave_Facility_Botanical.sav", "WorldSave_MetaData.sav"], session.OtherSaveNames.Order(StringComparer.Ordinal));
        Assert.Empty(session.OtherSaveFailures);
        Assert.DoesNotContain(session.PreviewHypothetical(e => e.StageDelete([target])).Issues, i => i.Code == "scan-scope");

        // A save with a staged deletion never writes the siblings.
        session.StagePlacedDelete([target]);
        await session.SaveAsync();
        Assert.True(siblingBytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(withSiblings.Folder, "WorldSave_Facility_Botanical.sav"))));
        Assert.False(File.Exists(Path.Combine(withSiblings.Folder, "WorldSave_Facility_Botanical.sav.bak")));
    }

    // ---------- what the scene draws ----------

    [Fact]
    public void The_scene_draws_deleted_objects_red_and_copies_as_new_boxes_the_filters_never_hide()
    {
        if (!HasServerFacility) return;
        using var temp = new TempWorld();
        var session = Open(temp.SavePath);
        var free = FreeObjects(session, Load());
        var rows = session.PlacedObjects.Where(o => o.Transform?.Translation is not null).Take(5).Append(free[0]).Distinct().ToList();
        var copy = new Base3DCopy("COPYKEY", free[0].ClassName, free[0].Transform!, "Copy of thing");

        var scene = Base3DScene.Build(rows, _ => null, new HashSet<string> { free[0].Key }, [copy]);

        Assert.Equal(rows.Count + 1, scene.Objects.Count);
        Assert.Equal(1, scene.CopyCount);
        Assert.Equal(Base3DObject.MarkCopy, scene.Objects[^1].Mark);
        Assert.Equal("COPYKEY", scene.Objects[^1].Key);
        Assert.Equal(Base3DObject.MarkDeleted, scene.Objects.Single(o => o.Key == free[0].Key).Mark);
        Assert.Equal(rows.Count - 1, scene.Objects.Count(o => o.Mark == Base3DObject.MarkNone));

        var hideEverything = new Base3DFilter { ShowPlayerBuilt = false, ShowLevelPlaced = false };
        Assert.Equal([scene.Objects.Count - 1], scene.Apply(hideEverything));
    }

    // ---------- resources and wiring ----------

    private static HashSet<string> ResourceNames()
        => XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx"))
            .Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Every_resource_key_the_delete_duplicate_and_group_ui_uses_exists_in_english()
    {
        var names = ResourceNames();
        var source = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor")
            + UiSource.ReadAllText("Components", "World", "WorldBases3DTab.BaseEdits.razor.cs");
        var used = Regex.Matches(source, "\"(World3D_[A-Za-z_]+)\"").Select(m => m.Groups[1].Value)
            .Where(k => !k.EndsWith('_') && k != "World3D_Gizmo")
            .ToHashSet();
        Assert.Contains("World3D_StageDelete", used);
        Assert.Contains("World3D_StageDuplicate", used);
        Assert.Contains("World3D_SelectAllShown", used);
        Assert.All(used, key => Assert.Contains(key, names));
        foreach (var policy in new[] { "Refuse", "Drop", "Keep" }) Assert.Contains("World3D_Policy_" + policy, names);

        // The staged-edits card and the delete panel keep the experimental warning visible.
        var value = XDocument.Load(UiSource.Resolve("Localization", "AppResources.resx")).Root!.Elements("data")
            .First(e => (string)e.Attribute("name")! == "World3D_EditUnverified").Element("value")!.Value;
        Assert.Contains("does not check that a piece fits", value, StringComparison.Ordinal);
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        Assert.Contains("data-b3d=\"delete-unverified\"", tab, StringComparison.Ordinal);
        Assert.Contains("data-b3d=\"duplicate-unverified\"", tab, StringComparison.Ordinal);
        Assert.Contains("data-b3d=\"staged-unverified\"", tab, StringComparison.Ordinal);
        Assert.Contains("ProximityHints.Disclaimer", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void The_new_ui_stages_only_through_the_session_and_only_under_the_experimental_opt_in()
    {
        var tab = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.razor");
        var code = UiSource.ReadAllText("Components", "World", "WorldBases3DTab.BaseEdits.razor.cs");
        Assert.DoesNotContain("WorldSaveWriter", tab + code, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyTo(", tab + code, StringComparison.Ordinal);
        Assert.Contains("@if (_moveOptIn && _deleteOpen)", tab, StringComparison.Ordinal);
        Assert.Contains("@if (_moveOptIn && _dupOpen)", tab, StringComparison.Ordinal);
        Assert.Contains("@if (_moveOptIn && _selected.Count > 0)", tab, StringComparison.Ordinal);
        Assert.Contains("Session.StagePlacedDelete", code, StringComparison.Ordinal);
        Assert.Contains("Session.StagePlacedDuplicate", code, StringComparison.Ordinal);
        Assert.Contains("Session.RevertAllBaseEdits()", tab, StringComparison.Ordinal); // REVERT ALL covers every kind, not only moves

        // A refused save is shown like other save failures: a toast in the shell.
        var shell = UiSource.ReadAllText("Components", "Shared", "WorkspaceShell.razor");
        Assert.Contains("BaseEditsRefusedException", shell, StringComparison.Ordinal);
    }
}
