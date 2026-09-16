using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

public sealed class LiveAppearanceEditingTests
{
    [Fact]
    public async Task Appearance_changes_read_back_current_player_and_save_only_on_request()
    {
        var channel = new Channel { CanSave = true };
        var playerId = "local";
        var session = new LivePlayerAppearanceSession(new(channel), () => playerId);
        await session.RefreshAsync();
        Assert.Equal("Customization_HairColor", Assert.Single(session.Fields).PropertyName);
        await session.SetAsync("Customization_HairColor", "Black");
        Assert.Equal("appearance.set", channel.Command);
        Assert.Equal("Black", Assert.Single(session.Fields).CurrentValue);
        Assert.True(session.HasProfileChanges);
        await session.SaveProfileAsync();
        Assert.Equal("appearance.save", channel.Command);
        Assert.False(session.HasProfileChanges);
        playerId = "other";
        channel.CanSave = false;
        await session.RefreshAsync();
        Assert.Equal("other", channel.PlayerId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveProfileAsync());
    }

    private sealed class Channel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        private string _color = "White";
        private bool _pending;
        public bool CanSave { get; set; }
        public string? Command { get; private set; }
        public string? PlayerId { get; private set; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<T> RequestAsync<T>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            var wire = JsonSerializer.SerializeToElement(payload, Options);
            PlayerId = wire.GetProperty("playerId").GetString();
            if (command == "appearance.get")
                return Task.FromResult(JsonSerializer.SerializeToElement(new LiveAppearanceDirectory(
                    new Dictionary<string, string> { ["Customization_HairColor"] = _color }, true, CanSave, _pending), Options).Deserialize<T>(Options)!);
            Command = command;
            if (command == "appearance.set") { _color = wire.GetProperty("rowName").GetString()!; _pending = true; }
            if (command == "appearance.save") _pending = false;
            return Task.FromResult(default(T)!);
        }
    }
}
