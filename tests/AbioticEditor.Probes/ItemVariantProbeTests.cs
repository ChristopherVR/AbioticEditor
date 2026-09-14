using AbioticEditor.Core.Assets;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.WorldSaves;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Research probe for the item visual-variant system. The save stores a row name from
/// DT_TextureVariants on each item instance, but the editor also needs to know which rows are
/// valid for a particular item before it can offer a safe picker.
/// </summary>
public sealed class ItemVariantProbeTests
{
    private const string VariantTable =
        "AbioticFactor/Content/Blueprints/DataTables/Customization/DT_TextureVariants";
    private const string ItemTable =
        "AbioticFactor/Content/Blueprints/Items/ItemTable_Global";

    private readonly ITestOutputHelper _output;

    public ItemVariantProbeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Dump_TextureVariants_And_ItemVariantFields()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null)
        {
            _output.WriteLine("No game install or mappings; skipping.");
            return;
        }

#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        var variants = provider.LoadPackage(VariantTable)
            .GetExports().OfType<UDataTable>().First();
        _output.WriteLine($"DT_TextureVariants: {variants.RowMap.Count} rows, struct={variants.RowStructName}");
        foreach (var (key, row) in variants.RowMap.OrderBy(kv => kv.Key.Text, StringComparer.OrdinalIgnoreCase))
        {
            _output.WriteLine($"VARIANT {key.Text}");
            DumpStruct(row, "  ", 0);
        }

        var items = provider.LoadPackage(ItemTable)
            .GetExports().OfType<UDataTable>().First();
        _output.WriteLine($"ITEM TEXTURE-VARIANT FIELDS ({items.RowMap.Count} rows scanned)");
        foreach (var (key, row) in items.RowMap.OrderBy(kv => kv.Key.Text, StringComparer.OrdinalIgnoreCase))
        {
            var matches = row.Properties
                .Where(p => p.Name.Text.Contains("TextureVariant", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0) continue;

            foreach (var property in matches)
            {
                var formatted = Format(property.Tag?.GenericValue, 0);
                if (IsEmptyVariant(formatted)) continue;
                _output.WriteLine($"ITEM {key.Text}: {formatted}");
            }
        }
    }

    [Fact]
    public void Dump_VariantRows_ObservedOnFixtureItems()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var observed = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav"))
        {
            var save = PlayerSaveReader.ReadFromFile(path);
            Add(save.Inventory.Equipment);
            Add(save.Inventory.Hotbar);
            Add(save.Inventory.Main);
            Add(save.TransmogSlots);
        }

        foreach (var path in Directory.EnumerateFiles(Fixtures.CascadeDir!, "WorldSave_*.sav"))
        {
            var save = WorldSaveReader.ReadFromFile(path);
            foreach (var inventory in save.Containers.SelectMany(c => c.Inventories)) Add(inventory.Slots);
            Add(save.DroppedItems.Select(d => d.Slot));
        }

        _output.WriteLine($"OBSERVED ITEM/VARIANT PAIRS: {observed.Sum(kv => kv.Value.Count)}");
        foreach (var (item, variants) in observed)
            _output.WriteLine($"  {item}: {string.Join(", ", variants)}");
        return;

        void Add(IEnumerable<InventoryItemSlot> slots)
        {
            foreach (var slot in slots)
            {
                if (string.IsNullOrWhiteSpace(slot.ItemId)
                    || string.IsNullOrWhiteSpace(slot.VariantRowName)
                    || string.Equals(slot.VariantRowName, "None", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!observed.TryGetValue(slot.ItemId, out var rows))
                {
                    rows = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    observed[slot.ItemId] = rows;
                }
                rows.Add(slot.VariantRowName);
            }
        }
    }

    [Fact]
    public void Dump_CatalogItems_MatchingKnownVariantFamilies()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall(includeMods: false);
        if (provider is null || !provider.HasMappings) return;

        var catalog = ItemCatalog.LoadFrom(provider);
        string[] terms =
        [
            "poster", "painting", "bonnet", "buckethat", "hydrohat", "backpack_small",
            "couch", "chair", "bench_locker", "fish_ante", "fish_rad", "fish_reaper",
            "photo", "frame", "arcade", "hardhat", "karate", "labmask", "floppy", "securitycap",
            "puffycoat", "safetyvest", "rocket_ammo", "stapler", "toolbox", "tv_",
            "warningsign", "fridge", "cafeteria", "military_cot",
        ];
        foreach (var item in catalog.Entries
                     .Where(item => terms.Any(term => item.Id.Contains(term, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
            _output.WriteLine($"{item.Id} | {item.DisplayName}");
    }

    [Fact]
    public void Dump_AssetsNamedForItemVariants()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall(includeMods: false);
        if (provider is null) return;

        foreach (var path in provider.AssetPaths
                     .Where(path => path.Contains("variant", StringComparison.OrdinalIgnoreCase)
                                    || path.Contains("paint", StringComparison.OrdinalIgnoreCase)
                                    || path.Contains("poster", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            _output.WriteLine(path);
    }

    [Fact]
    public void Dump_PaintedDeployables_Table()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) return;

#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        var table = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/DT_PaintedDeployables")
            .GetExports().OfType<UDataTable>().First();
        _output.WriteLine($"DT_PaintedDeployables: {table.RowMap.Count} rows, struct={table.RowStructName}");
        foreach (var (key, row) in table.RowMap.OrderBy(kv => kv.Key.Text, StringComparer.OrdinalIgnoreCase))
        {
            _output.WriteLine($"ROW {key.Text}");
            DumpStruct(row, "  ", 0);
        }
    }

    [Fact]
    public void Dump_PosterAndPainting_BlueprintDefaults()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) return;

#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        string[] paths =
        [
            "AbioticFactor/Content/Blueprints/DeployedObjects/Furniture/Deployed_Poster",
            "AbioticFactor/Content/Blueprints/DeployedObjects/Furniture/Deployed_Painting_Desk",
            "AbioticFactor/Content/Blueprints/DeployedObjects/Furniture/Deployed_Painting_Landscape",
            "AbioticFactor/Content/Blueprints/DeployedObjects/Furniture/Deployed_Painting_Landscape_Fancy",
            "AbioticFactor/Content/Blueprints/DeployedObjects/Furniture/Deployed_Painting_Landscape_Large",
            "AbioticFactor/Content/Blueprints/DeployedObjects/Furniture/Deployed_Painting_Landscape_Large_Fancy",
            "AbioticFactor/Content/Blueprints/DeployedObjects/Furniture/Deployed_Painting_Square",
            "AbioticFactor/Content/Blueprints/DeployedObjects/Furniture/Deployed_Painting_Square_Fancy",
            "AbioticFactor/Content/Blueprints/DeployedObjects/Furniture/Deployed_Painting_Vertical",
            "AbioticFactor/Content/Blueprints/DeployedObjects/Furniture/Deployed_Painting_Vertical_Fancy",
            "AbioticFactor/Content/Blueprints/Items/MiscPickups/Item_Paint",
        ];
        foreach (var path in paths)
        {
            _output.WriteLine($"PACKAGE {path}");
            foreach (var export in provider.LoadPackage(path).GetExports())
            {
                _output.WriteLine($"  EXPORT {export.Name} ({export.ExportType})");
                foreach (var property in export.Properties)
                {
                    if (!property.Name.Text.Contains("Variant", StringComparison.OrdinalIgnoreCase)
                        && !property.Name.Text.Contains("Texture", StringComparison.OrdinalIgnoreCase)
                        && !property.Name.Text.Contains("Material", StringComparison.OrdinalIgnoreCase)
                        && !property.Name.Text.Contains("Paint", StringComparison.OrdinalIgnoreCase)
                        && !property.Name.Text.Contains("Mesh", StringComparison.OrdinalIgnoreCase))
                        continue;
                    _output.WriteLine($"    {property.Name.Text} = {Format(property.Tag?.GenericValue, 0)}");
                }
            }
        }
    }

    [Fact]
    public void Dump_VariantRows_NotUsedAsFixedItemDefaults()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) return;

