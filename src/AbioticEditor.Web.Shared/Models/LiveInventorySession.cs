using AbioticEditor.Core.Items;
using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="PlayerSaveSession"/>'s inventory AND transmog slices:
/// implements both <see cref="IPlayerInventorySession"/> and <see cref="IPlayerTransmogSession"/>
/// so <c>PlayerInventoryTab</c> and <c>PlayerTransmogTab</c> bind to the SAME session object
/// unchanged - exactly the multi-interface pattern <see cref="PlayerSaveSession"/> already uses
/// for vitals/skills. One object has to answer to both boundaries because a slot can be dragged
/// between a backpack/equipment/hotbar slot and a transmog slot in either direction, and that
/// drag's cross-session check (<c>ReferenceEquals(source.Session, Session)</c>) only holds when
/// both tabs share one instance - <c>LiveConnect.razor</c> passes the same field to both.
///
/// Unlike the staged file session, there is no local "until SAVE" backup: every mutation here
/// (a sidebar field edit, a drag/drop swap, a sort, an upgrade) sends <c>inventory.set</c> to the
/// running game immediately, then re-reads the affected inventory so the tab reflects whatever
/// the game actually did with the write - see <see cref="AppliesImmediately"/>. All four kinds
/// (backpack/equip/hotbar/transmog) travel over the one existing <see cref="LiveInventoryChannel"/>:
/// the wire protocol already treats "kind" as an opaque string, so transmog is simply a fourth
/// value the Lua mod's INVENTORY_KINDS table now recognises, not a new command pair.
/// </summary>
public sealed class LiveInventorySession : IPlayerInventorySession, IPlayerTransmogSession
{
    private const string AppliedLiveStatus = "Applied live - this took effect in the running game immediately.";

    private readonly LiveInventoryChannel _channel;
    private string? _playerId;
    private bool _initialized;

    private LiveInventorySession(LiveInventoryChannel channel, string? playerId, ItemUpgradeCatalog itemUpgrades)
    {
        _channel = channel;
        _playerId = playerId;
        ItemUpgrades = itemUpgrades;
        Equipment = [];
        Hotbar = [];
        Backpack = [];
        Transmog = [];
        TransmogVisibility = [];
    }

