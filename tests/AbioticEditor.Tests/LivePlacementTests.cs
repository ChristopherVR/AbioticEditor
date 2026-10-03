using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

public sealed class LivePlacementTests
{
    [Fact]
    public async Task New_objects_get_unique_identity_and_session_refreshes_after_placement_and_move()
    {
        var channel = new Channel();
        var session = await LiveBasesSession.ConnectAsync(new LiveBasesChannel(channel));
        Assert.Single(session.PlacementDonors);
        Assert.Equal("spawned", await session.PlaceAsync("donor", 100, 200, 300, 45));
        var first = channel.LastPayload.GetProperty("assetId").GetString();
        Assert.True(Guid.TryParseExact(first, "N", out _));
        Assert.Equal(100, session.PositionFor("spawned")!.Value.X);
        await session.PlaceAsync("donor", 400, 500, 600, 0);
        Assert.NotEqual(first, channel.LastPayload.GetProperty("assetId").GetString());
        await session.MoveAsync("spawned", 700, 800, 900);
        Assert.Equal(700, session.PositionFor("spawned")!.Value.X);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Missing_host_capability_or_player_ownership_blocks_writes(bool host, bool capability, bool built)
    {
        var channel = new Channel { Host = host, Capability = capability, Built = built };
        var session = await LiveBasesSession.ConnectAsync(new LiveBasesChannel(channel));
        await Assert.ThrowsAsync<NotSupportedException>(() => session.PlaceAsync("donor", 0, 0, 0, 0));
        await Assert.ThrowsAsync<NotSupportedException>(() => session.MoveAsync("donor", 0, 0, 0));
        Assert.Equal(0, channel.Writes);
    }

    private sealed class Channel : ILiveGameChannel
    {
        public bool Host { get; init; } = true;
        public bool Capability { get; init; } = true;
        public bool Built { get; init; } = true;
        public int Writes { get; private set; }
        public JsonElement LastPayload { get; private set; }
        private object? _spawned;
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<T> RequestAsync<T>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            var donor = new { id = "donor", className = "Deployed_Locker_ParentBP_C", deployedByPlayer = Built };
            object? reply;
            if (command == "bases.list") reply = new { deployables = _spawned is null ? new object[] { donor } : [donor, _spawned], isHost = Host, supportsPlacement = Capability };
            else
            {
                Writes++;
                LastPayload = JsonSerializer.SerializeToElement(payload, Options);
                _spawned = new { id = "spawned", className = "Deployed_Locker_ParentBP_C", deployedByPlayer = true,
                    x = LastPayload.GetProperty("x").GetDouble(), y = LastPayload.GetProperty("y").GetDouble(), z = LastPayload.GetProperty("z").GetDouble() };
                reply = command == "bases.spawn" ? new { id = "spawned" } : null;
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(reply, Options).Deserialize<T>(Options)!);
        }
    }
}