#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        var variants = provider.LoadPackage(VariantTable).GetExports().OfType<UDataTable>().First();
        var items = provider.LoadPackage(ItemTable).GetExports().OfType<UDataTable>().First();
        var fixedDefaults = items.RowMap.Values
            .Select(row => row.Properties.FirstOrDefault(
                p => p.Name.Text.Contains("TextureVariant", StringComparison.OrdinalIgnoreCase)))
            .Select(property => ReadRowName(property?.Tag?.GenericValue))
            .Where(row => !string.IsNullOrWhiteSpace(row) && !string.Equals(row, "None", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = variants.RowMap.Keys
            .Select(key => key.Text)
            .Where(row => !fixedDefaults.Contains(row))
            .OrderBy(row => row, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _output.WriteLine($"VARIANT ROWS: {variants.RowMap.Count}");
        _output.WriteLine($"FIXED ITEM DEFAULTS: {fixedDefaults.Count}");
        _output.WriteLine($"NOT FIXED ITEM DEFAULTS: {candidates.Count}");
        foreach (var row in candidates) _output.WriteLine($"  {row}");
    }

    private static string? ReadRowName(object? value)
    {
        if (value is FScriptStruct scriptStruct) value = scriptStruct.StructType;
        if (value is not FStructFallback structure) return null;
        return structure.Properties.FirstOrDefault(
            property => property.Name.Text.Equals("RowName", StringComparison.OrdinalIgnoreCase))
            ?.Tag?.GenericValue?.ToString();
    }

    private void DumpStruct(FStructFallback value, string indent, int depth)
    {
        foreach (var property in value.Properties)
            _output.WriteLine($"{indent}{property.Name.Text} = {Format(property.Tag?.GenericValue, depth)}");
    }

    private static bool IsEmptyVariant(string formatted)
        => formatted.Length == 0
           || formatted == "<null>"
           || formatted.Contains("RowName: None", StringComparison.OrdinalIgnoreCase)
           || formatted.Contains("RowName: ", StringComparison.OrdinalIgnoreCase)
              && formatted.EndsWith("RowName:  }", StringComparison.Ordinal);

    private static string Format(object? value, int depth)
    {
        if (value is null) return "<null>";
        if (depth >= 4) return value.ToString() ?? value.GetType().Name;
        if (value is FScriptStruct scriptStruct)
            return Format(scriptStruct.StructType, depth + 1);
        if (value is FStructFallback structure)
            return "{ " + string.Join(", ", structure.Properties.Select(
                p => $"{p.Name.Text}: {Format(p.Tag?.GenericValue, depth + 1)}")) + " }";
        if (value is UScriptArray array)
            return "[" + string.Join(", ", array.Properties.Select(p => Format(p.GenericValue, depth + 1))) + "]";
        return value.ToString() ?? value.GetType().Name;
    }
}
