using System.IO;
using System.Linq;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests;

/// <summary>
/// Unit + fixture tests for the shared world-editing foundations: the area-name catalog, the
/// soft-object-path setter (tram last-station), and the bench-upgrade gameplay-tag read/write.
/// Fixture tests use <c>WorldSave_Facility.sav</c> (the only save carrying trams and the main
/// base) and skip gracefully when the Cascade fixture is absent.
/// </summary>
public sealed class WorldFoundationTests
{
    private static SaveGame? LoadFacility()
    {
        var dir = Fixtures.CascadeDir;
        if (dir is null)
        {
            return null;
        }
        var path = Path.Combine(dir, "WorldSave_Facility.sav");
        return File.Exists(path) ? WorldSaveReader.ReadFromFile(path).Raw : null;
    }

    // ---------- WorldAreaCatalog ----------

    [Theory]
    [InlineData("Facility", "The Facility")]
    [InlineData("Facility_Office1", "The Office Sector")]
    [InlineData("Facility_MFWest", "Manufacturing West")]
    [InlineData("Facility_MFMines", "The Mines")]
    [InlineData("Facility_Labs_Control", "Cascade Laboratories")]
    [InlineData("Facility_Security", "Security Sector")]
    [InlineData("Facility_Dam_Hydroplant", "The Dam")]
    public void AreaCatalog_maps_known_tokens(string token, string expected)
        => Assert.Equal(expected, WorldAreaCatalog.FriendlyName(token));

    [Fact]
    public void AreaCatalog_prettifies_unknown_tokens()
    {
        Assert.Equal("DistantShore", WorldAreaCatalog.FriendlyName("V_DistantShore"));
        Assert.Equal("Cabin", WorldAreaCatalog.FriendlyName("H_Cabin"));
        Assert.Null(WorldAreaCatalog.FriendlyName(""));
        Assert.Null(WorldAreaCatalog.FriendlyName(null));
    }

    [Fact]
    public void AreaCatalog_parses_save_file_and_actor_path_tokens()
    {
        Assert.Equal("Facility_Office1", WorldAreaCatalog.TokenFromSaveFile("WorldSave_Facility_Office1.sav"));
        Assert.Equal("The Office Sector", WorldAreaCatalog.FriendlyNameFromSaveFile("WorldSave_Facility_Office1.sav"));
        Assert.Null(WorldAreaCatalog.TokenFromSaveFile("Player_123.sav"));

        Assert.Equal("Facility_MFWest", WorldAreaCatalog.TokenFromActorPath(
            "/Game/Maps/Facility_MFWest.Facility_MFWest:PersistentLevel.VehicleSpawn_Forklift_C_3"));
        Assert.Equal("Facility", WorldAreaCatalog.TokenFromActorPath(
            "/Game/Maps/Facility.Facility:PersistentLevel.Tram_ParentBP_C_0"));
        Assert.Null(WorldAreaCatalog.TokenFromActorPath("not-a-path"));
    }

    // ---------- tram last-station (soft-object path setter) ----------

