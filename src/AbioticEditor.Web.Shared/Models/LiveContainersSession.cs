using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s container slice: implements
/// the same <see cref="IWorldContainersSession"/> boundary the shared <c>WorldContainersTab</c>
/// widget binds to, so that widget needs zero changes to work against a running game instead of
/// a loaded file. A live container has exactly one inventory (unlike a file container, whose
/// underlying property is an array); an edit or swap sends <c>containers.set</c> immediately,
/// then just that container is re-read (<c>containers.get</c>) so what is on screen stays honest
/// - there is no local "staged until Save" backup the way a file session has.
/// </summary>
public sealed class LiveContainersSession : IWorldContainersSession
{
    private readonly LiveContainersChannel _channel;
    private int _pendingOperations;
    private bool _supportsCompleteItemWrites;

    private LiveContainersSession(LiveContainersChannel channel, LiveContainerDirectory directory)
    {
        _channel = channel;
        Containers = ToWorldContainers(directory.Containers);
        IsHost = directory.IsHost;
        UpdateCapability(directory);
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

    /// <summary>Sticky once observed true - see <see cref="IWorldContainersSession.SupportsCompleteItemWrites"/>
    /// and <see cref="LiveInventorySession.SupportsCompleteItemWrites"/>'s remarks.</summary>
    public bool SupportsCompleteItemWrites => _supportsCompleteItemWrites;
    public string? Status { get; private set; }

    /// <summary>
    /// True while an edit (or the refresh that follows it) is in flight - see
    /// <see cref="LiveInventorySession.IsDirty"/>'s remarks for why this matters: the host's
    /// periodic background poll skips a tick while this is true, so it can never re-read the
    /// container list and hand a stale snapshot to <c>WorldContainersTab</c>'s own sync logic
    /// while a player-typed edit for a DIFFERENT, not-yet-sent slot in the same container is
    /// still sitting locally - which used to silently overwrite it before it was ever sent
    /// (reported as "changing a container item's quantity doesn't take effect").
    /// </summary>
    public bool IsDirty => Volatile.Read(ref _pendingOperations) > 0;

    /// <summary>Raised after <see cref="RefreshAsync(CancellationToken)"/> re-reads the world,
    /// after <see cref="RefreshContainerAsync"/> re-reads one container, and after every
    /// mutation (each of which already ends by refreshing the container it touched), so the tab
    /// that renders this session can redraw without needing to know which specific edit path
    /// fired.</summary>
    public event Action? Changed;

    /// <summary>
    /// The container the tab currently has open, or null. Round 91: this is what the host's
    /// periodic background tick (the zero-argument <see cref="RefreshAsync()"/>) re-reads - one
    /// container, one cheap <c>containers.get</c> - so the health and slot contents of the
    /// container being looked at follow the game every couple of seconds while every OTHER
    /// container is left alone. The full world scan is reserved for an explicit refresh.
    /// </summary>
    public string? WatchedContainerId { get; set; }

    /// <summary>
    /// The full re-read: every loaded container, every slot, a world scan on the game thread.
    /// This is what the REFRESH button and a fresh tab visit do; nothing else calls it any more
    /// (round 91) because on a built-up world it is what froze the game for a moment after every
    /// single slot write and made the whole list flicker.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        Containers = ToWorldContainers(directory.Containers);
        IsHost = directory.IsHost;
        UpdateCapability(directory);
        Changed?.Invoke();
    }

    /// <summary>
    /// The periodic tick's entry point - the zero-argument overload the host finds by reflection
    /// (see <c>LiveSessionPeriodicRefreshContractTests</c>). Deliberately NOT the world scan:
    /// it re-reads only <see cref="WatchedContainerId"/> and does nothing at all when no
    /// container is open, so leaving the CONTAINERS tab on screen costs the game one small read
    /// every couple of seconds instead of a full sweep.
    /// </summary>
    public Task RefreshAsync()
        => WatchedContainerId is { } id ? RefreshContainerAsync(id, CancellationToken.None) : Task.CompletedTask;

