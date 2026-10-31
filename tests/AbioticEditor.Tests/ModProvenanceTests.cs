using System.IO;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Compatibility;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// "Is this a mod or not": every catalog entry a mod adds or changes names its mod, and the editor
/// can say in plain words what each mod brings. The deterministic parts run everywhere; the last
/// test runs against real mod.io downloads (set <c>ABIOTIC_MODIO_ROOT</c>) and skips otherwise.
/// </summary>
public sealed class ModProvenanceTests(ITestOutputHelper output)
{
    private static ModOrigin Origin(string key, ModChange change = ModChange.Added) => new(key, key + " name", null, change);

    [Fact]
    public void Summary_counts_what_each_mod_adds_and_changes_once_per_row()
    {
        var kizz = Origin("mod.io/1");
        var grayson = Origin("mod.io/2", ModChange.Changed);
        var rows = new[]
        {
            new ModRowContribution("DT_NPC_Traders", "MGXP4_KizzMachine", kizz),
            new ModRowContribution("DT_NPC_TraderItems", "MGXP4_kizz_shop", kizz),
            new ModRowContribution("DT_NPC_Conversations", "MGXP4_kizz_machine", kizz),
            // A mod item lands in its own table and in the global one: still one item.
            new ModRowContribution("ItemTable_FoodAndGibs", "MGXP4_cubacola", kizz),
            new ModRowContribution("ItemTable_Global", "MGXP4_cubacola", kizz),
            new ModRowContribution("DT_NPC_TraderItems", "trader_grayson", grayson),
        };

        var summaries = ModContentSummary.Build(rows, new Dictionary<string, int> { ["mod.io/3"] = 2 });

        var first = summaries.Single(s => s.ModKey == "mod.io/1");
        Assert.Equal(1, first.Counts.Single(c => c.Kind == ModContentKind.Traders).Added);
        Assert.Equal(1, first.Counts.Single(c => c.Kind == ModContentKind.Items).Added);
        Assert.Equal(1, first.Counts.Single(c => c.Kind == ModContentKind.Supporting).Added);
        var second = summaries.Single(s => s.ModKey == "mod.io/2");
        Assert.Equal(new ModContentCount(ModContentKind.TraderStock, 0, 1), Assert.Single(second.Counts));
        // A mod that only swaps game files is still described.
        Assert.Equal(2, summaries.Single(s => s.ModKey == "mod.io/3").ReplacedGameFiles);
    }

    [Theory]
    [InlineData("DT_NPC_Traders", ModContentKind.Traders)]
    [InlineData("DT_NPC_TraderItems", ModContentKind.TraderStock)]
    [InlineData("DT_PhDs", ModContentKind.Jobs)]
    [InlineData("DT_Traits", ModContentKind.Traits)]
    [InlineData("ItemTable_Craftables", ModContentKind.Items)]
    [InlineData("DT_Recipes", ModContentKind.Recipes)]
    [InlineData("DT_BuffsDebuffs", ModContentKind.Effects)]
    [InlineData("DT_Pets", ModContentKind.Creatures)]
    [InlineData("DT_JobBanners", ModContentKind.Supporting)]
    [InlineData("DT_Something_New", ModContentKind.Other)]
    public void Summary_kind_follows_the_target_table(string table, ModContentKind expected)
        => Assert.Equal(expected, ModContentSummary.KindOf("AbioticFactor/Content/Blueprints/DataTables/" + table));

    [Fact]
    public void A_saved_mod_character_is_matched_to_its_trader_by_class()
    {
        var kemp = new TraderInfo("NK932_DrKemp", null, [], [], [], "Dr Kemp",
            "/DrKempc9PPu/Blueprints/NarrativeNPC_Human_DrKemp.NarrativeNPC_Human_DrKemp_C", Origin("mod.io/6472275"));
        var grayson = new TraderInfo("Grayson", null, [], [], [], "Grayson",
            "/Game/Blueprints/Characters/NarrativeNPCs/NarrativeNPC_Human_GraysonTrader.NarrativeNPC_Human_GraysonTrader_C");
        TraderInfo[] roster = [grayson, kemp];

        Assert.Same(kemp, TraderCatalog.FindForActor(roster, "NarrativeNPC_Human_DrKemp_C_2147482"));
        Assert.Same(kemp, TraderCatalog.FindForActor(roster, "/Game/Maps/Facility.Facility:PersistentLevel.NarrativeNPC_Human_DrKemp_C_3"));
        Assert.Same(kemp, TraderCatalog.FindForActor(roster, kemp.NpcClass));
        Assert.Null(TraderCatalog.FindForActor(roster, "NarrativeNPC_Human_DrKempish_C_1"));
        Assert.Null(TraderCatalog.FindForActor(roster, null));
        Assert.True(kemp.IsModded);
        Assert.False(grayson.IsModded);
    }

