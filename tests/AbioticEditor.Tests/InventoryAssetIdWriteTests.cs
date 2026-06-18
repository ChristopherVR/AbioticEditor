using System.IO;
using AbioticEditor.Core.PlayerSaves;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Regression: an item added to an inventory slot needs its per-instance AssetID GUID
/// written. The game tracks instanced items (weapons, tools, armor, pets) by that id, so a
/// slot left with a blank/duplicate AssetID is allocated but the item never renders in-game
/// ("takes a slot but you can't see it"). FillFromCatalog mints a fresh id; the writers'
/// ApplySlot must persist it, creating the AssetID_ tag when the slot was serialized sparsely.
/// </summary>
public class InventoryAssetIdWriteTests
{
    private readonly ITestOutputHelper _output;
    public InventoryAssetIdWriteTests(ITestOutputHelper output) { _output = output; }

    /// <summary>The canonical hash-suffixed name ApplySlot creates the AssetID_ tag with.</summary>
    private const string AssetIdFullName = "AssetID_25_06DB7A12469849D19D5FC3BA6BEDEEAB";

    [Fact]
    public void FilledSlot_AssetId_PersistsThroughFile()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var fixture = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561198128277890.sav");
        Assert.True(File.Exists(fixture), $"missing fixture: {fixture}");

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-assetid-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(fixture, tempPath);

            var data = PlayerSaveReader.ReadFromFile(tempPath);
            var target = data.Inventory.Main.First(s => s.IsEmpty);
            var newAssetId = Guid.NewGuid().ToString("N").ToUpperInvariant();
            _output.WriteLine($"filling Main slot {target.Index} (was AssetId={target.AssetId ?? "<null>"}) with {newAssetId}");

            var newMain = data.Inventory.Main
                .Select(s => s.Index == target.Index
                    ? s with { ItemId = "armor_helmet_security", Count = 1, Durability = 100, MaxDurability = 100, AssetId = newAssetId }
                    : s)
                .ToList();
            PlayerSaveWriter.ApplyInventory(data, data.Inventory with { Main = newMain });
            PlayerSaveWriter.WriteToFile(data, tempPath);

