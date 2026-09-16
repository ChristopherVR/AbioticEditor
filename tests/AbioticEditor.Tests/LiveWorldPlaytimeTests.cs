using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;

namespace AbioticEditor.Tests;

public sealed class LiveWorldPlaytimeTests
{
    [Fact]
    public async Task Old_agents_do_not_advertise_playtime_and_new_agents_use_a_distinct_write_command()
    {
        var fake = new FakeChannel();
        var channel = new LiveWorldStateChannel(fake);
        var oldState = await channel.GetAsync();
        Assert.Null(oldState.MinutesPassed);
        Assert.False(oldState.CanSetMinutesPassed);
        fake.Playtime = 123;
        var state = await channel.GetAsync();
        Assert.Equal(123, state.MinutesPassed);
        Assert.True(state.CanSetMinutesPassed);
        await channel.SetMinutesPassedAsync(0);
        Assert.Equal("world.setPlaytime", fake.LastCommand);
        Assert.Equal(0, fake.LastPayload.GetProperty("minutesPassed").GetInt32());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => channel.SetMinutesPassedAsync(-1));
    }

    private sealed class FakeChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public int? Playtime { get; set; }
        public string? LastCommand { get; private set; }
        public JsonElement LastPayload { get; private set; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<T> RequestAsync<T>(string command, object? payload = null, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            LastPayload = JsonSerializer.SerializeToElement(payload, JsonOptions);
            if (command != "world.get") return Task.FromResult(default(T)!);
            var response = new { day = 1, timeSeconds = 0, isHost = true, minutesPassed = Playtime, canSetMinutesPassed = Playtime.HasValue };
            return Task.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(response, JsonOptions), JsonOptions)!);
        }
    }
}
