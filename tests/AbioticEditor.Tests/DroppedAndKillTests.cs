using System.IO;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class DroppedAndKillTests
{
    private readonly ITestOutputHelper _output;

    public DroppedAndKillTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? FacilitySave()
    {
        if (Fixtures.CascadeDir is null) return null;
        var path = Path.Combine(Fixtures.CascadeDir, "WorldSave_Facility.sav");
        return File.Exists(path) ? path : null;
    }

    private static string? FixturePlayerSave()
    {
        if (Fixtures.CascadeDir is null) return null;
        var path = Path.Combine(Fixtures.CascadeDir, "PlayerData", "Player_76561197993781479.sav");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void Reads_DroppedItems()
    {
        var path = FacilitySave();
        Assert.NotNull(path);

        var data = WorldSaveReader.ReadFromFile(path!);
        _output.WriteLine($"{data.DroppedItems.Count} dropped items");
        Assert.True(data.DroppedItems.Count > 1000);
        var named = data.DroppedItems.Count(d => !string.IsNullOrEmpty(d.Slot.ItemId) && d.Slot.ItemId != "Empty");
        _output.WriteLine($"{named} with item ids");
        Assert.True(named > 1000);
    }

    [Fact]
    public void RemoveDroppedItems_RoundTripsThroughSerializer()
    {
        var path = FacilitySave();
        Assert.NotNull(path);

        var data = WorldSaveReader.ReadFromFile(path!);
        var before = data.DroppedItems.Count;
        var toRemove = data.DroppedItems.Take(5).Select(d => d.Id).ToList();

        var removed = WorldSaveWriter.RemoveDroppedItems(data, toRemove);
        Assert.Equal(5, removed);

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = WorldSaveReader.ReadFromStream(ms);

        Assert.Equal(before - 5, reloaded.DroppedItems.Count);
        Assert.DoesNotContain(reloaded.DroppedItems, d => toRemove.Contains(d.Id));
    }

    [Fact]
    public void ApplyDroppedItems_PatchesSlot()
    {
        var path = FacilitySave();
        Assert.NotNull(path);

        var data = WorldSaveReader.ReadFromFile(path!);
        var first = data.DroppedItems.First(d => d.Slot.ItemId is not null && d.Slot.ItemId != "Empty");
        var updated = first with { Slot = first.Slot with { Count = 99 }, NoDespawn = true };
        WorldSaveWriter.ApplyDroppedItems(data, new[] { updated });

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = WorldSaveReader.ReadFromStream(ms);

        var match = reloaded.DroppedItems.First(d => d.Id == first.Id);
        Assert.Equal(99, match.Slot.Count);
        Assert.True(match.NoDespawn);
    }

    [Fact]
    public void Reads_KillCountsAndFish()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        foreach (var k in data.KillCounts) _output.WriteLine($"{k.CompendiumRow}: {k.Count}");
        Assert.Contains(data.KillCounts, k => k.CompendiumRow == "Peccary" && k.Count == 48);
        Assert.Contains("Antefish", data.FishCaught);
    }

    [Fact]
    public void ApplyKillCountsAndFish_RoundTripThroughSerializer()
    {
        var path = FixturePlayerSave();
        Assert.NotNull(path);

        var data = PlayerSaveReader.ReadFromFile(path!);
        var updated = data.KillCounts
            .Select(k => k.CompendiumRow == "Peccary" ? k with { Count = 500 } : k)
            .ToList();
        var fish = data.FishCaught.Append("Fogfish").ToList();
        PlayerSaveWriter.ApplyKillCounts(data, updated);
        PlayerSaveWriter.ApplyFishCaught(data, fish);

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = PlayerSaveReader.ReadFromStream(ms);

        Assert.Contains(reloaded.KillCounts, k => k.CompendiumRow == "Peccary" && k.Count == 500);
        Assert.Equal(fish, reloaded.FishCaught);
    }

    [Fact]
    public void FishCatalog_LoadsWithItemRows()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null || !provider.HasMappings) { _output.WriteLine("No install/mappings."); return; }

        var fish = CodexCatalog.LoadFish(provider);
        _output.WriteLine($"{fish.Count} fish; rare: {fish.Count(f => f.IsRare)}");
        Assert.True(fish.Count >= 30);
        var ante = fish.First(f => f.Id == "Antefish");
        Assert.Equal("fish_antefish", ante.ItemId);

        var compendium = CodexCatalog.LoadCompendium(provider);
        var peccary = compendium.First(c => c.Id == "Peccary");
        Assert.Equal(500, peccary.KillRequired);
    }
}