            var reloaded = PlayerSaveReader.ReadFromFile(tempPath);
            var slot = reloaded.Inventory.Main[target.Index];
            Assert.Equal("armor_helmet_security", slot.ItemId);
            Assert.Equal(newAssetId, slot.AssetId);
            // The fresh id replaced the empty slot's placeholder (the whole point: a unique
            // id, not the blank/shared one the slot carried while empty).
            Assert.NotEqual(target.AssetId, slot.AssetId);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
        }
    }

    [Fact]
    public void SparseSlot_MissingAssetIdTag_IsCreatedOnFill()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var fixture = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561198128277890.sav");
        Assert.True(File.Exists(fixture), $"missing fixture: {fixture}");

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-assetid-sparse-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(fixture, tempPath);

            // Manufacture a slot the game wrote without an AssetID_ tag (delta-serialized).
            var setup = PlayerSaveReader.ReadFromFile(tempPath);
            var index = setup.Inventory.Main.First(s => s.IsEmpty).Index;
            var changeable = MainSlotChangeableData(setup, index);
            changeable.Properties = changeable.Properties
                .Where(t => !t.Name!.Value.StartsWith("AssetID_", StringComparison.Ordinal))
                .ToList();
            PlayerSaveWriter.WriteToFile(setup, tempPath);

            var data = PlayerSaveReader.ReadFromFile(tempPath);
            Assert.Null(data.Inventory.Main[index].AssetId); // confirm it really is sparse now

            var assetId = Guid.NewGuid().ToString("N").ToUpperInvariant();
            var newMain = data.Inventory.Main
                .Select(s => s.Index == index
                    ? s with { ItemId = "armor_helmet_security", Count = 1, AssetId = assetId }
                    : s)
                .ToList();
            PlayerSaveWriter.ApplyInventory(data, data.Inventory with { Main = newMain });
            PlayerSaveWriter.WriteToFile(data, tempPath);

            var reloaded = PlayerSaveReader.ReadFromFile(tempPath);
            Assert.Equal(assetId, reloaded.Inventory.Main[index].AssetId);
            Assert.True(
                MainSlotChangeableData(reloaded, index).Properties
                    .Any(t => t.Name!.Value == AssetIdFullName),
                "AssetID_ tag was not created with its canonical full name");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
        }
    }

    private const string ItemTableGlobal = "/Game/Blueprints/Items/ItemTable_Global.ItemTable_Global";

    [Fact]
    public void FilledEmptySlot_PointsDataTableAtItemTableGlobal()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var fixture = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561198128277890.sav");
        Assert.True(File.Exists(fixture), $"missing fixture: {fixture}");

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-dt-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(fixture, tempPath);
            var data = PlayerSaveReader.ReadFromFile(tempPath);
            var target = data.Inventory.Main.First(s => s.IsEmpty);

            // An empty slot defaults its row-handle DataTable to ItemTable_Pickups, where catalog
            // items do not exist - the game then renders the added item blank. Filling it must
            // repoint the DataTable at ItemTable_Global.
            Assert.DoesNotContain("ItemTable_Global", RawSlotDataTable(data, "Inventory_", target.Index) ?? "");

            var newMain = data.Inventory.Main
                .Select(s => s.Index == target.Index
                    ? s with { ItemId = "glowshard", Count = 1, AssetId = Guid.NewGuid().ToString("N").ToUpperInvariant() }
                    : s)
                .ToList();
            PlayerSaveWriter.ApplyInventory(data, data.Inventory with { Main = newMain });
            PlayerSaveWriter.WriteToFile(data, tempPath);

            var reloaded = PlayerSaveReader.ReadFromFile(tempPath);
            Assert.Equal("glowshard", reloaded.Inventory.Main[target.Index].ItemId);
            Assert.Equal(ItemTableGlobal, RawSlotDataTable(reloaded, "Inventory_", target.Index));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
        }
    }

    [Fact]
    public void ReSavingOccupiedItems_PreservesTheirDataTable()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var dir = Path.Combine(Fixtures.CascadeDir!, "PlayerData");
        var fixture = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "Player_*.sav").OrderBy(p => p).FirstOrDefault() : null;
        if (fixture is null) return;

        var tempPath = Path.Combine(Path.GetTempPath(), $"abf-dt2-{Guid.NewGuid():N}.sav");
        try
        {
            File.Copy(fixture, tempPath);
            var data = PlayerSaveReader.ReadFromFile(tempPath);

            // Find an occupied slot whose DataTable is NOT ItemTable_Global, to prove the fill fix
            // does not clobber a real item's own table on a plain re-save.
            (string Prefix, int Index, string Table)? target = null;
            foreach (var (prefix, slots) in new[]
            {
                ("EquipmentInventory_", data.Inventory.Equipment),
                ("HotbarInventory_", data.Inventory.Hotbar),
                ("Inventory_", data.Inventory.Main),
            })
            {
                foreach (var s in slots.Where(s => !s.IsEmpty))
                {
                    var dt = RawSlotDataTable(data, prefix, s.Index);
                    if (dt is not null && !dt.Contains("ItemTable_Global"))
                    {
                        target = (prefix, s.Index, dt);
                        break;
                    }
                }
                if (target is not null) break;
            }
            if (target is null) return; // no non-Global occupied item in this fixture; nothing to prove

            // Re-apply the inventory unchanged: every slot is rewritten, but an already-populated
            // slot must keep its own table.
            PlayerSaveWriter.ApplyInventory(data, data.Inventory);
            PlayerSaveWriter.WriteToFile(data, tempPath);

            var reloaded = PlayerSaveReader.ReadFromFile(tempPath);
            Assert.Equal(target.Value.Table, RawSlotDataTable(reloaded, target.Value.Prefix, target.Value.Index));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
        }
    }

    /// <summary>The ItemDataTable row-handle's DataTable object path for a slot, or null. The path
    /// lives on the ObjectProperty (exposed via ToString), not its Value.</summary>
    private static string? RawSlotDataTable(PlayerSaveData data, string arrayPrefix, int index)
    {
        var root = (PropertiesStruct)((StructProperty)data.Raw.Properties!
            .First(t => t.Name!.Value.StartsWith("CharacterSaveData", StringComparison.Ordinal)).Property!).Value!;
        var array = (ArrayProperty)root.Properties
            .First(t => t.Name!.Value.StartsWith(arrayPrefix, StringComparison.Ordinal)).Property!;
        var slot = (PropertiesStruct)((StructProperty)array.Value!.GetValue(index)!).Value!;
        var rowHandle = (PropertiesStruct)((StructProperty)slot.Properties
            .First(t => t.Name!.Value.StartsWith("ItemDataTable_", StringComparison.Ordinal)).Property!).Value!;
        return rowHandle.Properties
            .FirstOrDefault(t => t.Name!.Value.StartsWith("DataTable", StringComparison.Ordinal))?.Property?.ToString();
    }

    /// <summary>Raw ChangeableData_ of a Main (<c>Inventory_</c>) slot. "Inventory_" uniquely
    /// matches Main: Equipment/Hotbar/Transmog arrays carry their own distinct prefixes.</summary>
    private static PropertiesStruct MainSlotChangeableData(PlayerSaveData data, int index)
    {
        var root = (PropertiesStruct)((StructProperty)data.Raw.Properties!
            .First(t => t.Name!.Value.StartsWith("CharacterSaveData", StringComparison.Ordinal)).Property!).Value!;
        var array = (ArrayProperty)root.Properties
            .First(t => t.Name!.Value.StartsWith("Inventory_", StringComparison.Ordinal)).Property!;
        var slot = (PropertiesStruct)((StructProperty)array.Value!.GetValue(index)!).Value!;
        return (PropertiesStruct)((StructProperty)slot.Properties
            .First(t => t.Name!.Value.StartsWith("ChangeableData_", StringComparison.Ordinal)).Property!).Value!;
    }
}
