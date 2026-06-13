using System.IO;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Tests for <see cref="PowerSocketMapFeature"/>. Mirrors the <see cref="ElevatorMapFeatureTests"/>
/// shape: loads <c>WorldSave_Facility.sav</c> (which carries <c>PowerSocketMap</c>), reads entries,
/// edits the <c>hasTimer</c> field, and verifies the change round-trips through a save/reload cycle.
/// All tests skip gracefully when the fixture file is absent.
/// </summary>
public sealed class PowerSocketMapFeatureTests
{
    private static string FacilityPath => Path.Combine(Fixtures.CascadeDir ?? string.Empty, "WorldSave_Facility.sav");

    private static SaveGame? LoadFacility()
        => File.Exists(FacilityPath) ? WorldSaveReader.ReadFromFile(FacilityPath).Raw : null;

    [Fact]
    public void Feature_is_discovered_and_applies_to_facility()
    {
        var feature = WorldMapFeatures.Find("power-sockets");
        Assert.NotNull(feature);
        Assert.Equal("PowerSocketMap", feature!.MapName);
        Assert.True(WorldMapFeatures.IsKnownMap("PowerSocketMap"));

        var save = LoadFacility();
        if (save is null)
        {
            return; // fixture absent - skip
        }
        Assert.True(feature.AppliesTo(save));
    }

    [Fact]
    public void Read_returns_entries_with_expected_fields()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("power-sockets")!;
        var entries = feature.Read(save);

        Assert.NotEmpty(entries);

        var entry = entries[0];

        // socketId: read-only text
        var socketId = entry.Fields.Single(f => f.Id == "socketId");
        Assert.Equal(WorldFieldKind.Text, socketId.Kind);
        Assert.False(socketId.Editable);
        Assert.NotNull(socketId.Value);

        // pluggedInDevice: read-only text
        var pluggedIn = entry.Fields.Single(f => f.Id == "pluggedInDevice");
        Assert.Equal(WorldFieldKind.Text, pluggedIn.Kind);
        Assert.False(pluggedIn.Editable);

        // hasTimer: editable bool
        var hasTimer = entry.Fields.Single(f => f.Id == "hasTimer");
        Assert.Equal(WorldFieldKind.Bool, hasTimer.Kind);
        Assert.True(hasTimer.Editable);

        // timerMode: read-only (full enumerator set unknown)
        var timerMode = entry.Fields.Single(f => f.Id == "timerMode");
        Assert.Equal(WorldFieldKind.Text, timerMode.Kind);
        Assert.False(timerMode.Editable);

        // extraDevices: read-only integer-as-text count
        var extraDevices = entry.Fields.Single(f => f.Id == "extraDevices");
        Assert.Equal(WorldFieldKind.Text, extraDevices.Kind);
        Assert.False(extraDevices.Editable);
        Assert.True(int.TryParse(extraDevices.Value, out _));
    }

    [Fact]
    public void SetField_toggles_hasTimer_and_round_trips()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("power-sockets")!;
        var entry = feature.Read(save)[0];
        var before = entry.Fields.Single(f => f.Id == "hasTimer").Value == "true";

        var result = feature.SetField(save, entry.Key, "hasTimer", (!before).ToString());
        Assert.True(result.Changed);
        Assert.False(result.IsError);

        // Re-read sees the new value.
        var after = feature.Read(save).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "hasTimer").Value == "true";
        Assert.Equal(!before, after);

        // Survives a save/reload round trip.
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var reloaded = SaveGame.LoadFrom(buffer);
        var persisted = feature.Read(reloaded).Single(e => e.Key == entry.Key)
            .Fields.Single(f => f.Id == "hasTimer").Value == "true";
        Assert.Equal(!before, persisted);
    }

    [Fact]
    public void SetField_rejects_unknown_field_and_entry_and_invalid_value()
    {
        var save = LoadFacility();
        if (save is null)
        {
            return;
        }
        var feature = WorldMapFeatures.Find("power-sockets")!;
        var key = feature.Read(save)[0].Key;

        // Unknown field
        Assert.True(feature.SetField(save, key, "nope", "true").IsError);

        // Read-only field
        Assert.True(feature.SetField(save, key, "socketId", "whatever").IsError);
        Assert.True(feature.SetField(save, key, "timerMode", "E_PowerTimerModes::NewEnumerator0").IsError);

        // Non-existent entry
        Assert.True(feature.SetField(save, "no-such-entry", "hasTimer", "true").IsError);

        // Invalid bool value
        Assert.True(feature.SetField(save, key, "hasTimer", "notabool").IsError);
    }
}
