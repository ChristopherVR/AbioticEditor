using System.IO;
using System.Linq;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Tests for <see cref="ServerEntitlementsFeature"/>. Loads the metadata save (which carries
/// <c>ServerEntitlements</c>), reads an entry's per-entitlement toggles, flips one, and confirms
/// the change re-reads and survives a save/reload round trip.
/// </summary>
public sealed class ServerEntitlementsFeatureTests
{
    private static string MetaDataPath => Path.Combine(Fixtures.CascadeDir ?? string.Empty, "WorldSave_MetaData.sav");

    private static SaveGame? LoadMetaData()
        => File.Exists(MetaDataPath) ? WorldSaveReader.ReadFromFile(MetaDataPath).Raw : null;

    [Fact]
    public void Feature_is_discovered_and_applies_to_metadata()
    {
        var feature = WorldMapFeatures.Find("server-entitlements");
        Assert.NotNull(feature);
        Assert.Equal("ServerEntitlements", feature!.MapName);
        Assert.True(WorldMapFeatures.IsKnownMap("ServerEntitlements"));

        var save = LoadMetaData();
        if (save is null)
        {
            return; // fixture absent - skip
        }
        Assert.True(feature.AppliesTo(save));
    }

    [Fact]
    public void Read_exposes_a_toggle_per_known_entitlement()
    {
        var save = LoadMetaData();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("server-entitlements")!;
        var entries = feature.Read(save);

        Assert.NotEmpty(entries);
        var fields = entries[0].Fields;
        // The known entitlements each surface as an editable boolean toggle.
        foreach (var id in new[] { "EarlyAccess", "SupportersEdition" })
        {
            var f = fields.Single(x => x.Id == id);
            Assert.Equal(WorldFieldKind.Bool, f.Kind);
            Assert.True(f.Editable);
        }
    }

    [Fact]
    public void SetField_toggles_an_entitlement_and_round_trips()
    {
        var save = LoadMetaData();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("server-entitlements")!;
        var entry = feature.Read(save)[0];

        bool Held(SaveGame s) => string.Equals(
            feature.Read(s).Single(e => e.Key == entry.Key).Fields.Single(f => f.Id == "EarlyAccess").Value,
            "true", StringComparison.OrdinalIgnoreCase);

        var before = Held(save);
        var result = feature.SetField(save, entry.Key, "EarlyAccess", (!before).ToString());
        Assert.True(result.Changed);
        Assert.False(result.IsError);
        Assert.Equal(!before, Held(save));

        // Survives a save/reload round trip.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        Assert.Equal(!before, Held(reloaded));
    }

    [Fact]
    public void SetField_same_value_reports_no_change()
    {
        var save = LoadMetaData();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("server-entitlements")!;
        var entry = feature.Read(save)[0];
        var current = entry.Fields.Single(f => f.Id == "EarlyAccess").Value;

        var again = feature.SetField(save, entry.Key, "EarlyAccess", current);
        Assert.False(again.Changed);
        Assert.False(again.IsError);
    }

    [Fact]
    public void SetField_rejects_non_boolean_value()
    {
        var save = LoadMetaData();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("server-entitlements")!;
        var key = feature.Read(save)[0].Key;

        Assert.True(feature.SetField(save, key, "EarlyAccess", "not-a-bool").IsError);
    }
}
