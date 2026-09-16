namespace AbioticEditor.Core.PlayerSaves;

/// <summary>
/// One slot from an inventory array (Equipment / Hotbar / Main). When <see cref="ItemId"/>
/// is null/empty the slot is empty. <see cref="Index"/> is the slot's position in its
/// containing array.
/// </summary>
public sealed record InventoryItemSlot(
    int Index,
    string? ItemId,
    int Count,
    double Durability,
    double MaxDurability,
    int AmmoInMagazine,
    int LiquidLevel,
    string? LiquidType,
    bool DynamicState,
    string? PlayerMadeString,
    string? AssetId,
    // Which visual variant this instance shows (a poster's artwork, a helmet's paint color,
    // ...): the RowName of the item struct's TextureVariantRow_ handle into
    // DT_TextureVariants, independent of ItemId. Null when the game never wrote one for this
    // instance (most items), in which case there is nothing here yet to change.
    string? VariantRowName = null, int? CoatingIndex = null, int? CoatingDurability = null, InventoryInstanceMetadata? InstanceMetadata = null)
{
    public bool IsEmpty => string.IsNullOrEmpty(ItemId) || ItemId is "None" or "Empty";
    public double DurabilityPercent => MaxDurability > 0 ? Durability / MaxDurability : 0;
}

/// <summary>Complete extra instance state retained by connected inventories during moves.</summary>
public sealed record InventoryInstanceMetadata(IReadOnlyList<InventoryDynamicProperty> DynamicProperties,
    IReadOnlyList<string> GameplayTags, string? ItemDataTable = null, string? VariantDataTable = null, IReadOnlyList<string>? ParentGameplayTags = null)
{
    // The record keyword generates reference equality for list-typed positional members, so
    // dirty-tracking (PlayerInventorySlotEdit.IsDirty, WorldContainersTab, PlayerTransmogTab,
    // PlayerSaveSession.IsDirty) would treat every fresh live-refresh copy as changed. Override
    // with sequence equality instead; a null ParentGameplayTags is treated the same as empty.
    public bool Equals(InventoryInstanceMetadata? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return DynamicProperties.SequenceEqual(other.DynamicProperties)
            && GameplayTags.SequenceEqual(other.GameplayTags, StringComparer.Ordinal)
            && string.Equals(ItemDataTable, other.ItemDataTable, StringComparison.Ordinal)
            && string.Equals(VariantDataTable, other.VariantDataTable, StringComparison.Ordinal)
            && (ParentGameplayTags ?? []).SequenceEqual(other.ParentGameplayTags ?? [], StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var property in DynamicProperties) hash.Add(property);
        foreach (var tag in GameplayTags) hash.Add(tag, StringComparer.Ordinal);
        hash.Add(ItemDataTable, StringComparer.Ordinal);
        hash.Add(VariantDataTable, StringComparer.Ordinal);
        foreach (var tag in ParentGameplayTags ?? []) hash.Add(tag, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>A game-defined dynamic property, including pet progress and weapon coatings.</summary>
public sealed record InventoryDynamicProperty(string Key, int Value);
