using System.IO;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.PlayerSaves;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Probe tests for (1) backpack slot-count / special-slot definitions in ItemTable_Global
/// and (2) why some CDT_AllTraits rows show empty descriptions in the editor UI.
/// </summary>
public class BackpackTraitProbeTests
{
    private readonly ITestOutputHelper _output;

    public BackpackTraitProbeTests(ITestOutputHelper output)
    {
        _output = output;
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

    // ------------------------------------------------------------------
    // TASK 1 - backpack capacity + special slots
    // ------------------------------------------------------------------

    [Fact]
    public void Dump_BackpackRows_ItemTableGlobal()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Items/ItemTable_Global");
        var dt = pkg.GetExports().OfType<UDataTable>().First();

        // 1. Catalog every row id containing "pack" (case-insensitive) so we know the
        //    exact ids for void/cold/warm/rad variants.
        var packIds = dt.RowMap.Keys
            .Select(k => k.Text)
            .Where(k => k.Contains("pack", StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _output.WriteLine("=== ROW IDS CONTAINING 'pack' ===");
        foreach (var id in packIds) _output.WriteLine("  " + id);

        // 2. Full column dump for the interesting rows.
        var wanted = new[]
        {
            "backpack_basic", "backpack_small", "backpack_large", "backpack_huge",
            "backpack_void", "Voidpack", "coldpack", "warmpack", "radpack",
        };
        var dumpIds = packIds
            .Where(id => wanted.Any(w => string.Equals(w, id, StringComparison.OrdinalIgnoreCase))
                         || id.Contains("backpack", StringComparison.OrdinalIgnoreCase)
                         || id.Contains("coldpack", StringComparison.OrdinalIgnoreCase)
                         || id.Contains("warmpack", StringComparison.OrdinalIgnoreCase)
                         || id.Contains("radpack", StringComparison.OrdinalIgnoreCase)
                         || id.Contains("void", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var id in dumpIds)
        {
            var key = dt.RowMap.Keys.First(k => string.Equals(k.Text, id, StringComparison.OrdinalIgnoreCase));
            _output.WriteLine($"=== ROW {id} ===");
            foreach (var p in dt.RowMap[key].Properties)
            {
                DumpValue(p.Name.Text, p.Tag?.GenericValue, 1);
            }
        }
    }

    /// <summary>
    /// The slot counts may live on the equipable/gear data rather than flat columns;
    /// search ALL rows for any column whose name mentions slots, to find the real
    /// column names (NumberOfExtraSlots? E_InventorySlotType?).
    /// </summary>
    [Fact]
    public void Dump_SlotRelatedColumns_AcrossItemTable()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Items/ItemTable_Global");
        var dt = pkg.GetExports().OfType<UDataTable>().First();

        var seenColumns = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in dt.RowMap)
        {
            CollectColumnNames(kv.Value.Properties, "", seenColumns);
        }
        _output.WriteLine("=== ALL COLUMN NAMES (flattened, deduped) ===");
        foreach (var c in seenColumns) _output.WriteLine("  " + c);
    }

    private static void CollectColumnNames(
        IEnumerable<CUE4Parse.UE4.Assets.Objects.FPropertyTag> props, string prefix, ISet<string> sink)
    {
        foreach (var p in props)
        {
            var name = prefix + p.Name.Text;
            sink.Add(name);
            var v = p.Tag?.GenericValue;
            if (v is CUE4Parse.UE4.Assets.Objects.FScriptStruct ss) v = ss.StructType;
            if (v is CUE4Parse.UE4.Assets.Objects.FStructFallback sf && prefix.Count(ch => ch == '.') < 3)
            {
                CollectColumnNames(sf.Properties, name + ".", sink);
            }
        }
    }

    /// <summary>
    /// Does the saved Inventory_ array length track the equipped backpack, or is it a
    /// fixed-size array with unused tail slots? Compare the 4 Cascade fixture players
    /// and the fresh-ish Chrissie character.
    /// </summary>
    [Fact]
    public void Dump_PlayerInventoryLengths_VsEquippedBackpack()
    {
        var paths = new List<(string Label, string Path)>();

        if (Fixtures.CascadeDir is not null)
        {
            foreach (var sav in Directory.EnumerateFiles(
                Path.Combine(Fixtures.CascadeDir, "PlayerData"), "Player_*.sav"))
            {
                paths.Add(("Cascade/" + Path.GetFileName(sav), sav));
            }

            // Chrissie world lives under fixtures/ClientSaved/SaveGames/<steamid>/Worlds/Chrissie.
            if (Fixtures.ClientSavedDir is { } savedGames)
            {
                foreach (var sav in Directory.EnumerateFiles(savedGames, "Player_*.sav", SearchOption.AllDirectories))
                {
                    if (sav.Contains(Path.Combine("Worlds", "Chrissie"), StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(("Chrissie/" + Path.GetFileName(sav), sav));
                    }
                }
            }
        }

        foreach (var (label, path) in paths)
        {
            var data = PlayerSaveReader.ReadFromFile(path);
            var inv = data.Inventory;
            _output.WriteLine($"=== {label} ===");
            _output.WriteLine($"  Inventory_ length      = {inv.Main.Count}");
            _output.WriteLine($"  HotbarInventory_ length= {inv.Hotbar.Count}");
            _output.WriteLine($"  Equipment length       = {inv.Equipment.Count}");
            for (var i = 0; i < inv.Equipment.Count; i++)
            {
                _output.WriteLine($"    Equip[{i}] = {inv.Equipment[i].ItemId ?? "(empty)"}");
            }
            var nonEmpty = inv.Main.Where(s => !string.IsNullOrEmpty(s.ItemId) &&
                                               !string.Equals(s.ItemId, "Empty", StringComparison.OrdinalIgnoreCase)).ToList();
            _output.WriteLine($"  Main non-empty slots   = {nonEmpty.Count}; " +
                              $"occupied indices: {string.Join(",", nonEmpty.Select(s => s.Index))}");
            _output.WriteLine($"  Main slot ids (index:id): {string.Join(" ", inv.Main.Select(s => $"{s.Index}:{s.ItemId ?? "-"}"))}");
        }
    }

    /// <summary>
    /// The special slots (cold/warm/rad containment) are not columns on the ItemTable
    /// rows - find where they live: candidate assets (buffs, backpack BPs, widgets) and
    /// the class defaults of the Gear_Backpack blueprints.
    /// </summary>
    [Fact]
    public void Dump_BackpackSpecialSlotSources()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        _output.WriteLine("=== ASSET FILES mentioning backpack/slot/buff ===");
        foreach (var f in provider.Files.Keys
            .Where(k => k.Contains("backpack", StringComparison.OrdinalIgnoreCase)
                     || k.Contains("SlotType", StringComparison.OrdinalIgnoreCase)
                     || k.Contains("DataAssets/Inventory", StringComparison.OrdinalIgnoreCase)
                     || (k.Contains("Buff", StringComparison.OrdinalIgnoreCase) && k.Contains("DataTable", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(k => k))
        {
            _output.WriteLine("  " + f);
        }

        // Class defaults of the backpack blueprints - look for slot-type arrays.
        var classes = new[]
        {
            "AbioticFactor/Content/Blueprints/Items/Gear/Gear_Backpack_ParentBP",
            "AbioticFactor/Content/Blueprints/Items/Gear/Gear_Backpack_Huge",
            "AbioticFactor/Content/Blueprints/Items/Gear/Gear_Backpack_Glitch",
        };
        foreach (var path in classes)
        {
            _output.WriteLine($"=== BP {path} ===");
            try
            {
                var pkg = provider.LoadPackage(path);
                foreach (var exp in pkg.GetExports())
                {
                    _output.WriteLine($"  EXPORT {exp.ExportType} '{exp.Name}'");
                    foreach (var p in exp.Properties)
                    {
                        DumpValue(p.Name.Text, p.Tag?.GenericValue, 2);
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  (failed: {ex.Message})");
            }
        }
    }

    /// <summary>
    /// Dump the BackpackData_* DataAssets (special-slot definitions) and the
    /// E_InventorySlotType enum that the slot indices refer to.
    /// </summary>
    [Fact]
    public void Dump_BackpackDataAssets_AndSlotTypeEnum()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var assets = new[]
        {
            "AbioticFactor/Content/Blueprints/Data/E_InventorySlotType",
            "AbioticFactor/Content/Blueprints/DataAssets/Inventory/BackpackData_Research",
            "AbioticFactor/Content/Blueprints/DataAssets/Inventory/BackpackData_ColdStorage",
            "AbioticFactor/Content/Blueprints/DataAssets/Inventory/BackpackData_FieldSample",
            "AbioticFactor/Content/Blueprints/DataAssets/Inventory/BackpackData_Glitch",
        };
        foreach (var path in assets)
        {
            _output.WriteLine($"=== ASSET {path} ===");
            try
            {
                var pkg = provider.LoadPackage(path);
                foreach (var exp in pkg.GetExports())
                {
                    _output.WriteLine($"  EXPORT {exp.ExportType} '{exp.Name}'");
                    if (exp is CUE4Parse.UE4.Objects.UObject.UEnum en)
                    {
                        foreach (var (name, value) in en.Names)
                        {
                            _output.WriteLine($"    {name.Text} = {value}");
                        }
                    }
                    foreach (var p in exp.Properties)
                    {
                        DumpValue(p.Name.Text, p.Tag?.GenericValue, 2);
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  (failed: {ex.Message})");
            }
        }
    }

    /// <summary>
    /// Which asset wires an item row (backpack_large, ...) to its BackpackData_* asset?
    /// The link is in blueprint bytecode, so scan raw package bytes of the candidates
    /// for the "BackpackData" name-map string.
    /// </summary>
    [Fact]
    public void Scan_WhoReferencesBackpackData()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var candidates = provider.Files.Keys
            .Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                && k.Contains("Content/Blueprints/", StringComparison.OrdinalIgnoreCase)
                && !k.Contains("DataAssets/Inventory", StringComparison.OrdinalIgnoreCase))
            .ToList();
        _output.WriteLine($"scanning {candidates.Count} packages");

        foreach (var key in candidates)
        {
            if (!provider.TrySavePackage(key, out var parts)) continue;
            foreach (var (partName, bytes) in parts)
            {
                var hits = FindAscii(bytes, "BackpackData");
                if (hits.Count > 0)
                {
                    _output.WriteLine($"HIT {partName}: {string.Join(" | ", hits.Distinct())}");
                }
            }
        }
    }

    /// <summary>
    /// backpack_huge / backpack_voidpack_U1a have no DataAsset in DT_ItemCosmetics yet
    /// show special slots in-game - check whether the slot config names appear inside
    /// the Gear_Backpack_Huge / W_Inventory_PlayerBackpack packages (bytecode defaults).
    /// </summary>
    [Fact]
    public void Scan_GearBackpackHuge_ForSlotNames()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var targets = new[]
        {
            "AbioticFactor/Content/Blueprints/Items/Gear/Gear_Backpack_Huge.uasset",
            "AbioticFactor/Content/Blueprints/Items/Gear/Gear_Backpack_Glitch.uasset",
            "AbioticFactor/Content/Blueprints/Items/Gear/Gear_Backpack_ParentBP.uasset",
            "AbioticFactor/Content/Blueprints/Widgets/Inventory/W_Inventory_PlayerBackpack.uasset",
        };
        var needles = new[] { "RefrigeratedSlots", "FreezerSlots", "ShieldedSlots", "WarmerSlots", "InventoryData", "SlotAppearance" };
        foreach (var key in targets)
        {
            if (!provider.TrySavePackage(key, out var parts))
            {
                _output.WriteLine($"(could not save {key})");
                continue;
            }
            foreach (var (partName, bytes) in parts)
            {
                foreach (var needle in needles)
                {
                    var hits = FindAscii(bytes, needle);
                    if (hits.Count > 0)
                    {
                        _output.WriteLine($"HIT {partName}: {needle} -> {string.Join(" | ", hits.Distinct().Take(6))}");
                    }
                }
            }
        }
    }

    private static List<string> FindAscii(byte[] data, string needle)
    {
        var found = new List<string>();
        var pattern = System.Text.Encoding.ASCII.GetBytes(needle);
        for (var i = 0; i <= data.Length - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j]) { match = false; break; }
            }
            if (!match) continue;

            // Capture the full identifier-ish run around the hit.
            var end = i;
            while (end < data.Length && (char.IsLetterOrDigit((char)data[end]) || data[end] == '_' || data[end] == '/'))
            {
                end++;
            }
            found.Add(System.Text.Encoding.ASCII.GetString(data, i, end - i));
            i = end;
        }
        return found;
    }

    /// <summary>
    /// DT_ItemCosmetics turned out to be the table that wires item rows to
    /// InventoryData/BackpackData assets - dump the rows that reference them.
    /// </summary>
    [Fact]
    public void Dump_ItemCosmetics_BackpackRows()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/DT_ItemCosmetics");
        foreach (var dt in pkg.GetExports().OfType<UDataTable>())
        {
            _output.WriteLine($"ROWS {dt.RowMap.Count}");
            foreach (var kv in dt.RowMap)
            {
                var flat = string.Join(" ", kv.Value.Properties.Select(p => p.Tag?.GenericValue?.ToString()));
                if (!kv.Key.Text.Contains("pack", StringComparison.OrdinalIgnoreCase) &&
                    !flat.Contains("BackpackData", StringComparison.OrdinalIgnoreCase) &&
                    !flat.Contains("InventoryData", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                _output.WriteLine($"=== {kv.Key.Text} ===");
                foreach (var p in kv.Value.Properties)
                {
                    DumpValue(p.Name.Text, p.Tag?.GenericValue, 1);
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // TASK 2 - trait descriptions missing
    // ------------------------------------------------------------------

    [Fact]
    public void Dump_AllTraitsRows_FullColumns()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/Traits/CDT_AllTraits");
        foreach (var dt in pkg.GetExports().OfType<UDataTable>())
        {
            _output.WriteLine($"ROWS {dt.RowMap.Count}");
            foreach (var kv in dt.RowMap)
            {
                _output.WriteLine($"=== {kv.Key.Text} ===");
                foreach (var p in kv.Value.Properties)
                {
                    // Print exact column name (incl. hash suffix), the CLR type of the
                    // value, and the rendered value - so empty-description rows show
                    // whether the column is absent, differently named, or an FText that
                    // renders empty.
                    var v = p.Tag?.GenericValue;
                    _output.WriteLine($"  [{p.PropertyType.Text}/{v?.GetType().Name ?? "null"}]");
                    DumpValue(p.Name.Text, v, 1);
                }
            }
        }
    }

    /// <summary>
    /// Focused diff: for the problem rows, print every column whose name mentions
    /// description/point, with full (untruncated) values and FText internals.
    /// </summary>
    [Fact]
    public void Dump_ProblemTraitRows_DescriptionColumns()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/Traits/CDT_AllTraits");
        var dt = pkg.GetExports().OfType<UDataTable>().First();

        var problem = new[] { "Trait_Dyslexia", "Trait_Fumbler", "Trait_Cannibal" };
        var control = new[] { "Trait_Chef", "Trait_NightOwl" };

        foreach (var id in problem.Concat(control))
        {
            var key = dt.RowMap.Keys.FirstOrDefault(k => k.Text == id);
            if (key.IsNone)
            {
                _output.WriteLine($"=== {id} NOT FOUND ===");
                continue;
            }
            _output.WriteLine($"=== {id} ===");
            foreach (var p in dt.RowMap[key].Properties)
            {
                var n = p.Name.Text;
                if (!n.Contains("Desc", StringComparison.OrdinalIgnoreCase) &&
                    !n.Contains("Point", StringComparison.OrdinalIgnoreCase) &&
                    !n.Contains("Name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var v = p.Tag?.GenericValue;
                _output.WriteLine($"  col='{n}' type={p.PropertyType.Text} clr={v?.GetType().FullName ?? "null"}");
                if (v is CUE4Parse.UE4.Objects.Core.i18N.FText ft)
                {
                    _output.WriteLine($"    FText.Text='{ft.Text}' HistoryType={ft.HistoryType}");
                }
                else
                {
                    _output.WriteLine($"    value='{v}'");
                }
            }
        }
    }

    /// <summary>
    /// Can the editor fall back to the trait's buff row for a description?
    /// Dump the DT_BuffsDebuffs rows behind a few empty-description traits + controls.
    /// </summary>
    [Fact]
    public void Dump_BuffRows_ForEmptyDescriptionTraits()
    {
        using var provider = CreateProvider();
        if (provider is null) return;

        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/DataTables/BuffsDebuffs/DT_BuffsDebuffs");
        var dt = pkg.GetExports().OfType<UDataTable>().First();
        _output.WriteLine($"ROWS {dt.RowMap.Count}");

        var wanted = new[]
        {
            "Debuff_Trait_Dyslexia", "Debuff_Trait_Fumbler", "Debuff_Trait_Smoker",
            "Debuff_Trait_Cannibal", "Buff_Trait_Chef",
        };
        foreach (var id in wanted)
        {
            var key = dt.RowMap.Keys.FirstOrDefault(k => k.Text == id);
            if (key.IsNone)
            {
                _output.WriteLine($"=== {id} NOT FOUND ===");
                continue;
            }
            _output.WriteLine($"=== {id} ===");
            foreach (var p in dt.RowMap[key].Properties)
            {
                DumpValue(p.Name.Text, p.Tag?.GenericValue, 1);
            }
        }
    }

    // ------------------------------------------------------------------
    // shared dump helper (same shape as UpgradeProbeTests)
    // ------------------------------------------------------------------

    private void DumpValue(string name, object? value, int depth)
    {
        var pad = new string(' ', depth * 2);
        switch (value)
        {
            case CUE4Parse.UE4.Assets.Objects.FScriptStruct ss:
                DumpValue(name, ss.StructType, depth);
                break;
            case CUE4Parse.UE4.Assets.Objects.FStructFallback sf:
                _output.WriteLine($"{pad}{name}:");
                foreach (var p in sf.Properties) DumpValue(p.Name.Text, p.Tag?.GenericValue, depth + 1);
                break;
            case CUE4Parse.UE4.Assets.Objects.UScriptArray arr:
                _output.WriteLine($"{pad}{name}[{arr.Properties.Count}]:");
                var i = 0;
                foreach (var el in arr.Properties) DumpValue($"[{i++}]", el.GenericValue, depth + 1);
                break;
            case CUE4Parse.UE4.Assets.Objects.UScriptMap map:
                _output.WriteLine($"{pad}{name}{{{map.Properties.Count}}}:");
                foreach (var kv in map.Properties)
                {
                    DumpValue($"key={kv.Key?.GenericValue}", kv.Value?.GenericValue, depth + 1);
                }
                break;
            default:
                var s = value?.ToString() ?? "(null)";
                if (s.Length > 300) s = s[..300];
                _output.WriteLine($"{pad}{name} = {s}");
                break;
        }
    }
}
