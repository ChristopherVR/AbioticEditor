using System.IO;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Tests for <see cref="TramMapFeature"/>. Loads the Facility region save (which carries
/// <c>TramMap</c>), verifies auto-discovery, reads tram entries, and confirms that every
/// field is read-only and that <see cref="IWorldMapFeature.SetField"/> always returns an
/// error result.
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
    public void Read_returns_entries_with_read_only_fields()
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

        var lastStation = fields.Single(f => f.Id == "lastStation");
        Assert.Equal(WorldFieldKind.Text, lastStation.Kind);
        Assert.False(lastStation.Editable, "lastStation must be read-only");

        var inventories = fields.Single(f => f.Id == "inventories");
        Assert.Equal(WorldFieldKind.Text, inventories.Kind);
        Assert.False(inventories.Editable, "inventories must be read-only");
    }

    /// <summary>
    /// <see cref="IWorldMapFeature.SetField"/> must return an error result for every field
    /// name (including the two exposed fields and an unknown field), because this feature is
    /// entirely read-only. It must never throw.
    /// </summary>
    [Fact]
    public void SetField_always_returns_error_for_all_fields()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("trams")!;
        var key = feature.Read(save)[0].Key;

        // Both exposed read-only fields must be rejected.
        var r1 = feature.SetField(save, key, "lastStation", "SomeStation");
        Assert.True(r1.IsError, "SetField for 'lastStation' should return an error");

        var r2 = feature.SetField(save, key, "inventories", "0");
        Assert.True(r2.IsError, "SetField for 'inventories' should return an error");

        // An unknown field must also be rejected (error comes from the base class for unknown
        // entry, or from ApplyField for known entry + unknown field; both are errors).
        var r3 = feature.SetField(save, key, "nope", "anything");
        Assert.True(r3.IsError, "SetField for unknown field should return an error");

        // A non-existent entry key must be rejected (base-class guard).
        var r4 = feature.SetField(save, "no-such-tram-entry", "lastStation", "X");
        Assert.True(r4.IsError, "SetField for unknown entry key should return an error");
    }
}