    /// <summary>Connects and reads the current inventory (all four kinds) for
    /// <paramref name="playerId"/>, or the local player when omitted, to seed the session.</summary>
    public static async Task<LiveInventorySession> ConnectAsync(LiveInventoryChannel channel, string? playerId = null,
        ItemUpgradeCatalog? itemUpgrades = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var session = new LiveInventorySession(channel, playerId, itemUpgrades ?? ItemUpgradeCatalog.Empty);
        await session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    public IReadOnlyList<PlayerInventorySlotEdit> Equipment { get; private set; }
    public IReadOnlyList<PlayerInventorySlotEdit> Hotbar { get; private set; }
    public IReadOnlyList<PlayerInventorySlotEdit> Backpack { get; private set; }
    public IReadOnlyList<PlayerInventorySlotEdit> Transmog { get; private set; }

    /// <summary>
    /// No live property is confirmed for the armor-visibility toggles (see
    /// <c>docs/reference/live-editing-protocol.md</c>), so this always reports the six visual
    /// gear roles as visible; <c>PlayerTransmogTab</c> renders them disabled for a live session
    /// rather than letting an edit silently not apply.
    /// </summary>
    public IReadOnlyList<TransmogVisibilityEdit> TransmogVisibility { get; private set; }

    /// <summary>No live command exposes a discovered-item vocabulary; the sidebar palette's
    /// search still works from the full item catalog, just without this shortcut list.</summary>
    public IReadOnlyList<string> ItemVocabulary => [];

    public ItemUpgradeCatalog ItemUpgrades { get; }

    /// <summary>Never actually read: <c>PlayerInventoryTab</c> hides the money editor entirely
    /// when <see cref="AppliesImmediately"/> is true (money lives on the VITALS tab live).</summary>
    public PlayerVitals Vitals { get; } = new();

    /// <summary>Never actually read: there is no live world-drop surface for
    /// <c>PlayerInventoryTab</c>'s "on the ground nearby" panel to measure against.</summary>
    public PlayerRespawnEdit Respawn { get; } = new(0, 0, 0, null, null);

    public string? SteamIdentifier => null;

    /// <summary>Not a real file path; used only to key the file-only sibling-bench lookup, which
    /// the tab skips entirely for a live (<see cref="AppliesImmediately"/>) session.</summary>
    public string Path => $"live-inventory:{_playerId ?? "local"}";

    public string? Status { get; private set; }

    public bool AppliesImmediately => true;

    /// <summary>No-op: a live session has nothing to stage - every mutation method already set
    /// <see cref="Status"/> itself before this would run.</summary>
    public void MarkChanged() { }

    public bool TryGetInventorySlot(PlayerInventoryArea area, int index, out InventoryItemSlot slot)
    {
        var edit = FindSlot(area, index);
        if (edit is not null) { slot = edit.ToInventorySlot(); return true; }
        slot = default!;
        return false;
    }

    /// <summary>Updates only the local mirror. The two callers of this method
    /// (<c>InventoryTransferService</c>'s world-container/dropped-item transfers) never run
    /// against a live session in practice - they require a (file-only) world session that
    /// <c>LiveConnect.razor</c> never attaches - so there is nothing live to push here.</summary>
    public bool TrySetInventorySlot(PlayerInventoryArea area, int index, InventoryItemSlot slot)
    {
        var edit = FindSlot(area, index);
        if (edit is null) return false;
        edit.LoadFrom(slot with { Index = edit.Index });
        return true;
    }

    public async ValueTask PushSlotAsync(PlayerInventoryArea area, PlayerInventorySlotEdit slot, CancellationToken cancellationToken = default)
    {
        await _channel.SetAsync([ToEdit(area, slot.ToInventorySlot())], _playerId, cancellationToken).ConfigureAwait(false);
        Status = AppliedLiveStatus;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> TrySwapInventorySlotsAsync(PlayerInventoryArea firstArea, int firstIndex,
        PlayerInventoryArea secondArea, int secondIndex, CancellationToken cancellationToken = default)
    {
        var first = FindSlot(firstArea, firstIndex);
        var second = FindSlot(secondArea, secondIndex);
        if (first is null || second is null || ReferenceEquals(first, second)) return false;

        var firstValue = first.ToInventorySlot();
        var secondValue = second.ToInventorySlot();
        await _channel.SetAsync(
        [
            ToEdit(firstArea, secondValue with { Index = first.Index }),
            ToEdit(secondArea, firstValue with { Index = second.Index }),
        ], _playerId, cancellationToken).ConfigureAwait(false);
        Status = AppliedLiveStatus;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async ValueTask SortInventorySlotsAsync(PlayerInventoryArea area, CancellationToken cancellationToken = default)
    {
        var slots = SlotsFor(area);
        var ordered = slots.Select(slot => slot.ToInventorySlot())
            .OrderBy(slot => slot.IsEmpty)
            .ThenBy(slot => slot.ItemId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(slot => slot.PlayerMadeString, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var edits = new List<LiveInventoryEdit>(slots.Count);
        for (var index = 0; index < slots.Count; index++)
            edits.Add(ToEdit(area, ordered[index] with { Index = slots[index].Index }));
        await _channel.SetAsync(edits, _playerId, cancellationToken).ConfigureAwait(false);
        Status = AppliedLiveStatus;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> TryApplyItemUpgradeAsync(PlayerInventoryArea area, int index, bool downgrade, CancellationToken cancellationToken = default)
    {
        if (!TryGetInventorySlot(area, index, out var slot) || slot.IsEmpty) return false;
        var edge = downgrade ? ItemUpgrades.SourceOf(slot.ItemId) : ItemUpgrades.UpgradeFor(slot.ItemId);
        if (edge is null) return false;
        var updated = slot with { ItemId = downgrade ? edge.SourceId : edge.OutputId, AssetId = null };
        await _channel.SetAsync([ToEdit(area, updated)], _playerId, cancellationToken).ConfigureAwait(false);
        Status = AppliedLiveStatus;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Re-reads every slot from the running game. The first call (from
    /// <see cref="ConnectAsync"/>) builds the four lists; every later call updates the SAME
    /// <see cref="PlayerInventorySlotEdit"/> objects in place instead of replacing the lists, so
    /// a slot selected in the shared sidebar editor (which holds a reference to one of these
    /// objects) does not go stale the instant this session applies its own edit and refreshes.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.GetAsync(_playerId, cancellationToken).ConfigureAwait(false);
        if (!_initialized)
        {
            Equipment = Build(wire, "equip");
            Hotbar = Build(wire, "hotbar");
            Backpack = Build(wire, "backpack");
            Transmog = Build(wire, "transmog");
            TransmogVisibility = Enumerable.Range(0, 6).Select(index => new TransmogVisibilityEdit(index, true)).ToList();
            _initialized = true;
        }
        else
        {
            UpdateInPlace(Equipment, wire, "equip");
            UpdateInPlace(Hotbar, wire, "hotbar");
            UpdateInPlace(Backpack, wire, "backpack");
            UpdateInPlace(Transmog, wire, "transmog");
        }
    }

    /// <summary>Switches which connected player this session edits and rebuilds every list
    /// fresh (a different player's slots are not the same identities, so in-place reuse across
    /// a player switch would be meaningless).</summary>
    public async Task SwitchPlayerAsync(string? playerId, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        _initialized = false;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        Status = "Refreshed from the running game.";
    }

    private static List<PlayerInventorySlotEdit> Build(IReadOnlyList<LiveInventorySlot> wire, string kind)
        => wire.Where(slot => slot.Kind == kind).OrderBy(slot => slot.SlotIndex)
            .Select(slot => new PlayerInventorySlotEdit(ToInventoryItemSlot(slot))).ToList();

    private static void UpdateInPlace(IReadOnlyList<PlayerInventorySlotEdit> existing, IReadOnlyList<LiveInventorySlot> wire, string kind)
    {
        foreach (var slot in wire)
        {
            if (slot.Kind != kind) continue;
            var target = existing.FirstOrDefault(edit => edit.Index == slot.SlotIndex);
            target?.LoadFrom(ToInventoryItemSlot(slot));
        }
    }

    private static InventoryItemSlot ToInventoryItemSlot(LiveInventorySlot slot) => new(
        slot.SlotIndex, slot.IsEmpty ? PlayerSaveWriter.EmptySlotRowName : slot.ItemId,
        slot.Stack, slot.Durability, slot.MaxDurability, AmmoInMagazine: 0, LiquidLevel: 0,
        LiquidType: null, DynamicState: false, PlayerMadeString: null, AssetId: null);

    private static LiveInventoryEdit ToEdit(PlayerInventoryArea area, InventoryItemSlot slot) => slot.IsEmpty
        ? new LiveInventoryEdit(WireKind(area), slot.Index, Clear: true)
        : new LiveInventoryEdit(WireKind(area), slot.Index, ItemId: slot.ItemId, Stack: slot.Count,
            Durability: slot.Durability, MaxDurability: slot.MaxDurability);

    private static string WireKind(PlayerInventoryArea area) => area switch
    {
        PlayerInventoryArea.Equipment => "equip",
        PlayerInventoryArea.Hotbar => "hotbar",
        PlayerInventoryArea.Transmog => "transmog",
        _ => "backpack",
    };

    private IReadOnlyList<PlayerInventorySlotEdit> SlotsFor(PlayerInventoryArea area) => area switch
    {
        PlayerInventoryArea.Equipment => Equipment,
        PlayerInventoryArea.Hotbar => Hotbar,
        PlayerInventoryArea.Transmog => Transmog,
        _ => Backpack,
    };

    private PlayerInventorySlotEdit? FindSlot(PlayerInventoryArea area, int index)
        => SlotsFor(area).FirstOrDefault(edit => edit.Index == index);
}
