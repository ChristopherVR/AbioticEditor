using System.IO;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Integration tests for <see cref="NpcSpawnMapFeature"/>. Loads the Facility region save
/// (which carries <c>NPCSpawnMap</c>), reads entries, edits fields of each editable kind,
/// and confirms that changes both re-read and survive a save/reload round trip.
///
/// <para>Each test gracefully skips when the fixture file is absent (CI environments that
/// don't ship the save fixtures); the fixture is only required for the assertion branch.</para>
/// </summary>
public sealed class NpcSpawnMapFeatureTests
{
    private static string FacilityPath => Path.Combine(Fixtures.CascadeDir ?? string.Empty, "WorldSave_Facility.sav");

    private static SaveGame? LoadFacility()
        => File.Exists(FacilityPath) ? WorldSaveReader.ReadFromFile(FacilityPath).Raw : null;

    // ---------- discovery / applies-to ----------

    /// <summary>The feature is found by reflection and recognised by the map registry.</summary>
    [Fact]
    public void Feature_is_discovered_and_applies_to_facility()
    {
        var feature = WorldMapFeatures.Find("npc-spawns");
        Assert.NotNull(feature);
        Assert.Equal("NPCSpawnMap", feature!.MapName);
        Assert.True(WorldMapFeatures.IsKnownMap("NPCSpawnMap"));

        var save = LoadFacility();
        if (save is null)
        {
            return; // fixture absent - skip
        }
        Assert.True(feature.AppliesTo(save));
    }

    // ---------- read ----------

    /// <summary>Read returns at least one entry and every expected field is present and editable.</summary>
    [Fact]
    public void Read_returns_entries_with_all_expected_fields()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("npc-spawns")!;
        var entries = feature.Read(save);

        Assert.NotEmpty(entries);

        // Every entry must contain all six exposed fields.
        foreach (var entry in entries)
        {
            var byId = entry.Fields.ToDictionary(f => f.Id);

            // Number field
            Assert.True(byId.ContainsKey("cooldownRemaining"), "missing cooldownRemaining");
            Assert.Equal(WorldFieldKind.Number, byId["cooldownRemaining"].Kind);
            Assert.True(byId["cooldownRemaining"].Editable);

            // Integer fields
            foreach (var intField in new[] { "lastDayOnCooldown", "spawnCount", "minutesCooldownStarted" })
            {
                Assert.True(byId.ContainsKey(intField), $"missing {intField}");
                Assert.Equal(WorldFieldKind.Integer, byId[intField].Kind);
                Assert.True(byId[intField].Editable);
            }

            // Bool fields
            foreach (var boolField in new[] { "spawnedOnce", "encounteredOnce" })
            {
                Assert.True(byId.ContainsKey(boolField), $"missing {boolField}");
                Assert.Equal(WorldFieldKind.Bool, byId[boolField].Kind);
                Assert.True(byId[boolField].Editable);
            }
        }
    }

    // ---------- SetField – Integer (spawnCount) ----------

    /// <summary>Setting <c>spawnCount</c> to a new value changes the re-read value and round-trips.</summary>
    [Fact]
    public void SetField_spawnCount_changes_and_round_trips()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("npc-spawns")!;
        var entry = feature.Read(save)[0];
        var key = entry.Key;

        var before = long.Parse(
            entry.Fields.Single(f => f.Id == "spawnCount").Value ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
        var newValue = before + 10;

        var result = feature.SetField(save, key, "spawnCount", newValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(result.Changed, $"SetField reported not-changed (error: {result.Error})");
        Assert.False(result.IsError);

        // Re-read sees the new value.
        var afterRead = long.Parse(
            feature.Read(save).Single(e => e.Key == key).Fields.Single(f => f.Id == "spawnCount").Value ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(newValue, afterRead);

        // Survives a save/reload round trip.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        var persisted = long.Parse(
            feature.Read(reloaded).Single(e => e.Key == key).Fields.Single(f => f.Id == "spawnCount").Value ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(newValue, persisted);
    }

    /// <summary>Setting <c>spawnCount</c> to its current value returns <see cref="WorldEditResult.NoChange"/>.</summary>
    [Fact]
    public void SetField_spawnCount_noChange_when_same_value()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("npc-spawns")!;
        var entry = feature.Read(save)[0];
        var current = entry.Fields.Single(f => f.Id == "spawnCount").Value;

        var result = feature.SetField(save, entry.Key, "spawnCount", current);
        Assert.False(result.Changed);
        Assert.False(result.IsError);
    }

    // ---------- SetField – Bool (spawnedOnce) ----------

    /// <summary>Setting <c>spawnedOnce</c> to the opposite value changes the re-read value and round-trips.</summary>
    [Fact]
    public void SetField_spawnedOnce_toggles_and_round_trips()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("npc-spawns")!;
        var entry = feature.Read(save)[0];
        var key = entry.Key;
        var before = entry.Fields.Single(f => f.Id == "spawnedOnce").Value == "true";

        var result = feature.SetField(save, key, "spawnedOnce", (!before).ToString());
        Assert.True(result.Changed, $"SetField reported not-changed (error: {result.Error})");
        Assert.False(result.IsError);

        // Re-read sees the new value.
        var afterRead = feature.Read(save).Single(e => e.Key == key)
            .Fields.Single(f => f.Id == "spawnedOnce").Value == "true";
        Assert.Equal(!before, afterRead);

        // Survives a save/reload round trip.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        var persisted = feature.Read(reloaded).Single(e => e.Key == key)
            .Fields.Single(f => f.Id == "spawnedOnce").Value == "true";
        Assert.Equal(!before, persisted);
    }

    // ---------- SetField – Number (cooldownRemaining) ----------

    /// <summary>Setting <c>cooldownRemaining</c> to 0 (immediate spawn) changes the re-read value.</summary>
    [Fact]
    public void SetField_cooldownRemaining_to_zero_changes_value()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("npc-spawns")!;

        // Find an entry where cooldownRemaining is not already 0.
        var entry = feature.Read(save)
            .FirstOrDefault(e => e.Fields.Single(f => f.Id == "cooldownRemaining").Value != "0");
        if (entry is null)
        {
            return; // all already at 0, skip
        }

        var result = feature.SetField(save, entry.Key, "cooldownRemaining", "0");
        Assert.True(result.Changed, $"SetField reported not-changed (error: {result.Error})");
        Assert.False(result.IsError);

        var afterValue = feature.Read(save).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "cooldownRemaining").Value;
        Assert.Equal("0", afterValue);
    }

    // ---------- rejection cases ----------

    /// <summary>Unknown field, unknown entry, and invalid values are all rejected without throwing.</summary>
    [Fact]
    public void SetField_rejects_unknown_field_and_entry_and_invalid_values()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("npc-spawns")!;
        var key = feature.Read(save)[0].Key;

        // Unknown field
        Assert.True(feature.SetField(save, key, "nope", "true").IsError);

        // Unknown entry
        Assert.True(feature.SetField(save, "no-such-entry", "spawnCount", "1").IsError);

        // Invalid value for bool field
        Assert.True(feature.SetField(save, key, "spawnedOnce", "notabool").IsError);

        // Invalid value for integer field
        Assert.True(feature.SetField(save, key, "spawnCount", "notanumber").IsError);

        // Invalid value for double field
        Assert.True(feature.SetField(save, key, "cooldownRemaining", "notanumber").IsError);
    }
}
