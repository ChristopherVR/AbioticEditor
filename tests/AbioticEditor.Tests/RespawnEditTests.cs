using System.IO;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class RespawnEditTests
{
    private readonly ITestOutputHelper _output;

    public RespawnEditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void WorldLevelIndex_FindsLevelGuids_FastScan()
    {
        Assert.NotNull(Fixtures.CascadeDir);

        var levels = WorldLevelIndex.ScanFolder(Fixtures.CascadeDir!);
        foreach (var l in levels) _output.WriteLine($"{l.LevelGuid} = {l.DisplayName}");

        Assert.True(levels.Count >= 5, "the Cascade fixture has many region saves");

        // The fixture players' respawn GUID belongs to Facility_Office1.
        var office1 = levels.FirstOrDefault(l =>
            string.Equals(l.LevelGuid, "EB422B4245ACC9F546C26989FC936F5C", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(office1);
        Assert.Contains("Office1", office1!.FileName);
        Assert.Equal("Facility Office1", office1.DisplayName);
    }

    [Fact]
    public void ApplyRespawn_RoundTrips()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var source = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();

        var data = PlayerSaveReader.ReadFromFile(source);
        var newGuid = "0123456789ABCDEF0123456789ABCDEF";
        PlayerSaveWriter.ApplyRespawn(data, 111.5, -222.25, 333, newGuid);

        var temp = Path.Combine(Path.GetTempPath(), $"respawn-test-{Guid.NewGuid():N}.sav");
        try
        {
            PlayerSaveWriter.WriteToFile(data, temp);
            var reread = PlayerSaveReader.ReadFromFile(temp);

            Assert.Equal(111.5, reread.RespawnX, 3);
            Assert.Equal(-222.25, reread.RespawnY, 3);
            Assert.Equal(333, reread.RespawnZ, 3);
            Assert.Equal(newGuid, reread.RespawnLevelGuid);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
