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

    /// <summary>
    /// The catalog is read from the game's level; this checks it against real saves. In every
    /// fixture world each tram must be parked at a station on its own line, and no tram may be
    /// offered a station from another line.
    /// </summary>
    [Fact]
    public void Every_saved_tram_is_parked_on_its_own_line_and_is_offered_only_that_line()
    {
        var feature = WorldMapFeatures.Find("trams")!;
        var checkedTrams = 0;
        foreach (var (_, d) in AllFixtureSaves.WorldSaves)
        {
            if (!feature.AppliesTo(d.Raw)) continue;
            foreach (var entry in feature.Read(d.Raw))
            {
                var line = TramNetworkCatalog.LineFor(entry.Key);
                Assert.NotNull(line);
                var field = entry.Fields.Single(f => f.Id == "lastStation");
                var current = TramNetworkCatalog.StationForLabel(line!, field.Value);
                Assert.NotNull(current);
                Assert.Contains(current!.Value, line.Stations);

                // Offered in route order, exactly this line's stops.
                var offered = field.Options!.Select(o => TramNetworkCatalog.StationForLabel(line, o)).ToList();
                Assert.Equal(line.Stations.Cast<int?>(), offered);
                Assert.Equal(TramNetworkCatalog.RouteName(line), entry.Label);
                checkedTrams++;
            }
        }
        if (checkedTrams == 0) return; // no fixture
    }

    /// <summary>
    /// A re-parked tram changes only a soft object path. The save comparison used to print every
    /// such path as its type name, so the change compared as "identical"; it must now show.
    /// </summary>
    [Fact]
    public void Reparking_a_tram_shows_up_in_a_save_comparison()
    {
        var before = LoadFacility();
        var after = LoadFacility();
        if (before is null || after is null) return;
        var feature = WorldMapFeatures.Find("trams")!;
        var entry = feature.Read(after).First(e => TramNetworkCatalog.LineFor(e.Key) is { Stations.Count: > 1 });
        var field = entry.Fields.Single(f => f.Id == "lastStation");
        var other = field.Options!.First(o => o != field.Value);
        Assert.False(feature.SetField(after, entry.Key, "lastStation", other).IsError);

        var diff = AbioticEditor.Core.Compare.SaveComparer.Compare(before, after);
        var change = Assert.Single(diff.Differences);
        Assert.Contains("LastStation", change.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void SetField_refuses_a_station_on_a_different_line()
    {
        var save = LoadFacility();
        if (save is null) return;
        var feature = WorldMapFeatures.Find("trams")!;
        var entry = feature.Read(save)[0];
        var ownLine = TramNetworkCatalog.LineFor(entry.Key)!;
        var foreign = TramNetworkCatalog.Lines.First(line => !ReferenceEquals(line, ownLine)).Stations[0];

        var result = feature.SetField(save, entry.Key, "lastStation", TramNetworkCatalog.Label(foreign));
        Assert.True(result.IsError, "a station from another line must be refused");
    }

    [Theory]
    [InlineData("Station 4", 4)]
    [InlineData("PersistentLevel.TramSystem_Station_C_9", 9)]
    public void Station_labels_and_paths_both_give_the_station_number(string text, int expected)
        => Assert.Equal(expected, TramStationCatalog.StationNumber(text));

    [Fact]
    public void Network_catalog_covers_every_station_exactly_once_and_names_lines()
    {
        var all = TramNetworkCatalog.Lines.SelectMany(l => l.Stations).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(28, all.Count);
        Assert.Equal(10, TramNetworkCatalog.Lines.Sum(l => l.Trams.Count));
        // Stop labels read like the game: a name, a stop number only where a route repeats a name.
        Assert.Equal("Cascade Laboratories (stop 3)", TramNetworkCatalog.Label(12));
        Assert.Equal("Cascade Laboratories (stop 2)", TramNetworkCatalog.Label(14));
        Assert.Equal("Hydroplant", TramNetworkCatalog.Label(13));
        Assert.Equal("Stop 2", TramNetworkCatalog.Label(4)); // the containment lift's stops are unnamed

        // Route names, one per tram, all different.
        var names = TramNetworkCatalog.Lines.Select(TramNetworkCatalog.RouteName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Contains("Containment lift", names);
        Assert.Contains("The Office Sector ↔ Cascade Laboratories", names);
        Assert.Contains("Power Services ↔ Dusk Reactor ↔ Gale Reactor", names);
        Assert.Equal(1, TramNetworkCatalog.Lines.Count(l => l.IsLift));
    }

    [Fact]
    public void Stops_are_found_by_label_within_their_own_route_only()
    {
        var office = TramNetworkCatalog.LineOfStation(13)!; // Hydroplant <-> The Office Sector
        Assert.Equal(20, TramNetworkCatalog.StationForLabel(office, "The Office Sector"));
        var mines = TramNetworkCatalog.LineOfStation(2)!;
        Assert.Equal(3, TramNetworkCatalog.StationForLabel(mines, "The Office Sector"));
        Assert.Null(TramNetworkCatalog.StationForLabel(mines, "Hydroplant"));
    }
}
