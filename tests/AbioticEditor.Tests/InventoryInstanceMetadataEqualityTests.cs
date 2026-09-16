using AbioticEditor.Core.PlayerSaves;
using Xunit;

namespace AbioticEditor.Tests;

/// <summary>
/// <see cref="InventoryInstanceMetadata"/> holds lists, so the compiler-generated record
/// equality would compare list references, not contents. Every live refresh builds fresh list
/// instances for the same underlying data, which would make dirty-tracking
/// (PlayerInventorySlotEdit.IsDirty, PlayerSaveSession.IsDirty, WorldContainersTab,
/// PlayerTransmogTab) treat every metadata-bearing slot as changed forever. These tests pin the
/// value-equality override.
/// </summary>
public sealed class InventoryInstanceMetadataEqualityTests
{
    private static InventoryInstanceMetadata BuildMetadata() => new(
        [new("EDynamicProperty::WeaponCoating", 2), new("EDynamicProperty::CoatingDurability", 30)],
        ["Item.Special", "Item.Rare"],
        "/Game/Mods/Items.Items",
        "/Game/Mods/Variants.Variants",
        ["Item"]);

    [Fact]
    public void Independently_built_metadata_with_equal_content_are_equal()
    {
        var a = BuildMetadata();
        var b = BuildMetadata();

        Assert.NotSame(a.DynamicProperties, b.DynamicProperties);
        Assert.NotSame(a.GameplayTags, b.GameplayTags);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Inventory_item_slots_containing_equal_metadata_are_equal()
    {
        var slotA = new InventoryItemSlot(0, "z_item", 1, 100, 100, 0, 0, null, false, null, null,
            InstanceMetadata: BuildMetadata());
        var slotB = new InventoryItemSlot(0, "z_item", 1, 100, 100, 0, 0, null, false, null, null,
            InstanceMetadata: BuildMetadata());

        Assert.Equal(slotA, slotB);
    }

    [Fact]
    public void Differing_dynamic_property_value_breaks_equality()
    {
        var a = BuildMetadata();
        var b = a with
        {
            DynamicProperties = [new("EDynamicProperty::WeaponCoating", 99), new("EDynamicProperty::CoatingDurability", 30)],
        };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Null_and_empty_parent_gameplay_tags_are_treated_the_same()
    {
        var a = BuildMetadata() with { ParentGameplayTags = null };
        var b = BuildMetadata() with { ParentGameplayTags = [] };

        Assert.Equal(a, b);
    }
}
