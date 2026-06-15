using System.IO;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Tests for <see cref="TramMapFeature"/>. Loads the Facility region save (which carries
/// <c>TramMap</c>), verifies auto-discovery, reads tram entries, and confirms the editable
/// last-station choice round-trips while the inventory count stays read-only.
///
/// <para>All tests skip gracefully when the fixture file <c>WorldSave_Facility.sav</c> is
/// absent, the same pattern used by <see cref="ElevatorMapFeatureTests"/> and every other
/// feature test in this suite.</para>
/// </summary>
public sealed class TramMapFeatureTests
{
    private static string FacilityPath => Path.Combine(Fixtures.CascadeDir ?? string.Empty, "WorldSave_Facility.sav");

    private static SaveGame? LoadFacility()
        => File.Exists(FacilityPath) ? WorldSaveReader.ReadFromFile(FacilityPath).Raw : null;

    /// <summary>
    /// <see cref="WorldMapFeatures.Find"/> must locate the feature by its stable id
    /// <c>"trams"</c>, the feature must report the correct map name, and
    /// <see cref="WorldMapFeatures.IsKnownMap"/> must recognise <c>"TramMap"</c>.
    /// When the fixture is available the feature must also apply to the Facility save.
    /// </summary>
    [Fact]
    public void Feature_is_discovered_and_applies_to_facility()
    {
        var feature = WorldMapFeatures.Find("trams");
        Assert.NotNull(feature);
        Assert.Equal("TramMap", feature!.MapName);
        Assert.True(WorldMapFeatures.IsKnownMap("TramMap"));

        var save = LoadFacility();
        if (save is null)
        {
            return; // fixture absent – skip
        }
        Assert.True(feature.AppliesTo(save));
    }

    /// <summary>
    /// <see cref="IWorldMapFeature.Read"/> must return at least one entry when the map is
    /// present, and both expected fields (<c>lastStation</c> and <c>inventories</c>) must
    /// appear on the first entry with <c>Editable == false</c>.
    /// </summary>
    [Fact]
    public void Read_exposes_editable_last_station_and_read_only_inventory()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("trams")!;
        var entries = feature.Read(save);

        Assert.NotEmpty(entries);

        var fields = entries[0].Fields;

        // Last station is now an editable choice over the stations seen in this save.
        var lastStation = fields.Single(f => f.Id == "lastStation");
        Assert.Equal(WorldFieldKind.Enum, lastStation.Kind);
        Assert.True(lastStation.Editable, "lastStation must be editable");
        Assert.NotNull(lastStation.Options);
        Assert.NotEmpty(lastStation.Options!);

        var inventories = fields.Single(f => f.Id == "inventories");
        Assert.Equal(WorldFieldKind.Text, inventories.Kind);
        Assert.False(inventories.Editable, "inventories must be read-only");
    }

    /// <summary>
    /// Setting <c>lastStation</c> to one of its offered station options succeeds and survives a
    /// save/reload; a bogus station, the read-only inventory field, an unknown field, and an
    /// unknown entry key are all rejected without throwing.
    /// </summary>
    [Fact]
    public void SetField_validates_last_station_and_rejects_the_rest()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("trams")!;
        var entry = feature.Read(save)[0];
        var key = entry.Key;
        var lastStation = entry.Fields.Single(f => f.Id == "lastStation");
        var options = lastStation.Options!;

        // Re-parking at an offered station is accepted (no error).
        var target = options.First(o => !string.Equals(o, lastStation.Value, System.StringComparison.Ordinal));
        var ok = feature.SetField(save, key, "lastStation", target);
        Assert.False(ok.IsError, "setting a valid station must not error");
        Assert.Equal(target, feature.Read(save).First(e => e.Key == key).Fields.Single(f => f.Id == "lastStation").Value);

        // A station that is not in the option set is rejected.
        Assert.True(feature.SetField(save, key, "lastStation", "Not A Real Station").IsError);

        // The inventory count is read-only.
        Assert.True(feature.SetField(save, key, "inventories", "0").IsError);

        // Unknown field and unknown entry key are rejected (never throw).
        Assert.True(feature.SetField(save, key, "nope", "anything").IsError);
        Assert.True(feature.SetField(save, "no-such-tram-entry", "lastStation", target).IsError);
    }
}
