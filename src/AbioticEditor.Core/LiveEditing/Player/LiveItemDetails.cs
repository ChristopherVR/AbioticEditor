using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Core.LiveEditing.Player;

/// <summary>Instance fields shared by player and container inventories. Null details identify
/// an older agent; an explicit empty string clears a name or visual override.</summary>
public sealed record LiveItemDetails(int LiquidLevel = 0, string? LiquidType = null,
    bool DynamicState = false, string? PlayerMadeString = null, string? AssetId = null,
    string? VariantRowName = null, InventoryInstanceMetadata? InstanceMetadata = null)
{
    public static LiveItemDetails FromSlot(InventoryItemSlot slot) => new(slot.LiquidLevel,
        slot.LiquidType ?? "E_LiquidType::NewEnumerator0", slot.DynamicState, slot.PlayerMadeString ?? string.Empty,
        slot.AssetId, slot.VariantRowName ?? string.Empty, WithCoating(slot));

    private static InventoryInstanceMetadata? WithCoating(InventoryItemSlot slot)
    {
        if (slot.InstanceMetadata is not { } metadata) return null;
        var properties = metadata.DynamicProperties.ToList();
        Set("EDynamicProperty::WeaponCoating", slot.CoatingIndex);
        Set("EDynamicProperty::CoatingDurability", slot.CoatingDurability);
        return metadata with { DynamicProperties = properties };
        void Set(string key, int? value)
        {
            if (value is null) return;
            var index = properties.FindIndex(property => property.Key == key);
            var property = new InventoryDynamicProperty(key, value.Value);
            if (index < 0) properties.Add(property); else properties[index] = property;
        }
    }

    public int? DynamicValue(string key) => InstanceMetadata?.DynamicProperties
        .FirstOrDefault(property => property.Key == "EDynamicProperty::" + key)?.Value;
}
