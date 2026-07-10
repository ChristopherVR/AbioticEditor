using System.IO;
using System.Text.RegularExpressions;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// One-shot feasibility probe: does CUE4Parse's locres/culture support actually make
/// AbioticFactor item names resolve to Russian text? FText.Base resolves LocalizedString at
/// *deserialize time*, so culture must be set before the DataTable package is first loaded -
/// this uses two independent providers (one left on English, one switched to "ru") rather than
/// reusing a single provider's cached package.
/// </summary>
public class RussianLocalizationProbeTests
{
    private readonly ITestOutputHelper _output;

    public RussianLocalizationProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static DefaultFileProvider CreateProvider(string paks, string mappings)
    {
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
    public void Probe_ChangeCultureToRussian_ItemNamesResolve()
    {
        var paks = AbioticEditor.Core.Assets.AfInstallLocator.FindPaksDirectory();
        if (paks is null) { _output.WriteLine("No AF install found - skipping."); return; }

        var mappings = AbioticEditor.Core.Assets.GameAssetProvider.FindConventionalMappings();
        if (mappings is null) { _output.WriteLine("No mappings.usmap - skipping."); return; }

        const string path = "AbioticFactor/Content/Blueprints/Items/ItemTable_Global";
        string[] knownItems = { "armor_chest_groupe", "knife_super", "ammo_9mm", "personalteleporter" };

        // --- English baseline ---
        using var enProvider = CreateProvider(paks, mappings);
        _output.WriteLine($"AvailableCultures: {string.Join(", ", enProvider.Internationalization.AvailableCultures)}");
        var enDt = enProvider.LoadPackage(path).GetExports().OfType<UDataTable>().First();
        var enNames = ReadNames(enDt, knownItems);
        foreach (var (id, name) in enNames) _output.WriteLine($"  [en] {id} -> {name}");

        // Inspect the raw FText history to see how the name is actually sourced (inline
        // Base namespace/key/sourceString vs a reference into a UStringTable asset).
        foreach (var id in knownItems)
        {
            var row = enDt.RowMap.FirstOrDefault(kv => kv.Key.Text == id).Value;
            var nameTag = row?.Properties.FirstOrDefault(p => p.Name.Text.StartsWith("ItemName_", StringComparison.Ordinal));
            if (nameTag?.Tag?.GenericValue is FText ft)
            {
                _output.WriteLine($"\n'{id}' ItemName_ HistoryType = {ft.HistoryType}");
                switch (ft.TextHistory)
                {
                    case FTextHistory.Base b:
                        _output.WriteLine($"  Base: Namespace='{b.Namespace}' Key='{b.Key}' SourceString='{b.SourceString}'");
                        break;
                    case FTextHistory.StringTableEntry ste:
                        _output.WriteLine($"  StringTableEntry: TableId='{ste.TableId}' Key='{ste.Key}' SourceString='{ste.SourceString}'");
                        break;
                    default:
                        _output.WriteLine($"  Other history type: {ft.TextHistory.GetType().Name}");
                        break;
                }
            }
            else if (row is not null)
            {
                _output.WriteLine($"\n'{id}' ItemName_ GenericValue is not an FText: {nameTag?.Tag?.GenericValue?.GetType().FullName ?? "<null>"}");
            }
        }

        // --- Russian ---
        // AvailableCultures comes up empty on this cooked build (no CulturesToStage in
        // DefaultGame.ini, and CUE4Parse never auto-parses the .locmeta), so
        // TryChangeCulture("ru") is rejected outright even though ru/Game.locres genuinely
        // exists in the pak. Work around it: find the ru .locres file(s) ourselves and prime
        // Internationalization directly via the public Override() API, which SafeGet() reads
        // regardless of whether ChangeCulture "officially" succeeded.
        using var ruProvider = CreateProvider(paks, mappings);
        bool changed = ruProvider.TryChangeCulture("ru");
        _output.WriteLine($"TryChangeCulture(\"ru\") = {changed}");

        var locresPattern = new Regex(@"/ru/.+\.locres$", RegexOptions.IgnoreCase);
        var ruLocresFiles = ruProvider.Files.Where(f => locresPattern.IsMatch(f.Key)).ToList();
        _output.WriteLine($"Matching ru .locres file(s): {ruLocresFiles.Count}");
        foreach (var f in ruLocresFiles) _output.WriteLine($"  {f.Key}");

        var merged = new Dictionary<string, IDictionary<string, string>>();
        foreach (var f in ruLocresFiles)
        {
            if (!f.Value.TryCreateReader(out var archive)) continue;
            var locres = new FTextLocalizationResource(archive);
            foreach (var ns in locres.Entries)
            {
                if (!merged.TryGetValue(ns.Key.Str, out var dict))
                    merged[ns.Key.Str] = dict = new Dictionary<string, string>();
                foreach (var entry in ns.Value)
                    dict[entry.Key.Str] = entry.Value.LocalizedString;
            }
        }
        _output.WriteLine($"Parsed ru locres namespaces: {merged.Count}, total keys: {merged.Sum(m => m.Value.Count)}");
        ruProvider.Internationalization.Override(merged);
        _output.WriteLine($"Internationalization.Count after Override = {ruProvider.Internationalization.Count}");

        var ruDt = ruProvider.LoadPackage(path).GetExports().OfType<UDataTable>().First();
        var ruNames = ReadNames(ruDt, knownItems);
        foreach (var (id, name) in ruNames) _output.WriteLine($"  [ru] {id} -> {name}");

        var anyDifferent = enNames.Zip(ruNames, (en, ru) => en.name != ru.name).Any(d => d);
        var anyCyrillic = ruNames.Any(r => r.name.Any(c => c >= 0x0400 && c <= 0x04FF));
        _output.WriteLine($"\nAny name changed en->ru: {anyDifferent}");
        _output.WriteLine($"Any ru name contains Cyrillic: {anyCyrillic}");

        // Broad sweep across the WHOLE table (not just 4 hand-picked ids), since
        // ItemTable_Global specifically might just be an unlocalized merge table while the
        // per-category source tables (ItemTable_Gear, ItemTable_Craftables, ...) are the ones
        // actually gathered for translation.
        int total = 0, hit = 0;
        var samples = new List<string>();
        foreach (var kv in ruDt.RowMap)
        {
            var nameTag = kv.Value.Properties.FirstOrDefault(p => p.Name.Text.StartsWith("ItemName_", StringComparison.Ordinal));
            if (nameTag?.Tag?.GenericValue is not FText ft || ft.TextHistory is not FTextHistory.Base b) continue;
            total++;
            if (b.LocalizedString != b.SourceString)
            {
                hit++;
                if (samples.Count < 15) samples.Add($"{kv.Key.Text}: '{b.SourceString}' -> '{b.LocalizedString}' (ns={b.Namespace})");
            }
        }
        _output.WriteLine($"\nWhole-table sweep: {hit}/{total} item names differ from their English source string when ru is loaded.");
        foreach (var s in samples) _output.WriteLine($"  {s}");

        // Sanity check the locres parse itself isn't the problem: scan the WHOLE merged
        // dictionary for any value containing Cyrillic at all, anywhere, in any namespace.
        int cyrillicNamespaces = 0, cyrillicEntries = 0;
        var cyrillicSamples = new List<string>();
        foreach (var ns in merged)
        {
            var hitsInNs = 0;
            foreach (var kv in ns.Value)
            {
                if (kv.Value.Any(c => c >= 0x0400 && c <= 0x04FF))
                {
                    hitsInNs++;
                    cyrillicEntries++;
                    if (cyrillicSamples.Count < 10) cyrillicSamples.Add($"[{ns.Key}] {kv.Key} = '{kv.Value}'");
                }
            }
            if (hitsInNs > 0) cyrillicNamespaces++;
        }
        _output.WriteLine($"\nCyrillic sanity check: {cyrillicEntries} entries across {cyrillicNamespaces} namespace(s) contain Cyrillic text.");
        foreach (var s in cyrillicSamples) _output.WriteLine($"  {s}");

        // Is ANY item-shaped namespace translated at all (item/recipe/skill/trait/fish tables),
        // or is item/data-table text uniformly untranslated across the whole game?
        string[] nsHints = { "Item", "Recipe", "Skill", "Trait", "Fish" };
        foreach (var ns in merged.Keys.Where(n => nsHints.Any(h => n.Contains(h, StringComparison.OrdinalIgnoreCase))))
        {
            var dict = merged[ns];
            var cyrillicCount = dict.Values.Count(v => v.Any(c => c >= 0x0400 && c <= 0x04FF));
            _output.WriteLine($"  namespace '{ns}': {dict.Count} key(s), {cyrillicCount} with Cyrillic");
        }

        // Hypothesis: ItemTable_Global is a merged/cooked copy whose FText got re-stamped with a
        // fresh Namespace/Key at merge time (never part of the gather that produced Game.locres),
        // while the ORIGINAL per-category tables (ItemTable_Gear etc.) that this same row was
        // authored in DO carry a namespace/key the locres covers. Check: does the SAME item id,
        // read from its supplemental source table directly, carry a different (translatable)
        // namespace/key than the copy baked into Global?
        using var discoveryProvider = AbioticEditor.Core.Assets.GameAssetProvider.CreateForPaks(paks, mappingsPath: mappings);
        var supplementalPaths = AbioticEditor.Core.Items.ItemCatalog.DiscoverSupplementalTables(discoveryProvider);
        _output.WriteLine($"\nSupplemental item tables: {supplementalPaths.Count}");
        foreach (var suppPath in supplementalPaths)
        {
            if (!ruProvider.TryLoadPackage(suppPath, out var suppPkg)) continue;
            var suppDt = suppPkg.GetExports().OfType<UDataTable>().FirstOrDefault();
            if (suppDt is null) continue;
            foreach (var id in knownItems)
            {
                var row = suppDt.RowMap.FirstOrDefault(kv => kv.Key.Text == id).Value;
                if (row is null) continue;
                var nameTag = row.Properties.FirstOrDefault(p => p.Name.Text.StartsWith("ItemName_", StringComparison.Ordinal));
                if (nameTag?.Tag?.GenericValue is FText ft2 && ft2.TextHistory is FTextHistory.Base b2)
                {
                    _output.WriteLine($"  '{id}' ALSO found in {suppPath}: Namespace='{b2.Namespace}' Key='{b2.Key}' Source='{b2.SourceString}' Localized='{b2.LocalizedString}'");
                }
            }
        }
    }

    /// <summary>
    /// End-to-end check of the SHIPPED code path (not the manual workaround above):
    /// <c>GameAssetProvider.CreateForPaks(culture: "ru")</c> + <c>ItemCatalog.LoadFrom</c> should
    /// now produce real Cyrillic item names for items whose id also lives in a supplemental
    /// per-category table (see <c>ItemCatalog.RelinkLocalizedText</c>).
    /// </summary>
    [Fact]
    public void EndToEnd_CreateForPaksWithRussianCulture_ItemCatalogNamesAreLocalized()
    {
        var paks = AbioticEditor.Core.Assets.AfInstallLocator.FindPaksDirectory();
        if (paks is null) { _output.WriteLine("No AF install found - skipping."); return; }

        var mappings = AbioticEditor.Core.Assets.GameAssetProvider.FindConventionalMappings();
        if (mappings is null) { _output.WriteLine("No mappings.usmap - skipping."); return; }

        using var enProvider = AbioticEditor.Core.Assets.GameAssetProvider.CreateForPaks(paks, mappingsPath: mappings, culture: "en");
        var enCatalog = AbioticEditor.Core.Items.ItemCatalog.LoadFrom(enProvider);

        using var ruProvider = AbioticEditor.Core.Assets.GameAssetProvider.CreateForPaks(paks, mappingsPath: mappings, culture: "ru");
        var ruCatalog = AbioticEditor.Core.Items.ItemCatalog.LoadFrom(ruProvider);

        string[] knownItems = { "armor_chest_groupe", "knife_super", "ammo_9mm" };
        foreach (var id in knownItems)
        {
            var en = enCatalog.Find(id);
            var ru = ruCatalog.Find(id);
            _output.WriteLine($"{id}: en='{en?.DisplayName}' ru='{ru?.DisplayName}'");
            Assert.NotNull(en);
            Assert.NotNull(ru);
            Assert.NotEqual(en!.DisplayName, ru!.DisplayName);
            Assert.Contains(ru.DisplayName, c => c >= 0x0400 && c <= 0x04FF);
        }

        // Whole-catalog stat: how much of the real catalog actually gets a Russian name via
        // this path (some items may exist ONLY in ItemTable_Global with no supplemental
        // counterpart, and stay English - that's an accepted, graceful limitation).
        var total = ruCatalog.Count;
        var localized = ruCatalog.Entries.Count(e => e.DisplayName.Any(c => c >= 0x0400 && c <= 0x04FF));
        _output.WriteLine($"\n{localized}/{total} catalog entries have a Cyrillic display name.");
        Assert.True(localized > total / 2, $"Expected the majority of the catalog to be localized, got {localized}/{total}.");
    }

    /// <summary>
    /// Scoping check for follow-up work: does <c>DT_Skills</c> (read directly, no merge-table
    /// concept) localize cleanly, and does <c>CDT_AllTraits</c> (a "combined" table, same naming
    /// convention as the problematic <c>ItemTable_Global</c>) suffer the same re-baked-FText
    /// problem traits would need their own fix for?
    /// </summary>
    [Fact]
    public void Scoping_SkillsAndTraits_CultureCoverage()
    {
        var paks = AbioticEditor.Core.Assets.AfInstallLocator.FindPaksDirectory();
        if (paks is null) { _output.WriteLine("No AF install found - skipping."); return; }
        var mappings = AbioticEditor.Core.Assets.GameAssetProvider.FindConventionalMappings();
        if (mappings is null) { _output.WriteLine("No mappings.usmap - skipping."); return; }

        using var ruProvider = CreateProvider(paks, mappings);
        var locresPattern = new Regex(@"/ru/.+\.locres$", RegexOptions.IgnoreCase);
        var merged = new Dictionary<string, IDictionary<string, string>>();
        foreach (var f in ruProvider.Files.Where(f => locresPattern.IsMatch(f.Key)))
        {
            if (!f.Value.TryCreateReader(out var archive)) continue;
            var locres = new FTextLocalizationResource(archive);
            foreach (var ns in locres.Entries)
            {
                if (!merged.TryGetValue(ns.Key.Str, out var dict))
                    merged[ns.Key.Str] = dict = new Dictionary<string, string>();
                foreach (var entry in ns.Value)
                    dict[entry.Key.Str] = entry.Value.LocalizedString;
            }
        }
        ruProvider.Internationalization.Override(merged);

        void Sweep(string label, string path, string namePrefix)
        {
            if (!ruProvider.TryLoadPackage(path, out var pkg))
            {
                _output.WriteLine($"{label}: package not found at {path}");
                return;
            }
            var dt = pkg.GetExports().OfType<UDataTable>().FirstOrDefault();
            if (dt is null) { _output.WriteLine($"{label}: no UDataTable export."); return; }

            int total = 0, hit = 0, stringTableHit = 0, other = 0;
            foreach (var kv in dt.RowMap)
            {
                var nameTag = kv.Value.Properties.FirstOrDefault(p => p.Name.Text.StartsWith(namePrefix, StringComparison.Ordinal));
                if (nameTag?.Tag?.GenericValue is not FText ft)
                {
                    if (nameTag is not null) other++;
                    continue;
                }
                switch (ft.TextHistory)
                {
                    case FTextHistory.Base b:
                        total++;
                        if (b.LocalizedString != b.SourceString) hit++;
                        break;
                    case FTextHistory.StringTableEntry ste:
                        total++;
                        if (ste.LocalizedString != ste.SourceString) stringTableHit++;
                        break;
                    default:
                        other++;
                        break;
                }
            }
            _output.WriteLine($"{label} ({path}): {hit} Base-history + {stringTableHit} StringTableEntry-history differ from English source under ru culture (of {total} FText rows, {other} other/unmatched).");
        }

        Sweep("DT_Skills", "AbioticFactor/Content/Blueprints/DataTables/Customization/DT_Skills", "DisplayName");
        Sweep("CDT_AllTraits", "AbioticFactor/Content/Blueprints/DataTables/Traits/CDT_AllTraits", "TraitName_");
    }

    private static List<(string id, string name)> ReadNames(UDataTable dt, string[] ids)
    {
        var result = new List<(string, string)>();
        foreach (var id in ids)
        {
            var row = dt.RowMap.FirstOrDefault(kv => kv.Key.Text == id).Value;
            if (row is null) { result.Add((id, "<row missing>")); continue; }
            var nameProp = row.Properties.FirstOrDefault(p => p.Name.Text.StartsWith("ItemName_", StringComparison.Ordinal));
            result.Add((id, nameProp?.Tag?.GenericValue?.ToString() ?? "<no ItemName_>"));
        }
        return result;
    }
}
