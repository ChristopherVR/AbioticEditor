using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

public sealed class LiveTraitEditingTests
{
    [Fact]
    public async Task Trait_edits_pass_buff_mapping_and_refresh_authoritative_traits()
    {
        var channel = new Channel { CanEdit = true };
        var session = await LivePlayerGeneralSession.ConnectAsync(new(channel), "player-two");
        Assert.True(session.CanEditTraits);
        await session.SetTraitAsync("Trait_Chef", true, "Buff_Trait_Chef");
        Assert.Equal("general.trait.set", channel.Command);
        Assert.Equal("player-two", channel.Write.GetProperty("playerId").GetString());
        Assert.Equal("Buff_Trait_Chef", channel.Write.GetProperty("buffRowName").GetString());
        Assert.True(channel.Write.GetProperty("enabled").GetBoolean());
        Assert.Equal("Trait_Chef", Assert.Single(session.Traits));
    }

    [Fact]
    public async Task Missing_capability_or_catalog_mapping_cannot_write()
    {
        var channel = new Channel();
        var session = await LivePlayerGeneralSession.ConnectAsync(new(channel));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetTraitAsync("Trait_Chef", true, "Buff_Trait_Chef"));
        channel.CanEdit = true;
        await session.RefreshAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetTraitAsync("Trait_Chef", true));
        Assert.Null(channel.Command);
    }

    private sealed class Channel : ILiveGameChannel
    {
        private static readonly string[] Traits = ["Trait_Chef"];
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public bool CanEdit { get; set; }
        public string? Command { get; private set; }
        public JsonElement Write { get; private set; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<T> RequestAsync<T>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            if (command == "general.get")
                return Task.FromResult(JsonSerializer.SerializeToElement(new {canEditTraits = CanEdit,
                    traits = Command is null ? Array.Empty<string>() : Traits}, Options).Deserialize<T>(Options)!);
            Command = command;
            Write = JsonSerializer.SerializeToElement(payload, Options);
            return Task.FromResult(default(T)!);
        }
    }
}
