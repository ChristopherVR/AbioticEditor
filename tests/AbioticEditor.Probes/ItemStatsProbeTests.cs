using AbioticEditor.Core.Assets;
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
/// Research probe for the wiki-style item stat block (weapon/armor/consumable/repair/salvage
/// fields). Dumps the raw <c>ItemTable_Global</c> sub-struct property names (with their
/// blueprint-compiler hash suffixes) and a few sample values for a handful of representative
/// items, so <c>ItemCatalog.BuildEntry</c> can be extended against real, observed names instead
/// of guesses. Opt-in: every fact skips gracefully when the game/mappings are absent.
/// </summary>
public sealed class ItemStatsProbeTests
{
    private const string ItemTable = "AbioticFactor/Content/Blueprints/Items/ItemTable_Global";

    private static readonly string[] Weapons = ["sledgehammer", "knife", "rifle_assault"];
    private static readonly string[] Armor = ["armor_helmet_military", "armor_chest_highvisvest", "heatershield"];
    private static readonly string[] Consumables = ["soup_pestgoulash", "soupbowl", "soup_pea"];
    private static readonly string[] Tools = ["pickaxe", "pipewrench"];

    private readonly ITestOutputHelper _output;

    public ItemStatsProbeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Dump_ItemStats_ForSampleItems()
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

        var items = provider.LoadPackage(ItemTable).GetExports().OfType<UDataTable>().First();
        _output.WriteLine($"ItemTable_Global: {items.RowMap.Count} rows, struct={items.RowStructName}");

        var wanted = Weapons.Concat(Armor).Concat(Consumables).Concat(Tools)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, row) in items.RowMap.OrderBy(kv => kv.Key.Text, StringComparer.OrdinalIgnoreCase))
        {
            if (!wanted.Contains(key.Text)) continue;
            _output.WriteLine($"=== ITEM {key.Text} ===");
            foreach (var property in row.Properties)
                _output.WriteLine($"  {property.Name.Text} = {Format(property.Tag?.GenericValue, 0)}");
        }
    }

    /// <summary>Dumps just the top-level property NAMES (with hash suffixes) across every row,
    /// grouped by which nested-struct category they look like they belong to, so the sub-struct
    /// field names used by weapon/armor/consumable/repair/salvage parsing are exact.</summary>
    [Fact]
    public void Dump_ItemStats_TopLevelPropertyNames()
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

        var items = provider.LoadPackage(ItemTable).GetExports().OfType<UDataTable>().First();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in items.RowMap.Values)
        foreach (var property in row.Properties)
            names.Add(property.Name.Text);

        _output.WriteLine($"DISTINCT TOP-LEVEL PROPERTY NAMES: {names.Count}");
        foreach (var name in names) _output.WriteLine($"  {name}");
    }

    private static string Format(object? value, int depth)
    {
        if (value is null) return "<null>";
        if (depth >= 5) return value.ToString() ?? value.GetType().Name;
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