    [Fact]
    public void A_mod_editor_template_name_is_not_shown_as_the_mod_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "abiotic-modnames-" + Guid.NewGuid().ToString("N"));
        try
        {
            Install(root, 6485724, "MyProject_Test");
            Install(root, 6486614, "BiometricTraits");

            var mods = ModIoInstallLocator.FindInstalledAt(root).ToDictionary(mod => mod.ModIoId!.Value);

            Assert.Equal("mod.io #6485724", mods[6485724].DisplayName);
            Assert.Equal("BiometricTraits", mods[6486614].DisplayName);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        static void Install(string root, long id, string pluginName)
        {
            var folder = Path.Combine(root, "13105", "mods", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var paks = Directory.CreateDirectory(Path.Combine(folder, "Content", "Paks", "Windows")).FullName;
            File.WriteAllText(Path.Combine(paks, pluginName + "AbioticFactor-Windows.pak"), "pak");
            File.WriteAllText(Path.Combine(folder, pluginName + ".uplugin"), $"{{\"FriendlyName\": \"{pluginName}\"}}");
        }
    }

    /// <summary>
    /// Against real downloads laid out like the game's mod.io cache (skips unless
    /// <c>ABIOTIC_MODIO_ROOT</c> is set and the game is installed). Checked with Kizz Trader,
    /// Dr Kemp Companion, Gloop Trader, Loaded Grayson, Job Overhaul, Gatekeeper Starting Classes,
    /// Order Starting Ranks, Metallurgy Inspector, Interdimensional Chemist and six trait mods.
    /// Each block only runs when its mod is in the cache.
    /// </summary>
    [Fact]
    public void Real_mods_name_their_traders_jobs_traits_and_items()
    {
        if (Environment.GetEnvironmentVariable(ModIoInstallLocator.RootEnvironmentVariable) is not { Length: > 0 }
            || ModIoInstallLocator.FindInstalled() is not { Count: > 0 } mods
            || AfInstallLocator.FindPaksDirectory() is not { } paks
            || GameAssetProvider.FindConventionalMappings() is not { } mappings)
        {
            output.WriteLine("No mod.io cache or game install; skipping.");
            return;
        }
        bool Has(long id) => mods.Any(mod => mod.ModIoId == id);
        using var baseline = GameAssetProvider.CreateForPaks(paks, includeMods: false, mappingsPath: mappings);
        using var provider = GameAssetProvider.CreateForPaks(paks, mods, mappingsPath: mappings);
        var traders = TraderCatalog.LoadFrom(provider).ToDictionary(trader => trader.Id);
        var baseTraders = TraderCatalog.LoadFrom(baseline).ToDictionary(trader => trader.Id);
        var traits = TraitCatalog.LoadDetailsFrom(provider);
        var items = ItemCatalog.LoadFrom(provider);
        var summaries = ModContentSummary.For(provider).ToDictionary(summary => summary.ModKey);

        // Base game content carries no origin.
        Assert.Null(traders["Marion"].Origin);
        Assert.Null(traders["Marion"].StockOrigin);
        Assert.All(TraitCatalog.LoadDetailsFrom(baseline).Values, trait => Assert.Null(trait.Origin));

        if (Has(6421993))
        {
            // Kizz Trader: a whole new trader with its own name, stock and items.
            var kizz = traders["MGXP4_KizzMachine"];
            Assert.Equal("IS-0053", kizz.DisplayName);
            Assert.Equal(new ModOrigin("mod.io/6421993", kizz.Origin!.ModName, 6421993, ModChange.Added), kizz.Origin);
            Assert.Equal("mod.io/6421993", kizz.StockOrigin?.ModKey);
            Assert.Contains(kizz.Sells, offer => offer.ItemId == "MGXP4_cubacola");
            Assert.Equal("mod.io/6421993", items.Find("MGXP4_cubacola")?.Origin?.ModKey);
            Assert.True(summaries["mod.io/6421993"].Counts.Single(count => count.Kind == ModContentKind.Traders).Added == 1);
        }
        if (Has(6472275))
        {
            // Dr Kemp Companion: named from the mod's own data, and found from a saved actor.
            var kemp = traders["NK932_DrKemp"];
            Assert.Equal("Dr Kemp", kemp.DisplayName);
            Assert.Same(kemp, TraderCatalog.FindForActor(traders.Values, "NarrativeNPC_Human_DrKemp_C_2"));
        }
        if (Has(6484711))
        {
            // Loaded Grayson rewrites a base trader's stock: the trader stays base game, the stock does not.
            var grayson = traders["Grayson"];
            Assert.Null(grayson.Origin);
            Assert.Equal(ModChange.Changed, grayson.StockOrigin?.Change);
            Assert.Equal("mod.io/6484711", grayson.StockOrigin?.ModKey);
            Assert.True(grayson.Sells.Count > baseTraders["Grayson"].Sells.Count);
            Assert.Equal(new ModContentCount(ModContentKind.TraderStock, 0, 1), Assert.Single(summaries["mod.io/6484711"].Counts));
        }
        if (Has(6483078))
        {
            // Job Overhaul renames a base job; the composite trait table must show the new name.
            var job = traits["PhD_HumanBio"];
            Assert.Equal("Fluid Dynamics Specialist", job.DisplayName);
            Assert.True(job.IsBackground);
            Assert.Equal(ModChange.Changed, job.Origin?.Change);
        }
        if (Has(6487749))
        {
            Assert.All(traits.Values.Where(trait => trait.Id.StartsWith("NVDT9_Class_", StringComparison.Ordinal)), trait =>
            {
                Assert.True(trait.IsBackground, trait.Id);
                Assert.Equal("mod.io/6487749", trait.Origin?.ModKey);
            });
        }
        if (Has(6485893))
        {
            // A mod trait's id carries the mod prefix (NU0RB_Trait_IBS) and is still a trait, not a job.
            var ibs = traits["NU0RB_Trait_IBS"];
            Assert.False(ibs.IsBackground);
            Assert.Equal(ModChange.Added, ibs.Origin?.Change);
        }
        foreach (var summary in summaries.Values) output.WriteLine($"{summary.ModName}: {string.Join(", ", summary.Counts)} files={summary.ReplacedGameFiles}");
    }
}
