using AbioticEditor.Core.Items;

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

    /// <summary>Despawns the given items immediately; returns how many were actually found and
    /// confirmed destroyed, and how many ran their despawn yet stayed standing (see
    /// <see cref="LiveDroppedRemoveResult"/>).</summary>
    public async Task<LiveDroppedRemoveResult> RemoveAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<RemovedWire>("dropped.remove", new RemoveWire(ids), cancellationToken)
            .ConfigureAwait(false);
        return new LiveDroppedRemoveResult(wire.Removed, wire.Stuck);
    }

    /// <summary>
    /// Spawns <paramref name="itemId"/> (stack <paramref name="stack"/>) on the ground near the
    /// local player - see <c>dropped.add</c> in <c>main.lua</c> for exactly how (a scratch
    /// inventory slot plus the character's own <c>Request_DropInventorySlot</c> RPC, since no
    /// direct "spawn a dropped item" function has any precedent). Host only.
    /// <paramref name="x"/>/<paramref name="y"/>/<paramref name="z"/> (round 111, all three or
    /// none) ask the Lua module to move the just-dropped item there afterwards with
    /// <c>K2_TeleportTo</c> and read its position back to confirm; omitted, the item lands
    /// wherever the game's own drop logic puts it, same as before. A position request that the
    /// game could not honor (an ambiguous new-actor match, a stack merge, a failed move, or a
    /// mismatched final position) fails the whole call rather than silently leaving the item
    /// somewhere else - see <c>dropped.add</c>'s own header comment for exactly what is checked
    /// and why <c>SpawnItem</c> (the game mode's own item-spawn function) was rejected in favor of
    /// moving the RPC's own actor.
    /// </summary>
    public Task AddAsync(string itemId, int stack, double? x = null, double? y = null, double? z = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("dropped.add", new AddWire(itemId, stack, ItemTableIndex.TableRefFor(itemId), x, y, z), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<ItemWire>? Items, bool IsHost);
    private sealed record ItemWire(string Id, string ItemId, int Stack, double X, double Y, double Z);
    private sealed record RemoveWire(IReadOnlyList<string> Ids);
    // Stuck is absent (0) from a Lua bundle older than round 91, which only ever counted Removed.
    private sealed record RemovedWire(int Removed, int Stuck = 0);
    private sealed record AddWire(string ItemId, int Stack, string? DataTable, double? X, double? Y, double? Z);
}

/// <summary>One loose item in the world. <paramref name="Id"/> is the game's full object name
/// for this exact actor; <paramref name="ItemId"/> is its item row (e.g. <c>scrap_metal</c>).</summary>
public sealed record LiveDroppedItem(string Id, string ItemId, int Stack, double X, double Y, double Z);

/// <summary>What one <c>dropped.remove</c> did. <paramref name="Removed"/> items were found and
/// are now being destroyed by the engine. <paramref name="Stuck"/> items ran their despawn without
/// error yet still report themselves as not being destroyed - the game kept them, so the list
/// will keep showing them and the player should be told rather than shown a success.</summary>
public sealed record LiveDroppedRemoveResult(int Removed, int Stuck);

/// <summary>Every loose item plus whether this process has host authority to remove them.</summary>
public sealed record LiveDroppedItemDirectory(IReadOnlyList<LiveDroppedItem> Items, bool IsHost);
