using System.IO;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Investigation: how does a Personal Teleporter remember what it is synced to?
/// Hypothesis: the slot's ChangeableData carries the target deployable's GUID
/// (AssetID_) referencing a DeployedObjectMap key in the world save.
/// </summary>
public class TeleporterProbeTests
{
    private readonly ITestOutputHelper _output;

    public TeleporterProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void FindTeleporters_AcrossAllFixtureSaves()
    {
        Assert.NotNull(Fixtures.CascadeDir);

        // Player inventories.
        foreach (var playerSave in Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav"))
        {
            var data = PlayerSaveReader.ReadFromFile(playerSave);
            foreach (var (slot, where) in
                     data.Inventory.Equipment.Select(s => (s, "equipment"))
                     .Concat(data.Inventory.Hotbar.Select(s => (s, "hotbar")))
                     .Concat(data.Inventory.Main.Select(s => (s, "backpack"))))
            {
                if (slot.ItemId?.Contains("eleporter", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _output.WriteLine($"{Path.GetFileName(playerSave)} {where}[{slot.Index}]: {slot.ItemId}");
                    _output.WriteLine($"  AssetId='{slot.AssetId}' PlayerMadeString='{slot.PlayerMadeString}' DynamicState={slot.DynamicState} Liquid={slot.LiquidType}/{slot.LiquidLevel}");
                }
            }
        }

        // Facility containers + dropped items.
        var facility = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        if (File.Exists(facility))
        {
            var world = WorldSaveReader.ReadFromFile(facility);
            foreach (var c in world.Containers)
            {
                foreach (var slot in c.Inventories.SelectMany(i => i.Slots))
                {
                    if (slot.ItemId?.Contains("eleporter", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _output.WriteLine($"container {c.ClassName}/{c.Id} [{slot.Index}]: {slot.ItemId}");
                        _output.WriteLine($"  AssetId='{slot.AssetId}' PlayerMadeString='{slot.PlayerMadeString}' DynamicState={slot.DynamicState}");
                    }
                }
            }
            foreach (var d in world.DroppedItems)
            {
                if (d.Slot.ItemId?.Contains("eleporter", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _output.WriteLine($"dropped {d.Id}: {d.Slot.ItemId}");
                    _output.WriteLine($"  AssetId='{d.Slot.AssetId}' PlayerMadeString='{d.Slot.PlayerMadeString}' DynamicState={d.Slot.DynamicState}");
                }
            }

            // Check whether any teleporter AssetId matches a DeployedObjectMap key.
            var deployableIds = world.Deployables.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var teleporterAssetIds = world.Containers.SelectMany(c => c.Inventories).SelectMany(i => i.Slots)
                .Concat(world.DroppedItems.Select(d => d.Slot))
                .Where(s => s.ItemId?.Contains("eleporter", StringComparison.OrdinalIgnoreCase) == true
                            && !string.IsNullOrEmpty(s.AssetId))
                .Select(s => s.AssetId!)
                .ToList();
            foreach (var id in teleporterAssetIds)
            {
                var hit = deployableIds.Contains(id);
                _output.WriteLine($"AssetId {id} → deployable match: {hit}"
                    + (hit ? $"  class={world.Deployables.First(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)).ClassName}" : ""));
            }
        }
    }

    [Fact]
    public void Dump_TeleporterSlot_AllRawFields()
    {
        Assert.NotNull(Fixtures.CascadeDir);

        // Walk every player save's raw inventory arrays and dump the complete
        // ChangeableData of any teleporter - including fields our model doesn't parse.
        foreach (var playerSave in Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav"))
        {
            using var fs = File.OpenRead(playerSave);
            var save = SaveGame.LoadFrom(fs);
            var root = ((PropertiesStruct)((StructProperty)save.Properties!
                .First(t => t.Name!.Value.StartsWith("CharacterSaveData")).Property!).Value!).Properties;

            foreach (var arrayName in new[] { "EquipmentInventory_", "HotbarInventory_", "Inventory_" })
            {
                var tag = root.FirstOrDefault(t => t.Name!.Value.StartsWith(arrayName));
                if (tag?.Property is not ArrayProperty array || array.Value is null) continue;

                for (var i = 0; i < array.Value.Length; i++)
                {
                    if (array.Value.GetValue(i) is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;
                    var rowName = (ps.Properties.FirstOrDefault(p => p.Name!.Value.StartsWith("ItemDataTable_"))?.Property
                        as StructProperty)?.Value is PropertiesStruct rh
                        ? rh.Properties.FirstOrDefault(p => p.Name!.Value == "RowName")?.Property?.Value?.ToString()
                        : null;
                    if (rowName?.Contains("eleporter", StringComparison.OrdinalIgnoreCase) != true) continue;

                    _output.WriteLine($"=== {Path.GetFileName(playerSave)} {arrayName}[{i}] = {rowName} ===");
                    var changeable = ps.Properties.FirstOrDefault(p => p.Name!.Value.StartsWith("ChangeableData_"));
                    if (changeable?.Property is StructProperty cSp && cSp.Value is PropertiesStruct cPs)
                    {
                        foreach (var p in cPs.Properties)
                        {
                            _output.WriteLine($"  {p.Name!.Value} ({p.Property?.GetType().Name}) = {p.Property?.Value}");
                        }
                    }
                }
            }
        }
    }
}
