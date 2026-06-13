using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Internationalization;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Discovery probes for the player skill system: which assets describe skills, what
/// the canonical skill ordering is (player saves store skills positionally - every
/// entry's SkillName field is the blueprint default "skill_sprinting"), and what
/// display names the string table carries.
/// </summary>
public class SkillProbeTests
{
    private readonly ITestOutputHelper _output;

    public SkillProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Survey_SkillAssets()
    {
        using var provider = CreateRawProvider();
        if (provider is null) { _output.WriteLine("No local AF install."); return; }

        var matches = provider.Files.Keys
            .Where(p => p.Contains("skill", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _output.WriteLine($"{matches.Count} skill-related assets:");
        foreach (var m in matches) _output.WriteLine("  " + m);
    }

    [Fact]
    public void Dump_StringTable_Skills()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) { _output.WriteLine("No install/mappings."); return; }

        var pkg = provider.LoadPackage("AbioticFactor/Content/StringTables/StringTable_Skills");
        foreach (var export in pkg.GetExports())
        {
            _output.WriteLine($"export: {export.Name} ({export.GetType().Name})");
            if (export is UStringTable st)
            {
                foreach (var kv in st.StringTable.KeysToEntries)
                {
                    _output.WriteLine($"  {kv.Key} = {kv.Value}");
                }
            }
        }
    }

