using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Tests;

/// <summary>
/// Variant rows are delta-serialized: an item using its normal appearance usually has no
/// TextureVariantRow_ handle at all. These tests prove both inventory writers can add that
/// first handle and that UeSaveGame can read the resulting structure back from disk.
/// </summary>
public sealed class ItemVariantWriteTests
{
    private const string VariantRow = "gear_hardhat_blue";
    private const string VariantFullName = "TextureVariantRow_28_1C7CF7A0441335E8AC4EA7B5CA91F636";
    private const string VariantTable =
        "/Game/Blueprints/DataTables/Customization/DT_TextureVariants.DT_TextureVariants";

    [Fact]
    public void PlayerSlot_MissingVariantHandle_IsCreatedAndRoundTrips()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var fixture = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561198128277890.sav");
        Assert.True(File.Exists(fixture), $"missing fixture: {fixture}");
        var temp = Path.Combine(Path.GetTempPath(), $"abf-variant-player-{Guid.NewGuid():N}.sav");

        try
        {
            File.Copy(fixture, temp);
            var data = PlayerSaveReader.ReadFromFile(temp);
            var target = data.Inventory.Main[0];
            var changeable = MainSlotChangeableData(data, target.Index);
            changeable.Properties = changeable.Properties
                .Where(tag => !tag.Name.Value.StartsWith("TextureVariantRow_", StringComparison.Ordinal))
                .ToList();
            var slots = data.Inventory.Main
                .Select(slot => slot.Index == target.Index ? slot with { VariantRowName = VariantRow } : slot)
                .ToList();

            PlayerSaveWriter.ApplyInventory(data, data.Inventory with { Main = slots });
            PlayerSaveWriter.WriteToFile(data, temp);

            var reloaded = PlayerSaveReader.ReadFromFile(temp);
            Assert.Equal(VariantRow, reloaded.Inventory.Main[target.Index].VariantRowName);
            AssertVariantHandle(MainSlotChangeableData(reloaded, target.Index));
        }
        finally
        {
            DeleteWithBackup(temp);
        }
    }

    [Fact]
    public void WorldContainerSlot_VariantChange_RoundTrips()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var fixture = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        if (!File.Exists(fixture)) return;
        var temp = Path.Combine(Path.GetTempPath(), $"abf-variant-world-{Guid.NewGuid():N}.sav");

        try
        {
            File.Copy(fixture, temp);
            var data = WorldSaveReader.ReadFromFile(temp);
            var target = data.Containers.First(container => container.Inventories
                .Any(inventory => inventory.Slots.Any(slot => slot.VariantRowName is not null)));
            var inventoryIndex = target.Inventories.ToList().FindIndex(
                inventory => inventory.Slots.Any(slot => slot.VariantRowName is not null));
            var inventory = target.Inventories[inventoryIndex];
            var slotIndex = inventory.Slots.First(slot => slot.VariantRowName is not null).Index;
            var slots = inventory.Slots
                .Select(slot => slot.Index == slotIndex ? slot with { VariantRowName = VariantRow } : slot)
                .ToList();
            var inventories = target.Inventories
                .Select((entry, index) => index == inventoryIndex ? new WorldInventory(slots) : entry)
                .ToList();

            WorldSaveWriter.ApplyContainers(data, [target with { Inventories = inventories }]);
            WorldSaveWriter.WriteToFile(data, temp);

            var reloaded = WorldSaveReader.ReadFromFile(temp);
            var reloadedSlot = reloaded.Containers.First(container => container.Id == target.Id)
                .Inventories[inventoryIndex].Slots[slotIndex];
            Assert.Equal(VariantRow, reloadedSlot.VariantRowName);
        }
        finally
        {
            DeleteWithBackup(temp);
        }
    }

    private static void AssertVariantHandle(PropertiesStruct changeable)
    {
        var tag = changeable.Properties.Single(property => property.Name.Value == VariantFullName);
        var variant = Assert.IsType<StructProperty>(tag.Property);
        Assert.Equal("DataTableRowHandle", variant.StructType?.Name.Value);
        var body = Assert.IsType<PropertiesStruct>(variant.Value);
        Assert.Equal(VariantTable, body.Properties
            .Single(property => property.Name.Value == "DataTable").Property?.ToString());
    }

    private static PropertiesStruct MainSlotChangeableData(PlayerSaveData data, int index)
    {
        var root = (PropertiesStruct)((StructProperty)data.Raw.Properties!
            .First(tag => tag.Name.Value.StartsWith("CharacterSaveData", StringComparison.Ordinal)).Property!).Value!;
        var array = (ArrayProperty)root.Properties
            .First(tag => tag.Name.Value.StartsWith("Inventory_", StringComparison.Ordinal)).Property!;
        var slot = (PropertiesStruct)((StructProperty)array.Value!.GetValue(index)!).Value!;
        return (PropertiesStruct)((StructProperty)slot.Properties
            .First(tag => tag.Name.Value.StartsWith("ChangeableData_", StringComparison.Ordinal)).Property!).Value!;
    }

    private static void DeleteWithBackup(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
    }
}
