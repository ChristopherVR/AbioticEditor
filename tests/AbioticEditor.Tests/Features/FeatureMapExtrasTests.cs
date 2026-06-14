using System.IO;
using System.Linq;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Covers the cross-feature additions: numbered display names (no raw GUID labels), the wiki
/// image catalog, teleporter pad link summaries, and entry removal (a new write path). Tests that
/// need a region save use the ClientSaved Facility fixture and skip gracefully when it's absent.
/// </summary>
public sealed class FeatureMapExtrasTests
{
    private static SaveGame? LoadFacility()
    {
        var root = Fixtures.ClientSavedDir;
        if (root is null || !Directory.Exists(root))
        {
            return null;
        }
        var path = Directory.EnumerateFiles(root, "WorldSave_Facility.sav", SearchOption.AllDirectories)
            .FirstOrDefault(p => p.Replace('\\', '/').Contains("/Worlds/"));
        return path is not null && File.Exists(path) ? WorldSaveReader.ReadFromFile(path).Raw : null;
    }

    // ---------- display names ----------

    [Fact]
    public void Portal_feature_renamed_to_world_teleporters()
    {
        var portals = WorldMapFeatures.Find("portals");
        Assert.NotNull(portals);
        Assert.Equal("World Teleporters", portals!.DisplayName);
    }

    [Fact]
    public void Power_sockets_get_numbered_labels_not_guids()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("power-sockets")!;
        var entries = feature.Read(save);
        if (entries.Count == 0)
        {
            return; // this region carries no sockets
        }
        // The label is a friendly "Power Socket N", never the raw GUID key.
        Assert.StartsWith("Power Socket 1", entries[0].Label, System.StringComparison.Ordinal);
        Assert.All(entries, e => Assert.DoesNotContain(e.Key, e.Label));
    }

    [Fact]
    public void Teleporter_pads_expose_a_link_summary_field()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("teleporter-pads")!;
        var pads = feature.Read(save);
        if (pads.Count == 0)
        {
            return;
        }
        var linked = pads[0].Fields.Single(f => f.Id == "linkedWith");
        Assert.False(linked.Editable);
        Assert.False(string.IsNullOrWhiteSpace(linked.Value));
        Assert.StartsWith("Teleporter Pad 1", pads[0].Label, System.StringComparison.Ordinal);
    }

    // ---------- wiki image catalog ----------

    [Fact]
    public void Image_catalog_only_maps_structures_the_wiki_actually_pictures()
    {
        // The tram is the one fixed world-state structure with a real wiki render.
        var trams = FeatureWikiImageCatalog.CandidatesFor("trams");
        Assert.Single(trams);
        Assert.Equal("Vehicle_-_Tram.png", trams[0]);

        // The wiki does not picture these structures (item icons/tooltips/invisible volumes are
        // not the placed actor), so they map to nothing - the UI shows an honest "no image" note.
        Assert.Empty(FeatureWikiImageCatalog.CandidatesFor("teleporter-pads"));
        Assert.Empty(FeatureWikiImageCatalog.CandidatesFor("portals"));
        Assert.Empty(FeatureWikiImageCatalog.CandidatesFor("power-sockets"));
        Assert.Empty(FeatureWikiImageCatalog.CandidatesFor("elevators"));
        Assert.Empty(FeatureWikiImageCatalog.CandidatesFor("buttons"));
        Assert.Empty(FeatureWikiImageCatalog.CandidatesFor("triggers"));
    }

    // ---------- removal ----------

    [Fact]
    public void Removable_flag_matches_expectation_per_feature()
    {
        // Simple level-actor maps are removable; shared/metadata maps are not.
        Assert.True(WorldMapFeatures.Find("power-sockets")!.SupportsRemoval);
        Assert.True(WorldMapFeatures.Find("elevators")!.SupportsRemoval);
        Assert.True(WorldMapFeatures.Find("buttons")!.SupportsRemoval);
        Assert.False(WorldMapFeatures.Find("teleporter-pads")!.SupportsRemoval);
        Assert.False(WorldMapFeatures.Find("server-entitlements")!.SupportsRemoval);
    }

    [Fact]
    public void Remove_drops_a_power_socket_entry_and_round_trips()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("power-sockets")!;
        var before = feature.Read(save);
        if (before.Count == 0)
        {
            return;
        }
        var key = before[0].Key;

        var result = feature.Remove(save, key);
        Assert.True(result.Changed);
        Assert.False(result.IsError);

        var after = feature.Read(save);
        Assert.Equal(before.Count - 1, after.Count);
        Assert.DoesNotContain(after, e => e.Key == key);

        // The removal survives a save/reload round trip.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        var persisted = feature.Read(reloaded);
        Assert.Equal(after.Count, persisted.Count);
        Assert.DoesNotContain(persisted, e => e.Key == key);
    }

    [Fact]
    public void Remove_rejects_unknown_key_and_unsupported_feature()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        Assert.True(WorldMapFeatures.Find("power-sockets")!.Remove(save, "no-such-key").IsError);
        // Teleporter pads opt out of removal entirely.
        var pads = WorldMapFeatures.Find("teleporter-pads")!;
        var padList = pads.Read(save);
        if (padList.Count > 0)
        {
            Assert.True(pads.Remove(save, padList[0].Key).IsError);
        }
    }
}
