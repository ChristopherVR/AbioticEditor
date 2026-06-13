using System.IO;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

public class StoryProgressionTests
{
    private static string? MetadataSave()
    {
        if (Fixtures.CascadeDir is null) return null;
        var path = Path.Combine(Fixtures.CascadeDir, "WorldSave_MetaData.sav");
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void Reads_StoryProgressionAndPlaytime()
    {
        var path = MetadataSave();
        Assert.NotNull(path);

        var data = WorldSaveReader.ReadFromFile(path!);
        Assert.Equal("Voussoir", data.StoryProgressionRow);
        Assert.True(data.MinutesPassed > 0);
        Assert.True(StoryProgressionCatalog.IndexOf(data.StoryProgressionRow) >= 0);
    }

    [Fact]
    public void RegionSaves_HaveNoStoryProgression()
    {
        if (Fixtures.CascadeDir is null) return;
        var path = Path.Combine(Fixtures.CascadeDir, "WorldSave_H_Cabin.sav");
        if (!File.Exists(path)) return;

        var data = WorldSaveReader.ReadFromFile(path);
        Assert.Null(data.StoryProgressionRow);
        Assert.Null(data.MinutesPassed);
    }

    [Fact]
    public void ApplyStoryProgression_RoundTripsThroughSerializer()
    {
        var path = MetadataSave();
        Assert.NotNull(path);

        var data = WorldSaveReader.ReadFromFile(path!);
        WorldSaveWriter.ApplyStoryProgression(data, "EndGame");
        WorldSaveWriter.ApplyMinutesPassed(data, 123);

        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        ms.Position = 0;
        var reloaded = WorldSaveReader.ReadFromStream(ms);

        Assert.Equal("EndGame", reloaded.StoryProgressionRow);
        Assert.Equal(123, reloaded.MinutesPassed);
    }
}
