using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>Outcome of a cross-save pet move.</summary>
public sealed record PetTransferResult(bool Ok, string Message);

/// <summary>
/// Moves a pet between a world save (the <c>PetNPC</c> map) and a player save (a carried
/// inventory item). Operates on already-loaded <see cref="WorldSaveData"/> /
/// <see cref="PlayerSaveData"/>; the caller writes both saves afterwards (each keeps a
/// <c>.bak</c>). A moved pet keeps its variant, name, and XP/level; its health is reset to full
/// (the per-limb world health and the single item-durability are not 1:1 convertible).
/// </summary>
public static class PetTransfer
{
    // Values a fully-tamed/mutated carried variant carries (observed in real saves). Applied so
    // a world pet placed into a hotbar reads as a settled variant rather than mid-mutation.
    private const int MutatedProgress = 3;
    private const int MutatedFlag = 1;

    /// <summary>
    /// Picks a world pet up into a player's inventory. <paramref name="index"/> &lt; 0 uses the
    /// first free slot of <paramref name="kind"/> (default the Companion slot for Equipment).
    /// </summary>
    public static PetTransferResult WorldToPlayer(
        WorldSaveData world, string worldPetId, PlayerSaveData player, PetSlotKind kind, int index = -1)
    {
        var pet = world.Pets.FirstOrDefault(p => string.Equals(p.Id, worldPetId, StringComparison.Ordinal));
        if (pet is null) return new(false, $"world pet '{worldPetId}' not found.");

        var itemRow = PetItemCatalog.ItemRowFor(pet.NpcClass);
        if (itemRow is null) return new(false, $"no carried-item form is known for '{pet.ShortClass}'.");

        // Best-effort 1:1 health: the carried item's single durability = the world pet's total
        // limb health (its current HP pool). Falls back to a default when the pet has none.
        var total = pet.LimbHealth.Values.Sum();
        var health = total > 0 ? total : PetItemCatalog.DefaultMaxHealth;

        var carried = new CarriedPet(
            kind, -1, itemRow,
            Name: pet.CustomName,
            Health: health,
            MaxHealth: health,
            Xp: pet.Xp,
            MutationProgress: MutatedProgress,
            PetMutation: MutatedFlag);

        // Place the pet in the chosen destination, but fall back to the other so the move isn't
        // blocked when (say) the companion slot is occupied or absent while the hotbar has room.
        // The companion is equipment slot 12; the hotbar uses the first free slot.
        var (destKind, destIndex) = Place(player, kind, index, carried);
        if (destIndex < 0)
        {
            return new(false,
                "The target player's companion slot and hotbar are both full. Free a hotbar slot "
                + "(or the companion/pet slot) in that player save and try again.");
        }

        WorldSaveWriter.RemovePet(world, worldPetId);
        var where = destKind == PetSlotKind.Equipment ? "companion slot" : $"hotbar slot {destIndex}";
        return new(true, $"moved '{pet.DisplayName}' to the player's {where} (level {pet.Level} and {health:0} HP preserved).");
    }

    // Companion = equipment slot 12. Tries the preferred destination first, then the other, so a
    // full/absent companion slot doesn't block a move when the hotbar is free (and vice versa).
    private static (PetSlotKind Kind, int Index) Place(
        PlayerSaveData player, PetSlotKind preferred, int requestedIndex, CarriedPet carried)
    {
        const int CompanionSlot = 12;

        if (preferred == PetSlotKind.Equipment)
        {
            var eqIndex = requestedIndex >= 0 ? requestedIndex : CompanionSlot;
            var used = PlayerSaveWriter.AddCarriedPetToSlot(player, PetSlotKind.Equipment, eqIndex, carried);
            if (used >= 0) return (PetSlotKind.Equipment, used);

            used = PlayerSaveWriter.AddCarriedPetToSlot(player, PetSlotKind.Hotbar, -1, carried);
            return (PetSlotKind.Hotbar, used);
        }

        var hot = PlayerSaveWriter.AddCarriedPetToSlot(player, PetSlotKind.Hotbar, requestedIndex, carried);
        if (hot >= 0) return (PetSlotKind.Hotbar, hot);

        var comp = PlayerSaveWriter.AddCarriedPetToSlot(player, PetSlotKind.Equipment, CompanionSlot, carried);
        return (PetSlotKind.Equipment, comp);
    }

    /// <summary>
    /// Puts a carried pet back into the world at <paramref name="x"/>/<paramref name="y"/>/<paramref name="z"/>
    /// (e.g. a pet bed's location). Requires the target world to already have at least one pet to
    /// clone the entry shape from.
    /// </summary>
    public static PetTransferResult PlayerToWorld(
        PlayerSaveData player, PetSlotKind kind, int index, WorldSaveData world, double x, double y, double z)
    {
        var carried = player.CarriedPets.FirstOrDefault(c => c.Slot == kind && c.Index == index);
        if (carried is null) return new(false, $"no carried pet at {kind}[{index}].");

        var npcClass = PetItemCatalog.NpcClassFor(carried.ItemRow);
        if (npcClass is null) return new(false, $"no world creature class is known for '{carried.ItemRow}'.");

        var worldPet = new WorldPet(
            Id: string.Empty, IsDead: false, NpcClass: npcClass,
            X: x, Y: y, Z: z,
            CustomName: carried.Name,
            LimbHealth: new Dictionary<string, double>(),
            Xp: carried.Xp, State: null);

        // Carry the item's durability across as the world pet's total limb health (1:1 best-effort).
        var newId = WorldSaveWriter.AddPet(world, worldPet, carried.Health > 0 ? carried.Health : null);
        if (newId is null)
        {
            return new(false,
                "This world has no creature (pet or NPC) to base the new pet on, so it can't be placed here. "
                + "Choose a world that already contains a pet or a story NPC.");
        }

        PlayerSaveWriter.RemoveCarriedPet(player, kind, index);
        return new(true, $"placed '{carried.DisplayName}' into the world at ({x:F0}, {y:F0}, {z:F0}) (level {carried.Level} and {carried.Health:0} HP preserved).");
    }
}
