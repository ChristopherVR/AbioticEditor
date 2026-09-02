namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Live player inventory editing: lists every slot across the backpack, equipment, and hotbar
/// inventories for a connected player, and lets you set or clear a slot's item, stack size, and
/// durability - see <c>inventory.list</c>/<c>inventory.set</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/main.lua</c> for the mod-side implementation.
///
/// This is the one live-editing area built on source that is real but not confirmed EXERCISED by
/// any shipped, enabled command in the reference mod it was copied from (the field names and
/// getters are exact, hash-suffixed matches against real source - see the Lua file's own comment
/// for the full caveat). Built and tested live anyway given the low blast-radius of a direct
/// single-slot field write, but worth knowing if something here behaves unexpectedly.
/// </summary>
public sealed class LiveInventoryChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>Reads every backpack/equipment/hotbar slot for <paramref name="playerId"/> (as
    /// listed by <see cref="LivePlayerDirectoryChannel"/>), or the local player when omitted.</summary>
    public async Task<IReadOnlyList<LiveInventorySlot>> GetAsync(
        string? playerId = null, CancellationToken cancellationToken = default)
    {
        object? payload = playerId is null ? null : new PlayerIdWire(playerId);
        var wire = await _channel.RequestAsync<IReadOnlyList<SlotWire>>("inventory.list", payload, cancellationToken)
            .ConfigureAwait(false);
        return wire.Select(s => new LiveInventorySlot(
            s.Kind, s.SlotIndex, s.ItemId, s.IsEmpty, s.Stack, s.Durability, s.MaxDurability)).ToList();
    }

    /// <summary>Applies edits to one or more slots immediately (or the local player's inventory
    /// when <paramref name="playerId"/> is omitted).</summary>
    public Task SetAsync(IReadOnlyList<LiveInventoryEdit> edits, string? playerId = null,
        CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("inventory.set",
            new SetWire(edits.Select(e => new EditWire(
                e.Kind, e.SlotIndex, e.Clear, e.ItemId, e.Stack, e.Durability, e.MaxDurability)).ToList(), playerId),
            cancellationToken);

    private sealed record PlayerIdWire(string PlayerId);
    private sealed record SlotWire(string Kind, int SlotIndex, string ItemId, bool IsEmpty,
        int Stack, double Durability, double MaxDurability);
    private sealed record SetWire(IReadOnlyList<EditWire> Edits, string? PlayerId);
    private sealed record EditWire(string Kind, int SlotIndex, bool? Clear, string? ItemId,
        int? Stack, double? Durability, double? MaxDurability);
}

/// <summary>One inventory slot, as listed by <see cref="LiveInventoryChannel.GetAsync"/>.</summary>
/// <param name="Kind">Which inventory this slot belongs to: <c>backpack</c>, <c>equip</c>, or
/// <c>hotbar</c>.</param>
/// <param name="SlotIndex">0-based index within that inventory - pass this back in a
/// <see cref="LiveInventoryEdit"/> to target this exact slot.</param>
/// <param name="ItemId">The item's data-table row id, or empty when the slot has no item.</param>
public sealed record LiveInventorySlot(string Kind, int SlotIndex, string ItemId, bool IsEmpty,
    int Stack, double Durability, double MaxDurability);

/// <summary>One inventory slot edit; a null field is left untouched on that slot.
/// <paramref name="Clear"/> set true empties the slot and ignores every other field.</summary>
public sealed record LiveInventoryEdit(string Kind, int SlotIndex, bool? Clear = null,
    string? ItemId = null, int? Stack = null, double? Durability = null, double? MaxDurability = null);
