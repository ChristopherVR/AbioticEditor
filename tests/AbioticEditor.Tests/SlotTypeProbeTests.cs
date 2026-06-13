using System.IO;
using System.Text;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Investigation probes (findings: dotnet/docs/research-slot-types.md):
///   1. Which ItemTable_Global EquipmentData_100_* column encodes the equipment slot
///      (E_InventorySlotType) an item equips to - per item family, so the editor can
///      validate equipment/transmog assignments strictly.
///   2. Whether the user's REAL dedicated-server saves carry player-typed bench names in
///      CustomTextDisplay_ (then WorldSaveReader/UI has a bug) or somewhere else.
///   3. Whether a player save's CONTENT carries the steamid64 anywhere, or only the
///      file name does.
/// All probes guard on their inputs (game install / server tree) and skip when absent.
/// </summary>
public class SlotTypeProbeTests
{
    private readonly ITestOutputHelper _output;

    public SlotTypeProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ----------------------------------------------------------------- helpers

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

    private void DumpValue(string name, object? value, int depth, int maxDepth = 6)
    {
        var pad = new string(' ', depth * 2);
        if (depth > maxDepth) { _output.WriteLine($"{pad}{name} ... (max depth)"); return; }
        switch (value)
        {
            case CUE4Parse.UE4.Assets.Objects.FScriptStruct ss:
                DumpValue(name, ss.StructType, depth, maxDepth);
                break;
            case CUE4Parse.UE4.Assets.Objects.FStructFallback sf:
                _output.WriteLine($"{pad}{name}:");
                foreach (var p in sf.Properties) DumpValue(p.Name.Text, p.Tag?.GenericValue, depth + 1, maxDepth);
                break;
            case CUE4Parse.UE4.Assets.Objects.UScriptArray arr:
                _output.WriteLine($"{pad}{name}[{arr.Properties.Count}]:");
                var i = 0;
                foreach (var el in arr.Properties) DumpValue($"[{i++}]", el.GenericValue, depth + 1, maxDepth);
                break;
            case CUE4Parse.UE4.Assets.Objects.UScriptMap map:
                _output.WriteLine($"{pad}{name}{{{map.Properties.Count}}}:");
                foreach (var kv in map.Properties)
                    DumpValue($"[{kv.Key.GenericValue}]", kv.Value?.GenericValue, depth + 1, maxDepth);
                break;
            default:
                var s = value?.ToString() ?? "(null)";
                if (s.Length > 220) s = s[..220];
                _output.WriteLine($"{pad}{name} = {s}");
                break;
        }
    }

