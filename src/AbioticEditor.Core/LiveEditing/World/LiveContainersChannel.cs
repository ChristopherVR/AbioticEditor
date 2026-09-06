using AbioticEditor.Core.LiveEditing.Player;

namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Live world-container editing: lists every storage crate, locker and cabinet currently loaded
/// (anything deriving from <c>Deployed_Container_ParentBP</c>) with its slots and world
/// position, and lets a host set or clear a slot - see <c>containers.list</c>/
/// <c>containers.set</c> in <c>live-agent/AbioticEditorLiveAgentLua/Scripts/main.lua</c>. A
/// container's inventory is the same component class as the player's backpack, so the slot
/// shape (and the mod-side write) is shared with <see cref="LiveInventoryChannel"/>.
/// </summary>
public sealed class LiveContainersChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveContainerDirectory> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<DirectoryWire>("containers.list", payload: null, cancellationToken)
            .ConfigureAwait(false);
        var containers = (wire.Containers ?? [])
            .Select(c => new LiveContainer(c.Id, c.Label, c.X, c.Y, c.Z,
                (c.Slots ?? []).Select(s => new LiveContainerSlot(s.SlotIndex, s.ItemId, s.IsEmpty,
                    s.Stack, s.Durability, s.MaxDurability)).ToList()))
            .ToList();
        return new LiveContainerDirectory(containers, wire.IsHost);
    }

    /// <summary>Applies slot edits to the container with <paramref name="containerId"/> immediately.</summary>
    public Task SetAsync(string containerId, IReadOnlyList<LiveContainerSlotEdit> edits,
        CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("containers.set",
            new SetWire(containerId, edits.Select(e => new EditWire(
                e.SlotIndex, e.Clear, e.ItemId, e.Stack, e.Durability, e.MaxDurability)).ToList()),
            cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<ContainerWire>? Containers, bool IsHost);
    private sealed record ContainerWire(string Id, string Label, double X, double Y, double Z, IReadOnlyList<SlotWire>? Slots);
    private sealed record SlotWire(int SlotIndex, string ItemId, bool IsEmpty, int Stack, double Durability, double MaxDurability);
    private sealed record SetWire(string Id, IReadOnlyList<EditWire> Edits);
    private sealed record EditWire(int SlotIndex, bool? Clear, string? ItemId, int? Stack, double? Durability, double? MaxDurability);
}

/// <summary>One loaded container. <paramref name="Id"/> is the game's full object name for this
/// exact actor; <paramref name="Label"/> is its class name (e.g. <c>Deployed_StorageCrate_Makeshift_C</c>).</summary>
public sealed record LiveContainer(string Id, string Label, double X, double Y, double Z, IReadOnlyList<LiveContainerSlot> Slots)
{
    public int OccupiedCount => Slots.Count(s => !s.IsEmpty);
}

/// <summary>One container slot, the same shape as a player inventory slot.</summary>
public sealed record LiveContainerSlot(int SlotIndex, string ItemId, bool IsEmpty, int Stack, double Durability, double MaxDurability);

/// <summary>Every loaded container plus whether this process has host authority to change them.</summary>
public sealed record LiveContainerDirectory(IReadOnlyList<LiveContainer> Containers, bool IsHost);

/// <summary>One slot edit; a null field is left untouched, <paramref name="Clear"/> empties the slot.</summary>
public sealed record LiveContainerSlotEdit(int SlotIndex, bool? Clear = null, string? ItemId = null,
    int? Stack = null, double? Durability = null, double? MaxDurability = null);
