using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using AbioticEditor.Core.Saves;
using UeSaveGame.PropertyTypes;
using UeSaveGame.DataTypes;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests.Features;

public sealed class DeployedCareFeatureTests
{
    [Fact]
    public void Garden_fields_round_trip_without_changing_other_plots()
    {
        var path = Path.Combine(Fixtures.CascadeDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;
        var save = WorldSaveReader.ReadFromFile(path).Raw;
        var feature = new GardenPlotsFeature();
        var before = feature.Read(save);
        Assert.NotEmpty(before);
        var first = before.First(e => e.Fields.Any(f => f.Id.StartsWith("growth:", StringComparison.Ordinal)));
        Assert.True(feature.SetField(save, first.Key, "water", "100").Changed);
        var growth = first.Fields.First(f => f.Id.StartsWith("growth:", StringComparison.Ordinal));
        Assert.True(feature.SetField(save, first.Key, growth.Id, "5000").Changed);
        Assert.True(feature.SetField(save, first.Key, growth.Id, "10001").IsError);
        using var bytes = new MemoryStream(); save.WriteTo(bytes); bytes.Position = 0;
        var after = feature.Read(SaveGame.LoadFrom(bytes));
        Assert.Equal("100", after.Single(e => e.Key == first.Key).Fields.Single(f => f.Id == "water").Value);
        Assert.Equal("5000", after.Single(e => e.Key == first.Key).Fields.Single(f => f.Id == growth.Id).Value);
        foreach (var untouched in before.Where(e => e.Key != first.Key))
            Assert.Equal(untouched.Fields, after.Single(e => e.Key == untouched.Key).Fields);
        var unrelated = WorldMapAccessor.Entries(save, "DeployedObjectMap").First(e => !before.Any(g => g.Key == e.Key));
        Assert.True(feature.SetField(save, unrelated.Key, "water", "100").IsError);
        Assert.True(feature.Remove(save, first.Key).IsError);
    }

    [Fact]
    public void Garden_crop_change_round_trips_and_resets_growth()
    {
        var path = Path.Combine(Fixtures.CascadeDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;
        var save = WorldSaveReader.ReadFromFile(path).Raw;
        var feature = new GardenPlotsFeature();
        var before = feature.Read(save);
        var entry = before.First(e => e.Fields.Any(f => f.Id.StartsWith("crop:", StringComparison.Ordinal)));
        var cropField = entry.Fields.First(f => f.Id.StartsWith("crop:", StringComparison.Ordinal));
        Assert.Equal(WorldFieldKind.Enum, cropField.Kind);
        Assert.NotNull(cropField.Options);
        Assert.Contains("Plant_Carrot", cropField.Options!);
        var stageField = entry.Fields.First(f => f.Id == $"stage:{cropField.Id[5..]}");
        var growthField = entry.Fields.First(f => f.Id == $"growth:{cropField.Id[5..]}");

        var newCrop = cropField.Value == "Plant_Carrot" ? "Plant_Wheat" : "Plant_Carrot";
        Assert.True(feature.SetField(save, entry.Key, cropField.Id, newCrop).Changed);

        using var bytes = new MemoryStream(); save.WriteTo(bytes); bytes.Position = 0;
        var reloaded1 = SaveGame.LoadFrom(bytes);
        var after = feature.Read(reloaded1).Single(e => e.Key == entry.Key);
        Assert.Equal(newCrop, after.Fields.Single(f => f.Id == cropField.Id).Value);
        Assert.Equal("Sprout", after.Fields.Single(f => f.Id == stageField.Id).Value);
        Assert.Equal("0", after.Fields.Single(f => f.Id == growthField.Id).Value);
        foreach (var untouched in before.Where(e => e.Key != entry.Key))
            Assert.Equal(untouched.Fields, feature.Read(reloaded1).Single(e => e.Key == untouched.Key).Fields);

        // An unrecognized (modded/future) saved row stays selectable rather than being dropped.
        Assert.True(feature.SetField(save, entry.Key, cropField.Id, "Plant_ModFuture").Changed);
        using var bytes2 = new MemoryStream(); save.WriteTo(bytes2); bytes2.Position = 0;
        var reread = feature.Read(SaveGame.LoadFrom(bytes2)).Single(e => e.Key == entry.Key);
        var reReadCropField = reread.Fields.Single(f => f.Id == cropField.Id);
        Assert.Equal("Plant_ModFuture", reReadCropField.Value);
    }

    [Fact]
    public void Power_chair_charge_is_bounded_and_round_trips_in_a_deployable_layout()
    {
        var path = Path.Combine(Fixtures.CascadeDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;
        var save = WorldSaveReader.ReadFromFile(path).Raw;
        var garden = new GardenPlotsFeature().Read(save)[0];
        var props = WorldMapAccessor.FindEntry(save, "DeployedObjectMap", garden.Key)!;
        var classProperty = Assert.IsType<SoftObjectProperty>(props.FindByPrefix("Class_")!.Property);
        classProperty.Value = new SoftObjectPath
        {
            PackageName = new("/Game/Blueprints/DeployedObjects/Furniture/Deployed_Furniture_Chair_PowerChair"),
            AssetName = new("Deployed_Furniture_Chair_PowerChair_C"), SubPathString = new("")
        };
        var feature = new PowerChairsFeature();
        Assert.True(feature.AppliesTo(save));
        Assert.True(feature.SetField(save, garden.Key, "charge", "201").IsError);
        Assert.True(feature.SetField(save, garden.Key, "charge", "-1").IsError);
        Assert.True(feature.SetField(save, garden.Key, "charge", "175").Changed);
        using var buffer = new MemoryStream(); save.WriteTo(buffer); buffer.Position = 0;
        var reloaded = feature.Read(SaveGame.LoadFrom(buffer)).Single(e => e.Key == garden.Key);
        Assert.Equal("175", Assert.Single(reloaded.Fields).Value);
        Assert.False(feature.SupportsRemoval);
    }

    [Fact]
    public void Chemistry_links_to_saved_flasks_and_does_not_invent_a_timer()
    {
        var path = Path.Combine(Fixtures.CascadeDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;
        var save = WorldSaveReader.ReadFromFile(path).Raw;
        var feature = new ChemistryBenchesFeature();
        var entry = Assert.Single(feature.Read(save));
        Assert.Equal(entry.Key, entry.LinkTargetId);
        Assert.Equal(4, entry.Fields.Count(f => f.Id.StartsWith("flask:", StringComparison.Ordinal)));
        Assert.False(entry.Fields.Single(f => f.Id == "processing").Editable);
        var flask = entry.Fields.First(f => f.Id == "flask:0");
        Assert.True(flask.Editable);
        Assert.All(entry.Fields.Where(f => f.Id.StartsWith("flask:", StringComparison.Ordinal)), f => Assert.True(f.Editable));
        var replacement = flask.Value == "Empty" ? "Item_Water" : "Empty";
        Assert.True(feature.SetField(save, entry.Key, flask.Id, replacement).Changed);
        Assert.True(feature.SetField(save, entry.Key, "processing", "1").IsError);
    }

    [Fact]
    public async Task OfflineChemistryAdapter_stages_reverts_and_saves_flask_edit()
    {
        var source = Path.Combine(Fixtures.CascadeDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(source)) return;
        var copy = Path.Combine(Path.GetTempPath(), $"uesave-chemistry-{Guid.NewGuid():N}.sav");
        File.Copy(source, copy);
        try
        {
            var session = new WorldSaveSession(WorldSaveReader.ReadFromFile(copy), copy);
            var adapter = new OfflineChemistryBenchSession(session);
            var before = Assert.Single(adapter.Entries).Fields.Single(field => field.Id == "flask:0").Value;
            var replacement = before == "Empty" ? "Item_Water" : "Empty";
            var benchId = adapter.Entries.Single().Id;

            Assert.True((await adapter.SetFieldAsync(benchId, "flask:0", replacement)).Changed);
            Assert.Equal(replacement, adapter.Entries.Single().Fields.Single(field => field.Id == "flask:0").Value);
            session.Revert();
            Assert.Equal(before, adapter.Entries.Single().Fields.Single(field => field.Id == "flask:0").Value);

            Assert.True((await adapter.SetFieldAsync(benchId, "flask:0", replacement)).Changed);
            await session.SaveAsync();
            var savedRaw = WorldSaveReader.ReadFromFile(copy).Raw;
            var saved = new ChemistryBenchesFeature().Read(savedRaw);
            Assert.Equal(replacement, Assert.Single(saved).Fields.Single(field => field.Id == "flask:0").Value);
            var savedEntry = WorldMapAccessor.FindEntry(savedRaw, "DeployedObjectMap", benchId);
            Assert.NotNull(savedEntry);
            var inventory = Assert.IsType<ArrayProperty>(savedEntry!.FindByPrefix("ContainerInventories_")!.Property);
            var inventoryEntry = Assert.IsType<StructProperty>(inventory.Value!.GetValue(0));
            var inventoryProps = Assert.IsType<UeSaveGame.StructData.PropertiesStruct>(inventoryEntry.Value);
            var content = Assert.IsType<ArrayProperty>(inventoryProps.Properties.FindByPrefix("InventoryContent_")!.Property);
            var slotEntry = Assert.IsType<StructProperty>(content.Value!.GetValue(0));
            var slotProps = Assert.IsType<UeSaveGame.StructData.PropertiesStruct>(slotEntry.Value).Properties;
            var changeableEntry = Assert.IsType<StructProperty>(slotProps.FindByPrefix("ChangeableData_")!.Property);
            var changeable = Assert.IsType<UeSaveGame.StructData.PropertiesStruct>(changeableEntry.Value);
            Assert.Equal(1, changeable.Properties.FindByPrefix("CurrentStack_")!.Property!.Value);
        }
        finally
        {
            File.Delete(copy);
            File.Delete(copy + ".bak");
        }
    }

    [Fact]
    public void Digital_garden_cartridge_round_trips_and_resets_saved_progress()
    {
        var path = Path.Combine(Fixtures.CascadeDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;
        var save = WorldSaveReader.ReadFromFile(path).Raw;
        var garden = new GardenPlotsFeature().Read(save).First(e => e.Fields.Any(f => f.Id.StartsWith("crop:", StringComparison.Ordinal)));
        var props = WorldMapAccessor.FindEntry(save, "DeployedObjectMap", garden.Key)!;
        var classProperty = Assert.IsType<SoftObjectProperty>(props.FindByPrefix("Class_")!.Property);
        classProperty.Value = new SoftObjectPath
        {
            PackageName = new("/Game/Blueprints/DeployedObjects/Farming/Deployed_GardenPlot_Digital"),
            AssetName = new("Deployed_GardenPlot_Digital_C"), SubPathString = new("")
        };
        var feature = new DigitalGardenPlotsFeature();
        var entry = Assert.Single(feature.Read(save));
        var cartridge = entry.Fields.First(f => f.Id.StartsWith("cartridge:", StringComparison.Ordinal));
        Assert.Contains("Plant_Blank", cartridge.Options!);
        Assert.True(feature.SetField(save, entry.Key, cartridge.Id, "Plant_Blank").Changed);
        using var buffer = new MemoryStream(); save.WriteTo(buffer); buffer.Position = 0;
        var after = feature.Read(SaveGame.LoadFrom(buffer)).Single();
        Assert.Equal("Plant_Blank", after.Fields.Single(f => f.Id == cartridge.Id).Value);
        Assert.Equal("0", after.Fields.Single(f => f.Id == $"stage:{cartridge.Id[10..]}").Value);
        Assert.Equal("0", after.Fields.Single(f => f.Id == $"growth:{cartridge.Id[10..]}").Value);
        Assert.DoesNotContain(new GardenPlotsFeature().Read(save), entry => entry.Key == garden.Key);
    }

    [Fact]
    public void Sconce_lamp_state_round_trips()
    {
        var path = Path.Combine(Fixtures.CascadeDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;
        var save = WorldSaveReader.ReadFromFile(path).Raw;
        var feature = new SconceLampsFeature();
        var before = feature.Read(save)[0];
        var on = before.Fields.Single(f => f.Id == "on");
        var updated = on.Value == "true" ? "false" : "true";
        Assert.True(feature.SetField(save, before.Key, "on", updated).Changed);
        using var buffer = new MemoryStream(); save.WriteTo(buffer); buffer.Position = 0;
        Assert.Equal(updated, feature.Read(SaveGame.LoadFrom(buffer)).Single(e => e.Key == before.Key).Fields.Single(f => f.Id == "on").Value);
    }
}