    [Fact]
    public void SetSoftObjectSubPath_round_trips_a_tram_last_station()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return; // fixture absent
        }

        var tram = WorldMapAccessor.Entries(save, "TramMap").FirstOrDefault();
        Assert.NotNull(tram.Props); // Facility always has trams

        var before = WorldMapAccessor.GetSoftObjectPath(tram.Props, "LastStation_");
        Assert.NotNull(before);
        // PackageName/AssetName are the constant Facility map; the station id lives in SubPath.
        Assert.Equal("/Game/Maps/Facility", before!.Value.Package);

        const string newStation = "PersistentLevel.TramSystem_Station_C_99";
        Assert.True(WorldMapAccessor.SetSoftObjectSubPath(tram.Props, "LastStation_", newStation));

        var after = WorldMapAccessor.GetSoftObjectPath(tram.Props, "LastStation_");
        Assert.Equal(newStation, after!.Value.SubPath);
        // The package/asset components are left untouched.
        Assert.Equal(before.Value.Package, after.Value.Package);
        Assert.Equal(before.Value.Asset, after.Value.Asset);
    }

    [Fact]
    public void SetSoftObjectSubPath_returns_false_for_absent_leaf()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var tram = WorldMapAccessor.Entries(save, "TramMap").First();
        Assert.False(WorldMapAccessor.SetSoftObjectSubPath(tram.Props, "NoSuchLeaf_", "x"));
    }

    // ---------- bench upgrades ----------

    // ---------- power socket device resolution ----------

    [Fact]
    public void PowerSocketDeviceResolver_resolves_devices_and_links_containers()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }

        var index = PowerSocketDeviceResolver.BuildIndex(save);
        Assert.NotEmpty(index); // the Facility save has deployables

        var feature = (PowerSocketMapFeature)WorldMapFeatures.Find("power-sockets")!;
        var entries = feature.Read(save);
        if (entries.Count == 0)
        {
            return; // this save carries no power sockets
        }

        // Every socket's plugged-in-device field is a human description, never a bare 32-hex GUID.
        foreach (var e in entries)
        {
            var device = e.Fields.Single(f => f.Id == "pluggedInDevice").Value;
            Assert.False(string.IsNullOrEmpty(device));
            Assert.False(device!.Length == 32 && device.All(Uri.IsHexDigit),
                $"plugged-in device should be described, not a raw GUID: {device}");
        }

        // A link either opens a container in THIS save, or is a cross-world candidate whose device
        // id is not in this save's index (the host resolves it folder-wide on click). Either way it
        // carries a label, and a same-save link only targets a container (never a cable/battery).
        foreach (var e in entries.Where(e => e.LinkTargetId is not null))
        {
            Assert.False(string.IsNullOrWhiteSpace(e.LinkLabel));
            if (index.TryGetValue(e.LinkTargetId!, out var info))
            {
                Assert.True(info.IsContainer, "a same-save link must target a container");
            }
        }
    }

    [Fact]
    public void PowerSocketDeviceResolver_friendly_names_and_sentinels()
    {
        Assert.True(PowerSocketDeviceResolver.IsNothingPlugged("-1"));
        Assert.True(PowerSocketDeviceResolver.IsNothingPlugged(""));
        Assert.True(PowerSocketDeviceResolver.IsNothingPlugged(null));
        Assert.False(PowerSocketDeviceResolver.IsNothingPlugged("ABCD1234"));

        Assert.Equal("Plug Board", PowerSocketDeviceResolver.FriendlyName("Deployed_Plugboard_C"));
        Assert.Equal("Crafting Bench", PowerSocketDeviceResolver.FriendlyName("Deployed_CraftingBench_Default_C"));
        Assert.Equal("Battery", PowerSocketDeviceResolver.FriendlyName("Deployed_Battery_T2_C"));
    }

    [Theory]
    [InlineData("/Game/Maps/Facility.Facility:PersistentLevel.Resource_Micronode_LeyakEssence_C_2147263889", "Leyak Essence")]
    [InlineData("/Game/Maps/Facility_Office2.Facility_Office2:PersistentLevel.ResourceNode_WoodCrate_Office_C_2", "Wood Crate Office")]
    [InlineData("/Game/Maps/X.X:PersistentLevel.Resource_MicroNode_FireExtinguisher_C_4", "Fire Extinguisher")]
    [InlineData("/Game/Maps/X.X:PersistentLevel.ResourceNode_GlassPane_C_37", "Glass Pane")]
    public void ResourceNodeNaming_makes_friendly_type_names(string key, string expected)
        => Assert.Equal(expected, ResourceNodeNaming.FriendlyType(key));

    [Fact]
    public void BenchUpgradeCatalog_has_known_rows()
    {
        Assert.Equal(11, BenchUpgradeCatalog.All.Count);
        Assert.Equal("Tougher Bench", BenchUpgradeCatalog.DisplayName("TougherBench"));
        Assert.Equal("BenchUpgrade.TougherBench", BenchUpgradeCatalog.All.First(u => u.Row == "TougherBench").Tag);
        Assert.Equal("ItemTransporter", BenchUpgradeCatalog.RowFromTag("BenchUpgrade.ItemTransporter"));
        Assert.Equal("ItemTransporter", BenchUpgradeCatalog.RowFromTag("ItemTransporter"));
    }

    [Fact]
    public void Reader_surfaces_installed_bench_upgrades()
    {
        var dir = Fixtures.CascadeDir;
        if (dir is null)
        {
            return;
        }
        var data = WorldSaveReader.ReadFromFile(Path.Combine(dir, "WorldSave_Facility.sav"));
        var upgradedBench = data.Deployables.FirstOrDefault(d => d.InstalledUpgrades.Count > 0);
        // The Cascade base has several upgraded benches.
        Assert.NotNull(upgradedBench);
        Assert.All(upgradedBench!.InstalledUpgrades, row => Assert.False(string.IsNullOrWhiteSpace(row)));
    }

    [Fact]
    public void SetInstalled_adds_and_removes_a_bench_upgrade()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }

        // Find a deployable that carries a gameplay-tag container (a bench).
        WorldMapEntryProps bench = default;
        foreach (var entry in WorldMapAccessor.Entries(save, "DeployedObjectMap"))
        {
            if (BenchUpgradeCatalog.SupportsUpgrades(entry.Props))
            {
                bench = entry;
                break;
            }
        }
        Assert.NotNull(bench.Props);

        const string row = "BenchTurret";
        var hadIt = BenchUpgradeCatalog.ReadInstalledRows(bench.Props)
            .Contains(row, System.StringComparer.OrdinalIgnoreCase);

        // Toggle to the opposite, assert it took, then restore.
        Assert.True(BenchUpgradeCatalog.SetInstalled(bench.Props, row, !hadIt));
        Assert.Equal(!hadIt, BenchUpgradeCatalog.ReadInstalledRows(bench.Props)
            .Contains(row, System.StringComparer.OrdinalIgnoreCase));

        Assert.True(BenchUpgradeCatalog.SetInstalled(bench.Props, row, hadIt));
        Assert.Equal(hadIt, BenchUpgradeCatalog.ReadInstalledRows(bench.Props)
            .Contains(row, System.StringComparer.OrdinalIgnoreCase));

        // Setting to the current value is a no-op.
        Assert.False(BenchUpgradeCatalog.SetInstalled(bench.Props, row, hadIt));
    }
}
