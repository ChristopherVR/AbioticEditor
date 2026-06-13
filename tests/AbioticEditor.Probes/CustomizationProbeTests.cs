using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Investigation: where do player saves store
///   1. character customization (head/face, voice, clothing/suit + colors),
///   2. armor transmog state,
///   3. home bed / respawn point,
///   4. the player-set teleport location.
/// Findings are written up in dotnet/docs/research-customization.md.
/// </summary>
public class CustomizationProbeTests
{
    private static readonly string[] Keywords =
    {
        "Customiz", "Character", "Head", "Hair", "Face", "Voice", "Color", "Suit",
        "Outfit", "Cosmetic", "Transmog", "Bed", "Respawn", "Spawn", "Teleport",
        "Home", "Appearance",
    };

    private readonly ITestOutputHelper _output;

    public CustomizationProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ----------------------------------------------------------------- helpers

    private static bool MatchesKeyword(string name) =>
        Keywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));

    private void DumpTag(FPropertyTag tag, int depth, int maxDepth = 6)
    {
        DumpProperty(tag.Name.Value, tag.Type.ToString(), tag.Property, depth, maxDepth);
    }

    private void DumpProperty(string name, string? typeName, FProperty? prop, int depth, int maxDepth)
    {
        var pad = new string(' ', depth * 2);
        if (depth > maxDepth)
        {
            _output.WriteLine($"{pad}{name} ... (max depth)");
            return;
        }

        switch (prop)
        {
            case StructProperty { Value: PropertiesStruct ps }:
                _output.WriteLine($"{pad}{name} (Struct)");
                foreach (var child in ps.Properties) DumpTag(child, depth + 1, maxDepth);
                break;
            case StructProperty sp:
                _output.WriteLine($"{pad}{name} (Struct:{sp.Value?.GetType().Name}) = {Trunc(sp.Value?.ToString())}");
                break;
            case ArrayProperty ap when ap.Value is not null:
                _output.WriteLine($"{pad}{name} ({prop.GetType().Name}[{ap.Value.Length}])");
                for (var i = 0; i < ap.Value.Length; i++)
                {
                    if (ap.Value.GetValue(i) is FProperty el)
                        DumpProperty($"[{i}]", null, el, depth + 1, maxDepth);
                    else
                        _output.WriteLine($"{pad}  [{i}] = {Trunc(ap.Value.GetValue(i)?.ToString())}");
                }
                break;
            case MapProperty mp when mp.Value is not null:
                _output.WriteLine($"{pad}{name} (Map[{mp.Value.Count}])");
                foreach (var kvp in mp.Value)
                {
                    DumpProperty($"key={Trunc(kvp.Key.Value?.ToString())}", null, kvp.Value, depth + 1, maxDepth);
                }
                break;
            default:
                _output.WriteLine($"{pad}{name} ({prop?.GetType().Name}) = {Trunc(prop?.Value?.ToString())}");
                break;
        }
    }

    private static string Trunc(string? s, int max = 200)
    {
        if (s is null) return "(null)";
        return s.Length > max ? s[..max] + "..." : s;
    }

    private static IList<FPropertyTag> LoadTopLevel(string path)
    {
        using var fs = File.OpenRead(path);
        var save = SaveGame.LoadFrom(fs);
        return save.Properties!;
    }

    private static PropertiesStruct CharacterSaveData(IList<FPropertyTag> top) =>
        (PropertiesStruct)((StructProperty)top
            .First(t => t.Name.Value.StartsWith("CharacterSaveData")).Property!).Value!;

    // ------------------------------------------------------------------ probes

    [Fact]
    public void Probe1_PlayerSave_TopLevelInventory()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        foreach (var playerSave in Directory.EnumerateFiles(
                     Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav"))
        {
            _output.WriteLine($"##### {Path.GetFileName(playerSave)} #####");
            var top = LoadTopLevel(playerSave);
            _output.WriteLine("--- top-level tags ---");
            foreach (var tag in top)
            {
                _output.WriteLine($"  {tag.Name.Value} : {tag.Type}");
            }

            _output.WriteLine("--- CharacterSaveData children (name : type) ---");
            foreach (var tag in CharacterSaveData(top).Properties)
            {
                _output.WriteLine($"  {tag.Name.Value} : {tag.Type} ({tag.Property?.GetType().Name})");
            }
            _output.WriteLine("");
        }
    }

    [Fact]
    public void Probe2_PlayerSave_DeepDump_KeywordProperties()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        foreach (var playerSave in Directory.EnumerateFiles(
                     Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav"))
        {
            _output.WriteLine($"##### {Path.GetFileName(playerSave)} #####");
            var top = LoadTopLevel(playerSave);

            // Top-level properties outside CharacterSaveData.
            foreach (var tag in top.Where(t => !t.Name.Value.StartsWith("CharacterSaveData")))
            {
                _output.WriteLine($"--- top-level (non-CSD): {tag.Name.Value} ---");
                DumpTag(tag, 1);
            }

            foreach (var tag in CharacterSaveData(top).Properties)
            {
                var name = tag.Name.Value;
                // Skip the big inventory arrays; they are already mapped elsewhere.
                if (name.StartsWith("Inventory_") || name.StartsWith("HotbarInventory_")
                    || name.StartsWith("EquipmentInventory_")) continue;
                if (!MatchesKeyword(name)) continue;

                _output.WriteLine($"--- CSD: {name} ---");
                DumpTag(tag, 1);
            }
            _output.WriteLine("");
        }
    }

    [Fact]
    public void Probe3_WorldSaves_TopLevel_And_KeywordDump()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        foreach (var fileName in new[] { "WorldSave_MetaData.sav", "WorldSave_Facility.sav" })
        {
            var path = Path.Combine(Fixtures.CascadeDir!, fileName);
            if (!File.Exists(path)) continue;

            _output.WriteLine($"##### {fileName} #####");
            var top = LoadTopLevel(path);
            foreach (var tag in top)
            {
                _output.WriteLine($"  {tag.Name.Value} : {tag.Type}");
            }

            foreach (var tag in top)
            {
                var name = tag.Name.Value;
                if (name.StartsWith("DeployedObjectMap")) continue; // huge; probed separately
                if (!MatchesKeyword(name)) continue;
                _output.WriteLine($"--- {name} ---");
                DumpTag(tag, 1, maxDepth: 5);
            }
            _output.WriteLine("");
        }
    }

    [Fact]
    public void Probe4_Facility_BedDeployables()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;

        var top = LoadTopLevel(path);
        var tag = top.First(t => t.Name.Value.StartsWith("DeployedObjectMap"));
        var map = (MapProperty)tag.Property!;

        var found = 0;
        foreach (var kvp in map.Value!)
        {
            if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;
            var cls = ps.Properties.FirstOrDefault(p => p.Name.Value.StartsWith("Class_"))?.Property?.Value?.ToString();
            if (cls?.Contains("Bed", StringComparison.OrdinalIgnoreCase) != true
                && cls?.Contains("Sleep", StringComparison.OrdinalIgnoreCase) != true) continue;

            found++;
            _output.WriteLine($"=== DEPLOYABLE {kvp.Key.Value} class={cls} ===");
            foreach (var p in ps.Properties) DumpTag(p, 1, maxDepth: 5);
            _output.WriteLine("");
        }
        _output.WriteLine($"Total bed-like deployables: {found}");
    }

    [Fact]
    public void Probe6_ScientistCustomizationSav()
    {
        // Character appearance is NOT in the per-world player saves. The game keeps it
        // per Steam account on the local machine:
        //   %LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<steamid64>\ScientistCustomization_<n>.sav
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AbioticFactor", "Saved", "SaveGames");
        if (!Directory.Exists(root))
        {
            _output.WriteLine($"No local save games at {root}; skipping.");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, "ScientistCustomization_*.sav", SearchOption.AllDirectories))
        {
            _output.WriteLine($"##### {file} #####");
            foreach (var tag in LoadTopLevel(file))
            {
                DumpTag(tag, 1, maxDepth: 6);
            }
            _output.WriteLine("");
        }
    }

    [Fact]
    public void Probe7_TerminalRespawnID_ResolvesToDeployable()
    {
        Assert.NotNull(Fixtures.CascadeDir);

        // Collect each player's TerminalRespawnID.
        var respawnIds = new Dictionary<string, string>();
        foreach (var playerSave in Directory.EnumerateFiles(
                     Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav"))
        {
            var csd = CharacterSaveData(LoadTopLevel(playerSave));
            var id = csd.Properties.FirstOrDefault(t => t.Name.Value.StartsWith("TerminalRespawnID"))?
                .Property?.Value?.ToString();
            respawnIds[Path.GetFileNameWithoutExtension(playerSave)] = id ?? "(none)";
            _output.WriteLine($"{Path.GetFileName(playerSave)} TerminalRespawnID = {id}");
        }

        // Scan every world save's DeployedObjectMap for those GUIDs.
        foreach (var worldFile in Directory.EnumerateFiles(Fixtures.CascadeDir!, "WorldSave_*.sav"))
        {
            IList<FPropertyTag> top;
            try { top = LoadTopLevel(worldFile); }
            catch (Exception ex) { _output.WriteLine($"{Path.GetFileName(worldFile)}: load failed ({ex.Message})"); continue; }

            var tag = top.FirstOrDefault(t => t.Name.Value.StartsWith("DeployedObjectMap"));
            if (tag?.Property is not MapProperty map || map.Value is null) continue;

            foreach (var kvp in map.Value)
            {
                var key = kvp.Key.Value?.ToString();
                if (key is null) continue;
                foreach (var (player, rid) in respawnIds)
                {
                    if (!string.Equals(key, rid, StringComparison.OrdinalIgnoreCase)) continue;
                    var cls = kvp.Value is StructProperty sp && sp.Value is PropertiesStruct ps
                        ? ps.Properties.FirstOrDefault(p => p.Name.Value.StartsWith("Class_"))?.Property?.Value?.ToString()
                        : null;
                    _output.WriteLine($"MATCH: {player} respawn {rid} -> {Path.GetFileName(worldFile)} class={cls}");
                    if (kvp.Value is StructProperty sp2 && sp2.Value is PropertiesStruct ps2)
                    {
                        foreach (var p in ps2.Properties) DumpTag(p, 1, maxDepth: 4);
                    }
                }
            }
        }
    }

    [Fact]
    public void Probe8_HeadTable_RowStructure_And_VoiceAssets()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/Customization/DT_Customization_Head");
        foreach (var dt in pkg.GetExports().OfType<UDataTable>())
        {
            foreach (var kv in dt.RowMap.Take(3))
            {
                _output.WriteLine($"=== {kv.Key.Text} ===");
                foreach (var p in kv.Value.Properties)
                {
                    _output.WriteLine($"  {p.Name.Text} = {Trunc(p.Tag?.GenericValue?.ToString(), 160)}");
                }
            }
        }

        _output.WriteLine("--- pak entries containing 'voice' ---");
        foreach (var k in provider.Files.Keys
                     .Where(k => k.Contains("voice", StringComparison.OrdinalIgnoreCase))
                     .Take(60))
        {
            _output.WriteLine($"  {k}");
        }
    }

    [Fact]
    public void Probe9_HomeLocation_And_RespawnTerminalAssets()
    {
        Assert.NotNull(Fixtures.CascadeDir);

        // Each player's "home"/last-safe location.
        foreach (var playerSave in Directory.EnumerateFiles(
                     Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav"))
        {
            var csd = CharacterSaveData(LoadTopLevel(playerSave));
            _output.WriteLine($"##### {Path.GetFileName(playerSave)} #####");
            foreach (var prefix in new[] { "LastSafeWorldLocation", "LastSafeWorldGUID", "LastControlRotation", "TerminalRespawnID" })
            {
                var tag = csd.Properties.FirstOrDefault(t => t.Name.Value.StartsWith(prefix));
                if (tag is not null) DumpTag(tag, 1);
            }
        }

        // Which world does each LevelGUID belong to?
        _output.WriteLine("--- world LevelGUIDs ---");
        foreach (var worldFile in Directory.EnumerateFiles(Fixtures.CascadeDir!, "WorldSave_*.sav"))
        {
            try
            {
                var guid = LoadTopLevel(worldFile)
                    .FirstOrDefault(t => t.Name.Value.StartsWith("LevelGUID"))?.Property?.Value?.ToString();
                _output.WriteLine($"  {Path.GetFileName(worldFile)} LevelGUID={guid}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  {Path.GetFileName(worldFile)}: load failed ({ex.Message})");
            }
        }

        // Game assets that implement respawn terminals (the TerminalRespawnID target).
        using var provider = CreateProvider();
        if (provider is null) return;
        _output.WriteLine("--- pak entries containing 'respawn' or 'terminal' (blueprints) ---");
        foreach (var k in provider.Files.Keys
                     .Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                     .Where(k => k.Contains("Blueprint", StringComparison.OrdinalIgnoreCase))
                     .Where(k => k.Contains("respawn", StringComparison.OrdinalIgnoreCase)
                                 || k.Contains("terminal", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine($"  {k}");
        }
    }

    private static DefaultFileProvider? CreateProvider()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null) return null;

#pragma warning disable CS0618
        var provider = new DefaultFileProvider(
            paks, SearchOption.TopDirectoryOnly, isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.FileUsmapTypeMappingsProvider(mappings);
        provider.Initialize();
        provider.SubmitKey(new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));
        return provider;
    }

    [Fact]
    public void Probe5_GameTables_CustomizationRows()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        var mappings = GameAssetProvider.FindConventionalMappings();
        if (paks is null || mappings is null)
        {
            _output.WriteLine("No game install / mappings; skipping.");
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

        var tableKeywords = new[] { "Customiz", "Head", "Voice", "Hair", "Transmog", "Suit", "Outfit", "Skin" };
        var candidates = provider.Files.Keys
            .Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Where(k =>
            {
                var name = k[(k.LastIndexOf('/') + 1)..];
                return (name.StartsWith("DT_", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Table", StringComparison.OrdinalIgnoreCase))
                       && tableKeywords.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        _output.WriteLine("Candidate tables:");
        foreach (var c in candidates) _output.WriteLine($"  {c}");

        foreach (var c in candidates)
        {
            var pkg = provider.LoadPackage(c);
            foreach (var dt in pkg.GetExports().OfType<UDataTable>())
            {
                _output.WriteLine($"=== {c} ({dt.RowMap.Count} rows) ===");
                _output.WriteLine("  " + Trunc(string.Join(", ", dt.RowMap.Select(kv => kv.Key.Text)), 2000));
            }
        }
    }
}
