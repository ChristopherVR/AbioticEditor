using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Investigation:
///   1. Which TransmogInventory_106 index corresponds to which body part
///      (correlate with EquipmentInventory_ slot roles + cooked Transmog assets).
///   2. What the 12 TransmogVisibility_109 bools / 13 TransmogDisabledArray_145
///      bools index.
///   3. Whether DT_Customization_* rows carry display names and/or 2D preview
///      textures usable by the appearance pickers.
/// Findings are written up in dotnet/docs/research-transmog-appearance.md.
/// </summary>
public class TransmogAppearanceProbeTests
{
    private readonly ITestOutputHelper _output;

    public TransmogAppearanceProbeTests(ITestOutputHelper output)
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

    private static PropertiesStruct CharacterSaveData(string path)
    {
        using var fs = File.OpenRead(path);
        var save = SaveGame.LoadFrom(fs);
        return (PropertiesStruct)((StructProperty)save.Properties!
            .First(t => t.Name!.Value.StartsWith("CharacterSaveData")).Property!).Value!;
    }

    /// <summary>RowName of a standard inventory slot struct, or null.</summary>
    private static string? SlotRowName(object? element)
    {
        if (element is not StructProperty sp || sp.Value is not PropertiesStruct ps) return null;
        return (ps.Properties.FirstOrDefault(p => p.Name!.Value.StartsWith("ItemDataTable_"))?.Property
                as StructProperty)?.Value is PropertiesStruct rh
            ? rh.Properties.FirstOrDefault(p => p.Name!.Value == "RowName")?.Property?.Value?.ToString()
            : null;
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
            default:
                var s = value?.ToString() ?? "(null)";
                if (s.Length > 200) s = s[..200];
                _output.WriteLine($"{pad}{name} = {s}");
                break;
        }
    }

    // ------------------------------------------------------------ TASK 1 probes

    [Fact]
    public void Probe1_AllPlayers_Transmog_Equipment_Visibility()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        foreach (var playerSave in Directory.EnumerateFiles(
                     Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav"))
        {
            _output.WriteLine($"##### {Path.GetFileName(playerSave)} #####");
            var csd = CharacterSaveData(playerSave);

            foreach (var arrayName in new[] { "TransmogInventory_", "EquipmentInventory_" })
            {
                var tag = csd.Properties.FirstOrDefault(t => t.Name!.Value.StartsWith(arrayName));
                if (tag?.Property is not ArrayProperty array || array.Value is null)
                {
                    _output.WriteLine($"  {arrayName}: (missing)");
                    continue;
                }
                _output.WriteLine($"  {tag.Name!.Value} [{array.Value.Length}]");
                for (var i = 0; i < array.Value.Length; i++)
                {
                    var rowName = SlotRowName(array.Value.GetValue(i));
                    _output.WriteLine($"    [{i}] = {rowName ?? "(?)"}");
                }
            }

            foreach (var boolArrayName in new[] { "TransmogVisibility_", "TransmogDisabledArray_" })
            {
                var tag = csd.Properties.FirstOrDefault(t => t.Name!.Value.StartsWith(boolArrayName));
                if (tag?.Property is not ArrayProperty array || array.Value is null)
                {
                    _output.WriteLine($"  {boolArrayName}: (missing)");
                    continue;
                }
                var bools = Enumerable.Range(0, array.Value.Length)
                    .Select(i =>
                    {
                        var el = array.Value.GetValue(i);
                        return el is FProperty fp ? fp.Value?.ToString() ?? "(null)" : el?.ToString() ?? "(null)";
                    });
                _output.WriteLine($"  {tag.Name!.Value} [{array.Value.Length}] = {string.Join(",", bools)}");
            }
            _output.WriteLine("");
        }
    }

    [Fact]
    public void Probe2_FindTransmogAndEquipSlotAssets()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        foreach (var keyword in new[] { "Transmog", "EquipSlot", "EquipmentSlot" })
        {
            _output.WriteLine($"--- pak entries containing '{keyword}' ---");
            foreach (var k in provider.Files.Keys
                         .Where(k => k.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                         .Take(80))
            {
                _output.WriteLine($"  {k}");
            }
        }
    }

    [Fact]
    public void Probe3_TransmogWidget_ExportDump()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        var candidates = provider.Files.Keys
            .Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            .Where(k => k.Contains("Transmog", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var path in candidates)
        {
            _output.WriteLine($"##### {path} #####");
            CUE4Parse.UE4.Assets.IPackage pkg;
            try { pkg = provider.LoadPackage(path); }
            catch (Exception ex) { _output.WriteLine($"  load failed: {ex.Message}"); continue; }

            foreach (var export in pkg.GetExports())
            {
                // Only dump exports that actually carry properties of interest;
                // widget trees are huge, so filter to slot/equip/transmog-ish names.
                var interesting = export.Properties
                    .Where(p => p.Name.Text.Contains("Slot", StringComparison.OrdinalIgnoreCase)
                                || p.Name.Text.Contains("Equip", StringComparison.OrdinalIgnoreCase)
                                || p.Name.Text.Contains("Transmog", StringComparison.OrdinalIgnoreCase)
                                || p.Name.Text.Contains("Tag", StringComparison.OrdinalIgnoreCase)
                                || p.Name.Text.Contains("Type", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (interesting.Count == 0) continue;

                _output.WriteLine($"  EXPORT {export.Name} ({export.ExportType})");
                foreach (var p in interesting) DumpValue(p.Name.Text, p.Tag?.GenericValue, 2);
            }
            _output.WriteLine("");
        }
    }

    [Fact]
    public void Probe5_EquipSlotsWidget_And_SlotTypeEnum()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        // The equipment panel: which W_*Slot child has which SlotIndex/SlotType,
        // and whether per-slot transmog toggle buttons carry an index.
        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Widgets/Inventory/W_Inventory_EquipSlots");
        foreach (var export in pkg.GetExports())
        {
            if (!export.ExportType.Contains("Slot", StringComparison.OrdinalIgnoreCase)
                && !export.ExportType.Contains("Toggle", StringComparison.OrdinalIgnoreCase)) continue;
            var props = export.Properties
                .Where(p => p.Name.Text is "SlotIndex" or "SlotType" or "EmptySlotTooltipText"
                            or "EquipmentIndex" or "Index" or "TransmogSlotIndex" or "EmptySlotIcon")
                .ToList();
            if (props.Count == 0) continue;
            _output.WriteLine($"EXPORT {export.Name} ({export.ExportType})");
            foreach (var p in props) DumpValue(p.Name.Text, p.Tag?.GenericValue, 1);
        }

        // The E_InventorySlotType enum gives friendly names for the NewEnumeratorN ids.
        _output.WriteLine("--- enum assets containing 'SlotType' or 'EquipSlots' ---");
        foreach (var k in provider.Files.Keys
                     .Where(k => k.Contains("SlotType", StringComparison.OrdinalIgnoreCase)
                                 || k.Contains("E_PlayerEquipSlots", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine($"  {k}");
            if (!k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                foreach (var export in provider.LoadPackage(k).GetExports())
                {
                    if (export is CUE4Parse.UE4.Objects.UObject.UEnum en)
                    {
                        foreach (var (name, value) in en.Names)
                            _output.WriteLine($"    {value} = {name.Text}");
                    }
                }
            }
            catch (Exception ex) { _output.WriteLine($"    load failed: {ex.Message}"); }
        }
    }

    // ------------------------------------------------------------ TASK 2 probes

    [Fact]
    public void Probe4_CustomizationTables_FullRowColumns()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        var tables = new[]
        {
            "DT_Customization_Head", "DT_Customization_HairStyle", "DT_Customization_UpperBody",
            "DT_Customization_HairColor", "DT_Customization_ShirtColor", "DT_Customization_IDCard",
        };
        foreach (var table in tables)
        {
            var pkgPath = $"AbioticFactor/Content/Blueprints/DataTables/Customization/{table}";
            _output.WriteLine($"##### {pkgPath} #####");
            CUE4Parse.UE4.Assets.IPackage pkg;
            try { pkg = provider.LoadPackage(pkgPath); }
            catch (Exception ex) { _output.WriteLine($"  load failed: {ex.Message}"); continue; }

            foreach (var dt in pkg.GetExports().OfType<UDataTable>())
            {
                _output.WriteLine($"ROWS {dt.RowMap.Count}: {string.Join(", ", dt.RowMap.Keys.Select(k => k.Text).Take(20))}");
                // Dump two rows so default-valued columns in row 1 still show up.
                foreach (var kv in dt.RowMap.Skip(1).Take(2))
                {
                    _output.WriteLine($"=== {kv.Key.Text} ===");
                    foreach (var p in kv.Value.Properties)
                    {
                        DumpValue(p.Name.Text, p.Tag?.GenericValue, 1);
                    }
                }
            }
            _output.WriteLine("");
        }
    }
}
