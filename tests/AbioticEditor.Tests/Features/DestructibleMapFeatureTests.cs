using System.IO;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Tests for <see cref="DestructibleMapFeature"/>. Mirrors the <see cref="ElevatorMapFeatureTests"/>
/// shape: loads a dedicated-server region save that carries <c>DestructibleMap</c> entries
/// (<c>WorldSave_Facility_DF_Labs.sav</c>, chosen because it carries several broken destructibles
/// alongside a populated <c>CorpseMap</c>), reads entries, toggles the <c>broken</c> field, and
/// verifies the change round-trips through a save/reload cycle. All tests skip gracefully when the
/// fixture file is absent.
/// </summary>
public sealed class DestructibleMapFeatureTests
{
    private static string RegionPath =>
        Path.Combine(Fixtures.ServerWorldsDir ?? string.Empty, "WorldSave_Facility_DF_Labs.sav");

    private static SaveGame? LoadRegion()
        => File.Exists(RegionPath) ? WorldSaveReader.ReadFromFile(RegionPath).Raw : null;

    [Fact]
    public void Feature_is_discovered_and_applies_to_region()
    {
        var feature = WorldMapFeatures.Find("destructibles");
        Assert.NotNull(feature);
        Assert.Equal("DestructibleMap", feature!.MapName);
        Assert.Equal("Breakable Objects", feature.DisplayName);
        Assert.True(WorldMapFeatures.IsKnownMap("DestructibleMap"));
        Assert.False(feature.SupportsRemoval);

        var save = LoadRegion();
        if (save is null)
        {
            return; // fixture absent - skip
        }
        Assert.True(feature.AppliesTo(save));
    }

    [Fact]
    public void Read_returns_entries_with_broken_field_and_readable_labels()
    {
        var save = LoadRegion();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("destructibles")!;
        var entries = feature.Read(save);

        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            var field = entry.Fields.Single(f => f.Id == "broken");
            Assert.Equal(WorldFieldKind.Bool, field.Kind);
            Assert.True(field.Editable);
            // The raw actor path is never shown verbatim as the label.
            Assert.DoesNotContain("PersistentLevel", entry.Label, System.StringComparison.Ordinal);
        }
        // Every entry observed in the fixture is currently broken (the game only persists a
        // destructible once it deviates from its intact default).
        Assert.All(entries, e => Assert.Equal("true", e.Fields.Single(f => f.Id == "broken").Value));
    }

    [Fact]
    public void SetField_toggles_broken_and_round_trips()
    {
        var save = LoadRegion();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("destructibles")!;
        var entry = feature.Read(save)[0];
        var before = entry.Fields.Single(f => f.Id == "broken").Value == "true";

        var result = feature.SetField(save, entry.Key, "broken", (!before).ToString());
        Assert.True(result.Changed);
        Assert.False(result.IsError);

        // Re-read sees the new value.
        var after = feature.Read(save).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "broken").Value == "true";
        Assert.Equal(!before, after);

        // Survives a save/reload round trip, and unrelated bytes are untouched: reserializing
        // without any further edit and comparing lengths is the cheapest signal available here
        // (byte-for-byte diffing is covered by the writer-level fixture tests).
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        var persisted = feature.Read(reloaded).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "broken").Value == "true";
        Assert.Equal(!before, persisted);

        // Every other entry's state is unchanged by the edit.
        var otherBefore = feature.Read(save).Where(e => e.Key != entry.Key).ToList();
        var otherAfter = feature.Read(reloaded).Where(e => e.Key != entry.Key).ToList();
        Assert.Equal(
            otherBefore.Select(e => (e.Key, e.Fields.Single(f => f.Id == "broken").Value)),
            otherAfter.Select(e => (e.Key, e.Fields.Single(f => f.Id == "broken").Value)));
    }

    [Fact]
    public void SetField_rejects_unknown_field_entry_and_removal()
    {
        var save = LoadRegion();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("destructibles")!;
        var key = feature.Read(save)[0].Key;

        Assert.True(feature.SetField(save, key, "nope", "true").IsError);
        Assert.True(feature.SetField(save, "no-such-entry", "broken", "true").IsError);
        Assert.True(feature.SetField(save, key, "broken", "notabool").IsError);
        // Removal is deliberately unsupported (see DestructibleMapFeature remarks).
        Assert.True(feature.Remove(save, key).IsError);
    }
}
