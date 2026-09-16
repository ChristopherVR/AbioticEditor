using System.IO;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Tests for <see cref="CorpseMapFeature"/>. Loads a dedicated-server region save that carries
/// <c>CorpseMap</c> entries (<c>WorldSave_Facility_DF_Labs.sav</c>, which has four corpses from
/// three different NPC classes), reads entries, removes one, and verifies the change round-trips
/// through a save/reload cycle. All tests skip gracefully when the fixture file is absent.
/// </summary>
public sealed class CorpseMapFeatureTests
{
    private static string RegionPath =>
        Path.Combine(Fixtures.ServerWorldsDir ?? string.Empty, "WorldSave_Facility_DF_Labs.sav");

    private static SaveGame? LoadRegion()
        => File.Exists(RegionPath) ? WorldSaveReader.ReadFromFile(RegionPath).Raw : null;

    [Fact]
    public void Feature_is_discovered_and_applies_to_region()
    {
        var feature = WorldMapFeatures.Find("corpses");
        Assert.NotNull(feature);
        Assert.Equal("CorpseMap", feature!.MapName);
        Assert.Equal("Corpses", feature.DisplayName);
        Assert.True(WorldMapFeatures.IsKnownMap("CorpseMap"));
        Assert.True(feature.SupportsRemoval);

        var save = LoadRegion();
        if (save is null)
        {
            return; // fixture absent - skip
        }
        Assert.True(feature.AppliesTo(save));
    }

    [Fact]
    public void Read_returns_entries_with_npc_class_labels_and_readonly_fields()
    {
        var save = LoadRegion();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("corpses")!;
        var entries = feature.Read(save);

        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            // The label is a readable NPC class ("... Corpse"), never the raw actor path.
            Assert.DoesNotContain("PersistentLevel", entry.Label, System.StringComparison.Ordinal);
            Assert.DoesNotContain("CharacterCorpse", entry.Label, System.StringComparison.Ordinal);
            Assert.EndsWith(" Corpse", entry.Label, System.StringComparison.Ordinal);

            var gibbed = entry.Fields.Single(f => f.Id == "gibbed");
            var looted = entry.Fields.Single(f => f.Id == "looted");
            Assert.False(gibbed.Editable);
            Assert.False(looted.Editable);
            Assert.True(gibbed.Value is "Yes" or "No");
            Assert.True(looted.Value is "Yes" or "No");
        }
        // The fixture region carries a mix of Monster Generic, Order Grunt and Order Sniper corpses.
        Assert.Contains(entries, e => e.Label.StartsWith("Monster Generic", System.StringComparison.Ordinal));
        Assert.Contains(entries, e => e.Label.StartsWith("Order Grunt", System.StringComparison.Ordinal));
        Assert.Contains(entries, e => e.Label.StartsWith("Order Sniper", System.StringComparison.Ordinal));
    }

    [Fact]
    public void SetField_always_fails_no_editable_fields()
    {
        var save = LoadRegion();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("corpses")!;
        var key = feature.Read(save)[0].Key;
        Assert.True(feature.SetField(save, key, "gibbed", "true").IsError);
    }

    [Fact]
    public void Remove_drops_a_corpse_entry_and_round_trips()
    {
        var save = LoadRegion();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("corpses")!;
        var before = feature.Read(save);
        Assert.NotEmpty(before);
        var key = before[0].Key;

        var result = feature.Remove(save, key);
        Assert.True(result.Changed);
        Assert.False(result.IsError);

        var after = feature.Read(save);
        Assert.Equal(before.Count - 1, after.Count);
        Assert.DoesNotContain(after, e => e.Key == key);

        // The removal survives a save/reload round trip; the other corpses are untouched.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        var persisted = feature.Read(reloaded);
        Assert.Equal(after.Count, persisted.Count);
        Assert.DoesNotContain(persisted, e => e.Key == key);
        Assert.Equal(after.Select(e => e.Key).OrderBy(k => k, System.StringComparer.Ordinal),
            persisted.Select(e => e.Key).OrderBy(k => k, System.StringComparer.Ordinal));
    }

    [Fact]
    public void Remove_rejects_unknown_key()
    {
        var save = LoadRegion();
        if (save is null)
        {
            return;
        }
        Assert.True(WorldMapFeatures.Find("corpses")!.Remove(save, "no-such-key").IsError);
    }
}
