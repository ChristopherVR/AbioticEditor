using System.IO;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class DiscoveryTests
{
    private readonly ITestOutputHelper _output;

    public DiscoveryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? FixturePlayerSave()
    {
        if (Fixtures.CascadeDir is null) return null;
        var path = Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void Reads_DiscoveryAndCompendiumLists()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        _output.WriteLine($"picked up: {data.ItemsPickedUp.Count}, crafted: {data.CraftedItems.Count}, maps: {data.MapsUnlocked.Count}");
        _output.WriteLine($"compendium e/n/x: {data.CompendiumEmail.Count}/{data.CompendiumNarrative.Count}/{data.CompendiumExploration.Count}");
        Assert.True(data.ItemsPickedUp.Count > 500);
        Assert.True(data.MapsUnlocked.Count >= 8);
        Assert.True(data.CompendiumEmail.Count > 10);
        Assert.True(data.CompendiumNarrative.Count > 10);
        Assert.True(data.CompendiumExploration.Count > 10);
    }

    [Fact]
    public void ApplyDiscoveryAndCompendium_RoundTripsThroughSerializer()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        var pickedUp = data.ItemsPickedUp.Append("test_item_sentinel").ToList();
        var crafted = data.CraftedItems.Append("test_item_sentinel").ToList();
        var maps = data.MapsUnlocked.Append("Map_Reactors").ToList();
        var compEmail = data.CompendiumEmail.Append("TestSentinel").ToList();
        var compNarr = data.CompendiumNarrative.ToList();
        var compExpl = data.CompendiumExploration.Append("TestSentinel2").ToList();

        PlayerSaveWriter.ApplyItemsPickedUp(data, pickedUp);
        PlayerSaveWriter.ApplyCraftedItems(data, crafted);
        PlayerSaveWriter.ApplyMapsUnlocked(data, maps);
        PlayerSaveWriter.ApplyCompendium(data, compEmail, compNarr, compExpl);

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = PlayerSaveReader.ReadFromStream(ms);

        Assert.Equal(pickedUp, reloaded.ItemsPickedUp);
        Assert.Equal(crafted, reloaded.CraftedItems);
        Assert.Equal(maps, reloaded.MapsUnlocked);
        Assert.Equal(compEmail, reloaded.CompendiumEmail);
        Assert.Equal(compNarr, reloaded.CompendiumNarrative);
        Assert.Equal(compExpl, reloaded.CompendiumExploration);
    }

    [Fact]
    public void MapCatalog_LoadsPamphlets()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("No install/mappings."); return; }

        var maps = MapCatalog.LoadFrom(provider);
        _output.WriteLine(string.Join(", ", maps));
        Assert.True(maps.Count >= 11);
        Assert.Contains("Map_Reactors", maps);
    }

    [Fact]
    public void CompendiumTypeMapping_MatchesSaveArrays()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) return;
        var path = FixturePlayerSave();
        if (path is null) return;

        // For every unlocked entry, recompute which arrays it should appear in from the
        // catalog's section types and compare against where the game actually put it.
        var catalog = Core.Codex.CodexCatalog.LoadCompendium(provider)
            .ToDictionary(c => c.Id, StringComparer.Ordinal);
        var data = PlayerSaveReader.ReadFromFile(path);

        var mismatches = new List<string>();
        foreach (var (arrayName, rows) in new (string, IReadOnlyList<string>)[]
        {
            ("Email", data.CompendiumEmail),
            ("Narrative", data.CompendiumNarrative),
            ("Exploration", data.CompendiumExploration),
        })
        {
            foreach (var row in rows)
            {
                if (!catalog.TryGetValue(row, out var entry)) continue; // legacy row
                var expected = entry.SectionTypes.Select(t =>
                    t.StartsWith("Email", StringComparison.Ordinal) ? "Email"
                    : t.StartsWith("Narrative", StringComparison.Ordinal) ? "Narrative" : "Exploration");
                if (!expected.Contains(arrayName))
                {
                    mismatches.Add($"{row} in {arrayName} but types=[{string.Join(",", entry.SectionTypes)}]");
                }
            }
        }
        foreach (var m in mismatches.Take(20)) _output.WriteLine(m);
        _output.WriteLine($"{mismatches.Count} mismatch(es)");
        // A handful of rows drifted between arrays across game patches (5 observed);
        // that's why the editor preserves original placements and only uses the type
        // mapping for newly unlocked entries. Alert if the drift grows significantly.
        Assert.True(mismatches.Count <= 10, "type mapping diverges badly from the game's array placement");
    }

    [Fact]
    public void CompendiumEntries_CarrySectionTypes()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("No install/mappings."); return; }

        var compendium = Core.Codex.CodexCatalog.LoadCompendium(provider);
        var withTypes = compendium.Count(c => c.SectionTypes.Count > 0);
        _output.WriteLine($"{withTypes} of {compendium.Count} entries have section types");
        var typeNames = compendium.SelectMany(c => c.SectionTypes).Distinct().OrderBy(t => t).ToList();
        _output.WriteLine("types: " + string.Join(", ", typeNames));
        Assert.True(withTypes > compendium.Count / 2);
        Assert.Contains("Exploration", typeNames);
    }
}
