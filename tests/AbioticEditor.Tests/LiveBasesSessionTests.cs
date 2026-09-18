using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using Xunit;

namespace AbioticEditor.Tests;

/// <summary>
/// Exercises <see cref="LiveBasesSession"/>'s refresh surface against a fake
/// <see cref="ILiveGameChannel"/> that answers "bases.list"/"bases.set" from an in-memory table,
/// mirroring <see cref="LiveContainersSessionTests"/>'s pattern. Covers the same "keep the open
/// tab honest" requirement for the BASES area: a periodic <c>RefreshAsync()</c> call must pick up
/// a rename or upgrade install made out from under the session and announce it through
/// <see cref="LiveBasesSession.Changed"/>.
/// </summary>
public sealed class LiveBasesSessionTests
{
    [Fact]
    public async Task RefreshAsync_picks_up_a_rename_made_out_from_under_the_session_and_raises_Changed()
    {
        var channel = new FakeBasesChannel();
        channel.SetDeployable("d1", "Deployed_CraftingBench_Default_C", customName: "Old name");

        var session = await LiveBasesSession.ConnectAsync(new LiveBasesChannel(channel));

        var deployable = Assert.Single(session.Deployables);
        Assert.Equal("Old name", deployable.CustomName);

        // Renamed by someone else in the running game, without this session's involvement.
        channel.SetDeployable("d1", "Deployed_CraftingBench_Default_C", customName: "New name");

        var raised = 0;
        session.Changed += () => raised++;

        await session.RefreshAsync();

        var refreshed = Assert.Single(session.Deployables);
        Assert.Equal("d1", refreshed.Id);
        Assert.Equal("New name", refreshed.CustomName);
        Assert.Equal(1, raised);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public async Task Setting_a_custom_name_applies_immediately_and_raises_Changed()
    {
        var channel = new FakeBasesChannel();
        channel.SetDeployable("d1", "Deployed_CraftingBench_Default_C", customName: null);

        var session = await LiveBasesSession.ConnectAsync(new LiveBasesChannel(channel));

        var raised = 0;
        session.Changed += () => raised++;

        await session.SetCustomNameAsync("d1", "My bench");

        Assert.Equal(1, raised);
        Assert.Equal("My bench", session.Deployables[0].CustomName);
        Assert.Null(session.Status);
    }

    /// <summary>
    /// Round 118: a live report showed the agent's own "bases.list" reply carrying the exact same
    /// deployable id twice (bases.lua's single-class findAll sweep had no dedupe, unlike every
    /// other single-sweep area - see that file's own remarks), which used to throw straight out of
    /// <c>ToDictionary</c> inside <c>LiveBasesSession.Apply</c> with nothing on the calling side
    /// watching for it. That crash was a red herring for the actual reported symptom (a duplicate
    /// <c>@key</c> in WorldBasesTab from an unrelated cause - see that component's own remarks),
    /// but a genuinely duplicated wire id is still a live possibility this session must survive on
    /// its own, independent of whatever the Lua side does or does not dedupe.
    /// </summary>
    [Fact]
    public async Task ConnectAsync_dedupes_a_deployable_the_agent_reports_twice()
    {
        var channel = new FakeBasesChannel { DuplicateFirstEntry = true };
        channel.SetDeployable("d1", "Deployed_CraftingBench_Default_C", customName: "Bench");

        var session = await LiveBasesSession.ConnectAsync(new LiveBasesChannel(channel));

        var deployable = Assert.Single(session.Deployables);
        Assert.Equal("d1", deployable.Id);
        Assert.Equal("Bench", deployable.CustomName);

        // A later refresh replaying the same duplicate must not throw either.
        await session.RefreshAsync();
        Assert.Single(session.Deployables);
    }

    /// <summary>
    /// Round 121: a live report said the BASES tab showed deployables the player did not
    /// recognise as belonging to the region they were looking at. bases.lua's own sweep is not
    /// filtered by map path (see that file's header comment for why, and every other
    /// region-scoped area's identical shape), so the desktop app's mitigation is to show each row
    /// its own sub-level so the player can tell where it actually is - parsed from the very same
    /// <see cref="WorldDeployable.Id"/> the DOORS tab already parses for <c>WorldDoor.Id</c> via
    /// the shared <see cref="DoorIdParser"/>. This locks in that a live id (the game's own
    /// <c>GetFullName()</c> form, "ClassName /Game/Maps/X.X:PersistentLevel.Actor") parses the
    /// same way an offline map-key id does.
    /// </summary>
    [Theory]
    [InlineData(
        "Deployed_CraftingBench_Default_C /Game/Maps/Facility_MFWest.Facility_MFWest:PersistentLevel.Deployed_CraftingBench_Default_C_12",
        "Facility_MFWest")]
    [InlineData(
        "/Game/Maps/Facility.Facility:PersistentLevel.Deployed_Locker_ParentBP_C_3",
        "Facility")]
    [InlineData("not-an-actor-path", "")]
    public void WorldDeployable_SubLevel_parses_both_the_live_and_file_id_shapes(string id, string expected)
    {
        var deployable = new WorldDeployable(id, "Deployed_CraftingBench_Default_C", 0, 0, 0, false, 0);

        Assert.Equal(expected, deployable.SubLevel);
    }

    /// <summary>
    /// Backs the BASES tab's "Nearest first" sort and per-row distance (round 121) - the same
    /// straight-line-distance shape <c>WorldContainer.DistanceTo</c>/<c>WorldDroppedItem.DistanceTo</c>
    /// already use for WorldContainersTab/WorldDroppedItemsTab.
    /// </summary>
    [Fact]
    public void WorldDeployable_DistanceTo_computes_straight_line_distance()
    {
        var deployable = new WorldDeployable("id", "Deployed_CraftingBench_Default_C", 3, 4, 0, false, 0);

        // A 3-4-5 triangle from the origin - easy to verify by hand.
        Assert.Equal(5, deployable.DistanceTo(0, 0, 0), precision: 6);
        Assert.Equal(0, deployable.DistanceTo(3, 4, 0), precision: 6);
    }

    [Fact]
    public async Task Unsupported_bench_upgrades_are_unavailable_even_when_the_bench_has_upgrade_slots()
    {
        var channel = new FakeBasesChannel { SupportsBenchUpgrades = false };
        channel.SetDeployable("d1", "Deployed_CraftingBench_Default_C", null);
        IWorldBasesSession session =
            await LiveBasesSession.ConnectAsync(new LiveBasesChannel(channel));

        Assert.False(session.BenchSupportsUpgrades("d1"));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            session.SetBenchUpgradeAsync("d1", "Upgrade", true, CancellationToken.None));
    }

