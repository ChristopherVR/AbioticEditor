using System.IO;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Tests for the Teleporter Pad tag editor. The placed pads live in the (newer) ClientSaved
/// fixture's Facility region save; tests skip gracefully when it's absent. Also unit-tests the
/// tag↔frequency catalog, whose ordering is verified against real save values.
/// </summary>
public sealed class TeleporterPadFeatureTests
{
    private static string? FacilityPath()
    {
        var root = Fixtures.ClientSavedDir;
        if (root is null || !Directory.Exists(root))
        {
            return null;
        }
        // The live world save (not a Backups copy).
        return Directory.EnumerateFiles(root, "WorldSave_Facility.sav", SearchOption.AllDirectories)
            .FirstOrDefault(p => p.Replace('\\', '/').Contains("/Worlds/"));
    }

    private static SaveGame? LoadFacility()
    {
        var path = FacilityPath();
        return path is not null && File.Exists(path) ? WorldSaveReader.ReadFromFile(path).Raw : null;
    }

    // ---------- catalog ----------

    [Fact]
    public void Catalog_has_134_choices_and_verified_mapping()
    {
        // 1 "(none)" + 133 named tags = the wiki's "134 tags".
        Assert.Equal(134, TeleporterTagCatalog.Choices.Count);
        Assert.Equal(TeleporterTagCatalog.None, TeleporterTagCatalog.Choices[0]);
        Assert.Equal(133, TeleporterTagCatalog.MaxFrequency);

        // Spot-checks confirmed against real fixture pad frequencies (all region tags).
        Assert.Equal("Alpha", TeleporterTagCatalog.Label(1));
        Assert.Equal("Facility", TeleporterTagCatalog.Label(27));
        Assert.Equal("The Reactors", TeleporterTagCatalog.Label(34));
        Assert.Equal("Voussoir", TeleporterTagCatalog.Label(133));
        Assert.Equal(TeleporterTagCatalog.None, TeleporterTagCatalog.Label(0));

        Assert.Equal(0, TeleporterTagCatalog.Frequency("(none)"));
        Assert.Equal(27, TeleporterTagCatalog.Frequency("Facility"));
        Assert.Equal(1, TeleporterTagCatalog.Frequency("alpha")); // case-insensitive
        Assert.Null(TeleporterTagCatalog.Frequency("not-a-tag"));
    }

    [Fact]
    public void Catalog_handles_unknown_future_tags_for_forward_compat()
    {
        // A frequency beyond the known list (e.g. a DLC tag) displays as Unknown and round-trips.
        Assert.False(TeleporterTagCatalog.IsKnown(200));
        Assert.Equal("Unknown #200", TeleporterTagCatalog.Label(200));
        Assert.Equal(200, TeleporterTagCatalog.Frequency("Unknown #200"));
        // The older "Tag #N" form is still accepted on the way in.
        Assert.Equal(200, TeleporterTagCatalog.Frequency("Tag #200"));

        // The picker choices grow to include the current unknown value so it stays selectable.
        var choices = TeleporterTagCatalog.ChoicesFor(200);
        Assert.Equal(135, choices.Count);
        Assert.Contains("Unknown #200", choices);
        // A known value uses the plain 134-choice list.
        Assert.Equal(134, TeleporterTagCatalog.ChoicesFor(27).Count);
    }

    // ---------- feature ----------

    [Fact]
    public void Feature_is_discovered_and_applies_to_facility_with_pads()
    {
        var feature = WorldMapFeatures.Find("teleporter-pads");
        Assert.NotNull(feature);
        Assert.Equal("DeployedObjectMap", feature!.MapName);

        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        Assert.True(feature.AppliesTo(save));
    }

    [Fact]
    public void Read_returns_pads_with_tag_and_frequency_fields()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("teleporter-pads")!;
        var pads = feature.Read(save);

        Assert.NotEmpty(pads);
        var entry = pads[0];
        var tag = entry.Fields.Single(f => f.Id == "tag");
        Assert.Equal(WorldFieldKind.Enum, tag.Kind);
        Assert.True(tag.Editable);
        Assert.Equal(134, tag.Options!.Count);

        var freq = entry.Fields.Single(f => f.Id == "frequency");
        Assert.Equal(WorldFieldKind.Integer, freq.Kind);

        // Every tagged pad in this Facility save should resolve to a real (non-"Tag #") label.
        Assert.All(pads, p =>
        {
            var label = p.Fields.Single(f => f.Id == "tag").Value;
            Assert.False(label!.StartsWith("Tag #", StringComparison.Ordinal),
                $"unmapped frequency surfaced as {label}");
        });
    }

    [Fact]
    public void SetField_tag_changes_frequency_and_round_trips()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("teleporter-pads")!;
        var key = feature.Read(save)[0].Key;

        var result = feature.SetField(save, key, "tag", "Alpha");
        Assert.True(result.Changed);
        Assert.False(result.IsError);

        // Re-read: tag is Alpha, frequency is 1.
        var entry = feature.Read(save).Single(e => e.Key == key);
        Assert.Equal("Alpha", entry.Fields.Single(f => f.Id == "tag").Value);
        Assert.Equal("1", entry.Fields.Single(f => f.Id == "frequency").Value);

        // Survives a save/reload round trip.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        var persisted = feature.Read(reloaded).Single(e => e.Key == key);
        Assert.Equal("Alpha", persisted.Fields.Single(f => f.Id == "tag").Value);
    }

    [Fact]
    public void SetField_frequency_sets_named_tag()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("teleporter-pads")!;
        var key = feature.Read(save)[0].Key;

        Assert.True(feature.SetField(save, key, "frequency", "34").Changed);
        var entry = feature.Read(save).Single(e => e.Key == key);
        Assert.Equal("The Reactors", entry.Fields.Single(f => f.Id == "tag").Value);
    }

    [Fact]
    public void SetField_rejects_bad_tag_frequency_field_and_entry()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("teleporter-pads")!;
        var key = feature.Read(save)[0].Key;

        Assert.True(feature.SetField(save, key, "tag", "Nonexistent Tag").IsError);
        Assert.True(feature.SetField(save, key, "frequency", "-1").IsError); // negative
        Assert.True(feature.SetField(save, key, "frequency", "notanint").IsError);
        Assert.True(feature.SetField(save, key, "nope", "1").IsError);
        Assert.True(feature.SetField(save, "no-such-pad", "tag", "Alpha").IsError);
    }

    [Fact]
    public void SetField_accepts_future_frequency_and_shows_it_as_unknown()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("teleporter-pads")!;
        var key = feature.Read(save)[0].Key;

        // A value beyond the known 133 (a hypothetical DLC tag) is allowed, not rejected.
        var result = feature.SetField(save, key, "frequency", "200");
        Assert.True(result.Changed);
        Assert.False(result.IsError);

        var entry = feature.Read(save).Single(e => e.Key == key);
        var tag = entry.Fields.Single(f => f.Id == "tag");
        Assert.Equal("Unknown #200", tag.Value);
        Assert.Contains("Unknown #200", tag.Options!); // selectable in the picker
        Assert.Equal("200", entry.Fields.Single(f => f.Id == "frequency").Value);
    }
}
