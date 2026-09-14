using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
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
                    deployables = _deployables.Select(kv => new
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
                    }).ToList(),
                    isHost = true,
                    supportsBenchUpgrades = SupportsBenchUpgrades,
                },
                "bases.set" => ApplySet(payloadElement),
                _ => throw new LiveAgentException($"unknown command '{command}' in fake channel"),
            };
            var element = JsonSerializer.SerializeToElement(result, JsonOptions);
            return Task.FromResult(element.Deserialize<TResponse>(JsonOptions)!);
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
