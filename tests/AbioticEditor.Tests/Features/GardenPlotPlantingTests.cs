using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Fixture census and proof for planting/clearing a garden spot. The legacy Cascade world holds a
/// planted small plot (one spot, Nyxshade), the client "Chrissie" world holds seven never-planted
/// small plots (zero-length ItemProxies_). Both use the same class, so "empty" is the absence of a
/// proxy element and planting is appending one game-authored-shape element.
/// </summary>
public sealed class GardenPlotPlantingTests
{
    private static readonly string[] KnownStages = ["Grown", "Harvested", "Dead"];

    private static string? ChrissieFacility()
        => Fixtures.ClientWorldSaves("WorldSave_Facility.sav").FirstOrDefault(p => p.Contains("Chrissie", StringComparison.Ordinal));

    private static IEnumerable<(string Key, IList<FPropertyTag> Props)> Plots(SaveGame save)
        => WorldMapAccessor.Entries(save, "DeployedObjectMap")
            .Where(e => (e.Props.FindByPrefix("Class_")?.Property?.Value?.ToString() ?? "").Contains("/Farming/GardenPlot_", StringComparison.Ordinal))
            .Select(e => (e.Key, e.Props));

    private static int ProxyCount(IList<FPropertyTag> plot)
        => plot.FindByPrefix("ItemProxies_")?.Property is ArrayProperty { Value: { } v } ? v.Length : -1;

    private static SaveGame Reload(SaveGame save)
    {
        using var ms = new MemoryStream();
        save.WriteTo(ms);
        ms.Position = 0;
        return SaveGame.LoadFrom(ms);
    }

    [Fact]
    public void Empty_and_planted_small_plots_both_exist_and_differ_only_in_the_proxy_array()
    {
        var chrissie = ChrissieFacility();
        var legacy = Fixtures.CascadeDir is null ? null : Path.Combine(Fixtures.CascadeDir, "WorldSave_Facility.sav");
        if (chrissie is null || legacy is null || !File.Exists(legacy)) return;

        var emptyPlots = Plots(WorldSaveReader.ReadFromFile(chrissie).Raw).Where(p => GardenPlotPlanting.IsSupported(p.Props)).ToList();
        var plantedPlots = Plots(WorldSaveReader.ReadFromFile(legacy).Raw).Where(p => GardenPlotPlanting.IsSupported(p.Props)).ToList();
        Assert.Equal(7, emptyPlots.Count);
        Assert.All(emptyPlots, p => Assert.Equal(0, ProxyCount(p.Props)));
        var planted = Assert.Single(plantedPlots);
        Assert.Equal(1, ProxyCount(planted.Props));

        // Same top-level tag names (hash suffixes included) in the same order in both states.
        static string[] Names(IList<FPropertyTag> p) => p.Select(t => t.Name?.Value ?? "").ToArray();
        Assert.Equal(Names(planted.Props), Names(emptyPlots[0].Props));
    }

    [Fact]
    public void Read_offers_the_empty_choice_on_an_unplanted_small_plot()
    {
        var chrissie = ChrissieFacility();
        if (chrissie is null) return;
        var save = WorldSaveReader.ReadFromFile(chrissie).Raw;
        var entry = new GardenPlotsFeature().Read(save).First(e => e.Fields.Any(f => f.Id == "crop:0"));
        var field = entry.Fields.Single(f => f.Id == "crop:0");
        Assert.Equal(GardenPlotsFeature.EmptyOption, field.Value);
        Assert.Contains("Plant_Carrot", field.Options!);
    }

    [Fact]
    public void Planting_without_a_planted_template_in_the_same_save_fails_and_changes_nothing()
    {
        var chrissie = ChrissieFacility();
        if (chrissie is null) return;
        var save = WorldSaveReader.ReadFromFile(chrissie).Raw;
        var feature = new GardenPlotsFeature();
        var key = feature.Read(save).First(e => e.Fields.Any(f => f.Id == "crop:0")).Key;
        var before = File.ReadAllBytes(chrissie);
        var result = feature.SetField(save, key, "crop:0", "Plant_Carrot");
        Assert.True(result.IsError);
        Assert.Contains("Plant one in game first", result.Error, StringComparison.Ordinal);
        using var ms = new MemoryStream();
        save.WriteTo(ms);
        Assert.Equal(before, ms.ToArray());
    }

