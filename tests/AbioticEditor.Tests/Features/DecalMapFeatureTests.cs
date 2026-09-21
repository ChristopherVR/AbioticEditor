using System.IO;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

public sealed class DecalMapFeatureTests
{
    [Fact]
    public void Cleaned_state_round_trips_without_changing_other_entries()
    {
        var feature = WorldMapFeatures.Find("decals");
        Assert.NotNull(feature);
        Assert.True(WorldMapFeatures.IsKnownMap("DecalMap"));
        var path = Path.Combine(Fixtures.ServerWorldsDir ?? string.Empty, "WorldSave_Facility_Dam.sav");
        if (!File.Exists(path)) return;
        var save = WorldSaveReader.ReadFromFile(path).Raw;
        var before = feature.Read(save);
        Assert.NotEmpty(before);
        var target = before[0];
        var value = target.Fields.Single(f => f.Id == "removed").Value != "true";
        Assert.True(feature.SetField(save, target.Key, "removed", value.ToString()).Changed);
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        var after = feature.Read(SaveGame.LoadFrom(buffer));
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(value ? "true" : "false", after[0].Fields.Single(f => f.Id == "removed").Value);
        Assert.Equal(before.Skip(1).Select(e => (e.Key, e.Fields[0].Value)), after.Skip(1).Select(e => (e.Key, e.Fields[0].Value)));
        Assert.True(feature.SetField(save, target.Key, "removed", "invalid").IsError);
        Assert.True(feature.SetField(save, target.Key, "actor", "invalid").IsError);
        Assert.True(feature.Remove(save, target.Key).IsError);
    }

    [Fact]
    public void Cleaned_creates_the_exact_tag_when_the_default_was_omitted()
    {
        var path = Path.Combine(Fixtures.ServerWorldsDir ?? string.Empty, "WorldSave_Facility_Dam.sav");
        if (!File.Exists(path)) return;
        var save = WorldSaveReader.ReadFromFile(path).Raw;
        var entry = WorldMapAccessor.Entries(save, "DecalMap").First();
        entry.Props.Remove(entry.Props.FindByPrefix("Removed_")!);
        var feature = new DecalMapFeature();
        Assert.True(feature.SetField(save, entry.Key, "removed", "true").Changed);
        Assert.Equal("Removed_30_128506D0489955F65729EEA611C542AC", entry.Props.FindByPrefix("Removed_")!.Name!.Value);
        using var buffer = new MemoryStream();
        save.WriteTo(buffer);
        buffer.Position = 0;
        Assert.Equal("true", feature.Read(SaveGame.LoadFrom(buffer))[0].Fields[0].Value);
    }
}
