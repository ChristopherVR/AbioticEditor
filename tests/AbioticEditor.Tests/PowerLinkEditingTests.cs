using System.IO;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using static AbioticEditor.Tests.BaseEditTestSupport;

namespace AbioticEditor.Tests;

/// <summary>
/// Power rerouting and repair (<see cref="PowerLinkEdits"/>, <see cref="PowerRepair"/>) against the real
/// server Facility fixture: exactly the socket records a change should touch change, refusals change
/// nothing, and what is written reads back as the intended power graph.
/// </summary>
public sealed class PowerLinkEditingTests
{
    private static BaseEditApplyResult Applied(BaseEditApplyResult result)
    {
        Assert.True(result.Applied, string.Join(" | ", result.Issues.Where(i => i.IsBlocking).Select(i => i.Code + ": " + i.Message)));
        return result;
    }

    /// <summary>A plugged device, its current socket, and a free outlet elsewhere that it may move to.</summary>
    private static (string Device, string OldSocket, string NewSocket) MovablePlug(WorldSaveData data)
    {
        var sockets = Sockets(data);
        var built = PlayerBuilt(data).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var plugged = sockets.ToDictionary(s => s.Id, s => s.Plugged, StringComparer.Ordinal);
        foreach (var (id, _, device) in sockets.Where(s => s.Plugged is not null && built.Contains(s.Plugged)))
        {
            var free = sockets.FirstOrDefault(s => s.Plugged is null && s.Owner is not null && built.Contains(s.Owner)
                                                   && s.Owner != device && !PowerLinkEdits.IsUpstream(device!, s.Owner, plugged));
            if (free.Id is not null) return (device!, id, free.Id);
        }
        throw new InvalidOperationException("Fixture has no movable plug.");
    }

    [Fact]
    public void Replugging_moves_the_device_to_the_new_socket_and_clears_the_old_one()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var (device, oldSocket, newSocket) = MovablePlug(data);
        var before = Fingerprint(data.Raw);

        var edits = new StagedBaseEdits();
        edits.StagePlug(newSocket, device);
        var row = Assert.Single(edits.Preview(data).PowerLinks);
        Assert.Equal([oldSocket], row.FeedsCleared);
        Assert.False(row.CreatesRecord);

