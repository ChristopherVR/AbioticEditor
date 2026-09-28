using System.Text.RegularExpressions;
using AbioticEditor.Core.Compatibility;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Tests;

/// <summary>
/// Pins the placement classification and the tram station findings to the fixtures, so the
/// research doc's claims stay provable.
/// </summary>
public sealed partial class WorldObjectPlacementCatalogTests
{
    [GeneratedRegex("^[0-9A-Fa-f]{32}$")]
    private static partial Regex Guid32();

    private static readonly string[] PositionLeaves = ["Transform", "Location", "ItemLocation", "CurrentPosition", "Translation"];

    private static IEnumerable<(string Key, IList<FPropertyTag> Props)> Entries(SaveGame save, string map)
    {
        if (save.Properties?.FindByPrefix(map)?.Property is not MapProperty { Value: { } pairs }) yield break;
        foreach (var kv in pairs)
        {
            if (kv.Value is StructProperty { Value: PropertiesStruct ps })
            {
                yield return (kv.Key.Value?.ToString() ?? string.Empty, ps.Properties);
            }
        }
    }

    private static bool HasNonZeroVector(IList<FPropertyTag> props, string leaf)
    {
        var prop = props.FindByPrefix(leaf + "_")?.Property as StructProperty;
        return prop?.Value switch
        {
            VectorStruct v => v.Value.X != 0 || v.Value.Y != 0 || v.Value.Z != 0,
            PropertiesStruct ps when ps.Properties.FindByPrefix("Translation")?.Property is StructProperty { Value: VectorStruct t }
                => t.Value.X != 0 || t.Value.Y != 0 || t.Value.Z != 0,
            _ => false,
        };
    }

    [Fact]
    public void Save_spawned_maps_are_guid_keyed_with_a_position()
    {
        var any = false;
        foreach (var (_, d) in AllFixtureSaves.WorldSaves)
        {
            foreach (var (map, leaf) in new[] { ("DroppedItemMap", "ItemLocation"), ("PetNPC", "Location") })
            {
                foreach (var (key, props) in Entries(d.Raw, map))
                {
                    any = true;
                    Assert.Matches(Guid32(), key);
                    Assert.True(HasNonZeroVector(props, leaf), $"{map} {key}");
                    Assert.Equal(PlacementAuthority.SaveSpawned, WorldObjectPlacementCatalog.ForEntry(map, key));
                }
            }
        }
        Assert.True(any || AllFixtureSaves.WorldSaves.Count == 0);
    }

    [Fact]
    public void Mixed_maps_split_by_key_shape()
    {
        foreach (var (_, d) in AllFixtureSaves.WorldSaves)
        {
            foreach (var map in new[] { "DeployedObjectMap", "VehicleMap" })
            {
                foreach (var (key, _) in Entries(d.Raw, map))
                {
                    var expected = Guid32().IsMatch(key) ? PlacementAuthority.SaveSpawned : PlacementAuthority.LevelPlacedSavedPosition;
                    Assert.Equal(expected, WorldObjectPlacementCatalog.ForEntry(map, key));
                    Assert.Equal(!Guid32().IsMatch(key), key.Contains("PersistentLevel.", StringComparison.Ordinal));
                }
            }
        }
    }

    [Fact]
    public void Narrative_npc_locations_are_always_the_origin()
    {
        foreach (var (_, d) in AllFixtureSaves.WorldSaves)
        {
            foreach (var (key, props) in Entries(d.Raw, "NarrativeNPCMap"))
            {
                Assert.False(HasNonZeroVector(props, "Location"), key);
            }
        }
    }

    [Fact]
    public void State_only_maps_carry_no_position_member_and_are_level_path_keyed()
    {
        var stateOnly = WorldObjectPlacementCatalog.Maps.Where(m => m.Authority == PlacementAuthority.LevelPlacedStateOnly).ToList();
        Assert.NotEmpty(stateOnly);

        var leaves = UnmodeledFieldCensus.CollectLeaves(AllFixtureSaves.WorldSaves.Select(w => w.Data.Raw));
        foreach (var m in stateOnly)
        {
            Assert.DoesNotContain(leaves, l => l.Map == m.Map && PositionLeaves.Contains(l.Leaf));
            Assert.Null(m.PositionLeaf);
        }
    }

    [Fact]
    public void Every_classified_map_is_a_known_modeled_map()
    {
        Assert.All(WorldObjectPlacementCatalog.Maps, m => Assert.True(WorldSaveReader.IsModeledTopLevelKey(m.Map), m.Map));
    }

    [Theory]
    [InlineData("TramSystem_Station_C_9", 9)]
    [InlineData("PersistentLevel.TramSystem_Station_C_27", 27)]
    [InlineData("Tram_Default_C_1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Station_number_parses_saved_references(string? subPath, int? expected)
        => Assert.Equal(expected, TramStationCatalog.StationNumber(subPath));

    [Fact]
    public void Tram_unlock_flags_are_real_game_flags()
    {
        var known = new HashSet<string>(QuestFlagCatalog.KnownFlags, StringComparer.OrdinalIgnoreCase);
        Assert.All(TramStationCatalog.UnlockFlags, f => Assert.Contains(f.Flag, known));
    }

    [Fact]
    public void Occupied_stations_differ_between_worlds_so_no_single_save_lists_them_all()
    {
        var feature = WorldMapFeatures.Find("trams")!;
        var perSave = new List<HashSet<int>>();
        foreach (var (_, d) in AllFixtureSaves.WorldSaves)
        {
            if (!feature.AppliesTo(d.Raw)) continue;
            var stations = feature.Read(d.Raw)
                .Select(e => e.Fields.FirstOrDefault(f => f.Id == "lastStation")?.Value)
                .Select(TramStationCatalog.StationNumber)
                .OfType<int>().ToHashSet();
            perSave.Add(stations);
        }
        if (perSave.Count < 2) return;

        var union = perSave.SelectMany(s => s).ToHashSet();
        // Ten trams are parked at ten distinct stations in each save, yet the union across saves is larger.
        Assert.All(perSave, s => Assert.Equal(10, s.Count));
        Assert.True(union.Count > 10, $"union of occupied stations was {union.Count}");
        Assert.Contains(perSave, s => !s.SetEquals(perSave[0]));
    }
}
