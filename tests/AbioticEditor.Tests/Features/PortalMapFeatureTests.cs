using System.IO;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Tests for the PortalMap (teleporters) feature. Loads the Facility region save (which carries
/// <c>PortalMap</c> with eight <c>BP_Teleporter_ParentBP_C_*</c> entries), reads them, toggles
/// the editable <c>active</c> field, and confirms the change re-reads and survives a save/reload
/// round trip. Mirrors <see cref="ElevatorMapFeatureTests"/>.
/// </summary>
public sealed class PortalMapFeatureTests
{
    private static string FacilityPath => Path.Combine(Fixtures.CascadeDir ?? string.Empty, "WorldSave_Facility.sav");

    private static SaveGame? LoadFacility()
        => File.Exists(FacilityPath) ? WorldSaveReader.ReadFromFile(FacilityPath).Raw : null;

    [Fact]
    public void Feature_is_discovered_and_applies_to_facility()
    {
        var feature = WorldMapFeatures.Find("portals");
        Assert.NotNull(feature);
        Assert.Equal("PortalMap", feature!.MapName);
        Assert.True(WorldMapFeatures.IsKnownMap("PortalMap"));

        var save = LoadFacility();
        if (save is null)
        {
            return; // fixture absent - skip
        }
        Assert.True(feature.AppliesTo(save));
    }

    [Fact]
    public void Read_returns_entries_with_active_field()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("portals")!;
        var entries = feature.Read(save);

        Assert.NotEmpty(entries);
        var field = entries[0].Fields.Single(f => f.Id == "active");
        Assert.Equal(WorldFieldKind.Bool, field.Kind);
        Assert.True(field.Editable);
    }

    [Fact]
    public void SetField_toggles_active_and_round_trips()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("portals")!;
        var entry = feature.Read(save)[0];
        var before = feature.Read(save)[0].Fields.Single(f => f.Id == "active").Value == "true";

        var result = feature.SetField(save, entry.Key, "active", (!before).ToString());
        Assert.True(result.Changed);
        Assert.False(result.IsError);

        // Re-read sees the new value.
        var after = feature.Read(save).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "active").Value == "true";
        Assert.Equal(!before, after);

        // Survives a save/reload round trip.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        var persisted = feature.Read(reloaded).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "active").Value == "true";
        Assert.Equal(!before, persisted);
    }

    [Fact]
    public void SetField_rejects_unknown_field_entry_and_invalid_value()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("portals")!;
        var key = feature.Read(save)[0].Key;

        Assert.True(feature.SetField(save, key, "nope", "true").IsError);
        Assert.True(feature.SetField(save, "no-such-entry", "active", "true").IsError);
        Assert.True(feature.SetField(save, key, "active", "notabool").IsError);
    }
}
