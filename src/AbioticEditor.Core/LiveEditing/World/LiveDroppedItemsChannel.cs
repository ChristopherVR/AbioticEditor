namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live dropped-item listing and removal: every item lying loose in the loaded world (anything
/// the game spawned as <c>Abiotic_Item_Dropped</c> that nobody has picked up), and a host-only
/// despawn - see <c>dropped.list</c>/<c>dropped.remove</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/main.lua</c>, which is the reference mod's
/// own "destroy all dropped items" command applied to chosen items instead of all of them.
/// </summary>
public sealed class LiveDroppedItemsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveDroppedItemDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("dropped.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        return new LiveDroppedItemDirectory(
            (wire.Items ?? []).Select(i => new LiveDroppedItem(i.Id, i.ItemId, i.Stack, i.X, i.Y, i.Z)).ToList(),
            wire.IsHost);
    }

    /// <summary>Despawns the given items immediately; returns how many were actually found and removed.</summary>
    public async Task<int> RemoveAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<RemovedWire>("dropped.remove", new RemoveWire(ids), cancellationToken)
            .ConfigureAwait(false);
        return wire.Removed;
    }

    private sealed record DirectoryWire(IReadOnlyList<ItemWire>? Items, bool IsHost);
    private sealed record ItemWire(string Id, string ItemId, int Stack, double X, double Y, double Z);
    private sealed record RemoveWire(IReadOnlyList<string> Ids);
    private sealed record RemovedWire(int Removed);
}

/// <summary>One loose item in the world. <paramref name="Id"/> is the game's full object name
/// for this exact actor; <paramref name="ItemId"/> is its item row (e.g. <c>scrap_metal</c>).</summary>
public sealed record LiveDroppedItem(string Id, string ItemId, int Stack, double X, double Y, double Z);

/// <summary>Every loose item plus whether this process has host authority to remove them.</summary>
public sealed record LiveDroppedItemDirectory(IReadOnlyList<LiveDroppedItem> Items, bool IsHost);