    /// <summary>
    /// Re-reads one container and swaps it into <see cref="Containers"/> in place (same position,
    /// nothing else touched), then raises <see cref="Changed"/>. A container the game reports as
    /// gone is dropped from the list. For a Void Chest, the freshly-read contents are mirrored
    /// onto every other Void Chest row too - they are one shared pool in the game (see
    /// <c>containerInventory</c> in <c>main.lua</c>), so a write through one is visible through
    /// all of them without a world scan.
    /// </summary>
    public async Task RefreshContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        LiveContainer fresh;
        try
        {
            fresh = await _channel.GetContainerAsync(containerId, cancellationToken).ConfigureAwait(false);
        }
        catch (LiveAgentException exception) when (exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            if (Containers.Any(c => string.Equals(c.Id, containerId, StringComparison.Ordinal)))
            {
                Containers = Containers.Where(c => !string.Equals(c.Id, containerId, StringComparison.Ordinal)).ToArray();
                if (string.Equals(WatchedContainerId, containerId, StringComparison.Ordinal)) WatchedContainerId = null;
                Changed?.Invoke();
            }
            return;
        }
        var updated = ToWorldContainer(fresh);
        var list = Containers.ToList();
        var index = list.FindIndex(c => string.Equals(c.Id, containerId, StringComparison.Ordinal));
        if (index >= 0) list[index] = updated; else list.Add(updated);
        if (IsVoidChest(updated))
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (i != index && IsVoidChest(list[i]))
                    list[i] = list[i] with { Inventories = updated.Inventories, Name = updated.Name };
            }
        }
        Containers = list.ToArray();
        if (!_supportsCompleteItemWrites && fresh.Slots.Any(s => s.Details?.InstanceMetadata is not null))
            _supportsCompleteItemWrites = true;
        Changed?.Invoke();
    }

    private static bool IsVoidChest(WorldContainer container)
        => container.ClassName?.Contains("StorageCrate_Void", StringComparison.OrdinalIgnoreCase) == true;

    private void UpdateCapability(LiveContainerDirectory directory)
    {
        if (!_supportsCompleteItemWrites
            && directory.Containers.Any(c => c.Slots.Any(s => s.Details?.InstanceMetadata is not null)))
            _supportsCompleteItemWrites = true;
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
        var edit = ToEdit(slotIndex, slot);
        await ApplyAsync(id, edit, slot, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> TrySwapContainerSlotsAsync(WorldContainerSource source, string id, int inventoryIndex, int firstIndex, int secondIndex, CancellationToken cancellationToken = default)
    {
        if (inventoryIndex != 0 || firstIndex == secondIndex
            || !TryGetContainerSlot(source, id, inventoryIndex, firstIndex, out var first)
            || !TryGetContainerSlot(source, id, inventoryIndex, secondIndex, out var second)) return false;
        Interlocked.Increment(ref _pendingOperations);
        try
        {
            await _channel.SetAsync(id, [ToEdit(firstIndex, second), ToEdit(secondIndex, first)], cancellationToken).ConfigureAwait(false);
            Status = null;
            await RefreshContainerAsync(id, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { Interlocked.Decrement(ref _pendingOperations); }
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
        Interlocked.Increment(ref _pendingOperations);
        try
        {
            await _channel.SortAsync(id, cancellationToken).ConfigureAwait(false);
            Status = null;
            await RefreshContainerAsync(id, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { Interlocked.Decrement(ref _pendingOperations); }
    }

    public Task SetContainerSlotCountAsync(WorldContainerSource source, string id, int inventoryIndex, int slotIndex, int count, CancellationToken cancellationToken = default)
    {
        if (inventoryIndex != 0 || !TryGetContainerSlot(source, id, inventoryIndex, slotIndex, out var slot) || slot.IsEmpty)
            return Task.CompletedTask;
        return ApplyAsync(id, new LiveContainerSlotEdit(slotIndex, Stack: count), slot with { Count = count }, cancellationToken);
    }

    /// <summary>Renames a live container immediately - see <c>containers.rename</c> in
    /// <c>main.lua</c>. Only <see cref="WorldContainerSource.Live"/> is ever passed here in
    /// practice, but any other source is rejected the same way the other mutators above reject a
    /// source/inventory shape that does not apply to a live session.</summary>
    public async Task<bool> TryRenameContainerAsync(WorldContainerSource source, string id, string name, CancellationToken cancellationToken = default)
    {
        if (source != WorldContainerSource.Live || Containers.All(c => !string.Equals(c.Id, id, StringComparison.Ordinal)))
            return false;
        Interlocked.Increment(ref _pendingOperations);
        try
        {
            await _channel.RenameAsync(id, name.Trim(), cancellationToken).ConfigureAwait(false);
            Status = null;
            // One re-read of the renamed container; for a Void Chest RefreshContainerAsync mirrors
            // the new name onto every other Void Chest row, which is what the game did to them.
            await RefreshContainerAsync(id, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally { Interlocked.Decrement(ref _pendingOperations); }
    }

    /// <summary>Sends one slot edit and applies it to the local model as soon as the game confirms
    /// it, instead of making the caller wait on <see cref="RefreshContainerAsync"/>'s own
    /// <c>containers.get</c> round trip too - a DELETE/clear should vanish from the grid the moment
    /// the game acknowledges it. That re-read still happens right after, in the background, purely
    /// as reconciliation against whatever the game actually did with the write.</summary>
    private async Task ApplyAsync(string containerId, LiveContainerSlotEdit edit, InventoryItemSlot resultingSlot, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _pendingOperations);
        try
        {
            await _channel.SetAsync(containerId, [edit], cancellationToken).ConfigureAwait(false);
            Status = null;
        }
        catch
        {
            Interlocked.Decrement(ref _pendingOperations);
            throw;
        }
        ApplyLocalSlotEdit(containerId, resultingSlot with { Index = edit.SlotIndex });
        Changed?.Invoke();
        _ = ReconcileContainerAsync(containerId, cancellationToken);
    }

    /// <summary>Best-effort background re-read after <see cref="ApplyAsync"/> already applied its
    /// own result to the local model and repainted. Never lets a reconciliation failure surface as
    /// an error for an edit that already succeeded.</summary>
    private async Task ReconcileContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        try { await RefreshContainerAsync(containerId, cancellationToken).ConfigureAwait(false); }
        catch { /* best-effort; the confirmed write already applied to the local model above */ }
        finally { Interlocked.Decrement(ref _pendingOperations); }
    }

    /// <summary>Replaces one slot in one container's single inventory in place, rebuilding just the
    /// touched container/inventory records (everything else in <see cref="Containers"/> keeps its
    /// existing reference). No-op if the container or slot is no longer known locally.</summary>
    private void ApplyLocalSlotEdit(string containerId, InventoryItemSlot value)
    {
        var list = Containers.ToList();
        var index = list.FindIndex(c => string.Equals(c.Id, containerId, StringComparison.Ordinal));
        if (index < 0 || list[index].Inventories.Count == 0) return;
        var inventory = list[index].Inventories[0];
        var slots = inventory.Slots.ToList();
        var slotPos = slots.FindIndex(s => s.Index == value.Index);
        if (slotPos < 0) return;
        slots[slotPos] = value;
        list[index] = list[index] with { Inventories = [inventory with { Slots = slots }] };
        Containers = list;
    }

    public async Task TransferAsync(LiveInventoryEndpoint first, LiveInventoryEndpoint second,
        LiveInventorySession? player = null, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _pendingOperations);
        try
        {
            async Task Apply()
            {
                await _channel.TransferAsync(first, second, cancellationToken).ConfigureAwait(false);
                // Only the container end(s) of the move are re-read; a player end is the player
                // session's own business (ApplyExternalTransferAsync below refreshes it).
                if (first.ContainerId is { } firstId) await RefreshContainerAsync(firstId, cancellationToken).ConfigureAwait(false);
                if (second.ContainerId is { } secondId && !string.Equals(secondId, first.ContainerId, StringComparison.Ordinal))
                    await RefreshContainerAsync(secondId, cancellationToken).ConfigureAwait(false);
            }
            if (player is null) await Apply().ConfigureAwait(false);
            else await player.ApplyExternalTransferAsync(Apply, cancellationToken).ConfigureAwait(false);
            Status = null;
        }
        finally { Interlocked.Decrement(ref _pendingOperations); }
    }

    /// <summary>Maps the live wire shape onto the same <see cref="WorldContainer"/>/
    /// <see cref="WorldInventory"/>/<see cref="InventoryItemSlot"/> domain records the file
    /// editor uses, so the shared tab's display and icon lookups work unchanged. A live
    /// container always has exactly one inventory and carries its real world position
    /// (<see cref="WorldContainerSource.Live"/> is the only source that does).</summary>
    private static WorldContainer[] ToWorldContainers(IReadOnlyList<LiveContainer> containers)
        => containers.Select(ToWorldContainer).ToArray();

    private static WorldContainer ToWorldContainer(LiveContainer c) => new(
        c.Id, WorldContainerSource.Live, c.Label,
        [new WorldInventory(c.Slots.Select(ToSlot).ToArray())],
        c.X, c.Y, c.Z, c.Health, c.MaxHealth, c.Name);

    private static InventoryItemSlot ToSlot(LiveContainerSlot slot) => new(
        slot.SlotIndex, slot.IsEmpty ? null : slot.ItemId, slot.Stack, slot.Durability, slot.MaxDurability,
        slot.AmmoInMagazine, slot.Details?.LiquidLevel ?? 0, slot.Details?.LiquidType,
        slot.Details?.DynamicState ?? false, slot.Details?.PlayerMadeString, slot.Details?.AssetId,
        slot.Details?.VariantRowName, slot.Details?.DynamicValue("WeaponCoating"),
        slot.Details?.DynamicValue("CoatingDurability"), slot.Details?.InstanceMetadata);

    private static LiveContainerSlotEdit ToEdit(int index, InventoryItemSlot slot) => slot.IsEmpty
        ? new(index, Clear: true)
        : new(index, ItemId: slot.ItemId, Stack: slot.Count, Durability: slot.Durability,
            MaxDurability: slot.MaxDurability, AmmoInMagazine: slot.AmmoInMagazine,
            Details: LiveItemDetails.FromSlot(slot));
}
