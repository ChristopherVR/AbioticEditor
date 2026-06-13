using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Investigation (2026-06-11):
///   1. GATEPal PDA widget structure (app grid, header, colors, icon textures)
///      for a faithful rework of the editor's GATEPal tab.
///   2. In-game inventory widget structure (paper doll, hotbar, grid, weight bar).
///   3. Full dump of DT_StoryProgression (37 chapters) for the quest UI.
/// Findings are written up in dotnet/docs/research-gatepal-quests.md.
/// </summary>
public class GatePalQuestProbeTests
{
    private readonly ITestOutputHelper _output;

    public GatePalQuestProbeTests(ITestOutputHelper output)
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

    private void DumpValue(string name, object? value, int depth, int maxDepth = 8)
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
                if (s.Length > 300) s = s[..300];
                _output.WriteLine($"{pad}{name} = {s}");
                break;
        }
    }

    /// <summary>
    /// Reconstructs the widget hierarchy of a UMG blueprint by walking the
    /// PanelSlot exports (each slot has Parent + Content object refs), then
    /// prints the tree with layout/visual props per widget.
    /// </summary>
    private void DumpWidgetHierarchy(CUE4Parse.UE4.Assets.IPackage pkg)
    {
        var exports = pkg.GetExports().ToList();
        var byName = exports.GroupBy(e => e.Name)
            .ToDictionary(g => g.Key, g => g.First());
        var childrenOf = new Dictionary<string, List<(string Child, UObject Slot)>>();
        var hasParent = new HashSet<string>();

        foreach (var slot in exports.Where(e => e.ExportType.EndsWith("Slot")))
        {
            string? RefName(string prop) =>
                slot.Properties.FirstOrDefault(p => p.Name.Text == prop)?.Tag?.GenericValue
                    is FPackageIndex pi && !pi.IsNull
                    ? pi.ResolvedObject?.Name.Text
                    : null;

            var parent = RefName("Parent");
            var content = RefName("Content");
            if (parent is null || content is null) continue;
            if (!childrenOf.TryGetValue(parent, out var list))
                childrenOf[parent] = list = new List<(string, UObject)>();
            list.Add((content, slot));
            hasParent.Add(content);
        }

        void PrintWidget(string name, int depth)
        {
            var pad = new string(' ', depth * 2);
            if (!byName.TryGetValue(name, out var w)) { _output.WriteLine($"{pad}{name} (?)"); return; }

            var details = new List<string>();
            foreach (var p in w.Properties)
            {
                switch (p.Name.Text)
                {
                    case "Text" or "ToolTipText" or "DefaultText" or "HintText":
                        details.Add($"{p.Name.Text}=\"{p.Tag?.GenericValue}\"");
                        break;
                    case "Visibility" or "ActiveWidgetIndex" or "RenderOpacity" or "bIsVariable":
                        details.Add($"{p.Name.Text}={p.Tag?.GenericValue}");
                        break;
                }
            }
            _output.WriteLine($"{pad}{name} ({w.ExportType}){(details.Count > 0 ? "  " + string.Join(" ", details) : "")}");

            // Visual props worth a deep dump (brushes carry texture paths + tints).
            foreach (var p in w.Properties)
            {
                if (p.Name.Text is "Brush" or "ColorAndOpacity" or "Font" or "BrushColor"
                    or "ContentColorAndOpacity" or "BackgroundColor" or "DefaultTextStyle")
                {
                    DumpValue($".{p.Name.Text}", p.Tag?.GenericValue, depth + 1, depth + 4);
                }
                else if (p.Name.Text is "WidgetStyle")
                {
                    DumpValue($".{p.Name.Text}", p.Tag?.GenericValue, depth + 1, depth + 3);
                }
            }

            if (childrenOf.TryGetValue(name, out var kids))
                foreach (var (child, slot) in kids)
                {
                    // One-line slot geometry (anchors/offsets/padding/size) when present.
                    var geo = slot.Properties
                        .Where(sp => sp.Name.Text is "LayoutData" or "Padding" or "Size" or "HorizontalAlignment"
                            or "VerticalAlignment" or "Row" or "Column" or "RowSpan" or "ColumnSpan")
                        .Select(sp => $"{sp.Name.Text}={Inline(sp.Tag?.GenericValue)}")
                        .ToList();
                    if (geo.Count > 0)
                        _output.WriteLine($"{new string(' ', (depth + 1) * 2)}[slot {slot.ExportType}: {string.Join(" ", geo)}]");
                    PrintWidget(child, depth + 1);
                }
        }

        static string Inline(object? v)
        {
            if (v is CUE4Parse.UE4.Assets.Objects.FScriptStruct ss) v = ss.StructType;
            if (v is CUE4Parse.UE4.Assets.Objects.FStructFallback sf)
                return "{" + string.Join(" ", sf.Properties.Select(p => $"{p.Name.Text}={Inline(p.Tag?.GenericValue)}")) + "}";
            var s = v?.ToString() ?? "(null)";
            return s.Length > 120 ? s[..120] : s;
        }

        // Roots = panel widgets that appear as a parent but are nobody's content.
        foreach (var root in childrenOf.Keys.Where(k => !hasParent.Contains(k)))
            PrintWidget(root, 0);
    }

    // ----------------------------------------------------------- TOPIC 1 probes

    [Fact]
    public void Probe1_PakEntries_GatePal_PDA_Compendium()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        foreach (var keyword in new[] { "GatePal", "PDA", "Compendium", "StoryProgression", "Objective" })
        {
            _output.WriteLine($"--- pak entries containing '{keyword}' ---");
            foreach (var k in provider.Files.Keys
                         .Where(k => k.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(k => k)
                         .Take(200))
            {
                _output.WriteLine($"  {k}");
            }
        }
    }

    [Fact]
    public void Probe2_GatePal_MainWidget_Hierarchy()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        _output.WriteLine("--- all Journal widget packages ---");
        foreach (var k in provider.Files.Keys
                     .Where(k => k.Contains("Widgets/Journal", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(k => k))
        {
            _output.WriteLine($"  {k}");
        }
        _output.WriteLine("");

        foreach (var path in new[]
                 {
                     "AbioticFactor/Content/Blueprints/Widgets/Journal/W_Compendium_Main",
                     "AbioticFactor/Content/Blueprints/Widgets/Journal/W_Compendium_Index",
                     "AbioticFactor/Content/Blueprints/Widgets/Journal/W_Compendium_Index_Button",
                 })
        {
            _output.WriteLine($"##### {path} #####");
            try { DumpWidgetHierarchy(provider.LoadPackage(path)); }
            catch (Exception ex) { _output.WriteLine($"  load failed: {ex.Message}"); }
            _output.WriteLine("");
        }
    }

    [Fact]
    public void Probe2c_IndexButtons_InstanceProps()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        // The 10 app-tile instances in the index: their icon/label instance props.
        var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Widgets/Journal/W_Compendium_Index");
        foreach (var export in pkg.GetExports())
        {
            if (!export.Name.StartsWith("Button_") && export.ExportType != "W_Compendium_Index_Button_C") continue;
            _output.WriteLine($"EXPORT {export.Name} ({export.ExportType})");
            foreach (var p in export.Properties)
                DumpValue(p.Name.Text, p.Tag?.GenericValue, 1, 5);
        }

        // Default-object props of the button class itself (icon Image widget etc.).
        _output.WriteLine("--- W_Compendium_Index_Button full exports ---");
        var btn = provider.LoadPackage("AbioticFactor/Content/Blueprints/Widgets/Journal/W_Compendium_Index_Button");
        foreach (var export in btn.GetExports())
        {
            _output.WriteLine($"EXPORT {export.Name} ({export.ExportType})");
            foreach (var p in export.Properties.Where(p =>
                         p.Name.Text.Contains("Icon", StringComparison.OrdinalIgnoreCase)
                         || p.Name.Text.Contains("Text", StringComparison.OrdinalIgnoreCase)
                         || p.Name.Text.Contains("Image", StringComparison.OrdinalIgnoreCase)
                         || p.Name.Text.Contains("Brush", StringComparison.OrdinalIgnoreCase)
                         || p.Name.Text.Contains("App", StringComparison.OrdinalIgnoreCase)
                         || p.Name.Text.Contains("Section", StringComparison.OrdinalIgnoreCase)))
                DumpValue(p.Name.Text, p.Tag?.GenericValue, 1, 5);
        }
    }

    [Fact]
    public void Probe2d_JournalMain_And_Section_Hierarchies()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        foreach (var path in new[]
                 {
                     "AbioticFactor/Content/Blueprints/Widgets/Journal/W_Player_Journal_Main",
                     "AbioticFactor/Content/Blueprints/Widgets/Journal/W_Player_Emails_Main",
                     "AbioticFactor/Content/Blueprints/Widgets/Journal/W_Compendium_Section",
                     "AbioticFactor/Content/Blueprints/Widgets/Journal/W_Compendium_Entry",
                 })
        {
            _output.WriteLine($"##### {path} #####");
            try { DumpWidgetHierarchy(provider.LoadPackage(path)); }
            catch (Exception ex) { _output.WriteLine($"  load failed: {ex.Message}"); }
            _output.WriteLine("");
        }
    }

    [Fact]
    public void Probe2b_OSAppData_And_JournalMain()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        // OS_AppData looks like the PDA "app" registry (icons, titles, order).
        _output.WriteLine("##### AbioticFactor/Content/Blueprints/Data/OS_AppData #####");
        try
        {
            var pkg = provider.LoadPackage("AbioticFactor/Content/Blueprints/Data/OS_AppData");
            foreach (var export in pkg.GetExports())
            {
                _output.WriteLine($"EXPORT {export.Name} ({export.ExportType})");
                foreach (var p in export.Properties)
                    DumpValue(p.Name.Text, p.Tag?.GenericValue, 1);
                if (export is UDataTable dt)
                {
                    _output.WriteLine($"ROWS {dt.RowMap.Count}");
                    foreach (var kv in dt.RowMap)
                    {
                        _output.WriteLine($"=== {kv.Key.Text} ===");
                        foreach (var p in kv.Value.Properties)
                            DumpValue(p.Name.Text, p.Tag?.GenericValue, 1);
                    }
                }
            }
        }
        catch (Exception ex) { _output.WriteLine($"  load failed: {ex.Message}"); }
    }

    [Fact]
    public void Probe3_GatePal_Textures_And_AppIcons()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        _output.WriteLine("--- GUI chrome textures (PDA/Compendium/Journal/Wristwatch/OS) ---");
        foreach (var k in provider.Files.Keys
                     .Where(k => k.Contains("/Textures/GUI/", StringComparison.OrdinalIgnoreCase))
                     .Where(k => !k.Contains("/Compendium/Entries/", StringComparison.OrdinalIgnoreCase))
                     .Where(k => k.Contains("GatePal", StringComparison.OrdinalIgnoreCase)
                                 || k.Contains("PDA", StringComparison.OrdinalIgnoreCase)
                                 || k.Contains("/Compendium/", StringComparison.OrdinalIgnoreCase)
                                 || k.Contains("/Journal/", StringComparison.OrdinalIgnoreCase)
                                 || k.Contains("/Wristwatch/", StringComparison.OrdinalIgnoreCase)
                                 || k.Contains("/Inventory/", StringComparison.OrdinalIgnoreCase)
                                 || k.Contains("/SectorIcons/", StringComparison.OrdinalIgnoreCase)
                                 || k.Contains("/OS/", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(k => k))
        {
            _output.WriteLine($"  {k}");
        }
    }

    // ----------------------------------------------------------- TOPIC 2 probes

    [Fact]
    public void Probe4_PlayerInventory_Widget_Hierarchy()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        foreach (var name in new[]
                 {
                     "AbioticFactor/Content/Blueprints/Widgets/Inventory/W_PlayerInventory_Main",
                     "AbioticFactor/Content/Blueprints/Widgets/Inventory/W_Inventory_PlayerBackpack",
                     "AbioticFactor/Content/Blueprints/Widgets/Inventory/W_InventoryWeight",
                     "AbioticFactor/Content/Blueprints/Widgets/Inventory/W_Inventory_NavBar",
                     "AbioticFactor/Content/Blueprints/Widgets/Inventory/W_InventoryItemSlot_Tooltip",
                 })
        {
            _output.WriteLine($"##### {name} #####");
            try { DumpWidgetHierarchy(provider.LoadPackage(name)); }
            catch (Exception ex) { _output.WriteLine($"  load failed: {ex.Message}"); }
            _output.WriteLine("");
        }

        _output.WriteLine("--- all inventory widget packages ---");
        foreach (var k in provider.Files.Keys
                     .Where(k => k.Contains("Widgets/Inventory", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(k => k))
        {
            _output.WriteLine($"  {k}");
        }
    }

    // ----------------------------------------------------------- TOPIC 3 probes

    [Fact]
    public void Probe5_StoryProgression_FullDump()
    {
        using var provider = CreateProvider();
        if (provider is null) { _output.WriteLine("No game install; skipping."); return; }

        var path = provider.Files.Keys.FirstOrDefault(
            k => k.Contains("DT_StoryProgression", StringComparison.OrdinalIgnoreCase)
                 && k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(path);
        _output.WriteLine($"##### {path} #####");

        var pkg = provider.LoadPackage(path!);
        foreach (var dt in pkg.GetExports().OfType<UDataTable>())
        {
            _output.WriteLine($"ROWS {dt.RowMap.Count}");
            var index = 0;
            foreach (var kv in dt.RowMap)
            {
                _output.WriteLine($"=== [{index++}] {kv.Key.Text} ===");
                foreach (var p in kv.Value.Properties)
                    DumpValue(p.Name.Text, p.Tag?.GenericValue, 1);
            }
        }
    }
}
