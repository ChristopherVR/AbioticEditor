using System.Collections.Generic;
using System.IO;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Tests;

/// <summary>
/// The bundled game-data registry (a dumped snapshot of the game's data tables) must round-trip
/// through JSON byte-for-meaning and rebuild a working <see cref="ItemCatalog"/> with no game
/// install - that is the whole point of shipping it.
/// </summary>
public sealed class GameDataRegistryTests
{
    private static GameDataRegistry SampleRegistry() => new()
    {
        SchemaVersion = GameDataRegistry.CurrentSchemaVersion,
        GameVersion = "1.0.test",
        Items =
        [
            new ItemCatalogEntry(
                Id: "scrap_metal",
                DisplayName: "Metal Scrap",
                Description: "A pile of metal.",
                IconAssetPath: "/Game/Textures/itemicon_scrap_metal.itemicon_scrap_metal",
                StackSize: 64,
                MaxDurability: 100,
                IsWeapon: false,
                Weight: 0.2,
                Tags: ["Item.Material.Metal", "Item.Resource.Distillable"],
                ContainerCapacity: 0,
                EquipSlot: 1,
                MaxLiquid: 0,
                AllowedLiquids: null),
            new ItemCatalogEntry(
                Id: "canteen",
                DisplayName: "Canteen",
                Description: null,
                IconAssetPath: null,
                StackSize: 1,
                MaxDurability: 0,
                IsWeapon: false,
                Weight: 0.5,
                Tags: ["Item.Liquid.Container"],
                ContainerCapacity: 0,
                EquipSlot: 0,
                MaxLiquid: 3,
                AllowedLiquids: [1, 2, 8]),
        ],
        ItemTableRefs = new Dictionary<string, string>
        {
            ["scrap_metal"] = "/Game/Blueprints/Items/ItemTable_Global.ItemTable_Global",
            ["canteen"] = "/Game/Blueprints/Items/ItemTable_DLC.ItemTable_DLC",
        },
    };

    [Fact]
    public void SaveThenLoad_PreservesEveryField()
    {
        var path = Path.Combine(Path.GetTempPath(), $"reg-{System.Guid.NewGuid():N}.json");
        try
        {
            SampleRegistry().Save(path);
            var loaded = GameDataRegistry.TryLoad(path);

            Assert.NotNull(loaded);
            Assert.Equal(GameDataRegistry.CurrentSchemaVersion, loaded!.SchemaVersion);
            Assert.Equal("1.0.test", loaded.GameVersion);
            Assert.NotNull(loaded.Items);
            Assert.Equal(2, loaded.Items!.Count);

            var canteen = Assert.Single(loaded.Items, i => i.Id == "canteen");
            Assert.Equal("Canteen", canteen.DisplayName);
            Assert.Null(canteen.Description);
            Assert.Equal(3, canteen.MaxLiquid);
            Assert.Equal([1, 2, 8], canteen.AllowedLiquidList);

            Assert.NotNull(loaded.ItemTableRefs);
            Assert.Equal(
                "/Game/Blueprints/Items/ItemTable_DLC.ItemTable_DLC",
                loaded.ItemTableRefs!["canteen"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromRegistry_RebuildsCatalogAndPopulatesTableIndex()
    {
        var registry = SampleRegistry();
        var catalog = ItemCatalog.FromRegistry(registry.Items!, registry.ItemTableRefs!);

        Assert.Equal(2, catalog.Count);
        Assert.Equal("Metal Scrap", catalog.Find("scrap_metal")!.DisplayName);
        // Case-insensitive lookup, mirroring the live catalog.
        Assert.NotNull(catalog.Find("SCRAP_METAL"));

        // The writers read row tables through ItemTableIndex; the registry path must set it so a
        // DLC item still points at the table that actually holds its row.
        Assert.Equal(
            "/Game/Blueprints/Items/ItemTable_DLC.ItemTable_DLC",
            ItemTableIndex.TableRefFor("canteen"));
    }

    [Fact]
    public void TryLoad_RejectsUnsupportedSchemaVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"reg-{System.Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ \"SchemaVersion\": 9999, \"Items\": [] }");
            Assert.Null(GameDataRegistry.TryLoad(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryLoad_MissingFile_ReturnsNull()
        => Assert.Null(GameDataRegistry.TryLoad(
            Path.Combine(Path.GetTempPath(), $"absent-{System.Guid.NewGuid():N}.json")));
}
