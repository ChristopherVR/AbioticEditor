using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>Which player inventory array a carried pet lives in.</summary>
public enum PetSlotKind
{
    /// <summary>The equipment paper-doll. Index 12 is the Companion slot (the active pet).</summary>
    Equipment,

    /// <summary>The hotbar (pets can be picked up only into hotbar slots).</summary>
    Hotbar,

    /// <summary>The backpack / main inventory.</summary>
    Main,
}

/// <summary>
/// A pet carried by a player: an <c>Item.Pet</c> inventory item (see
/// <see cref="WorldSaves.PetItemCatalog"/>). Health is the item's durability; XP / mutation
/// live in the slot's <c>DynamicProperties_</c>. This is the player-side counterpart of a
/// world <see cref="WorldSaves.WorldPet"/>.
/// </summary>
public sealed record CarriedPet(
    PetSlotKind Slot,
    int Index,
    string ItemRow,
    string? Name,
    double Health,
    double MaxHealth,
    int Xp,
    int MutationProgress,
    int PetMutation)
{
    /// <summary>Friendly variant name (e.g. "Magma Skink"), else the item row.</summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(Name)
        ? Name!
        : PetItemCatalog.FriendlyName(ItemRow) ?? ItemRow;

    /// <summary>The variant's friendly name, ignoring any custom name.</summary>
    public string Variant => PetItemCatalog.FriendlyName(ItemRow) ?? ItemRow;

    /// <summary>Derived level (0-20) from <see cref="Xp"/>.</summary>
    public int Level => PetCatalog.LevelForXp(Xp);

    /// <summary>True for the Companion equipment slot (index 12), the active follower.</summary>
    public bool IsCompanionSlot => Slot == PetSlotKind.Equipment && Index == 12;
}
