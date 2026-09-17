using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s ground-item slice: implements
/// the same <see cref="IWorldDroppedItemsSession"/> boundary the shared
/// <c>WorldDroppedItemsTab</c> widget binds to, so that widget needs zero changes to work
/// against a running game instead of a loaded file. Only listing and removing are real live
/// operations (<c>dropped.remove</c> despawns immediately and cannot be undone); the file-only
/// members (restore, count/no-despawn edit, add) throw rather than pretend to work - see
/// <see cref="AppliesImmediately"/> on the interface, which the tab checks before ever calling
/// them.
/// </summary>
public sealed class LiveDroppedItemsSession : IWorldDroppedItemsSession
{
    private readonly LiveDroppedItemsChannel _channel;

    private LiveDroppedItemsSession(LiveDroppedItemsChannel channel, LiveDroppedItemDirectory directory)
    {
        _channel = channel;
        DroppedItems = ToWorldDroppedItems(directory.Items);
        IsHost = directory.IsHost;
    }

    public static async Task<LiveDroppedItemsSession> ConnectAsync(
        LiveDroppedItemsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveDroppedItemsSession(channel, directory);
    }

    public IReadOnlyList<WorldDroppedItem> DroppedItems { get; private set; }
    public bool CanEditDroppedItems => true;
    public bool AppliesImmediately => true;
    public bool IsHost { get; private set; }
    public string? Status { get; private set; }

    /// <summary>Always false: a live remove/add already reached the running game by the time it
    /// returns, so there is never a client-side staged copy of ground items.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world and after every
    /// mutation (each of which already ends by refreshing).</summary>
    public event Action? Changed;

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)), which requires a true no-parameter method -
    // the optional parameter below does not count. Without this the loop silently never refreshes
    // this session, so an item dropped in the running game never shows up here on its own.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        DroppedItems = ToWorldDroppedItems(directory.Items);
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    public async Task RemoveDroppedItemAsync(string id, CancellationToken cancellationToken = default)
    {
        var removed = await _channel.RemoveAsync([id], cancellationToken).ConfigureAwait(false);
        Status = removed > 0
            ? "Removed from the running game."
            : "Already gone - someone else picked it up or it despawned first.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Despawns every given item in one <c>dropped.remove</c> call, then refreshes once -
    /// see <see cref="IWorldDroppedItemsSession.RemoveDroppedItemsAsync"/>'s remarks for why this
    /// exists instead of relying on the default one-at-a-time loop.</summary>
    public async Task RemoveDroppedItemsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids as IReadOnlyList<string> ?? ids.ToArray();
        if (idList.Count == 0) return;
        var removed = await _channel.RemoveAsync(idList, cancellationToken).ConfigureAwait(false);
        Status = removed > 0
            ? $"Removed {removed} of {idList.Count} from the running game."
            : "Already gone - someone else picked it up or it despawned first.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>No live equivalent - see the class remarks. The shared tab only shows the
    /// count/no-despawn edit affordance when <see cref="AppliesImmediately"/> is false, so this
    /// is never expected to be called; it throws rather than silently no-op.</summary>
    public void SetDroppedItem(string id, int count, bool noDespawn)
        => throw new NotSupportedException("Ground-item count and despawn-timer edits are not available while editing live.");

    /// <summary>No live equivalent: a live despawn cannot be undone. See the class remarks.</summary>
    public bool RestoreDroppedItem(WorldDroppedItem item)
        => throw new NotSupportedException("A live-removed ground item cannot be restored.");

    /// <summary>File-only, explicit-position add - a live session uses
    /// <see cref="AddDroppedItemLiveAsync"/> instead (round 77, see the interface remarks).</summary>
    public bool TryAddDroppedItem(InventoryItemSlot slot, double x, double y, double z, out string pendingId)
        => throw new NotSupportedException("Adding a ground item this way is only available while editing a save file.");

    public bool SupportsLiveAdd => true;

    public async Task AddDroppedItemLiveAsync(string itemId, int stack, CancellationToken cancellationToken = default)
    {
        await _channel.AddAsync(itemId, stack, cancellationToken).ConfigureAwait(false);
        Status = "Spawned on the ground near the player - this took effect in the running game immediately.";
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>No live equivalent: there is no live despawn-timer bulk toggle.</summary>
    public void SetAllDroppedNoDespawn(bool noDespawn)
        => throw new NotSupportedException("Ground-item despawn-timer edits are not available while editing live.");

    /// <summary>Maps the live wire shape onto the same <see cref="WorldDroppedItem"/>/
    /// <see cref="InventoryItemSlot"/> domain records the file editor uses, so the shared tab's
    /// display and icon lookups work unchanged. <see cref="WorldDroppedItem.NoDespawn"/> is
    /// always false: the live protocol does not report it, only lets the file editor set it.</summary>
    private static WorldDroppedItem[] ToWorldDroppedItems(IReadOnlyList<LiveDroppedItem> items)
        => items.Select(i => new WorldDroppedItem(
            i.Id,
            new InventoryItemSlot(0, i.ItemId, i.Stack, Durability: 0, MaxDurability: 0,
                AmmoInMagazine: 0, LiquidLevel: 0, LiquidType: null, DynamicState: false,
                PlayerMadeString: null, AssetId: null),
            NoDespawn: false, i.X, i.Y, i.Z)).ToArray();
}
