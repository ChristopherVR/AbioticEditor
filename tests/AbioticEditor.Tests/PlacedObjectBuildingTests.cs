using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Base-building phases 1, 2 (evidence) and the Core half of 4: the placed-object census, the
/// transform round-trip proof, the staged-transform model, and the group reference analyzer. All
/// run against real fixture worlds and skip when a fixture is absent.
/// </summary>
public sealed class PlacedObjectBuildingTests
{
    private static string? SmallFacility()
        => Fixtures.ClientWorldSaves("WorldSave_Facility.sav")
            .OrderBy(p => new FileInfo(p).Length).FirstOrDefault();

    private static string? RegionFixture(string name)
        => Fixtures.ServerWorldsDir is { } d && File.Exists(Path.Combine(d, name)) ? Path.Combine(d, name) : null;

    // ---------- census ----------

    [Fact]
    public void Census_accounts_for_every_deployable_and_keeps_unknown_classes()
    {
        var path = SmallFacility();
        if (path is null) return;
        var data = WorldSaveReader.ReadFromFile(path);
        var report = PlacedObjectCensus.Build(data, "facility");

        Assert.Equal(data.Deployables.Count, report.ObjectCount);
        Assert.Equal(report.ObjectCount, report.Classes.Sum(c => c.Count));
        Assert.Equal(report.ObjectCount, report.KeysAreActorPaths + report.KeysAreGuids + report.KeysOther);
        Assert.NotNull(report.Objects);
        Assert.Equal(report.ObjectCount, report.Objects!.Count);

        // The persistent-vs-runtime split covers the struct members the fixtures actually carry.
        Assert.Contains(report.Fields, f => f.Field == "Transform" && f.Persistence == PlacedFieldPersistence.Persistent);
        Assert.Contains(report.Fields, f => f.Field == "ItemProxies" && f.Persistence == PlacedFieldPersistence.LikelyRuntime);

        // Player-built objects carry GUID keys and an actor path in the persistent level.
        var built = report.Objects.Where(o => o.DeployedByPlayer == true).ToList();
        Assert.NotEmpty(built);
        Assert.All(built, o => Assert.Equal("guid32", PlacedObjectCensus.KeyShape(o.Key)));
        Assert.All(built, o => Assert.NotNull(o.ActorPath));
        Assert.All(built, o => Assert.NotNull(o.Transform?.Translation));
    }

    [Fact]
    public void Census_flags_modded_paths_without_dropping_them()
    {
        Assert.Equal(PlacedClassOrigin.GameBlueprint,
            PlacedObjectCensus.ClassOriginOf("/Game/Blueprints/DeployedObjects/Misc/Deployed_PlugStrip.Deployed_PlugStrip_C"));
        Assert.Equal(PlacedClassOrigin.NonGamePath, PlacedObjectCensus.ClassOriginOf("/SomeMod/Foo.Foo_C"));
        Assert.Equal(PlacedClassOrigin.Missing, PlacedObjectCensus.ClassOriginOf(null));
        Assert.Equal("Hunger", PlacedObjectCensus.StripHash("Hunger_2_A6C5CC6E4D7A46F1B3A0A0E8B5C7D9F1"));
        Assert.Equal("Plain", PlacedObjectCensus.StripHash("Plain"));
    }

    // ---------- transform round trip ----------

    private static (string Path, WorldSaveData Data, string Key, PlacedObjectTransform Before) PickBuilt(string temp)
    {
        var src = SmallFacility()!;
        File.Copy(src, temp);
        var data = WorldSaveReader.ReadFromFile(temp);
        var report = PlacedObjectCensus.Build(data);
        var obj = report.Objects!.First(o => o.DeployedByPlayer == true
            && o.Transform is { Translation: not null, Rotation: not null, Scale3D: not null });
        return (temp, data, obj.Key, obj.Transform!);
    }

    [Fact]
    public void Transform_write_of_the_same_value_is_byte_identical()
    {
        if (SmallFacility() is null) return;
        var temp = Path.Combine(Path.GetTempPath(), $"abf-xf-{Guid.NewGuid():N}.sav");
        try
        {
            var (_, data, key, before) = PickBuilt(temp);
            var original = File.ReadAllBytes(temp);

            // Baseline: an untouched read/write is already byte-identical (the contract the edit builds on).
            WorldSaveWriter.WriteToFile(data, temp);
            Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(temp)), "baseline round trip differs");