    /// <summary>Escapes control / non-ASCII chars so exact stored text is visible.</summary>
    private static string Escape(string? s)
    {
        if (s is null) return "(null)";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c is >= ' ' and <= '~') sb.Append(c);
            else sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
        }
        return sb.ToString();
    }

    // =====================================================================
    // Q1 - EquipmentData slot-type column per item family
    // =====================================================================

    [Fact]
    public void Probe1_ItemTable_EquipmentData_FullDump()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("SKIP: no game install/mappings."); return; }

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Items/ItemTable_Global");
        var dt = pkg.GetExports().OfType<UDataTable>().First();
        var keys = dt.RowMap.Keys.Select(k => k.Text).ToList();

        // Exact ids where known; family keywords where the exact id is unknown.
        var wanted = new (string Label, Func<string, bool> Match, int MaxMatches)[]
        {
            ("armor_helmet_cqc",        id => id.Equals("armor_helmet_cqc", StringComparison.OrdinalIgnoreCase), 1),
            // The "*_mountaineer" exact ids do not exist in the table; probe the chest /
            // legs / arms armor families instead (and report all family ids).
            ("armor_chest (family)",    id => id.StartsWith("armor_chest", StringComparison.OrdinalIgnoreCase), 2),
            ("armor_legs (family)",     id => id.StartsWith("armor_legs", StringComparison.OrdinalIgnoreCase), 2),
            ("armor_arms (family)",     id => id.StartsWith("armor_arms", StringComparison.OrdinalIgnoreCase), 2),
            ("mountaineer (family)",    id => id.Contains("mountaineer", StringComparison.OrdinalIgnoreCase), 4),
            ("backpack_large",          id => id.Equals("backpack_large", StringComparison.OrdinalIgnoreCase), 1),
            ("hazmat suit (family)",    id => id.Contains("hazmat", StringComparison.OrdinalIgnoreCase), 3),
            ("trinket_kylie",           id => id.Equals("trinket_kylie", StringComparison.OrdinalIgnoreCase), 1),
            ("heatershield",            id => id.Contains("heatershield", StringComparison.OrdinalIgnoreCase)
                                              || id.Contains("heater_shield", StringComparison.OrdinalIgnoreCase), 2),
            ("headlamp (family)",       id => id.Contains("headlamp", StringComparison.OrdinalIgnoreCase)
                                              || id.Contains("head_lamp", StringComparison.OrdinalIgnoreCase), 3),
            ("watch (family)",          id => id.Contains("watch", StringComparison.OrdinalIgnoreCase), 4),
            ("keypad hacker (family)",  id => id.Contains("keypad", StringComparison.OrdinalIgnoreCase)
                                              || id.Contains("hack", StringComparison.OrdinalIgnoreCase), 4),
            ("non-equipment contrast",  id => id.Equals("bandage_basic", StringComparison.OrdinalIgnoreCase)
                                              || id.Equals("bandage", StringComparison.OrdinalIgnoreCase), 2),
        };

        foreach (var (label, match, maxMatches) in wanted)
        {
            var matches = keys.Where(match).ToList();
            _output.WriteLine($"##### {label}: {matches.Count} match(es): {string.Join(", ", matches.Take(12))}");
            foreach (var id in matches.Take(maxMatches))
            {
                var row = dt.RowMap.First(kv => kv.Key.Text == id).Value;
                _output.WriteLine($"=== {id} ===");
                foreach (var p in row.Properties)
                {
                    var n = p.Name.Text;
                    // Full EquipmentData struct, plus any other column whose name hints at
                    // slot/equip semantics (in case the slot enum lives outside EquipmentData).
                    if (n.StartsWith("EquipmentData_", StringComparison.Ordinal))
                    {
                        DumpValue(n, p.Tag?.GenericValue, 1, maxDepth: 8);
                    }
                    else if (n.Contains("Slot", StringComparison.OrdinalIgnoreCase)
                             || n.Contains("Equip", StringComparison.OrdinalIgnoreCase))
                    {
                        DumpValue(n, p.Tag?.GenericValue, 1, maxDepth: 4);
                    }
                }
            }
            _output.WriteLine("");
        }
    }

    [Fact]
    public void Probe2_InventorySlotTypeEnum_Names()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("SKIP: no game install/mappings."); return; }

        foreach (var k in provider.Files.Keys
                     .Where(k => k.Contains("E_InventorySlotType", StringComparison.OrdinalIgnoreCase)
                                 && k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine($"##### {k}");
            foreach (var export in provider.LoadPackage(k).GetExports())
            {
                _output.WriteLine($"EXPORT {export.Name} ({export.ExportType})");
                if (export is CUE4Parse.UE4.Objects.UObject.UEnum en)
                {
                    foreach (var (name, value) in en.Names)
                        _output.WriteLine($"  Names[{value}] = {name.Text}");
                }
                // UUserDefinedEnum.DisplayNameMap arrives via tagged properties.
                foreach (var p in export.Properties)
                {
                    DumpValue(p.Name.Text, p.Tag?.GenericValue, 1, maxDepth: 5);
                }
            }
        }
    }

    // =====================================================================
    // Q2 - bench names in the user's real (dedicated-server) saves
    // =====================================================================

    private static string? ServerCascadeDir => Fixtures.ServerWorldsDir;

    [Fact]
    public void Probe3_ServerBenches_CustomTextDisplay_And_ReaderRoundTrip()
    {
        var cascade = ServerCascadeDir;
        if (cascade is null) { _output.WriteLine("SKIP: no Server fixture tree found."); return; }

        // (file, benchId, customText) for benches carrying a non-empty CustomTextDisplay_.
        var named = new List<(string File, string Id, string Text)>();
        var benchTotal = 0;

        foreach (var path in Directory.EnumerateFiles(cascade, "WorldSave_*.sav", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            SaveGame save;
            try
            {
                using var fs = File.OpenRead(path);
                save = SaveGame.LoadFrom(fs);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"{Path.GetFileName(path)}: parse failed: {ex.Message}");
                continue;
            }

            var pairs = GetMapPairs(save.Properties, "DeployedObjectMap");
            if (pairs is null) continue;

            foreach (var kvp in pairs)
            {
                if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;
                var cls = FindByPrefix(ps.Properties, "Class_")?.Property?.Value?.ToString();
                if (cls?.Contains("CraftingBench", StringComparison.OrdinalIgnoreCase) != true) continue;

                benchTotal++;
                var id = kvp.Key.Value switch { FString f => f.Value, string s => s, var v => v?.ToString() } ?? "(?)";
                _output.WriteLine($"BENCH {Path.GetFileName(path)} :: {id}");
                _output.WriteLine($"  Class = {cls}");

                var customTag = FindByPrefix(ps.Properties, "CustomTextDisplay_");
                var custom = customTag?.Property?.Value?.ToString();
                _output.WriteLine($"  {customTag?.Name?.Value ?? "CustomTextDisplay_ (missing)"} = '{Escape(custom)}'");
                if (!string.IsNullOrWhiteSpace(custom))
                {
                    named.Add((path, id, custom!));
                }

                // Every other string-bearing leaf of the bench struct (skipping the bulky
                // inventory arrays) - catches names stored outside CustomTextDisplay_.
                WalkProperties("", ps, (leafPath, value) =>
                {
                    if (leafPath.Contains("/ContainerInventories", StringComparison.Ordinal)) return;
                    if (value.Length == 0) return;
                    // Only surface plausible text: skip pure numbers / asset paths / enums.
                    if (double.TryParse(value, out _)) return;
                    if (value is "True" or "False") return;
                    if (value.StartsWith("/Game/", StringComparison.Ordinal)) return;
                    _output.WriteLine($"  LEAF {leafPath} = '{Escape(value.Length > 120 ? value[..120] : value)}'");
                });
            }
        }

        _output.WriteLine($"TOTAL benches={benchTotal}, named={named.Count}");

        // Verdict assert: if the names ARE in CustomTextDisplay_, our reader must surface
        // them on WorldDeployable.CustomName (else the UI gap is a reader bug).
        foreach (var group in named.GroupBy(n => n.File))
        {
            var data = WorldSaveReader.ReadFromFile(group.Key);
            foreach (var (_, id, text) in group)
            {
                var dep = data.Deployables.FirstOrDefault(d => d.Id == id);
                _output.WriteLine($"READER {Path.GetFileName(group.Key)} :: {id}: CustomName='{Escape(dep?.CustomName)}' (raw '{Escape(text)}')");
                Assert.NotNull(dep);
                Assert.Equal(text, dep!.CustomName);
            }
        }
    }

    // Local clones of WorldSaveReader's internal helpers (no InternalsVisibleTo).

    private static IList<KeyValuePair<FProperty, FProperty>>? GetMapPairs(
        IList<FPropertyTag>? topLevel, string namePrefix)
        => FindByPrefix(topLevel ?? Array.Empty<FPropertyTag>(), namePrefix)?.Property is MapProperty mp
            ? mp.Value
            : null;

    private static FPropertyTag? FindByPrefix(IEnumerable<FPropertyTag> tags, string prefix)
        => tags.FirstOrDefault(t => t.Name?.Value is { } n && n.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// Generic UeSaveGame property-tree walker invoking <paramref name="onLeaf"/> with
    /// (path, value-as-string) for every scalar leaf.
    /// </summary>
    private static void WalkProperties(string path, object? node, Action<string, string> onLeaf, int depth = 0)
    {
        if (node is null || depth > 24) return;
        switch (node)
        {
            case FPropertyTag tag:
                WalkProperties($"{path}/{tag.Name?.Value}", tag.Property, onLeaf, depth + 1);
                break;
            case PropertiesStruct ps:
                foreach (var t in ps.Properties) WalkProperties(path, t, onLeaf, depth + 1);
                break;
            case ArrayProperty { Value: { } arr }:
                for (var i = 0; i < arr.Length; i++)
                    WalkProperties($"{path}[{i}]", arr.GetValue(i), onLeaf, depth + 1);
                break;
            case MapProperty { Value: { } pairs }:
                var j = 0;
                foreach (var kvp in pairs)
                {
                    WalkProperties($"{path}[{j}].key", kvp.Key, onLeaf, depth + 1);
                    WalkProperties($"{path}[{j}]", kvp.Value, onLeaf, depth + 1);
                    j++;
                }
                break;
            case StructProperty sp:
                WalkProperties(path, sp.Value, onLeaf, depth + 1);
                break;
            case FProperty p:
                WalkProperties(path, p.Value, onLeaf, depth + 1);
                break;
            case FString fs:
                onLeaf(path, fs.Value ?? "");
                break;
            default:
                onLeaf(path, node.ToString() ?? "");
                break;
        }
    }

    // =====================================================================
    // Q3 - steamid64 inside player-save CONTENT?
    // =====================================================================

    [Fact]
    public void Probe4_SteamId_InsidePlayerSaves()
    {
        const string steamIdText = "76561197993781479";
        const ulong steamId = 76561197993781479UL;

        var candidates = new List<string>();
        if (Fixtures.CascadeDir is not null)
            candidates.Add(Path.Combine(Fixtures.CascadeDir, "PlayerData", $"Player_{steamIdText}.sav"));
        if (ServerCascadeDir is not null)
            candidates.Add(Path.Combine(ServerCascadeDir, "PlayerData", $"Player_{steamIdText}.sav"));

        var any = false;
        foreach (var path in candidates.Where(File.Exists))
        {
            any = true;
            _output.WriteLine($"##### {path}");
            var bytes = File.ReadAllBytes(path);

            var hits = 0;
            hits += ScanFor(bytes, Encoding.ASCII.GetBytes(steamIdText), "ASCII digits");
            hits += ScanFor(bytes, Encoding.Unicode.GetBytes(steamIdText), "UTF-16LE digits");
            hits += ScanFor(bytes, BitConverter.GetBytes(steamId), "uint64 LE");
            if (hits == 0) _output.WriteLine("  (no byte-level hits)");

            // Property-tree pass: any parsed leaf containing a steamid64 prefix?
            using var fs = File.OpenRead(path);
            var save = SaveGame.LoadFrom(fs);
            var treeHits = 0;
            foreach (var tag in save.Properties!)
            {
                WalkProperties("", tag, (leafPath, value) =>
                {
                    if (!value.Contains("7656119", StringComparison.Ordinal)) return;
                    treeHits++;
                    _output.WriteLine($"  TREE {leafPath} = '{Escape(value.Length > 160 ? value[..160] : value)}'");
                });
            }
            if (treeHits == 0) _output.WriteLine("  (no property-tree hits) => steamid lives in the FILE NAME only");
        }

        if (!any) _output.WriteLine("SKIP: no player save fixture/server file found.");
    }

    /// <summary>Reports every occurrence of <paramref name="needle"/> with ASCII context.</summary>
    private int ScanFor(byte[] haystack, byte[] needle, string label)
    {
        var hits = 0;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var k = 0; k < needle.Length; k++)
            {
                if (haystack[i + k] != needle[k]) { match = false; break; }
            }
            if (!match) continue;

            hits++;
            var from = Math.Max(0, i - 96);
            var to = Math.Min(haystack.Length, i + needle.Length + 96);
            var context = new StringBuilder(to - from);
            for (var k = from; k < to; k++)
            {
                var b = haystack[k];
                context.Append(b is >= 32 and <= 126 ? (char)b : '.');
            }
            _output.WriteLine($"  HIT [{label}] @0x{i:X}: {context}");
            if (hits >= 16) { _output.WriteLine($"  ... more [{label}] hits suppressed"); break; }
        }
        return hits;
    }
}
