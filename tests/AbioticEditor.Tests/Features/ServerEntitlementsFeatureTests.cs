using System.IO;
using System.Linq;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Tests for <see cref="ServerEntitlementsFeature"/>. Loads the metadata save (which carries
/// <c>ServerEntitlements</c>), reads the single entry, edits the comma-separated entitlements
/// field, and confirms the change re-reads and survives a save/reload round trip. Mirrors the
/// <see cref="ElevatorMapFeatureTests"/> shape but against the metadata fixture.
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
    public void Read_returns_entries_with_editable_entitlements_field()
    {
        var save = LoadMetaData();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("server-entitlements")!;
        var entries = feature.Read(save);

        Assert.NotEmpty(entries);
        var field = entries[0].Fields.Single(f => f.Id == "entitlements");
        Assert.Equal(WorldFieldKind.Text, field.Kind);
        Assert.True(field.Editable);
    }

    [Fact]
    public void SetField_replaces_entitlements_and_round_trips()
    {
        var save = LoadMetaData();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("server-entitlements")!;
        var entry = feature.Read(save)[0];

        var result = feature.SetField(save, entry.Key, "entitlements", "alpha, beta");
        Assert.True(result.Changed);
        Assert.False(result.IsError);

        // Re-read sees the new value.
        var after = feature.Read(save).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "entitlements").Value;
        Assert.Equal("alpha, beta", after);

        // Survives a save/reload round trip.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        var persisted = feature.Read(reloaded).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "entitlements").Value;
        Assert.Equal("alpha, beta", persisted);
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
        var key = feature.Read(save)[0].Key;

        feature.SetField(save, key, "entitlements", "alpha, beta");
        var again = feature.SetField(save, key, "entitlements", "alpha, beta");
        Assert.False(again.Changed);
        Assert.False(again.IsError);
    }

    [Fact]
    public void SetField_rejects_unknown_field()
    {
        var save = LoadMetaData();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("server-entitlements")!;
        var key = feature.Read(save)[0].Key;

        Assert.True(feature.SetField(save, key, "nope", "x").IsError);
    }
}