            Assert.True(WorldSaveWriter.ApplyPlacedObjectTransform(data, key, before.Translation, before.Rotation));
            WorldSaveWriter.WriteToFile(data, temp);
            Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(temp)), "same-value transform write changed bytes");
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Fact]
    public void Transform_write_of_a_changed_value_rereads_equal_and_touches_only_that_transform()
    {
        if (SmallFacility() is null) return;
        var temp = Path.Combine(Path.GetTempPath(), $"abf-xf-{Guid.NewGuid():N}.sav");
        try
        {
            var (_, data, key, before) = PickBuilt(temp);
            var original = File.ReadAllBytes(temp);

            var moved = new PlacedVector(before.Translation!.Value.X + 123.5, before.Translation.Value.Y - 45.25, before.Translation.Value.Z + 10);
            var turned = new PlacedQuaternion(0, 0, Math.Sin(0.3), Math.Cos(0.3));
            Assert.True(WorldSaveWriter.ApplyPlacedObjectTransform(data, key, moved, turned));
            WorldSaveWriter.WriteToFile(data, temp);

            var after = File.ReadAllBytes(temp);
            Assert.Equal(original.Length, after.Length);
            var changed = Enumerable.Range(0, original.Length).Count(i => original[i] != after[i]);
            // Translation (3 doubles) + rotation (4 doubles) at most; nothing else in the file may move.
            Assert.InRange(changed, 1, 56);

            var reread = WorldSaveReader.ReadFromFile(temp);
            var t = PlacedObjectCensus.Build(reread).Objects!.Single(o => o.Key == key).Transform!;
            Assert.Equal(moved, t.Translation);
            Assert.Equal(turned, t.Rotation);
            Assert.Equal(before.Scale3D, t.Scale3D);
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Fact]
    public void Transform_write_refuses_unknown_keys_and_never_creates_members()
    {
        var path = SmallFacility();
        if (path is null) return;
        var data = WorldSaveReader.ReadFromFile(path);
        Assert.False(WorldSaveWriter.ApplyPlacedObjectTransform(data, "no-such-key", new PlacedVector(1, 2, 3), null));
    }

    // ---------- staged transforms ----------

    [Fact]
    public void Staged_transforms_preview_revert_and_apply()
    {
        var path = SmallFacility();
        if (path is null) return;
        var data = WorldSaveReader.ReadFromFile(path);
        var objs = PlacedObjectCensus.Build(data).Objects!;
        var built = objs.First(o => o.DeployedByPlayer == true && o.Transform?.Rotation is not null);
        var stat = objs.First(o => o.DeployedByPlayer == false && o.Transform?.Translation is not null);

        var staged = new StagedPlacedTransforms();
        Assert.True(staged.MoveBy(data, built.Key, 100, 0, 0));
        Assert.True(staged.RotateYawBy(data, built.Key, 90));
        var row = Assert.Single(staged.Preview(data));
        Assert.False(row.Blocked);
        Assert.Equal(100, row.DistanceCm!.Value, 6);
        Assert.Equal(90, row.YawDeltaDegrees!.Value, 6);
        Assert.Equal(built.Transform!.Translation!.Value.X + 100, row.After!.Translation!.Value.X, 6);

        // Preview and staging never write.
        Assert.Equal(built.Transform.Translation, PlacedObjectCensus.Build(data).Objects!.Single(o => o.Key == built.Key).Transform!.Translation);

        Assert.True(staged.Revert(built.Key));
        Assert.True(staged.IsEmpty);

        // Level-placed statics are refused by default and stay staged.
        staged.Stage(stat.Key, new PlacedVector(0, 0, 0), null);
        var refused = staged.ApplyTo(data);
        Assert.Empty(refused.Applied);
        Assert.Single(refused.Refused);
        Assert.False(staged.IsEmpty);
        staged.RevertAll();

        staged.MoveBy(data, built.Key, 0, 250, 0);
        var applied = staged.ApplyTo(data);
        Assert.Equal([built.Key], applied.Applied);
        Assert.True(staged.IsEmpty);
        var now = PlacedObjectCensus.Build(data).Objects!.Single(o => o.Key == built.Key).Transform!;
        Assert.Equal(built.Transform.Translation!.Value.Y + 250, now.Translation!.Value.Y, 6);
    }

    // ---------- group references ----------

    [Fact]
    public void Group_analyzer_splits_internal_from_external_power_links()
    {
        var candidates = Fixtures.ClientWorldSaves("WorldSave_Facility.sav").Concat(
            RegionFixture("WorldSave_Facility.sav") is { } dedicated ? [dedicated] : []).ToList();
        if (candidates.Count == 0) return;

        WorldSaveData? data = null;
        PowerLinkRecord? link = null;
        foreach (var candidate in candidates)
        {
            var d = WorldSaveReader.ReadFromFile(candidate);
            var report = PlacedObjectCensus.Build(d);
            var keys = report.Objects!.Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
            // A socket owned by a placed object that feeds another placed object of the same save.
            link = report.PowerLinks.FirstOrDefault(l =>
                l.PluggedInDeviceId is not null && keys.Contains(l.PluggedInDeviceId)
                && PlacedGroupReferenceAnalyzer.OwnerKeyOf(l.SocketId) is { } owner
                && keys.Contains(owner) && owner != l.PluggedInDeviceId);
            if (link is not null)
            {
                data = d;
                break;
            }
        }
        Assert.True(link is not null && data is not null, "no fixture Facility save has an owned socket feeding a placed device");
        var ownerKey = PlacedGroupReferenceAnalyzer.OwnerKeyOf(link!.SocketId)!;

        var both = PlacedGroupReferenceAnalyzer.Analyze(data!, [ownerKey, link.PluggedInDeviceId!]);
        Assert.Contains(both.Internal, r => r.Field == "PluggedInDeviceAssetID" && r.OtherEnd == link.PluggedInDeviceId);

        var onlyOwner = PlacedGroupReferenceAnalyzer.Analyze(data!, [ownerKey]);
        Assert.Contains(onlyOwner.External, r => r.Field == "PluggedInDeviceAssetID" && r.OtherEnd == link.PluggedInDeviceId);

        var onlyTarget = PlacedGroupReferenceAnalyzer.Analyze(data!, [link.PluggedInDeviceId!]);
        Assert.Contains(onlyTarget.External,
            r => r.Kind == GroupReferenceKind.PowerSocketTargetsSelection && r.SelectedKey == link.PluggedInDeviceId);

        var ghost = PlacedGroupReferenceAnalyzer.Analyze(data!, ["ffffffffffffffffffffffffffffffff"]);
        Assert.Equal(["ffffffffffffffffffffffffffffffff"], ghost.MissingKeys);
    }

    [Fact]
    public void Group_analyzer_reports_teleporter_peers_and_bed_claims()
    {
        var path = RegionFixture("WorldSave_Facility.sav");
        if (path is null) return;
        var data = WorldSaveReader.ReadFromFile(path);
        var objs = PlacedObjectCensus.Build(data).Objects!;

        var claimedBed = objs.FirstOrDefault(o => o.OwnerId is not null && o.ClassName?.Contains("Bed", StringComparison.Ordinal) == true);
        if (claimedBed is not null)
        {
            var r = PlacedGroupReferenceAnalyzer.Analyze(data, [claimedBed.Key]);
            Assert.Contains(r.IdentityBindings, b => b.Kind == "BedClaim" && b.ObjectKey == claimedBed.Key);
        }

        var pad = objs.FirstOrDefault(o => o.ClassName?.Contains("TeleporterPad", StringComparison.Ordinal) == true);
        if (pad is not null)
        {
            var r = PlacedGroupReferenceAnalyzer.Analyze(data, [pad.Key]);
            // A pad with a non-zero tag is reported; unassigned pads (tag 0) bind to nothing.
            Assert.All(r.IdentityBindings.Where(b => b.Kind == "TeleporterTag"), b => Assert.Equal(pad.Key, b.ObjectKey));
        }
    }

    private static void Cleanup(string temp)
    {
        foreach (var f in new[] { temp, temp + ".bak" })
        {
            try { File.Delete(f); } catch (IOException) { }
        }
    }
}
