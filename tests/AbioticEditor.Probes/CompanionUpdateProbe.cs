using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Anniversary-update research probe: enumerate the paks for the new companions
/// (Speedogi, Sir Ogi, Verdant Skink) and any companion/pet data tables so the
/// pet catalog can be made data-driven. Discovery output only, not pass/fail.
/// </summary>
public class CompanionUpdateProbe
{
    private readonly ITestOutputHelper _output;

    public CompanionUpdateProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Survey_NewCompanionsAndPetDataTables()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null)
        {
            _output.WriteLine("No local install found - skipping.");
            return;
        }

        var all = provider.AssetPaths.ToList();
        _output.WriteLine($"Total asset paths: {all.Count}");

        Dump("Ogi anywhere", all, p => p.Contains("Ogi", StringComparison.OrdinalIgnoreCase));
        Dump("Skink anywhere", all, p => p.Contains("Skink", StringComparison.OrdinalIgnoreCase));
        Dump("Verdant anywhere", all, p => p.Contains("Verdant", StringComparison.OrdinalIgnoreCase));
        Dump("Companion anywhere", all, p => p.Contains("Companion", StringComparison.OrdinalIgnoreCase));
        Dump("Pet in filename", all, p =>
            System.IO.Path.GetFileName(p).Contains("Pet", StringComparison.OrdinalIgnoreCase));

        Dump("All NPC_* blueprints anywhere", all, p =>
            System.IO.Path.GetFileName(p).StartsWith("NPC_", StringComparison.OrdinalIgnoreCase) &&
            p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase),
            limit: 400);

        Dump("DataTables mentioning pets/companions/tames", all, p =>
            System.IO.Path.GetFileName(p).StartsWith("DT_", StringComparison.OrdinalIgnoreCase) &&
            (p.Contains("Pet", StringComparison.OrdinalIgnoreCase) ||
             p.Contains("Companion", StringComparison.OrdinalIgnoreCase) ||
             p.Contains("Tame", StringComparison.OrdinalIgnoreCase) ||
             p.Contains("NPC", StringComparison.OrdinalIgnoreCase) ||
             p.Contains("Creature", StringComparison.OrdinalIgnoreCase)));
    }

    private static DefaultFileProvider? CreateRawProvider()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        if (paks is null) return null;

