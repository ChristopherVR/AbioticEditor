using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class ItemCatalogProbeTests
{
    private readonly ITestOutputHelper _output;

    public ItemCatalogProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Dump_ItemTableGlobalStructure()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        if (paks is null) return;

        var mappings = GameAssetProvider.FindConventionalMappings();
        if (mappings is null)
        {
            _output.WriteLine("No mappings.usmap — can't decode datatable properties.");
            return;
        }

#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        const string path = "AbioticFactor/Content/Blueprints/Items/ItemTable_Global";

        try
        {
            var pkg = provider.LoadPackage(path);
            var exports = pkg.GetExports().ToList();
            _output.WriteLine($"Package loaded. {exports.Count} export(s):");
            foreach (var e in exports.Take(5))
            {
                _output.WriteLine($"  {e.Name} ({e.ExportType})");
            }

            var dt = exports.OfType<UDataTable>().FirstOrDefault();
            if (dt is null)
            {
                _output.WriteLine("No UDataTable export found.");
                return;
            }

            _output.WriteLine($"DataTable has {dt.RowMap.Count} row(s)");

            // Print the first few row keys and one row's field names
            var first = dt.RowMap.Take(5).ToList();
            foreach (var kv in first)
            {
                _output.WriteLine($"  row: {kv.Key} ({kv.Value.GetType().Name})");
            }

            // Inspect first row's properties
            if (first.Count > 0)
            {
                var row = first[0].Value;
                _output.WriteLine("");
                _output.WriteLine($"--- First row '{first[0].Key}' fields ---");
                foreach (var p in row.Properties)
                {
                    var valueStr = p.Tag?.GenericValue?.ToString();
                    if (valueStr is not null && valueStr.Length > 80) valueStr = valueStr[..80] + "…";
                    _output.WriteLine($"  {p.Name}: {valueStr ?? "<null>"}");
                }
            }

            // Look up a few known item IDs from the player save
            string[] knownItems = { "armor_chest_groupe", "knife_super", "ammo_9mm", "personalteleporter" };
            _output.WriteLine("");
            _output.WriteLine("--- Known item lookups ---");
            foreach (var id in knownItems)
            {
                var found = dt.RowMap.FirstOrDefault(kv => kv.Key.Text == id);
                _output.WriteLine($"  '{id}' → {(found.Value is null ? "MISS" : "HIT (" + found.Value.Properties.Count + " props)")}");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Load failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
