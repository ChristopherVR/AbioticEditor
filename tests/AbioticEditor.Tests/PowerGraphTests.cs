using System.IO;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests;

/// <summary>
/// Fixture-backed tests for the stored power graph. The facts asserted here are the ones written up in
/// docs/reference/research/research-power-network-links.md, measured on the dedicated-server Cascade world.
/// </summary>
public sealed class PowerGraphTests
{
    private const string HubFile = "WorldSave_Facility.sav";
    private const string Office1File = "WorldSave_Facility_Office1.sav";
    private const string Office1Socket2 =
        "/Game/Maps/Facility_Office1.Facility_Office1:PersistentLevel.PowerSocket_ParentBP_C_2";

    private static readonly Lazy<(List<(string File, SaveGame Save)> Saves, PowerGraph Graph)?> World = new(Load);

    private static (List<(string, SaveGame)>, PowerGraph)? Load()
    {
        var dir = Fixtures.ServerWorldsDir;
        if (dir is null)
        {
            return null;
        }
        var saves = new List<(string, SaveGame)>();
        foreach (var path in Directory.GetFiles(dir, "WorldSave_*.sav").OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                saves.Add((Path.GetFileName(path), WorldSaveReader.ReadFromFile(path).Raw));
            }
            catch (InvalidDataException)
            {
                // An unreadable sibling contributes nothing.
            }
        }
        return (saves, PowerGraphBuilder.Build(saves));
    }

    [Fact]
    public void Every_link_is_owned_by_a_socket_record_and_no_device_record_carries_power_fields()
    {
        if (World.Value is not { } world) return;
        Assert.NotEmpty(world.Graph.Links);
        Assert.All(world.Graph.Links, link => Assert.Equal("PowerSocketMap", link.OwningRecord.MapName));

        // No reciprocal record: deployable records have no plugged-in / socket / extra-power leaf.
        foreach (var (_, save) in world.Saves)
        {
            foreach (var entry in WorldMapAccessor.Entries(save, "DeployedObjectMap"))
            {
                Assert.DoesNotContain(entry.Props, tag =>
                    tag.Name.ToString().StartsWith("PluggedIn", StringComparison.Ordinal)
                    || tag.Name.ToString().StartsWith("PowerSocket", StringComparison.Ordinal)
                    || tag.Name.ToString().StartsWith("ExtraPowered", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void Extra_powered_list_is_never_used_in_the_fixtures()
    {
        if (World.Value is not { } world) return;
        Assert.All(world.Graph.Links, link => Assert.Equal(PowerLinkSlot.PluggedIn, link.Slot));
    }

    [Fact]
    public void Device_owned_sockets_belong_to_power_infrastructure_with_consistent_outlet_numbers()
    {
        if (World.Value is not { } world) return;
        var owned = world.Graph.Sockets
            .Where(s => s.Kind == PowerSocketKind.DeviceOwned && s.OwnerStatus != PowerEndpointStatus.MissingInSuppliedSaves)
            .ToList();
        Assert.True(owned.Count > 100, $"expected many resolved owned sockets, got {owned.Count}");

        foreach (var socket in owned)
        {
            var owner = world.Graph.FindDevice(socket.OwnerDeviceId!)!;
            var ownerIsKnownPowerDevice = owner.Role != PowerDeviceRole.Other
                || owner.ClassName!.Contains("ExerciseBike", StringComparison.Ordinal);
            Assert.True(ownerIsKnownPowerDevice, $"{socket.Key} is owned by unexpected class {owner.ClassName}");
            if (owner.Role is PowerDeviceRole.CableReroute or PowerDeviceRole.Battery)
            {
                Assert.Equal("1", socket.SocketNumber);
            }
        }
        // Plug boards expose the most outlets, plug strips up to three.
        Assert.All(world.Graph.Sockets.Where(s => s.OwnerDeviceId is not null
                && world.Graph.FindDevice(s.OwnerDeviceId)?.Role == PowerDeviceRole.PlugStrip),
            s => Assert.True(s.SocketNumber is "1" or "2" or "3"));
    }

    [Fact]
    public void A_level_socket_in_a_region_save_supplies_a_cable_in_the_hub_save()
    {
        if (World.Value is not { } world) return;
        var socket = world.Graph.FindSocket(Office1Socket2);
        if (socket is null) return;
        Assert.Equal(PowerSocketKind.LevelPlaced, socket.Kind);
        Assert.Equal(Office1File, socket.Location.FileName);

        var link = Assert.Single(world.Graph.LinksOfSocket(socket.Key));
        Assert.True(link.IsCrossSave);
        var device = world.Graph.FindDevice(link.DeviceId)!;
        Assert.Equal(PowerDeviceRole.CableReroute, device.Role);
        Assert.Equal(HubFile, device.Location!.FileName);
    }

    [Fact]
    public void A_region_save_on_its_own_reports_every_cross_save_endpoint_as_missing()
    {
        if (World.Value is not { } world) return;
        var office = world.Saves.FirstOrDefault(s => s.File == Office1File);
        if (office.Save is null) return;

        var alone = PowerGraphBuilder.Build([office]);
        var levelLinks = alone.Links.Where(l => l.Socket.Kind == PowerSocketKind.LevelPlaced).ToList();
        Assert.NotEmpty(levelLinks);
        Assert.All(levelLinks, l => Assert.Equal(PowerEndpointStatus.MissingInSuppliedSaves, l.DeviceStatus));
        Assert.Contains(PowerGraphValidator.Validate(alone), i => i.Kind == PowerIssueKind.LinkedDeviceMissing);

        var hub = world.Saves.First(s => s.File == HubFile);
        var together = PowerGraphBuilder.Build([office, hub]);
        Assert.Equal(PowerEndpointStatus.ResolvedInOtherSave, together.LinksOfSocket(Office1Socket2).Single().DeviceStatus);
    }

    [Fact]
    public void Validator_flags_missing_endpoints_orphans_and_shared_devices_and_finds_no_loops()
    {
        if (World.Value is not { } world) return;
        var issues = PowerGraphValidator.Validate(world.Graph);

        Assert.Equal(world.Graph.Links.Count(l => l.DeviceStatus == PowerEndpointStatus.MissingInSuppliedSaves),
            issues.Count(i => i.Kind == PowerIssueKind.LinkedDeviceMissing));
        Assert.Contains(issues, i => i.Kind == PowerIssueKind.SocketOwnerMissing);
        Assert.Contains(issues, i => i.Kind == PowerIssueKind.CrossSaveLink);
        Assert.Contains(issues, i => i.Kind == PowerIssueKind.DeviceFedByMultipleSockets
            && i.SubjectIds.Contains("1F43BB0A40083FFDB0ACF291D2EE5591"));
        Assert.DoesNotContain(issues, i => i.Kind == PowerIssueKind.LinkCycle);
        Assert.DoesNotContain(issues, i => i.Kind == PowerIssueKind.UnrecognizedSocketKey);
        Assert.DoesNotContain(issues, i => i.Kind == PowerIssueKind.ExtraPoweredDevicesUsed);
    }

    [Fact]
    public void Upstream_and_downstream_traces_agree_with_the_stored_links()
    {
        if (World.Value is not { } world) return;
        var graph = world.Graph;
        var checkedAny = 0;
        var reachedLevelSocket = false;
        foreach (var link in graph.Links.Where(l => l.DeviceStatus != PowerEndpointStatus.MissingInSuppliedSaves
                     && l.Socket.OwnerDeviceId is not null && l.Socket.OwnerStatus != PowerEndpointStatus.MissingInSuppliedSaves))
        {
            var down = graph.Trace(link.Socket.OwnerDeviceId!, PowerTraceDirection.Downstream);
            Assert.Contains(down.Hops, h => h.Link == link);

            var up = graph.Trace(link.DeviceId, PowerTraceDirection.Upstream);
            Assert.Contains(up.Hops, h => h.Link == link);
            reachedLevelSocket |= up.Hops.Any(h => h.Outcome == PowerTraceOutcome.EndsAtLevelSocket);
            checkedAny++;
        }
        Assert.True(checkedAny > 50);
        Assert.True(reachedLevelSocket, "at least one device chain should lead back to a level-placed socket");
    }

    [Fact]
    public void Inspector_lists_connections_with_navigation_into_other_save_files()
    {
        if (World.Value is not { } world) return;
        var link = world.Graph.LinksOfSocket(Office1Socket2).SingleOrDefault();
        if (link is null) return;

        var socketView = PowerConnectionInspector.Inspect(world.Graph, Office1Socket2)!;
        var outgoing = Assert.Single(socketView.Connections);
        Assert.True(outgoing.IsOutgoing);
        Assert.Equal(HubFile, outgoing.FarEnd!.FileName);
        Assert.Equal(PowerNavigationSurface.Deployable, outgoing.FarEnd.Surface);
        Assert.Equal(Office1File, outgoing.SocketRecord.FileName);

        var deviceView = PowerConnectionInspector.Inspect(world.Graph, link.DeviceId)!;
        var incoming = Assert.Single(deviceView.Connections, c => !c.IsOutgoing && c.Link == link);
        Assert.Equal(Office1File, incoming.FarEnd!.FileName);
        Assert.Equal(PowerNavigationSurface.PowerSockets, incoming.FarEnd.Surface);
        Assert.Equal(PredictedPowerState.Unknown, deviceView.Prediction.State);
        Assert.Equal(PowerLiveSource.None, deviceView.Live.Source);
    }

    [Fact]
    public void Live_overlay_is_a_separate_type_and_defaults_to_unobserved()
    {
        if (World.Value is not { } world) return;
        Assert.Equal(PowerLiveState.Unobserved, PowerGraph.LiveStateOf(Office1Socket2, null));

        var directory = new LivePowerSocketDirectory(
            [new LivePowerSocket("obj", "label", Office1Socket2, "cable", null, null, true, 0, 0, 0)], true);
        var overlay = PowerLiveOverlay.From(directory);
        var state = PowerGraph.LiveStateOf(Office1Socket2, overlay);
        Assert.True(state.Powered);
        Assert.Equal(PowerLiveSource.LiveGame, state.Source);
        Assert.Null(PowerGraph.LiveStateOf("some other socket", overlay).Powered);
    }

    // ---- synthetic graphs: shapes no fixture contains ----

    private static string Guid32(char c) => new(c, 32);

    [Fact]
    public void Loops_are_reported_and_traces_still_end()
    {
        string a = Guid32('A'), b = Guid32('B');
        var graph = new PowerGraphBuilder().AddRecords("Synthetic.sav",
            [(a, "Deployed_PlugStrip_C", false), (b, "Deployed_PlugStrip_C", false)],
            [(a + "1", b, []), (b + "1", a, [])]).Build();

        Assert.Contains(PowerGraphValidator.Validate(graph), i => i.Kind == PowerIssueKind.LinkCycle);
        var down = graph.Trace(a, PowerTraceDirection.Downstream);
        Assert.False(down.Truncated);
        Assert.Contains(down.Hops, h => h.Outcome == PowerTraceOutcome.RevisitsEarlierNode);
        var up = graph.Trace(a, PowerTraceDirection.Upstream);
        Assert.False(up.Truncated);
    }

    [Fact]
    public void Missing_owner_missing_device_self_link_and_extra_list_are_all_reported()
    {
        string a = Guid32('A'), ghostOwner = Guid32('C'), ghostDevice = Guid32('D');
        var graph = new PowerGraphBuilder().AddRecords("Synthetic.sav",
            [(a, "Deployed_PlugStrip_C", false)],
            [
                (a + "1", a, []),
                (ghostOwner + "1", ghostDevice, []),
                (a + "2", null, [a]),
                ("not-a-known-shape", null, []),
            ]).Build();

        var kinds = PowerGraphValidator.Validate(graph).Select(i => i.Kind).ToHashSet();
        Assert.Contains(PowerIssueKind.SelfLink, kinds);
        Assert.Contains(PowerIssueKind.SocketOwnerMissing, kinds);
        Assert.Contains(PowerIssueKind.LinkedDeviceMissing, kinds);
        Assert.Contains(PowerIssueKind.ExtraPoweredDevicesUsed, kinds);
        Assert.Contains(PowerIssueKind.UnrecognizedSocketKey, kinds);
        Assert.Equal(PowerEndpointStatus.MissingInSuppliedSaves, graph.FindSocket(ghostOwner + "1")!.OwnerStatus);
    }

    [Fact]
    public void A_group_with_no_level_socket_is_reported_as_a_disconnected_branch_and_an_anchored_one_is_not()
    {
        string bat = Guid32('1'), strip = Guid32('2'), lone = Guid32('3'), loneTarget = Guid32('4');
        const string levelKey = "/Game/Maps/X.X:PersistentLevel.PowerSocket_ParentBP_C_0";
        var graph = new PowerGraphBuilder().AddRecords("Synthetic.sav",
            [
                (bat, "Deployed_Battery_T2_C", false), (strip, "Deployed_PlugStrip_C", false),
                (lone, "Deployed_PlugStrip_C", false), (loneTarget, "Deployed_CraftingBench_Default_C", true),
            ],
            [(levelKey, bat, []), (bat + "1", strip, []), (lone + "1", loneTarget, [])]).Build();

        var branch = Assert.Single(PowerGraphValidator.Validate(graph), i => i.Kind == PowerIssueKind.DisconnectedBranch);
        Assert.Contains(lone, branch.SubjectIds);
        Assert.DoesNotContain(bat, branch.SubjectIds);
        Assert.Equal(PredictedPowerState.Unknown, PowerGraph.Predict(bat).State);
    }
}