#pragma warning disable CS0618
        var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (mappings is not null)
        {
            provider.MappingsContainer = new CUE4Parse.MappingsProvider.FileUsmapTypeMappingsProvider(mappings);
        }
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
        return provider;
    }

    [Fact]
    public void Dump_DTPets_AllRows()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/DT_Pets");
        foreach (var export in pkg.GetExports())
        {
            if (export is not UDataTable dt) continue;
            _output.WriteLine($"=== DT_Pets: {dt.RowMap.Count} rows (struct {dt.RowStructName}) ===");
            foreach (var kv in dt.RowMap)
            {
                _output.WriteLine($"row {kv.Key.Text}:");
                foreach (var p in kv.Value.Properties)
                {
                    _output.WriteLine($"  {p.Name.Text} = {Render(p.Tag?.GenericValue)}");
                }
            }
        }
    }

    [Fact]
    public void Dump_DTNPCList_SampleRows()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/DT_NPCList");
        foreach (var export in pkg.GetExports())
        {
            if (export is not UDataTable dt) continue;
            _output.WriteLine($"=== DT_NPCList: {dt.RowMap.Count} rows (struct {dt.RowStructName}) ===");
            foreach (var kv in dt.RowMap)
            {
                var name = kv.Value.Properties.FirstOrDefault(p =>
                    p.Name.Text.StartsWith("DisplayName_", StringComparison.Ordinal));
                var cls = kv.Value.Properties.FirstOrDefault(p =>
                    p.Name.Text.StartsWith("NPCSpawnClass_", StringComparison.Ordinal));
                _output.WriteLine($"NPCROW {kv.Key.Text} | {name?.Tag?.GenericValue} | {cls?.Tag?.GenericValue}");
            }
        }
    }

    [Fact]
    public void Dump_PetTaggedItemRows()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Items/ItemTable_Global");
        foreach (var export in pkg.GetExports())
        {
            if (export is not UDataTable dt) continue;
            _output.WriteLine($"=== ItemTable_Global: {dt.RowMap.Count} rows ===");
            var dumpedFull = false;
            foreach (var kv in dt.RowMap)
            {
                var tagsProp = kv.Value.Properties.FirstOrDefault(p =>
                    p.Name.Text.StartsWith("GameplayTags_", StringComparison.Ordinal));
                var tags = tagsProp?.Tag?.GenericValue?.ToString() ?? string.Empty;
                if (!tags.Contains("Item.Pet", StringComparison.OrdinalIgnoreCase)) continue;

                var nameProp = kv.Value.Properties.FirstOrDefault(p =>
                    p.Name.Text.StartsWith("ItemName_", StringComparison.Ordinal));
                _output.WriteLine($"PET ITEM row {kv.Key.Text}: name='{nameProp?.Tag?.GenericValue}' tags=[{tags}]");

                if (!dumpedFull)
                {
                    dumpedFull = true;
                    _output.WriteLine("  -- full field dump of this row --");
                    foreach (var p in kv.Value.Properties)
                    {
                        _output.WriteLine($"  {p.Name.Text} = {Render(p.Tag?.GenericValue)}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Dump_DTSkills_PerkLinks()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/Customization/DT_Skills");
        foreach (var export in pkg.GetExports())
        {
            if (export is not UDataTable dt) continue;
            _output.WriteLine($"=== DT_Skills: {dt.RowMap.Count} rows (struct {dt.RowStructName}) ===");
            foreach (var kv in dt.RowMap)
            {
                _output.WriteLine($"row {kv.Key.Text}:");
                foreach (var p in kv.Value.Properties)
                {
                    _output.WriteLine($"  {p.Name.Text} = {Render(p.Tag?.GenericValue)}");
                }
            }
        }
    }

    [Fact]
    public void Survey_PerkAndCustomizationTables()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        var all = provider.Files.Keys.ToList();
        Dump("Perk/Milestone assets", all, p =>
            (p.Contains("Perk", StringComparison.OrdinalIgnoreCase) ||
             p.Contains("Milestone", StringComparison.OrdinalIgnoreCase)) &&
            p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase));
        Dump("Customization tables", all, p =>
            p.Contains("DT_Customization", StringComparison.OrdinalIgnoreCase) &&
            p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase));

        foreach (var path in all.Where(p =>
                     System.IO.Path.GetFileName(p).StartsWith("DT_", StringComparison.OrdinalIgnoreCase) &&
                     (p.Contains("Perk", StringComparison.OrdinalIgnoreCase) ||
                      p.Contains("Milestone", StringComparison.OrdinalIgnoreCase)) &&
                     p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)))
        {
            var pkg = provider.LoadPackage(path[..^".uasset".Length]);
            foreach (var export in pkg.GetExports())
            {
                if (export is not UDataTable dt) continue;
                _output.WriteLine($"=== {path}: {dt.RowMap.Count} rows (struct {dt.RowStructName}) ===");
                foreach (var kv in dt.RowMap)
                {
                    _output.WriteLine($"row {kv.Key.Text}:");
                    foreach (var p in kv.Value.Properties)
                    {
                        _output.WriteLine($"  {p.Name.Text} = {Render(p.Tag?.GenericValue)}");
                    }
                }
            }
        }
    }

    private string Render(object? value, int depth = 0)
    {
        switch (value)
        {
            case null: return "(null)";
            case CUE4Parse.UE4.Assets.Objects.FScriptStruct ss:
                return Render(ss.StructType, depth);
            case CUE4Parse.UE4.Assets.Objects.FStructFallback sf:
                if (depth > 2) return "(struct)";
                var fields = sf.Properties.Select(p => $"{p.Name.Text}={Render(p.Tag?.GenericValue, depth + 1)}");
                return "{ " + string.Join(", ", fields) + " }";
            case CUE4Parse.UE4.Assets.Objects.UScriptArray arr:
                return $"[{string.Join(", ", arr.Properties.Take(8).Select(p => Render(p.GenericValue, depth + 1)))}]"
                       + (arr.Properties.Count > 8 ? $" (+{arr.Properties.Count - 8})" : "");
            default:
                var s = value.ToString() ?? "(?)";
                return s.Length > 300 ? s[..300] + "…" : s;
        }
    }

    private void Dump(string label, IEnumerable<string> files, Func<string, bool> predicate, int limit = 100)
    {
        var matches = files.Where(predicate)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _output.WriteLine("");
        _output.WriteLine($"=== {label}  ({matches.Count}) ===");
        foreach (var p in matches.Take(limit)) _output.WriteLine($"  {p}");
        if (matches.Count > limit) _output.WriteLine($"  ... and {matches.Count - limit} more");
    }
}