    private sealed class FakeBasesChannel : ILiveGameChannel
    {
        public bool SupportsBenchUpgrades { get; init; } = true;
        /// <summary>Round 118: reproduces an agent reply that lists the same deployable twice
        /// (see the dedupe test above) without needing a second distinct id in <see cref="_deployables"/>,
        /// which - being a dictionary keyed by id - cannot hold a genuine duplicate itself.</summary>
        public bool DuplicateFirstEntry { get; init; }
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Dictionary<string, DeployableState> _deployables = new(StringComparer.Ordinal);

        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void SetDeployable(string id, string className, string? customName)
            => _deployables[id] = new DeployableState(className, customName, [], SupportsUpgrades: true);

        public Task<TResponse> RequestAsync<TResponse>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            var payloadElement = payload is null ? default : JsonSerializer.SerializeToElement(payload, JsonOptions);
            object? result = command switch
            {
                "bases.list" => new
                {
                    deployables = BuildDeployableRows(),
                    isHost = true,
                    supportsBenchUpgrades = SupportsBenchUpgrades,
                },
                "bases.set" => ApplySet(payloadElement),
                _ => throw new LiveAgentException($"unknown command '{command}' in fake channel"),
            };
            var element = JsonSerializer.SerializeToElement(result, JsonOptions);
            return Task.FromResult(element.Deserialize<TResponse>(JsonOptions)!);
        }

        private List<object> BuildDeployableRows()
        {
            var rows = _deployables.Select(kv => (object)new
            {
                id = kv.Key,
                className = kv.Value.ClassName,
                x = 0d,
                y = 0d,
                z = 0d,
                customName = kv.Value.CustomName,
                hasInventory = false,
                storedItemCount = 0,
                supportsUpgrades = kv.Value.SupportsUpgrades,
                installedUpgrades = kv.Value.InstalledUpgrades,
            }).ToList();
            if (DuplicateFirstEntry && rows.Count > 0) rows.Add(rows[0]);
            return rows;
        }

        private object? ApplySet(JsonElement payload)
        {
            var id = payload.GetProperty("id").GetString()!;
            if (!_deployables.TryGetValue(id, out var current)) return null;
            var customName = payload.TryGetProperty("customName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                ? nameProp.GetString()
                : current.CustomName;
            var upgrades = current.InstalledUpgrades;
            if (payload.TryGetProperty("upgradeRow", out var rowProp) && rowProp.ValueKind == JsonValueKind.String
                && payload.TryGetProperty("upgradeInstalled", out var installedProp) && installedProp.ValueKind == JsonValueKind.True)
            {
                upgrades = upgrades.Append(rowProp.GetString()!).ToList();
            }
            _deployables[id] = current with { CustomName = customName, InstalledUpgrades = upgrades };
            return null;
        }

        private sealed record DeployableState(string ClassName, string? CustomName, IReadOnlyList<string> InstalledUpgrades, bool SupportsUpgrades);
    }
}