    [Fact]
    public void Clear_then_plant_restores_the_original_layout_with_only_a_new_asset_id()
    {
        var legacy = Fixtures.CascadeDir is null ? null : Path.Combine(Fixtures.CascadeDir, "WorldSave_Facility.sav");
        if (legacy is null || !File.Exists(legacy)) return;
        var save = WorldSaveReader.ReadFromFile(legacy).Raw;
        var feature = new GardenPlotsFeature();
        var entry = feature.Read(save).First(e => WorldMapAccessor.FindEntry(save, "DeployedObjectMap", e.Key) is { } p && GardenPlotPlanting.IsSupported(p));
        var props = WorldMapAccessor.FindEntry(save, "DeployedObjectMap", entry.Key)!;
        var originalCrop = entry.Fields.Single(f => f.Id == "crop:0").Value!;
        var originalLength = SerializedLength(save);

        // Clear: the plot reaches the same shape the never-planted fixture plots have.
        Assert.True(feature.SetField(save, entry.Key, "crop:0", GardenPlotsFeature.EmptyOption).Changed);
        Assert.Equal(0, ProxyCount(props));
        var cleared = Reload(save);
        Assert.Equal(0, ProxyCount(WorldMapAccessor.FindEntry(cleared, "DeployedObjectMap", entry.Key)!));
        Assert.True(SerializedLength(save) < originalLength);
        Assert.Equal(GardenPlotsFeature.EmptyOption, feature.Read(cleared).Single(e => e.Key == entry.Key).Fields.Single(f => f.Id == "crop:0").Value);
        Assert.False(feature.SetField(save, entry.Key, "crop:0", GardenPlotsFeature.EmptyOption).Changed);

        // Plant the same crop back: identical byte length, grown-crop state, fresh unique asset id.
        Assert.True(feature.SetField(save, entry.Key, "crop:0", originalCrop).Changed);
        Assert.Equal(originalLength, SerializedLength(save));
        var replanted = Reload(save);
        var fields = feature.Read(replanted).Single(e => e.Key == entry.Key).Fields;
        Assert.Equal(originalCrop, fields.Single(f => f.Id == "crop:0").Value);
        Assert.Equal("Grown", fields.Single(f => f.Id == "stage:0").Value);
        Assert.Equal("0", fields.Single(f => f.Id == "growth:0").Value);

        // A second plant on a taken spot is refused; a planted spot still edits as before.
        Assert.True(GardenPlotPlanting.Plant(save, props, 0, "Plant_Wheat").IsError);
        Assert.True(feature.SetField(save, entry.Key, "crop:0", "Plant_Wheat").Changed);
        Assert.Equal("Plant_Wheat", feature.Read(save).Single(e => e.Key == entry.Key).Fields.Single(f => f.Id == "crop:0").Value);
    }

    [Fact]
    public void Multi_spot_and_round_plots_are_not_offered_planting()
    {
        var legacy = Fixtures.CascadeDir is null ? null : Path.Combine(Fixtures.CascadeDir, "WorldSave_Facility.sav");
        if (legacy is null || !File.Exists(legacy)) return;
        var save = WorldSaveReader.ReadFromFile(legacy).Raw;
        foreach (var plot in Plots(save).Where(p => !GardenPlotPlanting.IsSupported(p.Props)))
        {
            Assert.True(GardenPlotPlanting.Plant(save, plot.Props, 0, "Plant_Carrot").IsError);
            Assert.True(GardenPlotPlanting.Clear(plot.Props, 0).IsError);
        }
    }

    [Fact]
    public void Every_fixture_growth_stage_is_grown_harvested_or_dead()
    {
        // Census of every fixture plot: only stages 4 (Grown), 5 and 7 (Dead) are ever saved.
        // No fresh Sprout/Budding/Juvenile/Flowering state exists in any fixture.
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var dir in new[] { Fixtures.CascadeDir, Fixtures.ServerWorldsDir }.Where(d => d is not null))
        {
            var path = Path.Combine(dir!, "WorldSave_Facility.sav");
            if (!File.Exists(path)) continue;
            foreach (var entry in new GardenPlotsFeature().Read(WorldSaveReader.ReadFromFile(path).Raw))
                foreach (var f in entry.Fields.Where(f => f.Id.StartsWith("stage:", StringComparison.Ordinal)))
                    seen.Add(f.Value!);
        }
        Assert.All(seen, s => Assert.Contains(s, KnownStages));
    }

    private static long SerializedLength(SaveGame save)
    {
        using var ms = new MemoryStream();
        save.WriteTo(ms);
        return ms.Length;
    }
}
