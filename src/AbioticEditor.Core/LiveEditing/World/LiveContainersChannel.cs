using AbioticEditor.Core.Items;
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
                    s.Stack, s.Durability, s.MaxDurability, s.AmmoInMagazine, s.Details)).ToList(),
                c.Health, c.MaxHealth, c.Name))
            .ToList();
        return new LiveContainerDirectory(containers, wire.IsHost);
    }

    /// <summary>Applies slot edits to the container with <paramref name="containerId"/> immediately.</summary>
    public Task SetAsync(string containerId, IReadOnlyList<LiveContainerSlotEdit> edits,
        CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>(edits.Any(edit => edit.Details?.InstanceMetadata is not null) ? "containers.setcomplete" : edits.Any(edit => edit.Details is not null) ? "containers.setfull" : "containers.set",
            new SetWire(containerId, edits.Select(e => new EditWire(
                e.SlotIndex, e.Clear, e.ItemId, e.Stack, e.Durability, e.MaxDurability, e.Details?.InstanceMetadata?.ItemDataTable ?? ItemTableIndex.TableRefFor(e.ItemId), e.AmmoInMagazine, e.Details)).ToList(), Sort: null),
            cancellationToken);

    /// <summary>
    /// Reorders a container's slots immediately via the component's own zero-parameter
    /// <c>SortInventory()</c> function (round 77 - see <c>main.lua</c>'s <c>containers.set</c>).
    /// </summary>
    public Task SortAsync(string containerId, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("containers.set", new SetWire(containerId, [], Sort: true), cancellationToken);

    /// <summary>Trades the current contents of two slots in one host operation.</summary>
    public Task TransferAsync(LiveInventoryEndpoint first, LiveInventoryEndpoint second,
        CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("inventory.transfer", new { first, second }, cancellationToken);

    /// <summary>Sets (empty string clears) the container's player-given name immediately - see
    /// <c>containers.rename</c> in <c>main.lua</c> for how this reaches every connected player,
    /// not just the host.</summary>
    public Task RenameAsync(string containerId, string name, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("containers.rename", new RenameWire(containerId, name), cancellationToken);

    private sealed record DirectoryWire(IReadOnlyList<ContainerWire>? Containers, bool IsHost);
    private sealed record ContainerWire(string Id, string Label, double X, double Y, double Z, IReadOnlyList<SlotWire>? Slots,
        double? Health = null, double? MaxHealth = null, string? Name = null);
    private sealed record RenameWire(string Id, string Name);
    private sealed record SlotWire(int SlotIndex, string ItemId, bool IsEmpty, int Stack, double Durability, double MaxDurability, int AmmoInMagazine = 0, LiveItemDetails? Details = null);
    private sealed record SetWire(string Id, IReadOnlyList<EditWire> Edits, bool? Sort);
    private sealed record EditWire(int SlotIndex, bool? Clear, string? ItemId, int? Stack, double? Durability, double? MaxDurability, string? DataTable, int? AmmoInMagazine, LiveItemDetails? Details);
}

/// <summary>One loaded container. <paramref name="Id"/> is the game's full object name for this
/// exact actor; <paramref name="Label"/> is its class name (e.g. <c>Deployed_StorageCrate_Makeshift_C</c>).
/// <paramref name="Health"/>/<paramref name="MaxHealth"/> are null for a deployable that does not
/// track durability at all (most containers do, per the game's own Blueprint data - verified),
/// not for a destroyed one (that reads 0/positive instead). <paramref name="Name"/> is the
/// player-given label (null when never set), distinct from <paramref name="Label"/>'s
/// auto-generated class name - see <c>LiveContainersChannel.RenameAsync</c>.</summary>
public sealed record LiveContainer(string Id, string Label, double X, double Y, double Z, IReadOnlyList<LiveContainerSlot> Slots,
    double? Health = null, double? MaxHealth = null, string? Name = null)
{
    public int OccupiedCount => Slots.Count(s => !s.IsEmpty);
}

/// <summary>One container slot, the same shape as a player inventory slot.</summary>
public sealed record LiveContainerSlot(int SlotIndex, string ItemId, bool IsEmpty, int Stack, double Durability, double MaxDurability, int AmmoInMagazine = 0, LiveItemDetails? Details = null);

/// <summary>Every loaded container plus whether this process has host authority to change them.</summary>
public sealed record LiveContainerDirectory(IReadOnlyList<LiveContainer> Containers, bool IsHost);

/// <summary>One slot edit; a null field is left untouched, <paramref name="Clear"/> empties the slot.</summary>
public sealed record LiveContainerSlotEdit(int SlotIndex, bool? Clear = null, string? ItemId = null,
    int? Stack = null, double? Durability = null, double? MaxDurability = null,
    int? AmmoInMagazine = null, LiveItemDetails? Details = null);

/// <summary>A live slot address. ContainerId identifies a loaded crate; otherwise Kind and PlayerId identify a player inventory.</summary>
public sealed record LiveInventoryEndpoint(int SlotIndex, string? ContainerId = null, string? Kind = null, string? PlayerId = null);
