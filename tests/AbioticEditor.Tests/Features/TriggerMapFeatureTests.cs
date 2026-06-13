using System.IO;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Tests for <see cref="TriggerMapFeature"/>. Loads the Facility region save (which carries
/// <c>TriggerMap</c>), reads the entries, edits <c>timesTriggered</c>, and confirms the change
/// both re-reads and survives a save/reload round trip. Mirrors <see cref="ElevatorMapFeatureTests"/>.
/// </summary>
public sealed class TriggerMapFeatureTests
{
    private static string FacilityPath => Path.Combine(Fixtures.CascadeDir ?? string.Empty, "WorldSave_Facility.sav");

    private static SaveGame? LoadFacility()
        => File.Exists(FacilityPath) ? WorldSaveReader.ReadFromFile(FacilityPath).Raw : null;

    [Fact]
    public void Feature_is_discovered_and_applies_to_facility()
    {
        var feature = WorldMapFeatures.Find("triggers");
        Assert.NotNull(feature);
        Assert.Equal("TriggerMap", feature!.MapName);
        Assert.True(WorldMapFeatures.IsKnownMap("TriggerMap"));

        var save = LoadFacility();
        if (save is null)
        {
            return; // fixture absent - skip
        }
        Assert.True(feature.AppliesTo(save));
    }

    [Fact]
    public void Read_returns_non_empty_entries_with_expected_fields()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("triggers")!;
        var entries = feature.Read(save);

        Assert.NotEmpty(entries);

        // triggerId: read-only Text field
        var triggerId = entries[0].Fields.Single(f => f.Id == "triggerId");
        Assert.Equal(WorldFieldKind.Text, triggerId.Kind);
        Assert.False(triggerId.Editable);
        Assert.False(string.IsNullOrWhiteSpace(triggerId.Value));

        // timesTriggered: editable Integer field
        var timesTriggered = entries[0].Fields.Single(f => f.Id == "timesTriggered");
        Assert.Equal(WorldFieldKind.Integer, timesTriggered.Kind);
        Assert.True(timesTriggered.Editable);
        Assert.True(long.TryParse(timesTriggered.Value, out _), "timesTriggered value should be a parseable integer");
    }

    [Fact]
    public void SetField_changes_timesTriggered_and_round_trips()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("triggers")!;
        var entry = feature.Read(save)[0];
        var before = long.Parse(
            entry.Fields.Single(f => f.Id == "timesTriggered").Value!,
            System.Globalization.CultureInfo.InvariantCulture);

        // Use a value that differs from the current one: toggle between 0 and before+1.
        var newValue = before == 0 ? 1 : 0;

        var result = feature.SetField(save, entry.Key, "timesTriggered", newValue.ToString());
        Assert.True(result.Changed);
        Assert.False(result.IsError);

        // Re-read sees the new value.
        var afterRead = long.Parse(
            feature.Read(save).Single(e => e.Key == entry.Key)
                .Fields.Single(f => f.Id == "timesTriggered").Value!,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(newValue, afterRead);

        // Survives a save/reload round trip.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        var persisted = long.Parse(
            feature.Read(reloaded).Single(e => e.Key == entry.Key)
                .Fields.Single(f => f.Id == "timesTriggered").Value!,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(newValue, persisted);
    }

    [Fact]
    public void SetField_rejects_unknown_field_entry_and_invalid_values()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("triggers")!;
        var key = feature.Read(save)[0].Key;

        // Unknown field name
        Assert.True(feature.SetField(save, key, "nope", "1").IsError);

        // Unknown entry key
        Assert.True(feature.SetField(save, "no-such-entry", "timesTriggered", "0").IsError);

        // Non-numeric value
        Assert.True(feature.SetField(save, key, "timesTriggered", "notanumber").IsError);

        // Negative value
        Assert.True(feature.SetField(save, key, "timesTriggered", "-1").IsError);
    }
}
