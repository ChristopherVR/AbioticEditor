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
/// Discovery probes for "codex" content: emails, journals, compendium entries, and the
/// story-progression table's full column set (for chapter->flag sync).
/// </summary>
public class CodexProbeTests
{
    private readonly ITestOutputHelper _output;

    public CodexProbeTests(ITestOutputHelper output)
    {
        _output = output;
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
    public void Survey_CodexAssets()
    {
        using var provider = CreateRawProvider();
        if (provider is null) { _output.WriteLine("No local AF install."); return; }

        var matches = provider.Files.Keys
            .Where(p => (p.Contains("Email", StringComparison.OrdinalIgnoreCase)
                      || p.Contains("Journal", StringComparison.OrdinalIgnoreCase)
                      || p.Contains("Compendium", StringComparison.OrdinalIgnoreCase)
                      || p.Contains("Narrative", StringComparison.OrdinalIgnoreCase))
                     && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                     && (p.Contains("DataTable", StringComparison.OrdinalIgnoreCase)
                      || p.Contains("StringTable", StringComparison.OrdinalIgnoreCase)
                      || p.Contains("/Data", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var m in matches) _output.WriteLine(m);
    }

    [Fact]
    public void Dump_EmailJournalCompendiumRows()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        foreach (var path in new[]
        {
            "AbioticFactor/Content/Blueprints/DataTables/Communications/DT_Emails",
            "AbioticFactor/Content/Blueprints/DataTables/Communications/DT_JournalEntries",
            "AbioticFactor/Content/Blueprints/DataTables/DT_Compendium",
        })
        {
            var pkg = provider.LoadPackage(path);
            foreach (var export in pkg.GetExports())
            {
                if (export is not UDataTable dt) continue;
                _output.WriteLine($"=== {path}: {dt.RowMap.Count} rows ===");
                foreach (var kv in dt.RowMap.Take(2))
                {
                    _output.WriteLine($"row {kv.Key.Text}:");
                    foreach (var p in kv.Value.Properties)
                    {
                        var v = Render(p.Tag?.GenericValue);
                        _output.WriteLine($"  {p.Name.Text} = {v}");
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
                return $"[{string.Join(", ", arr.Properties.Take(4).Select(p => Render(p.GenericValue, depth + 1)))}]"
                       + (arr.Properties.Count > 4 ? $" (+{arr.Properties.Count - 4})" : "");
            default:
                var s = value.ToString() ?? "(?)";
                return s.Length > 220 ? s[..220] + "…" : s;
        }
    }

    [Fact]
    public void Dump_StoryProgressionWorldFlags()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/DT_StoryProgression");
        foreach (var export in pkg.GetExports())
        {
            if (export is not UDataTable dt) continue;
            foreach (var kv in dt.RowMap)
            {
                var flag = kv.Value.Properties.FirstOrDefault(p => p.Name.Text.StartsWith("WorldFlag_", StringComparison.Ordinal));
                _output.WriteLine($"{kv.Key.Text}: {Render(flag?.Tag?.GenericValue)}");
            }
        }
    }

    [Fact]
    public void Survey_MapTables()
    {
        using var provider = CreateRawProvider();
        if (provider is null) return;

        foreach (var p in provider.Files.Keys
            .Where(p => p.Contains("Map", StringComparison.OrdinalIgnoreCase)
                     && (p.Contains("DataTable", StringComparison.OrdinalIgnoreCase)
                      || p.Contains("DT_", StringComparison.OrdinalIgnoreCase))
                     && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine(p);
        }
    }

    [Fact]
    public void Dump_MapPamphlets()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/Environment/DT_MapPamphlets");
        foreach (var export in pkg.GetExports())
        {
            if (export is not UDataTable dt) continue;
            _output.WriteLine($"{dt.RowMap.Count} rows");
            foreach (var kv in dt.RowMap)
            {
                var first = kv.Value.Properties.Take(3).Select(x => $"{x.Name.Text}={x.Tag?.GenericValue}");
                _output.WriteLine($"  {kv.Key.Text}: {string.Join(", ", first)}");
            }
        }
    }

    [Fact]
    public void Dump_CompendiumTagsAndKillReqs()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/DT_Compendium");
        foreach (var export in pkg.GetExports())
        {
            if (export is not UDataTable dt) continue;
            var tags = new Dictionary<string, int>();
            foreach (var kv in dt.RowMap)
            {
                var tag = kv.Value.Properties.FirstOrDefault(p2 => p2.Name.Text == "Tags")?.Tag?.GenericValue?.ToString() ?? "(none)";
                tags[tag] = tags.TryGetValue(tag, out var n) ? n + 1 : 1;
            }
            foreach (var t in tags) _output.WriteLine($"{t.Key}: {t.Value}");

            var fish = dt.RowMap.Where(kv => kv.Key.Text.Contains("fish", StringComparison.OrdinalIgnoreCase)).Take(4);
            foreach (var kv in fish)
            {
                var title = kv.Value.Properties.FirstOrDefault(p2 => p2.Name.Text == "Title")?.Tag?.GenericValue;
                _output.WriteLine($"fish row {kv.Key.Text}: Title={title}");
            }

            var killReq = dt.RowMap.FirstOrDefault(kv => kv.Key.Text == "Peccary");
            foreach (var p2 in killReq.Value.Properties.Where(x => x.Name.Text.Contains("Kill")))
            {
                _output.WriteLine($"Peccary {p2.Name.Text} = {Render(p2.Tag?.GenericValue)}");
            }
        }
    }

    [Fact]
    public void Dump_FishTables()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        foreach (var f in provider.Files.Keys.Where(p2 =>
            p2.Contains("Fish", StringComparison.OrdinalIgnoreCase)
            && p2.Contains("DataTable", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine("candidate: " + f);
            var pkg = provider.LoadPackage(f[..f.LastIndexOf('.')]);
            foreach (var export in pkg.GetExports())
            {
                if (export is not UDataTable dt) continue;
                _output.WriteLine($"  {dt.RowMap.Count} rows");
                foreach (var kv in dt.RowMap.Take(3))
                {
                    var fields = kv.Value.Properties.Take(6).Select(x => $"{x.Name.Text}={Render(x.Tag?.GenericValue)}");
                    _output.WriteLine($"  row {kv.Key.Text}: {string.Join(", ", fields)}"[..Math.Min(400, ($"  row {kv.Key.Text}: {string.Join(", ", fields)}").Length)]);
                }
            }
        }
    }

    [Fact]
    public void Dump_TraderTables()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        foreach (var path in new[]
        {
            "AbioticFactor/Content/Blueprints/DataTables/NarrativeNPCs/DT_NPC_Traders",
            "AbioticFactor/Content/Blueprints/DataTables/NarrativeNPCs/DT_NPC_TraderItems",
        })
        {
            var pkg = provider.LoadPackage(path);
            foreach (var export in pkg.GetExports())
            {
                if (export is not UDataTable dt) continue;
                _output.WriteLine($"=== {path}: {dt.RowMap.Count} rows ===");
                foreach (var kv in dt.RowMap.Take(6))
                {
                    var fields = kv.Value.Properties.Take(10).Select(x => $"{x.Name.Text}={Render(x.Tag?.GenericValue)}");
                    var line = $"row {kv.Key.Text}: {string.Join(", ", fields)}";
                    _output.WriteLine(line.Length > 600 ? line[..600] : line);
                }
                if (dt.RowMap.Count > 6)
                {
                    _output.WriteLine("all rows: " + string.Join(", ", dt.RowMap.Keys.Select(k => k.Text)));
                }
            }
        }
    }

    [Fact]
    public void Dump_RecipeIngredientShape()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/DT_Recipes");
        foreach (var export in pkg.GetExports())
        {
            if (export is not UDataTable dt) continue;
            var row = dt.RowMap.First(kv => kv.Key.Text == "recipe_crossbow");
            foreach (var p in row.Value.Properties.Where(x => x.Name.Text.StartsWith("RecipeItems_") || x.Name.Text.StartsWith("BenchesRequired_")))
            {
                _output.WriteLine($"{p.Name.Text} = {Render(p.Tag?.GenericValue)}");
            }
        }
    }

    [Fact]
    public void Dump_StoryProgressionColumns()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/DT_StoryProgression");
        foreach (var export in pkg.GetExports())
        {
            if (export is not UDataTable dt) continue;
            foreach (var kv in dt.RowMap.Take(4))
            {
                _output.WriteLine($"row {kv.Key.Text}:");
                foreach (var p in kv.Value.Properties)
                {
                    var v = p.Tag?.GenericValue?.ToString() ?? "(null)";
                    if (v.Length > 200) v = v[..200] + "…";
                    _output.WriteLine($"  {p.Name.Text} = {v}");
                }
            }
        }
    }
}
