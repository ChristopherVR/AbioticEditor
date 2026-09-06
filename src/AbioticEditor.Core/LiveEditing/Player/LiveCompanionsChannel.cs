using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>
/// Live counterpart of <see cref="PlayerSaveReader"/>'s carried-pet slice: a pet is just an
/// <c>Item.Pet</c> row sitting in the same backpack/equip/hotbar inventory arrays
/// <see cref="LiveInventoryChannel"/> already reads/writes (round 74), so <c>companions.list</c>
/// reuses the exact same slot struct and hash-suffixed field names, plus two fields new to this
/// round: the pet's custom name (<c>PlayerMadeString_</c>, the SAME field
/// <see cref="LiveInventoryChannel"/>'s slot already proved live, just not surfaced there) and its
/// XP / mutation progress (<c>DynamicProperties_</c>, a genuinely new access path with no
/// reference-mod precedent - see <c>companions.lua</c>'s own comment).
///
/// The Lua side has no game-data catalog of its own, so it returns every occupied slot; filtering
/// down to the ones that are actually pets (<c>PetItemCatalog.IsPetItem</c>, or the Companion
/// equipment slot regardless of whether the catalog recognises the row - mirroring
/// <see cref="PlayerSaveReader"/>'s own rule) happens on the caller's side, not here.
/// </summary>
public sealed class LiveCompanionsChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>Every occupied backpack/equip/hotbar slot for <paramref name="playerId"/> (or the
    /// local player when omitted), before pet filtering (done by the caller, see this class's own
    /// summary).</summary>
    public async Task<IReadOnlyList<LiveInventoryPetRow>> ListAsync(
        string? playerId = null, CancellationToken cancellationToken = default)
    {
        object? payload = playerId is null ? null : new PlayerIdWire(playerId);
        var wire = await _channel.RequestAsync<DirectoryWire>("companions.list", payload, cancellationToken)
            .ConfigureAwait(false);
        return wire.Pets.Select(r => new LiveInventoryPetRow(
            r.Kind, r.SlotIndex, r.ItemId, r.Name, r.Health, r.MaxHealth, r.Xp, r.MutationProgress, r.PetMutation)).ToList();
    }

    /// <summary>Applies a full pet row (item id, name, health, XP, mutation) to one slot immediately.</summary>
    public Task SetAsync(string kind, int slotIndex, CarriedPet pet, string? playerId = null,
        CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("companions.set",
            new SetWire(kind, slotIndex, false, pet.ItemRow, pet.Name, pet.Health, pet.MaxHealth,
                pet.Xp, pet.MutationProgress, pet.PetMutation, playerId),
            cancellationToken);

    /// <summary>Clears a pet's slot back to empty immediately. There is no undo once this has been
    /// sent - the caller must have already confirmed this with the player.</summary>
    public Task ClearAsync(string kind, int slotIndex, string? playerId = null, CancellationToken cancellationToken = default)
        => _channel.RequestAsync<object?>("companions.set",
            new SetWire(kind, slotIndex, true, null, null, null, null, null, null, null, playerId),
            cancellationToken);

    /// <summary>Wire kind for a <see cref="PetSlotKind"/>, matching <c>companions.lua</c>'s
    /// <c>PET_KINDS</c> names (the same three the inventory area already uses).</summary>
    public static string ToWireKind(PetSlotKind kind) => kind switch
    {
        PetSlotKind.Equipment => "equip",
        PetSlotKind.Hotbar => "hotbar",
        _ => "backpack",
    };

    public static PetSlotKind FromWireKind(string kind) => kind switch
    {
        "equip" => PetSlotKind.Equipment,
        "hotbar" => PetSlotKind.Hotbar,
        _ => PetSlotKind.Main,
    };

    private sealed record PlayerIdWire(string PlayerId);
    private sealed record DirectoryWire(IReadOnlyList<RowWire> Pets, bool IsHost);
    private sealed record RowWire(string Kind, int SlotIndex, string ItemId, string? Name,
        double Health, double MaxHealth, int Xp, int MutationProgress, int PetMutation);
    private sealed record SetWire(string Kind, int SlotIndex, bool? Clear, string? ItemId, string? Name,
        double? Health, double? MaxHealth, int? Xp, int? MutationProgress, int? PetMutation, string? PlayerId);
}

/// <summary>One occupied player-inventory slot as read live, before pet filtering - see
/// <see cref="LiveCompanionsChannel.ListAsync"/>.</summary>
public sealed record LiveInventoryPetRow(string Kind, int SlotIndex, string ItemId, string? Name,
    double Health, double MaxHealth, int Xp, int MutationProgress, int PetMutation);