        var result = Applied(edits.ApplyTo(data));
        Assert.Equal(2, result.PowerLinksChanged);
        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Empty(added);
        Assert.Empty(removed);
        Assert.Equal(new[] { "PowerSocketMap/" + newSocket, "PowerSocketMap/" + oldSocket }.Order(StringComparer.Ordinal), changed.Order(StringComparer.Ordinal));
        Assert.Equal(device, SocketPlugged(data, newSocket));
        Assert.Equal("-1", SocketPlugged(data, oldSocket));
    }

    [Fact]
    public void A_replug_survives_writing_and_reading_the_save()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var (device, _, newSocket) = MovablePlug(data);
        var edits = new StagedBaseEdits();
        edits.StagePlug(newSocket, device);
        Applied(edits.ApplyTo(data));

        using var ms = new MemoryStream(Serialize(data));
        var reread = WorldSaveReader.ReadFromStream(ms);
        var graph = new PowerGraphBuilder().AddSave("WorldSave_Facility.sav", reread.Raw).Build();
        var link = Assert.Single(graph.LinksIntoDevice(device));
        Assert.Equal(newSocket, link.Socket.Key);
    }

    [Fact]
    public void Plugging_into_an_unused_outlet_creates_its_record_in_the_games_shape()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var sockets = Sockets(data);
        var classes = PowerLinkEdits.ObjectClasses(data);
        var numbers = PowerLinkEdits.OutletNumbersByClass(data);
        // An owner that lacks the record for one of its class's outlets.
        var (owner, digit) = sockets.Where(s => s.Owner is not null && classes.ContainsKey(s.Owner))
            .Select(s => s.Owner!).Distinct()
            .SelectMany(o => numbers[classes[o]!].Select(n => (Owner: o, Digit: n)))
            .First(x => !sockets.Any(s => s.Id == x.Owner + x.Digit));
        var device = PlayerBuilt(data).Select(o => o.Key)
            .First(k => k != owner && !sockets.Any(s => s.Plugged == k)
                        && !PowerLinkEdits.IsUpstream(k, owner, sockets.ToDictionary(s => s.Id, s => s.Plugged)));
        var before = Fingerprint(data.Raw);

        var edits = new StagedBaseEdits();
        edits.StagePlug(owner + digit, device);
        Assert.True(Assert.Single(edits.Preview(data).PowerLinks).CreatesRecord);
        var result = Applied(edits.ApplyTo(data));
        Assert.Equal(1, result.SocketRecordsCreated);

        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Equal(["PowerSocketMap/" + owner + digit], added);
        Assert.Empty(removed);
        Assert.Empty(changed);
        var record = WorldMapAccessor.FindEntry(data.Raw, "PowerSocketMap", owner + digit)!;
        Assert.Equal(owner + digit, record.GetString("PowerSocket_"));
        Assert.Equal(device, record.GetString("PluggedInDeviceAssetID_"));
        // Same member names and order as the records the game wrote for outlets.
        var gameShape = WorldMapAccessor.Entries(data.Raw, "PowerSocketMap")
            .First(e => e.Key != owner + digit && PlacedGroupReferenceAnalyzer.OwnerKeyOf(e.Key) is not null && e.Props.GetString("PluggedInDeviceAssetID_") is not null)
            .Props.Select(t => t.Name?.Value).ToList();
        Assert.Equal(gameShape, record.Select(t => t.Name?.Value).ToList());
    }

    [Fact]
    public void Unplugging_changes_only_that_socket()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var (_, socket, _) = MovablePlug(data);
        var before = Fingerprint(data.Raw);
        var edits = new StagedBaseEdits();
        edits.StageUnplug(socket);
        Applied(edits.ApplyTo(data));
        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Empty(added);
        Assert.Empty(removed);
        Assert.Equal(["PowerSocketMap/" + socket], changed);
        Assert.Equal("-1", SocketPlugged(data, socket));
    }

    [Fact]
    public void Self_plugs_loops_deleted_devices_and_unknown_outlets_are_refused_and_change_nothing()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var sockets = Sockets(data);
        var built = PlayerBuilt(data).Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        // A chain owner -> device where the device owns an outlet too: plugging the owner into it loops.
        var chain = sockets.First(s => s.Owner is not null && s.Plugged is not null && built.Contains(s.Owner)
                                       && sockets.Any(o => o.Owner == s.Plugged));
        var downstreamOutlet = sockets.First(o => o.Owner == chain.Plugged).Id;
        var bytes = Serialize(data);

        void Refused(Action<StagedBaseEdits> stage, string code)
        {
            var edits = new StagedBaseEdits();
            stage(edits);
            var result = edits.ApplyTo(data);
            Assert.False(result.Applied);
            Assert.Contains(result.Issues, i => i.Code == code && i.IsBlocking);
            Assert.Equal(bytes, Serialize(data));
        }

        Refused(e => e.StagePlug(chain.Id, chain.Owner!), "self-plug");
        Refused(e => e.StagePlug(downstreamOutlet, chain.Owner!), "power-loop");
        Refused(e => { e.StageDelete([chain.Plugged!]); e.StagePlug(chain.Id, chain.Plugged!); }, "device-deleted");
        Refused(e => e.StagePlug(chain.Owner + "9", chain.Plugged!), "unknown-outlet");
        Refused(e => e.StagePlug(chain.Id, "0123456789ABCDEF0123456789ABCDEF"), "device-missing");
    }

    // ---------- across saves ----------

    /// <summary>A region save that holds a wall socket record, with every other save of the world.</summary>
    private static (string Name, WorldSaveData Region, string WallSocket, List<(string Name, WorldSaveData Data)> Others)? RegionWithWallSocket()
    {
        if (Fixtures.ServerWorldsDir is not { } dir) return null;
        foreach (var file in Directory.GetFiles(dir, "WorldSave_Facility_*.sav").Order(StringComparer.Ordinal))
        {
            var region = WorldSaveReader.ReadFromFile(file);
            var wall = WorldMapAccessor.Entries(region.Raw, "PowerSocketMap").Select(e => e.Key).FirstOrDefault(k => k.Contains('/', StringComparison.Ordinal));
            if (wall is null) continue;
            var others = Directory.GetFiles(dir, "WorldSave_*.sav")
                .Where(f => !string.Equals(f, file, StringComparison.OrdinalIgnoreCase))
                .Select(f => (Path.GetFileName(f), WorldSaveReader.ReadFromFile(f))).ToList();
            return (Path.GetFileName(file), region, wall, others);
        }
        return null;
    }

    [Fact]
    public void A_wall_socket_in_a_region_save_can_power_a_device_from_the_facility_save()
    {
        if (!HasServerFacility || RegionWithWallSocket() is not { } r) return;
        var facility = r.Others.First(o => o.Name == "WorldSave_Facility.sav").Data;
        var fedAnywhere = r.Others.Select(o => o.Data).Append(r.Region)
            .SelectMany(d => Sockets(d)).Where(s => s.Plugged is not null).Select(s => s.Plugged!).ToHashSet(StringComparer.Ordinal);
        var device = PlayerBuilt(facility).Select(o => o.Key).First(k => !fedAnywhere.Contains(k));
        var before = Fingerprint(r.Region.Raw);

        var edits = new StagedBaseEdits { OtherSaves = r.Others };
        edits.StagePlug(r.WallSocket, device);
        Applied(edits.ApplyTo(r.Region));

        var (added, removed, changed) = Diff(before, Fingerprint(r.Region.Raw));
        Assert.Empty(added);
        Assert.Empty(removed);
        Assert.Equal(["PowerSocketMap/" + r.WallSocket], changed);
        Assert.Equal(device, SocketPlugged(r.Region, r.WallSocket));
    }

    [Fact]
    public void A_device_still_fed_in_another_save_is_refused_with_where_to_unplug_it()
    {
        if (!HasServerFacility || RegionWithWallSocket() is not { } r) return;
        var facility = r.Others.First(o => o.Name == "WorldSave_Facility.sav").Data;
        var (_, _, fedInFacility) = Sockets(facility).First(s => s.Plugged is not null && PlayerBuilt(facility).Any(o => o.Key == s.Plugged));
        var bytes = Serialize(r.Region);

        var edits = new StagedBaseEdits { OtherSaves = r.Others };
        edits.StagePlug(r.WallSocket, fedInFacility!);
        var result = edits.ApplyTo(r.Region);
        Assert.False(result.Applied);
        var issue = Assert.Single(result.Issues, i => i.Code == "feed-in-other-save");
        Assert.Contains("WorldSave_Facility.sav", issue.Message, StringComparison.Ordinal);
        Assert.Equal(bytes, Serialize(r.Region));
    }

    /// <summary>
    /// Wall sockets in streamed areas are stored relative to their sub-level. In world space (the sub-level's
    /// streaming transform applied) a socket that already powers a Facility device must sit near it.
    /// </summary>
    [Fact]
    public void Wall_socket_world_positions_sit_near_the_devices_they_power()
    {
        using var assets = AbioticEditor.Core.Assets.GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true } || !HasServerFacility || Fixtures.ServerWorldsDir is not { } dir) return;
        var facility = Load();
        var positions = PlacedObjectCensus.Build(facility).Objects!.Where(o => o.Transform?.Translation is not null)
            .GroupBy(o => o.Key).ToDictionary(g => g.Key, g => g.First().Transform!.Translation!.Value, StringComparer.Ordinal);
        var checkedLinks = 0;
        var nearWorld = 0;
        var nearLocal = 0;
        foreach (var file in Directory.GetFiles(dir, "WorldSave_Facility_*.sav"))
        {
            foreach (var (id, _, plugged) in Sockets(WorldSaveReader.ReadFromFile(file)))
            {
                if (!id.Contains('/', StringComparison.Ordinal) || plugged is null || !positions.TryGetValue(plugged, out var device)) continue;
                if (assets.TryGetActorWorldTransform(id) is not { } world || assets.TryGetActorTransform(id) is not { } local) continue;
                checkedLinks++;
                double Dist(double x, double y, double z) => Math.Sqrt(Math.Pow(x - device.X, 2) + Math.Pow(y - device.Y, 2) + Math.Pow(z - device.Z, 2));
                if (Dist(world.X, world.Y, world.Z) < 5000) nearWorld++;
                if (Dist(local.X, local.Y, local.Z) < 5000) nearLocal++;
            }
        }
        if (checkedLinks == 0) return;
        // Every link is within 50 m in world space; the untransformed positions miss most of them.
        Assert.Equal(checkedLinks, nearWorld);
        Assert.True(nearLocal < checkedLinks, $"local positions were already right for all {checkedLinks} links");
    }

    // ---------- repair ----------

    private static List<(string Name, WorldSaveData Data)> OtherServerSaves()
        => Directory.GetFiles(Fixtures.ServerWorldsDir!, "WorldSave_*.sav")
            .Where(f => !f.EndsWith("WorldSave_Facility.sav", StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path.GetFileName(f), WorldSaveReader.ReadFromFile(f)))
            .ToList();

    [Fact]
    public void Repairs_are_only_proposed_when_the_rest_of_the_world_was_read()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var fixes = PowerRepair.Find(data, [], _ => null);
        Assert.DoesNotContain(fixes, f => f.Kind is PowerRepairKind.UnplugMissingDevice or PowerRepairKind.RemoveLeftoverRecord);
    }

    [Fact]
    public void Repairing_removes_leftovers_and_dangling_plugs_and_nothing_else()
    {
        if (!HasServerFacility) return;
        var data = Load();
        var others = OtherServerSaves();
        var fixes = PowerRepair.Find(data, others, _ => null);
        var leftovers = fixes.Where(f => f.Kind == PowerRepairKind.RemoveLeftoverRecord).ToList();
        var dangling = fixes.Where(f => f.Kind == PowerRepairKind.UnplugMissingDevice).ToList();
        Assert.NotEmpty(leftovers); // the fixture has hundreds of outlet records for devices that are gone
        var before = Fingerprint(data.Raw);

        var edits = new StagedBaseEdits();
        edits.StageRepairs(leftovers.Concat(dangling));
        Applied(edits.ApplyTo(data));

        var (added, removed, changed) = Diff(before, Fingerprint(data.Raw));
        Assert.Empty(added);
        Assert.Equal(leftovers.Select(f => "PowerSocketMap/" + f.SocketId).Order(StringComparer.Ordinal), removed.Order(StringComparer.Ordinal));
        Assert.Equal(dangling.Select(f => "PowerSocketMap/" + f.SocketId).Order(StringComparer.Ordinal), changed.Order(StringComparer.Ordinal));

        // A second look finds nothing of either kind.
        var again = PowerRepair.Find(data, others, _ => null);
        Assert.DoesNotContain(again, f => f.Kind is PowerRepairKind.UnplugMissingDevice or PowerRepairKind.RemoveLeftoverRecord);
    }
}
