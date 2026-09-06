using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s container slice: implements
/// the same <see cref="IWorldContainersSession"/> boundary the shared <c>WorldContainersTab</c>
/// widget binds to, so that widget needs zero changes to work against a running game instead of
/// a loaded file. A live container has exactly one inventory (unlike a file container, whose
/// underlying property is an array); an edit or swap sends <c>containers.set</c> immediately,
/// then the container list is re-read so what is on screen stays honest - there is no local
/// "staged until Save" backup the way a file session has.
/// </summary>
public sealed class LiveContainersSession : IWorldContainersSession
{
    private readonly LiveContainersChannel _channel;

    private LiveContainersSession(LiveContainersChannel channel, LiveContainerDirectory directory)
    {
        _channel = channel;
        Containers = ToWorldContainers(directory.Containers);
        IsHost = directory.IsHost;
    }

    public static async Task<LiveContainersSession> ConnectAsync(
        LiveContainersChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveContainersSession(channel, directory);
    }

    public IReadOnlyList<WorldContainer> Containers { get; private set; }
    public bool CanEditContainers => Containers.Count > 0;
    /// <summary>No live equivalent to a deployable's crafting-bench flag; the sidebar's
    /// dismantle-to-bench flow simply finds nothing to offer.</summary>
    public IReadOnlyList<WorldDeployable> Deployables => [];
    public bool AppliesImmediately => true;
    public bool IsHost { get; private set; }
    public string? Status { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Containers = ToWorldContainers(directory.Containers);
        IsHost = directory.IsHost;
    }

    public bool TryGetContainerSlot(WorldContainerSource source, string id, int inventoryIndex, int slotIndex, out InventoryItemSlot slot)
    {
        slot = null!;
        if (inventoryIndex != 0) return false;
        var container = Containers.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        if (container is null || container.Inventories.Count == 0) return false;
        var slots = container.Inventories[0].Slots;
        if (slotIndex < 0 || slotIndex >= slots.Count) return false;
        slot = slots[slotIndex];
        return true;
    }

    public async Task<bool> TrySetContainerSlotAsync(WorldContainerSource source, string id, int inventoryIndex, int slotIndex, InventoryItemSlot slot, CancellationToken cancellationToken = default)
    {
        if (inventoryIndex != 0 || !TryGetContainerSlot(source, id, inventoryIndex, slotIndex, out _)) return false;
        var edit = slot.IsEmpty
            ? new LiveContainerSlotEdit(slotIndex, Clear: true)
            : new LiveContainerSlotEdit(slotIndex, ItemId: slot.ItemId, Stack: slot.Count,
                Durability: slot.Durability, MaxDurability: slot.MaxDurability);
        await ApplyAsync(id, edit, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> TrySwapContainerSlotsAsync(WorldContainerSource source, string id, int inventoryIndex, int firstIndex, int secondIndex, CancellationToken cancellationToken = default)
    {
        if (inventoryIndex != 0 || firstIndex == secondIndex
            || !TryGetContainerSlot(source, id, inventoryIndex, firstIndex, out var first)
            || !TryGetContainerSlot(source, id, inventoryIndex, secondIndex, out var second)) return false;
        // Neither send is atomic with the other on the wire, but both land before this
        // returns and the caller always refreshes off the result, so the screen never shows
        // a half-swapped state.
        return await TrySetContainerSlotAsync(source, id, inventoryIndex, firstIndex, second, cancellationToken).ConfigureAwait(false)
            && await TrySetContainerSlotAsync(source, id, inventoryIndex, secondIndex, first, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Round 77: grounded in the container's own inventory component's zero-parameter
    /// <c>SortInventory()</c> function (LiveClassPropsProbe, fragment
    /// "Abiotic_InventoryComponent") - the same reorder the in-game "sort" button performs.
    /// Not exercised by any mod before this round.
    /// </summary>
    public async Task<bool> SortContainerSlotsAsync(WorldContainerSource source, string id, int inventoryIndex, CancellationToken cancellationToken = default)
    {
        if (inventoryIndex != 0) return false;
        await _channel.SortAsync(id, cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task SetContainerSlotCountAsync(WorldContainerSource source, string id, int inventoryIndex, int slotIndex, int count, CancellationToken cancellationToken = default)
    {
        if (inventoryIndex != 0 || !TryGetContainerSlot(source, id, inventoryIndex, slotIndex, out var slot) || slot.IsEmpty)
            return Task.CompletedTask;
        return ApplyAsync(id, new LiveContainerSlotEdit(slotIndex, Stack: count), cancellationToken);
    }

    private async Task ApplyAsync(string containerId, LiveContainerSlotEdit edit, CancellationToken cancellationToken)
    {
        await _channel.SetAsync(containerId, [edit], cancellationToken).ConfigureAwait(false);
        Status = "Applied live - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Maps the live wire shape onto the same <see cref="WorldContainer"/>/
    /// <see cref="WorldInventory"/>/<see cref="InventoryItemSlot"/> domain records the file
    /// editor uses, so the shared tab's display and icon lookups work unchanged. A live
    /// container always has exactly one inventory and carries its real world position
    /// (<see cref="WorldContainerSource.Live"/> is the only source that does).</summary>
    private static WorldContainer[] ToWorldContainers(IReadOnlyList<LiveContainer> containers)
        => containers.Select(c => new WorldContainer(
            c.Id, WorldContainerSource.Live, c.Label,
            [new WorldInventory(c.Slots.Select(ToSlot).ToArray())],
            c.X, c.Y, c.Z)).ToArray();

    private static InventoryItemSlot ToSlot(LiveContainerSlot slot) => new(
        slot.SlotIndex, slot.IsEmpty ? null : slot.ItemId, slot.Stack, slot.Durability, slot.MaxDurability,
        AmmoInMagazine: 0, LiquidLevel: 0, LiquidType: null, DynamicState: false, PlayerMadeString: null, AssetId: null);
}
