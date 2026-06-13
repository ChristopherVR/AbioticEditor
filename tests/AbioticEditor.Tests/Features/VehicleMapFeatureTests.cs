using System.IO;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Tests for <see cref="VehicleMapFeature"/>. Loads the Facility region save (which carries
/// <c>VehicleMap</c>), verifies discovery, reads entries, edits <c>driveable</c>, confirms the
/// change re-reads correctly and survives a save/reload round trip, and rejects invalid inputs.
/// All tests skip gracefully when the fixture file is absent.
/// </summary>
public sealed class VehicleMapFeatureTests
{
    private static string FacilityPath => Path.Combine(Fixtures.CascadeDir ?? string.Empty, "WorldSave_Facility.sav");

    private static SaveGame? LoadFacility()
        => File.Exists(FacilityPath) ? WorldSaveReader.ReadFromFile(FacilityPath).Raw : null;

    /// <summary>
    /// The feature must be auto-discovered by <see cref="WorldMapFeatures.Find"/>, report the
    /// correct map name, register as a known map, and apply to the Facility save.
    /// </summary>
    [Fact]
    public void Feature_is_discovered_and_applies_to_facility()
    {
        var feature = WorldMapFeatures.Find("vehicles");
        Assert.NotNull(feature);
        Assert.Equal("VehicleMap", feature!.MapName);
        Assert.True(WorldMapFeatures.IsKnownMap("VehicleMap"));

        var save = LoadFacility();
        if (save is null)
        {
            return; // fixture absent – skip
        }
        Assert.True(feature.AppliesTo(save));
    }

    /// <summary>
    /// <see cref="IWorldMapFeature.Read"/> must return at least one entry and each entry must
    /// contain the <c>driveable</c> editable bool field.
    /// </summary>
    [Fact]
    public void Read_returns_entries_with_driveable_field()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("vehicles")!;
        var entries = feature.Read(save);

        Assert.NotEmpty(entries);
        var field = entries[0].Fields.Single(f => f.Id == "driveable");
        Assert.Equal(WorldFieldKind.Bool, field.Kind);
        Assert.True(field.Editable);
    }

    /// <summary>
    /// <see cref="IWorldMapFeature.SetField"/> with field <c>driveable</c> must toggle the value,
    /// be visible in a subsequent <see cref="IWorldMapFeature.Read"/>, and survive a
    /// <c>WriteTo</c>/<c>LoadFrom</c> round trip.
    /// </summary>
    [Fact]
    public void SetField_toggles_driveable_and_round_trips()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("vehicles")!;
        var entry = feature.Read(save)[0];
        var before = entry.Fields.Single(f => f.Id == "driveable").Value == "true";

        var result = feature.SetField(save, entry.Key, "driveable", (!before).ToString());
        Assert.True(result.Changed);
        Assert.False(result.IsError);

        // Re-read sees the new value.
        var after = feature.Read(save).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "driveable").Value == "true";
        Assert.Equal(!before, after);

        // Survives a save/reload round trip.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        var persisted = feature.Read(reloaded).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "driveable").Value == "true";
        Assert.Equal(!before, persisted);
    }

    /// <summary>
    /// <see cref="IWorldMapFeature.SetField"/> must reject: an unknown field name, a non-existent
    /// entry key, and a non-boolean value string.
    /// </summary>
    [Fact]
    public void SetField_rejects_unknown_field_and_entry_and_invalid_value()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("vehicles")!;
        var key = feature.Read(save)[0].Key;

        Assert.True(feature.SetField(save, key, "nope", "true").IsError);
        Assert.True(feature.SetField(save, "no-such-entry", "driveable", "true").IsError);
        Assert.True(feature.SetField(save, key, "driveable", "notabool").IsError);
    }

    /// <summary>
    /// Read-only fields (<c>class</c>, <c>vehicleId</c>, <c>inventories</c>) must be present
    /// in the entry and must not be editable.
    /// </summary>
    [Fact]
    public void Read_only_fields_are_present_and_not_editable()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("vehicles")!;
        var fields = feature.Read(save)[0].Fields;

        foreach (var id in new[] { "class", "vehicleId", "inventories" })
        {
            var field = fields.Single(f => f.Id == id);
            Assert.False(field.Editable, $"field '{id}' should be read-only");
        }
    }

    /// <summary>
    /// Attempting to set a read-only field must return a failure result, not throw.
    /// </summary>
    [Fact]
    public void SetField_rejects_read_only_fields()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("vehicles")!;
        var key = feature.Read(save)[0].Key;

        Assert.True(feature.SetField(save, key, "class", "anything").IsError);
        Assert.True(feature.SetField(save, key, "vehicleId", "anything").IsError);
        Assert.True(feature.SetField(save, key, "inventories", "0").IsError);
    }
}
