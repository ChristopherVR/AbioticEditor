using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

public sealed class LiveDeployedCareSessionTests
{
    [Fact]
    public async Task Garden_edits_validate_ranges_send_numbers_and_refresh()
    {
        var channel = new FakeChannel();
        var session = await LiveDeployedCareSession.ConnectAsync(new(channel), "garden-plots");
        var changed = 0;
        session.Changed += () => changed++;
        Assert.False((await session.SetMapFeatureField("garden-plots", "garden", "water", "401")).Changed);
        Assert.Equal(0, channel.Writes);
        Assert.True((await session.SetMapFeatureField("garden-plots", "garden", "water", "350")).Changed);
        Assert.Equal(350, channel.LastValue.GetInt32());
        Assert.Equal("350", session.MapFeature("garden-plots")!.Entries[0].Fields[0].Value);
        Assert.Equal(1, changed);
        Assert.False((await session.SetMapFeatureField("garden-plots", "garden", "water", "350")).Changed);
        Assert.Equal(1, channel.Writes);
    }

    [Fact]
    public async Task Client_cannot_write_and_unloaded_entries_do_not_send_requests()
    {
        var channel = new FakeChannel { Host = false };
        var session = await LiveDeployedCareSession.ConnectAsync(new(channel), "garden-plots");
        Assert.All(session.MapFeature("garden-plots")!.Entries[0].Fields, f => Assert.False(f.Editable));
        Assert.False((await session.SetMapFeatureField("garden-plots", "garden", "water", "12")).Changed);
        channel.Host = true;
        await session.RefreshAsync();
        Assert.False((await session.SetMapFeatureField("garden-plots", "missing", "water", "12")).Changed);
        Assert.Equal(0, channel.Writes);
    }

    [Fact]
    public async Task Failed_write_keeps_last_observed_state()
    {
        var channel = new FakeChannel { FailWrite = true };
        var session = await LiveDeployedCareSession.ConnectAsync(new(channel), "garden-plots");
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetMapFeatureField("garden-plots", "garden", "water", "12"));
        Assert.Equal("10", session.MapFeature("garden-plots")!.Entries[0].Fields[0].Value);
    }

    private sealed class FakeChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public bool Host { get; set; } = true;
        public bool FailWrite { get; init; }
        public int Writes { get; private set; }
        public JsonElement LastValue { get; private set; }
        private string _water = "10";
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<T> RequestAsync<T>(string command, object? payload = null, CancellationToken cancellationToken = default)
        {
            if (command == "care.list")
            {
                var response = new LiveCareDirectory([new("garden", "Garden plot", [new("water", "Water", _water, "integer", true, Maximum: 400)])], Host);
                return Task.FromResult((T)(object)response);
            }
            Assert.Equal("care.set", command);
            if (FailWrite) throw new InvalidOperationException("Rejected by game");
            var wire = JsonSerializer.SerializeToElement(payload, JsonOptions);
            Assert.Equal("garden-plots", wire.GetProperty("featureId").GetString());
            LastValue = wire.GetProperty("value");
            _water = LastValue.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
            Writes++;
            return Task.FromResult(default(T)!);
        }
    }
}
