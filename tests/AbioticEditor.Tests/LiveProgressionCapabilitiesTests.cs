using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

public sealed class LiveProgressionCapabilitiesTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recipe_lock_is_available_only_when_advertised(bool enabled)
    {
        var channel = new Channel { Read = new { unlockedIds = new[] { "recipe" }, canLock = enabled } };
        var session = await LivePlayerRecipesSession.ConnectAsync(new(channel));
        Assert.Equal(enabled, session.CanLock);
        if (enabled)
        {
            await session.SetUnlockedAsync("recipe", false);
            Assert.False(Assert.Single(session.Recipes).IsUnlocked);
            Assert.Equal("recipe", channel.Write.GetProperty("lockIds")[0].GetString());
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetUnlockedAsync("recipe", false));
            Assert.True(Assert.Single(session.Recipes).IsUnlocked);
            Assert.Equal(JsonValueKind.Undefined, channel.Write.ValueKind);
        }
    }

    [Fact]
    public async Task Host_can_discover_crafted_items_and_clear_a_known_email()
    {
        var channel = new Channel { Read = new { canDiscoverCrafted = true } };
        var general = await LivePlayerGeneralSession.ConnectAsync(new(channel));
        Assert.True(general.ItemsCrafted.CanDiscoverAll);
        await general.ItemsCrafted.DiscoverAllAsync(["crafted", "crafted"]);
        Assert.Equal("crafted", Assert.Single(general.ItemsCrafted.Known));
        Assert.Single(channel.Write.GetProperty("itemsCrafted").EnumerateArray());

        channel.Read = new { emails = new[] { "email" }, canUnsetKnown = true };
        var codex = await LivePlayerCodexSession.ConnectAsync(new(channel));
        Assert.True(codex.CanUnsetKnown);
        var row = Assert.Single(codex.Emails);
        await codex.SetKnownAsync(row, false);
        Assert.False(row.IsKnown);
        Assert.Equal("emails", channel.Write.GetProperty("clear").GetProperty("section").GetString());
    }

    [Fact]
    public async Task World_recipe_edits_use_capability_batching_and_authoritative_readback()
    {
        var channel = new Channel { Read = new { isHost = true, canEditRecipes = true, recipesUnlocked = new[] { "old" } } };
        // Match the interface dispatch used by the shared story tab.
#pragma warning disable CA1859
        IWorldStorySession session = await LiveStorySession.ConnectAsync(new(channel), new(channel), new(channel), new(channel));
#pragma warning restore CA1859
        Assert.True(session.CanEditGlobalRecipes);
        await session.SetGlobalRecipesAsync(["new", "new", ""], true);
        Assert.Single(channel.Write.GetProperty("recipes").EnumerateArray());
        Assert.Equal("new", channel.Write.GetProperty("recipes")[0].GetProperty("id").GetString());
        Assert.Equal("old", Assert.Single(session.GlobalRecipeIds));

        var unsupportedChannel = new Channel { Read = new { isHost = true } };
        var unsupported = await LiveStorySession.ConnectAsync(new(unsupportedChannel), new(unsupportedChannel), new(unsupportedChannel), new(unsupportedChannel));
        Assert.False(unsupported.CanEditGlobalRecipes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unsupported.SetGlobalRecipesAsync(["new"], true));
        Assert.Equal(JsonValueKind.Undefined, unsupportedChannel.Write.ValueKind);
    }

    [Fact]
    public async Task Unchanged_codex_poll_preserves_rows_and_updates_capabilities()
    {
        var channel = new Channel { Read = new { emails = new[] { "email" }, canUnsetKnown = false } };
        var codex = await LivePlayerCodexSession.ConnectAsync(new(channel));
        var rows = codex.Emails;
        var row = Assert.Single(rows);
        channel.Read = new { emails = new[] { "email" }, canUnsetKnown = true };
        await codex.RefreshAsync();
        Assert.Same(rows, codex.Emails);
        Assert.Same(row, Assert.Single(codex.Emails));
        Assert.True(codex.CanUnsetKnown);

        channel.Read = new { emails = new[] { "email", "new" }, canUnsetKnown = true };
        await codex.RefreshAsync();
        Assert.Equal(2, codex.Emails.Count);
        Assert.Contains(codex.Emails, entry => entry.Id == "new" && entry.IsKnown);
    }

    private sealed class Channel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
        public object Read { get; set; } = new { };
        public JsonElement Write { get; private set; }
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<T> RequestAsync<T>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            if (command.EndsWith(".get", StringComparison.Ordinal))
                return Task.FromResult(JsonSerializer.SerializeToElement(Read, Options).Deserialize<T>(Options)!);
            Write = JsonSerializer.SerializeToElement(payload, Options);
            return Task.FromResult(default(T)!);
        }
    }
}
