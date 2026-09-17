using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>Discovery probe: does DT_ChemistryRecipes carry a mixing-duration/timing column
/// RecipeCatalog does not already read? See LiveChemistryBenchTab.razor's own remarks.</summary>
public sealed class ChemistryRecipeTimingProbe
{
    private readonly ITestOutputHelper _output;
    public ChemistryRecipeTimingProbe(ITestOutputHelper output) => _output = output;

    private static DefaultFileProvider? CreateRawProvider()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        if (paks is null) return null;
#pragma warning disable CS0618
        var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new CUE4Parse.UE4.Versions.VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (mappings is not null)
            provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x" + new string('0', 64)));
        return provider;
    }

    [Fact]
    public void Dump_ChemistryRecipeRowProperties()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) { _output.WriteLine("No local AF install."); return; }

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/DT_ChemistryRecipes");
        foreach (var export in pkg.GetExports())
        {
            if (export is not UDataTable dt) continue;
            _output.WriteLine($"=== DT_ChemistryRecipes: {dt.RowMap.Count} rows ===");
            foreach (var kv in dt.RowMap.Take(3))
            {
                _output.WriteLine($"row {kv.Key.Text}:");
                foreach (var p in kv.Value.Properties)
                    _output.WriteLine($"  {p.Name.Text} = {p.Tag?.GenericValue}");
            }
        }
    }
}