    [Fact]
    public void Dump_CharacterSave_DefaultSkillsArray()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) { _output.WriteLine("No install/mappings."); return; }

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Saves/Abiotic_CharacterSave");
        foreach (var export in pkg.GetExports())
        {
            _output.WriteLine($"export: {export.Name} ({export.GetType().Name})");
            DumpSkillProps(export);
        }
    }

    [Fact]
    public void Dump_SkillEnumAndDataTable()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) { _output.WriteLine("No install/mappings."); return; }

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Data/E_CharacterSkills");
        foreach (var export in pkg.GetExports())
        {
            if (export is CUE4Parse.UE4.Objects.UObject.UEnum e)
            {
                _output.WriteLine($"enum {e.Name}:");
                foreach (var (n, v) in e.Names) _output.WriteLine($"  [{v}] {n.Text}");
            }
        }

        var dtPkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/Customization/DT_Skills");
        foreach (var export in dtPkg.GetExports())
        {
            if (export is CUE4Parse.UE4.Assets.Exports.Engine.UDataTable dt)
            {
                _output.WriteLine($"datatable {export.Name}: {dt.RowMap.Count} rows");
                foreach (var kv in dt.RowMap)
                {
                    _output.WriteLine($"  row: {kv.Key.Text}");
                    foreach (var p in kv.Value.Properties)
                    {
                        _output.WriteLine($"    {p.Name.Text} = {Render(p.Tag?.GenericValue)}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Dump_StoryProgressionAndTraits()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) { _output.WriteLine("No install/mappings."); return; }

        var candidates = provider.Files.Keys
            .Where(p => (p.Contains("StoryProgression", StringComparison.OrdinalIgnoreCase)
                      || p.Contains("Trait", StringComparison.OrdinalIgnoreCase))
                     && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var c in candidates) _output.WriteLine("candidate: " + c);

        foreach (var c in candidates.Where(p => p.Contains("DataTables", StringComparison.OrdinalIgnoreCase)))
        {
            var packagePath = c[..c.LastIndexOf('.')];
            try
            {
                var pkg = provider.LoadPackage(packagePath);
                foreach (var export in pkg.GetExports())
                {
                    if (export is not CUE4Parse.UE4.Assets.Exports.Engine.UDataTable dt) continue;
                    _output.WriteLine($"--- datatable {packagePath} :: {export.Name}: {dt.RowMap.Count} rows");
                    foreach (var kv in dt.RowMap)
                    {
                        var first = kv.Value.Properties.FirstOrDefault(p =>
                            p.Name.Text.Contains("Name", StringComparison.OrdinalIgnoreCase) ||
                            p.Name.Text.Contains("Title", StringComparison.OrdinalIgnoreCase));
                        _output.WriteLine($"  row: {kv.Key.Text}  ({first?.Name.Text}={Render(first?.Tag?.GenericValue)})");
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ! {packagePath}: {ex.Message}");
            }
        }
    }

    [Fact]
    public void Dump_TraitRowFields()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) { _output.WriteLine("No install/mappings."); return; }

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/Traits/CDT_AllTraits");
        foreach (var export in pkg.GetExports())
        {
            if (export is not CUE4Parse.UE4.Assets.Exports.Engine.UDataTable dt) continue;
            var first = dt.RowMap.Skip(11).First(); // a real trait row, not a PhD
            _output.WriteLine($"row {first.Key.Text}:");
            foreach (var p in first.Value.Properties)
            {
                var v = p.Tag?.GenericValue?.ToString() ?? "(null)";
                if (v.Length > 140) v = v[..140] + "…";
                _output.WriteLine($"  {p.Name.Text} = {v}");
            }
        }
    }

    [Fact]
    public void Dump_RecipeTables()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) { _output.WriteLine("No install/mappings."); return; }

        var candidates = provider.Files.Keys
            .Where(p => p.Contains("Recipe", StringComparison.OrdinalIgnoreCase)
                     && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var c in candidates) _output.WriteLine("candidate: " + c);

        foreach (var c in candidates.Where(p => p.Contains("DataTable", StringComparison.OrdinalIgnoreCase)))
        {
            var packagePath = c[..c.LastIndexOf('.')];
            try
            {
                var pkg = provider.LoadPackage(packagePath);
                foreach (var export in pkg.GetExports())
                {
                    if (export is not CUE4Parse.UE4.Assets.Exports.Engine.UDataTable dt) continue;
                    _output.WriteLine($"--- datatable {packagePath} :: {export.Name}: {dt.RowMap.Count} rows");
                    foreach (var kv in dt.RowMap.Take(8))
                    {
                        _output.WriteLine($"  row: {kv.Key.Text}  fields: {string.Join(", ", kv.Value.Properties.Take(8).Select(p => p.Name.Text))}");
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ! {packagePath}: {ex.Message}");
            }
        }
    }

    [Fact]
    public void Dump_PlayerCharacter_DefaultSkillsArray()
    {
        using var provider = CreateRawProvider();
        if (provider is null || provider.MappingsContainer is null) { _output.WriteLine("No install/mappings."); return; }

        // Find the player character blueprint package(s).
        var candidates = provider.Files.Keys
            .Where(p => p.Contains("PlayerCharacter", StringComparison.OrdinalIgnoreCase)
                     && p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var c in candidates) _output.WriteLine("candidate: " + c);

        foreach (var c in candidates.Take(6))
        {
            var packagePath = c[..c.LastIndexOf('.')];
            try
            {
                var pkg = provider.LoadPackage(packagePath);
                foreach (var export in pkg.GetExports())
                {
                    if (!export.Name.StartsWith("Default__", StringComparison.Ordinal)) continue;
                    _output.WriteLine($"--- {packagePath} :: {export.Name}");
                    DumpSkillProps(export);
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  ! {packagePath}: {ex.Message}");
            }
        }
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

    private void DumpSkillProps(UObject export)
    {
        foreach (var prop in export.Properties)
        {
            var name = prop.Name.Text;
            if (!name.Contains("Skill", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("CharacterSaveData", StringComparison.OrdinalIgnoreCase)) continue;

            _output.WriteLine($"  prop: {name} ({prop.Tag?.GetType().Name})");
            DumpValue(prop.Tag?.GenericValue, 2);
        }
    }

    private void DumpValue(object? value, int indent)
    {
        var pad = new string(' ', indent * 2);
        switch (value)
        {
            case UScriptArray arr:
                _output.WriteLine($"{pad}array x{arr.Properties.Count}");
                for (var i = 0; i < arr.Properties.Count; i++)
                {
                    _output.WriteLine($"{pad}[{i}]");
                    DumpValue(arr.Properties[i].GenericValue, indent + 1);
                }
                break;
            case CUE4Parse.UE4.Assets.Objects.FScriptStruct ss:
                DumpValue(ss.StructType, indent);
                break;
            case FStructFallback sf:
                foreach (var p in sf.Properties)
                {
                    _output.WriteLine($"{pad}{p.Name.Text} = {Render(p.Tag?.GenericValue)}");
                    if (p.Tag?.GenericValue is UScriptArray or FStructFallback)
                    {
                        DumpValue(p.Tag.GenericValue, indent + 1);
                    }
                }
                break;
            default:
                _output.WriteLine($"{pad}{Render(value)}");
                break;
        }
    }

    private static string Render(object? v) => v switch
    {
        null => "(null)",
        UScriptArray a => $"(array x{a.Properties.Count})",
        FStructFallback => "(struct)",
        _ => v.ToString() ?? "(?)",
    };
}
