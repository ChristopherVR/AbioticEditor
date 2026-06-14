using System.IO;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Tests for <see cref="ButtonMapFeature"/>. Loads the Facility region save (which carries
/// <c>ButtonMap</c>), reads entries, edits the <c>enabled</c> field, and confirms the change
/// both re-reads correctly and survives a save/reload round trip.
///
/// <para>Mirrors <see cref="ElevatorMapFeatureTests"/>; the fixture is optional, and every test
/// skips gracefully when <c>WorldSave_Facility.sav</c> is absent.</para>
/// </summary>
public sealed class ButtonMapFeatureTests
{
    private static string FacilityPath => Path.Combine(Fixtures.CascadeDir ?? string.Empty, "WorldSave_Facility.sav");

    private static SaveGame? LoadFacility()
        => File.Exists(FacilityPath) ? WorldSaveReader.ReadFromFile(FacilityPath).Raw : null;

    [Fact]
    public void Feature_is_discovered_and_applies_to_facility()
    {
        var feature = WorldMapFeatures.Find("buttons");
        Assert.NotNull(feature);
        Assert.Equal("ButtonMap", feature!.MapName);
        Assert.True(WorldMapFeatures.IsKnownMap("ButtonMap"));

        var save = LoadFacility();
        if (save is null)
        {
            return; // fixture absent – skip
        }
        Assert.True(feature.AppliesTo(save));
    }

    [Fact]
    public void Read_returns_entries_with_enabled_field()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("buttons")!;
        var entries = feature.Read(save);

        Assert.NotEmpty(entries);
        var field = entries[0].Fields.Single(f => f.Id == "enabled");
        Assert.Equal(WorldFieldKind.Bool, field.Kind);
        Assert.True(field.Editable);
    }

    [Fact]
    public void Read_exposes_all_expected_fields()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("buttons")!;
        var entries = feature.Read(save);
        Assert.NotEmpty(entries);

        var fields = entries[0].Fields;

        // Read-only display field.
        var buttonId = fields.Single(f => f.Id == "buttonId");
        Assert.Equal(WorldFieldKind.Text, buttonId.Kind);
        Assert.False(buttonId.Editable);

        // Editable bools.
        foreach (var editableId in new[] { "pressedOnce", "enabled", "activated", "noReset" })
        {
            var f = fields.Single(f => f.Id == editableId);
            Assert.Equal(WorldFieldKind.Bool, f.Kind);
            Assert.True(f.Editable);
        }
    }

    [Fact]
    public void SetField_toggles_enabled_and_round_trips()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("buttons")!;
        var entry = feature.Read(save)[0];
        var before = entry.Fields.Single(f => f.Id == "enabled").Value == "true";

        var result = feature.SetField(save, entry.Key, "enabled", (!before).ToString());
        Assert.True(result.Changed);
        Assert.False(result.IsError);

        // Re-read sees the new value.
        var after = feature.Read(save).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "enabled").Value == "true";
        Assert.Equal(!before, after);

        // Survives a save/reload round trip.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        var persisted = feature.Read(reloaded).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "enabled").Value == "true";
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
        var feature = WorldMapFeatures.Find("buttons")!;
        var key = feature.Read(save)[0].Key;

        // Unknown field name.
        Assert.True(feature.SetField(save, key, "nope", "true").IsError);

        // Unknown entry key.
        Assert.True(feature.SetField(save, "no-such-entry", "enabled", "true").IsError);

        // Invalid boolean value.
        Assert.True(feature.SetField(save, key, "enabled", "notabool").IsError);

        // Read-only field (buttonId) is not a valid editable field.
        Assert.True(feature.SetField(save, key, "buttonId", "42").IsError);
    }
}
